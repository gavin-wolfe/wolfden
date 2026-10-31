using System.Diagnostics;
using Wolfden.Index.Reading;

namespace Wolfden.Index.Writing;

/// <summary>
/// The den's write lock, <c>.wolfden/write.lock</c> (#221). Every write
/// the write tools make holds it from the moment it reads the files it's
/// based on until the last file is written (or put back), so two Wolfdens
/// on one den, such as <c>wolfden serve</c> and <c>wolfden new</c>, or an
/// MCP session, take turns: neither can check a file's hash, and then save
/// over the other's change made just after.
/// </summary>
/// <remarks>
/// <para>
/// The lock is the file held open with no sharing (<see cref="FileShare.None"/>),
/// as the watch lock is (<see cref="DenWatcher.LockFileName"/>), not a file
/// whose presence means "locked". The OS lets go of it when the process
/// ends, however it ends, so a crashed Wolfden never leaves a stale lock
/// and nothing has to guess whether a process id is still alive. The file
/// itself stays, empty; its presence means nothing. On Linux and macOS
/// .NET takes the no-sharing open as an advisory <c>flock</c>, which every
/// Wolfden honours, and which a den on a network share may not support.
/// </para>
/// <para>
/// One lock for the whole den, not one per file: a rename's link rewrites
/// and a move touch files across the den, and a single lock can't deadlock.
/// Writes are short, so the turns are too. It's in <c>.wolfden/</c>, which
/// is hidden (F-LAY-2), so the watcher, checkpoints, and the den's history
/// ignore it.
/// </para>
/// <para>
/// It keeps out other Wolfdens only. An editor saving a note doesn't know
/// about it, so for those the hash check just before the write is still
/// all there is (design.md §8, "Stale files").
/// </para>
/// <para>
/// <c>wolfden check --fix</c>, which adds missing ids without the write
/// tools, takes it too.
/// </para>
/// </remarks>
public sealed class DenWriteLock : IDisposable
{
    /// <summary>The lock file's name in <c>.wolfden/</c>.</summary>
    public const string FileName = "write.lock";

    /// <summary>How long a write waits for another Wolfden's write to finish before it gives up with <see cref="ToolErrorCodes.DenBusy"/>.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FirstPause = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan LongestPause = TimeSpan.FromMilliseconds(100);

    private readonly FileStream _file;

    private DenWriteLock(FileStream file) => _file = file;

    /// <summary>The lock file of the den at <paramref name="root"/>, a full path.</summary>
    public static string PathFor(string root) => Path.Combine(root, IndexBuilder.StateFolder, FileName);

    /// <summary>
    /// Takes the lock of the den at <paramref name="root"/>, waiting up to
    /// <paramref name="wait"/> while another Wolfden, in this process or
    /// another, holds it. <paramref name="waiting"/> is called once, with
    /// <paramref name="wait"/>, when the first try finds it held. Cancelling
    /// <paramref name="cancellationToken"/> ends the wait straight away (#234).
    /// </summary>
    /// <exception cref="DenToolException">
    /// <see cref="ToolErrorCodes.DenBusy"/> if it's still held after <paramref name="wait"/>;
    /// <see cref="ToolErrorCodes.WriteFailed"/> if the lock file can't be made or opened at all.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the lock was taken.</exception>
    public static DenWriteLock Take(string root, TimeSpan wait, Action<TimeSpan>? waiting = null, CancellationToken cancellationToken = default) =>
        Take(root, wait, Stopwatch.StartNew(), waiting, cancellationToken);

    /// <summary>
    /// <see cref="Take(string, TimeSpan, Action{TimeSpan}?, CancellationToken)"/>, with the wait counted
    /// on <paramref name="clock"/>, so time a write already spent waiting
    /// for its turn in this session counts against the same <paramref name="wait"/>.
    /// </summary>
    internal static DenWriteLock Take(string root, TimeSpan wait, Stopwatch clock, Action<TimeSpan>? waiting, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = Path.Combine(root, IndexBuilder.StateFolder);
        var path = Path.Combine(folder, FileName);
        try
        {
            if (File.Exists(folder))
            {
                throw new IOException($"'{folder}' is a file, but Wolfden keeps its index and write lock in a folder by that name. Move or rename the file, then try again.");
            }

            Directory.CreateDirectory(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new DenToolException(ToolErrorCodes.WriteFailed, $"Couldn't make the den's write lock, so nothing was written: {e.Message}");
        }

        var pause = FirstPause;
        var told = false;
        try
        {
            while (true)
            {
                try
                {
                    return new DenWriteLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                }
                catch (DirectoryNotFoundException)
                {
                    // The folder was deleted (by hand, or a reset index) between making it and opening the file.
                    Directory.CreateDirectory(folder);
                }
                catch (IOException e) when (IsHeld(e))
                {
                    // Held by another Wolfden, most likely; a virus scanner or indexer
                    // opening it for a moment looks the same, and passes.
                    if (clock.Elapsed >= wait)
                    {
                        throw Busy(wait);
                    }

                    if (!told)
                    {
                        told = true;
                        Waiters.AddOrUpdate(path, 1, (_, n) => n + 1);
                        waiting?.Invoke(wait);
                    }

                    // Waits on the token, so a cancelled call (a dropped request, an MCP
                    // cancel) stops waiting at once instead of at the end of the pause.
                    var left = wait - clock.Elapsed;
                    if (cancellationToken.WaitHandle.WaitOne(pause < left ? pause : left > TimeSpan.Zero ? left : TimeSpan.Zero))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    pause = pause * 2 > LongestPause ? LongestPause : pause * 2;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Not another holder: waiting won't help.
                    throw new DenToolException(ToolErrorCodes.WriteFailed, $"Couldn't open the den's write lock, {path}, so nothing was written: {e.Message}");
                }
            }
        }
        finally
        {
            if (told)
            {
                Waiters.AddOrUpdate(path, 0, (_, n) => n - 1);
            }
        }
    }

    // For tests: how many takes are waiting for each lock file now.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Waiters = new(StringComparer.Ordinal);

    /// <summary>
    /// For tests: how many takes of the lock of the den at <paramref name="root"/>
    /// are waiting for it right now, in this process. A test polls it to
    /// know a write is waiting, or has stopped waiting, rather than pausing.
    /// </summary>
    internal static int WaitersFor(string root) => Waiters.TryGetValue(PathFor(Path.GetFullPath(root)), out var n) ? n : 0;

    /// <summary>The <see cref="ToolErrorCodes.DenBusy"/> error for a write that waited all of <paramref name="wait"/>.</summary>
    internal static DenToolException Busy(TimeSpan wait) => new(
        ToolErrorCodes.DenBusy,
        $"Other writes to the den, or a checkpoint, kept it busy for {Seconds(wait)}, so nothing was written. They may be another Wolfden's " +
        "(wolfden serve, wolfden mcp, or a command) or this one's; a checkpoint can wait on git, such as a signing passphrase. Try again in a moment.");

    /// <summary>
    /// True if opening the file failed because another handle holds it:
    /// a sharing or lock violation on Windows (ERROR_SHARING_VIOLATION,
    /// ERROR_LOCK_VIOLATION), or, on Linux and macOS, the <c>flock</c> that
    /// .NET takes for <see cref="FileShare.None"/> finding it taken
    /// (<c>EWOULDBLOCK</c>, which .NET gives as the exception's HResult).
    /// </summary>
    internal static bool IsHeld(IOException e)
    {
        if (OperatingSystem.IsWindows())
        {
            return (e.HResult & 0xFFFF) is 32 or 33;
        }

        // EWOULDBLOCK (= EAGAIN) is 11 on Linux, 35 on macOS and the BSDs.
        return e.HResult == (OperatingSystem.IsLinux() ? 11 : 35);
    }

    /// <summary>Lets go of the lock.</summary>
    public void Dispose() => _file.Dispose();

    private static string Seconds(TimeSpan wait) =>
        wait.TotalSeconds == 1 ? "1 second" : $"{wait.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} seconds";
}

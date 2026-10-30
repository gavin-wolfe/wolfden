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
    /// another, holds it. <paramref name="waiting"/> is called once, when
    /// the first try finds it held.
    /// </summary>
    /// <exception cref="DenToolException">
    /// <see cref="ToolErrorCodes.DenBusy"/> if it's still held after <paramref name="wait"/>;
    /// <see cref="ToolErrorCodes.WriteFailed"/> if the lock file can't be made at all.
    /// </exception>
    public static DenWriteLock Take(string root, TimeSpan wait, Action? waiting = null)
    {
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

        var clock = Stopwatch.StartNew();
        var pause = FirstPause;
        var told = false;
        while (true)
        {
            try
            {
                return new DenWriteLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (UnauthorizedAccessException e)
            {
                throw new DenToolException(ToolErrorCodes.WriteFailed, $"Couldn't open the den's write lock, {path}, so nothing was written: {e.Message}");
            }
            catch (IOException e) when (e is not (DirectoryNotFoundException or PathTooLongException))
            {
                // Held by another Wolfden, most likely; a virus scanner or indexer
                // opening it for a moment looks the same, and passes.
                if (clock.Elapsed >= wait)
                {
                    throw new DenToolException(
                        ToolErrorCodes.DenBusy,
                        $"Another Wolfden program has been writing to the den for {Seconds(wait)}, so nothing was written. Try again in a moment. " +
                        $"If no other Wolfden (wolfden serve, wolfden mcp, or another command) is running, the system said: {e.Message}");
                }

                if (!told)
                {
                    told = true;
                    waiting?.Invoke();
                }

                Thread.Sleep(pause);
                pause = pause * 2 > LongestPause ? LongestPause : pause * 2;
            }
            catch (DirectoryNotFoundException)
            {
                // The folder was deleted (by hand, or a reset index) between making it and opening the file.
                Directory.CreateDirectory(folder);
            }
        }
    }

    /// <summary>Lets go of the lock.</summary>
    public void Dispose() => _file.Dispose();

    private static string Seconds(TimeSpan wait) =>
        wait.TotalSeconds == 1 ? "1 second" : $"{wait.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} seconds";
}

namespace Wolfden.Core.Structure;

/// <summary>
/// How the den's files are opened to be read (#298): shared with anything
/// that's writing, renaming, or deleting them, so only a truly exclusive lock
/// refuses a read, and a read never gets in the way of another program's save.
/// </summary>
/// <remarks>
/// <para>
/// The cost is that a read during another program's save can see part of it.
/// A view corrects itself on its next read. A write can't take that on trust:
/// see <see cref="TrackingDenSource"/>, and format.md F-GEN-5.
/// </para>
/// <para>
/// On Linux and macOS, .NET turns any sharing into the same advisory shared
/// <c>flock</c> (<see cref="FileShare.None"/> alone takes an exclusive one),
/// and ignores <see cref="FileShare.Delete"/>. So there this changes nothing:
/// only a program that takes an exclusive <c>flock</c> refuses a read, as it
/// did before, and a program that doesn't lock could always be read mid-save.
/// </para>
/// </remarks>
public static class SharedFile
{
    // ERROR_SHARING_VIOLATION.
    private const int SharingViolation = 32;

    /// <summary>
    /// The bytes of the file at the full path <paramref name="full"/>, read to
    /// its end as it is at that moment, so a file that shrinks or grows during
    /// the read isn't an error. Throws what opening or reading it throws.
    /// </summary>
    public static byte[] ReadAllBytes(string full)
    {
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0, FileOptions.SequentialScan);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>
    /// True if another program has the file at the full path
    /// <paramref name="full"/> open with write access now. Only Windows can
    /// tell, so it's false on Linux and macOS, where programs writing a file
    /// don't normally say so. False too if the file can't be opened for any
    /// other reason: what reads it next reports that.
    /// </summary>
    public static bool IsOpenForWriting(string full)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            // Not sharing writes is what conflicts with a program that has write access.
            using var probe = File.OpenHandle(full, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return false;
        }
        catch (IOException e) when ((e.HResult & 0xFFFF) == SharingViolation)
        {
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

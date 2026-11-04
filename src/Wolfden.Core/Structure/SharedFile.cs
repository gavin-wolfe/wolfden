namespace Wolfden.Core.Structure;

/// <summary>
/// How a view opens a den's file to read it (#298): shared with anything
/// writing, renaming, or deleting it, so only a lock that keeps others from
/// reading stops a page or a check seeing a note that's open elsewhere.
/// </summary>
/// <remarks>
/// <para>
/// A read like this can land part way through another program's save. A view
/// corrects itself on its next read. A write couldn't undo what it built on
/// that part, so nothing a write builds on reads this way: it reads with
/// <see cref="File.ReadAllBytes(string)"/>, which doesn't share writing, so it's
/// refused while another program has the file open to write. See
/// <see cref="FileSystemDenSource.ForViews"/> and format.md F-GEN-5.
/// </para>
/// <para>
/// On Linux and macOS, .NET takes the same advisory shared <c>flock</c> for any
/// sharing but <see cref="FileShare.None"/>, and ignores
/// <see cref="FileShare.Delete"/>, so the two kinds of read are the same there:
/// only an exclusive <c>flock</c> refuses either one.
/// </para>
/// </remarks>
public static class SharedFile
{
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
}

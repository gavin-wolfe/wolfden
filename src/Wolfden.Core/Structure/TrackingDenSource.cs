using System.Collections.Concurrent;
using System.Security.Cryptography;
using Wolfden.Core.Documents;

namespace Wolfden.Core.Structure;

/// <summary>
/// A file that's there but can't be read, or a folder that's there but can't
/// be listed, from <see cref="TrackingDenSource.Unreadable"/> (#284).
/// </summary>
/// <param name="Path">The den-relative path; the den's own folder is <c>.</c>.</param>
/// <param name="IsFolder">True for a folder that can't be listed.</param>
public sealed record UnreadablePath(string Path, bool IsFolder)
{
    /// <summary>The rule of <see cref="ToDiagnostic"/>.</summary>
    public const string RuleId = "F-GEN-5";

    /// <summary>What a writer can do about <paramref name="paths"/>, as sentences.</summary>
    public static string WhatToDo(IEnumerable<UnreadablePath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Any(p => !p.IsFolder)
            ? "If another program has a file open, close it there, then try again. If none does, check the permissions."
            : "Check the folder's permissions, then try again.";
    }

    /// <summary>This path as a <c>wolfden check</c> diagnostic (format.md F-GEN-5).</summary>
    public DenDiagnostic ToDiagnostic() => new(Path, new Diagnostic(
        RuleId,
        DiagnosticSeverity.Error,
        null,
        (IsFolder ? "This folder couldn't be listed, so what's in it wasn't checked, and links to it may show as broken. "
            : Path == "den.yaml" ? "den.yaml couldn't be read, so nothing else was checked. "
            : Path.EndsWith(".md", StringComparison.Ordinal) ? "This file couldn't be read, so it wasn't checked, and links to it may show as broken. "
            : "This file couldn't be read, so it wasn't checked. ") + WhatToDo([this])));
}

/// <summary>
/// A den source that remembers every file it couldn't read and every folder
/// it couldn't list, while they were there (#284). Reads are as absent as
/// ever (#112, #116), so a load goes on as usual. A check or a write that has
/// to have seen everything asks <see cref="Unreadable"/> afterwards, so what
/// it reports is exactly what that load missed, on the same reads.
/// </summary>
/// <remarks>
/// A file deleted between listing and reading isn't a failure, and neither
/// is a folder deleted: both really are absent. The den's views and the index
/// don't use this: for them, a file that can't be read stays absent (#115).
/// </remarks>
/// <param name="inner">The den to read.</param>
/// <param name="forWrite">
/// True if a write will build on these reads, so a read must not be part of
/// another program's save (#298). A file another program has open to write
/// (<see cref="IDenSource.IsOpenForWriting"/>) is read again after a pause, and
/// counts as unreadable if it keeps changing, or stays empty. And
/// <see cref="ChangedSinceRead"/> can say if any file read has changed since.
/// </param>
public sealed class TrackingDenSource(IDenSource inner, bool forWrite = false) : IDenSource
{
    /// <summary>How long a file open for writing has to stay the same to count as read whole.</summary>
    internal static readonly TimeSpan SettlePause = TimeSpan.FromMilliseconds(150);

    /// <summary>How many times a file open for writing is read again before it counts as still being saved.</summary>
    internal const int SettleLooks = 4;

    private readonly IDenSource _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    // A load can read on several threads.
    private readonly ConcurrentDictionary<UnreadablePath, byte> _unreadable = new();

    // For a write: the hash of each file when it was first read.
    private readonly ConcurrentDictionary<string, byte[]> _read = new(StringComparer.Ordinal);

    // Folders found listable, so a den of many books asks about each folder once (#119). A source is made for one load.
    private readonly ConcurrentDictionary<string, byte> _listable = new(StringComparer.Ordinal);

    /// <summary>What couldn't be read or listed so far, by path in ASCII order.</summary>
    public IReadOnlyList<UnreadablePath> Unreadable =>
        _unreadable.Keys.OrderBy(u => u.Path, StringComparer.Ordinal).ToList();

    /// <summary>True if a file exists at <paramref name="path"/>. One that isn't there may be in a folder that can't be listed, so that's noted.</summary>
    public bool FileExists(string path)
    {
        if (_inner.FileExists(path))
        {
            return true;
        }

        for (var folder = DenPaths.Parent(path); ; folder = DenPaths.Parent(folder))
        {
            if (NoteIfUnlistable(folder) || folder.Length == 0)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public byte[] ReadFile(string path)
    {
        try
        {
            var bytes = _inner.ReadFile(path);
            return forWrite ? Settled(path, bytes) : bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && e is not (FileNotFoundException or DirectoryNotFoundException))
        {
            // Asking again tells a file that's gone (a link broken just now, a delete under way) from one that's held.
            if (_inner.FileExists(path))
            {
                _unreadable.TryAdd(new UnreadablePath(path, IsFolder: false), 0);
            }

            throw;
        }
    }

    /// <summary>
    /// The paths of the files read so far that hold something else now, or
    /// are gone, by path in ASCII order, for a write to ask just before it
    /// saves. Part of another program's save hides whatever's still to come,
    /// such as a link, so this covers every file the write read, not only
    /// those it rewrites. Each is read again here, so one that can't be read
    /// now is in <see cref="Unreadable"/> too.
    /// </summary>
    public IReadOnlyList<string> ChangedSinceRead()
    {
        var self = (IDenSource)this;
        return _read
            .Where(r => self.TryReadListedFile(r.Key) is not { } now || !SHA256.HashData(now).AsSpan().SequenceEqual(r.Value))
            .Select(r => r.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The bytes read for a write, once they're known not to be part of a save, as far as that can be told.</summary>
    private byte[] Settled(string path, byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        if (_read.TryGetValue(path, out var first) && first.AsSpan().SequenceEqual(hash))
        {
            // Settled already: a load reads some files more than once.
            return bytes;
        }

        if (_inner.IsOpenForWriting(path))
        {
            bytes = AfterItSettles(path, bytes);
            hash = SHA256.HashData(bytes);
        }

        _read.TryAdd(path, hash);
        return bytes;
    }

    /// <summary>
    /// The file's bytes once two reads a pause apart agree. A program with the
    /// file open but not writing passes at the first pause, at the cost of
    /// that pause. One mid-save usually doesn't, and it can't be told from a
    /// save paused for longer than this; the empty file left when a save has
    /// just cut the old text is refused anyway. Throws an
    /// <see cref="IOException"/> if it doesn't settle, so it counts as
    /// unreadable like any other file that can't be read.
    /// </summary>
    private byte[] AfterItSettles(string path, byte[] bytes)
    {
        for (var look = 0; look < SettleLooks; look++)
        {
            Thread.Sleep(SettlePause);
            var again = _inner.ReadFile(path);
            if (again.AsSpan().SequenceEqual(bytes))
            {
                return bytes.Length > 0 ? bytes : throw StillBeingSaved(path);
            }

            bytes = again;
        }

        throw StillBeingSaved(path);
    }

    private static IOException StillBeingSaved(string path) => new($"Another program is still saving '{path}'.");

    /// <inheritdoc />
    public IEnumerable<string> GetDirectories(string path) => NotingEmpty(path, _inner.GetDirectories(path));

    /// <inheritdoc />
    public IEnumerable<string> GetFiles(string path) => NotingEmpty(path, _inner.GetFiles(path));

    /// <inheritdoc />
    public bool FolderExists(string path)
    {
        if (_inner.FolderExists(path))
        {
            return true;
        }

        NoteIfUnlistable(path);
        return false;
    }

    /// <inheritdoc />
    public bool CannotList(string path) => _inner.CannotList(path);

    /// <inheritdoc />
    public IEnumerable<string> GetFolderLinks(string path) => _inner.GetFolderLinks(path);

    /// <summary>A folder that can't be listed gives an empty listing, so only those need asking about.</summary>
    private List<string> NotingEmpty(string path, IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Count == 0)
        {
            NoteIfUnlistable(path);
        }

        return list;
    }

    private bool NoteIfUnlistable(string folder)
    {
        if (_listable.ContainsKey(folder))
        {
            return false;
        }

        if (!_inner.CannotList(folder))
        {
            _listable.TryAdd(folder, 0);
            return false;
        }

        _unreadable.TryAdd(new UnreadablePath(folder.Length == 0 ? "." : folder, IsFolder: true), 0);
        return true;
    }
}

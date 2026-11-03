using System.Collections.Concurrent;
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
public sealed class TrackingDenSource(IDenSource inner) : IDenSource
{
    private readonly IDenSource _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    // A load can read on several threads.
    private readonly ConcurrentDictionary<UnreadablePath, byte> _unreadable = new();

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
            return _inner.ReadFile(path);
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

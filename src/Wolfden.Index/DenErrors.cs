using Wolfden.Core.Checks;
using Wolfden.Core.History;
using Wolfden.Core.Structure;

namespace Wolfden.Index;

/// <summary>What an error leaves out (format.md F-GEN-2).</summary>
public enum DenErrorReach
{
    /// <summary>One section and its subsections: a bad value in its comment.</summary>
    Section,

    /// <summary>The whole file: an entity, an outline, or a pack's file.</summary>
    File,

    /// <summary>A universe, series, or book and everything under it.</summary>
    Container,

    /// <summary>The whole den: <c>den.yaml</c> can't be loaded.</summary>
    Den,
}

/// <summary>An error in the den, with what it leaves out (<see cref="DenIndex.Errors"/>).</summary>
/// <param name="Path">The file or folder, relative to the den root.</param>
/// <param name="Line">The 1-based line, or null.</param>
/// <param name="Rule">The rule, such as <c>F-STAT-1</c>.</param>
/// <param name="Message">The message.</param>
/// <param name="Reach">What it leaves out.</param>
/// <param name="Section">For <see cref="DenErrorReach.Section"/>, the section's name. A heading can give away a reveal, so a view at a story position doesn't say it.</param>
/// <param name="SectionLine">For <see cref="DenErrorReach.Section"/>, the line of the section's heading.</param>
/// <param name="HeldFile">For a container folder said to have no file (F-LAY-4), the file if it's there but can't be read (F-GEN-5), so there's nothing to add.</param>
public sealed record DenError(string Path, int? Line, string Rule, string Message, DenErrorReach Reach, string? Section, int? SectionLine, string? HeldFile = null);

/// <summary>Works out what an error reaches.</summary>
public static class DenErrors
{
    /// <summary>What an error that isn't about one section reaches, by where it is (format.md F-GEN-2).</summary>
    /// <param name="path">The file or folder, relative to the den root.</param>
    public static DenErrorReach ReachOfFileError(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return KindOf(path) switch
        {
            DenFileKinds.Den => DenErrorReach.Den,
            DenFileKinds.Universe or DenFileKinds.Series or DenFileKinds.Book or DenFileKinds.Folder => DenErrorReach.Container,
            _ => DenErrorReach.File,
        };
    }

    /// <summary>
    /// What the file or folder at <paramref name="path"/> is, from where it
    /// is alone (one of <see cref="DenFileKinds"/>; format.md §5.1), so it
    /// can be said of a file the den couldn't read.
    /// </summary>
    public static string KindOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path == "den.yaml")
        {
            return DenFileKinds.Den;
        }

        // The den's own folder, when it can't be listed (F-GEN-5).
        if (path == ".")
        {
            return DenFileKinds.Folder;
        }

        var names = path.Split('/');
        if (names[0] == "packs")
        {
            return DenFileKinds.Pack;
        }

        var other = names[^1].Contains('.', StringComparison.Ordinal) ? DenFileKinds.Other : DenFileKinds.Folder; // a container folder without its file (F-LAY-4)
        if (names[0] != "universes" || names.Length < 3)
        {
            return other;
        }

        var at = 2;
        var inSeries = names.Length > at + 2 && names[at] == "series";
        at += inSeries ? 2 : 0;
        var inBook = names.Length > at + 2 && names[at] == "books";
        at += inBook ? 2 : 0;
        return names[at] switch
        {
            "universe.md" when at == 2 && names.Length == 3 => DenFileKinds.Universe,
            "series.md" when inSeries && !inBook && names.Length == at + 1 => DenFileKinds.Series,
            "book.md" when inBook && names.Length == at + 1 => DenFileKinds.Book,
            "outline.md" when inBook && names.Length == at + 1 => DenFileKinds.Outline,
            "entities" when names.Length > at + 1 && names[^1].EndsWith(".md", StringComparison.Ordinal) => DenFileKinds.Note,
            _ => other,
        };
    }

    /// <summary>The universe the path is in, or null for a den-level one such as <c>den.yaml</c>.</summary>
    public static string? UniverseOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var names = path.Split('/');
        return names.Length >= 2 && names[0] == "universes" ? names[1] : null;
    }

    /// <summary>
    /// True if a session pinned to <paramref name="pinned"/> doesn't see the
    /// path: it's in another universe. Den-level material counts for every
    /// universe, as in a check of one universe (<see cref="UniverseCheck"/>).
    /// </summary>
    public static bool IsOutsidePin(string path, string? pinned) =>
        pinned is not null && UniverseOf(path) is { } universe && universe != pinned;

    /// <summary>
    /// True if Wolfden's Edit file can open this kind of file: a note, an
    /// outline, a container's file, and <c>den.yaml</c> unless the session is
    /// pinned, since <c>den.yaml</c> is shared by every universe. A pack's
    /// files and folders are fixed outside Wolfden.
    /// </summary>
    public static bool CanEdit(string kind, string? pinned) => kind switch
    {
        DenFileKinds.Note or DenFileKinds.Outline or DenFileKinds.Book or DenFileKinds.Series or DenFileKinds.Universe => true,
        DenFileKinds.Den => pinned is null,
        _ => false,
    };
}

/// <summary>What a path in the den is (<see cref="DenErrors.KindOf"/>).</summary>
public static class DenFileKinds
{
    /// <summary><c>den.yaml</c>.</summary>
    public const string Den = "den";

    /// <summary>A <c>universe.md</c>.</summary>
    public const string Universe = "universe";

    /// <summary>A <c>series.md</c>.</summary>
    public const string Series = "series";

    /// <summary>A <c>book.md</c>.</summary>
    public const string Book = "book";

    /// <summary>A book's <c>outline.md</c>.</summary>
    public const string Outline = "outline";

    /// <summary>An entity file.</summary>
    public const string Note = "note";

    /// <summary>A file of a den-local pack.</summary>
    public const string Pack = "pack";

    /// <summary>A folder.</summary>
    public const string Folder = "folder";

    /// <summary>Anything else.</summary>
    public const string Other = "other";
}

/// <summary>The den's errors now (<see cref="DenErrorWatch.Current"/>).</summary>
/// <param name="DenLoaded">False if <c>den.yaml</c> doesn't load, so nothing else was read.</param>
/// <param name="Errors">The errors, by path and line.</param>
/// <param name="Names">The name of each note in <paramref name="Errors"/> that the index has, by path.</param>
public sealed record DenErrorList(bool DenLoaded, IReadOnlyList<DenError> Errors, IReadOnlyDictionary<string, string> Names);

/// <summary>
/// The den's errors, cheap enough to ask for every few seconds (#280). Bringing
/// the index up to date reads and hashes every file, so that's done only when
/// a look at the files' sizes and times says something changed
/// (<see cref="AutoCheckpointer.FilesFingerprint"/>, which reads no contents).
/// Until then the last answer stands, for at most <paramref name="maxAge"/>,
/// since sizes and times don't say everything: a file another program held open
/// was read as absent (#112), and letting it go changes neither. Pinned, only the
/// universe's errors and the den-level ones count, as in a check of that universe.
/// </summary>
/// <remarks>
/// The index reads a note held open as absent, so what's wrong inside it isn't
/// known, but that it couldn't be read is (F-GEN-5, #284): <see cref="Read"/> adds
/// that to the index's errors, as <c>wolfden check</c> does. Taking a lock doesn't
/// change a file's size or time, so it shows when the answer's age runs out.
/// </remarks>
/// <param name="root">The den's folder.</param>
/// <param name="watcherWait">The longest to wait for a running watcher to catch up.</param>
/// <param name="pinned">The universe the session is pinned to, or null.</param>
/// <param name="maxAge">The longest an answer stands with no change seen, by default <see cref="DefaultMaxAge"/>.</param>
public sealed class DenErrorWatch(string root, TimeSpan watcherWait, string? pinned, TimeSpan? maxAge = null)
{
    /// <summary>How long an answer stands by default: a full look costs a hash of the den, so not often, but it does come.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromSeconds(60);

    private readonly Lock _lock = new();
    private readonly System.Diagnostics.Stopwatch _age = new();
    private string? _fingerprint;
    private DenErrorList? _last;

    /// <summary>What a container folder's file is called (format.md §5.1).</summary>
    private static readonly string[] ContainerFiles = ["universe.md", "series.md", "book.md"];

    /// <summary>How many times this has read the errors, so tests can see that an unchanged den costs no index update.</summary>
    public int Reads { get; private set; }

    /// <summary>The errors as the files are now.</summary>
    /// <exception cref="IOException">The index couldn't be read or rebuilt.</exception>
    /// <exception cref="UnauthorizedAccessException">The index couldn't be read or rebuilt.</exception>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">The index couldn't be read.</exception>
    public DenErrorList Current()
    {
        lock (_lock)
        {
            // Taken before the index is updated: a file that changes during the
            // update makes the next look differ, so it's never missed.
            var fingerprint = AutoCheckpointer.FilesFingerprint(root);
            if (_last is not null && fingerprint == _fingerprint && _age.Elapsed < (maxAge ?? DefaultMaxAge))
            {
                return _last;
            }

            _last = Read();
            _fingerprint = fingerprint;
            _age.Restart();
            Reads++;
            return _last;
        }
    }

    /// <summary>
    /// Brings the index up to date, reads the errors from it, and adds what
    /// couldn't be read. The index reads such a file as absent, so that takes
    /// a check of its own, the same one <c>wolfden check</c> runs, so the two
    /// list the same paths (<see cref="DenCheck"/>).
    /// </summary>
    private DenErrorList Read()
    {
        IndexBuilder.EnsureUpToDate(root, watcherWait);
        var tracked = new TrackingDenSource(new FileSystemDenSource(root));
        _ = DenCheck.Run(tracked, pinned);

        // Even pinned, the other universes' container files are read, for story order.
        var unreadable = tracked.Unreadable.Where(u => !DenErrors.IsOutsidePin(u.Path, pinned)).ToList();
        var held = unreadable.Where(u => !u.IsFolder).Select(u => u.Path).ToHashSet(StringComparer.Ordinal);
        using var index = DenIndex.Open(root);
        var errors = index.Errors()
            .Where(e => !DenErrors.IsOutsidePin(e.Path, pinned))

            // A den.yaml that's there isn't missing, as in check.
            .Where(e => !(held.Contains("den.yaml") && e.Path == "den.yaml" && e.Rule == "F-DEN-1"))
            .Select(e => e.Rule == "F-LAY-4" ? e with { HeldFile = ContainerFiles.Select(f => $"{e.Path}/{f}").FirstOrDefault(held.Contains) } : e)
            .Concat(unreadable.Select(u =>
            {
                var said = u.ToDiagnostic();
                return new DenError(u.Path, null, said.Diagnostic.RuleId, said.Diagnostic.Message, u.IsFolder ? DenErrorReach.Container : DenErrors.ReachOfFileError(u.Path), null, null);
            }))
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .ThenBy(e => e.Line ?? 0)
            .ToList();
        var paths = errors.Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
        var names = index.Context.Entities.Where(e => paths.Contains(e.Path)).Select(e => new { e.Path, e.Name }).ToList()
            .ToDictionary(e => e.Path, e => e.Name, StringComparer.Ordinal);
        return new DenErrorList(index.Context.Dens.Single().IsLoaded, errors, names);
    }
}

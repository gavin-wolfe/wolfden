using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Wolfden.Core.Documents;
using Wolfden.Core.Editing;
using Wolfden.Core.Entities;
using Wolfden.Core.Outlines;
using Wolfden.Core.Structure;
using Wolfden.Core.Text;
using Wolfden.Index.Reading;

namespace Wolfden.Index.Writing;

/// <summary>
/// The den's errors, and fixing them (#280): a file named by its path can be
/// opened and saved whole, even one the den leaves out, which no name in the
/// index reaches.
/// </summary>
/// <remarks>
/// <para>
/// Only a den file a writer edits can be named: a note, an outline, a
/// container's file, or a <c>den.yaml</c> that doesn't load. The path must be
/// written exactly as it is on disk, with no folder or file link on the way,
/// and in a pinned session it can't be in another universe, nor be
/// <c>den.yaml</c>. Anything else gets the same <c>unknown_file</c>, whether it
/// exists or not.
/// </para>
/// <para>
/// A file the den reads is saved by the usual rules. A file the den leaves out
/// whole can't get worse for the den, and fixing it can take more than one
/// step, so its save is refused only if it would have more errors than it has
/// now. Frontmatter that doesn't parse hides whatever is wrong below it, so a
/// save of such a file is never refused for what it reveals.
/// </para>
/// </remarks>
public sealed partial class DenTools
{
    private DenErrorWatch? _errors;

    /// <summary>
    /// The den's errors now, or a pinned session's (<see cref="DenErrorWatch"/>).
    /// It costs a look at the files' sizes and times unless something changed.
    /// </summary>
    public DenErrorList CurrentErrors() =>
        LazyInitializer.EnsureInitialized(ref _errors, () => new DenErrorWatch(Root, WatcherWait, PinnedUniverse)).Current();

    /// <summary>
    /// A short line for an assistant when the den has errors (#280), or null
    /// when it has none or they can't be read. It names no note or section,
    /// so it's safe in a view at a story position.
    /// </summary>
    public string? ErrorNotice()
    {
        try
        {
            var count = CurrentErrors().Errors.Count;
            return count == 0
                ? null
                : $"This den has {count.ToString(CultureInfo.InvariantCulture)} error{(count == 1 ? "" : "s")}, so some notes or sections may be missing from this answer. Call check with severity 'error' to see {(count == 1 ? "it" : "them")}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException)
        {
            return null;
        }
    }

    /// <summary>A file as Edit file needs it, by its path, read from the file. See the remarks on this part of the class for which paths it takes.</summary>
    public FileSourceResult GetFileSource(string path)
    {
        var den = Snapshot();
        var structure = DenStructure.Load(den);
        var kind = CheckEditable(den, structure, path);
        var bytes = den.TryReadFile(path) ?? throw NoFile(path);
        var file = Parsed(kind, bytes);
        var entity = kind == DenFileKinds.Note ? EntityCatalog.ReadEntityFile(structure, path, bytes).Entity : null;
        return new FileSourceResult(
            DenReader.ResultVersion, path, kind, DenErrors.UniverseOf(path), HashOf(bytes),
            file.IsReadable ? Lf(file.Text) : null,
            file.LineEndings switch { LineEndingKind.CrLf => "crlf", LineEndingKind.Mixed => "mixed", _ => "lf" },
            file.HasByteOrderMark,
            !file.IsReadable ? null : kind == DenFileKinds.Den && structure.IsLoaded ? DenYamlLoads : FileFrozen(kind, path, structure, Lf(file.Text)),
            Errors(path, DiagnosticsIn(kind, path, bytes, structure)),
            entity is null ? null : Summary(entity));
    }

    /// <summary>
    /// Replaces the whole file at <paramref name="path"/> with
    /// <paramref name="text"/>: its line breaks become the file's own, and a
    /// byte order mark it had is kept. It's refused on a stale hash, in a final
    /// book, or if it would add an error (see the remarks on this part of the
    /// class). An id never changes (F-ID-4).
    /// </summary>
    public WriteResult ReplaceFileAt(string path, string text, string expectedHash)
    {
        ArgumentNullException.ThrowIfNull(text);
        var hash = RequireHash(expectedHash);
        var look = Snapshot();
        var looked = DenStructure.Load(look);
        var kind = CheckEditable(look, looked, path);
        if (kind == DenFileKinds.Den && looked.IsLoaded)
        {
            throw new DenToolException(ToolErrorCodes.UnsupportedEdit, DenYamlLoads);
        }

        // A note the den reads is saved as any other, with every rule of Edit file.
        if (kind == DenFileKinds.Note && look.TryReadFile(path) is { } now && EntityCatalog.ReadEntityFile(looked, path, now).Entity is not null)
        {
            return ReplaceFile(path, text, hash, DenErrors.UniverseOf(path));
        }

        var hasId = kind is not (DenFileKinds.Den or DenFileKinds.Outline);
        var ids = hasId
            ? WithIndex(index => index.Context.Entities.Where(e => e.Ulid != null).Select(e => e.Ulid!).ToList()
                .Concat(index.Context.Containers.Where(c => c.Ulid != null).Select(c => c.Ulid!))
                .ToHashSet(StringComparer.Ordinal))
            : [];
        using (EnterWrite())
        {
            var den = Snapshot();
            var structure = DenStructure.Load(den);
            CheckEditable(den, structure, path);
            if (kind == DenFileKinds.Den && structure.IsLoaded)
            {
                throw new DenToolException(ToolErrorCodes.UnsupportedEdit, DenYamlLoads);
            }

            var original = den.TryReadFile(path) ?? throw Gone(path);
            CheckHash(path, original, hash);
            var current = Parsed(kind, original);
            if (!current.IsReadable)
            {
                throw new DenToolException(ToolErrorCodes.UnsupportedEdit, $"{path} isn't valid UTF-8 text, so it can't be edited here. {ToolWords.Cap(Words.CheckFile(path))} to see why.");
            }

            var before = Lf(current.Text);
            if (before == Lf(text))
            {
                return new WriteResult(DenReader.ResultVersion, false, path, hash, null, ["Nothing changed: the file already says that."], [], []);
            }

            if (FileFrozen(kind, path, structure, before) is { } frozen)
            {
                throw new DenToolException(ToolErrorCodes.Frozen, frozen);
            }

            CheckFinalBooksAbove(kind, path, structure, before, Lf(text));
            if (hasId)
            {
                CheckId(path, before, Lf(text), ids);
            }

            var newline = current.LineEndings == LineEndingKind.CrLf || (current.LineEndings == LineEndingKind.Mixed && CrLfOutnumbersLf(current.Text)) ? "\r\n" : "\n";
            var payload = Encoding.UTF8.GetBytes(LineBreak().Replace(text, newline));
            var bytes = current.HasByteOrderMark ? [0xEF, 0xBB, 0xBF, .. payload] : payload;

            var errorsBefore = OnlyErrors(DiagnosticsIn(kind, path, original, structure));
            var wasLeftOut = errorsBefore.Any(d => d.LeavesOutOnly is null);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = bytes };
            if (!wasLeftOut)
            {
                // The den reads this file now, so a save may add no error (format.md §1.3).
                var check = Check(den, structure, files);
                Save(files, new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = original }, created: null);
                return new WriteResult(DenReader.ResultVersion, true, path, HashOf(bytes), null, ["Saved the whole file."], [], Warnings(check));
            }

            var afterStructure = kind is DenFileKinds.Note or DenFileKinds.Outline ? structure : DenStructure.Load(new OverlayDenSource(den, files));
            var diagnostics = DiagnosticsIn(kind, path, bytes, afterStructure);
            var errorsAfter = OnlyErrors(diagnostics);
            var hidIts = kind == DenFileKinds.Den || FrontmatterHid(errorsBefore);
            if (!hidIts && errorsAfter.Count > errorsBefore.Count)
            {
                throw new DenToolException(
                    ToolErrorCodes.InvalidEdit,
                    $"The edit would add errors, so nothing was written. It has {errorsBefore.Count.ToString(CultureInfo.InvariantCulture)} now and would have {errorsAfter.Count.ToString(CultureInfo.InvariantCulture)}: " +
                    string.Join("; ", errorsAfter.Select(d => $"{path}:{d.Line?.ToString(CultureInfo.InvariantCulture) ?? "-"}: {d.RuleId}: {d.Message}")));
            }

            var healed = errorsAfter.All(d => d.LeavesOutOnly is not null);
            string? idAdded = null;
            if (healed && hasId && WrittenId(Lf(text)) is null)
            {
                var file = MarkdownFile.Parse(bytes, requireFrontmatter: true);
                idAdded = AddId(ref file, ids);
                bytes = file.ToBytes();
                files[path] = bytes;
            }

            Save(files, new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = original }, created: null);

            var entity = kind == DenFileKinds.Note && healed ? EntityCatalog.ReadEntityFile(afterStructure, path, bytes).Entity : null;
            List<string> effects = [healed
                ? kind == DenFileKinds.Den ? "Saved the whole file. The den loads now, so errors in its files can show too." : "Saved the whole file. It's back in the den."
                : $"Saved the whole file. It still has {errorsAfter.Count.ToString(CultureInfo.InvariantCulture)} error{(errorsAfter.Count == 1 ? "" : "s")}, so the den still leaves it out."];
            if (idAdded is not null)
            {
                effects.Add($"The file had no id, so it was given {idAdded} (F-ID-6).");
            }

            return new WriteResult(
                DenReader.ResultVersion, true, path, HashOf(bytes), entity is null ? null : Summary(entity), effects, [],
                diagnostics.OrderBy(d => d.Severity == DiagnosticSeverity.Error ? 0 : 1).ThenBy(d => d.Line ?? 0)
                    .Select(d => new DiagnosticView(path, d.Line, d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.RuleId, d.Message)).ToList(),
                IdAdded: idAdded);
        }
    }

    /// <summary>Why a <c>den.yaml</c> that loads is shown but not saved here: changing it can change how every file reads.</summary>
    private const string DenYamlLoads = "den.yaml loads, so it can't be changed here: Edit file saves it only to fix an error that stops the den loading. To change it, open it in your own editor.";

    private static DenToolException NoFile(string? path) => new(
        ToolErrorCodes.UnknownFile,
        $"Wolfden has no file to edit at '{(path is { Length: > 200 } ? path[..200] + "…" : path)}'. It may have been moved or deleted.");

    /// <summary>What the path is, if it can be opened; otherwise <c>unknown_file</c>, or an error saying which file to fix first.</summary>
    private string CheckEditable(IDenSource den, DenStructure structure, string? path)
    {
        if (path is null || DenPaths.Problem(path) is not null)
        {
            throw NoFile(path);
        }

        var kind = DenErrors.KindOf(path);
        if (!DenErrors.CanEdit(kind, PinnedUniverse) || DenErrors.IsOutsidePin(path, PinnedUniverse) || !IsPlainFile(path) || den.TryReadFile(path) is null)
        {
            throw NoFile(path);
        }

        if (kind == DenFileKinds.Den)
        {
            return kind;
        }

        if (!structure.IsLoaded)
        {
            throw new DenToolException(ToolErrorCodes.InvalidEdit, $"den.yaml doesn't load, so {path} can't be read as the den would. Fix den.yaml first.");
        }

        var containers = structure.Universes.SelectMany(u => new Container[] { u }.Concat(u.Series).Concat(u.Books)).Select(c => c.Path).ToHashSet(StringComparer.Ordinal);
        var loaded = kind switch
        {
            DenFileKinds.Universe => true,
            DenFileKinds.Series => containers.Contains(DenPaths.Parent(DenPaths.Parent(DenPaths.Parent(path)))),
            DenFileKinds.Book => containers.Contains(DenPaths.Parent(DenPaths.Parent(DenPaths.Parent(path)))),
            DenFileKinds.Outline => containers.Contains(DenPaths.Parent(path)),
            _ => EntityCatalog.EntityFileScope(structure, path) is not null,
        };
        return loaded
            ? kind
            : throw new DenToolException(ToolErrorCodes.InvalidEdit, $"An error in what holds {path} leaves it out too, so it can't be read as the den would. Fix that first: it's in the list of problems.");
    }

    /// <summary>
    /// True if a file is at the path under the den's folder, written exactly
    /// as it is on disk (so no other case, and no Windows short name), with no
    /// folder link on the way and not a link itself.
    /// </summary>
    private bool IsPlainFile(string path)
    {
        var options = new EnumerationOptions { MatchType = MatchType.Simple, MatchCasing = MatchCasing.PlatformDefault, AttributesToSkip = 0, IgnoreInaccessible = true };
        try
        {
            var names = path.Split('/');
            var folder = new DirectoryInfo(Root);
            foreach (var name in names[..^1])
            {
                var next = folder.EnumerateDirectories(name, options).FirstOrDefault(d => d.Name == name);
                if (next is null || FileSystemDenSource.IsFolderLink(next))
                {
                    return false;
                }

                folder = next;
            }

            return folder.EnumerateFiles(names[^1], options).FirstOrDefault(f => f.Name == names[^1]) is { LinkTarget: null };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static DenFile Parsed(string kind, byte[] bytes) => kind switch
    {
        DenFileKinds.Den => YamlFile.Parse(bytes),
        DenFileKinds.Outline => MarkdownFile.ParseOutline(bytes),
        _ => MarkdownFile.Parse(bytes, requireFrontmatter: true),
    };

    /// <summary>The file's diagnostics as the den reads it, with <paramref name="structure"/> already holding it if it's a container's file or <c>den.yaml</c>.</summary>
    private static List<Diagnostic> DiagnosticsIn(string kind, string path, byte[] bytes, DenStructure structure)
    {
        switch (kind)
        {
            case DenFileKinds.Note:
                return EntityCatalog.ReadEntityFile(structure, path, bytes).Diagnostics.ToList();
            case DenFileKinds.Outline:
                var outline = MarkdownFile.ParseOutline(bytes);
                return OutlineReader.IsExcluded(outline) ? outline.Diagnostics.ToList() : outline.Diagnostics.Concat(OutlineReader.Read(outline).Diagnostics).ToList();
            default:
                return structure.Diagnostics.Where(d => d.Path == path).Select(d => d.Diagnostic).ToList();
        }
    }

    private static List<Diagnostic> OnlyErrors(List<Diagnostic> diagnostics) => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    private static List<FileErrorView> Errors(string path, List<Diagnostic> diagnostics) =>
        OnlyErrors(diagnostics).OrderBy(d => d.Line ?? 0).Select(d => d.LeavesOutOnly is { } section
            ? new FileErrorView(d.Line, d.RuleId, d.Message, "section", section.Name, section.Line)
            : new FileErrorView(d.Line, d.RuleId, d.Message, DenErrors.ReachOfFileError(path).ToString().ToLowerInvariant(), null, null)).ToList();

    /// <summary>True if the frontmatter couldn't be read, so nothing below it was checked.</summary>
    private static bool FrontmatterHid(List<Diagnostic> errors) =>
        errors.Any(d => d.RuleId is "F-ENC-1" or "F-ENC-3" or "F-FM-1" or "F-FM-2" || d.RuleId.StartsWith("F-YAML-", StringComparison.Ordinal));

    /// <summary>Why the file can't be changed because of a final book, and what to do, or null if it can be.</summary>
    private string? FileFrozen(string kind, string path, DenStructure structure, string text)
    {
        var finalBook = kind switch
        {
            DenFileKinds.Note => EntityCatalog.EntityFileScope(structure, path) as Book,
            DenFileKinds.Outline or DenFileKinds.Book => structure.Universes.SelectMany(u => u.Books).FirstOrDefault(b => b.Path == DenPaths.Parent(path)),
            _ => null,
        };
        if (finalBook is { Status: BookStatus.Final })
        {
            return $"{path} is in book '{finalBook.Key}', which is final (frozen), so it can't be edited until the book is unfrozen (F-BOOK-1). {Unfreezing(finalBook)}";
        }

        // Left out, so the book can't be unfrozen. A freeze promised this text won't change, and that's for the writer to break.
        return kind == DenFileKinds.Book && finalBook is null && FinalStatus().IsMatch(Head(text))
            ? $"{path} says the book is final (frozen), but has an error, so the book can't be unfrozen here. Open it in your own editor to fix it."
            : null;
    }

    /// <summary>
    /// A book becomes final by Freeze, which checkpoints it, not by a word in
    /// its file; and a series or universe that loads can't change what a final
    /// book below it inherits or how it's ordered, so its frontmatter stays as
    /// it is here while it has one.
    /// </summary>
    private void CheckFinalBooksAbove(string kind, string path, DenStructure structure, string before, string after)
    {
        if (kind == DenFileKinds.Book && FinalStatus().IsMatch(Head(after)))
        {
            throw new DenToolException(
                ToolErrorCodes.UnsupportedEdit,
                $"{path} would mark the book final. Take 'status: final' out and save; Freeze… on the book's page makes it final, with a checkpoint.");
        }

        if (kind is not (DenFileKinds.Series or DenFileKinds.Universe) || Head(before) == Head(after))
        {
            return;
        }

        var folder = DenPaths.Parent(path) + "/";
        var finals = structure.Universes.SelectMany(u => u.Books).Where(b => b.Status == BookStatus.Final && b.Path.StartsWith(folder, StringComparison.Ordinal)).ToList();
        if (finals.Count > 0 && structure.Universes.SelectMany(u => new Container[] { u }.Concat(u.Series)).Any(c => c.FilePath == path))
        {
            throw new DenToolException(
                ToolErrorCodes.Frozen,
                $"{path} holds a final book, so its frontmatter can't change here, though the text below it can. {Unfreezing(finals)}");
        }
    }

    /// <summary>An id never changes (F-ID-4), and two files can't share one (F-ID-3). The file may not parse, so its id is read as text.</summary>
    private static void CheckId(string path, string before, string after, HashSet<string> used)
    {
        var old = WrittenId(before);
        var id = WrittenId(after);
        if (old is not null && id != old)
        {
            throw new DenToolException(
                ToolErrorCodes.UnsupportedEdit,
                $"The id of {path} is {old}, and an id never changes (F-ID-4): links and history follow it. Put back 'id: {old}' and save again.");
        }

        if (old is null && id is not null && used.Contains(id))
        {
            throw new DenToolException(
                ToolErrorCodes.AlreadyExists,
                $"Another note or container already has the id {id}, and two files can't share one (F-ID-3): links to either would break. " +
                "Take the id line out and save: the file gets a new id of its own (F-ID-6).");
        }
    }

    private static string? WrittenId(string text) => WrittenIdLine().Match(Head(text)) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>The text as far as the line that closes its frontmatter.</summary>
    private static string Head(string text)
    {
        var end = text.Length < 3 ? -1 : text.IndexOf("\n---", 3, StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    private static bool CrLfOutnumbersLf(string text)
    {
        var crlf = text.Split("\r\n").Length - 1;
        return crlf > text.Count(c => c == '\n') - crlf;
    }

    [GeneratedRegex(@"^id:[ \t]*[""']?([^\s""'#]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex WrittenIdLine();

    [GeneratedRegex(@"^status:[ \t]*[""']?final\b", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FinalStatus();
}

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
/// exists or not. A file it may open that's there but can't be read says so, with
/// <c>den_unreadable</c>, rather than seeming to have gone (#284). So does one
/// left out only because <c>den.yaml</c>, a pack, or a container's file above it
/// can't be read, rather than seeming to have an error.
/// </para>
/// <para>
/// A file the den reads is saved by the usual rules. A file the den leaves out
/// whole can't get worse for the den, and fixing it can take more than one
/// step, so its save is refused only if it would have more errors than it has
/// now. Frontmatter that doesn't parse hides whatever is wrong below it, so a
/// save of such a file is never refused for what it reveals. That ends when a
/// save brings the file back: such a save is checked as any other write is.
/// </para>
/// <para>
/// What's decided from the frontmatter (a book's status, an id, what a final book
/// depends on) is read from it parsed, so any way of writing it counts. A book
/// whose unparsed frontmatter holds <c>final</c> or an escape might be final, so
/// it isn't saved here. Without either it can't be, so it can be fixed, and a
/// book's save must leave it parsing.
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
        var den = ViewSnapshot();
        var structure = DenStructure.Load(den);
        var kind = CheckEditable(den, structure, path, opening: true);
        var bytes = den.TryReadFile(path) ?? throw UnreadableOrNoFile(den, path, opening: true);
        var file = Parsed(kind, bytes);
        var entity = kind == DenFileKinds.Note ? EntityCatalog.ReadEntityFile(structure, path, bytes).Entity : null;
        return new FileSourceResult(
            DenReader.ResultVersion, path, kind, DenErrors.UniverseOf(path), HashOf(bytes),
            file.IsReadable ? Lf(file.Text) : null,
            file.LineEndings switch { LineEndingKind.CrLf => "crlf", LineEndingKind.Mixed => "mixed", _ => "lf" },
            file.HasByteOrderMark,
            !file.IsReadable ? null : kind == DenFileKinds.Den && structure.IsLoaded ? DenYamlLoads : FileFrozen(kind, path, den, structure, Lf(file.Text)),
            Errors(path, DiagnosticsIn(kind, path, bytes, structure)),
            entity is null ? null : Summary(entity));
    }

    /// <summary>
    /// Replaces the whole file at <paramref name="path"/> with
    /// <paramref name="text"/>: its line breaks become the file's own, and a
    /// byte order mark it had is kept. It's refused on a stale hash, in a final
    /// book, or if it would add an error (see the remarks on this part of the
    /// class). An id never changes (F-ID-4). Unless it's a note the den reads,
    /// which is an ordinary note edit, it's refused while anything the save read
    /// can't be read (<see cref="RefuseUnreadable"/>, #284): the den's own files
    /// and outlines, though not other notes.
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

        if (text.All(c => char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format))
        {
            throw new DenToolException(ToolErrorCodes.InvalidEdit, $"That would leave {path} empty, and an empty file can't be put back without history. Nothing was written. To remove a file, delete it in your file manager.");
        }

        var hasId = kind is not (DenFileKinds.Den or DenFileKinds.Outline);
        var ids = hasId ? IdsInUse(look, path) : [];
        using (EnterWrite())
        {
            var tracked = new TrackingDenSource(Snapshot());
            IDenSource den = tracked;
            var structure = DenStructure.Load(den);
            CheckEditable(den, structure, path);
            if (kind == DenFileKinds.Den && structure.IsLoaded)
            {
                throw new DenToolException(ToolErrorCodes.UnsupportedEdit, DenYamlLoads);
            }

            var original = den.TryReadFile(path) ?? throw GoneOrUnreadable(den, path);
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

            if (FileFrozen(kind, path, den, structure, before) is { } frozen)
            {
                throw new DenToolException(ToolErrorCodes.Frozen, frozen);
            }

            CheckFinalBooksAbove(kind, path, den, structure, before, Lf(text));
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
                RefuseUnreadable(tracked);
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

            // A save that brings the file back is an ordinary write: the exception for a file left out ends here.
            CatalogEntity? entity = null;
            var warnings = diagnostics.OrderBy(d => d.Severity == DiagnosticSeverity.Error ? 0 : 1).ThenBy(d => d.Line ?? 0)
                .Select(d => new DiagnosticView(path, d.Line, d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.RuleId, d.Message)).ToList();
            if (healed && kind != DenFileKinds.Den)
            {
                var check = Check(den, structure, files);
                warnings = Warnings(check);
                if (kind == DenFileKinds.Note)
                {
                    entity = EntityCatalog.ReadEntityFile(check.Structure, path, bytes).Entity!;
                    CheckWrittenPositions(null, entity, tracked);
                }
            }

            RefuseUnreadable(tracked);
            Save(files, new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = original }, created: null);

            List<string> effects = [healed
                ? kind == DenFileKinds.Den ? "Saved the whole file. The den loads now, so errors in its files can show too." : "Saved the whole file. It's back in the den."
                : $"Saved the whole file. It still has {errorsAfter.Count.ToString(CultureInfo.InvariantCulture)} error{(errorsAfter.Count == 1 ? "" : "s")}, so the den still leaves it out."];
            if (idAdded is not null)
            {
                effects.Add($"The file had no id, so it was given {idAdded} (F-ID-6).");
            }

            return new WriteResult(
                DenReader.ResultVersion, true, path, HashOf(bytes), entity is null ? null : Summary(entity), effects, [], warnings, IdAdded: idAdded);
        }
    }

    /// <summary>Why a <c>den.yaml</c> that loads is shown but not saved here: changing it can change how every file reads.</summary>
    private const string DenYamlLoads = "den.yaml loads, so it can't be changed here: Edit file saves it only to fix an error that stops the den loading. To change it, open it in your own editor.";

    private static DenToolException NoFile(string? path) => new(
        ToolErrorCodes.UnknownFile,
        $"Wolfden has no file to edit at '{(path is { Length: > 200 } ? path[..200] + "…" : path)}'. It may have been moved or deleted.");

    /// <summary>What to say of a file that may be opened, but gave no bytes: there, it can't be read; gone, there's no file.</summary>
    private DenToolException UnreadableOrNoFile(IDenSource den, string path, bool opening) =>
        den.FileExists(path) ? CantRead([new UnreadablePath(path, IsFolder: false)], opening) : NoFile(path);

    /// <summary>The refusal for what can't be read: of a save, or, when the writer has only opened a page, in words for that.</summary>
    private DenToolException CantRead(IReadOnlyList<UnreadablePath> paths, bool opening) => !opening ? Unreadable(paths) : new(
        ToolErrorCodes.DenUnreadable,
        $"Couldn't read {Named(paths)}, so this can't be opened right now. " +
        (paths.Any(p => !p.IsFolder)
            ? "If another program has a file open, close it there, then reload this page. If none does, check the permissions."
            : "Check the folder's permissions, then reload this page."));

    /// <summary>
    /// Refuses, naming it, if what stopped the path being read as the den would
    /// is something that can't be read, not an error to fix. A second load of the
    /// structure, only on the way to refusing anyway. Only what's above the path
    /// counts when the den itself loaded: a sibling held has nothing to do with it.
    /// </summary>
    private void RefuseIfHeldAbove(IDenSource den, string path, bool denLoaded, bool opening)
    {
        var tracked = new TrackingDenSource(den);
        _ = DenStructure.Load(tracked);
        var why = tracked.Unreadable.Where(u => !denLoaded || IsAtOrUnder(path, u.IsFolder ? u.Path : DenPaths.Parent(u.Path))).ToList();
        if (why.Count > 0)
        {
            throw CantRead(why, opening);
        }
    }

    /// <summary>What the path is, if it can be opened; otherwise <c>unknown_file</c>, <c>den_unreadable</c>, or an error saying which file to fix first.</summary>
    private string CheckEditable(IDenSource den, DenStructure structure, string? path, bool opening = false)
    {
        if (path is null || DenPaths.Problem(path) is not null)
        {
            throw NoFile(path);
        }

        var kind = DenErrors.KindOf(path);
        if (!DenErrors.CanEdit(kind, PinnedUniverse) || DenErrors.IsOutsidePin(path, PinnedUniverse) || !IsPlainFile(path))
        {
            throw NoFile(path);
        }

        if (den.TryReadFile(path) is null)
        {
            throw UnreadableOrNoFile(den, path, opening);
        }

        if (kind == DenFileKinds.Den)
        {
            return kind;
        }

        if (!structure.IsLoaded)
        {
            RefuseIfHeldAbove(den, path, denLoaded: false, opening);
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
        if (!loaded)
        {
            RefuseIfHeldAbove(den, path, denLoaded: true, opening);
            throw new DenToolException(ToolErrorCodes.InvalidEdit, $"An error in what holds {path} leaves it out too, so it can't be read as the den would. Fix that first: it's in the list of problems.");
        }

        return kind;
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
    private string? FileFrozen(string kind, string path, IDenSource den, DenStructure structure, string text)
    {
        var book = kind switch
        {
            DenFileKinds.Note => EntityCatalog.EntityFileScope(structure, path) as Book,
            DenFileKinds.Outline or DenFileKinds.Book => structure.Universes.SelectMany(u => u.Books).FirstOrDefault(b => b.Path == DenPaths.Parent(path)),
            _ => null,
        };
        if (book is { Status: BookStatus.Final })
        {
            return $"{path} is in book '{book.Key}', which is final (frozen), so it can't be edited until the book is unfrozen (F-BOOK-1). {Unfreezing(book)}";
        }

        // Left out, so the book can't be unfrozen. A freeze promised this text won't change, and that's for the writer to break.
        if (kind == DenFileKinds.Book && book is null)
        {
            if (ParsedFrontmatter(text) is not { } frontmatter)
            {
                // Without the word, or an escape that could spell it, it can't say final.
                return Head(text).Contains("final", StringComparison.OrdinalIgnoreCase) || Head(text).Contains('\\', StringComparison.Ordinal)
                    ? $"{path} has frontmatter that doesn't parse and might say the book is final (frozen), so Wolfden doesn't change it here. Open it in your own editor to fix it."
                    : null;
            }

            if (SaysFinal(frontmatter))
            {
                return $"{path} says the book is final (frozen), but has an error, so the book can't be unfrozen here. Open it in your own editor to fix it.";
            }
        }

        // What it sets for the books below can't be compared, and a final book might inherit it.
        if (kind is DenFileKinds.Series or DenFileKinds.Universe && ParsedFrontmatter(text) is null && SuspectBooksBelow(den, structure, kind, path) is { Count: > 0 } suspect)
        {
            return $"{path} has frontmatter that doesn't parse, so Wolfden can't tell what it changes for these books, which are final or can't be read: {string.Join(", ", suspect)}. Open it in your own editor to fix it.";
        }

        return null;
    }

    /// <summary>
    /// A book becomes final by Freeze, which checkpoints it, not by a word in
    /// its file, and its file must parse here, or the next save couldn't tell
    /// whether it's final. A series or universe that loads keeps its frontmatter
    /// while a final book that loads is below it. And none, loaded or not, changes
    /// what such a book depends on (<see cref="WhatFinalBooksDependOn"/>) while a book
    /// below says final or can't be read, as <c>update_chapter_defaults</c> refuses
    /// (<see cref="CheckUnloadedBooksBelow"/>), or saves frontmatter that doesn't
    /// parse, since the next save couldn't compare it.
    /// </summary>
    private void CheckFinalBooksAbove(string kind, string path, IDenSource den, DenStructure structure, string before, string after)
    {
        var parsedAfter = ParsedFrontmatter(after);
        if (kind == DenFileKinds.Book)
        {
            if (parsedAfter is null)
            {
                throw new DenToolException(
                    ToolErrorCodes.UnsupportedEdit,
                    $"The frontmatter of {path} wouldn't parse, so nothing could tell whether the book is final. Nothing was written. Fix it, and save again.");
            }

            if (SaysFinal(parsedAfter))
            {
                throw new DenToolException(
                    ToolErrorCodes.UnsupportedEdit,
                    $"{path} would mark the book final. Take 'status: final' out and save; Freeze… on the book's page makes it final, with a checkpoint.");
            }
        }

        if (kind is not (DenFileKinds.Series or DenFileKinds.Universe))
        {
            return;
        }

        var folder = DenPaths.Parent(path) + "/";
        var finals = structure.Universes.SelectMany(u => u.Books).Where(b => b.Status == BookStatus.Final && b.Path.StartsWith(folder, StringComparison.Ordinal)).ToList();
        if (Head(before) != Head(after) && finals.Count > 0 && structure.Universes.SelectMany(u => new Container[] { u }.Concat(u.Series)).Any(c => c.FilePath == path))
        {
            throw new DenToolException(
                ToolErrorCodes.Frozen,
                $"{path} holds a final book, so its frontmatter can't change here, though the text below it can. {Unfreezing(finals)}");
        }

        var suspect = SuspectBooksBelow(den, structure, kind, path);
        if (suspect.Count == 0)
        {
            return;
        }

        var books = string.Join(", ", suspect);
        if (parsedAfter is null)
        {
            throw new DenToolException(
                ToolErrorCodes.UnsupportedEdit,
                $"The frontmatter of {path} wouldn't parse, so nothing could tell what it changes for these books, which are final or can't be read: {books}. Nothing was written. Fix it, and save again.");
        }

        // FileFrozen has sent one that doesn't parse now to the writer's editor.
        var parsedBefore = ParsedFrontmatter(before)!;
        foreach (var key in WhatFinalBooksDependOn)
        {
            if (Compared(parsedBefore[key], key) != Compared(parsedAfter[key], key))
            {
                throw new DenToolException(
                    ToolErrorCodes.Frozen,
                    $"{path} would change its {key}, which decides what these books inherit, how they're headed, or where they come in the story, and they're final or can't be read: {books}. Nothing was written. Put {key} back to save the rest (F-BOOK-1).");
            }
        }

        string Compared(Wolfden.Core.Yaml.YamlNode? node, string key)
        {
            if (key is not ("books" or "order"))
            {
                return JsonOf(node).GetRawText();
            }

            // The order is what counts, so fixing a repeated key (F-UNI-2) is no change.
            return node switch
            {
                null or Wolfden.Core.Yaml.YamlScalar { IsNull: true } => "",
                Wolfden.Core.Yaml.YamlSequence list => string.Join("\n", list.Items.Select(i => JsonOf(i).GetRawText()).Distinct(StringComparer.Ordinal)),
                _ => throw new DenToolException(
                    ToolErrorCodes.Frozen,
                    $"{key} in {path} isn't a list, so Wolfden can't tell what a change means for these books, which are final or can't be read: {books}. Open it in your own editor to fix it."),
            };
        }
    }

    /// <summary>What a series' or universe's frontmatter decides for a book that's final: what it inherits, how its chapters are headed, and where it comes in the story, and so what a reader has met at its positions.</summary>
    private static readonly string[] WhatFinalBooksDependOn = ["chapter_defaults", "chapter_heading", "books", "order"];

    /// <summary>The <c>book.md</c> files below a series or universe that are final, or say so, or can't be read to tell.</summary>
    private static List<string> SuspectBooksBelow(IDenSource den, DenStructure structure, string kind, string path)
    {
        var folder = DenPaths.Parent(path);
        var isUniverse = kind == DenFileKinds.Universe;
        var (suspect, _) = UnloadedBooks(structure, FilesBelow(den, folder, isUniverse));
        return structure.Universes.SelectMany(u => u.Books).Where(b => b.Status == BookStatus.Final && b.Path.StartsWith(folder + "/", StringComparison.Ordinal)).Select(b => b.FilePath)
            .Concat(suspect)
            .Concat(FoldersBelow(den, folder, isUniverse).Select(f => $"{f}/book.md").Where(p => den.FileExists(p) && den.TryReadFile(p) is null))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>True if the status isn't one that says draft: final in any spelling, or a value that isn't text. A typo stays a typo.</summary>
    private static bool SaysFinal(Wolfden.Core.Yaml.YamlMapping frontmatter) => frontmatter["status"] switch
    {
        null or Wolfden.Core.Yaml.YamlScalar { IsNull: true } => false,
        Wolfden.Core.Yaml.YamlScalar status => status.Text.Trim().Equals("final", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    /// <summary>The frontmatter as the den reads it, or null if it doesn't parse.</summary>
    private static Wolfden.Core.Yaml.YamlMapping? ParsedFrontmatter(string text)
    {
        var file = MarkdownFile.Parse(Encoding.UTF8.GetBytes(text), requireFrontmatter: true);
        return FrontmatterHid(file.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList()) ? null : file.Frontmatter;
    }

    /// <summary>
    /// Every id in the den but <paramref name="path"/>'s (F-ID-3): those in the
    /// index, and those written in the notes and containers' files it doesn't have,
    /// which come back with them, including those under a container left out.
    /// </summary>
    private HashSet<string> IdsInUse(IDenSource den, string path)
    {
        var (ids, known) = WithIndex(index => (
            index.Context.Entities.Where(e => e.Ulid != null).Select(e => e.Ulid!).ToList()
                .Concat(index.Context.Containers.Where(c => c.Ulid != null).Select(c => c.Ulid!))
                .ToHashSet(StringComparer.Ordinal),
            index.Context.Entities.Select(e => e.Path).ToList()
                .Concat(index.Context.Files.Where(f => f.IsLoaded).Select(f => f.Path))
                .ToHashSet(StringComparer.Ordinal)));
        var folders = new Stack<string>(["universes"]);
        while (folders.Count > 0)
        {
            var folder = folders.Pop();
            foreach (var name in den.GetDirectories(folder).Where(n => !n.StartsWith('.')))
            {
                folders.Push($"{folder}/{name}");
            }

            foreach (var other in den.GetFiles(folder).Where(n => !n.StartsWith('.')).Select(n => $"{folder}/{n}"))
            {
                if (other != path && !known.Contains(other) && DenErrors.CanEdit(DenErrors.KindOf(other), null) && DenErrors.KindOf(other) != DenFileKinds.Outline
                    && den.TryReadFile(other) is { } bytes && Parsed(DenErrors.KindOf(other), bytes) is { IsReadable: true } file && WrittenId(Lf(file.Text)) is { } id)
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    /// <summary>An id never changes (F-ID-4), and two files can't share one (F-ID-3).</summary>
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

    /// <summary>The file's id: from its frontmatter parsed, or if that doesn't parse, its line as text, so an id survives a file being broken.</summary>
    private static string? WrittenId(string text)
    {
        if (ParsedFrontmatter(text) is { } frontmatter)
        {
            return frontmatter["id"] is Wolfden.Core.Yaml.YamlScalar { IsNull: false } id ? id.Text.Trim() : null;
        }

        // The top level's, if one is; broken YAML can leave it indented.
        var lines = WrittenIdLine().Matches(Head(text));
        return (lines.FirstOrDefault(m => m.Groups[1].Length == 0) ?? lines.FirstOrDefault())?.Groups[2].Value;
    }

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

    [GeneratedRegex(@"^([ \t]*)[""']?id[""']?[ \t]*:[ \t]*[""']?([^\s""'#]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex WrittenIdLine();
}

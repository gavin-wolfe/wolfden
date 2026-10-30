using System.Globalization;
using Wolfden.Core.Editing;
using Wolfden.Core.Structure;
using Wolfden.Core.Yaml;
using Wolfden.Index.Reading;

namespace Wolfden.Index.Writing;

/// <summary>
/// Adding a chapter to a book (#215), for the writer, from the web UI's
/// <b>Add chapter</b> and <b>Insert chapter…</b> and from
/// <c>wolfden new chapter</c>. There's no MCP tool for it yet (out of scope
/// for #215).
/// </summary>
/// <remarks>
/// <para>
/// The new item goes in the book's <c>chapters</c> (format.md F-BOOK-2)
/// through the lossless list edit (<c>WithNewListItem</c>), the rest of
/// <c>book.md</c> kept: its comments, block or flow style, the quoting of
/// the item before it, and every other key. Its key and number, and the
/// numbers after it that change, are <see cref="NewChapter.Plan"/>'s; each
/// changed number is a scalar edit in place, and no key ever changes
/// (F-BOOK-6). A book with no chapters gets <c>chapters:</c> with one
/// <c>- { key: "1", number: 1 }</c> item, as a new book's are written
/// (<see cref="NewContainer"/>).
/// </para>
/// <para>
/// The write rules are the other <c>book.md</c> writes': an expected hash
/// (the web UI's; the CLI reads and writes in one go and passes none), a
/// check of the file as the den reads it, an id for a <c>book.md</c> that
/// has none (F-ID-6), and nothing in a final book (F-BOOK-1).
/// </para>
/// </remarks>
public sealed partial class DenTools
{
    /// <summary>
    /// Adds a chapter to <paramref name="book"/>: at the end, or after the
    /// chapter <paramref name="after"/>, with an optional title.
    /// </summary>
    /// <param name="book">The book's key.</param>
    /// <param name="title">The chapter's title, one line of at most 200 characters, or null (or blank) for none.</param>
    /// <param name="after">The key of the chapter it comes after, or null for the end.</param>
    /// <param name="expectedHash">The hash of the <c>book.md</c> the writer saw, or null to take it as it is now.</param>
    /// <param name="universe">The universe, when the den has several.</param>
    public ChapterAddResult AddChapter(string book, string? title = null, string? after = null, string? expectedHash = null, string? universe = null)
    {
        if (string.IsNullOrWhiteSpace(book))
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, "Give the key of the book the chapter goes in.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = null;
        }
        else if (NewEntity.NameProblem(title) is { } problem)
        {
            throw new DenToolException(
                ToolErrorCodes.BadArgument, $"Give the chapter a title of one line, at most {NewEntity.MaxNameLength} characters, or none. That one can't be used: {problem}.");
        }

        title = title?.Trim();
        after = string.IsNullOrWhiteSpace(after) ? null : after.Trim();
        var hash = expectedHash is null ? null : RequireHash(expectedHash);
        var key = book.Trim();
        var (universeKey, _) = LocateBook(key, universe);
        using (EnterWrite())
        {
            var den = Snapshot();
            var structure = DenStructure.Load(den);
            var target = FindBook(structure, universeKey, key);
            var path = target.FilePath;
            var original = target.File.ToBytes();
            if (hash is not null)
            {
                CheckHash(path, original, hash);
            }

            if (target.Status == BookStatus.Final)
            {
                throw new DenToolException(
                    ToolErrorCodes.Frozen, $"Book '{target.Key}' is final (frozen), so no chapter can be added to it until it's unfrozen (F-BOOK-1). {Unfreezing(target)}");
            }

            if (after is not null)
            {
                if (target.Chapters.Count == 0)
                {
                    throw new DenToolException(
                        ToolErrorCodes.UnknownChapter, $"Book '{target.Key}' has no chapters yet, so there's no chapter '{after}' to add one after. Leave that out to add its first.");
                }

                FindChapter(target, after);
            }

            var slots = target.Chapters.Select(c => new ChapterSlot(c.Key, c.Number)).ToList();
            var plan = NewChapter.Plan(slots, after) ?? throw new DenToolException(
                ToolErrorCodes.BadArgument,
                $"A chapter after '{after}' would need a key longer than {Keys.MaxLength} characters (F-KEY-1). Add it after another chapter, or at the end.");

            var file = target.File;
            var items = (file.GetFrontmatterValue("chapters") as YamlSequence)?.Items ?? [];
            var like = items.Count == 0 ? null : items[Math.Max(plan.Index - 1, 0)];
            var item = NewChapter.Item(plan.Key, plan.Number, title, bare: like is YamlScalar);
            if (file.GetFrontmatterValue("chapters") is YamlSequence)
            {
                var before = file;
                file = Apply(() => before.WithNewListItem(["chapters"], item, plan.Index < items.Count ? plan.Index : null));
            }
            else
            {
                // A missing or null chapters: written as a new book's are, one `- { key: "1", number: 1 }` line per item.
                var list = new YamlSequenceValue { item };
                var before = file;
                file = Apply(() => before.WithFrontmatterValue(["chapters"], list));
            }

            foreach (var change in plan.Renumbered)
            {
                var current = file;
                file = Apply(() => current.WithFrontmatterValue(
                    ["chapters", change.Index.ToString(CultureInfo.InvariantCulture), "number"], YamlScalarValue.Integer(change.To)));
            }

            var idAdded = target.Id is null ? AddId(ref file) : null;
            var bytes = file.ToBytes();
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = bytes };
            var check = Check(den, structure, files);

            // It reads back as planned: the same keys, with the new one in its place, and only the planned numbers changed.
            var made = check.Structure.FindUniverse(universeKey)?.Books.FirstOrDefault(b => b.FilePath == path);
            var expected = slots.Select(s => (s.Key, s.Number, Title: target.FindChapter(s.Key)!.Title)).ToList();
            expected.Insert(plan.Index, (plan.Key, plan.Number, title));
            foreach (var change in plan.Renumbered)
            {
                expected[change.Index] = expected[change.Index] with { Number = change.To };
            }

            if (made is null || !made.Chapters.Select(c => (c.Key, c.Number, c.Title)).SequenceEqual(expected))
            {
                throw new DenToolException(
                    ToolErrorCodes.UnsupportedEdit, $"The new chapter didn't read back as built, so book.md wasn't changed. {ToolWords.Cap(Words.CheckTool)} to see why.");
            }

            Save(files, new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = original }, created: null);

            var chapter = made.Chapters[plan.Index];
            var effects = new List<string> { AddedEffect(made, chapter, plan, after) };
            if (plan.Renumbered.Count > 0)
            {
                static string Change(Renumbering r) => $"'{r.Key}' {r.From.ToString(CultureInfo.InvariantCulture)} → {r.To.ToString(CultureInfo.InvariantCulture)}";
                var changes = plan.Renumbered.Count <= 5
                    ? string.Join(", ", plan.Renumbered.Select(Change))
                    : $"{string.Join(", ", plan.Renumbered.Take(2).Select(Change))}, … {Change(plan.Renumbered[^1])} ({plan.Renumbered.Count.ToString(CultureInfo.InvariantCulture)} chapters)";
                effects.Add($"Renumbered the chapters after it, keys unchanged, so positions still name the same chapters: {changes}.");
            }

            if (idAdded is not null)
            {
                effects.Add($"{path} had no id, so it was given one: {idAdded} (F-ID-6).");
            }

            effects.Add($"Book '{made.Key}' now has these chapters, in order: {string.Join(", ", made.Chapters.Select(c => c.Key))}.");
            return new ChapterAddResult(
                DenReader.ResultVersion, universeKey, made.Key, path, HashOf(bytes), chapter.Key, $"{made.Key}:{chapter.Key}", chapter.Number, chapter.Title,
                plan.Index, plan.Renumbered.Select(r => new RenumberedView(r.Key, r.From, r.To)).ToList(), effects, Warnings(check),
                [new WrittenFileView(path, HashOf(bytes), Created: false)], idAdded);
        }
    }

    /// <summary>What adding the chapter did, in words: its key, where it went, and its number and title.</summary>
    private static string AddedEffect(Book book, Chapter chapter, ChapterPlan plan, string? after)
    {
        var place = plan.Index == 0 && book.Chapters.Count == 1
            ? "its first chapter"
            : plan.Index == book.Chapters.Count - 1
                ? $"at the end, after '{book.Chapters[plan.Index - 1].Key}'"
                : $"after '{after}'";
        var number = chapter.Number is { } n
            ? $"number {n.ToString(CultureInfo.InvariantCulture)}"
            : "no number, as the book's other chapters have none";
        var titled = chapter.Title is null ? "no title" : $"the title '{chapter.Title}'";
        return $"Added chapter '{chapter.Key}' to book '{book.Key}', {place}, with {number} and {titled}. Its position is {book.Key}:{chapter.Key}, and its key never changes.";
    }
}

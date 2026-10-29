using System.Globalization;
using Wolfden.Core.Documents;
using Wolfden.Core.Editing;
using Wolfden.Core.Structure;
using Wolfden.Core.Yaml;
using Wolfden.Index.Reading;

namespace Wolfden.Index.Writing;

/// <summary>
/// Creating series and books (#213), for the writer, from the web UI and
/// the CLI. There are no MCP tools for them yet (out of scope for #213).
/// </summary>
/// <remarks>
/// <para>
/// A create writes the new container's folder and its file
/// (<c>series.md</c> or <c>book.md</c>, format.md §6.3, §6.4): a new id, the
/// name, and for a book its chapters, keys <c>"1"</c> to <c>"N"</c> with
/// <c>number</c> 1 to N (maintainer decision on #213, 2026-10-29). Its key,
/// the folder's name, is made from the name as a note's slug is (F-TPL-6),
/// but a key that's taken is refused rather than given <c>-2</c>, since
/// positions name a book by its key (F-KEY-5).
/// </para>
/// <para>
/// It's placed in story order by its container's list, edited in place,
/// the rest of the file kept: a series' <c>books</c> (F-SER-1), or for a
/// new series or a standalone book, the universe's <c>order</c> (F-UNI-1).
/// It goes at the end, or after the work the writer names (maintainer
/// decision on #213, 2026-10-29). A container with no list and one work
/// gets one, with that work first (F-UNI-3, F-SER-3), so <c>check</c>
/// stays quiet; one with no list and no works needs none. Works a list
/// leaves out, which come after the listed ones by key, are added to it in
/// that order first, so their place in story order doesn't change and the
/// new work goes where it was asked to (design choice, #213). A list that
/// already names the key, a work planned ahead, keeps that place.
/// </para>
/// <para>
/// Containers aren't link targets (format.md F-LINK-5), so a new series or
/// book never changes which note a link reaches, and there's no
/// <c>allow_relinks</c> here (design choice, #213). Nothing is written in a
/// final book's scope (F-BOOK-1): a new container is never in one, and its
/// universe's or series' file isn't either, even when every book of the
/// series is final, so in practice a freeze never refuses a create; the
/// check is kept as a backstop.
/// </para>
/// </remarks>
public sealed partial class DenTools
{
    /// <summary>The most chapters a new book can start with: room for any book, while a slip of the keyboard stays an error.</summary>
    public const int MaxNewChapters = 1000;

    /// <summary>
    /// The works a new series or book can be placed after, in story order:
    /// the universe's series and standalone books, or one series' books,
    /// with the hash of the file that lists them (<c>universe.md</c> or
    /// <c>series.md</c>) to pass back with the create.
    /// </summary>
    public WorksView GetWorks(string? universe = null, string? series = null)
    {
        var (universeKey, _) = PlaceFor(universe, series);
        var holder = Holder(DenStructure.Load(Snapshot()), universeKey, series);
        var works = holder is Universe u
            ? u.Works.Select(w => new WorkView(w.Kind == ContainerKind.Series ? "series" : "book", w.Key, w.Name))
            : ((Series)holder).Books.Select(b => new WorkView("book", b.Key, b.Name));
        return new WorksView(DenReader.ResultVersion, universeKey, series?.Trim(), holder.Name, holder.FilePath, HashOf(holder.File.ToBytes()), works.ToList());
    }

    /// <summary>Creates a series in the universe, placed in its <c>order</c> at the end or after <paramref name="after"/>.</summary>
    /// <param name="name">The series' name; its key is made from it.</param>
    /// <param name="universe">The universe, when the den has several.</param>
    /// <param name="after">The key of the series or standalone book it comes after, or null for the end.</param>
    /// <param name="expectedHash">The hash of the <c>universe.md</c> the writer saw, or null to take it as it is now.</param>
    /// <param name="first">True to place it at the start of the story order, before every work; not with <paramref name="after"/>.</param>
    public ContainerCreateResult CreateSeries(string name, string? universe = null, string? after = null, string? expectedHash = null, bool first = false) =>
        CreateContainer(ContainerKind.Series, name, universe, null, after, null, expectedHash, first);

    /// <summary>
    /// Creates a book: in a series, placed in its <c>books</c>, or without
    /// one, a standalone book of the universe, placed in its <c>order</c>.
    /// </summary>
    /// <param name="name">The book's name; its key is made from it.</param>
    /// <param name="universe">The universe, when the den has several.</param>
    /// <param name="series">The series it's in, or null for a standalone book.</param>
    /// <param name="after">The key of the work it comes after, or null for the end.</param>
    /// <param name="chapters">How many chapters it starts with, keys 1 to N, or null for none.</param>
    /// <param name="expectedHash">The hash of the <c>series.md</c> or <c>universe.md</c> the writer saw, or null to take it as it is now.</param>
    /// <param name="first">True to place it at the start of the story order, before every work; not with <paramref name="after"/>.</param>
    public ContainerCreateResult CreateBook(
        string name, string? universe = null, string? series = null, string? after = null, int? chapters = null, string? expectedHash = null,
        bool first = false) =>
        CreateContainer(ContainerKind.Book, name, universe, series, after, chapters, expectedHash, first);

    private ContainerCreateResult CreateContainer(
        ContainerKind kind, string name, string? universe, string? series, string? after, int? chapters, string? expectedHash, bool first)
    {
        var what = kind == ContainerKind.Series ? "series" : "book";
        if (first && !string.IsNullOrWhiteSpace(after))
        {
            throw new DenToolException(
                ToolErrorCodes.BadArgument, $"Place the new {what} at the start or after a work, not both: give one, or neither for the end.");
        }

        if (NewEntity.NameProblem(name) is { } problem)
        {
            throw new DenToolException(
                ToolErrorCodes.BadArgument, $"Give the {what} a name of one line, at most {NewEntity.MaxNameLength} characters. That one can't be used: {problem}.");
        }

        if (chapters is < 1 or > MaxNewChapters)
        {
            throw new DenToolException(
                ToolErrorCodes.BadArgument,
                $"A new book starts with 1 to {MaxNewChapters} chapters, not {chapters.Value.ToString(CultureInfo.InvariantCulture)}. Leave the number out for a book with no chapters yet.");
        }

        if (string.IsNullOrWhiteSpace(series))
        {
            series = null;
        }

        name = name.Trim();
        after = string.IsNullOrWhiteSpace(after) ? null : after.Trim();
        var (universeKey, ids) = PlaceFor(universe, series);
        lock (_writeLock)
        {
            var den = Snapshot();
            var structure = DenStructure.Load(den);
            var holder = Holder(structure, universeKey, series);
            var u = holder.Universe;
            var original = holder.File.ToBytes();
            if (expectedHash is not null)
            {
                CheckHash(holder.FilePath, original, RequireHash(expectedHash));
            }

            var key = NewEntity.Slugify(name);
            if (!Keys.IsContainerKey(key))
            {
                var afterwards = $"you can change the name in its {what}.md afterwards, and the key stays";
                throw new DenToolException(
                    ToolErrorCodes.BadArgument,
                    key.Length > 0
                        ? $"A {what}'s key, its folder's name, is made from its name, and must start with a letter (F-KEY-2), so '{name}' can't be used: it would be '{key}'. " +
                          $"Start the name with a word, such as 'Book {name}'; {afterwards}."
                        : name.Any(char.IsLetterOrDigit)
                            ? $"A {what}'s key, its folder's name, is made from its name, and keys use only the letters a–z and the digits 0–9 for now, so '{name}' gives no key. " +
                              $"Give it a name in Latin letters, such as a transliteration; {afterwards}, so it can be '{name}' again."
                            : $"A {what}'s key, its folder's name, is made from its name, and '{name}' has no letters or digits to make one from. Give it a name with letters a–z in it; {afterwards}.");
            }

            CheckNewKey(den, u, kind, holder, key, what);
            var folder = kind == ContainerKind.Series ? $"{u.Path}/series/{key}" : $"{holder.Path}/books/{key}";
            var path = $"{folder}/{what}.md";
            var hadParent = den.GetDirectories(DenPaths.Parent(DenPaths.Parent(folder))).Contains(DenPaths.Name(DenPaths.Parent(folder)), StringComparer.Ordinal);
            if (DenPaths.Problem(path) is { } pathProblem)
            {
                throw new DenToolException(ToolErrorCodes.BadArgument, $"'{name}' can't be a {what}'s folder name ('{key}'): {pathProblem}. Give it another name.");
            }

            var id = NewUnusedId(ids);
            ids.Add(id);
            var bytes = Apply(() => NewContainer.File(id, name, kind == ContainerKind.Book ? chapters ?? 0 : 0)).ToBytes();

            var effects = new List<string>
            {
                $"Created {path}: {what} '{name}', key '{key}', with id {id}." + (kind == ContainerKind.Series
                    ? " It has no books yet."
                    : chapters is { } n
                        ? $" It has {(n == 1 ? "1 chapter" : $"{n.ToString(CultureInfo.InvariantCulture)} chapters")}, {key}:1{(n == 1 ? "" : $" to {key}:{n.ToString(CultureInfo.InvariantCulture)}")}."
                        : $" It has no chapters yet, so the only position in it is the bare '{key}', its start (F-POS-7)."),
            };

            var (listed, placed) = Placed(holder, key, kind, after, first);
            effects.Add(placed);
            string? idAdded = null;
            if (listed is not null && holder.Id is null)
            {
                var file = listed;
                idAdded = AddId(ref file, ids);
                listed = file;
                if (idAdded is not null)
                {
                    effects.Add($"{holder.FilePath} had no id, so it was given one: {idAdded} (F-ID-6).");
                }
            }

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = bytes };
            if (listed is not null)
            {
                files[holder.FilePath] = listed.ToBytes();
            }

            // A backstop: no container file of a final book is written (F-BOOK-1). None of these ever is.
            if (structure.Universes.SelectMany(x => x.Books).FirstOrDefault(b => b.Status == BookStatus.Final && files.Keys.Any(p => IsAtOrUnder(p, b.Path))) is { } frozen)
            {
                throw new DenToolException(
                    ToolErrorCodes.Frozen, $"Book '{frozen.Key}' is final (frozen), so nothing can be added to it until it's unfrozen (F-BOOK-1).");
            }

            var check = Check(den, structure, files);
            var made = check.Structure.FindUniverse(u.Key) is { } then
                ? kind == ContainerKind.Series ? then.Series.FirstOrDefault(s => s.Key == key) as Container : then.FindBook(key)
                : null;
            if (made is null || made.Name != name || made.Id != id || (made is Book b2 && b2.Chapters.Count != (chapters ?? 0)))
            {
                throw new DenToolException(ToolErrorCodes.UnsupportedEdit, $"The new {what} didn't read back as built, so it wasn't made. {ToolWords.Cap(Words.CheckTool)} to see why.");
            }

            try
            {
                Save(files, listed is null ? null : new Dictionary<string, byte[]>(StringComparer.Ordinal) { [holder.FilePath] = original }, created: path);
            }
            catch (DenToolException)
            {
                // The new file was put back (deleted); its empty folder would be a container with no file (F-LAY-4),
                // and the books/ or series/ folder above it goes too if this create made it.
                RemoveEmptyFolder(folder);
                if (!hadParent)
                {
                    RemoveEmptyFolder(DenPaths.Parent(folder));
                }

                throw;
            }

            effects.Add(made is Book { Series: { } inSeries }
                ? $"Series '{inSeries.Key}' now has these books, in story order: {string.Join(", ", inSeries.Books.Select(x => x.Key))}."
                : $"Universe '{u.Key}' now has these series and standalone books, in story order: {string.Join(", ", made.Universe.Works.Select(w => w.Key))}.");
            return new ContainerCreateResult(
                DenReader.ResultVersion, u.Key, what, key, name, made is Book { Series: { } parent } ? parent.Key : null, path, HashOf(bytes), id,
                made is Book book ? book.Chapters.Count : 0, effects, Warnings(check),
                files.Select(f => new WrittenFileView(f.Key, HashOf(f.Value), Created: f.Key == path)).OrderBy(f => f.Path, StringComparer.Ordinal).ToList(),
                idAdded);
        }
    }

    /// <summary>The universe and the den's ids, after checking that the universe (and the series, if one is named) is there.</summary>
    private (string Universe, HashSet<string> Ids) PlaceFor(string? universe, string? series) => WithIndex(index =>
    {
        var db = index.Context;
        var u = ChooseUniverse(db, universe, null, allowAll: false)!;
        CheckFilter(db, u, string.IsNullOrWhiteSpace(series) ? null : Filter(series.Trim(), null));

        // Ids are compared across the whole den, whatever universe the session reads.
        var used = db.Entities.Where(e => e.Ulid != null).Select(e => e.Ulid!).ToList()
            .Concat(db.Containers.Where(c => c.Ulid != null).Select(c => c.Ulid!))
            .ToHashSet(StringComparer.Ordinal);
        return (u.Key, used);
    });

    /// <summary>The universe, or its series <paramref name="series"/>, as the files say now.</summary>
    private static Container Holder(DenStructure structure, string universe, string? series)
    {
        var u = structure.FindUniverse(universe) ?? throw Gone($"universes/{universe}");
        if (string.IsNullOrWhiteSpace(series))
        {
            return u;
        }

        var key = series.Trim();
        return u.Series.SingleOrDefault(s => s.Key == key)
            ?? throw new DenToolException(ToolErrorCodes.UnknownScope, $"Universe '{u.Key}' has no series '{key}' now; it may have changed since it was read.");
    }

    /// <summary>
    /// Refuses a key that clashes with a series or book of the universe
    /// (F-KEY-5), or with a folder already there, compared ignoring case,
    /// as Windows and macOS compare names. Folders are listed, not the
    /// loaded works, so one that doesn't load counts too.
    /// </summary>
    private static void CheckNewKey(IDenSource den, Universe u, ContainerKind kind, Container holder, string key, string what)
    {
        var again = $" Give it another name: its key, the folder's name, is made from the name. You can change the name in its {what}.md afterwards, and the key stays.";
        string? Folder(string parent) => den.GetDirectories(parent).FirstOrDefault(d => string.Equals(d, key, StringComparison.OrdinalIgnoreCase)) is { } d ? $"{parent}/{d}" : null;
        var seriesFolders = den.GetDirectories($"{u.Path}/series").ToList();
        var asSeries = Folder($"{u.Path}/series");
        var standalone = Folder($"{u.Path}/books");
        var inSeries = seriesFolders.Select(s => Folder($"{u.Path}/series/{s}/books")).OfType<string>().FirstOrDefault();
        if (kind == ContainerKind.Series)
        {
            if (asSeries is not null)
            {
                throw new DenToolException(ToolErrorCodes.AlreadyExists, $"Universe '{u.Key}' already has a series '{key}' ({asSeries}).{again}");
            }

            if (standalone is not null)
            {
                throw new DenToolException(
                    ToolErrorCodes.AlreadyExists,
                    $"Universe '{u.Key}' has a standalone book '{key}' ({standalone}), and a series' key can't be a standalone book's, since the universe's order lists both (F-KEY-5).{again}");
            }

            return;
        }

        if ((standalone ?? inSeries) is { } book)
        {
            throw new DenToolException(
                ToolErrorCodes.AlreadyExists,
                $"Universe '{u.Key}' already has a book '{key}' ({book}). Book keys are unique in a universe, since positions name a book without its series (F-KEY-5).{again}");
        }

        if (holder is Universe && asSeries is not null)
        {
            throw new DenToolException(
                ToolErrorCodes.AlreadyExists,
                $"Universe '{u.Key}' has a series '{key}' ({asSeries}), and a standalone book's key can't be a series', since the universe's order lists both (F-KEY-5).{again}");
        }
    }

    /// <summary>
    /// The container's file with <paramref name="key"/> in its list
    /// (<c>order</c> or <c>books</c>), at the end, at the start
    /// (<paramref name="first"/>), or after <paramref name="after"/>, and
    /// what that did, in words; or a null file if the file needn't change.
    /// </summary>
    private static (MarkdownFile? File, string Effect) Placed(Container holder, string key, ContainerKind kind, string? after, bool first)
    {
        var placedAt = first ? "at the start" : after is null ? "at the end" : $"after '{after}'";
        var field = holder is Universe ? "order" : "books";
        var where = holder is Universe ? $"universe '{holder.Key}'" : $"series '{holder.Key}'";
        var works = holder is Universe u ? u.Works.Select(w => w.Key).ToList() : ((Series)holder).Books.Select(b => b.Key).ToList();
        if (after is not null && !works.Contains(after, StringComparer.Ordinal))
        {
            var them = holder is Universe ? "series and standalone books" : "books";
            throw new DenToolException(
                ToolErrorCodes.BadArgument,
                $"{ToolWords.Cap(where)} has no {(holder is Universe ? "series or standalone book" : "book")} '{after}' to place the new {(kind == ContainerKind.Series ? "series" : "book")} after." +
                (works.Count == 0 ? $" It has no {them} yet, so leave that out." : $" Its {them}, in story order: {string.Join(", ", works)}."));
        }

        var node = holder.File.Frontmatter?[field];
        if (node is YamlSequence list)
        {
            var entries = list.Items.OfType<YamlScalar>().Where(s => !s.IsNull).Select(s => s.Text).ToList();
            var planned = entries.Contains(key, StringComparer.Ordinal);
            if (planned && (after is not null || first))
            {
                throw new DenToolException(
                    ToolErrorCodes.BadArgument,
                    $"{field} in {holder.FilePath} already lists '{key}', so the new {(kind == ContainerKind.Series ? "series" : "book")} takes that place. Leave out where to place it, or change {field} first.");
            }

            // Works the list leaves out come after the listed ones, by key (F-UNI-3, F-SER-3): listed in that order, their place is the same.
            var file = holder.File;
            var missing = works.Where(w => !entries.Contains(w, StringComparer.Ordinal)).ToList();
            foreach (var work in missing)
            {
                var current = file;
                file = Apply(() => current.WithNewListItem([field], YamlScalarValue.String(work)));
            }

            var filled = missing.Count == 0 ? "" : $" It left out {string.Join(", ", missing)}, which came after the listed ones, by key; they're listed there now, so their place in story order is the same.";
            if (planned)
            {
                return (missing.Count == 0 ? null : file, $"{field} in {holder.FilePath} already listed '{key}', so it takes that place in story order.{filled}");
            }

            int? index = first ? 0 : null;
            if (after is not null)
            {
                var items = ((YamlSequence)file.Frontmatter![field]!).Items;
                var at = items.ToList().FindIndex(i => i is YamlScalar { IsNull: false } s && s.Text == after) + 1;
                index = at < items.Count ? at : null;
            }

            var before = file;
            file = Apply(() => before.WithNewListItem([field], YamlScalarValue.String(key), index is 0 && ((YamlSequence)before.Frontmatter![field]!).Items.Count == 0 ? null : index));
            return (file, $"Added '{key}' to {field} in {holder.FilePath}, {placedAt}.{filled}");
        }

        if (node is not (null or YamlScalar { IsNull: true }))
        {
            // Not a list is an error, so the container wouldn't have loaded; kept for safety.
            throw new DenToolException(ToolErrorCodes.UnsupportedEdit, $"{field} in {holder.FilePath} isn't a list, so the new work can't be added to it. Fix it by hand first.");
        }

        if (works.Count == 0)
        {
            return (null, $"It's the only {(holder is Universe ? "work" : "book")} in {where}, so {holder.FilePath} needs no {field} list (F-{(holder is Universe ? "UNI" : "SER")}-3).");
        }

        var keys = works.ToList();
        keys.Insert(first ? 0 : after is null ? keys.Count : keys.IndexOf(after) + 1, key);
        var value = new YamlSequenceValue();
        foreach (var k in keys)
        {
            value.Add(YamlScalarValue.String(k));
        }

        var made = Apply(() => holder.File.WithFrontmatterValue([field], value));
        return (made, $"{holder.FilePath} had no {field} list, which is needed once there's more than one {(holder is Universe ? "work" : "book")} (F-{(holder is Universe ? "UNI" : "SER")}-3), so one was written, with the new one {placedAt}: [{string.Join(", ", keys)}].");
    }

    /// <summary>Removes <paramref name="folder"/> if it's there and empty; a failure is left for the writer, with the error already reported.</summary>
    private void RemoveEmptyFolder(string folder)
    {
        try
        {
            if (Store.GetDirectories(DenPaths.Parent(folder)).Contains(DenPaths.Name(folder), StringComparer.Ordinal)
                && !Store.GetFiles(folder).Any() && !Store.GetDirectories(folder).Any())
            {
                Store.DeleteFolder(folder);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DenStoreException)
        {
            // Nothing more to do: the write's own error says what happened.
        }
    }
}

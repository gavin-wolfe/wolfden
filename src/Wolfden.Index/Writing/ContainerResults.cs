using System.Text.Json;

namespace Wolfden.Index.Writing;

// The shapes of creating series and books (#213), adding chapters
// (#215), and showing and editing chapter fields (#225): what the web API answers, as camelCase JSON, and what
// `wolfden new series`, `wolfden new book`, and `wolfden new chapter`
// print with --json. Within a version, members may be added, but never
// renamed, removed, or given a new meaning.

/// <summary>A series or book a new work can be placed after, in <see cref="WorksView"/>.</summary>
/// <param name="Kind"><c>series</c> or <c>book</c>.</param>
/// <param name="Key">Its key.</param>
/// <param name="Name">Its name.</param>
public sealed record WorkView(string Kind, string Key, string Name);

/// <summary>
/// The works of a universe (its series and standalone books) or of a
/// series (its books), in story order, for a form that places a new one
/// among them (#213).
/// </summary>
/// <param name="Version">The shape version.</param>
/// <param name="Universe">The universe's key.</param>
/// <param name="Series">The series' key, or null for the universe's works.</param>
/// <param name="Name">The universe's or series' name.</param>
/// <param name="Path">The file that lists them, <c>universe.md</c> or <c>series.md</c>, relative to the den root.</param>
/// <param name="Hash">That file's hash, to send back with the create: it's refused if the file has changed since.</param>
/// <param name="Works">The works, in story order.</param>
public sealed record WorksView(int Version, string Universe, string? Series, string Name, string Path, string Hash, IReadOnlyList<WorkView> Works);

/// <summary>What creating a series or book did (#213).</summary>
/// <param name="Version">The shape version.</param>
/// <param name="Universe">The universe's key.</param>
/// <param name="Kind"><c>series</c> or <c>book</c>.</param>
/// <param name="Key">The new container's key, its folder's name, made from the name.</param>
/// <param name="Name">Its name.</param>
/// <param name="Series">For a book in a series, the series' key; otherwise null.</param>
/// <param name="Path">Its new file, <c>series.md</c> or <c>book.md</c>, relative to the den root.</param>
/// <param name="Hash">That file's hash.</param>
/// <param name="Id">Its new id.</param>
/// <param name="Chapters">How many chapters it has (0 for a series, or a book with none yet).</param>
/// <param name="Effects">What the create did, in words: the file, where it goes in story order, and the list it was added to.</param>
/// <param name="Warnings">Warnings about the files written, as <c>check</c> reports them.</param>
/// <param name="Files">Every file written: the new one, and the universe's or series' file when its list changed.</param>
/// <param name="IdAdded">The id given to the universe's or series' file, if it had none (format.md F-ID-6), or null.</param>
public sealed record ContainerCreateResult(
    int Version, string Universe, string Kind, string Key, string Name, string? Series, string Path, string Hash, string Id, int Chapters,
    IReadOnlyList<string> Effects, IReadOnlyList<DiagnosticView> Warnings, IReadOnlyList<WrittenFileView> Files, string? IdAdded = null);

/// <summary>A chapter whose display number an added chapter changed (#215); its key didn't change (format.md F-BOOK-6).</summary>
/// <param name="Key">The chapter's key.</param>
/// <param name="From">Its number before.</param>
/// <param name="To">Its number now.</param>
public sealed record RenumberedView(string Key, long From, long To);

/// <summary>What adding a chapter did (#215).</summary>
/// <param name="Version">The shape version.</param>
/// <param name="Universe">The universe's key.</param>
/// <param name="Book">The book's key.</param>
/// <param name="Path">Its <c>book.md</c>, relative to the den root.</param>
/// <param name="Hash">That file's hash now, for the next edit.</param>
/// <param name="Key">The new chapter's key, which never changes.</param>
/// <param name="Position">Its position, <c>book:key</c>.</param>
/// <param name="Number">Its display number, or null for none.</param>
/// <param name="Title">Its title, or null.</param>
/// <param name="Index">Its place in the book's chapters, from 0.</param>
/// <param name="Renumbered">The chapters after it whose numbers changed, in book order.</param>
/// <param name="Effects">What the add did, in words.</param>
/// <param name="Warnings">Warnings about <c>book.md</c>, as <c>check</c> reports them.</param>
/// <param name="Files">The file written: <c>book.md</c>.</param>
/// <param name="IdAdded">The id given to <c>book.md</c>, if it had none (format.md F-ID-6), or null.</param>
public sealed record ChapterAddResult(
    int Version, string Universe, string Book, string Path, string Hash, string Key, string Position, long? Number, string? Title, int Index,
    IReadOnlyList<RenumberedView> Renumbered, IReadOnlyList<string> Effects, IReadOnlyList<DiagnosticView> Warnings, IReadOnlyList<WrittenFileView> Files,
    string? IdAdded = null);

/// <summary>
/// One chapter field of a chapter or of a container's <c>chapter_defaults</c>,
/// as the web UI shows and edits it (#225): what the file writes, what holds,
/// and what would hold without it (types.md F-CHAP-5). Values are as
/// written (<see cref="SourceField.Value"/>), so a form can send back the
/// ones the writer didn't touch.
/// </summary>
/// <param name="Name">The field name, such as <c>pov</c>.</param>
/// <param name="Set">True if the chapter's <c>fields</c> (or the container's <c>chapter_defaults</c>) writes the field, even with a value that's ignored.</param>
/// <param name="Own">The value it writes, as written, or null.</param>
/// <param name="Ignored">True if it writes the field, but the value is null or doesn't fit the field, so it's ignored and the value is inherited as if it weren't there (F-CHAP-3).</param>
/// <param name="Value">The value that holds (the effective value), as written, or null if nothing sets it.</param>
/// <param name="From">Where that comes from: <c>chapter</c> (a chapter's own), or <c>book</c>, <c>series</c>, or <c>universe</c> (that container's <c>chapter_defaults</c>), or null.</param>
/// <param name="FromKey">The key of the container whose <c>chapter_defaults</c> gives it, or null for a chapter's own value.</param>
/// <param name="None">True if the value that holds is an empty list, "none here", which stops inheritance.</param>
/// <param name="Default">What would hold without the field written here: the nearest <c>chapter_defaults</c> above that sets it, as written, or null.</param>
/// <param name="DefaultFrom">Where that comes from: <c>book</c>, <c>series</c>, or <c>universe</c>, or null.</param>
/// <param name="DefaultFromKey">The key of that container, or null.</param>
/// <param name="Links">The links in <paramref name="Value"/>, resolved from the container that holds it (F-CHAP-4).</param>
/// <param name="Text">
/// The lines of the file where an edit of the field lands, as written, with LF line breaks: a chapter's item in
/// <c>chapters</c> (its fields are written there), or the field's entry in a container's <c>chapter_defaults</c> (the
/// whole <c>chapter_defaults</c> when it doesn't set the field, or is written on one line). A page that finds the file
/// changed sends its changes again only if these are byte for byte as they were.
/// </param>
public sealed record ChapterValueView(
    string Name, bool Set, JsonElement? Own, bool Ignored, JsonElement? Value, string? From, string? FromKey, bool None, JsonElement? Default,
    string? DefaultFrom, string? DefaultFromKey, IReadOnlyList<ChapterLinkView> Links, string Text);

/// <summary>A link in a chapter field's value, resolved (format.md §10.2).</summary>
/// <param name="Text">The link as written, such as <c>[[mina-harker]]</c>.</param>
/// <param name="Target">Its target.</param>
/// <param name="Label">Its label, or null.</param>
/// <param name="Outcome"><c>resolved</c>, <c>ambiguous</c>, or <c>broken</c>.</param>
/// <param name="Entity">The key of the entity it reaches (its id, or its path while it has none), or null.</param>
/// <param name="Name">That entity's name, or null.</param>
/// <param name="Type">That entity's type, or null.</param>
/// <param name="Problem">Why it doesn't resolve, or null.</param>
public sealed record ChapterLinkView(string Text, string Target, string? Label, string Outcome, string? Entity, string? Name, string? Type, string? Problem);

/// <summary>A chapter and its chapter fields, for the web UI (#225).</summary>
/// <param name="Key">The chapter key.</param>
/// <param name="Position">Its position, <c>book:key</c>.</param>
/// <param name="Number">Its display number, or null.</param>
/// <param name="Title">Its title, or null.</param>
/// <param name="Bare">True if <c>book.md</c> writes it as a bare key (<c>chapters: [1, 2]</c>), which has no place for fields.</param>
/// <param name="Fields">Each chapter field the den defines, in field order, then any other name the chapter writes or inherits, by name.</param>
public sealed record ChapterFieldsView(string Key, string Position, long? Number, string? Title, bool Bare, IReadOnlyList<ChapterValueView> Fields);

/// <summary>A book's chapters with their chapter fields, for the web UI (#225).</summary>
/// <param name="Version">The shape version.</param>
/// <param name="Universe">The universe's key.</param>
/// <param name="Book">The book's key.</param>
/// <param name="Path">Its <c>book.md</c>, relative to the den root.</param>
/// <param name="Hash">That file's hash, for an edit of a chapter's fields.</param>
/// <param name="Final">True for a final (frozen) book, whose chapters can't be edited.</param>
/// <param name="FieldTypes">The den's chapter fields (types.md F-CHAP-2), in pack order, as a field editor needs them.</param>
/// <param name="Chapters">The chapters, in book order.</param>
public sealed record BookChapterFieldsResult(
    int Version, string Universe, string Book, string Path, string Hash, bool Final, IReadOnlyList<SourceFieldType> FieldTypes, IReadOnlyList<ChapterFieldsView> Chapters);

/// <summary>A universe's, series', or book's <c>chapter_defaults</c>, for the web UI (#225).</summary>
/// <param name="Version">The shape version.</param>
/// <param name="Universe">The universe's key.</param>
/// <param name="Kind"><c>universe</c>, <c>series</c>, or <c>book</c>.</param>
/// <param name="Key">The container's key.</param>
/// <param name="Path">Its file, relative to the den root.</param>
/// <param name="Hash">That file's hash, for an edit of its defaults.</param>
/// <param name="Final">True for a final (frozen) book, whose defaults can't be edited.</param>
/// <param name="FieldTypes">The den's chapter fields, in pack order.</param>
/// <param name="Fields">Each chapter field the den defines, in field order, then any other name it or a container above writes, by name.</param>
/// <param name="Books">The books whose chapters its defaults reach, in story order.</param>
public sealed record ChapterDefaultsSourceResult(
    int Version, string Universe, string Kind, string Key, string Path, string Hash, bool Final, IReadOnlyList<SourceFieldType> FieldTypes,
    IReadOnlyList<ChapterValueView> Fields, IReadOnlyList<BookBelowView> Books);

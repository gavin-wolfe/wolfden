namespace Wolfden.Index.Writing;

// The shapes of creating series and books (#213): what the web API answers,
// as camelCase JSON, and what `wolfden new series` and `wolfden new book`
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

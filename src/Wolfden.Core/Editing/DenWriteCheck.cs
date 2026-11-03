using Wolfden.Core.Checks;
using Wolfden.Core.Documents;
using Wolfden.Core.Entities;
using Wolfden.Core.Structure;

namespace Wolfden.Core.Editing;

/// <summary>
/// Checks files a writer is about to save, as the den would read them once
/// saved (format.md §1.3: the app and the MCP tools must refuse to write a
/// file that would have an error). Each file is read the way a load of the
/// den reads it, through an <see cref="OverlayDenSource"/>, so the errors
/// are exactly the ones the parser and <c>wolfden check</c> would report.
/// Errors depend only on the file itself, <c>den.yaml</c>, and the enabled
/// types (F-GEN-4), so the rest of the den needn't be read again.
/// </summary>
/// <remarks>
/// A bad value in a section comment that's already in a file doesn't block a
/// save of it (format.md §1.3). Such an error is known across the edit by its
/// rule, its attribute, and its value as written, never by its line or its
/// section's name, so text added above it, a renamed heading, or a moved
/// section keeps it the same error, and a new value is a new one. They're
/// counted, so a second <c>status=maybe</c> is new even if one is already there.
/// </remarks>
public static class DenWriteCheck
{
    /// <summary>
    /// Reads <paramref name="files"/> (den-relative path to new contents) as
    /// the den would: container files by loading the den's structure over
    /// them, outlines as outlines, and entity files as the entity catalog
    /// does, with the field checks of <c>wolfden check</c> for warnings.
    /// </summary>
    /// <param name="den">The den as it is now.</param>
    /// <param name="structure">The den's structure as it is now.</param>
    /// <param name="files">The files to check, by den-relative path.</param>
    /// <param name="movedFrom">For a file that moves, its new path to its path now, so the errors it has now count as already there. A new file has none.</param>
    /// <exception cref="ArgumentException">A path isn't a container file, an outline, or an entity file of the den: writers don't edit anything else.</exception>
    public static DenWriteCheckResult Check(
        IDenSource den, DenStructure structure, IReadOnlyDictionary<string, byte[]> files, IReadOnlyDictionary<string, string>? movedFrom = null)
    {
        ArgumentNullException.ThrowIfNull(den);
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(files);
        var overlay = new OverlayDenSource(den, files);
        var containers = Containers(structure).ToDictionary(c => c.FilePath, StringComparer.Ordinal);

        // A new series' or book's file (made from the web UI or the CLI, #213) is a container file too.
        var made = files.Keys.Where(p => !containers.ContainsKey(p) && IsNewContainerFile(structure, p)).ToHashSet(StringComparer.Ordinal);
        var after = files.Keys.Any(containers.ContainsKey) || made.Count > 0 ? DenStructure.Load(overlay) : structure;
        var outlines = after.Universes.SelectMany(u => u.Books).ToDictionary(b => $"{b.Path}/outline.md", StringComparer.Ordinal);
        RowIndex? rows = null;

        var result = new Dictionary<string, IReadOnlyList<Diagnostic>>(StringComparer.Ordinal);
        foreach (var (path, bytes) in files)
        {
            if (containers.ContainsKey(path) || made.Contains(path))
            {
                result[path] = after.Diagnostics.Where(d => d.Path == path).Select(d => d.Diagnostic).ToList();
            }
            else if (outlines.ContainsKey(path))
            {
                var outline = MarkdownFile.ParseOutline(bytes);
                result[path] = outline.Diagnostics.Concat(Outlines.OutlineReader.Read(outline).Diagnostics).OrderBy(d => d.Line ?? 0).ToList();
            }
            else if (EntityCatalog.EntityFileScope(after, path) is not null)
            {
                var read = EntityCatalog.ReadEntityFile(after, path, bytes);
                var found = read.Diagnostics.ToList();
                if (read.Entity is { } entity)
                {
                    if (rows is null)
                    {
                        OutlineChecks.Run(after, overlay, out rows);
                    }

                    FieldChecks.Run(entity, rows, found);
                }

                result[path] = found.OrderBy(d => d.Line ?? 0).ToList();
            }
            else if (EntityCatalog.EntityFileScope(structure, path) is not null
                || structure.Universes.SelectMany(u => u.Books).Any(b => path == $"{b.Path}/outline.md"))
            {
                // Its container would no longer load: the error is the container file's.
                result[path] = [];
            }
            else
            {
                throw new ArgumentException($"'{path}' isn't a container file, an outline, or an entity file of the den, so it isn't checked here.", nameof(files));
            }
        }

        return new DenWriteCheckResult(after, result)
        {
            Kept = result.ToDictionary(p => p.Key, p => Kept(den, movedFrom?.GetValueOrDefault(p.Key) ?? p.Key, p.Value), StringComparer.Ordinal),
        };
    }

    /// <summary>The errors of <paramref name="after"/> that the file at <paramref name="path"/> already has (see the remarks on this class).</summary>
    private static IReadOnlyList<Diagnostic> Kept(IDenSource den, string path, IReadOnlyList<Diagnostic> after)
    {
        var candidates = after.Where(d => d is { Severity: DiagnosticSeverity.Error, LeftOut: not null }).ToList();
        if (candidates.Count == 0 || den.TryReadFile(path) is not { } bytes)
        {
            return [];
        }

        var before = MarkdownFile.Parse(bytes, requireFrontmatter: true).Diagnostics
            .Where(d => d.LeftOut is not null)
            .GroupBy(Key)
            .ToDictionary(g => g.Key, g => g.Count());
        var kept = new List<Diagnostic>();
        foreach (var error in candidates)
        {
            if (before.TryGetValue(Key(error), out var left) && left > 0)
            {
                before[Key(error)] = left - 1;
                kept.Add(error);
            }
        }

        return kept;

        static (string, string, string) Key(Diagnostic d) => (d.RuleId, d.LeftOut!.Attribute, d.LeftOut.Value);
    }

    /// <summary>
    /// True if <paramref name="path"/> is where a new series' or book's file
    /// goes (format.md §5.1, F-LAY-4): <c>series.md</c> in a folder directly
    /// in a loaded universe's <c>series/</c>, or <c>book.md</c> in a folder
    /// directly in a loaded universe's or series' <c>books/</c>.
    /// </summary>
    private static bool IsNewContainerFile(DenStructure structure, string path)
    {
        var folder = DenPaths.Parent(path);
        if (folder.Length == 0 || !folder.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        var parent = DenPaths.Parent(folder);
        return DenPaths.Name(path) switch
        {
            "series.md" => structure.Universes.Any(u => parent == $"{u.Path}/series"),
            "book.md" => structure.Universes.Any(u => parent == $"{u.Path}/books" || u.Series.Any(s => parent == $"{s.Path}/books")),
            _ => false,
        };
    }

    private static IEnumerable<Container> Containers(DenStructure structure) =>
        structure.Universes.SelectMany(u => new Container[] { u }.Concat(u.Series).Concat(u.Books));
}

/// <summary>What <see cref="DenWriteCheck.Check"/> found.</summary>
/// <param name="Structure">The den's structure as it would be after the writes.</param>
/// <param name="Diagnostics">Each file's diagnostics as it would be read, by path.</param>
public sealed record DenWriteCheckResult(DenStructure Structure, IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>> Diagnostics)
{
    /// <summary>
    /// The bad section comments each file already had, by path, which don't
    /// block a save of it (format.md §1.3). They're in <see cref="Diagnostics"/> too.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>> Kept { get; init; } = new Dictionary<string, IReadOnlyList<Diagnostic>>();

    /// <summary>True if any file would have an error.</summary>
    public bool HasErrors => Diagnostics.Values.Any(list => list.Any(d => d.Severity == DiagnosticSeverity.Error));

    /// <summary>True if the write would add an error, so it mustn't be made.</summary>
    public bool AddsErrors => AddedErrors.Count > 0;

    /// <summary>Every error, with its file's path, by path and line.</summary>
    public IReadOnlyList<DenDiagnostic> Errors => All(DiagnosticSeverity.Error);

    /// <summary>The errors that aren't <see cref="Kept"/>: what refuses the write, with its file's path, by path and line.</summary>
    public IReadOnlyList<DenDiagnostic> AddedErrors => Errors.Where(d => !IsKept(d)).ToList();

    /// <summary>The bad section comments the write leaves as they were, with their file's path, by path and line.</summary>
    public IReadOnlyList<DenDiagnostic> KeptErrors => Errors.Where(IsKept).ToList();

    /// <summary>Every warning, with its file's path, by path and line.</summary>
    public IReadOnlyList<DenDiagnostic> Warnings => All(DiagnosticSeverity.Warning);

    private bool IsKept(DenDiagnostic d) =>
        Kept.TryGetValue(d.Path, out var kept) && kept.Any(k => ReferenceEquals(k, d.Diagnostic));

    private List<DenDiagnostic> All(DiagnosticSeverity severity) =>
        Diagnostics.OrderBy(p => p.Key, StringComparer.Ordinal)
            .SelectMany(p => p.Value.Where(d => d.Severity == severity).Select(d => new DenDiagnostic(p.Key, d)))
            .ToList();
}

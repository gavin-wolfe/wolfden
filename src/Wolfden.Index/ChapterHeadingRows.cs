using System.Text.Json;
using Wolfden.Core.Structure;

namespace Wolfden.Index;

/// <summary>
/// Chapter headings in the index (format.md F-CTR-6, #227). A chapter row
/// keeps what its heading is built from, the fields' items with their links
/// as written (<see cref="ChapterRecord.HeadingJson"/>), and a heading is
/// put together when it's read, with <see cref="ChapterHeadings"/>, naming
/// each link's entity from the link rows. Chapter rows are kept while the
/// den's structure stays the same, but link rows and entity names are
/// written on every update, so a renamed note's new name shows at once.
/// </summary>
public static class ChapterHeadingRows
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The heading's parts for a chapter row, or null if it has none.</summary>
    internal static string? Serialize(Chapter chapter)
    {
        var parts = ChapterHeadings.Parts(chapter);
        return parts.Count == 0
            ? null
            : JsonSerializer.Serialize(parts.Select(p => new StoredPart(p.Field, p.Scope.FilePath, p.Items)).ToList(), Json);
    }

    /// <summary>
    /// Each chapter's heading, by chapter row id: its title, then the fields
    /// <c>chapter_heading</c> lists, each link named by the entity it
    /// reaches. As of a story position, give <paramref name="showFields"/>
    /// false for a chapter whose fields the reader isn't to see (one after the
    /// position: it shows its title alone), and <paramref name="met"/>, the
    /// paths of the entities the reader has met: a link to any other shows as
    /// written, so the heading never names what the reader hasn't met (#26).
    /// </summary>
    /// <param name="db">The index.</param>
    /// <param name="universeId">The chapters' universe row.</param>
    /// <param name="chapters">The chapter rows.</param>
    /// <param name="showFields">Whether a chapter's heading may show its fields; null for all.</param>
    /// <param name="met">The paths of the entities the reader has met, or null for everything.</param>
    public static IReadOnlyDictionary<int, string?> Headings(
        IndexDbContext db, int universeId, IReadOnlyCollection<ChapterRecord> chapters, Func<ChapterRecord, bool>? showFields = null, IReadOnlySet<string>? met = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(chapters);
        var parts = chapters
            .Where(c => c.HeadingJson is not null && (showFields is null || showFields(c)))
            .ToDictionary(c => c.Id, c => JsonSerializer.Deserialize<List<StoredPart>>(c.HeadingJson!, Json) ?? []);

        // Each link's entity: any link row in the same file with the same
        // target resolves the same way, since a file's links all resolve from
        // its container's scope (format.md F-CTR-3, types.md F-CHAP-4).
        var paths = parts.Values.SelectMany(p => p).Where(p => p.Items.Any(i => i.Links.Count > 0)).Select(p => p.Path).Distinct().ToList();
        var reached = new Dictionary<(string Path, string Target), string>();
        if (paths.Count > 0)
        {
            var rows = db.Links
                .Where(l => l.UniverseId == universeId && paths.Contains(l.Path) && l.Outcome == "resolved" && l.TargetKey != null)
                .Select(l => new { l.Path, l.Target, l.TargetKey })
                .ToList();
            foreach (var row in rows)
            {
                reached.TryAdd((row.Path, row.Target), row.TargetKey!);
            }
        }

        var keys = reached.Values.Distinct().ToList();
        var entities = keys.Count == 0
            ? []
            : db.Entities.Where(e => e.UniverseId == universeId && keys.Contains(e.Key)).Select(e => new { e.Key, e.Name, e.Path }).ToList()
                .GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Path, StringComparer.Ordinal).First());

        string? NameOf(string path, ChapterHeadingLink link) =>
            reached.TryGetValue((path, link.Target.Trim()), out var key) && entities.TryGetValue(key, out var entity)
                && (met is null || met.Contains(entity.Path))
                ? entity.Name
                : null;

        return chapters.ToDictionary(
            c => c.Id,
            c => ChapterHeadings.Compose(
                c.Title,
                parts.TryGetValue(c.Id, out var stored)
                    ? stored.Select(p => p.Items.Select(i => ChapterHeadings.ItemText(i, l => ChapterHeadings.LinkText(l, NameOf(p.Path, l)))))
                    : []));
    }

    /// <summary>A heading part as a chapter row keeps it: the field, the file that holds the value, and its items.</summary>
    private sealed record StoredPart(string Field, string Path, IReadOnlyList<ChapterHeadingItem> Items);
}

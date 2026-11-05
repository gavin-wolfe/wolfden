using Wolfden.Core.Documents;
using Wolfden.Core.Links;

namespace Wolfden.Index.Reading;

/// <summary>
/// The links of a universe's notes as a reader's page shows them (#300), so a Reader's
/// search looks at what's on that page rather than what's written: a note's name for a
/// link written by its alias or key, and no target behind a label.
/// </summary>
/// <remarks>
/// <see cref="Shown"/> is the server's copy of the web's <c>linkText</c> in
/// <c>Links.tsx</c>. The two can't share code, so both are tested against the same
/// cases, in <c>tests/shared/link-text-cases.json</c>, and <c>ReadersSearchTests</c>
/// compares search with the Reader's page data word by word.
/// </remarks>
public sealed class ReadersLinks
{
    private readonly Lazy<Dictionary<(int Note, string Text), string>> _shown;

    /// <param name="db">The index.</param>
    /// <param name="universeId">The universe's container row.</param>
    /// <param name="met">Whether readers have met a note, so a link shows its name.</param>
    public ReadersLinks(IndexDbContext db, int universeId, Func<EntityRecord, bool> met)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(met);
        _shown = new Lazy<Dictionary<(int, string), string>>(() => Load(db, universeId, met));
    }

    /// <summary>What a page shows for a link: its label, else the note it reaches if readers have met it, else its target as written, with the heading if it has no label.</summary>
    public static string Shown(string? label, string? metName, string target, string? heading)
    {
        var name = label ?? metName ?? target;
        return !string.IsNullOrEmpty(heading) && label is null ? $"{name}: {heading}" : name;
    }

    /// <summary>A lead or a section's text as the note's Reader's page has it: no HTML comments, and each link as it shows.</summary>
    public string Text(int note, string text) =>
        Apply(note, MarkdownComments.Strip(text), markdown: true);

    /// <summary>A field's value as text, as the note's Reader's page has it, with each link as it shows.</summary>
    public string Value(int note, string value) => Apply(note, value, markdown: false);

    private string Apply(int note, string text, bool markdown) =>
        text.Contains("[[", StringComparison.Ordinal)
            ? LinkSyntax.WithLinksShown(text, markdown, link => _shown.Value.GetValueOrDefault((note, link)))
            : text;

    private static Dictionary<(int, string), string> Load(IndexDbContext db, int universeId, Func<EntityRecord, bool> met)
    {
        var targets = db.Entities.Where(e => e.UniverseId == universeId).ToList()
            .GroupBy(e => e.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Path, StringComparer.Ordinal).First());
        var shown = new Dictionary<(int, string), string>();
        var links = db.Links
            .Where(l => l.UniverseId == universeId && l.SourceEntityId != null)
            .Select(l => new { Note = l.SourceEntityId!.Value, l.Text, l.Label, l.Target, l.Heading, l.TargetKey })
            .ToList();
        foreach (var link in links)
        {
            var name = link.TargetKey is { } key && targets.GetValueOrDefault(key) is { } target && met(target) ? target.Name : null;
            shown.TryAdd((link.Note, link.Text), Shown(link.Label, name, link.Target, link.Heading));
        }

        return shown;
    }
}

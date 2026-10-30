using Wolfden.Core.Entities;

namespace Wolfden.Core.Types;

/// <summary>
/// Example links for the messages about a ref field (types.md F-FT-9): a
/// note of the den the field could link to, so a warning suggests a link
/// that resolves there (#240). Without a note of the type, it's a
/// placeholder such as <c>[[name-of-the-location]]</c>.
/// </summary>
public sealed class ExampleLinks
{
    /// <summary>
    /// Placeholders only: for a message read before the notes are, or one
    /// that may be read at a story position, where naming a real note could
    /// be a spoiler.
    /// </summary>
    public static readonly ExampleLinks Placeholders = new([]);

    private readonly List<(string Type, string Slug)> _notes;

    /// <summary>Examples from <paramref name="notes"/>, usually the notes of one universe.</summary>
    public ExampleLinks(IEnumerable<CatalogEntity> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        _notes = notes
            .Where(n => n.Slug is not null)
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Slug, StringComparer.Ordinal)
            .Select(n => (n.Type, n.Slug!))
            .ToList();
    }

    /// <summary>
    /// A link to the first note by name of a type <paramref name="field"/>
    /// takes (any type if it has no <c>to</c>), as a writer types it, such
    /// as <c>[[mara-voss]]</c>; or a placeholder if there's none.
    /// </summary>
    public string For(FieldDefinition field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var to = field.To is { Count: > 0 } types ? types : null;
        foreach (var (type, slug) in _notes)
        {
            if (to is null || to.Contains(type, StringComparer.Ordinal))
            {
                return $"[[{slug}]]";
            }
        }

        return $"[[name-of-the-{to?[0] ?? "note"}]]";
    }
}

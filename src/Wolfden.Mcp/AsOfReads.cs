using System.Text.Json;

namespace Wolfden.Mcp;

/// <summary>
/// What an AI assistant's read at a story position leaves out beyond the
/// canon view itself: a fact's <c>until</c> (format.md F-VIEW-4, #288) and
/// the values of position fields later than the position (F-VIEW-5, #305).
/// Each says what the reader doesn't know yet, such as when a thread pays
/// off, and an assistant drafting that chapter could write toward it.
/// </summary>
/// <remarks>
/// A fact past its <c>until</c> isn't in a view at that position (ranges are
/// half-open, F-TMP-3, F-SEC-8), so every <c>until</c> in one is later than
/// the position, and all of them go. A position field's value has to be
/// compared. One that can't be, since it isn't a position or doesn't resolve,
/// goes too, since nothing says it isn't ahead. A view of the end of the
/// story has nothing ahead of it, and the full view is where a planning
/// assistant reads all of this, so neither is touched. This is in the MCP
/// layer because the web API answers from the same <see cref="DenReader"/>
/// and keeps it all for the writer.
/// </remarks>
internal static class AsOfReads
{
    /// <summary><paramref name="result"/> as an assistant gets it.</summary>
    public static GetEntityResult ForAssistant(GetEntityResult result, StoryHorizon? horizon) =>
        result.Position is null || result.Entity is null ? result : result with { Entity = AsKnown(result.Entity, Required(horizon)) };

    /// <summary><paramref name="result"/> as an assistant gets it.</summary>
    public static CanonAsOfResult ForAssistant(CanonAsOfResult result, StoryHorizon? horizon)
    {
        if (result.Position is null)
        {
            return result;
        }

        var at = Required(horizon);
        return result with
        {
            // An entity left with nothing pinned isn't listed: an empty entry would say something was left out.
            Pinned = result.Pinned
                .Select(p => p with { Fields = AsKnown(p.Fields, p.Type, at), Sections = WithoutUntil(p.Sections) })
                .Where(p => p.Fields.Count > 0 || p.Sections.Count > 0)
                .ToList(),
            Entities = result.Entities.Select(e => AsKnown(e, at)).ToList(),
        };
    }

    private static StoryHorizon Required(StoryHorizon? horizon) =>
        horizon ?? throw new InvalidOperationException("A read at a position comes with its horizon.");

    private static EntityView AsKnown(EntityView entity, StoryHorizon at) => entity with
    {
        Pinned = entity.Pinned is { } pinned ? new PinnedFacts(AsKnown(pinned.Fields, entity.Type, at), WithoutUntil(pinned.Sections)) : null,
        Fields = entity.Fields is { } fields ? AsKnown(fields, entity.Type, at) : null,
        Sections = entity.Sections is { } sections ? WithoutUntil(sections) : null,
    };

    private static List<FieldView> AsKnown(IReadOnlyList<FieldView> fields, string type, StoryHorizon at)
    {
        var known = new List<FieldView>(fields.Count);
        foreach (var field in fields)
        {
            if (!at.IsPositionField(type, field.Name))
            {
                known.Add(field with { Until = null });
            }
            else if (Reached(field.Value, at) is { } value)
            {
                known.Add(field with { Value = value, Until = null });
            }
        }

        return known;
    }

    /// <summary>
    /// The part of a position field's value the reader has reached, or null
    /// if there's none: a position it has reached, or those of a list. A list
    /// written empty stays, since it points nowhere. Anything else, which
    /// F-FT-10 warns about, can't be compared, so it goes.
    /// </summary>
    private static JsonElement? Reached(JsonElement value, StoryHorizon at)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return at.HasReached(value.GetString()!) ? value : null;
            case JsonValueKind.Array:
                var items = value.EnumerateArray().ToList();
                var reached = items.Where(i => i.ValueKind == JsonValueKind.String && at.HasReached(i.GetString()!)).ToList();
                return reached.Count == items.Count ? value
                    : reached.Count == 0 ? null
                    : JsonSerializer.SerializeToElement(reached);
            default:
                return null;
        }
    }

    private static List<SectionView> WithoutUntil(IReadOnlyList<SectionView> sections) => sections.Select(s => s with { Until = null }).ToList();
}

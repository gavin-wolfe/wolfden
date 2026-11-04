using System.Text.Json;
using Wolfden.Core.Documents;

namespace Wolfden.Index.Reading;

/// <summary>
/// What an AI assistant's read at a story position leaves out beyond the
/// canon view itself: a fact's <c>until</c> (format.md F-VIEW-4, #288), the
/// values of position fields later than the position (F-VIEW-5, #305), and the
/// HTML comments in a lead or section (F-VIEW-6, #306). Each can say what the
/// reader doesn't know yet, such as when a thread pays off, and an assistant
/// drafting that chapter could write toward it.
/// </summary>
/// <remarks>
/// A fact past its <c>until</c> isn't in a view at that position (ranges are
/// half-open, F-TMP-3, F-SEC-8), so every <c>until</c> in one is later than
/// the position, and all of them go. A position field's value has to be
/// compared. One that can't be, since it isn't a position or doesn't resolve,
/// goes too, since nothing says it isn't ahead. A comment can say anything, so
/// all of them go, and the text reads as if none was there. A view of the end of the
/// story has nothing ahead of it, and the full view is where a planning
/// assistant reads all of this, so neither is touched. This is in the MCP
/// layer because the web API answers from the same <see cref="DenReader"/>
/// and keeps it all for the writer.
/// </remarks>
public static class AsOfReads
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
                .Select(p => p with { Fields = AsKnown(p.Fields, p.Type, null, at), Sections = AsKnown(p.Sections) })
                .Where(p => p.Fields.Count > 0 || p.Sections.Count > 0)
                .ToList(),
            Entities = result.Entities.Select(e => AsKnown(e, at)).ToList(),
        };
    }

    private static StoryHorizon Required(StoryHorizon? horizon) =>
        horizon ?? throw new InvalidOperationException("A read at a position comes with its horizon.");

    private static EntityView AsKnown(EntityView entity, StoryHorizon at) => entity with
    {
        Lead = entity.Lead is { } lead ? MarkdownComments.Strip(lead) : null,
        Pinned = entity.Pinned is { } pinned ? new PinnedFacts(AsKnown(pinned.Fields, entity.Type, entity.Status, at), AsKnown(pinned.Sections)) : null,
        Fields = entity.Fields is { } fields ? AsKnown(fields, entity.Type, entity.Status, at) : null,
        Sections = entity.Sections is { } sections ? AsKnown(sections) : null,
    };

    /// <param name="fields">The fields.</param>
    /// <param name="type">The entity's type.</param>
    /// <param name="status">The entity's status, or null where every field is pinned (F-VIEW-2).</param>
    /// <param name="at">The position.</param>
    private static List<FieldView> AsKnown(IReadOnlyList<FieldView> fields, string type, string? status, StoryHorizon at)
    {
        var known = new List<FieldView>(fields.Count);
        foreach (var field in fields)
        {
            if (!at.IsPositionField(type, field.Name))
            {
                known.Add(WithoutUntil(field, status));
            }
            else if (Reached(field.Value, at) is { } value)
            {
                known.Add(WithoutUntil(field, status) with { Value = value });
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

    /// <summary>
    /// A field with no <c>until</c>. If an <c>until</c> was all that made it an entry
    /// or a list item, it reads as plain, since <c>kind</c> would hint at what's gone
    /// (F-VIEW-4). A pinned field or one with its own status was written as an entry anyway.
    /// </summary>
    private static FieldView WithoutUntil(FieldView field, string? status) =>
        field.Until is not null && field.From is null && !field.Pinned && field.Status == status
            ? field with { Until = null, Kind = "plain" }
            : field with { Until = null };

    /// <summary>
    /// Sections with no <c>until</c> and no HTML comments. A comment in the middle of
    /// a heading is part of the name F-SEC-3 gives it, so it goes from there too.
    /// </summary>
    private static List<SectionView> AsKnown(IReadOnlyList<SectionView> sections) => sections
        .Select(s => s with
        {
            Name = MarkdownComments.Strip(s.Name),
            HeadingPath = s.HeadingPath.Select(MarkdownComments.Strip).ToList(),
            Until = null,
            Body = MarkdownComments.Strip(s.Body),
        })
        .ToList();
}

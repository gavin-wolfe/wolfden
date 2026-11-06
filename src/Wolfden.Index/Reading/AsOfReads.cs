using System.Text.Json;
using System.Text.Json.Nodes;
using Wolfden.Core.Documents;

namespace Wolfden.Index.Reading;

/// <summary>
/// What a read at a story position for an AI assistant, or for the web UI's
/// Reader's view, leaves out beyond the canon view itself: a fact's <c>until</c> (format.md F-VIEW-4, #288), the
/// values of position fields later than the position (F-VIEW-5, #305), and the
/// HTML comments in a lead or section (F-VIEW-6, #306). Each can say what the
/// reader doesn't know yet, such as when a thread pays off, and an assistant
/// drafting that chapter could write toward it. It also lists the names readers
/// know there in <c>aliases</c>, and reads a link written by a name they learn
/// later as if written by the note's name (F-VIEW-7, #67).
/// </summary>
/// <remarks>
/// A fact past its <c>until</c> isn't in a view at that position (ranges are
/// half-open, F-TMP-3, F-SEC-8), so every <c>until</c> in one is later than
/// the position, and all of them go. A position field's value has to be
/// compared. One that can't be, since it isn't a position or doesn't resolve,
/// goes too, since nothing says it isn't ahead. A comment can say anything, so
/// all of them go, and the text reads as if none was there. A view of the end of the
/// story has nothing ahead of it, and the full view is where a planning
/// assistant reads all of this, so neither is touched. This is apart from
/// <see cref="DenReader"/> because the writer's reads keep it all: the web API
/// applies it only for <c>readers=true</c>, and the MCP tools always do, so
/// there's one rule for the two (#300).
/// </remarks>
public static class AsOfReads
{
    /// <summary><paramref name="result"/> with only what a reader at its position knows.</summary>
    public static GetEntityResult ForReaders(GetEntityResult result, StoryHorizon? horizon) =>
        result.Position is null || result.Entity is null ? result : result with { Entity = AsKnown(result.Entity, Required(horizon)) };

    /// <summary><paramref name="result"/> with only what a reader at its position knows.</summary>
    public static CanonAsOfResult ForReaders(CanonAsOfResult result, StoryHorizon? horizon)
    {
        if (result.Position is null)
        {
            return result;
        }

        var at = Required(horizon);
        var paths = result.Entities.GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Path, StringComparer.Ordinal);
        return result with
        {
            // An entity left with nothing pinned isn't listed: an empty entry would say something was left out.
            Pinned = result.Pinned
                .Select(p => p with
                {
                    Fields = AsKnown(p.Fields, p.Type, null, at, paths.GetValueOrDefault(p.Key)),
                    Sections = AsKnown(p.Sections, at, paths.GetValueOrDefault(p.Key)),
                })
                .Where(p => p.Fields.Count > 0 || p.Sections.Count > 0)
                .ToList(),
            Entities = result.Entities.Select(e => AsKnown(e, at)).ToList(),
        };
    }

    /// <summary><paramref name="entity"/>, read at the position of <paramref name="at"/>, with only what a reader there knows.</summary>
    public static EntityView ForReaders(EntityView entity, StoryHorizon at)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(at);
        return AsKnown(entity, at);
    }

    private static StoryHorizon Required(StoryHorizon? horizon) =>
        horizon ?? throw new InvalidOperationException("A read at a position comes with its horizon.");

    /// <remarks>
    /// The names readers know are listed only with the entity's content: a brief view,
    /// like a list, has none of it. They're plain strings, with no <c>from</c>, so a name
    /// learned partway through reads like one known all along (D3 on #67).
    /// </remarks>
    private static EntityView AsKnown(EntityView entity, StoryHorizon at) => entity with
    {
        Aliases = entity.Fields is null ? null : at.NamesOf(entity.Path),
        Lead = entity.Lead is { } lead ? at.LinksAsKnown(entity.Path, MarkdownComments.Strip(lead), markdown: true) : null,
        Pinned = entity.Pinned is { } pinned
            ? new PinnedFacts(AsKnown(pinned.Fields, entity.Type, entity.Status, at, entity.Path), AsKnown(pinned.Sections, at, entity.Path))
            : null,
        Fields = entity.Fields is { } fields ? AsKnown(fields, entity.Type, entity.Status, at, entity.Path) : null,
        Sections = entity.Sections is { } sections ? AsKnown(sections, at, entity.Path) : null,
    };

    /// <param name="fields">The fields.</param>
    /// <param name="type">The entity's type.</param>
    /// <param name="status">The entity's status, or null where every field is pinned (F-VIEW-2).</param>
    /// <param name="at">The position.</param>
    /// <param name="path">The entity's file, whose links are read as known there; null to leave them as written.</param>
    private static List<FieldView> AsKnown(IReadOnlyList<FieldView> fields, string type, string? status, StoryHorizon at, string? path)
    {
        var known = new List<FieldView>(fields.Count);
        foreach (var field in fields)
        {
            if (!at.IsPositionField(type, field.Name))
            {
                known.Add(WithLinksAsKnown(WithoutUntil(field, status), at, path));
            }
            else if (Reached(field.Value, at) is { } value)
            {
                known.Add(WithoutUntil(field, status) with { Value = value });
            }
        }

        return known;
    }

    /// <summary>A field whose value's strings have each link written by a later name written by the note's name (F-VIEW-7).</summary>
    private static FieldView WithLinksAsKnown(FieldView field, StoryHorizon at, string? path)
    {
        if (path is null || field.Kind == "default" || !field.Value.GetRawText().Contains("[[", StringComparison.Ordinal))
        {
            return field;
        }

        return field with { Value = JsonSerializer.SerializeToElement(Rewrite(JsonNode.Parse(field.Value.GetRawText()))) };

        JsonNode? Rewrite(JsonNode? node)
        {
            switch (node)
            {
                case JsonValue value when value.TryGetValue<string>(out var text):
                    return JsonValue.Create(at.LinksAsKnown(path, text, markdown: false));
                case JsonArray array:
                    return new JsonArray(array.Select(item => Rewrite(item?.DeepClone())).ToArray());
                case JsonObject map:
                    var copy = new JsonObject();
                    foreach (var (name, item) in map)
                    {
                        copy[name] = Rewrite(item?.DeepClone());
                    }

                    return copy;
                default:
                    return node?.DeepClone();
            }
        }
    }

    /// <summary>
    /// The part of a position field's value the reader has reached, or null
    /// if there's none: a position it has reached, or those of a list. A list
    /// written empty stays, since it points nowhere. Anything else, which
    /// F-FT-10 warns about, can't be compared, so it goes.
    /// </summary>
    public static JsonElement? Reached(JsonElement value, StoryHorizon at)
    {
        ArgumentNullException.ThrowIfNull(at);
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
    private static List<SectionView> AsKnown(IReadOnlyList<SectionView> sections, StoryHorizon at, string? path) => sections
        .Select(s => s with
        {
            Name = Known(s.Name, at, path),
            HeadingPath = s.HeadingPath.Select(h => Known(h, at, path)).ToList(),
            Until = null,
            Body = Known(s.Body, at, path),
        })
        .ToList();

    private static string Known(string text, StoryHorizon at, string? path) =>
        path is null ? MarkdownComments.Strip(text) : at.LinksAsKnown(path, MarkdownComments.Strip(text), markdown: true);
}

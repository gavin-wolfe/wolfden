namespace Wolfden.Mcp;

/// <summary>
/// What an AI assistant's read at a story position leaves out beyond the
/// canon view itself (format.md F-VIEW-4, #288). A fact's <c>until</c> says
/// when it stops holding, so at a position it tells an assistant drafting
/// that chapter what the reader doesn't know yet, and it could write toward
/// that.
/// </summary>
/// <remarks>
/// A fact past its <c>until</c> isn't in a view at that position (ranges are
/// half-open, F-TMP-3, F-SEC-8), so every <c>until</c> in one is later than
/// the position, and all of them go. A view of the end of the story holds
/// only facts with no end, and the full view is where a planning assistant
/// reads them, so neither is touched. This is in the MCP layer because the
/// web API answers from the same <see cref="DenReader"/> and keeps the date
/// for the writer, who sees it marked.
/// </remarks>
internal static class AsOfReads
{
    /// <summary><paramref name="result"/> as an assistant gets it.</summary>
    public static GetEntityResult ForAssistant(GetEntityResult result) =>
        result.Position is null || result.Entity is null ? result : result with { Entity = WithoutUntil(result.Entity) };

    /// <summary><paramref name="result"/> as an assistant gets it.</summary>
    public static CanonAsOfResult ForAssistant(CanonAsOfResult result) =>
        result.Position is null
            ? result
            : result with
            {
                Pinned = result.Pinned.Select(p => p with { Fields = WithoutUntil(p.Fields), Sections = WithoutUntil(p.Sections) }).ToList(),
                Entities = result.Entities.Select(WithoutUntil).ToList(),
            };

    private static EntityView WithoutUntil(EntityView entity) => entity with
    {
        Pinned = entity.Pinned is { } pinned ? new PinnedFacts(WithoutUntil(pinned.Fields), WithoutUntil(pinned.Sections)) : null,
        Fields = entity.Fields is { } fields ? WithoutUntil(fields) : null,
        Sections = entity.Sections is { } sections ? WithoutUntil(sections) : null,
    };

    private static List<FieldView> WithoutUntil(IReadOnlyList<FieldView> fields) => fields.Select(f => f with { Until = null }).ToList();

    private static List<SectionView> WithoutUntil(IReadOnlyList<SectionView> sections) => sections.Select(s => s with { Until = null }).ToList();
}

using Wolfden.Core.Documents;
using Wolfden.Core.Links;

namespace Wolfden.Index.Reading;

public sealed partial class DenReader
{
    /// <summary>
    /// A search of what a reader at <paramref name="asOf"/> can read (#300, the web
    /// UI's Reader's view): the words are looked for in what an AI assistant's
    /// <c>get_entity</c> at that position gives, after <see cref="AsOfReads"/>, rather
    /// than in the search table, which holds every note's whole text. So there's one
    /// rule for what a reader knows, and a word in an HTML comment, a later position
    /// value, or a later <c>until</c> neither matches nor shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A note's text is its name and its lead, and a section's is its heading and its own
    /// text. It's what the Reader's page shows, so in text and field values a labelled
    /// link is only its label (<see cref="LinkSyntax.WithOnlyLabels"/>), and a note's
    /// aliases, which that page and <c>get_entity</c> don't list and which can hold a
    /// secret, don't match. Types' defaults aren't searched, as with no position: they
    /// aren't the note's writing. A value's link matches by its note's name only if the
    /// reader has met that note (#179). The search table only gives the order, by how
    /// well a note's text matches there, with no comments counted.
    /// </para>
    /// <para>
    /// MCP's <c>search</c> has no position, on purpose, and doesn't use this.
    /// </para>
    /// </remarks>
    public SearchResult SearchAsReaders(
        string query, string? universe, string? type, bool canonOnly, int limit, string? status, string? series, string? book, string asOf, string? field)
    {
        var filter = CheckSearch(query, limit, status, series, book);
        return WithIndex(index =>
        {
            var db = index.Context;
            var universes = db.Containers.Where(c => c.Kind == "universe").ToDictionary(c => c.Id, c => c.Key);
            var u = ChooseUniverse(db, universe, asOf, allowAll: true)!;
            if (type is not null)
            {
                CheckTypes(db, u.Id, [type], listUsed: false);
            }

            CheckFilterNamingNothing(db, u, filter);
            var types = TypesMatching(type);
            var view = CanonView(index, u.Key, asOf, filter);
            var whole = filter is null ? view : CanonView(index, u.Key, asOf, null);
            var horizon = Horizon(index, u.Key, asOf);
            var fieldName = string.IsNullOrWhiteSpace(field) ? null : field.Trim();
            if (fieldName is not null)
            {
                CheckField(db, u.Id, fieldName, FieldsKnownIn(whole, u.Key, horizon), asOf);
            }

            var words = SearchWords.Parse(query);
            if (words.IsEmpty)
            {
                return new SearchResult(ResultVersion, query, 0, false, []);
            }

            var records = db.Entities.Where(e => e.UniverseId == u.Id).ToList().ToDictionary(e => e.Path, StringComparer.Ordinal);
            var known = view.Entities
                .Where(e => (types is null || types.Contains(e.Type)) && (status is null || records[e.Path].Status == status))
                .Select(e => AsOfReads.ForReaders(CanonEntityView(e, u.Key, includeContent: true), horizon))
                .ToList();
            var scopes = Scopes(db);
            var hits = new List<SearchHitView>();
            if (fieldName is null)
            {
                var ranks = index.SearchRanks(query);
                hits.AddRange(known
                    .SelectMany(e => TextHits(e, records[e.Path], words))
                    .OrderBy(h => ranks.TryGetValue((h.Entity.Path, h.Line), out var rank) ? rank : double.MaxValue)
                    .ThenBy(h => h.Entity.Path, StringComparer.Ordinal)
                    .ThenBy(h => h.Line ?? -1)
                    .Select(h => h.Hit(Summary(h.Entity, universes, scopes)))
                    .Where(h => !canonOnly || h.InCanon)
                    .Take(limit + 1));
            }

            if (hits.Count <= limit)
            {
                var met = whole.Entities.Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
                var search = new FieldSearch(index, words, u.Id, fieldName, met, asReaders: true);
                hits.AddRange(known
                    .SelectMany(e => (e.Fields ?? [])
                        .Where(f => f.Kind != "default" && (fieldName is null || f.Name == fieldName))
                        .Select(f => new FieldCandidate(records[e.Path], f.Name, f.Line, f.Value.GetRawText(), f.Status ?? e.Status, f.EntryIndex)))
                    .Where(c => !canonOnly || c.Status is "draft" or "canon")
                    .Select(search.Match)
                    .OfType<FieldHit>()
                    .Take(limit + 1 - hits.Count)
                    .Select(h => new SearchHitView(
                        Summary(h.Entity, universes, scopes), null, h.Line, h.Snippet.Bracketed(), h.Status, h.Status is "draft" or "canon", false, h.Field)
                    {
                        Text = h.Snippet.Text,
                        Matches = h.Snippet.Matches,
                        EntryIndex = h.EntryIndex,
                        LinkedNotes = h.LinkedNotes,
                    }));
            }

            var shown = hits.Take(limit).ToList();
            return new SearchResult(ResultVersion, query, shown.Count, hits.Count > limit, shown);
        });
    }

    /// <summary>
    /// Whether every word of <paramref name="query"/> is in <paramref name="title"/> or
    /// <paramref name="text"/> as a reader's page has them, with HTML comments taken out as
    /// a reader's read takes them out (<see cref="MarkdownComments.Strip(string)"/>), and
    /// the text's labelled links only their labels. For a caller comparing a search
    /// table's hit with what a Reader's search finds, so that's one rule too.
    /// </summary>
    public static bool ReadersCanFind(string query, string title, string text) =>
        Found(SearchWords.Parse(query), MarkdownComments.Strip(title), ReadersText(text)) is not null;

    /// <summary>
    /// Whether every word of <paramref name="query"/> is there once HTML comments are out,
    /// labelled links as written: so a hit a reader can't find this way is only in a comment.
    /// </summary>
    public static bool FoundWithoutComments(string query, string title, string text) =>
        Found(SearchWords.Parse(query), MarkdownComments.Strip(title), MarkdownComments.Strip(text)) is not null;

    /// <summary>
    /// Whether every word of <paramref name="query"/> is in a field's value as a reader's
    /// page has it, where a labelled link is its label, or in <paramref name="names"/>, the
    /// names of the notes it links to that readers have met.
    /// </summary>
    public static bool ReadersCanFindValue(string query, string valueJson, IEnumerable<string> names) =>
        Found(SearchWords.Parse(query), string.Join("\n", names), LinkSyntax.WithOnlyLabels(FieldSearch.ValueText(valueJson), markdown: false)) is not null;

    /// <summary>Whether every word of <paramref name="query"/> is in a field's value as written, or in <paramref name="names"/>.</summary>
    public static bool FoundInValueAsWritten(string query, string valueJson, IEnumerable<string> names) =>
        Found(SearchWords.Parse(query), string.Join("\n", names), FieldSearch.ValueText(valueJson)) is not null;

    /// <summary>A lead or section's text as a reader's page has it, from what a reader's read gives or as written.</summary>
    private static string ReadersText(string text) => LinkSyntax.WithOnlyLabels(MarkdownComments.Strip(text), markdown: true);

    /// <summary>Refuses a series or book the universe doesn't have, naming neither the others nor which are later.</summary>
    private static void CheckFilterNamingNothing(IndexDbContext db, ContainerRecord universe, CanonFilter? filter)
    {
        try
        {
            CheckFilter(db, universe, filter);
        }
        catch (DenToolException e) when (e.Code == ToolErrorCodes.UnknownScope)
        {
            var kind = filter!.Kind == CanonFilterKind.Series ? "series" : "book";
            throw new DenToolException(ToolErrorCodes.UnknownScope, $"There's no {kind} '{filter.Key}' to search in here.");
        }
    }

    /// <summary>The names of the fields with a value a reader at <paramref name="at"/> can see, not the types' defaults.</summary>
    private static IEnumerable<string> FieldsKnownIn(CanonView view, string universe, StoryHorizon at) =>
        view.Entities
            .SelectMany(e => AsOfReads.ForReaders(CanonEntityView(e, universe, includeContent: true), at).Fields ?? [])
            .Where(f => f.Kind != "default")
            .Select(f => f.Name)
            .Distinct();

    private static IEnumerable<ReadersTextHit> TextHits(EntityView note, EntityRecord record, SearchWords words)
    {
        if (Found(words, note.Name, ReadersText(note.Lead ?? "")) is { } own)
        {
            yield return new ReadersTextHit(record, null, null, own, note.Status);
        }

        foreach (var section in note.Sections ?? [])
        {
            if (Found(words, section.Name, ReadersText(section.Body)) is { } found)
            {
                yield return new ReadersTextHit(record, section.Name, section.Line, found, section.Status);
            }
        }
    }

    /// <summary>
    /// The excerpt, if every word is in the title or the text, as the search table
    /// matches across them: of the text if a word is there, otherwise of the title.
    /// </summary>
    private static MarkedText? Found(SearchWords words, string title, string text)
    {
        if (words.IsEmpty)
        {
            return null;
        }

        var inTitle = words.Find(title);
        var inText = words.Find(text);
        for (var i = 0; i < words.Count; i++)
        {
            if (inTitle[i].Count == 0 && inText[i].Count == 0)
            {
                return null;
            }
        }

        return inText.Any(m => m.Count > 0)
            ? FieldSearch.Cut(text, inText.SelectMany(m => m).ToList())
            : FieldSearch.Cut(title, inTitle.SelectMany(m => m).ToList());
    }

    /// <summary>A match in a note's name, aliases, or lead, or in a section.</summary>
    private sealed record ReadersTextHit(EntityRecord Entity, string? Section, int? Line, MarkedText Excerpt, string Status)
    {
        public SearchHitView Hit(EntitySummary summary) =>
            new(summary, Section, Line, Excerpt.Bracketed(), Status, Status is "draft" or "canon", false)
            {
                Text = Excerpt.Text,
                Matches = Excerpt.Matches,
            };
    }
}

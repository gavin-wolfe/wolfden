using Wolfden.Core.Links;

namespace Wolfden.Index.Reading;

/// <summary>
/// How a link picker's lookup (#198) matches a note by what's typed: each
/// word typed starts a word of the note's name, one of its aliases, its slug,
/// or (typed whole enough) its id. Words are split and folded as the search
/// table splits and folds them (<see cref="SearchWords"/>: ignoring case and
/// Latin accents), so <c>zoe ark</c> finds Zoë Arkwright, and a whole text
/// that matches as a link would (<see cref="LinkText.Normalize"/>) ranks first.
/// </summary>
public static class EntityLookup
{
    /// <summary>The fewest characters of an id that match it by prefix: a ULID's first ten are its timestamp, which notes made together share.</summary>
    public const int IdPrefixLength = 10;

    /// <summary>
    /// How well <paramref name="query"/> matches a note, best first, or null
    /// if it doesn't: an exact name; an exact slug or id; an exact alias; a
    /// name that starts with the words typed, in order; a name holding them
    /// all; a slug holding them all; an alias that starts with them; an alias
    /// holding them all; the start of the id. The alias is the one that
    /// matched, for <c>alias</c> matches.
    /// </summary>
    public static LookupMatch? Match(string query, string name, string? slug, string? id, IReadOnlyList<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(aliases);
        var words = Words(query);
        if (words.Count == 0)
        {
            return null;
        }

        var whole = LinkText.Normalize(query);
        if (whole == LinkText.Normalize(name))
        {
            return new(0, "name", null);
        }

        if (slug is not null && whole == LinkText.Normalize(slug))
        {
            return new(1, "slug", null);
        }

        if (id is not null && whole == LinkText.Normalize(id))
        {
            return new(1, "id", null);
        }

        if (aliases.FirstOrDefault(a => whole == LinkText.Normalize(a)) is { } exact)
        {
            return new(2, "alias", exact);
        }

        var nameWords = Words(name);
        if (StartsWith(nameWords, words))
        {
            return new(3, "name", null);
        }

        if (HasAll(nameWords, words))
        {
            return new(4, "name", null);
        }

        if (slug is not null && HasAll(Words(slug), words))
        {
            return new(5, "slug", null);
        }

        var aliasWords = aliases.Select(a => (Alias: a, Words: Words(a))).ToList();
        if (aliasWords.FirstOrDefault(a => StartsWith(a.Words, words)) is { Alias: not null } starts)
        {
            return new(6, "alias", starts.Alias);
        }

        if (aliasWords.FirstOrDefault(a => HasAll(a.Words, words)) is { Alias: not null } holds)
        {
            return new(7, "alias", holds.Alias);
        }

        var typed = query.Trim();
        if (id is not null && typed.Length >= IdPrefixLength && id.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
        {
            return new(8, "id", null);
        }

        return null;
    }

    private static List<string> Words(string text) => SearchWords.Tokens(text).Select(t => t.Folded).ToList();

    /// <summary>True if the text's first words start with the words typed, in order.</summary>
    private static bool StartsWith(List<string> text, List<string> typed) =>
        typed.Count <= text.Count && typed.Select((w, i) => text[i].StartsWith(w, StringComparison.Ordinal)).All(x => x);

    /// <summary>True if each word typed starts some word of the text, in any order.</summary>
    private static bool HasAll(List<string> text, List<string> typed) =>
        typed.All(w => text.Any(t => t.StartsWith(w, StringComparison.Ordinal)));
}

/// <summary>How a note matched a lookup.</summary>
/// <param name="Rank">Lower is better (<see cref="EntityLookup.Match"/>).</param>
/// <param name="By">name, slug, id, or alias.</param>
/// <param name="Alias">The alias that matched, for an alias match.</param>
public sealed record LookupMatch(int Rank, string By, string? Alias);

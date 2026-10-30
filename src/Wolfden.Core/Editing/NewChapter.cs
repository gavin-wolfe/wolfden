using System.Globalization;
using System.Numerics;
using Wolfden.Core.Yaml;

namespace Wolfden.Core.Editing;

/// <summary>A chapter as <see cref="NewChapter"/> sees it: its key, and its display number if it has a valid one (format.md F-BOOK-3).</summary>
/// <param name="Key">The chapter key.</param>
/// <param name="Number">The display number, or null when it has none, or one that isn't a non-negative integer.</param>
public sealed record ChapterSlot(string Key, long? Number);

/// <summary>A chapter whose display number an added chapter pushes along.</summary>
/// <param name="Key">The chapter key, which doesn't change (F-BOOK-6).</param>
/// <param name="Index">Its place in the <c>chapters</c> list after the new chapter is in it, from 0.</param>
/// <param name="From">Its number before.</param>
/// <param name="To">Its number after.</param>
public sealed record Renumbering(string Key, int Index, long From, long To);

/// <summary>Where a new chapter goes, its key and number, and the numbers it changes.</summary>
/// <param name="Key">The new chapter's key.</param>
/// <param name="Index">Its place in the <c>chapters</c> list, from 0.</param>
/// <param name="Number">Its display number, or null for none.</param>
/// <param name="Renumbered">The chapters after it whose numbers change, in book order.</param>
public sealed record ChapterPlan(string Key, int Index, long? Number, IReadOnlyList<Renumbering> Renumbered);

/// <summary>
/// Plans adding a chapter to a book's <c>chapters</c> (format.md §6.4,
/// §8; #215): its key, its display number, and the numbers after it that
/// change. It never writes and never changes a key (F-BOOK-6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys</b> (maintainer decisions on #215, 2026-10-29). At the end, the
/// new chapter gets the next whole number: one more than the largest key
/// that's all digits, or <c>"1"</c> if there's none. Inserted after key
/// <c>K</c>, it gets the first unused letter suffix: <c>Kb</c>, then
/// <c>Kc</c>, and so on; after <c>2b</c> it's <c>2ba</c>. The rule behind
/// both: a key that ends in a digit starts at <c>b</c> (as if <c>2</c>
/// were <c>2a</c>), and a key that ends in a letter starts at <c>a</c>,
/// so <c>prologue</c> gives <c>prologuea</c> (rule chosen, #215). When
/// every single letter is taken, two letters follow (<c>2ba</c>,
/// <c>2bb</c>, …; <see cref="KeyAfter"/>). When the chapter after it is
/// <c>K</c> with letters added, the new key takes a suffix that sorts
/// between the two if there's one, so between <c>2</c> and <c>2b</c> it's
/// <c>2a</c> (maintainer decision on #229; <see cref="InsertedKey"/>).
/// Inserting after the last
/// chapter is adding at the end (rule chosen, #215), so the end of a book
/// always gets a whole number.
/// </para>
/// <para>
/// <b>Numbers</b> (rules chosen, #215). <c>number</c> is for display only
/// and never moves a position (F-BOOK-3), so the new chapter's number
/// follows the numbered chapter before it (that one's number plus 1), or
/// takes the number of the numbered chapter after it when none comes
/// before (a chapter inserted after an unnumbered prologue becomes 1). A
/// book whose chapters have no numbers at all keeps that style: the new
/// chapter has none either, except in a book with no chapters, where it's
/// <c>"1"</c>, number 1. The numbered chapters after it are pushed along
/// only as far as needed to keep the numbers rising (F-BOOK-5): each one
/// whose number isn't above the one before it gets that one plus 1, and
/// the first that already is stops it, so a hand-set gap (1, 2, 3, 10)
/// absorbs the insert and the numbers after the gap stay. Chapters with no
/// number, or one that isn't a non-negative integer, are left alone.
/// </para>
/// </remarks>
public static class NewChapter
{
    /// <summary>
    /// The plan for a chapter added at the end (<paramref name="after"/>
    /// null, or the last chapter's key) or after the chapter
    /// <paramref name="after"/>, or null when no key can be made (the key
    /// after <paramref name="after"/> would be longer than 64 characters,
    /// F-KEY-1).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="after"/> isn't one of the chapters.</exception>
    public static ChapterPlan? Plan(IReadOnlyList<ChapterSlot> chapters, string? after)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        var taken = chapters.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        if (chapters.Count == 0)
        {
            return after is null ? new ChapterPlan("1", 0, 1, []) : throw new ArgumentException("The book has no chapters.", nameof(after));
        }

        var at = after is null ? chapters.Count - 1 : IndexOf(chapters, after);
        if (at < 0)
        {
            throw new ArgumentException($"The book has no chapter '{after}'.", nameof(after));
        }

        var numbered = chapters.Any(c => c.Number is not null);
        if (at == chapters.Count - 1)
        {
            // At the end: the next whole number, numbered after the last numbered chapter.
            var last = chapters.LastOrDefault(c => c.Number is not null)?.Number;
            return NextWholeNumber(taken) is { } next
                ? new ChapterPlan(next, chapters.Count, numbered ? last + 1 : null, [])
                : null;
        }

        if (InsertedKey(chapters, at, taken) is not { } key)
        {
            return null;
        }

        var before = chapters.Take(at + 1).LastOrDefault(c => c.Number is not null)?.Number;
        var number = before is { } b ? b + 1 : chapters.Skip(at + 1).FirstOrDefault(c => c.Number is not null)?.Number;
        var renumbered = new List<Renumbering>();
        if (number is { } current)
        {
            for (var i = at + 1; i < chapters.Count; i++)
            {
                if (chapters[i].Number is not { } n)
                {
                    continue;
                }

                if (n > current)
                {
                    break;
                }

                current++;
                renumbered.Add(new Renumbering(chapters[i].Key, i + 1, n, current));
            }
        }

        return new ChapterPlan(key, at + 1, number, renumbered);
    }

    /// <summary>
    /// The key of a chapter inserted after chapter <paramref name="at"/>,
    /// which isn't the last: the one place the rule for inserted keys is
    /// chosen (maintainer decision on #229, 2026-10-30). When the next
    /// chapter's key is this one's with letters added (<c>2</c>, then
    /// <c>2b</c>), the new key is the first unused suffix that sorts
    /// between them, comparing the suffixes as ASCII strings as F-OUT-4
    /// does for rows, so inserting between <c>2</c> and <c>2b</c> gives
    /// <c>2a</c>, and between <c>2b</c> and <c>2bb</c> gives <c>2ba</c>.
    /// Shorter suffixes come first. Within each length, the ones
    /// <see cref="KeyAfter"/> would try come first, so between <c>2</c>
    /// and <c>2d</c> it's still <c>2b</c>; then, for a key that ends in a
    /// digit, those of the same length that start with <c>a</c>, so
    /// between <c>2</c> and <c>2c</c> it's <c>2a</c> when <c>2b</c> is
    /// taken, but <c>2ba</c> before <c>2aa</c> when <c>2a</c> is taken too
    /// (rules chosen, #229). When no unused suffix sorts between them
    /// (between <c>2</c> and <c>2a</c>), or the next key isn't this one with letters added, it's
    /// <see cref="KeyAfter"/>: the first unused suffix. Any other next key
    /// already sorts after every suffix of this one, or never could, so
    /// only this case needs a choice.
    /// </summary>
    internal static string? InsertedKey(IReadOnlyList<ChapterSlot> chapters, int at, IReadOnlySet<string> taken)
    {
        var key = chapters[at].Key;
        if (at + 1 < chapters.Count && chapters[at + 1].Key is var next
            && next.Length > key.Length && next.StartsWith(key, StringComparison.Ordinal) && char.IsAsciiLetterLower(next[key.Length]))
        {
            if (KeyBetween(key, next[key.Length..], taken) is { } between)
            {
                return between;
            }
        }

        return KeyAfter(key, taken);
    }

    /// <summary>
    /// The key of a chapter inserted after <paramref name="key"/>: the
    /// first of <c>Kb</c>, <c>Kc</c>, … <c>Kz</c> (from <c>Ka</c> when
    /// <paramref name="key"/> ends in a letter) that isn't in
    /// <paramref name="taken"/>, then two letters (<c>Kba</c>, <c>Kbb</c>,
    /// …), and so on; or null when it would be longer than 64 characters.
    /// </summary>
    public static string? KeyAfter(string key, IReadOnlySet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(taken);
        var first = FirstLetter(key);
        for (var length = 1; key.Length + length <= Structure.Keys.MaxLength; length++)
        {
            if (Suffixes(length, first).Select(s => key + s).FirstOrDefault(k => !taken.Contains(k) && Structure.Keys.IsChapterKey(k)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// The first of <paramref name="key"/>'s suffixes that's below
    /// <paramref name="limit"/> as an ASCII string and isn't in
    /// <paramref name="taken"/>, or null. Shorter suffixes come first; within
    /// a length, those <see cref="KeyAfter"/> tries (from <c>b</c> after a
    /// digit), then, after a digit, those that start with <c>a</c>. Within a
    /// length each run rises, so it ends at the first suffix at or above
    /// the limit; and when no suffix of a length is below it, no longer one
    /// is either, so the search stops.
    /// </summary>
    private static string? KeyBetween(string key, string limit, IReadOnlySet<string> taken)
    {
        var first = FirstLetter(key);
        for (var length = 1; key.Length + length <= Structure.Keys.MaxLength; length++)
        {
            var below = Below(Suffixes(length, first), limit);
            if (first != 'a')
            {
                below = below.Concat(Below(length == 1 ? ["a"] : Suffixes(length - 1, 'a').Select(s => "a" + s), limit));
            }

            var any = false;
            foreach (var suffix in below)
            {
                any = true;
                var candidate = key + suffix;
                if (!taken.Contains(candidate) && Structure.Keys.IsChapterKey(candidate))
                {
                    return candidate;
                }
            }

            if (!any)
            {
                return null;
            }
        }

        return null;
    }

    private static IEnumerable<string> Below(IEnumerable<string> suffixes, string limit) =>
        suffixes.TakeWhile(s => string.CompareOrdinal(s, limit) < 0);

    /// <summary>The letter a key's suffixes start at: <c>b</c> after a digit (as if <c>2</c> were <c>2a</c>), else <c>a</c>.</summary>
    private static char FirstLetter(string key) => key.Length > 0 && char.IsAsciiDigit(key[^1]) ? 'b' : 'a';

    /// <summary>One more than the largest key that's all digits (<c>"1"</c> if none is), or null if that's longer than 64 characters.</summary>
    public static string? NextWholeNumber(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var max = keys.Where(k => k.Length > 0 && k.All(char.IsAsciiDigit))
            .Select(k => BigInteger.Parse(k, NumberStyles.None, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(BigInteger.Zero).Max();
        var next = (max + 1).ToString(CultureInfo.InvariantCulture);
        return next.Length <= Structure.Keys.MaxLength ? next : null;
    }

    /// <summary>
    /// The new chapter's item, as format.md §6.4 writes one: <c>key</c>,
    /// then <c>number</c> and <c>title</c> when it has them; or the bare key
    /// when it has neither and <paramref name="bare"/> is true, for a list
    /// whose items are bare keys (<c>chapters: [1, 2, 3]</c>).
    /// </summary>
    public static YamlValue Item(string key, long? number, string? title, bool bare)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (bare && number is null && title is null)
        {
            return YamlScalarValue.String(key);
        }

        var item = new YamlMappingValue { { "key", YamlScalarValue.String(key) } };
        if (number is { } n)
        {
            item.Add("number", YamlScalarValue.Integer(n));
        }

        if (title is not null)
        {
            item.Add("title", YamlScalarValue.String(title));
        }

        return item;
    }

    private static int IndexOf(IReadOnlyList<ChapterSlot> chapters, string key)
    {
        for (var i = 0; i < chapters.Count; i++)
        {
            if (chapters[i].Key == key)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Every suffix of <paramref name="length"/> letters, in order, the first letter from <paramref name="first"/>.</summary>
    private static IEnumerable<string> Suffixes(int length, char first)
    {
        if (length == 1)
        {
            for (var c = first; c <= 'z'; c++)
            {
                yield return c.ToString();
            }

            yield break;
        }

        foreach (var head in Suffixes(length - 1, first))
        {
            for (var c = 'a'; c <= 'z'; c++)
            {
                yield return head + c;
            }
        }
    }
}

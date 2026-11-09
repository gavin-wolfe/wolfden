using System.Globalization;
using System.Text;

namespace Wolfden.Core.Text;

/// <summary>A word of some text, as search splits it (<see cref="SearchTokens.Tokens"/>).</summary>
/// <param name="Start">The 0-based offset of its first character, in UTF-16 code units.</param>
/// <param name="Length">Its length.</param>
/// <param name="Folded">The word in lower case without Latin accents, as search compares words.</param>
public readonly record struct SearchToken(int Start, int Length, string Folded);

/// <summary>
/// Splits text into words the way the search table's <c>unicode61</c>
/// tokenizer does, with <c>remove_diacritics 2</c>, so "whole word" means one
/// thing in Wolfden: search (#179) and the plain-text name check
/// (format.md F-NAME-1) both use it.
/// </summary>
public static class SearchTokens
{
    /// <summary>
    /// The words of <paramref name="text"/>, each with its folded form, as
    /// SQLite's <c>unicode61</c> tokenizer finds them: runs of letters,
    /// digits, private-use characters, and the combining marks SQLite counts
    /// as diacritics (<see cref="IsDiacritic"/>, which folding drops). Any
    /// other combining mark, such as U+0305 overline, a Devanagari vowel
    /// sign, or a Hebrew point, ends a word.
    /// </summary>
    public static List<SearchToken> Tokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<SearchToken>();
        var start = -1;
        var i = 0;
        while (i <= text.Length)
        {
            var isWord = false;
            var width = 1;
            if (i < text.Length)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(text, i);
                width = char.IsSurrogatePair(text, i) ? 2 : 1;
                isWord = IsDiacritic(text[i]) || category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                    or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                    or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
                    or UnicodeCategory.PrivateUse;
            }

            if (isWord && start < 0)
            {
                start = i;
            }
            else if (!isWord && start >= 0)
            {
                var folded = Fold(text[start..i]);
                if (folded.Length > 0)
                {
                    tokens.Add(new SearchToken(start, i - start, folded));
                }

                start = -1;
            }

            i += width;
        }

        return tokens;
    }

    /// <summary>
    /// True if <paramref name="text"/> has a character that folding might take
    /// an accent off: a letter of the Latin blocks that hold precomposed accented
    /// letters, or a combining diacritic. Text without one folds to itself in
    /// lower case, so an ordinal search that ignores case finds a folded word in
    /// it wherever <see cref="Tokens"/> would.
    /// </summary>
    public static bool MayHoldAccents(ReadOnlySpan<char> text) =>
        text.IndexOfAnyInRange((char)0x00C0, (char)0x024F) >= 0
        || text.IndexOfAnyInRange((char)0x1E00, (char)0x1EFF) >= 0
        || text.IndexOfAnyInRange((char)0x0300, (char)0x0331) >= 0;

    /// <summary>
    /// Lower case, without the diacritics <c>remove_diacritics 2</c> removes:
    /// combining diacritics (<see cref="IsDiacritic"/>), and those of
    /// precomposed Latin letters (é, ñ, ǘ). Other scripts' letters are kept as they are, so ё
    /// isn't е, and ά isn't α, as in the search table.
    /// </summary>
    private static string Fold(string word)
    {
        var builder = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            if (IsDiacritic(c))
            {
                continue;
            }

            var parts = IsLatinWithMarks(c) ? c.ToString().Normalize(NormalizationForm.FormD) : null;
            if (parts is { Length: > 1 } && parts.Skip(1).All(IsDiacritic))
            {
                // A Latin letter whose marks are all diacritics folds to its base.
                builder.Append(parts[0]);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// A combining mark SQLite's <c>unicode61</c> counts as a diacritic: a
    /// copy of <c>sqlite3Fts5UnicodeIsdiacritic</c>, whose masks pick 25 code
    /// points of U+0300 to U+0331 (such as U+0301 acute and U+0308
    /// diaeresis, but not U+0305 overline). Every other mark ends a word.
    /// Checked against SQLite 3.53.3, as bundled with
    /// Microsoft.EntityFrameworkCore.Sqlite 10.0.12, by
    /// <c>SearchTests.SearchWordsMatchFieldTextAsTheSearchTableDoes</c>.
    /// </summary>
    private static bool IsDiacritic(char c)
    {
        const uint mask0 = 0x08029FDF; // U+0300 to U+031F
        const uint mask1 = 0x000361F8; // U+0320 to U+0331
        int code = c;
        if (code < 0x0300 || code > 0x0331)
        {
            return false;
        }

        return code < 0x0320 ? (mask0 & (1u << (code - 0x0300))) != 0 : (mask1 & (1u << (code - 0x0320))) != 0;
    }

    /// <summary>A Latin letter that may be precomposed with diacritics: Latin-1 Supplement, Latin Extended-A and -B, and Latin Extended Additional.</summary>
    private static bool IsLatinWithMarks(char c) => c is (>= (char)0x00C0 and <= (char)0x024F) or (>= (char)0x1E00 and <= (char)0x1EFF);
}

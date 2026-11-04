using System.Text;
using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Wolfden.Core.Links;

namespace Wolfden.Core.Documents;

/// <summary>
/// Separates the HTML comments in markdown (format.md F-SEC-11) from the
/// text around them, as CommonMark reads them: a <c>&lt;!--</c> in a code
/// span or code block is just text.
/// </summary>
public static partial class MarkdownComments
{
    /// <summary>
    /// Splits <paramref name="markdown"/> into its text with every HTML
    /// comment replaced by a space, and the comments' contents (without
    /// <c>&lt;!--</c> and <c>--&gt;</c>), one per line.
    /// </summary>
    public static (string Text, string Comments) Split(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (!markdown.Contains("<!--", StringComparison.Ordinal)
            || BodyLinkScanner.Parse(markdown, 0, BodyLinkScanner.Plain) is not { } document)
        {
            return (markdown, "");
        }

        var ranges = new List<(int Start, int End)>();
        foreach (var node in document.Descendants())
        {
            switch (node)
            {
                case HtmlBlock html:
                    var (start, end) = Range(html);
                    BodyLinkScanner.AddComments(markdown, start, end, ranges);
                    break;
                case HtmlInline { Tag: { } tag } inline when tag.StartsWith("<!--", StringComparison.Ordinal):
                    ranges.Add(Range(inline));
                    break;
            }
        }

        if (ranges.Count == 0)
        {
            return (markdown, "");
        }

        var text = new StringBuilder();
        var comments = new StringBuilder();
        var at = 0;
        foreach (var (start, end) in ranges.OrderBy(r => r.Start))
        {
            if (start < at)
            {
                continue;
            }

            text.Append(markdown, at, start - at).Append(' ');
            var comment = markdown[start..end];
            comment = comment.StartsWith("<!--", StringComparison.Ordinal) ? comment[4..] : comment;
            comment = comment.EndsWith("-->", StringComparison.Ordinal) ? comment[..^3] : comment;
            comments.Append(comments.Length > 0 ? "\n" : "").Append(comment.Trim());
            at = end;
        }

        text.Append(markdown, at, markdown.Length - at);
        return (text.ToString(), comments.ToString());

        (int, int) Range(MarkdownObject o) =>
            (o.Span.Start, Math.Min(markdown.Length, Math.Max(o.Span.Start, o.Span.End) + 1));
    }

    /// <summary>
    /// <paramref name="markdown"/> with its HTML comments taken out and no sign
    /// they were there, for a read that mustn't show them (format.md F-VIEW-6).
    /// A text with none, or with one only in code, comes back as it was.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Split"/>, this errs towards hiding: what CommonMark would
    /// show as text because a comment is badly formed may still be what the writer
    /// meant to hide. So it looks for <c>&lt;!--</c> everywhere except where code
    /// makes it text or a backslash escapes it, rather than only at what the parser
    /// calls a comment. It takes LF line breaks, as reads return text.
    /// </remarks>
    public static string Strip(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (!markdown.Contains("<!--", StringComparison.Ordinal))
        {
            return markdown;
        }

        // A body too deep to parse (F-FM-2) has no code to spare, so everything that looks like a comment goes.
        var document = BodyLinkScanner.Parse(markdown, 0, BodyLinkScanner.Plain);
        var ranges = CommentsToStrip(markdown, document);
        return ranges.Count == 0 ? markdown : Without(markdown, ranges);
    }

    private static List<(int Start, int End)> CommentsToStrip(string markdown, MarkdownDocument? document)
    {
        List<(int Start, int End)> code = document is null ? [] : CodeRanges(markdown, document);
        var ranges = new List<(int Start, int End)>();
        var at = 0;
        while (NextOpen(markdown, at, code) is var open && open >= 0)
        {
            var end = EndOfComment(markdown, open, code) ?? EndOfBlock(markdown, open, document);
            ranges.Add((open, end));
            at = end;
        }

        return ranges;
    }

    private static List<(int Start, int End)> CodeRanges(string markdown, MarkdownDocument document)
    {
        var ranges = new List<(int Start, int End)>();
        foreach (var node in document.Descendants())
        {
            // A fenced block is a CodeBlock too. One nothing closes has a span that can stop before its last lines.
            if (node is CodeBlock or CodeInline)
            {
                var end = Math.Max(node.Span.Start, node.Span.End);
                if (node is CodeBlock { Lines.Count: > 0 } block)
                {
                    end = Math.Max(end, block.Lines.Lines[block.Lines.Count - 1].Slice.End);
                }

                ranges.Add((node.Span.Start, Math.Min(markdown.Length, end + 1)));
            }
        }

        return ranges;
    }

    private static bool InCode(int offset, List<(int Start, int End)> code) => code.Exists(c => offset >= c.Start && offset < c.End);

    private static int NextOpen(string markdown, int from, List<(int Start, int End)> code)
    {
        for (var open = markdown.IndexOf("<!--", from, StringComparison.Ordinal); open >= 0; open = markdown.IndexOf("<!--", open + 4, StringComparison.Ordinal))
        {
            if (!InCode(open, code) && !IsEscaped(markdown, open))
            {
                return open;
            }
        }

        return -1;
    }

    private static bool IsEscaped(string markdown, int offset)
    {
        var slashes = 0;
        while (offset - slashes > 0 && markdown[offset - slashes - 1] == '\\')
        {
            slashes++;
        }

        return slashes % 2 == 1;
    }

    /// <summary>
    /// Where the comment at <paramref name="open"/> ends, or null if nothing closes
    /// it. It's at the first <c>--&gt;</c>, as in CommonMark, unless another
    /// <c>&lt;!--</c> opened inside it and a later <c>--&gt;</c> balances the two:
    /// then the text between them is what the writer meant to hide too.
    /// </summary>
    private static int? EndOfComment(string markdown, int open, List<(int Start, int End)> code)
    {
        // "<!-->" and "<!--->" are complete comments in CommonMark 0.31.2.
        foreach (var shortForm in new[] { open + 2, open + 3 })
        {
            if (string.CompareOrdinal(markdown, shortForm, "-->", 0, 3) == 0)
            {
                return shortForm + 3;
            }
        }

        int? first = null;
        var depth = 1;
        var at = open + 4;
        while (at < markdown.Length)
        {
            var nextOpen = markdown.IndexOf("<!--", at, StringComparison.Ordinal);
            var nextClose = markdown.IndexOf("-->", at, StringComparison.Ordinal);
            if (nextClose < 0)
            {
                break;
            }

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth += InCode(nextOpen, code) ? 0 : 1;
                at = nextOpen + 4;
            }
            else
            {
                at = nextClose + 3;
                if (InCode(nextClose, code))
                {
                    continue;
                }

                first ??= at;
                if (--depth == 0)
                {
                    return at;
                }
            }
        }

        return first;
    }

    /// <summary>
    /// The end of the block a comment nothing closes is in. An HTML block has
    /// already run to the end of the text, as CommonMark has it. In a paragraph or
    /// heading, the blocks after it are more likely meant to be read than hidden.
    /// </summary>
    private static int EndOfBlock(string markdown, int open, MarkdownDocument? document)
    {
        var block = document?.Descendants().OfType<LeafBlock>().LastOrDefault(b => b.Span.Start <= open && open <= b.Span.End);
        if (block is not null)
        {
            return Math.Min(markdown.Length, block.Span.End + 1);
        }

        var blank = markdown.IndexOf("\n\n", open, StringComparison.Ordinal);
        return blank < 0 ? markdown.Length : blank;
    }

    private static string Without(string markdown, List<(int Start, int End)> ranges)
    {
        var text = new StringBuilder();
        var touched = new HashSet<int>();
        var line = 0;
        var at = 0;
        foreach (var (start, end) in ranges)
        {
            line += Append(markdown, at, start, text);
            touched.Add(line);
            at = end;

            // The comment's line reads as it would with no comment there: no doubled or trailing space, and no new indent.
            var lineStart = text.ToString().LastIndexOf('\n') + 1;
            var isFirst = string.IsNullOrWhiteSpace(text.ToString(lineStart, text.Length - lineStart));
            var spaceBefore = text.Length > lineStart && text[^1] is ' ' or '\t';
            var spaceAfter = at < markdown.Length && markdown[at] is ' ' or '\t';
            if (isFirst || (spaceBefore && spaceAfter))
            {
                while (at < markdown.Length && markdown[at] is ' ' or '\t')
                {
                    at++;
                }
            }

            if (at >= markdown.Length || markdown[at] == '\n')
            {
                while (text.Length > lineStart && text[^1] is ' ' or '\t')
                {
                    text.Length--;
                }
            }
        }

        Append(markdown, at, markdown.Length, text);
        return string.Join('\n', WithoutEmptiedLines(text.ToString().Split('\n'), touched)).Trim('\n').TrimEnd();
    }

    private static int Append(string markdown, int from, int to, StringBuilder text)
    {
        text.Append(markdown, from, to - from);
        return markdown.AsSpan(from, to - from).Count('\n');
    }

    /// <summary>
    /// Takes out the lines a comment left empty. One left between two others stays as
    /// one blank line, since the comment was its own block and they don't become one
    /// paragraph. A list item or block quote holding only a comment goes too.
    /// </summary>
    private static List<string> WithoutEmptiedLines(string[] lines, HashSet<int> touched)
    {
        var kept = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            var emptied = EmptiedLine().Match(lines[i]);
            if (!touched.Contains(i) || !emptied.Success)
            {
                kept.Add(lines[i]);
                continue;
            }

            var hasNext = i + 1 < lines.Length;
            var blankBefore = kept.Count == 0 || IsBlank(kept[^1]);
            var blankAfter = !hasNext || IsBlank(lines[i + 1]);
            if (emptied.Groups["marker"].Success)
            {
                // "- <!-- x -->" with an indented line under it is still that line's list item.
                if (!blankAfter && lines[i + 1][0] is ' ' or '\t')
                {
                    kept.Add(lines[i]);
                }
            }
            else if (!blankBefore && !blankAfter)
            {
                kept.Add(lines[i]);
            }
            else if (blankBefore && hasNext && blankAfter && !touched.Contains(i + 1))
            {
                i++;
            }
        }

        return kept;
    }

    /// <summary>A blank line, or one with only block quote markers.</summary>
    private static bool IsBlank(string line) => BlankLine().IsMatch(line);

    [GeneratedRegex(@"^[ \t]*(>[ \t]*)*$")]
    private static partial Regex BlankLine();

    [GeneratedRegex(@"^[ \t]*(>[ \t]*)*(?<marker>([-+*]|\d{1,9}[.)])[ \t]*)?$")]
    private static partial Regex EmptiedLine();
}

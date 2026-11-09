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
    public static string Strip(string markdown) => Strip(markdown, out _);

    /// <param name="markdown">The text.</param>
    /// <param name="steps">How much looking up it took, so a test can bound the work on a large, badly formed note without timing it.</param>
    internal static string Strip(string markdown, out long steps)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        steps = 0;
        if (!markdown.Contains("<!--", StringComparison.Ordinal))
        {
            return markdown;
        }

        // A body too deep to parse (F-FM-2) has no code to spare, so everything that looks like a comment goes.
        var scan = new CommentScan(markdown, BodyLinkScanner.Parse(markdown, 0, BodyLinkScanner.Plain));
        var comments = scan.Comments();
        steps = scan.Steps;
        return comments.Count == 0 ? markdown : Without(markdown, comments);
    }

    /// <summary>
    /// Where <paramref name="markdown"/> holds what doesn't count as its prose, for
    /// the plain-text name check (format.md F-NAME-1): each HTML comment, read as
    /// widely as <see cref="Strip"/> reads it, since no read at a position shows it
    /// (F-VIEW-6); and each code span, code block, and fence's info string, whose
    /// text is code, not story. A body too deep to parse (F-FM-2) has no code.
    /// </summary>
    /// <returns>The comments' ranges and the code's, each from start to just past the end.</returns>
    internal static (List<(int Start, int End)> Comments, List<(int Start, int End)> Code) NotProse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var scan = new CommentScan(markdown, BodyLinkScanner.Parse(markdown, 0, BodyLinkScanner.Plain));
        var comments = markdown.Contains("<!--", StringComparison.Ordinal) ? scan.Comments().ConvertAll(c => (c.Start, c.End)) : [];
        return (comments, scan.Code());
    }

    private static string Without(string markdown, List<FoundComment> comments)
    {
        var text = new StringBuilder(markdown.Length);
        var touched = new HashSet<int>();
        var markers = new HashSet<int>();
        var line = 0;
        var lineStart = 0;
        var lineIsBlank = true;
        var at = 0;
        foreach (var comment in comments)
        {
            Append(comment.Start);
            touched.Add(line);
            if (comment.AfterListMarker)
            {
                markers.Add(line);
            }

            at = comment.End;

            // The comment's line reads as it would with no comment there: no doubled or trailing space, and no new indent.
            var spaceBefore = text.Length > lineStart && text[^1] is ' ' or '\t';
            var spaceAfter = at < markdown.Length && markdown[at] is ' ' or '\t';
            if (lineIsBlank || (spaceBefore && spaceAfter))
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
            else if (text.Length > lineStart && text[^1] == '`' && markdown[at] == '`')
            {
                // Two code spans side by side would otherwise join into one.
                text.Append(' ');
            }
        }

        Append(markdown.Length);
        return string.Join('\n', WithoutEmptiedLines(text.ToString().Split('\n'), touched, markers)).Trim('\n').TrimEnd();

        void Append(int to)
        {
            var added = markdown.AsSpan(at, to - at);
            var lastBreak = added.LastIndexOf('\n');
            text.Append(added);
            if (lastBreak >= 0)
            {
                line += added.Count('\n');
                lineStart = text.Length - (added.Length - lastBreak - 1);
                lineIsBlank = true;
                added = added[(lastBreak + 1)..];
            }

            lineIsBlank = lineIsBlank && added.IsWhiteSpace();
        }
    }

    /// <summary>
    /// Takes out the lines a comment left empty. One left between two others stays as
    /// one blank line, since the comment was its own block and they don't become one
    /// paragraph. A list item or block quote holding only a comment goes too.
    /// </summary>
    private static List<string> WithoutEmptiedLines(string[] lines, HashSet<int> touched, HashSet<int> markers)
    {
        var kept = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            var isBlank = touched.Contains(i) && IsBlank(lines[i]);
            if (!isBlank && !(markers.Contains(i) && OnlyAListMarker().IsMatch(lines[i])))
            {
                kept.Add(lines[i]);
                continue;
            }

            var hasNext = i + 1 < lines.Length;
            var blankBefore = kept.Count == 0 || IsBlank(kept[^1]);
            var blankAfter = !hasNext || IsBlank(lines[i + 1]);
            if (!isBlank)
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

    [GeneratedRegex(@"^[ \t]*(>[ \t]*)*([-+*]|\d{1,9}[.)])[ \t]*$")]
    private static partial Regex OnlyAListMarker();

    /// <param name="Start">Where its <c>&lt;!--</c> is.</param>
    /// <param name="End">Just past where it ends.</param>
    /// <param name="AfterListMarker">True if a list item starts on its line, before it.</param>
    private readonly record struct FoundComment(int Start, int End, bool AfterListMarker);

    /// <summary>
    /// Finds what to strip. Everything's looked up once, in sorted lists, so a note full
    /// of badly formed comments costs about what a note of well-formed ones does.
    /// </summary>
    private sealed class CommentScan
    {
        private readonly string _markdown;
        private readonly SortedRanges _code = new();
        private readonly SortedRanges _html = new();
        private readonly SortedRanges _fenceLines = new();
        private readonly SortedRanges _leaves = new();
        private readonly HashSet<int> _startsACommentBlock = [];
        private readonly List<int> _listItems = [];
        private readonly List<int> _lineStarts = [0];
        private readonly List<int> _blankLines = [];

        /// <summary>Every <c>&lt;!--</c> that isn't in code.</summary>
        private readonly List<int> _opens = [];

        /// <summary>Those that aren't in a fence's info string, which comments can't span.</summary>
        private readonly List<int> _innerOpens = [];

        /// <summary>Every <c>--&gt;</c> that isn't in code or a fence's info string.</summary>
        private readonly List<int> _closes = [];

        /// <summary>For an opening, where the <c>--&gt;</c> that balances it ends.</summary>
        private readonly Dictionary<int, int> _balanced = [];

        public CommentScan(string markdown, MarkdownDocument? document)
        {
            _markdown = markdown;
            if (document is not null)
            {
                ReadBlocks(document);
            }

            ReadLines();
            ReadMarkers();
            Balance();
        }

        public long Steps { get; private set; }

        /// <summary>The code spans and blocks, and the fences' info strings.</summary>
        public List<(int Start, int End)> Code() => [.. _code.All, .. _fenceLines.All];

        public List<FoundComment> Comments()
        {
            var comments = new List<FoundComment>();
            var at = 0;
            foreach (var open in _opens)
            {
                Steps++;
                if (open >= at && !IsEscaped(open))
                {
                    at = EndOf(open);
                    comments.Add(new FoundComment(open, at, AfterAListMarker(open)));
                }
            }

            return comments;
        }

        private void ReadBlocks(MarkdownDocument document)
        {
            foreach (var node in document.Descendants())
            {
                var end = Math.Min(_markdown.Length, Math.Max(node.Span.Start, node.Span.End) + 1);
                switch (node)
                {
                    case FencedCodeBlock fence:
                        // Its first line isn't code, and the page never shows what its info string holds.
                        var lastLine = fence.Lines.Count > 0 ? fence.Lines.Lines[fence.Lines.Count - 1].Slice.End + 1 : end;
                        var firstBreak = _markdown.IndexOf('\n', fence.Span.Start);
                        var fenceLineEnd = firstBreak < 0 ? _markdown.Length : firstBreak;
                        _fenceLines.Add(fence.Span.Start, fenceLineEnd);
                        _code.Add(fenceLineEnd, Math.Min(_markdown.Length, Math.Max(end, lastLine)));
                        _leaves.Add(fence.Span.Start, end);
                        break;
                    case CodeBlock block:
                        _code.Add(block.Span.Start, end);
                        _leaves.Add(block.Span.Start, end);
                        break;
                    case CodeInline:
                        _code.Add(node.Span.Start, end);
                        break;
                    case HtmlBlock html:
                        _html.Add(html.Span.Start, end);
                        _leaves.Add(html.Span.Start, end);
                        if (html.Type == HtmlBlockType.Comment)
                        {
                            _startsACommentBlock.Add(_markdown.IndexOf("<!--", html.Span.Start, StringComparison.Ordinal));
                        }

                        break;
                    case LeafBlock leaf:
                        _leaves.Add(leaf.Span.Start, end);
                        break;
                    case ListItemBlock item:
                        _listItems.Add(item.Span.Start);
                        break;
                }
            }

            _listItems.Sort();
        }

        private void ReadLines()
        {
            var start = 0;
            while (start <= _markdown.Length)
            {
                var lineBreak = _markdown.IndexOf('\n', start);
                var end = lineBreak < 0 ? _markdown.Length : lineBreak;
                if (BlankLine().IsMatch(_markdown.AsSpan(start, end - start)))
                {
                    _blankLines.Add(start);
                }

                if (lineBreak < 0)
                {
                    return;
                }

                start = lineBreak + 1;
                _lineStarts.Add(start);
            }
        }

        private void ReadMarkers()
        {
            for (var open = _markdown.IndexOf("<!--", StringComparison.Ordinal); open >= 0; open = _markdown.IndexOf("<!--", open + 4, StringComparison.Ordinal))
            {
                if (!_code.Contains(open, this))
                {
                    _opens.Add(open);
                    if (!_fenceLines.Contains(open, this))
                    {
                        _innerOpens.Add(open);
                    }
                }
            }

            for (var close = _markdown.IndexOf("-->", StringComparison.Ordinal); close >= 0; close = _markdown.IndexOf("-->", close + 1, StringComparison.Ordinal))
            {
                if (!_code.Contains(close, this) && !_fenceLines.Contains(close, this))
                {
                    _closes.Add(close);
                }
            }
        }

        /// <summary>Pairs each opening with the <c>--&gt;</c> that balances it, in one pass.</summary>
        private void Balance()
        {
            var open = new Stack<int>();
            var nextOpen = 0;
            foreach (var close in _closes)
            {
                while (nextOpen < _innerOpens.Count && _innerOpens[nextOpen] < close)
                {
                    Steps++;
                    open.Push(_innerOpens[nextOpen++]);
                }

                if (open.Count > 0)
                {
                    _balanced[open.Pop()] = close + 3;
                }
            }
        }

        private bool IsEscaped(int offset)
        {
            // A backslash escapes nothing in an HTML block.
            if (_html.Contains(offset, this))
            {
                return false;
            }

            var slashes = 0;
            while (offset - slashes > 0 && _markdown[offset - slashes - 1] == '\\')
            {
                slashes++;
            }

            return slashes % 2 == 1;
        }

        private int EndOf(int open)
        {
            if (_fenceLines.Find(open, this) is { } fenceLine)
            {
                var close = _markdown.IndexOf("-->", open + 2, fenceLine.End - (open + 2), StringComparison.Ordinal);
                return close < 0 ? fenceLine.End : close + 3;
            }

            return EndOfComment(open) ?? EndOfBlock(open);
        }

        /// <summary>
        /// Where the comment at <paramref name="open"/> ends, or null if nothing closes
        /// it. It's at the first <c>--&gt;</c>, even in a later paragraph, unless:
        /// another <c>&lt;!--</c> opened on the way there, in a later block of a comment
        /// CommonMark doesn't think is one, so that <c>--&gt;</c> is that one's (F-VIEW-6);
        /// or a later <c>--&gt;</c> with no blank line before it balances the two, which
        /// makes what's between them the writer's to hide too.
        /// </summary>
        private int? EndOfComment(int open)
        {
            // "<!-->" and "<!--->" are complete comments in CommonMark 0.31.2.
            foreach (var shortForm in new[] { open + 2, open + 3 })
            {
                if (string.CompareOrdinal(_markdown, shortForm, "-->", 0, 3) == 0)
                {
                    return shortForm + 3;
                }
            }

            var firstClose = FirstAtOrAfter(_closes, open + 4);
            if (firstClose < 0)
            {
                return null;
            }

            var inner = FirstAtOrAfter(_innerOpens, open + 4);
            if (inner < 0 || inner > firstClose)
            {
                return firstClose + 3;
            }

            if (HasBlankLine(open, firstClose) && !_startsACommentBlock.Contains(open))
            {
                return null;
            }

            return _balanced.TryGetValue(open, out var balanced) && !HasBlankLine(firstClose, balanced) ? balanced : firstClose + 3;
        }

        /// <summary>
        /// The end of the block a comment nothing closes is in. An HTML block has
        /// already run to the end of the text, as CommonMark has it. In a paragraph or
        /// heading, the blocks after it are more likely meant to be read than hidden.
        /// </summary>
        private int EndOfBlock(int open)
        {
            if (_leaves.Find(open, this) is { } leaf)
            {
                return leaf.End;
            }

            var blank = FirstAtOrAfter(_blankLines, open);
            return blank < 0 ? _markdown.Length : blank - 1;
        }

        private bool HasBlankLine(int from, int to)
        {
            var blank = FirstAtOrAfter(_blankLines, from);
            return blank >= 0 && blank < to;
        }

        private bool AfterAListMarker(int open)
        {
            var line = LastAtOrBefore(_lineStarts, open);
            var item = FirstAtOrAfter(_listItems, line);
            return item >= 0 && item < open;
        }

        private int FirstAtOrAfter(List<int> sorted, int value)
        {
            var index = IndexAtOrAfter(sorted, value);
            return index < sorted.Count ? sorted[index] : -1;
        }

        private int LastAtOrBefore(List<int> sorted, int value)
        {
            var index = IndexAtOrAfter(sorted, value + 1) - 1;
            return index >= 0 ? sorted[index] : 0;
        }

        private int IndexAtOrAfter(List<int> sorted, int value)
        {
            var low = 0;
            var high = sorted.Count;
            while (low < high)
            {
                Steps++;
                var middle = low + ((high - low) / 2);
                if (sorted[middle] < value)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        /// <summary>Ranges that don't overlap, added in any order and looked up by offset.</summary>
        private sealed class SortedRanges
        {
            private readonly List<(int Start, int End)> _ranges = [];
            private List<int>? _starts;

            public IReadOnlyList<(int Start, int End)> All => _ranges;

            public void Add(int start, int end)
            {
                _ranges.Add((start, end));
                _starts = null;
            }

            public bool Contains(int offset, CommentScan scan) => Find(offset, scan) is not null;

            public (int Start, int End)? Find(int offset, CommentScan scan)
            {
                if (_starts is null)
                {
                    _ranges.Sort();
                    _starts = _ranges.ConvertAll(r => r.Start);
                }

                var index = scan.IndexAtOrAfter(_starts, offset + 1) - 1;
                return index >= 0 && offset < _ranges[index].End ? _ranges[index] : null;
            }
        }
    }
}

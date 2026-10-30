using Wolfden.Core.Text;

namespace Wolfden.Core.Yaml;

/// <summary>
/// Moving one list item to another place in its list, in place (#197): the
/// item's own lines, or its own text in a flow list, move with the comments
/// that belong to it, and nothing else changes (format.md F-RT-1, F-RT-2).
/// </summary>
/// <remarks>
/// <para>
/// Which comments belong to a block list item follows the rules the other
/// edits use. A comment on the item's lines is part of it. Comment lines
/// right after it, indented deeper than its dash, are written under it, so
/// they're its own (as a removal takes them). Comment lines right above its
/// dash, with no blank line between, at or right of its dash and not
/// indented under the item before, are a heading for it (as a new item goes
/// after the item before and its comments, not between those lines and the
/// next item), so they move with it too. That includes comment lines above
/// the first item. Each item's lines are a slot, and the items move
/// between the slots: blank lines, and comment lines that aren't an
/// item's (left of the dash, or after a blank line), stay where they are,
/// as the blank lines between sections do when a section moves.
/// </para>
/// <para>
/// In a flow list, a comment on an item's own line (<c>"[[a]]",  # main</c>)
/// moves with the item, and the commas stay where they were, so a list
/// whose last item has no comma still has none. A comment anywhere else
/// inside the brackets, such as a line of its own between items, can't be
/// placed with certainty, so the move is refused.
/// </para>
/// </remarks>
internal static partial class YamlEditor
{
    /// <summary>
    /// Edits that move item <paramref name="from"/> of the list at
    /// <paramref name="path"/> so it becomes item <paramref name="to"/>:
    /// the items between shift up or down by one. Moving an item to where it
    /// is already is no edit.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> or <paramref name="to"/> isn't an item of the list.</exception>
    /// <exception cref="InvalidOperationException">The value at the path isn't a list.</exception>
    /// <exception cref="NotSupportedException">The list is written in a way an item can't be moved in place.</exception>
    public static List<TextEdit> MoveItem(SourceText source, YamlBlock block, IReadOnlyList<string> path, int from, int to)
    {
        CheckPath(path);
        if (Find(block.Root, path) is not YamlSequence sequence)
        {
            throw new InvalidOperationException($"'{Join(path, path.Count)}' isn't a list.");
        }

        var count = sequence.Items.Count;
        if (from < 0 || from >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(from), $"'{Join(path, path.Count)}' has {count} item(s), so there's no item {from}.");
        }

        if (to < 0 || to >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(to), $"'{Join(path, path.Count)}' has {count} item(s), so an item can move to 0 to {count - 1}.");
        }

        if (from == to)
        {
            return [];
        }

        return sequence.IsFlow ? FlowMove(source, sequence, from, to) : BlockMove(source, sequence, from, to);
    }

    /// <summary>The first and last lines that belong to each item of a block list (see the remarks on the class).</summary>
    private static (int First, int Last)[] BlockItemLines(SourceText source, YamlSequence sequence)
    {
        var spans = new (int First, int Last)[sequence.Items.Count];
        var previousLast = -1;
        for (var k = 0; k < spans.Length; k++)
        {
            var item = sequence.Items[k];
            var (dash, lineStart) = Dash(source, item);
            var column = dash - lineStart;
            var dashLine = source.LineIndexOf(dash);
            var first = dashLine;
            while (first - 1 > previousLast && IsCommentLine(source, first - 1) && Indent(source, first - 1) >= column)
            {
                first--;
            }

            var last = OwnedEnd(source, Math.Max(dashLine, LineIndexOfEnd(source, item)), column);
            spans[k] = (first, last);
            previousLast = last;
        }

        return spans;
    }

    /// <summary>
    /// Each item's own lines are a slot, and the items move between the
    /// slots; what's between them (blank lines, comment lines that aren't an
    /// item's) stays in place, as when a section moves (scenarios.md SC-RT-13). A
    /// slot's last line break stays with the slot, so a file without a
    /// final newline still has none (F-ENC-4).
    /// </summary>
    private static List<TextEdit> BlockMove(SourceText source, YamlSequence sequence, int from, int to)
    {
        var text = source.Text;
        var spans = BlockItemLines(source, sequence)
            .Select(s => (Start: source.Lines[s.First].Start, End: source.Lines[s.Last].End))
            .ToList();
        var order = MovedOrder(spans.Count, from, to);
        var edits = new List<TextEdit>();
        for (var slot = Math.Min(from, to); slot <= Math.Max(from, to); slot++)
        {
            var (start, end) = spans[slot];
            var moved = spans[order[slot]];
            edits.Add(new TextEdit(start, end - start, text[moved.Start..moved.End]));
        }

        return edits;
    }

    /// <summary>The item numbers in their new order: <paramref name="from"/> taken out and put back at <paramref name="to"/>.</summary>
    private static List<int> MovedOrder(int count, int from, int to)
    {
        var order = Enumerable.Range(0, count).ToList();
        order.RemoveAt(from);
        order.Insert(to, from);
        return order;
    }

    private static List<TextEdit> FlowMove(SourceText source, YamlSequence sequence, int from, int to)
    {
        var layout = FlowLayout.Of(source, sequence);
        layout.RefuseLeadingCommas(sequence);
        var text = source.Text;
        var items = layout.Items;
        var count = items.Count;
        var ownLines = Enumerable.Range(0, count).All(layout.OwnLine);

        // Each item's text, and on a line of its own, the comment after it
        // (with the spaces before it): what moves.
        var parts = new (int Start, int End, int TailStart, int TailEnd)[count];
        for (var k = 0; k < count; k++)
        {
            var (start, end) = items[k];
            var tailStart = end;
            var tailEnd = end;
            if (ownLines)
            {
                var p = end;
                while (p < text.Length && text[p] is ' ' or '\t')
                {
                    p++;
                }

                tailStart = p < text.Length && text[p] == ',' ? p + 1 : end;
                tailEnd = source.Lines[source.LineIndexOf(end)].End;
                if (!text.AsSpan(tailStart, tailEnd - tailStart).Contains('#'))
                {
                    tailStart = tailEnd;
                }
            }

            parts[k] = (start, end, tailStart, tailEnd);
        }

        // Any other comment between the brackets can't be placed for sure.
        var gapStart = layout.Open + 1;
        for (var k = 0; k <= count; k++)
        {
            var gapEnd = k < count ? parts[k].Start : layout.Close;
            if (layout.HasComment(gapStart, gapEnd))
            {
                throw new NotSupportedException(
                    $"A comment in the list at line {sequence.Line} isn't on an item's own line, so it can't be told which item it goes with, and the item can't be moved in place.");
            }

            if (k < count)
            {
                gapStart = parts[k].TailStart < parts[k].TailEnd ? parts[k].TailEnd : parts[k].End;
            }
        }

        var order = MovedOrder(count, from, to);
        var edits = new List<TextEdit>();
        for (var slot = 0; slot < count; slot++)
        {
            var moved = order[slot];
            if (moved == slot)
            {
                continue;
            }

            var (start, end, tailStart, tailEnd) = parts[slot];
            var other = parts[moved];
            edits.Add(new TextEdit(start, end - start, text[other.Start..other.End]));
            if (ownLines)
            {
                var tail = text[other.TailStart..other.TailEnd];
                if (tailStart == tailEnd && tail.Length > 0)
                {
                    // No comment here before: the moved one goes at the end of the line.
                    edits.Add(new TextEdit(tailEnd, 0, tail));
                }
                else if (tailStart < tailEnd)
                {
                    edits.Add(new TextEdit(tailStart, tailEnd - tailStart, tail));
                }
            }
        }

        return edits;
    }
}

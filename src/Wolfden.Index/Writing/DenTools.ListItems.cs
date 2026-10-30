using System.Globalization;
using Wolfden.Core.Documents;
using Wolfden.Core.Yaml;
using Wolfden.Index.Reading;

namespace Wolfden.Index.Writing;

/// <summary>
/// Moving and replacing one list item in place (#197): the <c>move</c>
/// change of <see cref="UpdateFields"/>, and the <c>move</c> and
/// <c>replace</c> changes of <see cref="UpdateChapterFields"/> and
/// <see cref="UpdateChapterDefaults"/>. Unlike taking an item out and adding
/// it again, they keep the comments that belong to the item
/// (<see cref="MarkdownFile.WithListItemMoved"/>, format.md F-RT-7). Dated
/// entries are never moved (F-TMP-7).
/// </summary>
public sealed partial class DenTools
{
    /// <summary>Moves the list item at <paramref name="path"/> (the list's path, then its item number) so it becomes item <paramref name="to"/>.</summary>
    private static MarkdownFile MoveItem(MarkdownFile file, IReadOnlyList<string> path, int to, List<string> notes)
    {
        var listPath = path.Take(path.Count - 1).ToList();
        if (!file.CanEdit)
        {
            // The file's own error comes first: it's what stops the edit.
            return file.WithListItemMoved(listPath, 0, to);
        }

        if (path.Count < 2 || !int.TryParse(path[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
            || file.GetFrontmatterValue(listPath) is not YamlSequence list)
        {
            throw new DenToolException(
                ToolErrorCodes.BadArgument,
                $"'{string.Join('.', path)}' isn't an item of a list. A 'move' change names the item by its path, such as 'fields.pov.2', and where it goes in 'index'.");
        }

        RefuseEntryMove(list, string.Join('.', listPath));
        if (from >= list.Items.Count)
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, $"'{string.Join('.', listPath)}' has {list.Items.Count} item(s), so there's no item {from}.");
        }

        var moved = file.WithListItemMoved(listPath, from, to, out var kept, out var joined);
        NoteListHeading(kept, joined, string.Join('.', listPath), notes);
        return moved;
    }

    /// <summary>
    /// Says, in <paramref name="notes"/>, when a move changed which comment
    /// lines head a block list (format.md F-RT-7): those above the first
    /// item stay at the top whichever item is first, so a heading the old
    /// first item seemed to have stays, and one the new first item brought
    /// up now heads the whole list.
    /// </summary>
    private static void NoteListHeading(IReadOnlyList<string> kept, IReadOnlyList<string> joined, string list, List<string> notes)
    {
        if (kept.Count > 0)
        {
            notes.Add($"{Quoted(kept)} {(kept.Count == 1 ? "is" : "are")} above the whole {list} list, so {(kept.Count == 1 ? "it" : "they")} stayed at the top, over the new first item, rather than moving with the old one (format.md F-RT-7).");
        }

        if (joined.Count > 0)
        {
            notes.Add($"{Quoted(joined)} {(joined.Count == 1 ? "is" : "are")} now above the whole {list} list, and {(joined.Count == 1 ? "stays" : "stay")} at the top when items move (format.md F-RT-7).");
        }

        static string Quoted(IReadOnlyList<string> comments) => string.Join(", ", comments.Select(c => $"'# {c}'"));
    }

    /// <summary>
    /// A chapter field's or default's <c>move</c> (item <c>item</c> to
    /// <c>index</c>) or <c>replace</c> (item <c>item</c> with <c>value</c>),
    /// keeping the item's comments. A replace that drops comments inside a
    /// multi-line item says so in <paramref name="notes"/>.
    /// </summary>
    /// <param name="owner">How a message names the field's holder: "The chapter's" or "The default".</param>
    private static MarkdownFile InPlace(MarkdownFile file, string[] fieldPath, Change op, string owner, List<string> notes)
    {
        if (!file.CanEdit)
        {
            // The file's own error comes first: it's what stops the edit.
            return file.WithListItemMoved(fieldPath, 0, 0);
        }

        if (file.GetFrontmatterValue(fieldPath) is not YamlSequence list)
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, $"{owner} '{op.Field}' isn't a list, so it has no item to {op.Op}. To change its value, use 'set'.");
        }

        if (op.Item is not { } item)
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, $"A '{op.Op}' change names the item by its number from 0, in 'item'.");
        }

        if (item >= list.Items.Count)
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, $"{owner} '{op.Field}' has {list.Items.Count} item(s), so there's no item {item}.");
        }

        if (op.Op == "move")
        {
            RefuseEntryMove(list, op.Field!);
            var moved = file.WithListItemMoved(fieldPath, item, op.Index!.Value, out var kept, out var joined);
            NoteListHeading(kept, joined, op.Field!, notes);
            return moved;
        }

        var before = file.FrontmatterComments;
        var result = file.WithFrontmatterValue([.. fieldPath, item.ToString(CultureInfo.InvariantCulture)], op.Value!);
        NoteReplacedComments(before, result, $"item {item} of {op.Field}", notes);
        return result;
    }

    /// <summary>Refuses to reorder a list of dated entries (format.md F-TMP-7).</summary>
    private static void RefuseEntryMove(YamlSequence list, string what)
    {
        if (list.Items.Any(i => i is YamlMapping m && m.ContainsKey("value")))
        {
            throw new DenToolException(
                ToolErrorCodes.UnsupportedEdit,
                $"'{what}' is a list of dated entries, which are never reordered (F-TMP-7): of two entries with the same 'from', the later one in the file holds (F-TMP-5), so a move could change what holds. Change an entry's 'from' instead.");
        }
    }

    /// <summary>
    /// Says, in <paramref name="notes"/>, which comments a value set or
    /// replaced in place took with it: those inside a multi-line value it
    /// replaced whole. A comment on a scalar's line stays (F-RT-7).
    /// </summary>
    private static void NoteReplacedComments(IReadOnlyList<string> before, MarkdownFile after, string what, List<string> notes)
    {
        var left = after.FrontmatterComments.ToList();
        var lost = before.Where(c => !left.Remove(c)).ToList();
        if (lost.Count > 0)
        {
            notes.Add($"Replaced {what} whole, so the comment{(lost.Count == 1 ? "" : "s")} inside the old value went: {string.Join(", ", lost.Select(c => $"'# {c.Trim()}'"))}.");
        }
    }

    private static DenToolException ListAsItem() =>
        new(ToolErrorCodes.BadArgument, "A list item is one value, not a list: give the item itself, or 'set' the whole field to a list.");
}

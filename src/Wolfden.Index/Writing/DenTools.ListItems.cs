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
/// (<see cref="MarkdownFile.WithListItemMoved"/>).
/// </summary>
public sealed partial class DenTools
{
    /// <summary>Moves the list item at <paramref name="path"/> (the list's path, then its item number) so it becomes item <paramref name="to"/>.</summary>
    private static MarkdownFile MoveItem(MarkdownFile file, IReadOnlyList<string> path, int to)
    {
        var listPath = path.Take(path.Count - 1).ToList();
        if (path.Count < 2 || !int.TryParse(path[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
            || file.GetFrontmatterValue(listPath) is not YamlSequence list)
        {
            throw new DenToolException(
                ToolErrorCodes.BadArgument,
                $"'{string.Join('.', path)}' isn't an item of a list. A 'move' change names the item by its path, such as 'fields.pov.2', and where it goes in 'index'.");
        }

        if (from >= list.Items.Count)
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, $"'{string.Join('.', listPath)}' has {list.Items.Count} item(s), so there's no item {from}.");
        }

        return file.WithListItemMoved(listPath, from, to);
    }

    /// <summary>
    /// A chapter field's or default's <c>move</c> (item <c>item</c> to
    /// <c>index</c>) or <c>replace</c> (item <c>item</c> with <c>value</c>),
    /// keeping the item's comments.
    /// </summary>
    /// <param name="owner">How a message names the field's holder: "The chapter's" or "The default".</param>
    private static MarkdownFile InPlace(MarkdownFile file, string[] fieldPath, Change op, string owner)
    {
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

        return op.Op == "move"
            ? file.WithListItemMoved(fieldPath, item, op.Index!.Value)
            : file.WithFrontmatterValue([.. fieldPath, item.ToString(CultureInfo.InvariantCulture)], op.Value!);
    }
}

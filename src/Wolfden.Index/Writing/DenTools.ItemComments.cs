using Wolfden.Core.Documents;
using Wolfden.Core.Yaml;

namespace Wolfden.Index.Writing;

public sealed partial class DenTools
{
    /// <summary>
    /// Says, in a chapter field edit's effects, when a list item's comment
    /// went with it (#225), as <see cref="UpdateFields"/> does for a note's
    /// fields: an item removed with a comment on it, one removed and added
    /// again elsewhere (moved), or one removed and a new value added in its
    /// place (rewritten), each written afresh without its comment. The
    /// <c>move</c> and <c>replace</c> changes keep an item's comments (#197),
    /// and the web UI sends those, so these notes are for a client that
    /// takes an item out and adds it again.
    /// </summary>
    private sealed class ItemComments
    {
        private readonly List<(string List, string Value, int Index, string Item, List<string> Comments)> _removed = [];

        /// <summary>Notes what <paramref name="op"/> did to the comments of the list at <paramref name="fieldPath"/>, which <paramref name="label"/> names.</summary>
        public void After(Change op, MarkdownFile before, MarkdownFile after, string[] fieldPath, string label, int notesBefore, List<string> notes)
        {
            if (op.Op == "remove")
            {
                // An emptied list's comments are already noted (NoteEmptied).
                if (notes.Count != notesBefore || before.GetFrontmatterValue(fieldPath) is not YamlSequence list
                    || Lost(before.FrontmatterComments, after.FrontmatterComments) is not { Count: > 0 } lost)
                {
                    return;
                }

                var item = ItemToRemove(before, fieldPath, op);
                _removed.Add((label, JsonOf(list.Items[item]).GetRawText(), item, $"item {item} of {label}", lost));
            }
            else if (op.Op == "add" && after.GetFrontmatterValue(fieldPath) is YamlSequence added)
            {
                var at = op.Index ?? added.Items.Count - 1;
                var value = JsonOf(added.Items[at]).GetRawText();
                var moved = _removed.FindIndex(r => r.List == label && r.Value == value);
                if (moved >= 0)
                {
                    var r = _removed[moved];
                    _removed.RemoveAt(moved);
                    notes.Add($"Moved an item of {label} to item {at}, written afresh, so the comment on it was dropped: {Quoted(r.Comments)}. Put it back by hand if you want it; a 'move' change moves an item in place, with its comments.");
                    return;
                }

                // Taken out and a new value added in its place: the item was changed (a 'replace' keeps its comments).
                var rewritten = _removed.FindLastIndex(r => r.List == label && r.Index == at);
                if (rewritten >= 0)
                {
                    var r = _removed[rewritten];
                    _removed.RemoveAt(rewritten);
                    notes.Add($"Rewrote item {at} of {label}; the comment on it was dropped: {Quoted(r.Comments)}. Put it back by hand if you want it; a 'replace' change changes an item in place, keeping its comments.");
                }
            }
        }

        /// <summary>Notes the items removed with a comment that weren't added again.</summary>
        public void Finish(List<string> notes) =>
            notes.AddRange(_removed.Select(r => $"Removed {r.Item} with the comment on it: {Quoted(r.Comments)}."));

        private static string Quoted(List<string> comments) => string.Join(", ", comments.Select(c => $"'# {c.Trim()}'"));

        // The comments in `before` that `after` doesn't have, counting repeats.
        private static List<string> Lost(IReadOnlyList<string> before, IReadOnlyList<string> after)
        {
            var left = after.ToList();
            return before.Where(c => !left.Remove(c)).ToList();
        }
    }
}

using System.Globalization;

namespace Wolfden.Index.Reading;

/// <summary>
/// The words the MCP tools and <see cref="DenReader"/> use when their errors
/// and effects advise what to do next: the names of parameters and of the
/// tools to call. An MCP client gets the tools' own (<see cref="Mcp"/>); a
/// command line that runs the same tools, such as <c>wolfden copy</c>, gives
/// its options and commands instead, and the web API its own words, so their
/// messages never name a parameter they don't have.
/// </summary>
/// <param name="AllowRelinks">How to make a change that relinks anyway, after "or": <c>pass allow_relinks: true</c>.</param>
/// <param name="NewName">Where a new name goes, after "Give the copy's name": <c>in 'new_name'</c>.</param>
/// <param name="Slug">What sets a file name: <c>slug</c>.</param>
/// <param name="Universe">Where a universe key goes, after "Give its key": <c>in 'universe'</c>.</param>
/// <param name="FindEntity">How to find an entity, lowercase: <c>use search or list_entities</c>.</param>
/// <param name="CheckDen">How to see more of what check reports, lowercase: <c>call check</c>.</param>
/// <param name="CheckTool">How to see why the den doesn't load, lowercase: <c>call the check tool</c>.</param>
/// <param name="CheckPath">How to see one file's errors, lowercase, with <c>{0}</c> for its path: <c>call check with path '{0}'</c>.</param>
/// <param name="PinnedTo">
/// What pins every read to one universe, with <c>{0}</c> for its key, to
/// start a sentence: <c>This session is pinned to universe '{0}' (wolfden mcp --universe)</c>.
/// </param>
/// <param name="AllUniverseReads">What alone takes every universe at once, after "is only for": <c>search and list_entities</c>.</param>
/// <param name="NameLinks">
/// What rewrites the links that used an entity's old name, as a sentence:
/// <c>Renaming the note (rename_entity) rewrites them.</c>
/// </param>
/// <param name="AliasLinks">
/// What rewrites the links that used a removed alias, as a sentence:
/// <c>Changing an alias with change_alias rewrites them.</c>
/// </param>
/// <param name="AskAgain">
/// How to ask again for one of an ambiguous name's matches, as a sentence:
/// <c>Ask again with one's key or path, or name its universe.</c>
/// </param>
/// <param name="ChapterKeys">
/// Where to find a position that resolves, lowercase, for a refused one:
/// <c>take chapter keys from get_chapter</c>.
/// </param>
/// <param name="Unfreeze">
/// What to do about a final (frozen) book that refused a write (F-BOOK-1,
/// #209), as a sentence, with <c>{0}</c> for the book's key and <c>{1}</c>
/// for its universe's: <c>Ask the writer whether to unfreeze book '{0}'; only they can.</c>
/// An AI agent can't unfreeze a book, so it asks; a writer is told how.
/// </param>
/// <param name="ReplaceOrClear">
/// What to do about a chapter field (or chapter default) whose value an
/// edit can't change in place, such as an <c>add</c> to a value that isn't a
/// list (#233), as a sentence, with <c>{0}</c> for what clearing does, after
/// "to" (<c>go back to what the chapter inherits from chapter_defaults</c>),
/// and <c>{1}</c> for the web page's button that does it:
/// <c>To replace it, send a 'set' change with the whole value; to {0}, send a 'clear' change.</c>
/// </param>
/// <param name="SetOne">
/// What to do instead of adding to or removing from a chapter field that
/// takes one value, not a list (#233), as a sentence:
/// <c>To change it, send a 'set' change with the new value.</c>
/// </param>
/// <param name="ItemsListed">
/// Where to see a chapter field's list items, for a <c>remove</c> that names
/// one that isn't there (#233), as a sentence, with <c>{0}</c> for the MCP
/// tool that reads them and <c>{1}</c> for the API path:
/// <c>{0} lists its items; name one by its number from 0 in 'item'.</c>
/// </param>
/// <param name="NotWritten">
/// What to do about a change of a chapter field's list items where the
/// chapter (or container) doesn't write the field, so it inherits it (#197),
/// as a sentence, with <c>{0}</c> for the field and <c>{1}</c> for where it
/// inherits from (<c>chapter_defaults</c>):
/// <c>It inherits '{0}' from {1}: to change it there, use update_chapter_defaults; to give it a value of its own here, send a 'set' change with the whole value.</c>
/// </param>
public sealed record ToolWords(
    string AllowRelinks, string NewName, string Slug, string Universe, string FindEntity, string CheckDen, string CheckTool, string CheckPath,
    string PinnedTo = "This session is pinned to universe '{0}' (wolfden mcp --universe)",
    string AllUniverseReads = "search and list_entities",
    string AskAgain = "Ask again with one's key or path, or name its universe.",
    string NameLinks = "Renaming the note (rename_entity) rewrites them.",
    string AliasLinks = "Changing an alias with change_alias rewrites them.",
    string ChapterKeys = "take chapter keys from get_chapter",
    string Unfreeze = "Ask the writer whether to unfreeze book '{0}'; only they can.",
    string ReplaceOrClear = "To replace it, send a 'set' change with the whole value; to {0}, send a 'clear' change.",
    string SetOne = "To change it, send a 'set' change with the new value.",
    string ItemsListed = "{0} lists its items; name one by its number from 0 in 'item'.",
    string NotWritten = "It inherits '{0}' from {1}: to change it there, use update_chapter_defaults; to give it a value of its own here, send a 'set' change with the whole value.")
{
    /// <summary>The MCP tools' own words, the default.</summary>
    public static ToolWords Mcp { get; } = new(
        "pass allow_relinks: true", "in 'new_name'", "slug", "in 'universe'", "use search or list_entities", "call check", "call the check tool",
        "call check with path '{0}'");

    /// <summary><see cref="CheckPath"/> for <paramref name="path"/>.</summary>
    public string CheckFile(string path) => string.Format(CultureInfo.InvariantCulture, CheckPath, path);

    /// <summary><see cref="PinnedTo"/> for the universe <paramref name="universe"/>.</summary>
    public string Pinned(string universe) => string.Format(CultureInfo.InvariantCulture, PinnedTo, universe);

    /// <summary><see cref="Unfreeze"/> for book <paramref name="book"/> of universe <paramref name="universe"/>.</summary>
    public string Unfreezing(string book, string universe) => string.Format(CultureInfo.InvariantCulture, Unfreeze, book, universe);

    /// <summary><see cref="ReplaceOrClear"/>, where clearing does <paramref name="clearTo"/> and the web page's button is <paramref name="clearButton"/>.</summary>
    public string Replacing(string clearTo, string clearButton) => string.Format(CultureInfo.InvariantCulture, ReplaceOrClear, clearTo, clearButton);

    /// <summary><see cref="ItemsListed"/>, where the MCP tool <paramref name="tool"/> or the API's <paramref name="apiPath"/> reads the items.</summary>
    public string ListingItems(string tool, string apiPath) => string.Format(CultureInfo.InvariantCulture, ItemsListed, tool, apiPath);

    /// <summary><see cref="NotWritten"/> for <paramref name="field"/>, inherited from <paramref name="from"/>.</summary>
    public string Inheriting(string field, string from) => string.Format(CultureInfo.InvariantCulture, NotWritten, field, from);

    /// <summary><paramref name="words"/> with its first letter capitalized, to start a sentence.</summary>
    public static string Cap(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
}

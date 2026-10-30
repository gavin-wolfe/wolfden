using System.Text;
using System.Text.RegularExpressions;
using Wolfden.Core.Links;
using Wolfden.Core.Yaml;

namespace Wolfden.Core.Structure;

/// <summary>A link in a chapter field's value that a heading shows (format.md F-CTR-6).</summary>
/// <param name="Text">The link as written, such as <c>[[ines-ardent|Ardent]]</c>.</param>
/// <param name="Target">The target, trimmed.</param>
/// <param name="Heading">The <c>#heading</c>, or null.</param>
/// <param name="Label">The label, or null.</param>
public sealed record ChapterHeadingLink(string Text, string Target, string? Heading, string? Label);

/// <summary>One item of a chapter field's value, as a heading shows it: its text, with the links in it.</summary>
/// <param name="Text">The item's text as written, links and all.</param>
/// <param name="Links">The links in <paramref name="Text"/>, in order.</param>
public sealed record ChapterHeadingItem(string Text, IReadOnlyList<ChapterHeadingLink> Links);

/// <summary>One field a chapter's heading is built from, with the items that hold.</summary>
/// <param name="Field">The chapter field's name, as <c>chapter_heading</c> lists it.</param>
/// <param name="Scope">The container whose file holds the value, which its links resolve from (types.md F-CHAP-4).</param>
/// <param name="Items">The items that hold (types.md F-CHAP-3), never empty.</param>
public sealed record ChapterHeadingPart(string Field, Container Scope, IReadOnlyList<ChapterHeadingItem> Items);

/// <summary>
/// Chapter headings (format.md F-CTR-6, #227): a container's
/// <c>chapter_heading</c> lists chapter fields, and a chapter's displayed
/// heading is its title, if it has one, then the effective values of those
/// fields, in that order, such as "Ardent — Vesta Yards, 2350-04-12".
/// Every surface (the read API, the index, the MCP tools) builds headings
/// here, and differs only in how it names a link's entity.
/// </summary>
public static partial class ChapterHeadings
{
    /// <summary>The container key.</summary>
    public const string Key = "chapter_heading";

    /// <summary>Between the title and the fields.</summary>
    public const string TitleSeparator = " · ";

    /// <summary>Between the first field shown and the rest.</summary>
    public const string LeadSeparator = " — ";

    /// <summary>Between the fields after the first.</summary>
    public const string FieldSeparator = ", ";

    /// <summary>Between the items of a list field, such as two POV characters.</summary>
    public const string ItemSeparator = " & ";

    /// <summary>
    /// The <c>chapter_heading</c> that holds for <paramref name="book"/>'s
    /// chapters: the nearest container in its scope chain (book, series,
    /// universe) that sets one, or null if none does. An empty list is set,
    /// to "no fields here", and stops inheritance.
    /// </summary>
    public static IReadOnlyList<string>? FieldsFor(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.ScopeChain.Select(c => c.ChapterHeading).FirstOrDefault(h => h is not null);
    }

    /// <summary>
    /// The fields <paramref name="chapter"/>'s heading is built from, in the
    /// order <c>chapter_heading</c> lists them, each with the items of its
    /// effective value (types.md F-CHAP-5). A field the chapter has no value
    /// for, or "none here" (<c>[]</c>), is left out, and so is an item with
    /// nothing to show. Empty where no <c>chapter_heading</c> holds.
    /// </summary>
    public static IReadOnlyList<ChapterHeadingPart> Parts(Chapter chapter)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        var result = new List<ChapterHeadingPart>();
        foreach (var field in FieldsFor(chapter.Book) ?? [])
        {
            if (!chapter.Fields.TryGetValue(field, out var value))
            {
                continue;
            }

            var items = value.Items.Select(ItemOf).OfType<ChapterHeadingItem>().ToList();
            if (items.Count > 0)
            {
                result.Add(new ChapterHeadingPart(field, value.DefaultFrom ?? chapter.Book, items));
            }
        }

        return result;
    }

    /// <summary>
    /// <paramref name="chapter"/>'s displayed heading, naming each link's
    /// entity with <paramref name="nameOf"/> (null where the link doesn't
    /// reach one, or the reader shouldn't learn which: the link then shows as
    /// written). Null when the chapter has neither a title nor fields to show.
    /// </summary>
    public static string? Heading(Chapter chapter, Func<ChapterHeadingPart, ChapterHeadingLink, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(nameOf);
        return Compose(chapter.Title, Parts(chapter).Select(p => p.Items.Select(i => ItemText(i, l => LinkText(l, nameOf(p, l))))));
    }

    /// <summary><see cref="Heading(Chapter, Func{ChapterHeadingPart, ChapterHeadingLink, string?})"/>, naming each link's entity as <paramref name="links"/> resolves it from the container that holds the value.</summary>
    public static string? Heading(Chapter chapter, LinkResolver links)
    {
        ArgumentNullException.ThrowIfNull(links);
        return Heading(chapter, (part, link) =>
            links.Resolve(part.Scope, link.Target) is { Outcome: LinkOutcome.Resolved, Entity: { } entity } ? entity.Name : null);
    }

    /// <summary>
    /// A heading from a title and the shown text of each field's items: the
    /// title, then <see cref="TitleSeparator"/> and the fields. The first
    /// field with anything to show leads, then <see cref="LeadSeparator"/>,
    /// then the others, with <see cref="FieldSeparator"/> between them; a
    /// field's items are joined with <see cref="ItemSeparator"/>. White space
    /// runs, line breaks included, become one space. Null if there's nothing
    /// to show.
    /// </summary>
    public static string? Compose(string? title, IEnumerable<IEnumerable<string>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var shown = fields
            .Select(items => string.Join(ItemSeparator, items.Select(OneLine).Where(t => t.Length > 0)))
            .Where(t => t.Length > 0)
            .ToList();
        var built = shown.Count switch
        {
            0 => null,
            1 => shown[0],
            _ => shown[0] + LeadSeparator + string.Join(FieldSeparator, shown.Skip(1)),
        };
        var titled = title is null ? "" : OneLine(title);
        return (titled.Length > 0, built) switch
        {
            (true, null) => titled,
            (true, _) => titled + TitleSeparator + built,
            (false, _) => built,
        };
    }

    /// <summary>An item's text with each link replaced by <paramref name="linkText"/>'s words for it.</summary>
    public static string ItemText(ChapterHeadingItem item, Func<ChapterHeadingLink, string> linkText)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(linkText);
        var text = new StringBuilder();
        var at = 0;
        foreach (var link in item.Links)
        {
            var found = item.Text.IndexOf(link.Text, at, StringComparison.Ordinal);
            if (found < 0)
            {
                continue;
            }

            text.Append(item.Text, at, found - at).Append(linkText(link));
            at = found + link.Text.Length;
        }

        return text.Append(item.Text, at, item.Text.Length - at).ToString();
    }

    /// <summary>
    /// What a link shows in a heading, as the web UI shows a link: its label,
    /// else <paramref name="name"/> (the entity it reaches, by its current
    /// name), else its target as written; a <c>#heading</c> follows a name
    /// or target after a colon.
    /// </summary>
    public static string LinkText(ChapterHeadingLink link, string? name)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.Label is { } label)
        {
            return label;
        }

        var shown = name ?? link.Target;
        return link.Heading is { } heading ? $"{shown}: {heading}" : shown;
    }

    /// <summary>
    /// An item as a heading shows it: a scalar's text as written; a date
    /// mapping's <c>text</c>, or else its <c>sort</c> numbers joined with
    /// <c>-</c> (types.md F-FT-11). Anything else shows nothing.
    /// </summary>
    private static ChapterHeadingItem? ItemOf(YamlNode node)
    {
        var text = node switch
        {
            YamlScalar { IsNull: false } s => s.Text,
            YamlMapping m when m["text"] is YamlScalar { IsNull: false } t => t.Text,
            YamlMapping m when m["sort"] is YamlScalar { IsNull: false } n => n.Text,
            YamlMapping m when m["sort"] is YamlSequence numbers =>
                string.Join("-", numbers.Items.OfType<YamlScalar>().Select(n => n.Text)),
            _ => null,
        };
        if (text is null || OneLine(text).Length == 0)
        {
            return null;
        }

        var links = LinkSyntax.FindLinks(text).Select(l => new ChapterHeadingLink(l.Text, l.Target, l.Heading, l.Label)).ToList();
        return new ChapterHeadingItem(text, links);
    }

    private static string OneLine(string text) => Spaces().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

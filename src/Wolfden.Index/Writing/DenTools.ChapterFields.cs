using Wolfden.Core.Documents;
using Wolfden.Core.Entities;
using Wolfden.Core.Links;
using Wolfden.Core.Structure;
using Wolfden.Core.Types;
using Wolfden.Core.Yaml;
using Wolfden.Index.Reading;

namespace Wolfden.Index.Writing;

/// <summary>
/// Chapter fields as the web UI shows and edits them (#225): each chapter's
/// fields and each container's <c>chapter_defaults</c>, with the values as
/// written (so a form can send back the ones the writer didn't touch), what
/// holds and where it comes from, and the den's chapter field definitions,
/// so a field a pack adds gets its form like the core pack's. The edits
/// themselves are <see cref="UpdateChapterFields"/> and
/// <see cref="UpdateChapterDefaults"/>, the MCP tools' code.
/// </summary>
public sealed partial class DenTools
{
    /// <summary>A book's chapters with each chapter field's value as written, what holds, and what it would inherit, and the hash of <c>book.md</c>.</summary>
    public BookChapterFieldsResult GetBookChapterFields(string book, string? universe = null)
    {
        if (string.IsNullOrWhiteSpace(book))
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, "Give the book's key in 'book'.");
        }

        var (universeKey, _) = LocateBook(book.Trim(), universe);
        var den = Snapshot();
        var structure = DenStructure.Load(den);
        var found = FindBook(structure, universeKey, book.Trim());
        var links = new Lazy<LinkResolver>(() => new LinkResolver(EntityCatalog.Load(structure, den)));
        var defined = structure.Types.ChapterFields.Select(f => f.Name).ToList();
        var chapters = found.Chapters.Select(c =>
        {
            var item = ChapterItem(found.File, c.Key);
            var written = (item as YamlMapping)?["fields"] as YamlMapping;
            var names = Names(defined, WrittenNames(written).Concat(c.Fields.Keys).Concat(c.OwnFields.Keys));
            return new ChapterFieldsView(
                c.Key, $"{found.Key}:{c.Key}", c.Number, c.Title, item is YamlScalar,
                names.Select(name => ChapterValue(name, c, written, links)).ToList());
        }).ToList();
        return new BookChapterFieldsResult(
            DenReader.ResultVersion, universeKey, found.Key, found.FilePath, HashOf(found.File.ToBytes()), found.Status == BookStatus.Final,
            ChapterFieldTypes(structure), chapters);
    }

    /// <summary>A universe's, series', or book's <c>chapter_defaults</c>, each field's value as written, what its books get, and the hash of its file.</summary>
    public ChapterDefaultsSourceResult GetChapterDefaultsSource(string? universe = null, string? series = null, string? book = null)
    {
        var universeKey = LocateContainer(universe, series, book);
        var den = Snapshot();
        var structure = DenStructure.Load(den);
        var container = FindContainer(structure, universeKey, series, book);
        var links = new Lazy<LinkResolver>(() => new LinkResolver(EntityCatalog.Load(structure, den)));
        var written = container.File.Frontmatter?["chapter_defaults"] as YamlMapping;
        var names = Names(
            structure.Types.ChapterFields.Select(f => f.Name).ToList(),
            WrittenNames(written).Concat(container.ScopeChain.SelectMany(c => c.ChapterDefaults.Keys)));
        return new ChapterDefaultsSourceResult(
            DenReader.ResultVersion, universeKey, KindOf(container), container.Key, container.FilePath, HashOf(container.File.ToBytes()),
            container is Book { Status: BookStatus.Final }, ChapterFieldTypes(structure),
            names.Select(name => DefaultValue(name, container, written, links)).ToList(),
            BooksBelow(container).Select(b => new BookBelowView(b.Key, b.Status == BookStatus.Final, b.Chapters.Count, b.FilePath)).ToList());
    }

    /// <summary>
    /// The den's chapter fields as a field editor needs them. A chapter field
    /// has no <c>temporal</c>, <c>required</c>, or <c>default</c> (types.md
    /// F-CHAP-1), so those are always off.
    /// </summary>
    private static List<SourceFieldType> ChapterFieldTypes(DenStructure structure) =>
        structure.Types.ChapterFields.Select(f => FieldTypeOf(f) with { Temporal = false, Required = false, Default = null }).ToList();

    /// <summary>The defined names in field order, then any other name, by name.</summary>
    private static List<string> Names(List<string> defined, IEnumerable<string> others) =>
        [.. defined, .. others.Where(n => !defined.Contains(n, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static IEnumerable<string> WrittenNames(YamlMapping? written) =>
        written?.Entries.Select(e => e.KeyText).OfType<string>() ?? [];

    /// <summary>A chapter's item in <c>book.md</c>'s <c>chapters</c>: a mapping, or a bare key; null if it isn't there.</summary>
    private static YamlNode? ChapterItem(MarkdownFile file, string key) =>
        ((file.GetFrontmatterValue("chapters") as YamlSequence)?.Items ?? []).FirstOrDefault(i => i switch
        {
            YamlMapping m => m["key"] is YamlScalar { IsNull: false } k && k.Text == key,
            YamlScalar s => s.Text == key,
            _ => false,
        });

    private static ChapterValueView ChapterValue(string name, Chapter chapter, YamlMapping? written, Lazy<LinkResolver> links)
    {
        var set = written?.ContainsKey(name) == true;
        var effective = chapter.Fields.GetValueOrDefault(name);
        var inherited = Inherited(chapter, name);
        return new ChapterValueView(
            name, set, set ? AsWritten(written![name]) : null, set && !chapter.OwnFields.ContainsKey(name),
            effective is null ? null : AsWritten(effective.Value),
            effective is null ? null : effective.DefaultFrom is { } from ? KindOf(from) : "chapter",
            effective?.DefaultFrom?.Key,
            effective?.IsNone == true,
            inherited is null ? null : AsWritten(inherited.Value),
            inherited?.DefaultFrom is { } d ? KindOf(d) : null,
            inherited?.DefaultFrom?.Key,
            LinksOf(effective, effective?.DefaultFrom ?? chapter.Book, links));
    }

    private static ChapterValueView DefaultValue(string name, Container container, YamlMapping? written, Lazy<LinkResolver> links)
    {
        var set = written?.ContainsKey(name) == true;
        var own = container.ChapterDefaults.GetValueOrDefault(name);
        var above = Above(container, name);
        var effective = own ?? above;
        return new ChapterValueView(
            name, set, set ? AsWritten(written![name]) : null, set && own is null,
            effective is null ? null : AsWritten(effective.Value),
            effective?.DefaultFrom is { } from ? KindOf(from) : null,
            effective?.DefaultFrom?.Key,
            effective?.IsNone == true,
            above is null ? null : AsWritten(above.Value),
            above?.DefaultFrom is { } d ? KindOf(d) : null,
            above?.DefaultFrom?.Key,
            LinksOf(effective, effective?.DefaultFrom ?? container, links));
    }

    /// <summary>The links in a value's items, resolved from <paramref name="scope"/>, the container that holds the value (types.md F-CHAP-4).</summary>
    private static List<ChapterLinkView> LinksOf(ChapterFieldValue? value, Container scope, Lazy<LinkResolver> links)
    {
        if (value is null)
        {
            return [];
        }

        var result = new List<ChapterLinkView>();
        foreach (var item in value.Items.OfType<YamlScalar>().Where(s => s.Text.Contains("[[", StringComparison.Ordinal)))
        {
            foreach (var link in LinkSyntax.FindLinks(item.Text))
            {
                var resolved = links.Value.Resolve(scope, link.Target);
                var entity = resolved.Outcome == LinkOutcome.Resolved ? resolved.Entity : null;
                result.Add(new ChapterLinkView(
                    link.Text, link.Target, link.Label, resolved.Outcome.ToString().ToLowerInvariant(), entity?.Key, entity?.Name, entity?.Type, resolved.Problem));
            }
        }

        return result;
    }

    private static string KindOf(Container c) => c.Kind.ToString().ToLowerInvariant();
}

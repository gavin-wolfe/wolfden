using System.Text.Json;
using System.Text.Json.Nodes;
using Wolfden.Core.Links;
using Wolfden.Index;
using Wolfden.Index.Reading;
using Wolfden.Index.Writing;

namespace Wolfden.Api;

/// <summary>A chapter field as the Reader's view shows it (#300): what holds, and where it comes from, with nothing to edit by.</summary>
/// <param name="Name">The field's name.</param>
/// <param name="Value">What holds, each link replaced by what it shows, unless it's to a note readers have met.</param>
/// <param name="From">Where it comes from: <c>chapter</c>, <c>book</c>, <c>series</c>, or <c>universe</c>.</param>
/// <param name="FromKey">The key of that container, or null for a chapter's own.</param>
/// <param name="None">The value is [], "none here".</param>
/// <param name="Links">The links left in the value, each to a note readers have met.</param>
public sealed record ReadersChapterValue(string Name, JsonElement Value, string? From, string? FromKey, bool None, IReadOnlyList<ChapterLinkView> Links);

/// <summary>A chapter readers have reached, with its fields that have a value.</summary>
/// <param name="Key">The chapter key.</param>
/// <param name="Position">Its position.</param>
/// <param name="Number">Its display number, or null.</param>
/// <param name="Title">Its title, or null.</param>
/// <param name="Fields">The fields with a value they can see.</param>
public sealed record ReadersChapterFields(string Key, string Position, long? Number, string? Title, IReadOnlyList<ReadersChapterValue> Fields);

/// <summary>The result of <c>GET /api/book/chapter-fields</c> with <c>asOf</c> and <c>readers=true</c>.</summary>
/// <param name="Version">The shape version.</param>
/// <param name="Universe">The universe's key.</param>
/// <param name="Book">The book's key.</param>
/// <param name="Chapters">The chapters readers have reached, in book order.</param>
public sealed record ReadersChapterFieldsResult(int Version, string Universe, string Book, IReadOnlyList<ReadersChapterFields> Chapters);

/// <summary>
/// A book's chapter fields in the Reader's view (#300), by the rules for a note's fields
/// (design.md §9): nothing of a chapter they haven't reached, no position value later than
/// the position (<see cref="AsOfReads.Reached"/>), and each link what it shows. So the
/// answer holds no later name: a labelled link to a note isn't its target, and a link to
/// a note they haven't met is only the words it shows, not a link. None of what an edit
/// needs is here, such as a value as written, what it would inherit, or the file's hash.
/// </summary>
internal static class ReadersChapters
{
    public static ReadersChapterFieldsResult Get(DenReader reader, DenTools tools, string book, string? universe, string asOf)
    {
        // Only a book the tree readers know has, so a later book and one that doesn't exist answer the same, and a book's own error, which can list the others, isn't given.
        var tree = ReadEndpoints.Hierarchy(reader, universe, asOf, readers: true).Universe;
        if (!tree.Books.Concat(tree.Series.SelectMany(s => s.Books)).Any(b => b.Key == book))
        {
            throw new DenToolException(ToolErrorCodes.UnknownScope, $"There's no book '{book}' to read here.");
        }

        var whole = tools.GetBookChapterFields(book, tree.Key);
        return reader.WithIndex(index =>
        {
            var horizon = DenReader.Horizon(index, whole.Universe, asOf);
            var met = reader.CanonView(index, whole.Universe, asOf, null).Entities.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
            var positionFields = whole.FieldTypes.Where(t => t.Type == "position").Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            return new ReadersChapterFieldsResult(
                ReadEndpoints.ResultVersion,
                whole.Universe,
                whole.Book,
                whole.Chapters
                    .Where(c => horizon.HasReached(c.Position))
                    .Select(c => new ReadersChapterFields(
                        c.Key, c.Position, c.Number, ReadersLinks.Title(c.Title), c.Fields.Select(f => Known(f, met, positionFields, horizon)).OfType<ReadersChapterValue>().ToList()))
                    .ToList());
        });
    }

    private static ReadersChapterValue? Known(ChapterValueView field, HashSet<string> met, HashSet<string> positionFields, StoryHorizon horizon)
    {
        if (field.Value is not { } written)
        {
            return null;
        }

        var value = written;
        if (positionFields.Contains(field.Name))
        {
            if (AsOfReads.Reached(written, horizon) is not { } reached)
            {
                return null;
            }

            value = reached;
        }

        var links = new List<ChapterLinkView>();
        var shown = Shown(JsonNode.Parse(value.GetRawText()), field.Links, met, links);
        return new ReadersChapterValue(field.Name, JsonSerializer.SerializeToElement(shown), field.From, field.FromKey, field.None, links);
    }

    /// <summary>A value with each link in its text and its mappings' keys as it shows, adding those it keeps to <paramref name="kept"/>.</summary>
    private static JsonNode? Shown(JsonNode? value, IReadOnlyList<ChapterLinkView> links, HashSet<string> met, List<ChapterLinkView> kept)
    {
        switch (value)
        {
            case JsonArray list:
                return new JsonArray(list.Select(i => Shown(i, links, met, kept)).ToArray());
            case JsonObject map:
                var copy = new JsonObject();
                foreach (var (key, item) in map)
                {
                    copy[Text(key, links, met, kept)] = Shown(item, links, met, kept);
                }

                return copy;
            case JsonValue text when text.TryGetValue<string>(out var words):
                return JsonValue.Create(Text(words, links, met, kept));
            default:
                return value?.DeepClone();
        }
    }

    private static string Text(string text, IReadOnlyList<ChapterLinkView> links, HashSet<string> met, List<ChapterLinkView> kept) =>
        LinkSyntax.WithLinksShown(text, markdown: false, written =>
        {
            if (links.FirstOrDefault(l => l.Text == written) is not { } link)
            {
                return null;
            }

            if (link.Entity is not { } key || !met.Contains(key))
            {
                return ReadersLinks.Shown(link.Label, null, link.Target, null);
            }

            // Written again by the note's key, so an alias or anything else behind its label isn't in the answer.
            var words = ReadersLinks.Shown(link.Label, link.Name, link.Target, null);
            if (words.AsSpan().IndexOfAny("[]\r\n") >= 0)
            {
                return words;
            }

            var again = $"[[{key}|{words}]]";
            kept.Add(new ChapterLinkView(again, key, words, link.Outcome, key, link.Name, link.Type, null));
            return again;
        });
}

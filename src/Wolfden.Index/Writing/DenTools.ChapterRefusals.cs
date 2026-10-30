using Wolfden.Core.Documents;
using Wolfden.Core.Structure;
using Wolfden.Core.Types;
using Wolfden.Core.Yaml;
using Wolfden.Index.Reading;

namespace Wolfden.Index.Writing;

/// <summary>
/// Refusals of an edit of a chapter's fields or a container's
/// <c>chapter_defaults</c> (#233), in the writer's terms: each names the
/// chapter (its position and title) or the container, the file, and the
/// field, says what's written there (and why it's ignored, if it is), and
/// what to do next, never a YAML path such as <c>chapters.6.fields.setting</c>.
/// </summary>
public sealed partial class DenTools
{
    /// <summary>Where an edit of a chapter field lands, named for a refusal.</summary>
    /// <param name="Name">The chapter or container, after "in": <c>chapter ember:ch07 (The Harbour)</c>, or <c>the chapter_defaults of series 'ember'</c>.</param>
    /// <param name="FieldsName">What holds the fields: <c>the fields of chapter ember:ch07 (The Harbour)</c>, or <c>the chapter_defaults of series 'ember'</c>.</param>
    /// <param name="FieldsPlural">True if <paramref name="FieldsName"/> takes "are".</param>
    /// <param name="Path">The file, relative to the den root.</param>
    /// <param name="FieldsPath">The YAML path of what holds the fields: <c>chapters.6.fields</c>, or <c>chapter_defaults</c>.</param>
    /// <param name="ClearTo">What a <c>clear</c> does, after "to": <c>go back to what the chapter inherits from chapter_defaults</c>.</param>
    /// <param name="ClearButton">The web page's button that clears the field.</param>
    /// <param name="ReadTool">The MCP tool that reads the fields: <c>get_chapter</c> or <c>get_chapter_defaults</c>.</param>
    /// <param name="ReadPath">The API path that reads them.</param>
    /// <param name="Types">The den's types, for the chapter fields' definitions.</param>
    /// <param name="Ignored">The values written there that are ignored, as the den read the file before the edit.</param>
    private sealed record FieldPlace(
        string Name, string FieldsName, bool FieldsPlural, string Path, string[] FieldsPath, string ClearTo, string ClearButton, string ReadTool, string ReadPath,
        DenTypes Types, IReadOnlyList<IgnoredChapterField> Ignored);

    /// <summary>The longest a chapter's title runs in a message before it's cut short with "…".</summary>
    private const int TitleInMessages = 60;

    /// <summary>
    /// A chapter, named for the writer: <c>chapter ember:ch07 (The Harbour)</c>,
    /// with the title's spaces and line breaks folded to single spaces and a
    /// long title cut short with "…", so the name stays on one line.
    /// </summary>
    internal static string ChapterName(Chapter chapter)
    {
        var title = chapter.Title is { } t ? string.Join(' ', t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) : "";
        if (title.Length > TitleInMessages)
        {
            title = title[..(TitleInMessages - 1)].TrimEnd() + "…";
        }

        return $"chapter {chapter.Book.Key}:{chapter.Key}{(title.Length > 0 ? $" ({title})" : "")}";
    }

    private static FieldPlace ChapterPlace(Chapter chapter, string[] itemPath, DenTypes types) => new(
        ChapterName(chapter), $"the fields of {ChapterName(chapter)}", true, chapter.Book.FilePath, [.. itemPath, "fields"],
        "go back to what the chapter inherits from chapter_defaults", "Use the default", "get_chapter", "/api/book/chapter-fields", types, chapter.IgnoredFields);

    private static FieldPlace DefaultsPlace(Container container, DenTypes types)
    {
        var name = $"the chapter_defaults of {KindOf(container)} '{container.Key}'";

        // A universe's defaults have nothing above them to go back to.
        var clearTo = container is Universe
            ? "take it out, so its books have none unless their series or the book sets one"
            : "go back to what its books inherit from above";
        return new(
            name, name, false, container.FilePath, ["chapter_defaults"], clearTo, "Clear", "get_chapter_defaults", "/api/chapter-defaults", types,
            container.IgnoredChapterDefaults);
    }

    /// <summary>
    /// Makes one change of a chapter field (<see cref="Apply"/>), first
    /// refusing one the file's shape can't take, in the writer's terms, and
    /// rewording any other refusal so it names the chapter or container and
    /// its file rather than a YAML path.
    /// </summary>
    private MarkdownFile ApplyToField(FieldPlace place, Change op, MarkdownFile file, Func<MarkdownFile> edit)
    {
        var field = op.Field!;
        CheckFieldShape(place, op, field, file);
        try
        {
            return Apply(edit);
        }
        catch (DenToolException e) when (e.Code is ToolErrorCodes.BadArgument or ToolErrorCodes.UnsupportedEdit)
        {
            var message = Reworded(place, field, e.Message);
            throw message == e.Message ? e : new DenToolException(e.Code, message, e.Candidates);
        }
    }

    /// <summary>The changes that work on a list field's items, which <see cref="CheckFieldShape"/> checks the list for.</summary>
    private static readonly HashSet<string> ListItemOps = ["add", "remove"];

    /// <summary>What a change of a list's items can't do on a value that isn't a list, after "so".</summary>
    private static string NoList(string op) => op switch
    {
        "add" => "'add' has no list to add an item to",
        "remove" => "'remove' has no list item to take out",
        _ => $"'{op}' has no list item to change",
    };

    /// <summary>
    /// Refuses an edit that what's written can't take: fields that aren't a
    /// mapping; or a change of a list's items (<see cref="ListItemOps"/>) on a
    /// field that takes one value, on a value that isn't a list, or naming an
    /// item that isn't there.
    /// </summary>
    private void CheckFieldShape(FieldPlace place, Change op, string field, MarkdownFile file)
    {
        var holder = file.GetFrontmatterValue(place.FieldsPath);
        if (holder is not (null or YamlMapping or YamlScalar { IsNull: true }))
        {
            var (are, they, them) = place.FieldsPlural ? ("are", "they're", "them") : ("is", "it's", "it");
            throw new DenToolException(
                ToolErrorCodes.UnsupportedEdit,
                $"In {place.Path}, {place.FieldsName} {are} written as {Written(holder)}, not a mapping from chapter field to value, so {they} ignored, " +
                $"and '{field}' can't be changed in place. Fix {them} by hand first, such as '{{ {field}: ... }}'.");
        }

        if (!ListItemOps.Contains(op.Op))
        {
            return;
        }

        var at = $"'{field}' in {place.Name} ({place.Path})";
        if (place.Types.FindChapterField(field) is { IsList: false })
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, $"{at} takes one value, not a list, so {NoList(op.Op)}. {Words.SetOne}");
        }

        string[] fieldPath = [.. place.FieldsPath, field];
        switch (file.GetFrontmatterValue(fieldPath))
        {
            case null when op.Op != "add":
                throw new DenToolException(ToolErrorCodes.BadArgument, $"{ToolWords.Cap(place.Name)} ({place.Path}) doesn't write '{field}', so {NoList(op.Op)}.");
            case YamlScalar { IsNull: true } when op.Op != "add":
                throw new DenToolException(
                    ToolErrorCodes.BadArgument,
                    $"{at} is written with no value, so {NoList(op.Op)}. {Words.Replacing(place.ClearTo, place.ClearButton)}");
            case null or YamlScalar { IsNull: true }:
                return;
            case YamlSequence:
                if (op.Op == "remove")
                {
                    try
                    {
                        ItemToRemove(file, fieldPath, op);
                    }
                    catch (DenToolException e)
                    {
                        throw new DenToolException(e.Code, $"{Reworded(place, field, e.Message)} {Words.ListingItems(place.ReadTool, place.ReadPath)}");
                    }
                }

                return;
            case var current:
                // The reason is the den's, so only while the value is as the den read it.
                var ignored = place.Ignored.FirstOrDefault(i => i.Name == field && i.Item is null && JsonOf(i.Value).GetRawText() == JsonOf(current).GetRawText());
                throw new DenToolException(
                    ToolErrorCodes.BadArgument,
                    $"{at} is written as one value, {Written(current)}, not a list, so {NoList(op.Op)}." +
                    (ignored is null ? "" : $" That value is ignored: {ignored.Reason}") +
                    $" {Words.Replacing(place.ClearTo, place.ClearButton)}");
        }
    }

    /// <summary>A refusal's message with the YAML paths it names put in the writer's terms.</summary>
    private static string Reworded(FieldPlace place, string field, string message)
    {
        var fieldsPath = string.Join('.', place.FieldsPath);
        var named = $"'{field}' in {place.Name}";
        var result = message
            .Replace($"'{fieldsPath}.{field}'", named, StringComparison.Ordinal)
            .Replace($"'{fieldsPath}'", place.FieldsName, StringComparison.Ordinal)
            .Replace($"The chapter's '{field}'", Cap(named), StringComparison.Ordinal)
            .Replace($"The default '{field}'", Cap(named), StringComparison.Ordinal);
        if (place.FieldsPath.Length > 1)
        {
            // A chapter's item: chapters.6.
            result = result.Replace($"'{string.Join('.', place.FieldsPath[..^1])}'", place.Name, StringComparison.Ordinal);
        }

        if (result.Contains(place.Path, StringComparison.Ordinal))
        {
            return result;
        }

        // Every refusal names the file: after the chapter or container, or else first.
        var at = result.IndexOf(place.Name, StringComparison.OrdinalIgnoreCase);
        return at >= 0
            ? result.Insert(at + place.Name.Length, $" ({place.Path})")
            : $"{Cap(named)} ({place.Path}): {result}";

        static string Cap(string text) => ToolWords.Cap(text);
    }

    /// <summary>A value as written, for a message: a scalar's text in quotes, anything else as JSON.</summary>
    private static string Written(YamlNode node) => node is YamlScalar s ? $"'{s.Text}'" : JsonOf(node).GetRawText();
}

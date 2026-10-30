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
    /// <param name="ClearTo">What a <c>clear</c> goes back to, after "to go back to".</param>
    /// <param name="ClearButton">The web page's button that clears the field.</param>
    /// <param name="Ignored">The fields written there whose values are ignored, as the den read the file before the edit.</param>
    private sealed record FieldPlace(
        string Name, string FieldsName, bool FieldsPlural, string Path, string[] FieldsPath, string ClearTo, string ClearButton,
        IReadOnlyDictionary<string, IgnoredChapterField> Ignored);

    /// <summary>A chapter, named for the writer: <c>chapter ember:ch07 (The Harbour)</c>.</summary>
    private static string ChapterName(Chapter chapter) =>
        $"chapter {chapter.Book.Key}:{chapter.Key}{(chapter.Title is { } t ? $" ({t})" : "")}";

    private static FieldPlace ChapterPlace(Chapter chapter, string[] itemPath) => new(
        ChapterName(chapter), $"the fields of {ChapterName(chapter)}", true, chapter.Book.FilePath, [.. itemPath, "fields"],
        "what the chapter inherits from chapter_defaults", "Use the default", chapter.IgnoredFields);

    private static FieldPlace DefaultsPlace(Container container)
    {
        var name = $"the chapter_defaults of {KindOf(container)} '{container.Key}'";
        return new(name, name, false, container.FilePath, ["chapter_defaults"], "what its books inherit from above", "Clear", container.IgnoredChapterDefaults);
    }

    /// <summary>
    /// Makes one change of a chapter field (<see cref="Apply"/>), first
    /// refusing one the file's shape can't take, in the writer's terms, and
    /// rewording any other refusal so it names the chapter or container
    /// rather than a YAML path.
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

    /// <summary>Refuses an edit that what's written can't take: fields that aren't a mapping, or an add or remove on a value that isn't a list.</summary>
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

        if (op.Op is not ("add" or "remove") || file.GetFrontmatterValue([.. place.FieldsPath, field]) is not { } current
            || current is YamlSequence or YamlScalar { IsNull: true })
        {
            return;
        }

        // The reason is the den's, so only while the value is as the den read it.
        var ignored = place.Ignored.TryGetValue(field, out var i) && JsonOf(i.Value).GetRawText() == JsonOf(current).GetRawText() ? i : null;
        var cannot = op.Op == "add" ? "'add' has no list to add an item to" : "'remove' has no list item to take out";
        throw new DenToolException(
            ToolErrorCodes.BadArgument,
            $"'{field}' in {place.Name} ({place.Path}) is written as one value, {Written(current)}, not a list, so {cannot}." +
            (ignored is null ? "" : $" That value is ignored: {ignored.Reason}") +
            $" {Words.Replacing(place.ClearTo, place.ClearButton)}");
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

        return result.Contains(place.Name, StringComparison.OrdinalIgnoreCase) || result.Contains(place.Path, StringComparison.Ordinal)
            ? result
            : $"{Cap(named)} ({place.Path}): {result}";

        static string Cap(string text) => ToolWords.Cap(text);
    }

    /// <summary>A value as written, for a message: a scalar's text in quotes, anything else as JSON.</summary>
    private static string Written(YamlNode node) => node is YamlScalar s ? $"'{s.Text}'" : JsonOf(node).GetRawText();
}

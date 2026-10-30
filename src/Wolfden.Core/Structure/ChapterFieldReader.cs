using Wolfden.Core.Documents;
using Wolfden.Core.Entities;
using Wolfden.Core.Types;
using Wolfden.Core.Yaml;

namespace Wolfden.Core.Structure;

/// <summary>
/// Reads a universe's chapter fields: each container's
/// <c>chapter_defaults</c> and each chapter item's <c>fields</c>, checked
/// against the den's chapter fields (types.md F-CHAP-3), then each
/// chapter's effective values (F-CHAP-5). Every problem is a warning, and a
/// value that doesn't match its definition counts as not set, so the
/// chapter inherits.
/// </summary>
internal sealed class ChapterFieldReader(DenStructure den, Universe universe)
{
    public void Read()
    {
        foreach (var container in new Container[] { universe }.Concat(universe.Series).Concat(universe.Books))
        {
            var ignored = new Dictionary<string, IgnoredChapterField>(StringComparer.Ordinal);
            container.ChapterDefaults = ReadDefaults(container, ignored);
            container.IgnoredChapterDefaults = ignored;
            container.ChapterHeading = ReadHeading(container);
        }

        foreach (var book in universe.Books)
        {
            foreach (var chapter in book.Chapters)
            {
                var ignored = new Dictionary<string, IgnoredChapterField>(StringComparer.Ordinal);
                chapter.OwnFields = ReadChapter(book, chapter, ignored);
                chapter.IgnoredFields = ignored;
                var effective = new Dictionary<string, ChapterFieldValue>(chapter.OwnFields, StringComparer.Ordinal);
                foreach (var container in book.ScopeChain)
                {
                    foreach (var (name, value) in container.ChapterDefaults)
                    {
                        effective.TryAdd(name, value);
                    }
                }

                chapter.Fields = effective;
            }
        }
    }

    private Dictionary<string, ChapterFieldValue> ReadDefaults(Container container, Dictionary<string, IgnoredChapterField> ignored)
    {
        var result = new Dictionary<string, ChapterFieldValue>(StringComparer.Ordinal);
        switch (container.File.Frontmatter?["chapter_defaults"])
        {
            case null or YamlScalar { IsNull: true }:
                return result;
            case YamlMapping map:
                Read(container.FilePath, map, container, result, ignored, "chapter_defaults");
                return result;
            case var node:
                Warn(container.FilePath, "F-CHAP-5", node.Line,
                    "chapter_defaults must be a mapping from chapter field to value, such as '{ tense: past }', so it's ignored.");
                return result;
        }
    }

    private const string HeadingExample = "such as '[pov, setting, when]'";

    /// <summary>
    /// A container's <c>chapter_heading</c> (format.md F-CTR-6): a list of
    /// chapter field names. Every problem is a warning. An entry that isn't a
    /// name, or repeats one, is ignored; a name that isn't a chapter field is
    /// kept, as a chapter's own undefined field is (types.md F-CHAP-3). One
    /// name on its own is a one-item list. Any other value that isn't a list,
    /// or a list left with no entries, counts as not set, so the books
    /// inherit; an explicit <c>[]</c> is "no fields here".
    /// </summary>
    private List<string>? ReadHeading(Container container)
    {
        switch (container.File.Frontmatter?[ChapterHeadings.Key])
        {
            case null or YamlScalar { IsNull: true }:
                return null;
            case YamlSequence { Items.Count: 0 }:
                return [];
            case YamlScalar single when single.Text.Trim().Length > 0:
                // One name on its own (chapter_heading: pov) is a one-item list.
                return Names(container, [single]);
            case YamlSequence list:
                return Names(container, list.Items);
            case var node:
                Warn(container.FilePath, "F-CTR-6", node.Line,
                    $"chapter_heading must be a chapter field's name or a list of them, {HeadingExample}, so it's ignored and the chapters inherit one from further up, if any.");
                return null;
        }
    }

    /// <summary>The names of a <c>chapter_heading</c>'s entries, or null if none is left.</summary>
    private List<string>? Names(Container container, IReadOnlyList<YamlNode> items)
    {
        var names = new List<string>();
        foreach (var item in items)
        {
            if (item is not YamlScalar { IsNull: false } scalar || scalar.Text.Trim().Length == 0)
            {
                Warn(container.FilePath, "F-CTR-6", item.Line,
                    $"Each entry in chapter_heading must be a chapter field's name, {HeadingExample}. This one is ignored.");
                continue;
            }

            var name = scalar.Text.Trim();
            if (names.Contains(name, StringComparer.Ordinal))
            {
                Warn(container.FilePath, "F-CTR-6", item.Line, $"'{name}' is already in chapter_heading, so this entry is ignored.");
                continue;
            }

            if (den.Types.FindChapterField(name) is null)
            {
                var known = den.Types.ChapterFields.Select(f => f.Name).ToList();
                Warn(container.FilePath, "F-CTR-6", item.Line,
                    $"'{name}' in chapter_heading isn't a chapter field, so headings show it only for a chapter whose fields write it." +
                    (known.Count == 0 ? "" : $" The chapter fields are: {string.Join(", ", known)}."));
            }

            names.Add(name);
        }

        return names.Count == 0 ? null : names;
    }

    private Dictionary<string, ChapterFieldValue> ReadChapter(Book book, Chapter chapter, Dictionary<string, IgnoredChapterField> ignored)
    {
        var result = new Dictionary<string, ChapterFieldValue>(StringComparer.Ordinal);
        switch (chapter.FieldsNode)
        {
            case null or YamlScalar { IsNull: true }:
                return result;
            case YamlMapping map:
                Read(book.FilePath, map, null, result, ignored, $"chapter {chapter}");
                return result;
            case var node:
                Warn(book.FilePath, "F-CHAP-3", node.Line,
                    $"The fields of chapter {chapter} must be a mapping from chapter field to value, such as '{{ pov: \"[[mara-voss]]\" }}', so they're ignored.");
                return result;
        }
    }

    private void Read(
        string path, YamlMapping map, Container? defaultFrom, Dictionary<string, ChapterFieldValue> result, Dictionary<string, IgnoredChapterField> ignored, string where)
    {
        foreach (var entry in map.Entries)
        {
            if (entry.KeyText is not { } name || result.ContainsKey(name))
            {
                continue;
            }

            var value = entry.Value;
            if (value is YamlScalar { IsNull: true })
            {
                ignored.TryAdd(name, new IgnoredChapterField(name, value, $"{name} is written with no value (null), which counts as not set."));
                continue;
            }

            if (EntityCatalog.IsEntry(value) || value is YamlSequence list && list.Items.Any(EntityCatalog.IsEntry))
            {
                Warn(path, "F-CHAP-3", value.Line,
                    $"{name} in {where} is an entry, but a chapter field has no entries: write the plain value. It's ignored, as if it were left out.");
                ignored.TryAdd(name, new IgnoredChapterField(name, value, $"{name} is written as an entry, but a chapter field has no entries: it takes the plain value."));
                continue;
            }

            var definition = den.Types.FindChapterField(name);
            if (definition is null)
            {
                Warn(path, "F-CHAP-3", entry.Key.Line, $"'{name}' isn't a chapter field. Its value is kept as written.");
                result[name] = new ChapterFieldValue(name, null, value, value is YamlSequence s ? s.Items : [value], defaultFrom);
                continue;
            }

            var check = FieldValues.Check(definition, value);
            foreach (var problem in check.Problems)
            {
                Warn(path, "F-CHAP-3", problem.Line, problem.IsAdvisory
                    ? $"{problem.Message} (In {where}.)"
                    : $"{problem.Message} In {where}, it's ignored, so the chapter inherits instead.");
            }

            // F-CHAP-3, F-CHAP-5: a value that doesn't match counts as not set;
            // in a list, only the items that don't match are ignored, and a list
            // left with no items counts as not set. An explicit empty list is
            // "none here".
            var set = definition.IsList ? check.IsEmptyList || check.Items.Count > 0 : check.Matches;
            if (set)
            {
                result[name] = new ChapterFieldValue(name, definition, value, check.Items, defaultFrom);
                ignored.Remove(name);
            }
            else
            {
                ignored.TryAdd(name, new IgnoredChapterField(
                    name, value, string.Join(" ", check.Problems.Where(p => !p.IsAdvisory).Select(p => p.Message).Distinct(StringComparer.Ordinal))));
            }
        }
    }

    private void Warn(string path, string rule, int? line, string message) =>
        den.Add(path, new Diagnostic(rule, DiagnosticSeverity.Warning, line, message));
}

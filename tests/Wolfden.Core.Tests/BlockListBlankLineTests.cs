using Wolfden.Core.Documents;
using Wolfden.Core.Yaml;

namespace Wolfden.Core.Tests;

/// <summary>
/// format.md F-RT-1, F-RT-2: adding an item to a block list keeps the blank
/// lines between its items and after its last one, and the comment lines
/// that group them, byte for byte (#215 review).
/// </summary>
public class BlockListBlankLineTests
{
    private static MarkdownFile Md(string frontmatter) => MarkdownFile.Parse(TestFiles.Utf8("---\n" + frontmatter + "---\nBody.\n"));

    private static string Fm(MarkdownFile file) => file.FrontmatterText["---\n".Length..^"---\n".Length];

    private static YamlMappingValue Chapter(string key, long number) =>
        new() { { "key", YamlScalarValue.String(key) }, { "number", YamlScalarValue.Integer(number) } };

    private const string Grouped =
        "chapters:\n" +
        "  # Part One\n" +
        "  - { key: \"1\", number: 1 }\n" +
        "\n" +
        "  - { key: \"2\", number: 2 }\n" +
        "\n" +
        "  # Part Two\n" +
        "  - { key: \"3\", number: 3 }\n" +
        "\n" +
        "status: draft\n";

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void BlankLinesBetweenItemsAndAfterTheLastStay()
    {
        var edited = Md(Grouped).WithNewListItem(["chapters"], Chapter("4", 4));

        Assert.Equal(
            "chapters:\n" +
            "  # Part One\n" +
            "  - { key: \"1\", number: 1 }\n" +
            "\n" +
            "  - { key: \"2\", number: 2 }\n" +
            "\n" +
            "  # Part Two\n" +
            "  - { key: \"3\", number: 3 }\n" +
            "  - { key: \"4\", number: 4 }\n" +
            "\n" +
            "status: draft\n",
            Fm(edited));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void AnInsertedItemGoesRightAfterTheOneBeforeItAndTheGroupingCommentStaysWithTheNext()
    {
        // After 2: before the blank line and '# Part Two', which belong to 3.
        var afterTwo = Md(Grouped).WithNewListItem(["chapters"], Chapter("2b", 3), 2);
        Assert.Equal(
            Grouped.Replace("  - { key: \"2\", number: 2 }\n", "  - { key: \"2\", number: 2 }\n  - { key: \"2b\", number: 3 }\n", StringComparison.Ordinal),
            Fm(afterTwo));

        // After 1: the blank line between 1 and 2 stays where it was, under the new item.
        var afterOne = Md(Grouped).WithNewListItem(["chapters"], Chapter("1b", 2), 1);
        Assert.Equal(
            Grouped.Replace("  - { key: \"1\", number: 1 }\n", "  - { key: \"1\", number: 1 }\n  - { key: \"1b\", number: 2 }\n", StringComparison.Ordinal),
            Fm(afterOne));

        // At the start: above '# Part One', which belongs to the old first item.
        var first = Md(Grouped).WithNewListItem(["chapters"], Chapter("prologue", 0), 0);
        Assert.Equal(Grouped.Replace("chapters:\n", "chapters:\n  - { key: \"prologue\", number: 0 }\n", StringComparison.Ordinal), Fm(first));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void BlockMappingItemsKeepTheirBlankLinesToo()
    {
        const string Before =
            "chapters:\n" +
            "  - key: 1\n" +
            "    number: 1\n" +
            "\n" +
            "  - key: 2\n" +
            "    number: 2\n" +
            "\n" +
            "status: draft\n";

        var edited = Md(Before).WithNewListItem(["chapters"], Chapter("3", 3));

        Assert.Equal(Before.Replace("    number: 2\n", "    number: 2\n  - key: \"3\"\n    number: 3\n", StringComparison.Ordinal), Fm(edited));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void AScalarAliasListKeepsItsBlankLines()
    {
        const string Before = "aliases:\n  - Voss\n\n  - Halden\n\nstatus: draft\n";

        var edited = Md(Before).WithNewListItem(["aliases"], YamlScalarValue.String("Mara"));

        Assert.Equal("aliases:\n  - Voss\n\n  - Halden\n  - Mara\n\nstatus: draft\n", Fm(edited));
    }
}

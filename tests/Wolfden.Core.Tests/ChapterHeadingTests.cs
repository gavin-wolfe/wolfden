using Wolfden.Core.Entities;
using Wolfden.Core.Links;
using Wolfden.Core.Structure;
using static Wolfden.Core.Tests.LinkDens;

namespace Wolfden.Core.Tests;

/// <summary>
/// Chapter headings built from chapter fields (format.md F-CTR-6, #227): a
/// container's <c>chapter_heading</c>, inherited down like
/// <c>chapter_defaults</c>, and the heading each chapter shows.
/// </summary>
public class ChapterHeadingTests
{
    private static MemoryDenSource Space() => Den()
        .Entity(Starfall, "ines-ardent", Character("Ines Ardent"))
        .Entity(Starfall, "kofi-mbeki", Character("Kofi Mbeki"))
        .Entity(Starfall, "vesta-yards", "type: location\nname: Vesta Yards\nstatus: canon", folder: "locations")
        .Entity(Starfall, "halcyon-relay", "type: location\nname: Halcyon Relay\nstatus: canon", folder: "locations");

    private static (DenStructure Den, LinkResolver Links) Load(MemoryDenSource source)
    {
        var catalog = EntityCatalog.Load(source);
        return (catalog.Structure, new LinkResolver(catalog));
    }

    private static string? HeadingOf(DenStructure den, LinkResolver links, string book, string chapter) =>
        ChapterHeadings.Heading(den.FindUniverse("starfall")!.FindBook(book)!.FindChapter(chapter)!, links);

    [Fact]
    [Trait("Scenario", "SC-WEB-77")]
    public void AHeadingIsBuiltFromTheListedFieldsInOrder()
    {
        var (den, links) = Load(Space()
            .With($"{LongDark}/series.md", "---\nname: The Long Dark\nbooks: [ember, ash]\nchapter_heading: [pov, setting, when]\n---\n")
            .With($"{Ember}/book.md", """
                ---
                name: Ember
                chapter_defaults: { setting: "[[vesta-yards]]" }
                chapters:
                  - { key: "1", number: 1, fields: { pov: "[[ines-ardent|Ardent]]", when: "2350-04-12" } }
                  - { key: "2", number: 2, title: The Long Watch, fields: { pov: ["[[kofi-mbeki]]", "[[ines-ardent]]"], setting: ["[[halcyon-relay]]"] } }
                  - { key: "3", number: 3, fields: { setting: [] } }
                  - { key: "4", number: 4, fields: { when: { text: "late spring", sort: [2350, 5] } } }
                  - { key: "5", number: 5, title: Plain }
                ---
                """));

        Assert.Empty(den.Diagnostics);
        Assert.Equal("Ardent — Vesta Yards, 2350-04-12", HeadingOf(den, links, "ember", "1"));
        Assert.Equal("The Long Watch · Kofi Mbeki & Ines Ardent — Halcyon Relay", HeadingOf(den, links, "ember", "2"));
        // A field with no value, or none ([]), is left out; so is the separator.
        Assert.Null(HeadingOf(den, links, "ember", "3"));
        Assert.Equal("Vesta Yards — late spring", HeadingOf(den, links, "ember", "4"));
        Assert.Equal("Plain · Vesta Yards", HeadingOf(den, links, "ember", "5"));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-77")]
    public void LeavingTheKeyOutChangesNothing()
    {
        var (den, links) = Load(Space().With($"{Ember}/book.md", """
            ---
            name: Ember
            chapters:
              - { key: "1", title: Kessan Reach, fields: { pov: "[[ines-ardent]]" } }
              - { key: "2", fields: { pov: "[[ines-ardent]]" } }
            ---
            """));

        Assert.Equal("Kessan Reach", HeadingOf(den, links, "ember", "1"));
        Assert.Null(HeadingOf(den, links, "ember", "2"));
        Assert.Null(ChapterHeadings.FieldsFor(den.FindUniverse("starfall")!.FindBook("ember")!));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-77")]
    public void TheNearestContainerWinsAndAnEmptyListStopsInheritance()
    {
        var (den, links) = Load(Space()
            .With($"{Starfall}/universe.md", "---\nname: Starfall\norder: [long-dark, a-quiet-moon]\nchapter_heading: [when]\nchapter_defaults: { when: \"2350\" }\n---\n")
            .With($"{LongDark}/series.md", "---\nname: The Long Dark\nbooks: [ember, ash]\nchapter_heading: [pov]\nchapter_defaults: { pov: \"[[ines-ardent]]\" }\n---\n")
            .With($"{Ember}/book.md", "---\nname: Ember\nchapter_heading: []\nchapters: [1]\n---\n")
            .With($"{Ash}/book.md", "---\nname: Ash\nchapter_heading: ~\nchapters: [1]\n---\n"));

        var universe = den.FindUniverse("starfall")!;
        Assert.Empty(ChapterHeadings.FieldsFor(universe.FindBook("ember")!)!);
        Assert.Null(HeadingOf(den, links, "ember", "1"));
        // A null key inherits: the series' list, not the universe's.
        Assert.Equal("Ines Ardent", HeadingOf(den, links, "ash", "1"));
        Assert.Equal("2350", HeadingOf(den, links, "a-quiet-moon", "1"));
    }

    [Fact]
    public void LinksShowTheCurrentNameOrElseTheLinkAsWritten()
    {
        var (den, links) = Load(Space()
            .With($"{Ember}/book.md", """
                ---
                name: Ember
                chapter_heading: [pov, mood]
                chapters:
                  - { key: "1", fields: { pov: ["[[nobody-here]]"], mood: "grim, [[halcyon-relay#Docks]]
                      and   wet" } }
                ---
                """));

        // A broken link shows its target; an undefined field's text is kept, links named, white space folded.
        Assert.Equal("nobody-here — grim, Halcyon Relay: Docks and wet", HeadingOf(den, links, "ember", "1"));
        var chapter = den.FindUniverse("starfall")!.FindBook("ember")!.FindChapter("1")!;
        // Where the reader shouldn't learn what a link reaches, it shows as written.
        Assert.Equal("nobody-here — grim, halcyon-relay: Docks and wet", ChapterHeadings.Heading(chapter, (_, _) => null));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-43")]
    public void ProblemsWithTheKeyAreWarnings()
    {
        var den = DenStructure.Load(Den()
            .With($"{Starfall}/universe.md", "---\nname: Starfall\norder: [long-dark, a-quiet-moon]\nchapter_heading: [when]\n---\n")
            .With($"{LongDark}/series.md", "---\nname: The Long Dark\nbooks: [ember, ash]\nchapter_heading: pov\n---\n")
            .With($"{Ember}/book.md", """
                ---
                name: Ember
                chapter_heading:
                  - pov
                  - { field: when }
                  - pov
                  - pvo
                chapters: [1]
                ---
                """)
            .With($"{Ash}/book.md", "---\nname: Ash\nchapter_heading: [[], ~]\nchapters: [1]\n---\n"));

        Assert.Equal(
            [
                ($"{LongDark}/series.md", (int?)4),
                ($"{Ash}/book.md", 3),
                ($"{Ash}/book.md", 3),
                ($"{Ember}/book.md", 5),
                ($"{Ember}/book.md", 6),
                ($"{Ember}/book.md", 7),
            ],
            den.Diagnostics.Where(d => d.Diagnostic.RuleId == "F-CTR-6").Select(d => (d.Path, d.Diagnostic.Line)).OrderBy(d => d.Path.Length).ThenBy(d => d.Path, StringComparer.Ordinal).ThenBy(d => d.Line));
        Assert.All(den.Diagnostics, d => Assert.Equal(Documents.DiagnosticSeverity.Warning, d.Diagnostic.Severity));
        Assert.Contains(den.Diagnostics, d => d.Diagnostic.Message.Contains("'pvo' in chapter_heading isn't a chapter field", StringComparison.Ordinal));

        var universe = den.FindUniverse("starfall")!;
        // The unknown name is kept; the bad entry and the repeat aren't.
        Assert.Equal(["pov", "pvo"], ChapterHeadings.FieldsFor(universe.FindBook("ember")!));
        // A value that isn't a list, and a list left with no entries, inherit.
        Assert.Equal(["when"], ChapterHeadings.FieldsFor(universe.FindBook("ash")!));
        Assert.Null(universe.Series.Single().ChapterHeading);
    }

    [Theory]
    [InlineData(null, new string[0], null)]
    [InlineData("  Title  ", new string[0], "Title")]
    [InlineData(null, new[] { "A|", "|B", "C|D" }, "A — B, C & D")]
    [InlineData("T", new[] { " | ", "B" }, "T · B")]
    public void ComposeJoinsWhatThereIsToShow(string? title, string[] fields, string? expected) =>
        Assert.Equal(expected, ChapterHeadings.Compose(title, fields.Select(f => f.Split('|'))));
}

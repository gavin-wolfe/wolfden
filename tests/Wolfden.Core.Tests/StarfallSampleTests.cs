using Wolfden.Core.Documents;
using Wolfden.Core.Links;
using Wolfden.Core.Outlines;

namespace Wolfden.Core.Tests;

/// <summary>
/// <c>samples/starfall</c>, the invented sample series (#34): it loads with
/// nothing reported, spoiler checks on, every link resolves, and it uses
/// what design.md §11 says it shows. Not to be confused with the smaller
/// test den in <c>Dens/starfall</c>.
/// </summary>
public class StarfallSampleTests
{
    private const string Series = "universes/starfall/series/long-dark";

    private static readonly string Root = Path.Combine(TestFiles.RepoRoot, "samples", "starfall");

    private static readonly Lazy<LinkIndex> Index = new(() => LinkIndex.Load(Root));

    private static List<string> RowsOf(string book) =>
        [.. OutlineReader.ReadRows(MarkdownFile.Parse(File.ReadAllBytes(Path.Combine(Root, book, "outline.md")))).Select(r => r.RowId)];

    [Fact]
    public void EveryEntityLoadsWithNoDiagnostics()
    {
        var catalog = Index.Value.Catalog;

        Assert.True(catalog.Structure.SpoilerChecks);
        Assert.Empty(catalog.Structure.Diagnostics);
        Assert.Empty(catalog.Diagnostics);
        Assert.True(catalog.Entities.Count >= 30, $"Only {catalog.Entities.Count} entities found.");
        Assert.All(catalog.Entities, e => Assert.NotNull(e.Id));
        Assert.All(catalog.Entities, e => Assert.NotNull(e.EntersCanon));
    }

    [Fact]
    public void EveryLinkResolvesWithNoDiagnostics()
    {
        Assert.Empty(Index.Value.Diagnostics);
        Assert.All(Index.Value.Links, l => Assert.Equal(LinkOutcome.Resolved, l.Outcome));
    }

    [Fact]
    public void ItUsesCoreAndSpaceOperaTypes()
    {
        var types = Index.Value.Catalog.Entities.Select(e => e.Type).ToHashSet();

        Assert.Superset(
            new HashSet<string> { "character", "event", "faction", "item", "idea", "location", "note", "thread", "polity", "settlement", "ship", "ship-class", "species", "star-system", "technology", "world" },
            types);
    }

    [Fact]
    public void ChaptersAreSetOnKindsOfLocationAndFactionsNameAPolity()
    {
        // F-TYPE-13: `setting` is a ref to location, and `factions` to faction.
        var settings = Index.Value.Links.Where(l => l.Occurrence.Path == $"{Series}/books/ember/book.md" && l.Occurrence.Site == LinkSite.ChapterField).Select(l => l.Target!.Type).ToHashSet();
        var factions = Index.Value.Links.Where(l => l.Occurrence.Path == $"{Series}/entities/characters/ilse-tarrow.md" && l.Target!.Type == "polity").Select(l => l.Target!.Name);

        Assert.Contains("ship", settings);
        Assert.Contains("settlement", settings);
        Assert.Contains("world", settings);
        Assert.Equal(["Kessan Compact"], factions);
    }

    [Theory]
    [InlineData("universes/starfall/entities/worlds/tamber.md")]
    [InlineData("universes/starfall/entities/settlements/ostrel-skim.md")]
    public void AMoonAndAPlatformNestInTheGasGiant(string path)
    {
        var partOf = Index.Value.Links.Single(l => l.Occurrence.Path == path && l.Occurrence.Site == LinkSite.EntityField && l.Target!.Name == "Ostrel");

        Assert.Equal("[[ostrel]]", partOf.Occurrence.Link.Text);
    }

    [Fact]
    public void OutlineRowsKeepCountingFromEmberIntoAsh()
    {
        var ember = RowsOf($"{Series}/books/ember").Select(int.Parse).ToList();
        var ash = RowsOf($"{Series}/books/ash").Select(int.Parse).ToList();

        Assert.Equal(1, ember[0]);
        Assert.Equal(ember.Max() + 1, ash[0]);
        Assert.Equal(ember.Concat(ash), Enumerable.Range(1, ember.Count + ash.Count));
    }

    [Fact]
    public void EmberIsFrozenAndAshHasThirtyChaptersListed()
    {
        var universe = Index.Value.Catalog.Structure.FindUniverse("starfall")!;
        var ember = universe.Books.Single(b => b.Key == "ember");
        var ash = universe.Books.Single(b => b.Key == "ash");

        Assert.Equal(Core.Structure.BookStatus.Final, ember.Status);
        Assert.Equal(Core.Structure.BookStatus.Draft, ash.Status);
        Assert.Equal(30, ember.Chapters.Count);
        Assert.Equal(30, ash.Chapters.Count);
        Assert.Null(universe.Books.Single(b => b.Key == "a-quiet-moon").Series);
    }
}

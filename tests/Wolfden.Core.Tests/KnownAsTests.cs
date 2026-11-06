using Wolfden.Core.Checks;
using Wolfden.Core.Documents;
using Wolfden.Core.Entities;
using Wolfden.Core.Links;
using Wolfden.Core.Positions;
using static Wolfden.Core.Tests.LinkDens;

namespace Wolfden.Core.Tests;

/// <summary>Names readers learn partway through the story: <c>known_as</c> (format.md F-ENT-10, F-LINK-13, #67).</summary>
public class KnownAsTests
{
    private const string Courier = $"{Starfall}/entities/characters/the-courier.md";

    private static CatalogEntity Load(string frontmatter, out IReadOnlyList<Diagnostic> diagnostics)
    {
        var catalog = EntityCatalog.Load(Den().Entity(Starfall, "the-courier", frontmatter));
        diagnostics = catalog.Diagnostics.Where(d => d.Path == Courier && d.Diagnostic.RuleId != "F-ID-2").Select(d => d.Diagnostic).ToList();
        return Assert.Single(catalog.Entities);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-49")]
    public void AKnownAsEntryIsANameWithWhereReadersLearnIt()
    {
        var entity = Load(Character("The Courier", "aliases: [the Messenger]", "known_as:\n  - { value: Mara Voss, from: \"ash:2\" }"), out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(["the Messenger"], entity.Aliases);
        var known = Assert.Single(entity.KnownAs);
        Assert.Equal("Mara Voss", known.Value);
        Assert.Equal("ash:2", known.From?.ToString());
        Assert.True(known.IsLive);
        Assert.Equal(["the Messenger", "Mara Voss"], entity.AllAliases);
        Assert.Same(known, entity.KnownNameMatching("  mara   VOSS "));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-49")]
    [InlineData("known_as: { value: Mara Voss, from: \"ash:2\" }")]
    [InlineData("known_as:\n  value: Mara Voss\n  from: \"ash:2\"")]
    public void ASingleEntryNeedsNoList(string knownAs)
    {
        var entity = Load(Character("The Courier", knownAs), out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal("Mara Voss", Assert.Single(entity.KnownAs).Value);
    }

    [Theory]
    [InlineData("known_as:")]
    [InlineData("known_as: ~")]
    [InlineData("known_as: []")]
    public void NoKnownAsIsNone(string knownAs)
    {
        var entity = Load(Character("The Courier", knownAs), out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(entity.KnownAs);
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-54")]
    [InlineData("known_as: [{ value: Mara Voss }]", "has no from")]
    [InlineData("known_as: [{ value: Mara Voss, from: ~ }]", "has no from")]
    [InlineData("known_as: [Mara Voss]", "move it to aliases")]
    [InlineData("known_as: Mara Voss", "move it to aliases")]
    public void ANameWithNoFromIsKeptButNeverKnown(string knownAs, string message)
    {
        // Fails closed: a warning, and the name still reaches the note as a link, but no read at a position knows it.
        var entity = Load(Character("The Courier", knownAs), out var diagnostics);

        var warning = Assert.Single(diagnostics);
        Assert.Equal(("F-ENT-10", DiagnosticSeverity.Warning), (warning.RuleId, warning.Severity));
        Assert.Contains(message, warning.Message, StringComparison.Ordinal);
        var known = Assert.Single(entity.KnownAs);
        Assert.False(known.IsLive);
        Assert.Equal(["Mara Voss"], entity.AllAliases);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-54")]
    public void AFromThatDoesntResolveIsAWarningAndTheNameIsNeverKnown()
    {
        var source = Den().Entity(Starfall, "the-courier", Character("The Courier", "known_as: [{ value: Mara Voss, from: \"ash:9\" }]"));

        var check = DenCheck.Run(source);

        var entity = Assert.Single(EntityCatalog.Load(source).Entities);
        Assert.False(Assert.Single(entity.KnownAs).IsLive);
        var warning = Assert.Single(check.Diagnostics, d => d.Path == Courier && d.Diagnostic.RuleId == "F-POS-4");
        Assert.Contains("is a name no read at a story position knows", warning.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownKeyIsAWarningAndKept()
    {
        var entity = Load(Character("The Courier", "known_as: [{ value: Mara Voss, from: \"ash:2\", known_by: [orrin] }]"), out var diagnostics);

        var warning = Assert.Single(diagnostics);
        Assert.Equal("F-ENT-10", warning.RuleId);
        Assert.Contains("'known_by' isn't a key", warning.Message, StringComparison.Ordinal);
        Assert.True(Assert.Single(entity.KnownAs).IsLive);
    }

    [Theory]
    [InlineData("known_as: [{ value: \"\", from: \"ash:2\" }]")]
    [InlineData("known_as: [{ value: [a], from: \"ash:2\" }]")]
    [InlineData("known_as: [{ from: \"ash:2\" }]")]
    [InlineData("known_as: [{ value: \"Mara\\nVoss\", from: \"ash:2\" }]")]
    [InlineData("known_as: [[Mara Voss]]")]
    [InlineData("known_as: [~]")]
    [InlineData("known_as: [{ value: Mara Voss, from: \"Ember\" }]")]
    public void AMalformedEntryIsAnError(string knownAs)
    {
        var catalog = EntityCatalog.Load(Den().Entity(Starfall, "the-courier", Character("The Courier", knownAs)));

        Assert.Empty(catalog.Entities);
        Assert.Contains(catalog.Diagnostics, d => d.Diagnostic.RuleId is "F-ENT-10" or "F-POS-3" && d.Diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TheNotesOwnNameCantBeHidden()
    {
        var entity = Load(Character("The Courier", "known_as: [{ value: the courier, from: \"ash:2\" }]"), out var diagnostics);

        Assert.Contains("the note's own name", Assert.Single(diagnostics).Message, StringComparison.Ordinal);
        Assert.Empty(entity.KnownAs);
    }

    [Fact]
    public void ANameInBothKeysIsHidden()
    {
        // Hiding is the safe reading.
        var entity = Load(Character("The Courier", "aliases: [Mara Voss, the Messenger]", "known_as: [{ value: Mara Voss, from: \"ash:2\" }]"), out var diagnostics);

        Assert.Contains("also in aliases", Assert.Single(diagnostics).Message, StringComparison.Ordinal);
        Assert.Equal(["the Messenger"], entity.Aliases);
        Assert.Equal("Mara Voss", Assert.Single(entity.KnownAs).Value);
    }

    [Fact]
    public void ANameListedTwiceCountsFromItsEarliestFrom()
    {
        var entity = Load(Character("The Courier", "known_as:\n  - { value: Mara Voss, from: \"ash:3\" }\n  - { value: MARA VOSS, from: \"ash:1\" }"), out var diagnostics);

        Assert.Contains("listed more than once", Assert.Single(diagnostics).Message, StringComparison.Ordinal);
        Assert.Equal("ash:1", Assert.Single(entity.KnownAs).From?.ToString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-54")]
    public void EachSpellingIsItsOwnEntry()
    {
        // Links don't fold accents (F-LINK-4), so each spelling a writer uses is listed, with its own from.
        var source = Den()
            .Entity(Starfall, "the-courier", Character("The Courier", "known_as:\n  - { value: Søren Holt, from: \"ash:1\" }\n  - { value: Soren Holt, from: \"ash:2\" }"))
            .Entity(Starfall, "the-ledger", Character("The Ledger"), "[[Søren Holt]] and [[Soren Holt]] and [[Sœren Holt]].\n");

        var links = LinkIndex.Load(source);

        var entity = Assert.Single(links.Catalog.Entities, e => e.Name == "The Courier");
        Assert.Equal(2, entity.KnownAs.Count);
        var ledger = $"{Starfall}/entities/characters/the-ledger.md";
        Assert.Same(entity, links.Link(ledger, "[[Søren Holt]]").Target);
        Assert.Same(entity, links.Link(ledger, "[[Soren Holt]]").Target);
        Assert.Equal(LinkOutcome.Broken, links.Link(ledger, "[[Sœren Holt]]").Outcome);
    }

    [Fact]
    public void APositionScanFindsAKnownAsFrom()
    {
        var file = MarkdownFile.Parse(System.Text.Encoding.UTF8.GetBytes("---\ntype: note\nname: X\nknown_as:\n  - { value: Y, from: \"ash:2\" }\n  - Z\n---\n"), requireFrontmatter: true);

        var reference = Assert.Single(PositionScanner.Scan(file).References);
        Assert.Equal((PositionSite.KnownAsFrom, "ash:2", 5), (reference.Site, reference.Text, reference.Line));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-51")]
    public void ALinkByANameReadersLearnLaterRunsAheadEvenAtTheStartOfTheStory()
    {
        // F-LINK-13 compares a link by a known_as name with that name's from, not only the note's entry.
        var source = Den()
            .Entity(Starfall, "the-courier", Character("The Courier", "known_as: [{ value: Mara Voss, from: \"ash:2\" }]"))
            .Entity(Starfall, "the-ledger", Character("The Ledger", "fields:\n  signer: { value: \"[[Mara Voss]]\", from: \"ember:2\" }"),
                "\n[[Mara Voss]] signed it. [[Mara Voss|The stranger]] paid. [[The Courier]] left.\n\n## Later\n<!-- wolfden: from=ash:3 -->\n[[Mara Voss]] again.\n");

        var links = LinkIndex.Load(source);

        var ledger = $"{Starfall}/entities/characters/the-ledger.md";
        var warnings = links.Diagnostics.Where(d => d.Path == ledger && d.Diagnostic.RuleId == "F-LINK-13").Select(d => d.Diagnostic.Message).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.StartsWith("[[Mara Voss]]: 'Mara Voss' is a name of 'The Courier' that readers learn at ash:2, after this link's position, the start of the story", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("ember:2", StringComparison.Ordinal));

        // Without spoiler checks, nothing.
        var off = LinkIndex.Load(Den(spoilerChecks: false)
            .Entity(Starfall, "the-courier", Character("The Courier", "known_as: [{ value: Mara Voss, from: \"ash:2\" }]"))
            .Entity(Starfall, "the-ledger", Character("The Ledger"), "[[Mara Voss]] signed it.\n"));
        Assert.DoesNotContain(off.Diagnostics, d => d.Diagnostic.RuleId == "F-LINK-13");
    }
}

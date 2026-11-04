using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// An assistant's read at a story position leaves out the values of position
/// fields later than the position, and a read without one has them
/// (format.md F-VIEW-5, #305).
/// </summary>
public sealed class FuturePositionFieldTests : IDisposable
{
    private const string Manifest = "The missing manifest";

    private readonly TempDen _den = TempDen.Starfall();
    private readonly WolfdenTools _tools;

    public FuturePositionFieldTests()
    {
        _tools = new WolfdenTools(new DenTools(_den.Root));
    }

    public void Dispose() => _den.Dispose();

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void GetEntityAtAPositionKeepsOnlyTheValuesTheReaderHasReached()
    {
        using var json = Read(_tools.GetEntity(Manifest, "starfall", "ember:3"));

        Assert.Equal(["ember:2"], Strings(Field(json, "planted").GetProperty("value")));
        Assert.Null(FieldOrNull(json, "paid_off"));
        Assert.DoesNotContain("ash:1", json.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("a-quiet-moon:2b", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void AFullReadStillHasEveryValue()
    {
        using var json = Read(_tools.GetEntity(Manifest, "starfall"));

        Assert.Equal(["ember:2", "ash:1"], Strings(Field(json, "planted").GetProperty("value")));
        Assert.Equal("a-quiet-moon:2b", Field(json, "paid_off").GetProperty("value").GetString());
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-45")]
    [InlineData("ash:1", new[] { "ember:2", "ash:1" }, null)] // At the position is known; a value there isn't later.
    [InlineData("ash", new[] { "ember:2", "ash:1" }, null)] // A bare book orders the same as its first chapter.
    [InlineData("ember:3", new[] { "ember:2" }, null)]
    [InlineData("a-quiet-moon:2", new[] { "ember:2", "ash:1" }, null)] // 2b comes after 2 in reading order.
    [InlineData("a-quiet-moon:2b", new[] { "ember:2", "ash:1" }, "a-quiet-moon:2b")]
    public void LaterMeansLaterInReadingOrder(string position, string[] planted, string? paidOff)
    {
        using var json = Read(_tools.GetEntity(Manifest, "starfall", position));

        Assert.Equal(planted, Strings(Field(json, "planted").GetProperty("value")));
        Assert.Equal(paidOff, FieldOrNull(json, "paid_off")?.GetProperty("value").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void ASplitChapterIsComparedByItsPlaceNotItsNumber()
    {
        Thread("the-split", "paid_off: \"ember:2b\"");

        using var before = Read(_tools.GetEntity("The split", "starfall", "ember:2"));
        Assert.Null(FieldOrNull(before, "paid_off"));

        using var at = Read(_tools.GetEntity("The split", "starfall", "ember:2b"));
        Assert.Equal("ember:2b", Field(at, "paid_off").GetProperty("value").GetString());

        using var after = Read(_tools.GetEntity("The split", "starfall", "ember:3"));
        Assert.Equal("ember:2b", Field(after, "paid_off").GetProperty("value").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void ABareBookValueIsTheStartOfThatBook()
    {
        Thread("the-bare", "paid_off: ash");

        using var before = Read(_tools.GetEntity("The bare", "starfall", "ember:3"));
        Assert.Null(FieldOrNull(before, "paid_off"));

        using var at = Read(_tools.GetEntity("The bare", "starfall", "ash:1"));
        Assert.Equal("ash", Field(at, "paid_off").GetProperty("value").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void CanonAsOfLeavesThemOutInPlaceAndAtTheEndOfTheStoryKeepsThem()
    {
        using var at = Read(_tools.CanonAsOf("ember:3", "starfall"));
        var manifest = Entity(at, Manifest);
        Assert.Equal(["ember:2"], Strings(EntityField(manifest, "planted").GetProperty("value")));
        Assert.DoesNotContain(manifest.GetProperty("fields").EnumerateArray(), f => f.GetProperty("name").GetString() == "paid_off");

        using var end = Read(_tools.CanonAsOf(universe: "starfall"));
        var whole = Entity(end, Manifest);
        Assert.Equal(["ember:2", "ash:1"], Strings(EntityField(whole, "planted").GetProperty("value")));
        Assert.Equal("a-quiet-moon:2b", EntityField(whole, "paid_off").GetProperty("value").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void TheyAreLeftOutOfPinnedInEveryRead()
    {
        Thread("the-pinned", """
            planted: { value: ["ember:1", "ash:2"], pin: true }
            paid_off: { value: "ash:3", pin: true }
            """);

        using var entity = Read(_tools.GetEntity("The pinned", "starfall", "ember:3"));
        var pinned = entity.RootElement.GetProperty("entity").GetProperty("pinned").GetProperty("fields");
        Assert.Equal(["ember:1"], Strings(Assert.Single(pinned.EnumerateArray()).GetProperty("value")));

        using var canon = Read(_tools.CanonAsOf("ember:3", "starfall"));
        var inCanon = canon.RootElement.GetProperty("pinned").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "The pinned");
        Assert.Equal(["ember:1"], Strings(Assert.Single(inCanon.GetProperty("fields").EnumerateArray()).GetProperty("value")));

        using var brief = Read(_tools.CanonAsOf("ember:3", "starfall", brief: true));
        var inBrief = brief.RootElement.GetProperty("pinned").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "The pinned");
        Assert.Equal(["ember:1"], Strings(Assert.Single(inBrief.GetProperty("fields").EnumerateArray()).GetProperty("value")));
        Assert.DoesNotContain("ash:3", brief.RootElement.GetRawText(), StringComparison.Ordinal);

        using var full = Read(_tools.GetEntity("The pinned", "starfall"));
        Assert.Equal(2, full.RootElement.GetProperty("entity").GetProperty("pinned").GetProperty("fields").GetArrayLength());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void AnEntityWithOnlyALaterPinnedValueIsNotListedAsPinned()
    {
        Thread("the-hidden-pin", "paid_off: { value: \"ash:3\", pin: true }");

        using var canon = Read(_tools.CanonAsOf("ember:3", "starfall"));
        Assert.DoesNotContain(canon.RootElement.GetProperty("pinned").EnumerateArray(), p => p.GetProperty("name").GetString() == "The hidden pin");

        using var entity = Read(_tools.GetEntity("The hidden pin", "starfall", "ember:3"));
        Assert.Empty(entity.RootElement.GetProperty("entity").GetProperty("pinned").GetProperty("fields").EnumerateArray());

        using var later = Read(_tools.CanonAsOf("ash:3", "starfall"));
        Assert.Contains(later.RootElement.GetProperty("pinned").EnumerateArray(), p => p.GetProperty("name").GetString() == "The hidden pin");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void AnEntrysValueIsComparedAndItsFromStays()
    {
        Thread("the-entry", """
            planted: { value: ["ember:1", "ash:2"], from: "ember:1" }
            paid_off: { value: "ash:3", from: "ember:1" }
            """);

        using var json = Read(_tools.GetEntity("The entry", "starfall", "ember:3"));

        var planted = Field(json, "planted");
        Assert.Equal(["ember:1"], Strings(planted.GetProperty("value")));
        Assert.Equal("ember:1", planted.GetProperty("from").GetString());
        Assert.Null(FieldOrNull(json, "paid_off"));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void ADatedListShowsTheValueInEffectOnlyOnceTheReaderHasReachedIt()
    {
        Thread("the-dated", """
            paid_off:
              - { value: "ash:3", from: "ember:1", until: "ember:3" }
              - { value: "ember:2", from: "ember:3" }
            """);

        using var early = Read(_tools.GetEntity("The dated", "starfall", "ember:2"));
        Assert.Null(FieldOrNull(early, "paid_off"));

        using var later = Read(_tools.GetEntity("The dated", "starfall", "ember:3"));
        Assert.Equal("ember:2", Field(later, "paid_off").GetProperty("value").GetString());

        using var full = Read(_tools.GetEntity("The dated", "starfall"));
        var entries = full.RootElement.GetProperty("entity").GetProperty("fields").EnumerateArray().Where(f => f.GetProperty("name").GetString() == "paid_off").ToList();
        Assert.Equal(["ash:3", "ember:2"], entries.Select(e => e.GetProperty("value").GetString()!));
        Assert.Equal("ember:3", entries[0].GetProperty("until").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void APositionFieldOfAnyTypeCounts()
    {
        _den.Write(
            "packs/starfall/types/omen.yaml",
            """
            name: Omen
            plural: omens
            fields:
              signs: { type: position, list: true }
              fulfilled: { type: position }
              summary: { type: string }
            """);
        _den.Write(
            "universes/starfall/entities/omens/the-red-comet.md",
            """
            ---
            type: omen
            name: The red comet
            status: canon
            fields:
              signs: ["ember:1", "ash:1"]
              fulfilled: "ash:4"
              summary: "ash:4"
            ---

            # The red comet
            """);

        using var json = Read(_tools.GetEntity("The red comet", "starfall", "ember:3"));
        Assert.Equal(["ember:1"], Strings(Field(json, "signs").GetProperty("value")));
        Assert.Null(FieldOrNull(json, "fulfilled"));
        Assert.Equal("ash:4", Field(json, "summary").GetProperty("value").GetString());

        using var full = Read(_tools.GetEntity("The red comet", "starfall"));
        Assert.Equal("ash:4", Field(full, "fulfilled").GetProperty("value").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void AFieldThatBecomesAPositionFieldIsFilteredAtTheNextRead()
    {
        _den.Write("packs/starfall/types/omen.yaml", "name: Omen\nplural: omens\nfields:\n  fulfilled: { type: string }\n");
        _den.Write(
            "universes/starfall/entities/omens/the-red-comet.md",
            "---\ntype: omen\nname: The red comet\nstatus: canon\nfields:\n  fulfilled: \"ash:4\"\n---\n\n# The red comet\n");

        using var asText = Read(_tools.GetEntity("The red comet", "starfall", "ember:3"));
        Assert.Equal("ash:4", Field(asText, "fulfilled").GetProperty("value").GetString());

        _den.Write("packs/starfall/types/omen.yaml", "name: Omen\nplural: omens\nfields:\n  fulfilled: { type: position }\n");

        using var asPosition = Read(_tools.GetEntity("The red comet", "starfall", "ember:3"));
        Assert.Null(FieldOrNull(asPosition, "fulfilled"));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-45")]
    [InlineData("\"ember:99\"")] // A chapter the book doesn't have (F-POS-4).
    [InlineData("\"nowhere:1\"")] // A book the universe doesn't have.
    [InlineData("\"ember:1.2\"")] // The scene form (F-POS-2).
    [InlineData("\"ember:r7\"")] // The row form (F-POS-6).
    [InlineData("\"soon\"")] // Reads as a bare book that doesn't exist.
    [InlineData("\"not a position\"")]
    [InlineData("14")]
    [InlineData("{ a: 1 }")]
    public void AValueThatCantBeComparedIsLeftOutThereAndKeptInAFullRead(string value)
    {
        Thread("the-odd", $"paid_off: {value}");

        using var at = Read(_tools.GetEntity("The odd", "starfall", "ash:4"));
        Assert.Null(FieldOrNull(at, "paid_off"));

        using var full = Read(_tools.GetEntity("The odd", "starfall"));
        Assert.NotNull(FieldOrNull(full, "paid_off"));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void InAListOnlyTheItemsTheReaderHasReachedStay()
    {
        Thread("the-mixed", """
            planted: ["ember:1", "ember:99", "soon", ["ember:2"], { a: "ember:2" }, "ash:4"]
            """);

        using var json = Read(_tools.GetEntity("The mixed", "starfall", "ember:3"));

        Assert.Equal(["ember:1"], Strings(Field(json, "planted").GetProperty("value")));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void AListWithNothingReachedIsLeftOutAndOneWrittenEmptyStays()
    {
        Thread("the-later", "planted: [\"ash:1\", \"ash:2\"]");
        using (var later = Read(_tools.GetEntity("The later", "starfall", "ember:3")))
        {
            Assert.Null(FieldOrNull(later, "planted"));
        }

        Thread("the-empty", "planted: []");
        using var empty = Read(_tools.GetEntity("The empty", "starfall", "ember:3"));
        Assert.Equal(0, Field(empty, "planted").GetProperty("value").GetArrayLength());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void ABookOfAnotherUniverseDoesntResolve()
    {
        Thread("the-crossing", "paid_off: \"tide:1\"");

        using var json = Read(_tools.GetEntity("The crossing", "starfall", "ash:4"));

        Assert.Null(FieldOrNull(json, "paid_off"));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void AFieldTheTypeDoesntDefineIsNotAPositionField()
    {
        Thread("the-extra", "next_clue: \"ash:4\"");

        using var json = Read(_tools.GetEntity("The extra", "starfall", "ember:3"));

        Assert.Equal("ash:4", Field(json, "next_clue").GetProperty("value").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void AnUntilOnAReachedValueStillGoes()
    {
        Thread("the-ending", """
            paid_off: { value: "ember:1", until: "ash:4" }
            """);

        using var json = Read(_tools.GetEntity("The ending", "starfall", "ember:3"));

        var paidOff = Field(json, "paid_off");
        Assert.Equal("ember:1", paidOff.GetProperty("value").GetString());
        Assert.False(paidOff.TryGetProperty("until", out _));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void APositionAloneSettlesTheUniverse()
    {
        using var json = Read(_tools.GetEntity(Manifest, as_of: "ember:3"));

        Assert.Equal(["ember:2"], Strings(Field(json, "planted").GetProperty("value")));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-45")]
    public void TheServersInstructionsSaySo()
    {
        Assert.Contains("position field's values later", WolfdenMcpServer.Instructions, StringComparison.Ordinal);
    }

    /// <summary>A canon thread in the Starfall universe, in view from the start of the story.</summary>
    private void Thread(string file, string fields)
    {
        var name = char.ToUpperInvariant(file[0]) + file[1..].Replace('-', ' ');
        var lines = string.Join('\n', fields.Split('\n').Select(l => "  " + l.TrimEnd('\r')));
        _den.Write($"universes/starfall/entities/threads/{file}.md", $"---\ntype: thread\nname: {name}\nstatus: canon\nfields:\n{lines}\n---\n\n## Overview\nA thread.\n");
    }

    private static JsonDocument Read(CallToolResult result) => JsonDocument.Parse(Text(result));

    private static string Text(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    private static JsonElement Field(JsonDocument json, string name) => EntityField(json.RootElement.GetProperty("entity"), name);

    private static JsonElement? FieldOrNull(JsonDocument json, string name)
    {
        foreach (var f in json.RootElement.GetProperty("entity").GetProperty("fields").EnumerateArray())
        {
            if (f.GetProperty("name").GetString() == name)
            {
                return f;
            }
        }

        return null;
    }

    private static JsonElement EntityField(JsonElement entity, string name) =>
        entity.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == name);

    private static JsonElement Entity(JsonDocument json, string name) =>
        json.RootElement.GetProperty("entities").EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);

    private static List<string> Strings(JsonElement list) => list.EnumerateArray().Select(i => i.GetString()!).ToList();
}

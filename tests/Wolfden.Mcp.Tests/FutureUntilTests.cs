using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// An assistant's read at a story position doesn't say when a fact stops
/// holding, and a read without one does (format.md F-VIEW-4, #288).
/// </summary>
public sealed class FutureUntilTests : IDisposable
{
    private const string Ilse = "Ilse Tarrow";

    private readonly TempDen _den = TempDen.Starfall();
    private readonly WolfdenTools _tools;

    public FutureUntilTests()
    {
        _den.Write(
            "universes/starfall/entities/notes/the-armistice.md",
            """
            ---
            type: note
            name: The armistice
            status: canon
            fields:
              terms:
                - { value: holding, until: "ash:2", pin: true }
            ---

            # The armistice

            ## Accord
            <!-- wolfden: until=ash:2 pin=true -->
            Both fleets keep to their lanes.
            """);
        _tools = new WolfdenTools(new DenTools(_den.Root));
    }

    public void Dispose() => _den.Dispose();

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void GetEntityAtAPositionLeavesOutAFieldEntrysUntil()
    {
        using var json = Read(_tools.GetEntity(Ilse, "starfall", "ember:1"));

        var posting = Field(json, "posting");
        Assert.Contains("tessaly", posting.GetProperty("value").GetString(), StringComparison.Ordinal);
        Assert.Equal("ember:1", posting.GetProperty("from").GetString());
        Assert.Empty(Untils(json.RootElement));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void GetEntityAtAPositionLeavesOutASectionsUntil()
    {
        using var json = Read(_tools.GetEntity(Ilse, "starfall", "ash:1"));

        var recovery = Section(json.RootElement.GetProperty("entity"), "Recovery");
        Assert.Equal("ash:1", recovery.GetProperty("from").GetString());
        Assert.Empty(Untils(json.RootElement));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void APinnedFactHasNoUntilInPinnedOrInPlace()
    {
        using var entity = Read(_tools.GetEntity("The armistice", "starfall", "ash:1"));
        Assert.Equal("holding", entity.RootElement.GetProperty("entity").GetProperty("pinned").GetProperty("fields")[0].GetProperty("value").GetString());
        Assert.Equal("Accord", entity.RootElement.GetProperty("entity").GetProperty("pinned").GetProperty("sections")[0].GetProperty("name").GetString());
        Assert.Empty(Untils(entity.RootElement));

        using var canon = Read(_tools.CanonAsOf("ash:1", "starfall"));
        var pinned = canon.RootElement.GetProperty("pinned").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "The armistice");
        Assert.Equal("holding", pinned.GetProperty("fields")[0].GetProperty("value").GetString());
        Assert.Empty(Untils(canon.RootElement));

        using var brief = Read(_tools.CanonAsOf("ash:1", "starfall", brief: true));
        Assert.Contains(brief.RootElement.GetProperty("pinned").EnumerateArray(), p => p.GetProperty("name").GetString() == "The armistice");
        Assert.Empty(Untils(brief.RootElement));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-41")]
    [InlineData("ember:1")]
    [InlineData("ash")]
    [InlineData("ash:1")]
    [InlineData("ash:2")]
    public void CanonAsOfAtAPositionHasNoUntilAnywhere(string position)
    {
        using var json = Read(_tools.CanonAsOf(position, "starfall"));

        Assert.Empty(Untils(json.RootElement));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void NothingSaysAnUntilWasLeftOut()
    {
        var text = Text(_tools.GetEntity(Ilse, "starfall", "ember:1"));

        Assert.DoesNotContain("until", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ash:2", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AnUntilThatWasAllAFieldHadLeavesNoHintInItsKind()
    {
        _den.Write(
            "universes/starfall/entities/notes/the-toll-board.md",
            """
            ---
            type: note
            name: The toll board
            status: canon
            fields:
              plain_one: written plain
              single: { value: lasts, until: "ash:2" }
              listed:
                - { value: lasts, until: "ash:2" }
              dated:
                - { value: lasts, from: "ember:1", until: "ash:2" }
              marked: { value: lasts, until: "ash:2", status: draft }
              pinned_one: { value: lasts, until: "ash:2", pin: true }
              no_end: { value: stays }
            ---

            # The toll board
            """);

        using var json = Read(_tools.GetEntity("The toll board", "starfall", "ember:1"));

        Assert.Equal("plain", Field(json, "plain_one").GetProperty("kind").GetString());
        Assert.Equal("plain", Field(json, "single").GetProperty("kind").GetString());
        Assert.Equal("plain", Field(json, "listed").GetProperty("kind").GetString());

        // These say something else that makes them an entry, and that's all they say.
        Assert.Equal("temporal", Field(json, "dated").GetProperty("kind").GetString());
        Assert.Equal("entry", Field(json, "marked").GetProperty("kind").GetString());
        Assert.Equal("entry", Field(json, "pinned_one").GetProperty("kind").GetString());
        Assert.Equal("entry", Field(json, "no_end").GetProperty("kind").GetString());

        // A read with no position still has them as written.
        using var full = Read(_tools.GetEntity("The toll board", "starfall"));
        Assert.Equal("entry", Field(full, "single").GetProperty("kind").GetString());
        Assert.Equal("temporal", Field(full, "listed").GetProperty("kind").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AnAssistantHelpingToPlanSeesItByReadingWithoutAPosition()
    {
        using var json = Read(_tools.GetEntity(Ilse, "starfall"));

        Assert.Contains("ash:2", Untils(json.RootElement));
        Assert.Contains("ash:3", Untils(json.RootElement));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void TheEndOfTheStoryHasNoUntilToLeaveOut()
    {
        // Every fact with an until has ended by then, so it can't be how a planner reads one: get_entity is.
        using var json = Read(_tools.CanonAsOf(universe: "starfall"));

        Assert.Empty(Untils(json.RootElement));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void TheCodeTheWebReadsFromStillHasIt()
    {
        var view = new DenTools(_den.Root).GetEntity(Ilse, "starfall", "ember:1");

        Assert.Contains(view.Entity!.Fields!, f => f.Until == "ash:2");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AMappingOfTheWritersOwnWithAKeyNamedUntilIsNotAFactsUntil()
    {
        _den.Write(
            "universes/starfall/entities/notes/the-ledger.md",
            """
            ---
            type: note
            name: The ledger
            status: canon
            fields:
              plainmap: { name: inner, until: "ash:2" }
            ---

            # The ledger
            """);

        using var json = Read(_tools.GetEntity("The ledger", "starfall", "ember:1"));

        Assert.Equal("ash:2", Field(json, "plainmap").GetProperty("value").GetProperty("until").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AReaderWhoHasntMetTheEntityStillGetsNothing()
    {
        using var json = Read(_tools.GetEntity("Halden Pike", "starfall", "ember:3"));

        Assert.False(json.RootElement.GetProperty("in_view").GetBoolean());
    }

    private static JsonDocument Read(CallToolResult result) => JsonDocument.Parse(Text(result));

    private static string Text(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    private static JsonElement Field(JsonDocument json, string name) =>
        json.RootElement.GetProperty("entity").GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == name);

    private static JsonElement Section(JsonElement entity, string name) =>
        entity.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    /// <summary>Every <c>until</c> anywhere in a result.</summary>
    private static List<string?> Untils(JsonElement element)
    {
        var found = new List<string?>();
        Walk(element);
        return found;

        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in e.EnumerateObject())
                {
                    if (property.Name == "until")
                    {
                        found.Add(property.Value.GetString());
                    }

                    Walk(property.Value);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray())
                {
                    Walk(item);
                }
            }
        }
    }
}

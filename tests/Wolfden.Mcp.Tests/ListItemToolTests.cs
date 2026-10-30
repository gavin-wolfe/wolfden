using System.Text.Json;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// The <c>move</c> change of <c>update_fields</c>, and the <c>move</c> and
/// <c>replace</c> changes of <c>update_chapter_fields</c> and
/// <c>update_chapter_defaults</c> (#197): one list item moved or changed in
/// place, keeping its comments, over a copy of the invented Starfall den.
/// </summary>
public sealed class ListItemToolTests : IDisposable
{
    private const string LongDark = "universes/starfall/series/long-dark";
    private const string Ember = LongDark + "/books/ember/book.md";
    private const string Tomas = LongDark + "/entities/characters/tomas-reyes.md";

    private readonly TempDen _den = TempDen.Starfall();

    public ListItemToolTests()
    {
        _den.Write(Tomas, """
            ---
            id: 01JB0000000000000000000T01
            type: character
            name: Tomas Reyes
            aliases:
              # what the crew call him
              - Tom   # mostly
              - the Pilot
            status: canon
            ---

            Pilot of the *Tessaly*.
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        _den.Write(Ember, """
            ---
            id: 01JAQKFBF5KZNWJ47TAN9ZT24M
            name: Ember
            status: draft
            chapter_defaults:
              pov:
                - "[[mara-voss]]"  # most of the book
                - "[[tomas-reyes]]"
            chapters:
              - key: "1"
                number: 1
                fields:
                  pov: ["[[mara-voss]]", "[[tomas-reyes]]"]  # both watches
              - { key: "2", number: 2 }
            ---
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    private DenTools Tools => new(_den.Root);

    public void Dispose() => _den.Dispose();

    [Fact]
    [Trait("Scenario", "SC-MCP-16")]
    public void UpdateFieldsMovesAnItemWithItsComments()
    {
        var hash = Tools.GetEntity("Tomas Reyes", "starfall").Entity!.Hash!;

        var result = Tools.UpdateFields("Tomas Reyes", hash, Json("""[{"op": "move", "path": "aliases.0", "index": 1}]"""), "starfall");

        Assert.True(result.Changed);
        Assert.DoesNotContain(result.Effects, e => e.Contains("comment", StringComparison.Ordinal));
        Assert.Contains("aliases:\n  - the Pilot\n  # what the crew call him\n  - Tom   # mostly\nstatus: canon\n", _den.Read(Tomas), StringComparison.Ordinal);

        // A move needs an index, and an item that's there; nothing is written otherwise.
        var before = _den.Read(Tomas);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateFields("Tomas Reyes", result.Hash, Json("""[{"op": "move", "path": "aliases.0"}]"""), "starfall")).Code);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateFields("Tomas Reyes", result.Hash, Json("""[{"op": "move", "path": "aliases.2", "index": 0}]"""), "starfall")).Code);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateFields("Tomas Reyes", result.Hash, Json("""[{"op": "move", "path": "aliases", "index": 0}]"""), "starfall")).Code);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateFields("Tomas Reyes", result.Hash, Json("""[{"op": "move", "path": "status.0", "index": 0}]"""), "starfall")).Code);
        Assert.Equal(ToolErrorCodes.StaleFile, Assert.Throws<DenToolException>(() => Tools.UpdateFields("Tomas Reyes", hash, Json("""[{"op": "move", "path": "aliases.0", "index": 1}]"""), "starfall")).Code);
        Assert.Equal(before, _den.Read(Tomas));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-20")]
    public void ChapterFieldsMoveAndReplaceAnItemKeepingItsComments()
    {
        var hash = Tools.GetChapter("ember", "1", "starfall").Hash;

        var moved = Tools.UpdateChapterFields("ember", "1", hash, Json("""[{"op": "move", "field": "pov", "item": 1, "index": 0}]"""), "starfall");
        Assert.Contains("pov: [\"[[tomas-reyes]]\", \"[[mara-voss]]\"]  # both watches\n", _den.Read(Ember), StringComparison.Ordinal);

        var replaced = Tools.UpdateChapterFields("ember", "1", moved.Hash, Json("""[{"op": "replace", "field": "pov", "item": 1, "value": "[[halden-pike]]"}]"""), "starfall");
        Assert.Contains("pov: [\"[[tomas-reyes]]\", \"[[halden-pike]]\"]  # both watches\n", _den.Read(Ember), StringComparison.Ordinal);
        Assert.DoesNotContain(replaced.Effects, e => e.Contains("dropped", StringComparison.Ordinal));

        // An item is named by its number, a replace needs a value, and a field that isn't a list has no items.
        var before = _den.Read(Ember);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateChapterFields("ember", "1", replaced.Hash, Json("""[{"op": "move", "field": "pov", "index": 0}]"""), "starfall")).Code);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateChapterFields("ember", "1", replaced.Hash, Json("""[{"op": "replace", "field": "pov", "item": 0}]"""), "starfall")).Code);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateChapterFields("ember", "2", replaced.Hash, Json("""[{"op": "move", "field": "pov", "item": 0, "index": 1}]"""), "starfall")).Code);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateChapterFields("ember", "1", replaced.Hash, Json("""[{"op": "replace", "field": "pov", "item": 2, "value": "x"}]"""), "starfall")).Code);
        Assert.Equal(before, _den.Read(Ember));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-24")]
    public void ChapterDefaultsMoveAndReplaceAnItemKeepingItsComments()
    {
        var hash = Tools.GetChapterDefaults("starfall", book: "ember").Hash;

        var moved = Tools.UpdateChapterDefaults(hash, Json("""[{"op": "move", "field": "pov", "item": 0, "index": 1}]"""), "starfall", book: "ember");
        Assert.Contains("  pov:\n    - \"[[tomas-reyes]]\"\n    - \"[[mara-voss]]\"  # most of the book\nchapters:", _den.Read(Ember), StringComparison.Ordinal);

        var replaced = Tools.UpdateChapterDefaults(moved.Hash, Json("""[{"op": "replace", "field": "pov", "item": 1, "value": "[[halden-pike]]"}]"""), "starfall", book: "ember");
        Assert.Contains("    - \"[[halden-pike]]\"  # most of the book\n", _den.Read(Ember), StringComparison.Ordinal);
        Assert.True(replaced.Changed);

        // Moving an item to where it is changes nothing.
        Assert.False(Tools.UpdateChapterDefaults(replaced.Hash, Json("""[{"op": "move", "field": "pov", "item": 0, "index": 0}]"""), "starfall", book: "ember").Changed);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-21")]
    public void AMoveInAFinalBookIsRefused()
    {
        _den.Write(Ember, _den.Read(Ember).Replace("status: draft", "status: final", StringComparison.Ordinal));
        var before = _den.Read(Ember);
        var hash = Tools.GetChapter("ember", "1", "starfall").Hash;

        Assert.Equal(
            ToolErrorCodes.Frozen,
            Assert.Throws<DenToolException>(() => Tools.UpdateChapterFields("ember", "1", hash, Json("""[{"op": "move", "field": "pov", "item": 1, "index": 0}]"""), "starfall")).Code);
        Assert.Equal(
            ToolErrorCodes.Frozen,
            Assert.Throws<DenToolException>(() => Tools.UpdateChapterDefaults(hash, Json("""[{"op": "replace", "field": "pov", "item": 0, "value": "[[halden-pike]]"}]"""), "starfall", book: "ember")).Code);
        Assert.Equal(before, _den.Read(Ember));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

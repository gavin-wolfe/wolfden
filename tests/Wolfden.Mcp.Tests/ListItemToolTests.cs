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
        // The comment line above the first alias heads the list, so it stays at the top (format.md F-RT-7), and the result says so.
        Assert.Contains("aliases:\n  # what the crew call him\n  - the Pilot\n  - Tom   # mostly\nstatus: canon\n", _den.Read(Tomas), StringComparison.Ordinal);
        Assert.Contains(
            "'# what the crew call him' is above the whole aliases list, so it stayed at the top, over the new first item, rather than moving with the old one (format.md F-RT-7).",
            result.Effects);

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
    [Trait("Scenario", "SC-MCP-16")]
    public void DatedEntriesAreNeverMoved()
    {
        // F-TMP-7: of two entries with the same from, the later one holds (F-TMP-5), so moving calm after angry would unseat pinned canon.
        _den.Write(Tomas, _den.Read(Tomas).Replace(
            "status: canon\n",
            "status: canon\nfields:\n  x_mood:\n    - { value: calm, from: \"ember:1\" }\n    - { value: angry, from: \"ember:1\", pin: true }\n",
            StringComparison.Ordinal));
        var before = _den.Read(Tomas);
        var hash = Tools.GetEntity("Tomas Reyes", "starfall").Entity!.Hash!;

        var refused = Assert.Throws<DenToolException>(() => Tools.UpdateFields("Tomas Reyes", hash, Json("""[{"op": "move", "path": "fields.x_mood.0", "index": 1}]"""), "starfall"));

        Assert.Equal(ToolErrorCodes.UnsupportedEdit, refused.Code);
        Assert.Contains("F-TMP-7", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, _den.Read(Tomas));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-16")]
    public void ReplacingAMultiLineItemSaysWhichCommentsInsideItWent()
    {
        _den.Write(Tomas, _den.Read(Tomas).Replace(
            "status: canon\n",
            "status: canon\nfields:\n  x_crew:\n    - name: Mara\n      # the captain\n      role: captain\n    - Halden\n",
            StringComparison.Ordinal));
        var hash = Tools.GetEntity("Tomas Reyes", "starfall").Entity!.Hash!;

        var result = Tools.UpdateFields("Tomas Reyes", hash, Json("""[{"op": "set", "path": "fields.x_crew.0", "value": "Mara"}]"""), "starfall");

        Assert.Contains("    - Mara\n    - Halden\n", _den.Read(Tomas), StringComparison.Ordinal);
        Assert.Contains("Replaced fields.x_crew.0 whole, so the comment inside the old value went: '# the captain'.", result.Effects);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-20")]
    public void AListIsntAListItem()
    {
        _den.Write(Tomas, _den.Read(Tomas).Replace("status: canon\n", "status: canon\nfields:\n  x_tags: [a, b]\n", StringComparison.Ordinal));
        var hash = Tools.GetEntity("Tomas Reyes", "starfall").Entity!.Hash!;
        var before = _den.Read(Tomas);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateFields("Tomas Reyes", hash, Json("""[{"op": "add", "path": "aliases", "value": ["Tom"]}]"""), "starfall")).Code);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateFields("Tomas Reyes", hash, Json("""[{"op": "set", "path": "fields.x_tags.0", "value": []}]"""), "starfall")).Code);
        Assert.Equal(before, _den.Read(Tomas));

        var book = Tools.GetChapter("ember", "1", "starfall").Hash;
        var ember = _den.Read(Ember);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateChapterFields("ember", "1", book, Json("""[{"op": "replace", "field": "pov", "item": 0, "value": []}]"""), "starfall")).Code);
        Assert.Equal(ToolErrorCodes.BadArgument, Assert.Throws<DenToolException>(() => Tools.UpdateChapterDefaults(book, Json("""[{"op": "add", "field": "pov", "value": ["[[mara-voss]]"]}]"""), "starfall", book: "ember")).Code);
        Assert.Equal(ember, _den.Read(Ember));
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

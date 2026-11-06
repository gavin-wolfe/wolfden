using System.Text.Json;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// Writing the names readers learn partway through the story (<c>known_as</c>,
/// format.md F-ENT-10, #67 §9): writes take no position, links follow a changed
/// name as they follow an alias, and moving where readers learn one changes no link.
/// </summary>
public sealed class KnownAsWriteTests : IDisposable
{
    private const string Courier = "universes/starfall/entities/notes/the-courier.md";
    private const string Ledger = "universes/starfall/entities/notes/the-ledger.md";

    private readonly TempDen _den = TempDen.Starfall();

    public KnownAsWriteTests()
    {
        _den.Write(
            Courier,
            """
            ---
            id: 01M4TF5F0EKGGWD5FNVBAEY8K1
            type: note
            name: The Courier
            aliases: [the Messenger]
            known_as:
              - { value: Mara Voss, from: "ash:2" }
            status: canon
            from: "ember:1"
            ---

            Carries the post.
            """);
        _den.Write(
            Ledger,
            """
            ---
            id: 01M4TF5F0EKGGWD5FNVBAEY8K2
            type: note
            name: The ledger
            status: canon
            from: "ember:1"
            fields:
              signer: "[[Mara Voss]]"
            ---

            Kept at the Reach.

            ## Entries
            [[Mara Voss]] signed it.

            ## Later
            <!-- wolfden: from=ash:3 -->
            [[Mara Voss]] again, and [[the Messenger]].
            """);
    }

    private DenTools Tools => new(_den.Root);

    public void Dispose() => _den.Dispose();

    private string Hash(string name) => Tools.GetEntity(name, "starfall").Entity!.Hash!;

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static DenToolException Refused(string code, Action write)
    {
        var e = Assert.Throws<DenToolException>(write);
        Assert.Equal(code, e.Code);
        return e;
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-53")]
    public void MovingTheRevealIsAnOrdinaryEdit()
    {
        var ledger = _den.Read(Ledger);
        Assert.Equal(ToolErrorCodes.UnknownEntity, Assert.Throws<DenToolException>(() => Tools.GetEntity("Mara Voss", "starfall", "ember:3")).Code);

        var result = Tools.UpdateFields("The Courier", Hash("The Courier"), Json("""[{"op": "set", "path": "known_as.0.from", "value": "ember:3"}]"""), "starfall");

        // No other file changes, since links don't depend on where readers learn the name.
        Assert.Equal(Courier, result.Path);
        Assert.Contains("  - { value: Mara Voss, from: \"ember:3\" }\n", _den.Read(Courier), StringComparison.Ordinal);
        Assert.Equal(ledger, _den.Read(Ledger));
        Assert.Equal("The Courier", Tools.GetEntity("Mara Voss", "starfall", "ember:3").Entity!.Name);
        Assert.Equal(ToolErrorCodes.UnknownEntity, Assert.Throws<DenToolException>(() => Tools.GetEntity("Mara Voss", "starfall", "ember:2b")).Code);
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-53")]
    [InlineData("""[{"op": "set", "path": "known_as.0.value", "value": "Mara Holt"}]""", "unsupported_edit")]
    [InlineData("""[{"op": "remove", "path": "known_as.0"}]""", "unsupported_edit")]
    [InlineData("""[{"op": "remove", "path": "known_as"}]""", "unsupported_edit")]
    [InlineData("""[{"op": "set", "path": "known_as", "value": []}]""", "unsupported_edit")]
    [InlineData("""[{"op": "set", "path": "known_as.0.from", "value": null}]""", "bad_argument")]
    [InlineData("""[{"op": "set", "path": "known_as.0.from", "value": "ash:99"}]""", "bad_position")]
    [InlineData("""[{"op": "add", "path": "known_as", "value": {"value": "Mara Holt"}}]""", "bad_argument")]
    [InlineData("""[{"op": "add", "path": "known_as", "value": "Mara Holt"}]""", "bad_argument")]
    [InlineData("""[{"op": "add", "path": "known_as", "value": {"value": "Mara Holt", "from": "ash:99"}}]""", "bad_position")]
    public void ANameCantBeChangedOrLostHere(string changes, string code)
    {
        var before = _den.Read(Courier);

        var e = Refused(code, () => Tools.UpdateFields("The Courier", Hash("The Courier"), Json(changes), "starfall"));

        Assert.Equal(before, _den.Read(Courier));
        if (code == "unsupported_edit")
        {
            Assert.Contains("change_alias", e.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ANameCanBeAddedHere()
    {
        var result = Tools.UpdateFields(
            "The Courier", Hash("The Courier"), Json("""[{"op": "add", "path": "known_as", "value": {"value": "Mara Holt", "from": "ash:3"}}]"""), "starfall");

        Assert.Contains("  - { value: Mara Holt, from: \"ash:3\" }\n", _den.Read(Courier), StringComparison.Ordinal);
        Assert.Equal("The Courier", Tools.GetEntity("Mara Holt", "starfall", "ash:3").Entity!.Name);
        Assert.Equal(ToolErrorCodes.UnknownEntity, Assert.Throws<DenToolException>(() => Tools.GetEntity("Mara Holt", "starfall", "ash:2")).Code);
        Assert.True(result.Changed);
    }

    [Fact]
    [Trait("Scenario", "SC-RT-19")]
    public void RenamingALaterNameRewritesItsLinksAtEveryPositionAndKeepsItsFrom()
    {
        var result = Tools.ChangeAlias("The Courier", "Mara Voss", Hash("The Courier"), newAlias: "Mara Holt", universe: "starfall");

        Assert.Contains("  - { value: Mara Holt, from: \"ash:2\" }\n", _den.Read(Courier), StringComparison.Ordinal);
        var ledger = _den.Read(Ledger);
        Assert.Contains("  signer: \"[[Mara Holt]]\"\n", ledger, StringComparison.Ordinal);
        Assert.Contains("[[Mara Holt]] signed it.", ledger, StringComparison.Ordinal);
        Assert.Contains("[[Mara Holt]] again", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("Mara Voss", ledger, StringComparison.Ordinal);
        Assert.Contains(result.Effects, e => e.Contains("rewrote 3 link(s)", StringComparison.Ordinal));
        Assert.Equal(ToolErrorCodes.UnknownEntity, Assert.Throws<DenToolException>(() => Tools.GetEntity("Mara Holt", "starfall", "ember:3")).Code);
    }

    [Fact]
    [Trait("Scenario", "SC-RT-19")]
    public void MovingANameBetweenTheTwoKeysChangesNoLink()
    {
        var ledger = _den.Read(Ledger);

        // Plain: known wherever the note is.
        var plain = Tools.ChangeAlias("The Courier", "Mara Voss", Hash("The Courier"), from: "", universe: "starfall");
        var courier = _den.Read(Courier);
        Assert.Contains("aliases: [the Messenger, Mara Voss]\n", courier, StringComparison.Ordinal);
        Assert.DoesNotContain("value: Mara Voss", courier, StringComparison.Ordinal);
        Assert.Equal([Courier], plain.Files!.Select(f => f.Path));
        Assert.Equal(ledger, _den.Read(Ledger));
        Assert.Contains(plain.Effects, e => e.Contains("is a plain alias now", StringComparison.Ordinal) && e.Contains("gives it away", StringComparison.Ordinal));
        Assert.Equal("The Courier", Tools.GetEntity("Mara Voss", "starfall", "ember:1").Entity!.Name);

        // And a plain alias made one readers learn at a position.
        var known = Tools.ChangeAlias("The Courier", "the Messenger", plain.Hash, from: "ember:2", universe: "starfall");
        courier = _den.Read(Courier);
        Assert.Contains("aliases: [Mara Voss]\n", courier, StringComparison.Ordinal);
        Assert.Contains("  - { value: the Messenger, from: \"ember:2\" }\n", courier, StringComparison.Ordinal);
        Assert.Equal(ledger, _den.Read(Ledger));
        Assert.Contains(known.Effects, e => e.StartsWith("Changed where readers learn 'the Messenger'. No link was changed", StringComparison.Ordinal));
        Assert.Equal(ToolErrorCodes.UnknownEntity, Assert.Throws<DenToolException>(() => Tools.GetEntity("the Messenger", "starfall", "ember:1")).Code);

        // A known_as name's from changes in place, and a from the universe doesn't have is refused.
        Tools.ChangeAlias("The Courier", "the Messenger", known.Hash, from: "ember:3", universe: "starfall");
        Assert.Contains("  - { value: the Messenger, from: \"ember:3\" }\n", _den.Read(Courier), StringComparison.Ordinal);
        Refused(ToolErrorCodes.BadPosition, () => Tools.ChangeAlias("The Courier", "the Messenger", Hash("The Courier"), from: "ember:99", universe: "starfall"));
        Refused(ToolErrorCodes.BadPosition, () => Tools.ChangeAlias("The Courier", "the Messenger", Hash("The Courier"), from: "Ember", universe: "starfall"));
        Assert.Equal(ledger, _den.Read(Ledger));
    }

    [Fact]
    [Trait("Scenario", "SC-RT-19")]
    public void AKeptOrRemovedLaterName()
    {
        // A new name kept beside it goes in the same key with the same from.
        var kept = Tools.ChangeAlias("The Courier", "Mara Voss", Hash("The Courier"), newAlias: "Voss", keepOldAlias: true, universe: "starfall");
        var courier = _den.Read(Courier);
        Assert.Contains("  - { value: Mara Voss, from: \"ash:2\" }\n  - { value: Voss, from: \"ash:2\" }\n", courier, StringComparison.Ordinal);
        Assert.Contains("[[Mara Voss]] signed it.", _den.Read(Ledger), StringComparison.Ordinal);

        // Removed, its links get the note's name, as for an alias.
        Tools.ChangeAlias("The Courier", "Mara Voss", kept.Hash, universe: "starfall");
        courier = _den.Read(Courier);
        Assert.DoesNotContain("Mara Voss", courier, StringComparison.Ordinal);
        Assert.Contains("[[The Courier]] signed it.", _den.Read(Ledger), StringComparison.Ordinal);

        // Given a name it doesn't have, the message lists both kinds.
        var e = Refused(ToolErrorCodes.BadArgument, () => Tools.ChangeAlias("The Courier", "Nobody", Hash("The Courier"), universe: "starfall"));
        Assert.Contains("Its aliases: 'the Messenger'; names readers learn later (known_as): 'Voss'.", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenamingTheNoteToOneOfItsLaterNamesMakesItKnownEverywhere()
    {
        var result = Tools.RenameEntity("The Courier", "Mara Voss", Hash("The Courier"), universe: "starfall");

        var courier = _den.Read(Courier);
        Assert.Contains("\nname: Mara Voss\n", courier, StringComparison.Ordinal);
        Assert.DoesNotContain("value: Mara Voss", courier, StringComparison.Ordinal);
        Assert.Contains(result.Effects, e => e.Contains("'Mara Voss' was a name readers learn at ash:2 (known_as); it's the name now", StringComparison.Ordinal));
        Assert.Equal("Mara Voss", Tools.GetEntity("Mara Voss", "starfall", "ember:1").Entity!.Name);
    }

    [Fact]
    public void ACopyHasNoneOfTheOriginalsOtherNames()
    {
        var result = Tools.CopyEntity("The Courier", "The Second Courier", "starfall");

        var copy = _den.Read(result.Path);
        Assert.DoesNotContain("known_as", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("aliases", copy, StringComparison.Ordinal);
        Assert.Contains(result.Effects, e => e.Contains("names readers learn later (known_as: 'Mara Voss') weren't copied", StringComparison.Ordinal));
        Assert.Equal("The Courier", Tools.GetEntity("Mara Voss", "starfall").Entity!.Name);
    }

    [Fact]
    public void ANewNoteCanHaveLaterNames()
    {
        var result = Tools.CreateEntity(
            "note", "The man in the grey coat", "starfall", from: "ember:1", status: "canon",
            knownAs: Json("""[{"value": "Corin Hask", "from": "ash:1"}, {"value": "Corin", "from": "ember:3"}]"""));

        Assert.Equal("universes/starfall/entities/notes/the-man-in-the-grey-coat.md", result.Path);
        var text = _den.Read(result.Path);
        Assert.Contains("known_as:\n  - { value: Corin Hask, from: \"ash:1\" }\n  - { value: Corin, from: \"ember:3\" }\n", text, StringComparison.Ordinal);
        var early = Tools.GetEntity("The man in the grey coat", "starfall", "ember:3", out var horizon);
        Assert.Equal(["Corin"], Wolfden.Index.Reading.AsOfReads.ForReaders(early, horizon).Entity!.Aliases);
        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Path == result.Path && d.Rule is "F-ENT-10" or "F-POS-4");

        foreach (var bad in new[] { """[{"value": "Corin", "from": "ash:99"}]""", """[{"value": "Corin"}]""", """{"value": "Corin", "from": "ash:1"}""" })
        {
            Refused(bad.Contains("ash:99", StringComparison.Ordinal) ? ToolErrorCodes.BadPosition : ToolErrorCodes.BadArgument,
                () => Tools.CreateEntity("note", "Another stranger", "starfall", knownAs: Json(bad)));
            Assert.False(File.Exists(Path.Combine(_den.Root, "universes/starfall/entities/notes/another-stranger.md")));
        }
    }
}

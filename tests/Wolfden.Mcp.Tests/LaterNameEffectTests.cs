using System.Text.Json;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// A write's effects warn when the text it saves names someone, or links to them, by a
/// name readers learn later than the text's position (#67, PR 5): <c>check</c>'s
/// F-NAME-1 and F-LINK-13, for the notes written only. The write is never refused.
/// </summary>
public sealed class LaterNameEffectTests : IDisposable
{
    private const string Courier = "universes/starfall/entities/notes/the-courier.md";
    private const string Ledger = "universes/starfall/entities/notes/the-ledger.md";
    private const string Plain = "warning F-NAME-1: 'Mara Voss' is a name of 'The Courier' that readers learn at ash:2, and this text is at ";
    private const string Linked = "warning F-LINK-13: [[Mara Voss]]: 'Mara Voss' is a name of 'The Courier' that readers learn at ash:2, after this link's position, ";

    private readonly TempDen _den = TempDen.Starfall();

    public LaterNameEffectTests()
    {
        _den.Write(
            Courier,
            """
            ---
            id: 01M4TF5F0EKGGWD5FNVBAEY8K1
            type: note
            name: The Courier
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
            ---

            Kept at the Reach. Mara Voss signed it.

            ## Entries
            A stranger paid.
            """);
    }

    private DenTools Tools => new(_den.Root);

    public void Dispose() => _den.Dispose();

    private string Hash(string entity) => Tools.GetEntity(entity, "starfall").Entity!.Hash!;

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static List<string> Warned(WriteResult result) =>
        result.Effects.Where(e => e.Contains(": warning F-", StringComparison.Ordinal)).ToList();

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void AppendingTheNameOrALinkByItWarnsAndIsWritten()
    {
        var result = Tools.AppendSection("The ledger", "Entries", "Mara Voss paid, and [[Mara Voss]] left.", Hash("The ledger"), "starfall");

        Assert.True(result.Changed);
        Assert.Contains("[[Mara Voss]] left.", _den.Read(Ledger), StringComparison.Ordinal);
        Assert.Collection(
            Warned(result),
            w => Assert.StartsWith($"{Ledger}:14: {Plain}ember:1. It may be a spoiler.", w, StringComparison.Ordinal),
            w => Assert.Equal($"{Ledger}:14: {Linked}ember:1. It may be a spoiler.", w));

        // The plain mention already there, at line 9, isn't said again, and check says the same three.
        var check = Tools.Check(null, null, 5000, "starfall").Diagnostics.Where(d => d.Path == Ledger && d.Rule is "F-NAME-1" or "F-LINK-13").ToList();
        Assert.Equal([9, 14, 14], check.Select(d => d.Line ?? 0).Order());
        Assert.All(Warned(result), w => Assert.Contains(check, d => w == $"{d.Path}:{d.Line}: warning {d.Rule}: {d.Message}"));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void TextWithoutTheNameSaysNothingNew()
    {
        var result = Tools.AppendSection("The ledger", "Entries", "Paid in full.", Hash("The ledger"), "starfall");

        Assert.Empty(Warned(result));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void TextAfterTheRevealSaysNothing()
    {
        var result = Tools.AppendSection("The ledger", "Settled", "Mara Voss paid. [[Mara Voss]]", Hash("The ledger"), "starfall", newSection: true, from: "ash:3");

        Assert.Empty(Warned(result));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void CreateEntityAndAddIdeaWarnAboutTheirText()
    {
        var created = Tools.CreateEntity("note", "The receipt", "starfall", text: "Signed by Mara Voss.", from: "ember:2");
        Assert.Equal(Plain + "ember:2.", Assert.Single(Warned(created)).Split(": ", 2)[1].Split(" It may be", 2)[0]);

        // An idea is out of canon, so its text isn't canon text.
        var idea = Tools.AddIdea("Mara Voss could have a twin.", name: "Twin", universe: "starfall");
        Assert.Empty(Warned(idea));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void AFieldValueWithTheNameWarns()
    {
        var result = Tools.UpdateFields("The ledger", Hash("The ledger"), Json("""[{"op": "set", "path": "fields.signer", "value": "Mara Voss"}]"""), "starfall");

        Assert.Contains(Warned(result), w => w.Contains(Plain + "ember:1.", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void MovingARevealLaterWarnsAboutTheOwnersText()
    {
        _den.Write(Courier, _den.Read(Courier).Replace("Carries the post.", "Carries the post.\n\n## Ash\n<!-- wolfden: from=ash:2 -->\nMara Voss, at last.", StringComparison.Ordinal));

        var result = Tools.UpdateFields("The Courier", Hash("The Courier"), Json("""[{"op": "set", "path": "known_as.0.from", "value": "ash:3"}]"""), "starfall");

        var warned = Assert.Single(Warned(result));
        Assert.Contains("readers learn at ash:3, and this text is at ash:2", warned, StringComparison.Ordinal);
        Assert.Contains(result.Effects, e => e.Contains("run check to find text and links in other notes", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void ChangeAliasWarnsAboutTheLinksItRewrites()
    {
        _den.Write(Ledger, _den.Read(Ledger).Replace("A stranger paid.", "[[Mara Voss]] paid.", StringComparison.Ordinal));

        var result = Tools.ChangeAlias("The Courier", "Mara Voss", Hash("The Courier"), newAlias: "Mara Holt", universe: "starfall");

        Assert.Contains("[[Mara Holt]] paid.", _den.Read(Ledger), StringComparison.Ordinal);
        var warned = Assert.Single(Warned(result));
        Assert.StartsWith($"{Ledger}:12: warning F-LINK-13: [[Mara Holt]]: 'Mara Holt' is a name of 'The Courier' that readers learn at ash:2", warned, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void RenamingToAnotherNotesLaterNameSaysWhatCheckSays()
    {
        // Once it's the ledger's own name, readers know those words from the ledger's entry (F-NAME-1's "already known" rule).
        var result = Tools.RenameEntity("The ledger", "Mara Voss", Hash("The ledger"), universe: "starfall", allowRelinks: true);

        Assert.True(result.Changed);
        Assert.Empty(Warned(result));
        Assert.DoesNotContain(Tools.Check(null, null, 5000, "starfall").Diagnostics, d => d.Path == Ledger && d.Rule == "F-NAME-1");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void RenamingTheNoteWhoseNameItIsRepeatsNothing()
    {
        _den.Write(Ledger, _den.Read(Ledger).Replace("A stranger paid.", "[[Mara Voss]] too. [[The Courier]]", StringComparison.Ordinal));
        _den.Write(Courier, _den.Read(Courier).Replace("Carries the post.", "Carries the post. Mara Voss, really.", StringComparison.Ordinal));

        var renamed = Tools.RenameEntity("The Courier", "The Envoy", Hash("The Courier"), universe: "starfall");
        Assert.Contains("[[The Envoy]]", _den.Read(Ledger), StringComparison.Ordinal);
        Assert.Empty(Warned(renamed));

        // A whole-file save that changes name: is the same.
        var text = _den.Read(Courier).Replace("name: The Envoy", "name: The Emissary", StringComparison.Ordinal);
        Assert.Empty(Warned(Tools.ReplaceFile("The Envoy", text, Hash("The Envoy"), "starfall")));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void SavingTheWholeFileWarnsAboutWhatItAdds()
    {
        var text = _den.Read(Ledger).Replace("A stranger paid.", "Mara Voss paid.", StringComparison.Ordinal);

        var result = Tools.ReplaceFile("The ledger", text, Hash("The ledger"), "starfall");

        Assert.Equal($"{Ledger}:12: {Plain}ember:1.", Assert.Single(Warned(result)).Split(" It may be", 2)[0]);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void ACopyWarnsAboutTheTextItBringsAndAMoveThatKeepsViewsSaysNothing()
    {
        var copied = Tools.CopyEntity("The ledger", "The second ledger", "starfall");
        Assert.Contains(Warned(copied), w => w.Contains(Plain + "ember:1.", StringComparison.Ordinal));

        var moved = Tools.MoveEntity("The ledger", Hash("The ledger"), toBook: "ember", universe: "starfall");
        Assert.True(moved.Changed);
        Assert.Empty(Warned(moved));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void ANoteSavedBackIntoTheDenWarnsAboutItsText()
    {
        const string broken = "universes/starfall/entities/notes/the-draft.md";
        _den.Write(broken, "---\ntype: note\nstatus: canon\nfrom: \"ember:1\"\n---\n\nMara Voss.\n");
        var hash = Tools.GetFileSource(broken).Hash;

        // It gets an id (F-ID-6), a line above the text.
        var result = Tools.ReplaceFileAt(broken, "---\ntype: note\nname: The draft\nstatus: canon\nfrom: \"ember:1\"\n---\n\nMara Voss.\n", hash);

        Assert.Contains("It's back in the den", result.Effects[0], StringComparison.Ordinal);
        Assert.StartsWith($"{broken}:9: {Plain}ember:1.", Assert.Single(Warned(result)), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void WithSpoilerChecksOffNothingIsSaid()
    {
        _den.Write("den.yaml", _den.Read("den.yaml").Replace("spoiler_checks: true", "spoiler_checks: false", StringComparison.Ordinal));

        var result = Tools.AppendSection("The ledger", "Entries", "Mara Voss paid, and [[Mara Voss]] left.", Hash("The ledger"), "starfall");

        Assert.True(result.Changed);
        Assert.Empty(Warned(result));
    }
}

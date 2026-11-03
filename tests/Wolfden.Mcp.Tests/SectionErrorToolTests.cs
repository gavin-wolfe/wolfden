using Wolfden.Index.Reading;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// format.md F-GEN-2 (#50) through the tools an assistant and the web UI use,
/// over a copy of the invented Starfall den: reading a note with a section an
/// error leaves out, saving it, renaming and moving around it, and freezing.
/// </summary>
public sealed class SectionErrorToolTests : IDisposable
{
    private const string Universe = "starfall";
    private const string Series = "universes/starfall/series/long-dark";
    private const string Ember = Series + "/books/ember";
    private const string Ash = Series + "/books/ash";
    private const string Note = Ember + "/entities/characters/greta-brandt.md";
    private const string Id = "01M4TF5F0EKGGWD5FNVBAEY8Z1";

    private const string Text = $"""
        ---
        id: {Id}
        type: character
        name: Greta Brandt
        status: canon
        ---

        # Greta Brandt

        A medic.

        ## Overview
        Serves on the [[Tessaly]].

        ## Quorlax reveal
        <!-- wolfden: status=maybe -->
        She knows [[Halden Pike]]. Zanthic vow.

        ### Zanthic detail
        Under it.

        """;

    private readonly TempDen _den = TempDen.Starfall();

    public SectionErrorToolTests()
    {
        _den.Write(Note, Text);
    }

    private DenTools Tools => new(_den.Root);

    public void Dispose() => _den.Dispose();

    private string Hash() => Tools.GetEntity("Greta Brandt", Universe).Entity!.Hash!;

    [Fact]
    [Trait("Scenario", "SC-MCP-35")]
    public void TheFullViewFlagsItInPlaceAndAViewAtAPositionDoesntNameIt()
    {
        var full = Tools.GetEntity("Greta Brandt", Universe).Entity!;

        Assert.Equal(["Overview", "Quorlax reveal", "Zanthic detail"], full.Sections!.Select(s => s.Name));
        Assert.Null(full.Sections![0].LeftOut);
        var why = Assert.Single(full.Sections![1].LeftOut!);
        Assert.Equal((16, "F-STAT-1"), (why.Line!.Value, why.Rule));
        Assert.Contains("'Quorlax reveal' is left out", why.Message, StringComparison.Ordinal);
        Assert.Equal(why, Assert.Single(full.Sections![2].LeftOut!));
        Assert.Empty(full.Pinned!.Sections);

        foreach (var position in new[] { "ember:1", "ash:1", "ash:4" })
        {
            var result = Tools.GetEntity("Greta Brandt", Universe, position);
            var json = System.Text.Json.JsonSerializer.Serialize(result, ToolJson.Options);
            Assert.Equal(["Overview"], result.Entity!.Sections!.Select(s => s.Name));
            Assert.DoesNotContain("Quorlax", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Zanthic", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("left_out", json, StringComparison.Ordinal);

            var canon = System.Text.Json.JsonSerializer.Serialize(Tools.CanonAsOf(Universe, position), ToolJson.Options);
            Assert.DoesNotContain("Quorlax", canon, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Zanthic", canon, StringComparison.OrdinalIgnoreCase);

            var reader = new DenReader(_den.Root);
            Assert.Empty(reader.Search("zanthic", Universe, asOf: position).Hits);
            Assert.Empty(reader.Search("quorlax", Universe, asOf: position).Hits);
        }

        Assert.DoesNotContain("Quorlax", System.Text.Json.JsonSerializer.Serialize(Tools.CanonAsOf(Universe), ToolJson.Options), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-35")]
    public void SearchFindsItWithoutAPositionMarkedAndCanonOnlyLeavesItOut()
    {
        var hit = Tools.Search("zanthic", Universe).Hits;

        Assert.Equal(["Quorlax reveal", "Zanthic detail"], hit.Select(h => h.Section).Order(StringComparer.Ordinal));
        Assert.All(hit, h =>
        {
            Assert.True(h.LeftOut);
            Assert.False(h.InCanon);
        });
        Assert.Empty(Tools.Search("zanthic", Universe, canonOnly: true).Hits);
        Assert.Null(Tools.Search("medic", Universe).Hits.Single(h => h.Entity.Name == "Greta Brandt").LeftOut);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-36")]
    public void AnEditElsewhereIsAllowedAndTheResultSaysTheErrorIsStillThere()
    {
        var source = Tools.GetSource("Greta Brandt", Universe);
        Assert.Equal("F-STAT-1", Assert.Single(source.Sections[1].LeftOut!).Rule);
        Assert.Null(source.Sections[0].LeftOut);

        var result = Tools.ReplaceSection("Greta Brandt", 0, "Overview", "Serves on the [[Tessaly]], and always did.", source.Hash, Universe);

        Assert.True(result.Changed);
        var kept = Assert.Single(result.Warnings, w => w.Severity == "error");
        Assert.Equal("F-STAT-1", kept.Rule);
        Assert.Contains("always did", _den.Read(Note), StringComparison.Ordinal);
        Assert.Contains("status=maybe", _den.Read(Note), StringComparison.Ordinal);

        // Editing the bad section's text is allowed too, and so is adding a section, with the lines shifting.
        var again = Tools.GetSource("Greta Brandt", Universe);
        Tools.ReplaceSection("Greta Brandt", 1, "Quorlax reveal", "Rewritten while the typo's there.", again.Hash, Universe);
        Assert.True(Tools.AppendSection("Greta Brandt", "Added", "A new section.", Hash(), Universe).Changed);
        Assert.Equal(1, Tools.Check(path: Note).Errors);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-36")]
    public void EditingTheFileToFixItWorksAndBringsTheSectionBack()
    {
        Assert.DoesNotContain("Quorlax reveal", Tools.GetEntity("Greta Brandt", Universe, "ash:1").Entity!.Sections!.Select(s => s.Name));

        var result = Tools.ReplaceFile("Greta Brandt", Text.Replace("status=maybe", "status=canon", StringComparison.Ordinal), Hash(), Universe);

        Assert.DoesNotContain(result.Warnings, w => w.Severity == "error");
        Assert.Equal(0, Tools.Check(path: Note).Errors);
        Assert.Contains("Quorlax reveal", Tools.GetEntity("Greta Brandt", Universe, "ash:1").Entity!.Sections!.Select(s => s.Name));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-36")]
    public void ANewBadValueIsRefusedAndNothingIsWritten()
    {
        var before = _den.Read(Note);

        var refused = Assert.Throws<DenToolException>(() =>
            Tools.ReplaceFile("Greta Brandt", Text + "\n## Another\n<!-- wolfden: pin=yes -->\nText.\n", Hash(), Universe));

        Assert.Equal(ToolErrorCodes.InvalidEdit, refused.Code);
        Assert.Contains("would add errors", refused.Message, StringComparison.Ordinal);
        Assert.Contains("F-PIN-1", refused.Message, StringComparison.Ordinal);

        // It names only what's new, not the typo that was there.
        Assert.DoesNotContain("F-STAT-1", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, _den.Read(Note));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-37")]
    public void RenamingANoteRewritesTheLinksInsideALeftOutSection()
    {
        const string Pike = Ash + "/entities/characters/halden-pike.md";
        var hash = Tools.GetEntity("Halden Pike", Universe).Entity!.Hash!;

        var result = Tools.RenameEntity("Halden Pike", "Halden Rook", hash, universe: Universe);

        Assert.Contains(result.Files!, f => f.Path == Note);
        Assert.Contains(result.Files!, f => f.Path == Pike);
        var text = _den.Read(Note);
        Assert.Contains("[[Halden Rook]]", text, StringComparison.Ordinal);
        Assert.Contains("status=maybe", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-37")]
    public void MovingANoteWithALeftOutSectionKeepsItAsItIs()
    {
        var result = Tools.MoveEntity("Greta Brandt", Hash(), toSeries: "long-dark", universe: Universe);

        Assert.True(result.Changed);
        var moved = Series + "/entities/characters/greta-brandt.md";
        Assert.Equal(moved, result.Path);
        Assert.Contains("status=maybe", _den.Read(moved), StringComparison.Ordinal);
        Assert.Equal(1, Tools.Check(path: moved).Errors);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-37")]
    public void ACopyOfANoteWithABadSectionIsRefusedSinceItWouldAddAnError()
    {
        var refused = Assert.Throws<DenToolException>(() => Tools.CopyEntity("Greta Brandt", "Greta Two", universe: Universe));

        Assert.Equal(ToolErrorCodes.InvalidEdit, refused.Code);
        Assert.Contains("F-STAT-1", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-20")]
    public void ABookWithALeftOutSectionCantBeFrozenUntilItsFixed()
    {
        var status = Tools.GetBookFreeze("ember", Universe);

        var refused = Assert.Throws<DenToolException>(() => Tools.FreezeBook("ember", status.Hash, Universe));

        Assert.Equal(ToolErrorCodes.InvalidEdit, refused.Code);
        Assert.Contains(Note, refused.Message, StringComparison.Ordinal);
        Assert.Contains("can't be fixed without unfreezing", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("status: final", _den.Read(Ember + "/book.md"), StringComparison.Ordinal);

        // Another book isn't held up by it.
        Assert.Equal("final", Tools.FreezeBook("ash", Tools.GetBookFreeze("ash", Universe).Hash, Universe).Status);

        Tools.ReplaceFile("Greta Brandt", Text.Replace("status=maybe", "status=draft", StringComparison.Ordinal), Hash(), Universe);
        Assert.Equal("final", Tools.FreezeBook("ember", status.Hash, Universe).Status);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-20")]
    public void ABadSectionInBookMdRefusesTooAndABookMarkedFinalByHandIsntSnapshotted()
    {
        const string BookFile = Ember + "/book.md";
        var book = _den.Read(BookFile).ReplaceLineEndings("\n");
        Tools.ReplaceFile("Greta Brandt", Text.Replace("status=maybe", "status=draft", StringComparison.Ordinal), Hash(), Universe);
        _den.Write(BookFile, book + "\n## Plan\n<!-- wolfden: canon=x -->\nLater.\n");

        var refused = Assert.Throws<DenToolException>(() => Tools.FreezeBook("ember", null, Universe));
        Assert.Contains(BookFile, refused.Message, StringComparison.Ordinal);

        _den.Write(BookFile, book.Replace("status: draft", "status: final", StringComparison.Ordinal) + "\n## Plan\n<!-- wolfden: canon=x -->\nLater.\n");
        Assert.Contains(BookFile, Assert.Throws<DenToolException>(() => Tools.FreezeBook("ember", null, Universe)).Message, StringComparison.Ordinal);

        // It can be unfrozen, since that's how it gets fixed.
        Assert.Equal("draft", Tools.UnfreezeBook("ember", null, Universe).Status);
    }
}

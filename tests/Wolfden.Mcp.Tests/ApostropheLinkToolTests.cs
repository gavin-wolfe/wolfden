using Wolfden.Index;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// Link matching treats every apostrophe as one character (format.md F-LINK-4,
/// #308), through the tools over a copy of the Dracula sample: finding a note,
/// a <c>ref</c> field, the index's stored names, and renames, which leave a
/// writer's links alone unless their text really changes.
/// </summary>
public sealed class ApostropheLinkToolTests : IDisposable
{
    private const string Journal = "universes/dracula/entities/notes/minas-journal.md";
    private const string Note = "universes/dracula/entities/notes/whitby-dialect.md";
    private const string BookFile = "universes/dracula/books/dracula/book.md";
    private const string Twin = "universes/dracula/books/dracula/entities/notes/mina-curly.md";

    private readonly TempDen _den = TempDen.Dracula();

    private DenTools Tools => new(_den.Root);

    public void Dispose() => _den.Dispose();

    /// <summary>The note "Mina's Journal", with a section and an alias, and a note whose links use each apostrophe.</summary>
    private void WriteNotes(string name = "Mina's Journal")
    {
        _den.Write(Journal,
            $"---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z2\ntype: note\nname: \"{name}\"\naliases: [\"Mina's Diary\"]\nstatus: draft\n---\n\n# {name}\n\n## Mina's Notes\nShorthand.\n");
        _den.Write(Note,
            "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z1\ntype: note\nname: Whitby dialect\nstatus: draft\n---\n\n# Whitby dialect\n\n" +
            "[[Mina’s Journal]], [[Mina's Journal]], [[Mina’s Journal|her words’ home]], [[Mina’s Journal#Mina’s Notes]], [[mina’s diary]], and [[minas-journal]].\n");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void ANoteIsFoundAndItsLinksReachItWithAnyApostrophe()
    {
        WriteNotes();

        foreach (var written in new[] { "Mina's Journal", "Mina’s Journal", "MINA‘S JOURNAL", "minaʼs journal", "Mina＇s Journal", "Mina’s Diary" })
        {
            Assert.Equal(Journal, Tools.GetEntity(written).Entity!.Path);
        }

        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule is "F-LINK-7" or "F-LINK-8" or "F-LINK-9");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void TheIndexStoresNamesAndAliasesWithTheStraightApostrophe()
    {
        WriteNotes("Mina’s Journal");
        IndexBuilder.Rebuild(_den.Root);

        using var index = DenIndex.Open(_den.Root);
        var entity = index.Context.Entities.Single(e => e.Path == Journal);
        Assert.Equal("Mina’s Journal", entity.Name);
        Assert.Equal("mina's journal", entity.NameNormalized);
        Assert.Contains(index.Context.Aliases, a => a.EntityId == entity.Id && a.AliasNormalized == "mina's diary");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void ARefFieldReachesItsNoteWithAnyApostrophe()
    {
        var mina = "universes/dracula/books/dracula/entities/characters/mina-harker.md";
        _den.Write(mina, _den.Read(mina).Replace("name: Mina Harker", "name: \"Mina's Self\"", StringComparison.Ordinal));
        _den.Write(BookFile, _den.Read(BookFile).Replace("[[mina-harker]]", "[[Mina’s Self]]", StringComparison.Ordinal));

        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Path == BookFile && d.Rule is "F-LINK-7" or "F-LINK-8" or "F-LINK-11");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void TwoNotesThatDifferOnlyByApostropheAreAmbiguous()
    {
        WriteNotes();
        _den.Write("universes/dracula/entities/notes/mina-curly.md",
            "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z3\ntype: note\nname: \"Mina’s Journal\"\nstatus: draft\n---\n\n# Mina’s Journal\n");

        Assert.Contains(Tools.Check().Diagnostics, d => d.Rule == "F-LINK-7" && d.Path == Note);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void ANewNoteThatDiffersOnlyByApostropheWouldTakeLinksSoItsRefusedUnlessAllowed()
    {
        WriteNotes();

        var refused = Assert.Throws<DenToolException>(() => Tools.CreateEntity("note", "Mina’s Journal"));

        Assert.Equal(ToolErrorCodes.BadArgument, refused.Code);
        var made = Tools.CreateEntity("note", "Mina’s Journal", allowRelinks: true);
        Assert.Equal("universes/dracula/entities/notes/minas-journal-2.md", made.Path);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void ChangingOnlyAnApostropheInANameChangesNoLink()
    {
        WriteNotes();
        var before = _den.Read(Note);

        var result = Tools.RenameEntity("Mina's Journal", "Mina’s Journal", Tools.GetEntity(Journal).Entity!.Hash!, keepOldName: true);

        Assert.True(result.Changed);
        Assert.Equal(before, _den.Read(Note));
        Assert.Equal([Journal], result.Files!.Select(f => f.Path));
        var journal = _den.Read(Journal);
        Assert.Contains("name: \"Mina’s Journal\"", journal, StringComparison.Ordinal);
        Assert.Contains("\n# Mina’s Journal\n", journal, StringComparison.Ordinal);

        // Kept as an alias, it would only be the same text for links twice.
        Assert.Contains("aliases: [\"Mina's Diary\"]\n", journal, StringComparison.Ordinal);
        Assert.Contains(result.Effects, e => e.Contains("which apostrophe is used", StringComparison.Ordinal) && e.Contains("none were changed", StringComparison.Ordinal));
        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule is "F-LINK-7" or "F-LINK-8");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void ChangingOnlyAnApostropheInAnAliasChangesNoLink()
    {
        WriteNotes();
        var before = _den.Read(Note);

        // The alias is named with another apostrophe than it's written with.
        var result = Tools.ChangeAlias("Mina's Journal", "Mina’s Diary", Tools.GetEntity(Journal).Entity!.Hash!, newAlias: "Mina’s Diary");

        Assert.True(result.Changed);
        Assert.Equal(before, _den.Read(Note));
        Assert.Contains("aliases: [\"Mina’s Diary\"]", _den.Read(Journal), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void ARenameRewritesEveryApostropheOfTheOldNameToTheNewNameAsGiven()
    {
        WriteNotes();

        var result = Tools.RenameEntity("Mina's Journal", "Mina's Letters", Tools.GetEntity(Journal).Entity!.Hash!);

        // A label stays as the writer typed it, and so do links by another route.
        Assert.Contains(
            "[[Mina's Letters]], [[Mina's Letters]], [[Mina's Letters|her words’ home]], [[Mina's Letters#Mina’s Notes]], [[mina’s diary]], and [[minas-journal]].\n",
            _den.Read(Note),
            StringComparison.Ordinal);
        Assert.Contains(result.Effects, e => e.Contains("rewrote 4 link(s)", StringComparison.Ordinal));
        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule is "F-LINK-7" or "F-LINK-8" or "F-LINK-9");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void ChangingAnAliasRewritesLinksWithAnyApostrophe()
    {
        WriteNotes();

        Tools.ChangeAlias("Mina's Journal", "Mina's Diary", Tools.GetEntity(Journal).Entity!.Hash!, newAlias: "The Diary");

        Assert.Contains("[[The Diary]], and [[minas-journal]].\n", _den.Read(Note), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void ChangingOnlyAnApostropheInASectionChangesNoLink()
    {
        WriteNotes();
        var before = _den.Read(Note);

        var result = Tools.RenameSection(Journal, "Mina's Notes", "Mina’s Notes", Tools.GetEntity(Journal).Entity!.Hash!);

        Assert.Equal(before, _den.Read(Note));
        Assert.Equal([Journal], result.Files!.Select(f => f.Path));
        Assert.Contains("\n## Mina’s Notes\n", _den.Read(Journal), StringComparison.Ordinal);
        Assert.Contains(result.Effects, e => e.Contains("Links match any apostrophe", StringComparison.Ordinal));
        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule == "F-LINK-9");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void ChangingOnlyAnApostropheInASectionIsntStoppedByALinkInAFinalBook()
    {
        WriteNotes();
        const string Memories = "universes/dracula/books/aftermath/entities/notes/memories.md";
        _den.Write("universes/dracula/books/aftermath/book.md", "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z4\nname: Aftermath\nstatus: final\n---\n");
        _den.Write(Memories, "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z5\ntype: note\nname: Memories\nstatus: draft\n---\n\nRecall [[minas-journal#Mina's Notes]].\n");
        var before = _den.Read(Memories);

        var result = Tools.RenameSection(Journal, "Mina's Notes", "Mina’s Notes", Tools.GetEntity(Journal).Entity!.Hash!);

        Assert.True(result.Changed);
        Assert.Equal([Journal], result.Files!.Select(f => f.Path));
        Assert.Equal(before, _den.Read(Memories));
        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule == "F-LINK-9");

        // A real change has to rewrite it, so the final book stops that, whichever apostrophe the section now uses.
        var refused = Assert.Throws<DenToolException>(() => Tools.RenameSection(Journal, "Mina’s Notes", "Shorthand", result.Hash));
        Assert.Equal(ToolErrorCodes.Frozen, refused.Code);
        Assert.Contains(Memories, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void MovingANoteLeavesALinkWithAnotherApostropheAsItIs()
    {
        WriteNotes();
        var before = _den.Read(Note);

        var result = Tools.MoveEntity("Mina's Journal", Tools.GetEntity(Journal).Entity!.Hash!, toBook: "dracula", keepEntry: true);

        Assert.True(result.Changed);
        Assert.Equal(before, _den.Read(Note));
        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule is "F-LINK-7" or "F-LINK-8" or "F-LINK-9");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void MovingANoteBesideOneThatDiffersOnlyByApostropheSendsItsLinksToItsId()
    {
        WriteNotes();
        _den.Write(Twin, "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z3\ntype: note\nname: \"Mina’s Journal\"\nstatus: draft\n---\n\n# Mina’s Journal\n");

        var result = Tools.MoveEntity(Journal, Tools.GetEntity(Journal).Entity!.Hash!, toBook: "dracula", keepEntry: true);

        Assert.True(result.Changed);
        Assert.Contains("[[01M4TF5F0EKGGWD5FNVBAEY8Z2|Mina’s Journal]]", _den.Read(Note), StringComparison.Ordinal);
        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Path == Note && d.Rule is "F-LINK-7" or "F-LINK-8");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void MovingANoteThatWouldMakeALinkToOneDifferingOnlyByApostropheAmbiguousIsRefused()
    {
        WriteNotes();
        _den.Write(Twin, "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z3\ntype: note\nname: \"Mina’s Journal\"\nstatus: draft\n---\n\n# Mina’s Journal\n\nSee [[Mina's Journal]].\n");
        var before = _den.Read(Journal);

        var refused = Assert.Throws<DenToolException>(
            () => Tools.MoveEntity(Journal, Tools.GetEntity(Journal).Entity!.Hash!, toBook: "dracula", keepEntry: true));

        Assert.Contains("minas-journal.md", refused.Message, StringComparison.Ordinal);
        Assert.Contains("mina-curly.md", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, _den.Read(Journal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void RenamingASectionRewritesItsLinksWithAnyApostrophe()
    {
        WriteNotes();

        var result = Tools.RenameSection(Journal, "Mina's Notes", "Shorthand", Tools.GetEntity(Journal).Entity!.Hash!);

        Assert.Contains("[[Mina’s Journal#Shorthand]]", _den.Read(Note), StringComparison.Ordinal);
        Assert.Contains(result.Effects, e => e.Contains("rewrote 1 link(s)", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-46")]
    public void ChangingOnlyAnApostropheIsntStoppedByALinkInAFinalBook()
    {
        // A link in a final book would stop a rewrite, but this rename rewrites nothing.
        WriteNotes();
        const string Memories = "universes/dracula/books/aftermath/entities/notes/memories.md";
        _den.Write("universes/dracula/books/aftermath/book.md", "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z4\nname: Aftermath\nstatus: final\n---\n");
        _den.Write(Memories, "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z5\ntype: note\nname: Memories\nstatus: draft\n---\n\nRecall [[Mina’s Journal]].\n");
        var before = _den.Read(Memories);
        var hash = Tools.GetEntity(Journal).Entity!.Hash!;

        var result = Tools.RenameEntity("Mina's Journal", "Mina’s Journal", hash);

        Assert.True(result.Changed);
        Assert.Equal(before, _den.Read(Memories));

        // A real change has to rewrite it, so the final book stops that, whichever apostrophe the link uses.
        var refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mina’s Journal", "Mina's Letters", result.Hash));
        Assert.Equal(ToolErrorCodes.Frozen, refused.Code);
    }
}

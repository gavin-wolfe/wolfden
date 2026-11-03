using System.Text;
using Wolfden.Core.Checks;
using Wolfden.Core.Documents;
using Wolfden.Core.Editing;
using Wolfden.Core.Entities;
using Wolfden.Core.Links;
using Wolfden.Core.Structure;

namespace Wolfden.Core.Tests;

/// <summary>
/// format.md F-GEN-2 and F-SEC-10 (#50): a bad value in a well-formed section
/// comment leaves out that section and its subsections, not the file or
/// container, and it's still an error. A save is refused only if it adds one.
/// </summary>
public class SectionErrorTests
{
    private const string Book = "universes/vael/books/a1";
    private const string OrrinPath = Book + "/entities/characters/orrin-fell.md";
    private const string Front = "---\nid: 01JC5CK1000000000000000005\ntype: character\nname: Orrin Fell\nstatus: canon\n---\n\n# Orrin Fell\n\n";

    private static MemoryDenSource Den(string body = "## Overview\nA deckhand.\n") => new MemoryDenSource()
        .With("den.yaml", "format: 1\nspoiler_checks: true\n")
        .With("universes/vael/universe.md", "---\nid: 01JC5CK1000000000000000001\nname: Vael\norder: [a1, a2]\n---\n")
        .With(Book + "/book.md", "---\nid: 01JC5CK1000000000000000003\nname: Book 1\nchapters: [1, 2, 3]\n---\n")
        .With("universes/vael/books/a2/book.md", "---\nid: 01JC5CK1000000000000000004\nname: Book 2\nchapters: [1]\n---\n")
        .With(OrrinPath, Front + body);

    private static (string Rule, DiagnosticSeverity Severity, int? Line) Error(DenCheck check) =>
        check.Diagnostics.Where(d => d.Diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(d => (d.Diagnostic.RuleId, d.Diagnostic.Severity, d.Diagnostic.Line)).Single();

    private static EntityFacts Facts(IDenSource den, string path = OrrinPath) =>
        EntityFacts.For(EntityCatalog.Load(den).FindByPath(path)!, new Bookkeeping());

    private static SectionFact Fact(EntityFacts facts, string name) => facts.Sections.Single(s => s.Section.Name == name);

    [Theory]
    [Trait("Scenario", "SC-CHECK-59")]
    [InlineData("status=maybe", "F-STAT-1", "status", "maybe")]
    [InlineData("from=a1:2.1", "F-POS-2", "from", "a1:2.1")]
    [InlineData("until=A1:2", "F-POS-3", "until", "A1:2")]
    [InlineData("pin=yes", "F-PIN-1", "pin", "yes")]
    [InlineData("canon=x", "F-SEC-7", "canon", "x")]
    [InlineData("from=a1:1 from=a1:2", "F-SEC-7", "from", "a1:2")]
    public void ABadValueLeavesOutThatSectionAndTheNoteStays(string attributes, string rule, string attribute, string value)
    {
        var den = Den($"## Overview\nA deckhand.\n\n## Secret\n<!-- wolfden: {attributes} -->\nHe's the heir.\n\n## After\nMore.\n");

        var check = DenCheck.Run(den);

        // Still an error, so check fails. The line is the comment's.
        Assert.Equal((rule, DiagnosticSeverity.Error, (int?)14), Error(check));
        Assert.True(check.ErrorCount == 1);
        var error = check.Diagnostics.Single(d => d.Diagnostic.Severity == DiagnosticSeverity.Error).Diagnostic;
        Assert.Equal(new LeftOutSection("Secret", 13, attribute, value), error.LeftOut);
        Assert.EndsWith("'Secret' is left out until this is fixed.", error.Message, StringComparison.Ordinal);

        // Everything else is indexed as usual.
        var entity = Assert.Single(check.Catalog.Entities);
        var facts = EntityFacts.For(entity, new Bookkeeping());
        Assert.True(Fact(facts, "Overview").IsLive);
        Assert.True(Fact(facts, "After").IsLive);
        var secret = Fact(facts, "Secret");
        Assert.True(secret.IsLeftOut);
        Assert.False(secret.IsLive);
        Assert.False(secret.Pinned);
        Assert.Null(secret.EffectiveFrom);
        Assert.Null(secret.EffectiveUntil);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-59")]
    public void ItsSubsectionsGoWithItAndABadSubsectionTakesOnlyItself()
    {
        var facts = Facts(Den("""
            ## Arc
            <!-- wolfden: status=maybe -->
            Text.

            ### Early
            <!-- wolfden: from=a1:2 pin=true -->
            Text.

            #### Deep
            Text.

            ## Roots
            Text.

            ### Good
            Text.

            ### Bad
            <!-- wolfden: pin=yes -->
            Text.

            #### Under bad
            Text.

            ### Good too
            Text.

            """));

        Assert.Equal(
            ["Arc", "Early", "Deep", "Bad", "Under bad"],
            facts.Sections.Where(f => f.IsLeftOut).Select(f => f.Section.Name));
        Assert.False(Fact(facts, "Early").Pinned);
        Assert.Null(Fact(facts, "Early").EffectiveFrom);
        Assert.True(Fact(facts, "Good").IsLive);
        Assert.True(Fact(facts, "Good too").IsLive);

        // A subsection says why by its holder's error.
        var why = Assert.Single(Fact(facts, "Deep").Section.LeftOutBecause);
        Assert.Equal("F-STAT-1", why.RuleId);
        Assert.Same(why, Assert.Single(Fact(facts, "Arc").Section.Problems));
        Assert.Empty(Fact(facts, "Deep").Section.Problems);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-59")]
    public void AFrontmatterErrorStillExcludesTheFileEvenWithABadSection()
    {
        var den = Den().With(OrrinPath, "---\nid: 01JC5CK1000000000000000005\ntype: character\nname: Orrin Fell\nstatus: maybe\n---\n\n## Secret\n<!-- wolfden: pin=yes -->\nText.\n");

        var check = DenCheck.Run(den);

        Assert.Empty(check.Catalog.Entities);
        Assert.Equal(["F-ENT-5", "F-PIN-1"], check.Diagnostics.Where(d => d.Diagnostic.Severity == DiagnosticSeverity.Error).Select(d => d.Diagnostic.RuleId).Order(StringComparer.Ordinal));

        // The whole note is out, so the comment's error doesn't say a section is.
        var comment = check.Diagnostics.Single(d => d.Diagnostic.RuleId == "F-PIN-1").Diagnostic;
        Assert.Equal("pin must be true or false, not 'yes'.", comment.Message);
        Assert.Null(comment.LeavesOutOnly);
        Assert.True(comment.LeftOut!.FileLeftOutToo);
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-60")]
    [InlineData("universes/vael/universe.md", "---\nid: 01JC5CK1000000000000000001\nname: Vael\norder: [a1, a2]\n---\n")]
    [InlineData(Book + "/book.md", "---\nid: 01JC5CK1000000000000000003\nname: Book 1\nchapters: [1, 2, 3]\n---\n")]
    public void ABadSectionInAContainerFileKeepsTheContainer(string path, string front)
    {
        var den = Den().With(path, front + "\n## Plan\n<!-- wolfden: status=maybe -->\nLater.\n");

        var check = DenCheck.Run(den);

        var error = check.Diagnostics.Single(d => d.Diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(path, error.Path);
        Assert.Equal("Plan", error.Diagnostic.LeftOut!.Name);
        Assert.Equal(1, check.ErrorCount);
        Assert.Single(check.Catalog.Entities);
        Assert.Equal(3, check.Structure.FindUniverse("vael")!.FindBook("a1")!.Chapters.Count);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-60")]
    public void ABadSectionInASeriesFileKeepsTheSeriesAndAFrontmatterErrorDoesnt()
    {
        const string Series = "universes/vael/series/s/series.md";
        MemoryDenSource With(string series) => new MemoryDenSource()
            .With("den.yaml", "format: 1\n")
            .With("universes/vael/universe.md", "---\nid: 01JC5CK1000000000000000001\nname: Vael\norder: [s]\n---\n")
            .With(Series, series)
            .With("universes/vael/series/s/books/b/book.md", "---\nid: 01JC5CK1000000000000000006\nname: B\nchapters: [1]\n---\n");

        var kept = DenStructure.Load(With("---\nid: 01JC5CK1000000000000000002\nname: S\nbooks: [b]\n---\n\n## Plan\n<!-- wolfden: from=b:1.2 -->\nLater.\n"));
        Assert.NotNull(kept.FindUniverse("vael")!.FindBook("b"));

        var gone = DenStructure.Load(With("---\nid: 01JC5CK1000000000000000002\nbooks: [b]\n---\n"));
        Assert.Null(gone.FindUniverse("vael")!.FindBook("b"));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-60")]
    public void AContainerLeftOutWholeDoesntSayASectionIs()
    {
        var den = Den().With(Book + "/book.md", "---\nid: 01JC5CK1000000000000000003\nchapters: [1]\n---\n\n## Plan\n<!-- wolfden: status=maybe -->\nLater.\n");

        var structure = DenStructure.Load(den);

        Assert.Null(structure.FindUniverse("vael")!.FindBook("a1"));
        var comment = structure.Diagnostics.Single(d => d.Diagnostic.RuleId == "F-STAT-1").Diagnostic;
        Assert.Null(comment.LeavesOutOnly);
        Assert.DoesNotContain("left out", comment.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CANON-11")]
    public void AnEarlierStandardSectionDoesntHandOverToALeftOutOne()
    {
        var facts = Facts(Den("""
            ## Appearance
            Silver hair.

            ## Appearance
            <!-- wolfden: from=a1:2 status=caonn -->
            A brace on his arm.

            ## Appearance
            <!-- wolfden: from=a1:3 -->
            The brace is gone.

            """));

        var (first, typo, last) = (facts.Sections[0], facts.Sections[1], facts.Sections[2]);
        Assert.False(typo.IsLive);

        // The first runs on past the typo's a1:2, to the live one at a1:3.
        Assert.Equal(last.EffectiveFrom!.Point, first.EffectiveUntil);
    }

    [Fact]
    [Trait("Scenario", "SC-CANON-11")]
    public void WithOnlyTheLeftOutOneLeftTheEarlierRunsToTheEndOfTheStory()
    {
        var den = Den("## Appearance\nSilver hair.\n\n## Appearance\n<!-- wolfden: from=a1:2.1 -->\nA brace.\n");

        var facts = Facts(den);

        Assert.Equal(EntityCatalog.Load(den).Structure.FindUniverse("vael")!.EndOfStory, facts.Sections[0].EffectiveUntil);
    }

    [Fact]
    public void ALeftOutSectionDoesntCountForAnOverlap()
    {
        // Both would start with the entity, which would be F-SEC-4.
        var check = DenCheck.Run(Den("## Appearance\nSilver hair.\n\n## Appearance\n<!-- wolfden: pin=yes -->\nA brace.\n"));

        Assert.DoesNotContain(check.Diagnostics, d => d.Diagnostic.RuleId == "F-SEC-4");
    }

    [Fact]
    public void ARequiredSectionWhoseOnlyInstanceIsLeftOutGetsNoSecondMessage()
    {
        var den = new MemoryDenSource()
            .With("den.yaml", "format: 1\npacks: [kit]\n")
            .With("packs/kit/pack.yaml", "name: Kit\ndescription: Invented.\n")
            .With("packs/kit/types/ship.yaml", "name: Ship\nplural: ships\nsections:\n  - { name: Crew, required: true }\n")
            .With("universes/vael/universe.md", "---\nid: 01JC5CK1000000000000000001\nname: Vael\norder: [a1]\n---\n")
            .With(Book + "/book.md", "---\nid: 01JC5CK1000000000000000003\nname: Book 1\nchapters: [1]\n---\n")
            .With(Book + "/entities/ships/kestrel.md", "---\nid: 01JC5CK1000000000000000005\ntype: ship\nname: Kestrel\nstatus: canon\n---\n\n## Crew\n<!-- wolfden: status=maybe -->\nForty.\n");

        var check = DenCheck.Run(den);

        Assert.Equal("F-STAT-1", Assert.Single(check.Diagnostics).Diagnostic.RuleId);
    }

    [Fact]
    public void OtherChecksOnItsCommentWaitUntilTheErrorIsFixed()
    {
        var den = Den("""
            ## Roots
            <!-- wolfden: until=a1:2 -->
            Text.

            ### Later
            <!-- wolfden: from=a1:3 status=maybe -->
            Text.

            ## Idea
            <!-- wolfden: status=idea pin=yes from=a1:3 until=a1:2 -->
            Text.

            """);

        var check = DenCheck.Run(den);

        // Not F-SEC-8, F-SEC-9, or F-PIN-3: what's wrong may be what they'd complain about.
        Assert.Equal(
            ["F-PIN-1", "F-STAT-1"],
            check.Diagnostics.Select(d => d.Diagnostic.RuleId).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-61")]
    public void LinksInALeftOutSectionAreCheckedOnlyForWhatDoesntDependOnPositionOrStatus()
    {
        var den = Den("""
            ## Secret
            <!-- wolfden: status=maybe from=a1:1 -->
            Knows [[Nobody Here]], [[the-stranger]], and [[the-notion]], and sees [[orrin-fell#Secret]].

            """)
            .With("universes/vael/books/a2/entities/characters/the-stranger.md", "---\nid: 01JC5CK1000000000000000008\ntype: character\nname: The Stranger\nstatus: canon\n---\n")
            .With(Book + "/entities/notes/the-notion.md", "---\nid: 01JC5CK1000000000000000009\ntype: note\nname: The notion\nstatus: idea\n---\n");

        var check = DenCheck.Run(den);

        // A broken link is reported. No F-LINK-12 (an idea), no F-LINK-13 (a later book), and no
        // F-LINK-9: the section it names is there.
        Assert.Equal(
            ["F-LINK-8", "F-STAT-1"],
            check.Diagnostics.Where(d => d.Path == OrrinPath).Select(d => d.Diagnostic.RuleId).Order(StringComparer.Ordinal));

        // They still resolve, so they count as links, with no position.
        var link = LinkIndex.Load(den).Links.Single(l => l.Occurrence.Link.Target == "the-stranger");
        Assert.NotNull(link.Target);
        Assert.True(link.Occurrence.IsLeftOut);
        Assert.Empty(link.Occurrence.Positions);
        Assert.False(link.Occurrence.IsInCanonFact);
    }

    [Fact]
    [Trait("Scenario", "SC-RT-18")]
    public void AFileWithABadSectionRoundTripsExactly()
    {
        var text = Front + "## Overview\r\nA deckhand.\r\n\r\n## Secret\r\n<!--  wolfden:  status=maybe   pin=yes -->\r\nHe's the heir.   \r\n";
        var bytes = Encoding.UTF8.GetBytes(text);

        var file = MarkdownFile.Parse(bytes, requireFrontmatter: true);

        Assert.Equal(bytes, file.ToBytes());
        Assert.Equal(2, file.Sections[1].Problems.Count);

        // The bad comment is kept exactly, and is still there to fix.
        Assert.Equal("<!--  wolfden:  status=maybe   pin=yes -->", file.Sections[1].Comment!.Text);
        Assert.Equal("He's the heir.   \r\n", file.Sections[1].Body);
    }

    [Fact]
    [Trait("Scenario", "SC-RT-18")]
    public void AnEditElsewhereChangesOnlyThatTextAndKeepsTheBadComment()
    {
        var text = Front + "## Overview\nA deckhand.\n\n## Secret\n<!--  wolfden:  status=maybe   pin=yes -->\nHe's the heir.   \n";
        var file = MarkdownFile.Parse(Encoding.UTF8.GetBytes(text), requireFrontmatter: true);

        var edited = file.WithSectionBody(file.Sections[0], "A sailor.\n");

        Assert.Equal(Encoding.UTF8.GetBytes(text.Replace("A deckhand.", "A sailor.", StringComparison.Ordinal)), edited.ToBytes());
    }

    [Fact]
    public void ATemplateKeepsItsOwnWording()
    {
        var den = DenStructure.Load(new MemoryDenSource()
            .With("den.yaml", "format: 1\npacks: [kit]\n")
            .With("packs/kit/pack.yaml", "name: Kit\ndescription: Invented.\n")
            .With("packs/kit/types/ship.yaml", "name: Ship\nplural: ships\nsections: [Crew]\n")
            .With("packs/kit/templates/ship.md", "# {{name}}\n\n## Crew\n<!-- wolfden: status=maybe -->\n"));

        var error = den.Types.Diagnostics.Single(d => d.Diagnostic.RuleId == "F-STAT-1").Diagnostic;

        // A template isn't a note: it's replaced by the generated one, and no section of a den is left out.
        Assert.Equal("'maybe' isn't a status. Use idea, undecided, candidate, draft, canon, or retconned.", error.Message);
        Assert.Null(error.LeftOut);
        Assert.True(den.Types.Find("ship")!.Template.IsGenerated);
    }

    // ----- Saves (format.md §1.3) -----

    private static DenWriteCheckResult Save(IDenSource den, string text) => Save(den, OrrinPath, text);

    private static DenWriteCheckResult Save(IDenSource den, string path, string text, IReadOnlyDictionary<string, string>? movedFrom = null) =>
        DenWriteCheck.Check(den, DenStructure.Load(den), new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = Encoding.UTF8.GetBytes(text) }, movedFrom);

    private const string WithTypo = "## Overview\nA deckhand.\n\n## Secret\n<!-- wolfden: status=maybe -->\nHe's the heir.\n";

    [Fact]
    [Trait("Scenario", "SC-CHECK-62")]
    public void AnEditElsewhereKeepsAnErrorThatWasThereAndIsAllowed()
    {
        var den = Den(WithTypo);

        var check = Save(den, Front + WithTypo.Replace("A deckhand.", "A deckhand on the Kestrel.", StringComparison.Ordinal));

        Assert.True(check.HasErrors);
        Assert.False(check.AddsErrors);
        Assert.Equal("F-STAT-1", Assert.Single(check.KeptErrors).Diagnostic.RuleId);
        Assert.Empty(check.AddedErrors);
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-62")]
    [InlineData("## Added\nNew text.\n\n" + WithTypo)] // the lines shift
    [InlineData("## Secret\n<!-- wolfden: status=maybe -->\nHe's the heir.\n\n## Overview\nA deckhand.\n")] // the sections swap
    [InlineData("## Overview\nA deckhand.\n\n## The secret\n<!-- wolfden: status=maybe -->\nHe's the heir.\n")] // it's renamed
    [InlineData("## Overview\nA deckhand.\n\n## Secret\n<!-- wolfden:   status=maybe -->\nHe's the heir.\n")] // the comment's spacing
    [InlineData("## Overview\nA deckhand.\n\n## Secret\n\n<!-- wolfden: status=\"maybe\" -->\nHe's the heir.\n")] // quoted, after a blank line
    public void TheSameErrorIsKnownWhereverItMoved(string body)
    {
        var check = Save(Den(WithTypo), Front + body);

        Assert.False(check.AddsErrors);
        Assert.Single(check.KeptErrors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-62")]
    public void FixingItLeavesNothing()
    {
        var check = Save(Den(WithTypo), Front + WithTypo.Replace("maybe", "candidate", StringComparison.Ordinal));

        Assert.False(check.HasErrors);
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-62")]
    [InlineData("## Overview\nA deckhand.\n\n## Secret\n<!-- wolfden: status=perhaps -->\nHe's the heir.\n", "F-STAT-1")] // another bad value
    [InlineData(WithTypo + "\n## Another\n<!-- wolfden: status=maybe -->\nText.\n", "F-STAT-1")] // the same mistake again
    [InlineData(WithTypo + "\n## Another\n<!-- wolfden: pin=yes -->\nText.\n", "F-PIN-1")] // another kind
    [InlineData("## Overview\nA deckhand.\n\n## Secret\n<!-- wolfden: status=maybe from=a1:2.1 -->\nHe's the heir.\n", "F-POS-2")] // a second one in its comment
    public void ANewBadValueIsRefused(string body, string rule)
    {
        var check = Save(Den(WithTypo), Front + body);

        Assert.True(check.AddsErrors);
        Assert.Equal(rule, Assert.Single(check.AddedErrors).Diagnostic.RuleId);
    }

    [Fact]
    public void TheCountIsExactWhenOneIsFixedAndAnotherTheSameIsAdded()
    {
        // One typo out, the same typo in somewhere else: there's no more of them than before.
        var check = Save(
            Den(WithTypo),
            Front + "## Overview\n<!-- wolfden: status=maybe -->\nA deckhand.\n\n## Secret\nHe's the heir.\n");

        Assert.False(check.AddsErrors);
    }

    [Fact]
    public void AFrontmatterErrorIsStillRefusedEvenWithABadSectionThatWasThere()
    {
        var bad = Front.Replace("status: canon", "status: maybe", StringComparison.Ordinal) + WithTypo;

        var check = Save(Den(WithTypo), bad);

        Assert.True(check.AddsErrors);
        Assert.Equal("F-ENT-5", Assert.Single(check.AddedErrors).Diagnostic.RuleId);
    }

    [Fact]
    public void ANewFileHasNothingToKeep()
    {
        const string Path = Book + "/entities/characters/new-hand.md";
        var check = Save(Den(), Path, Front + WithTypo);

        Assert.True(check.AddsErrors);
    }

    [Fact]
    public void AMovedFileKeepsWhatItHad()
    {
        const string Path = "universes/vael/entities/characters/orrin-fell.md";
        var check = Save(Den(WithTypo), Path, Front + WithTypo, new Dictionary<string, string>(StringComparer.Ordinal) { [Path] = OrrinPath });

        Assert.False(check.AddsErrors);
        Assert.Single(check.KeptErrors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-62")]
    public void AContainerFileKeepsWhatItHadToo()
    {
        const string Path = Book + "/book.md";
        const string Text = "---\nid: 01JC5CK1000000000000000003\nname: Book 1\nchapters: [1, 2, 3]\n---\n\n## Plan\n<!-- wolfden: status=maybe -->\nLater.\n";
        var den = Den().With(Path, Text);

        var kept = Save(den, Path, Text.Replace("Later.", "Much later.", StringComparison.Ordinal));
        Assert.False(kept.AddsErrors);
        Assert.Single(kept.KeptErrors);

        var added = Save(den, Path, Text + "\n## More\n<!-- wolfden: canon=x -->\nText.\n");
        Assert.Equal("F-SEC-7", Assert.Single(added.AddedErrors).Diagnostic.RuleId);
        Assert.NotNull(added.Structure.FindUniverse("vael")!.FindBook("a1"));
    }
}

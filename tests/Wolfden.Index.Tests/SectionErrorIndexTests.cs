using Wolfden.Core.Structure;

namespace Wolfden.Index.Tests;

/// <summary>
/// format.md F-GEN-2 (#50) in the index: a section an error leaves out is in
/// no canon view at any position, and the rest of its file is. It can be found
/// only without a position, marked. <see cref="DenIndex.Errors"/> says what each
/// error leaves out.
/// </summary>
public sealed class SectionErrorIndexTests : IDisposable
{
    private const string Book = "universes/vael/books/a1";
    private const string Orrin = Book + "/entities/characters/orrin-fell.md";

    private readonly TempDen _den = TempDen.FromFiles(new Dictionary<string, string>
    {
        ["den.yaml"] = "format: 1\n",
        ["universes/vael/universe.md"] = "---\nid: 01JC5CK1000000000000000001\nname: Vael\norder: [a1, a2]\n---\n",
        [$"{Book}/book.md"] = "---\nid: 01JC5CK1000000000000000003\nname: Book 1\nchapters: [1, 2, 3]\n---\n\n## Plan\n<!-- wolfden: status=maybe -->\nA note about the plan.\n",
        ["universes/vael/books/a2/book.md"] = "---\nid: 01JC5CK1000000000000000004\nname: Book 2\nchapters: [1]\n---\n",
        [$"{Book}/entities/characters/torn.md"] = "---\nid: 01JC5CK1000000000000000006\ntype: character\nname: Torn\nstatus: maybe\n---\n",
        [Orrin] = """
            ---
            id: 01JC5CK1000000000000000005
            type: character
            name: Orrin Fell
            status: canon
            ---

            # Orrin Fell

            A deckhand.

            ## Appearance
            Silver hair.

            ## Appearance
            <!-- wolfden: from=a1:2 status=caonn -->
            A brace from the wreckage.

            ### The brace
            <!-- wolfden: from=a1:3 pin=true -->
            Forged by quorlax smiths.

            ## Heirloom
            <!-- wolfden: pin=yes -->
            The signet of Vael.

            ## Overview
            <!-- wolfden: pin=true -->
            Sails the coast.

            """,
    });

    public void Dispose() => _den.Dispose();

    [Fact]
    [Trait("Scenario", "SC-WEB-90")]
    public void AnUnchangedDenIsNotReadAgainAndAChangedOneIs()
    {
        var watch = new DenErrorWatch(_den.Root, TimeSpan.Zero, null);
        var first = watch.Current();
        var rebuilds = 0;
        void Count(string root)
        {
            if (root == _den.Root)
            {
                rebuilds++;
            }
        }

        IndexBuilder.Rebuilding += Count;
        try
        {
            Assert.Same(first, watch.Current());
            Assert.Same(first, watch.Current());
            Assert.Equal(1, watch.Reads);
            Assert.Equal(0, rebuilds);

            _den.Write(Book + "/entities/characters/torn.md", "---\nid: 01JC5CK1000000000000000006\ntype: character\nname: Torn\nstatus: canon\n---\n");

            var next = watch.Current();
            Assert.Equal(2, watch.Reads);
            Assert.Equal(first.Errors.Count - 1, next.Errors.Count);
        }
        finally
        {
            IndexBuilder.Rebuilding -= Count;
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-90")]
    public void TheListHasEveryErrorCheckHas()
    {
        _den.Write(Book + "/outline.md", "---\nkey: [unclosed\n---\n\n# Outline\n");
        using var index = Index();

        var listed = index.Errors().Select(e => (e.Path, e.Line, e.Rule, e.Message)).Order().ToList();

        var check = Wolfden.Core.Checks.DenCheck.Run(_den.Root).Diagnostics
            .Where(d => d.Diagnostic.Severity == Wolfden.Core.Documents.DiagnosticSeverity.Error)
            .Select(d => (d.Path, d.Diagnostic.Line, d.Diagnostic.RuleId, d.Diagnostic.Message)).Order().ToList();
        Assert.Equal(check, listed);
        Assert.Contains(listed, e => e.Path == Book + "/outline.md");
        Assert.Equal(DenErrorReach.File, index.Errors().First(e => e.Path == Book + "/outline.md").Reach);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-90")]
    [InlineData("den.yaml", "den")]
    [InlineData("packs/mine/pack.yaml", "pack")]
    [InlineData("universes/vael/universe.md", "universe")]
    [InlineData("universes/vael/series/long/series.md", "series")]
    [InlineData("universes/vael/books/a1/book.md", "book")]
    [InlineData("universes/vael/series/long/books/a1/book.md", "book")]
    [InlineData("universes/vael/books/a1/outline.md", "outline")]
    [InlineData("universes/vael/entities/notes/x.md", "note")]
    [InlineData("universes/vael/series/long/entities/x.md", "note")]
    [InlineData("universes/vael/series/long/books/a1/entities/characters/x.md", "note")]
    [InlineData("universes/vael/entities/notes/book.md", "note")]
    [InlineData("universes/vael/books/a1", "folder")]
    [InlineData("universes/vael", "folder")]
    [InlineData("universes/vael/readme.md", "other")]
    [InlineData("universes/vael/books/a1/notes.md", "other")]
    [InlineData("notes.md", "other")]
    public void APathsKindComesFromWhereItIs(string path, string kind) => Assert.Equal(kind, DenErrors.KindOf(path));

    private DenIndex Index()
    {
        IndexBuilder.Rebuild(_den.Root);
        return DenIndex.Open(_den.Root);
    }

    [Theory]
    [Trait("Scenario", "SC-CANON-11")]
    [InlineData("a1")]
    [InlineData("a1:1")]
    [InlineData("a1:2")]
    [InlineData("a1:3")]
    [InlineData("a2")]
    [InlineData("a2:1")]
    [InlineData(null)]
    public void ItsInNoCanonViewAtAnyPositionAndTheEarlierSectionKeepsApplying(string? position)
    {
        using var index = Index();

        var view = index.CanonAsOf("vael", position);

        var entity = Assert.Single(view.Entities);
        Assert.Equal(["Appearance", "Overview"], entity.Sections.Select(s => s.Name));
        Assert.Equal("Silver hair.", entity.Sections[0].Body.Trim());
        Assert.Equal(["Overview"], Assert.Single(view.Pinned().Entities).Sections.Select(s => s.Name));
    }

    [Fact]
    public void TheRowsSayWhyAndHaveNoRange()
    {
        using var index = Index();

        var rows = index.Context.Sections.OrderBy(s => s.Index).ToList();

        Assert.Equal(["Appearance", "Appearance", "The brace", "Heirloom", "Overview"], rows.Select(s => s.Name));
        Assert.Equal([false, true, true, true, false], rows.Select(s => s.LeftOutJson is not null));
        foreach (var row in rows.Where(s => s.LeftOutJson is not null))
        {
            Assert.Null(row.FromOrdinal);
            Assert.Null(row.UntilOrdinal);
            Assert.False(row.Pinned);
        }

        // The subsection's is its parent's.
        Assert.Contains("F-STAT-1", rows[2].LeftOutJson, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CANON-11")]
    public void ItsSearchableMarkedAndNeverCountsAsCanon()
    {
        using var index = Index();

        var hit = Assert.Single(index.Search("quorlax"));

        Assert.Equal("The brace", hit.SectionName);
        Assert.True(hit.IsLeftOut);
        Assert.False(hit.IsInCanon);
        Assert.False(Assert.Single(index.Search("hair")).IsLeftOut);
        Assert.True(Assert.Single(index.Search("hair")).IsInCanon);
    }

    [Fact]
    public void ErrorsSayWhatEachLeavesOut()
    {
        using var index = Index();

        var errors = index.Errors();

        Assert.Equal(
            [
                new DenError($"{Book}/book.md", 8, "F-STAT-1", "'maybe' isn't a status. Use idea, undecided, candidate, draft, canon, or retconned. 'Plan' is left out until this is fixed.", DenErrorReach.Section, "Plan", 7),
            ],
            errors.Where(e => e.Path.EndsWith("book.md", StringComparison.Ordinal)));
        Assert.Equal(
            [("Appearance", 15, 16), ("Heirloom", 23, 24)],
            errors.Where(e => e.Path == Orrin).Select(e => (e.Section!, e.SectionLine!.Value, e.Line!.Value)));
        Assert.All(errors.Where(e => e.Path == Orrin), e => Assert.Equal(DenErrorReach.Section, e.Reach));
        var torn = errors.Single(e => e.Path.EndsWith("torn.md", StringComparison.Ordinal));
        Assert.Equal(DenErrorReach.File, torn.Reach);
        Assert.Null(torn.Section);
    }

    [Fact]
    public void AFileOrContainerLeftOutWholeReachesThatFarWhateverItsSectionsHave()
    {
        const string Torn = Book + "/entities/characters/torn.md";
        _den.Write(Torn, "---\nid: 01JC5CK1000000000000000006\ntype: character\nname: Torn\nstatus: maybe\n---\n\n## Secret\n<!-- wolfden: pin=yes -->\nText.\n");
        _den.Write("universes/vael/books/a2/book.md", "---\nid: 01JC5CK1000000000000000004\nchapters: [1]\n---\n\n## Plan\n<!-- wolfden: canon=x -->\nLater.\n");
        using var index = Index();

        var errors = index.Errors();

        var inNote = errors.Where(e => e.Path == Torn).ToList();
        Assert.Equal(["F-ENT-5", "F-PIN-1"], inNote.Select(e => e.Rule).Order(StringComparer.Ordinal));
        Assert.All(inNote, e =>
        {
            Assert.Equal(DenErrorReach.File, e.Reach);
            Assert.Null(e.Section);
            Assert.Null(e.SectionLine);
            Assert.DoesNotContain("left out", e.Message, StringComparison.Ordinal);
        });
        var inBook = errors.Where(e => e.Path == "universes/vael/books/a2/book.md").ToList();
        Assert.Equal(2, inBook.Count);
        Assert.All(inBook, e =>
        {
            Assert.Equal(DenErrorReach.Container, e.Reach);
            Assert.Null(e.Section);
        });

        // A note with only a bad section stays at section reach.
        Assert.All(errors.Where(e => e.Path == Orrin), e => Assert.Equal(DenErrorReach.Section, e.Reach));
    }

    [Theory]
    [InlineData("den.yaml", DenErrorReach.Den)]
    [InlineData("universes/vael/universe.md", DenErrorReach.Container)]
    [InlineData("universes/vael/series/s/series.md", DenErrorReach.Container)]
    [InlineData("universes/vael/books/a1/book.md", DenErrorReach.Container)]
    [InlineData("universes/vael/books/a9", DenErrorReach.Container)]
    [InlineData("universes/vael/books/a1/outline.md", DenErrorReach.File)]
    [InlineData("universes/vael/entities/notes/x.md", DenErrorReach.File)]
    [InlineData("packs/kit/types/ship.yaml", DenErrorReach.File)]
    public void AFileErrorsReachIsByWhereItIs(string path, DenErrorReach reach) =>
        Assert.Equal(reach, DenErrors.ReachOfFileError(path));

    [Fact]
    [Trait("Scenario", "SC-WATCH-9")]
    public void TheWatcherPicksUpABadSectionAndItsFixInPlace()
    {
        var updater = new IndexUpdater(_den.Root);
        updater.Rebuild("start");
        var text = File.ReadAllText(Path.Combine(_den.Root, Orrin));

        // Fixed: the note's section comes back.
        _den.Write(Orrin, text.Replace("status=caonn", "status=canon", StringComparison.Ordinal));
        Assert.NotNull(updater.Apply(IncrementalUpdateTests.Batch(_den, (WatcherChangeTypes.Changed, Orrin, null)), out _));
        IncrementalUpdateTests.AssertSameAsFullRebuild(_den, "after the fix");
        using (var index = DenIndex.Open(_den.Root))
        {
            Assert.Equal(["Appearance", "The brace", "Overview"], Assert.Single(index.CanonAsOf("vael", "a1:3").Entities).Sections.Select(s => s.Name));
        }

        // Broken again, in place.
        _den.Write(Orrin, text);
        Assert.NotNull(updater.Apply(IncrementalUpdateTests.Batch(_den, (WatcherChangeTypes.Changed, Orrin, null)), out _));
        IncrementalUpdateTests.AssertSameAsFullRebuild(_den, "after the typo");
        using (var index = DenIndex.Open(_den.Root))
        {
            Assert.Equal(["Appearance", "Overview"], Assert.Single(index.CanonAsOf("vael", "a1:3").Entities).Sections.Select(s => s.Name));
        }
    }
}

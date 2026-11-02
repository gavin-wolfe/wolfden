using Microsoft.EntityFrameworkCore;

namespace Wolfden.Index.Tests;

/// <summary>
/// Canon-as-of views of <c>samples/starfall</c> (#34): the reveal and the
/// retcon arrive where the sample tags them, facts change over time, and
/// what isn't decided stays out of canon. The positions are the ones the
/// docs use, such as <c>ember:14</c> and <c>ember:27</c>.
/// </summary>
public sealed class StarfallSampleCanonViewTests : IClassFixture<StarfallSampleCanonViewTests.Built>
{
    private readonly Built _built;

    public StarfallSampleCanonViewTests(Built built)
    {
        _built = built;
    }

    [Fact]
    [Trait("Scenario", "SC-CANON-10")]
    public void TheVelariAreExtinctAtEmberFourteen()
    {
        var view = View("ember:14");
        var velari = Entity("ember:14", "Velari");

        Assert.Equal("\"extinct\"", Field(velari, "population"));
        Assert.DoesNotContain(velari.Fields, f => f.Name == "homeworld");
        Assert.DoesNotContain(velari.Sections, s => s.Name == "Alive");
        Assert.DoesNotContain(view.Entities, e => e.Name == "Velari Prime");
        foreach (var term in new[] { "terrified", "cover story", "Commander", "flechette", "Greta" })
        {
            Assert.DoesNotContain(term, Text(view), StringComparison.Ordinal);
        }

        Assert.Contains("a plague killed the Velari", Text(Entity("ember:14", "The Velari plague")), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-CANON-10")]
    [InlineData("ember:27")]
    [InlineData("ember:30")]
    [InlineData("ash:4")]
    public void TheVelariAreAliveFromEmberTwentySeven(string position)
    {
        var velari = Entity(position, "Velari");

        Assert.Equal("\"alive, a few thousand in hiding\"", Field(velari, "population"));
        Assert.Contains(velari.Sections, s => s.Name == "Alive");
        Assert.Contains("terrified", Text(velari), StringComparison.Ordinal);
        Assert.DoesNotContain("cover story", Text(View(position)), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-CANON-10")]
    [InlineData("ash:5")]
    [InlineData(null)]
    public void FromAshFiveThePlagueIsACoverStory(string? position)
    {
        var plague = Entity(position, "The Velari plague");

        Assert.Equal(["What really happened"], plague.Sections.Select(s => s.Name));
        Assert.Contains("There was no plague", plague.Sections[0].Body, StringComparison.Ordinal);
        Assert.True(plague.Sections[0].Pinned);
        Assert.Contains("cover story", Text(Entity(position, "Velari")), StringComparison.Ordinal);
        Assert.Contains(Entity(position, "Relay network").Sections, s => s.Name == "What the signal did");
    }

    [Theory]
    [InlineData("ember:21", "Lieutenant", false)]
    [InlineData("ember:22", "Commander", true)]
    [InlineData("ash:1", "Commander", true)]
    public void IlsesRankChangesAtEmberTwentyTwo(string position, string rank, bool commanderSection)
    {
        var ilse = Entity(position, "Ilse Tarrow");

        Assert.Equal($"\"{rank}\"", Field(ilse, "rank"));
        Assert.Equal(commanderSection, ilse.Sections.Any(s => s.Name == "Commander"));
        Assert.Equal("\"2291\"", Field(ilse, "born"));
        Assert.Contains("burn scar along her left forearm", Text(ilse), StringComparison.Ordinal);
    }

    [Fact]
    public void IlsesInjuryStartsAtEmberNineteen()
    {
        Assert.DoesNotContain(Entity("ember:18", "Ilse Tarrow").Sections, s => s.Name == "Shoulder injury");
        Assert.Contains("flechette round", Entity("ember:19", "Ilse Tarrow").Sections.Single(s => s.Name == "Shoulder injury").Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ember:1", "in service")]
    [InlineData("ember:6", "in service")]
    [InlineData("ember:7", "silent and presumed lost")]
    [InlineData("ash:2", "missing with one survivor reported")]
    public void TheTessalysConditionChanges(string position, string condition)
    {
        Assert.Equal($"\"{condition}\"", Field(Entity(position, "Tessaly"), "condition"));
    }

    [Fact]
    public void TheLanternPassesToIlseAtEmberEight()
    {
        Assert.DoesNotContain(View("ember:4").Entities, e => e.Name == "Lantern");
        Assert.Equal("\"[[joss-marrable]]\"", Field(Entity("ember:7", "Lantern"), "captain"));
        Assert.Equal("\"[[ilse-tarrow]]\"", Field(Entity("ember:8", "Lantern"), "captain"));
    }

    [Theory]
    [InlineData("ember:1")]
    [InlineData("ember:3")]
    public void NothingSaysIlseIsLeftBehindBeforeItHappens(string position)
    {
        var text = Text(View(position));

        Assert.DoesNotContain("left behind", text, StringComparison.Ordinal);
        Assert.DoesNotContain("goes silent", text, StringComparison.Ordinal);
    }

    [Fact]
    public void IlseIsLeftBehindAtEmberFour()
    {
        Assert.Contains("left her behind", Text(Entity("ember:4", "Halden Pike")), StringComparison.Ordinal);
        Assert.Contains("left behind", Text(Entity("ember:4", "Tessaly")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ember:5", "Captain of the Lantern", false, false)]
    [InlineData("ember:7", "Captain of the Lantern", true, false)]
    [InlineData("ember:8", "Former captain of the Lantern", true, true)]
    public void MarrablesRoleAndOverviewFollowTheStory(string position, string role, bool silent, bool handsOver)
    {
        var marrable = Entity(position, "Joss Marrable");

        Assert.Equal($"\"{role}\"", Field(marrable, "role"));
        Assert.Equal(silent, Text(marrable).Contains("goes silent", StringComparison.Ordinal));
        Assert.Equal(handsOver, marrable.Sections.Any(s => s.Name == "Handing over"));
    }

    [Fact]
    public void WhatIsntDecidedStaysOutOfCanon()
    {
        var view = View(null);

        // Candidate and idea notes, and undecided sections, are never canon.
        Assert.DoesNotContain(view.Entities, e => e.Type == "idea");
        Assert.DoesNotContain(view.Entities.Single(e => e.Name == "Who buried the records").Sections, s => s.HeadingPath[0] == "Payoff");
        Assert.DoesNotContain(view.Entities.Single(e => e.Name == "What the logs were cut off from saying").Sections, s => s.HeadingPath[0] == "Payoff");

        // Each is still a real note in the index, with the status it was given.
        var ideas = _built.Index.Context.Entities.Where(e => e.Type == "idea").ToList();
        Assert.Equal(3, ideas.Count(i => i.Status == "candidate"));
        Assert.Equal(2, ideas.Count(i => i.Status == "idea"));
    }

    [Fact]
    public void OutlineRowsKeepCountingAcrossBooksInTheIndex()
    {
        var context = _built.Index.Context;
        List<OutlineRowRecord> RowsOf(string book)
        {
            var file = context.Files.AsNoTracking().ToList().Single(f => f.Path.EndsWith($"/books/{book}/outline.md", StringComparison.Ordinal));
            return [.. context.OutlineRows.AsNoTracking().Where(r => r.FileId == file.Id)];
        }

        var ember = RowsOf("ember");
        var ash = RowsOf("ash");

        Assert.Equal(32, ember.Max(r => r.Number));
        Assert.Equal(33, ash.Min(r => r.Number));
        Assert.Equal("retconned", ash.Single(r => r.RowId == "36").Status);
        Assert.Equal(7, ash.Count(r => r.Status == "candidate"));
    }

    private CanonView View(string? position) => _built.Index.CanonAsOf("starfall", position);

    private CanonEntity Entity(string? position, string name) => View(position).Entities.Single(e => e.Name == name);

    private static string Field(CanonEntity entity, string name) => entity.Fields.Single(f => f.Name == name).ValueJson;

    private static string Text(CanonView view) => string.Join("\n", view.Entities.Select(Text));

    private static string Text(CanonEntity e) => string.Join("\n",
        new[] { e.Name, e.Title ?? "", e.Lead }
            .Concat(e.Fields.Select(f => f.ValueJson))
            .Concat(e.Sections.SelectMany(s => s.HeadingPath.Append(s.Body))));

    /// <summary>One index of a copy of the sample, shared by the tests in this class.</summary>
    public sealed class Built : IDisposable
    {
        private readonly TempDen _den = TempDen.StarfallSample();

        public Built()
        {
            Index = DenIndex.Open(_den.Root);
        }

        public DenIndex Index { get; }

        public void Dispose()
        {
            Index.Dispose();
            _den.Dispose();
        }
    }
}

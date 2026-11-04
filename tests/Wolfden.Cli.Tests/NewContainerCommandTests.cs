using System.Text.Json;

namespace Wolfden.Cli.Tests;

/// <summary>
/// <c>wolfden new series</c> and <c>wolfden new book</c> (#213): the
/// command line over the write tools' <c>CreateSeries</c> and
/// <c>CreateBook</c>, whose behaviour is tested in Wolfden.Mcp
/// (<c>CreateContainerTests</c>). Each test works on its own copy of
/// <c>samples/dracula</c>.
/// </summary>
public sealed class NewContainerCommandTests : IDisposable
{
    private const string Universe = "universes/dracula/universe.md";

    private readonly TempDen _den = TempDen.DraculaCopy();

    public void Dispose() => _den.Dispose();

    private (int ExitCode, string Output, string Error) New(params string[] args) =>
        Cli.Run(["new", "--den", _den.Root, .. args]);

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void NewSeriesAndBooksGoInStoryOrder()
    {
        // --in dracula is the universe, though it has a book 'dracula' too: a book holds no books.
        var (exitCode, output, error) = New("book", "Dracula's Guest", "--in", "dracula", "--chapters", "2");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Empty(error);
        const string Guest = "universes/dracula/books/draculas-guest/book.md";
        Assert.StartsWith($"Created {Guest}\n  Created {Guest}: book 'Dracula's Guest', key 'draculas-guest', with id ", output, StringComparison.Ordinal);
        Assert.Contains("It has 2 chapters, draculas-guest:1 to draculas-guest:2.", output, StringComparison.Ordinal);
        Assert.Contains("[dracula, draculas-guest]", output, StringComparison.Ordinal);
        Assert.Contains("\nchapters:\n  - { key: \"1\", number: 1 }\n  - { key: \"2\", number: 2 }\n---\n", _den.Read(Guest), StringComparison.Ordinal);
        Assert.Contains("\norder: [dracula, draculas-guest]\n", _den.Read(Universe).ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // A series after the novel, then a book in it, named by its bare key in a den with one universe.
        Assert.Equal(CliApp.ExitOk, New("series", "The Harker Papers", "--after", "dracula").ExitCode);
        Assert.Contains("\norder: [dracula, the-harker-papers, draculas-guest]\n", _den.Read(Universe).ReplaceLineEndings("\n"), StringComparison.Ordinal);
        var inSeries = New("book", "Mina's Journal", "--in", "the-harker-papers");
        Assert.Equal(CliApp.ExitOk, inSeries.ExitCode);
        Assert.StartsWith("Created universes/dracula/series/the-harker-papers/books/minas-journal/book.md\n", inSeries.Output, StringComparison.Ordinal);
        Assert.Equal(CliApp.ExitOk, New("book", "Jonathan's Journal", "--in", "dracula/the-harker-papers", "--after", "minas-journal").ExitCode);
        Assert.Contains(
            "\nbooks: [minas-journal, jonathans-journal]\n",
            _den.Read("universes/dracula/series/the-harker-papers/series.md").ReplaceLineEndings("\n"),
            StringComparison.Ordinal);

        // check finds nothing wrong with any of them.
        var (checkExit, check, _) = Cli.Run("check", _den.Root);
        Assert.Equal(CliApp.ExitOk, checkExit);
        Assert.DoesNotContain("universe.md", check, StringComparison.Ordinal);
        Assert.DoesNotContain("harker-papers", check, StringComparison.Ordinal);
        Assert.DoesNotContain("draculas-guest", check, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void ARefusedCreateExitsTwoAndWritesNothing()
    {
        var before = Cli.Snapshot(_den.Root);

        void Refused(string text, params string[] args)
        {
            var (exitCode, output, error) = New(args);
            Assert.Equal(CliApp.ExitUsage, exitCode);
            Assert.Empty(output);
            Assert.StartsWith("wolfden new: ", error, StringComparison.Ordinal);
            Assert.Contains(text, error, StringComparison.Ordinal);
        }

        Refused("give the book's name", "book");
        Refused("it's empty", "series", " ");
        Refused("already has a book 'dracula'", "book", "Dracula");
        Refused("has no series or standalone book 'ember'", "book", "Dracula's Guest", "--after", "ember");
        Refused("--chapters takes a whole number from 1 to 1000, not '0'", "book", "Dracula's Guest", "--chapters", "0");
        Refused("not 'two'", "book", "Dracula's Guest", "--chapters", "two");
        Refused("not '1001'", "book", "Dracula's Guest", "--chapters", "1001");
        Refused("--chapters is for a new book, not a series", "series", "The Harker Papers", "--chapters", "3");
        Refused("--from is for a note", "book", "Dracula's Guest", "--from", "dracula:1");
        Refused("--slug is for a note", "series", "The Harker Papers", "--slug", "papers");
        Refused("never changes which note a link reaches", "book", "Dracula's Guest", "--allow-relinks");
        Refused("--after is for a new series or book", "character", "Sister Agatha", "--after", "dracula");
        Refused("'dracula' is a book, and a book goes in a universe or a series", "book", "Dracula's Guest", "--in", "dracula/book:dracula");
        Refused("universe 'dracula' has no series 'sequels'", "book", "Dracula's Guest", "--in", "dracula/sequels");
        Refused("a series goes in a universe", "series", "The Harker Papers", "--in", "dracula/book:dracula");
        Refused("give --first or --after, not both", "book", "Dracula's Guest", "--first", "--after", "dracula");
        Refused("--first is for a new series or book", "character", "Sister Agatha", "--first");
        Refused("keys use only the letters a–z", "book", "Дракула");

        Assert.Equal(before, Cli.Snapshot(_den.Root));
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void AnInThatNamesNothingListsTheSeriesAndBooksThere()
    {
        Assert.Equal(CliApp.ExitOk, New("series", "The Long Dark").ExitCode);
        var before = Cli.Snapshot(_den.Root);

        // The Getting Started slip: the series' key is from its whole name.
        var (exitCode, output, error) = New("book", "Ember", "--in", "long-dark", "--chapters", "24");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Empty(output);
        Assert.Contains("--in 'long-dark' doesn't name one.", error, StringComparison.Ordinal);
        Assert.Contains("Universe 'dracula' has series: the-long-dark.", error, StringComparison.Ordinal);

        // A book can't hold a book, so the message offers none.
        Assert.Contains("'<universe>/<series>'", error, StringComparison.Ordinal);
        Assert.DoesNotContain("series or book", error, StringComparison.Ordinal);
        Assert.DoesNotContain("books:", error, StringComparison.Ordinal);

        // The key it lists works.
        Assert.Equal(before, Cli.Snapshot(_den.Root));
        Assert.Equal(CliApp.ExitOk, New("book", "Ember", "--in", "the-long-dark", "--chapters", "24").ExitCode);
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void WithSeveralUniversesTheListForANewBookNamesEachOnesSeriesAndNoBooks()
    {
        using var den = TempDen.Empty();
        den.Write("den.yaml", "format: 1\nname: Two\n");
        den.Write("universes/vael/universe.md", "---\nname: Vael\n---\n");
        den.Write("universes/vael/series/the-long-dark/series.md", "---\nname: The Long Dark\n---\n");
        den.Write("universes/vael/books/tide/book.md", "---\nname: Tide\n---\n");
        den.Write("universes/orra/universe.md", "---\nname: Orra\n---\n");

        var (exitCode, _, error) = Cli.Run(["new", "--den", den.Root, "book", "Ember", "--in", "long-dark"]);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("Universe 'vael' has series: the-long-dark.", error, StringComparison.Ordinal);
        Assert.Contains("Universe 'orra' has no series yet.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("tide", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void ANoteInASlashKeyThatNamesNothingListsTheSeriesAndBooks()
    {
        Assert.Equal(CliApp.ExitOk, New("series", "The Long Dark").ExitCode);

        // The issue's typo, for a note: it isn't just called a missing book.
        var (exitCode, _, error) = New("character", "Sister Agatha", "--in", "dracula/long-dark");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("universe 'dracula' has no series or book 'long-dark'.", error, StringComparison.Ordinal);
        Assert.Contains("Universe 'dracula' has series: the-long-dark; books: dracula.", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void AnUnknownUniverseBeforeASlashListsTheUniverses()
    {
        var (exitCode, _, error) = New("book", "Ember", "--in", "bogus/long-dark");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("the den has no universe 'bogus'", error, StringComparison.Ordinal);
        Assert.Contains("The den's universes: dracula.", error, StringComparison.Ordinal);
        Assert.Contains("for a standalone book, or '<universe>/<series>'.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("series or book", error, StringComparison.Ordinal);

        var forSeries = New("series", "The Harker Papers", "--in", "bogus/long-dark");
        Assert.Equal(CliApp.ExitUsage, forSeries.ExitCode);
        Assert.Contains("A series goes in a universe", forSeries.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("series or book", forSeries.Error, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-CLI-16")]
    [InlineData("dracula/series:nope", "universe 'dracula' has no series 'nope'.")]
    [InlineData("dracula/book:nope", "universe 'dracula' has no book 'nope'.")]
    public void AScopeNamedWithItsKindThatIsntThereSaysSoInTheSameWords(string scope, string words)
    {
        var (exitCode, _, error) = New("character", "Sister Agatha", "--in", scope);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("wolfden new: " + words, error, StringComparison.Ordinal);
        Assert.Contains("books: dracula.", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void ANewSeriesInADenWithNoUniversesSaysSo()
    {
        using var den = TempDen.Empty();
        den.Write("den.yaml", "format: 1\nname: Empty\n");

        var (exitCode, _, error) = Cli.Run(["new", "--den", den.Root, "series", "The Long Dark", "--in", "nowhere"]);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("The den has no universes yet.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("universes: .", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void ANewSeriesInSomethingElseNamesTheUniverses()
    {
        var (exitCode, _, error) = New("series", "The Harker Papers", "--in", "nowhere");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("A series goes in a universe", error, StringComparison.Ordinal);
        Assert.Contains("The den's universes: dracula.", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void FirstPutsItAtTheStart()
    {
        var (exitCode, output, _) = New("book", "Dracula's Guest", "--first");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Contains("at the start: [draculas-guest, dracula]", output, StringComparison.Ordinal);
        Assert.Contains("\norder: [draculas-guest, dracula]\n", _den.Read(Universe).ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void ABareBookKeyInADenWithSeveralUniversesSaysUniverseOrSeries()
    {
        _den.Write("universes/legend/universe.md", "---\nid: 01M4TF5F0EKGGWD5FNVBAEY9A1\nname: Legend\n---\n");
        _den.Write("universes/legend/books/ember/book.md", "---\nid: 01M4TF5F0EKGGWD5FNVBAEY9A2\nname: Ember\n---\n");
        var before = Cli.Snapshot(_den.Root);

        var (exitCode, _, error) = New("book", "Ash", "--in", "ember");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("'ember' is a book, and a book goes in a universe or a series.", error, StringComparison.Ordinal);
        Assert.Contains("Give --in '<universe>' for a standalone book, or '<universe>/<series>'.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("series or book", error, StringComparison.Ordinal);
        Assert.Equal(before, Cli.Snapshot(_den.Root));
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-16")]
    public void JsonPrintsWhatWasMade()
    {
        var (exitCode, output, _) = New("series", "The Harker Papers", "--json");

        Assert.Equal(CliApp.ExitOk, exitCode);
        using var json = JsonDocument.Parse(output);
        Assert.Equal("universes/dracula/series/the-harker-papers/series.md", json.RootElement.GetProperty("path").GetString());
        Assert.Equal("series", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("the-harker-papers", json.RootElement.GetProperty("key").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("files").GetArrayLength());
    }
}

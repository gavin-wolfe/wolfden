namespace Wolfden.Mcp.Tests;

/// <summary>
/// Creating series and books (#213; format.md §6, F-UNI-1..3, F-SER-1..3,
/// F-KEY-5): the writer's operation behind the web UI's New series and New
/// book and <c>wolfden new series</c> and <c>wolfden new book</c>, over
/// copies of the invented Starfall den and the Dracula sample.
/// </summary>
public sealed class CreateContainerTests : IDisposable
{
    private const string Universe = "universes/starfall/universe.md";
    private const string LongDark = "universes/starfall/series/long-dark/series.md";
    private const string DriftWar = "universes/starfall/series/drift-war/series.md";
    private const string DraculaUniverse = "universes/dracula/universe.md";
    private const string DraculaBook = "universes/dracula/books/dracula/book.md";

    private readonly List<TempDen> _dens = [];

    public void Dispose()
    {
        foreach (var den in _dens)
        {
            den.Dispose();
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-69")]
    public void ANewBookInASeriesGoesAtTheEndOfItsBooksWithItsChapters()
    {
        var den = Den(TempDen.Starfall());
        var tools = new DenTools(den.Root);
        var before = Diagnostics(tools);
        var universeBefore = den.Read(Universe);

        var made = tools.CreateBook("Cinder", "starfall", "long-dark", chapters: 3);

        const string Path = "universes/starfall/series/long-dark/books/cinder/book.md";
        Assert.Equal(Path, made.Path);
        Assert.Equal(("book", "cinder", "Cinder", "long-dark", 3), (made.Kind, made.Key, made.Name, made.Series, made.Chapters));
        Assert.Equal(
            $"---\nid: {made.Id}\nname: Cinder\nchapters:\n  - {{ key: \"1\", number: 1 }}\n  - {{ key: \"2\", number: 2 }}\n  - {{ key: \"3\", number: 3 }}\n---\n",
            den.Read(Path));
        Assert.Equal("---\nid: 01JA3E28JT97KB6CQ643DZVMXX\nname: The Long Dark\nbooks: [ember, ash, cinder]\n---\n", den.Read(LongDark).ReplaceLineEndings("\n"));
        Assert.Equal(universeBefore, den.Read(Universe));
        Assert.Equal([Path, LongDark], made.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
        Assert.True(made.Files.Single(f => f.Path == Path).Created);
        Assert.Contains(made.Effects, e => e.Contains("It has 3 chapters, cinder:1 to cinder:3.", StringComparison.Ordinal));
        Assert.Contains(made.Effects, e => e.Contains($"Added 'cinder' to books in {LongDark}, at the end.", StringComparison.Ordinal));
        Assert.Contains(made.Effects, e => e.Contains("ember, ash, cinder", StringComparison.Ordinal));
        Assert.Empty(made.Warnings);

        // The den reads it, and check finds nothing new.
        Assert.Empty(Diagnostics(tools).Except(before));
        Assert.Equal(["ember", "ash", "cinder"], tools.GetWorks("starfall", "long-dark").Works.Select(w => w.Key));
        Assert.Equal("cinder:3", tools.CanonAsOf("starfall", "cinder:3").Position);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-69")]
    public void PlaceAfterPutsTheNewWorkInItsPlaceAndKeepsTheRestOfTheFile()
    {
        var den = Den(TempDen.Starfall());
        var tools = new DenTools(den.Root);
        var before = Diagnostics(tools);

        var book = tools.CreateBook("Kindling", "starfall", "long-dark", after: "ember");
        Assert.Contains("books: [ember, kindling, ash]\n", den.Read(LongDark).ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Equal(0, book.Chapters);
        Assert.Equal($"---\nid: {book.Id}\nname: Kindling\n---\n", den.Read(book.Path));
        Assert.Contains(book.Effects, e => e.Contains("no chapters yet, so the only position in it is the bare 'kindling'", StringComparison.Ordinal));

        var series = tools.CreateSeries("The Far Shore", "starfall", after: "long-dark");
        Assert.Equal("universes/starfall/series/the-far-shore/series.md", series.Path);
        Assert.Equal($"---\nid: {series.Id}\nname: The Far Shore\n---\n", den.Read(series.Path));
        Assert.Equal(
            "---\nid: 01JAM9S346Q3D25VT4F5V37E3S\nname: Starfall\nauthors: [wren-calder]\norder: [long-dark, the-far-shore, a-quiet-moon, drift-war]\n---\n\nThe Kessan Reach and the long war over it.\n",
            den.Read(Universe).ReplaceLineEndings("\n"));
        Assert.Contains(series.Effects, e => e.Contains("It has no books yet.", StringComparison.Ordinal));

        // A standalone book, at the end of the universe's order.
        var standalone = tools.CreateBook("Salt and Iron", "starfall");
        Assert.Equal("universes/starfall/books/salt-and-iron/book.md", standalone.Path);
        Assert.Null(standalone.Series);
        Assert.Contains("order: [long-dark, the-far-shore, a-quiet-moon, drift-war, salt-and-iron]\n", den.Read(Universe).ReplaceLineEndings("\n"), StringComparison.Ordinal);

        Assert.Equal(
            ["long-dark", "the-far-shore", "a-quiet-moon", "drift-war", "salt-and-iron"],
            tools.GetWorks("starfall").Works.Select(w => w.Key));
        Assert.Empty(Diagnostics(tools).Except(before));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-69")]
    public void AnOmittedListIsWrittenWithTheWorkAlreadyThereFirst()
    {
        // Dracula's universe has one book and no order (F-UNI-3).
        var den = Den(TempDen.Dracula());
        var tools = new DenTools(den.Root);
        var before = Diagnostics(tools);
        var text = den.Read(DraculaUniverse).ReplaceLineEndings("\n");

        var made = tools.CreateBook("Dracula's Guest", "dracula", chapters: 1);

        Assert.Equal("universes/dracula/books/dracula-s-guest/book.md", made.Path);
        Assert.Equal(
            text.Replace("authors: [bram-stoker]\n", "authors: [bram-stoker]\norder: [dracula, dracula-s-guest]\n", StringComparison.Ordinal),
            den.Read(DraculaUniverse).ReplaceLineEndings("\n"));
        Assert.Contains(made.Effects, e => e.Contains("had no order list", StringComparison.Ordinal) && e.Contains("[dracula, dracula-s-guest]", StringComparison.Ordinal));
        Assert.Contains(made.Effects, e => e.Contains("It has 1 chapter, dracula-s-guest:1.", StringComparison.Ordinal));
        Assert.Empty(Diagnostics(tools).Except(before));

        // A series with no books needs no list for its first; its second writes one.
        var star = Den(TempDen.Starfall());
        var starTools = new DenTools(star.Root);
        var series = star.Read(DriftWar);
        var first = starTools.CreateBook("First Light", "starfall", "drift-war");
        Assert.Equal(series, star.Read(DriftWar));
        Assert.Equal([first.Path], first.Files.Select(f => f.Path));
        Assert.Contains(first.Effects, e => e.Contains("needs no books list", StringComparison.Ordinal));
        starTools.CreateBook("Last Light", "starfall", "drift-war");
        Assert.Contains("\nbooks: [first-light, last-light]\n", star.Read(DriftWar).ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-69")]
    public void WorksAListLeavesOutAreListedFirstAndAPlannedEntryKeepsItsPlace()
    {
        var den = Den(TempDen.Starfall());
        // 'ash' is left out, so it comes after ember by key; 'cinder' is planned ahead, with no folder yet.
        den.Write(LongDark, "---\nid: 01JA3E28JT97KB6CQ643DZVMXX\nname: The Long Dark\nbooks: [ember]  # the main line\n---\n");
        var tools = new DenTools(den.Root);

        var made = tools.CreateBook("Kindling", "starfall", "long-dark");
        Assert.Equal("---\nid: 01JA3E28JT97KB6CQ643DZVMXX\nname: The Long Dark\nbooks: [ember, ash, kindling]  # the main line\n---\n", den.Read(LongDark));
        Assert.Contains(made.Effects, e => e.Contains("It left out ash", StringComparison.Ordinal));

        den.Write(LongDark, "---\nid: 01JA3E28JT97KB6CQ643DZVMXX\nname: The Long Dark\nbooks: [ember, cinder, ash, kindling]\n---\n");
        var hash = tools.GetWorks("starfall", "long-dark").Hash;
        var refused = Assert.Throws<DenToolException>(() => tools.CreateBook("Cinder", "starfall", "long-dark", after: "kindling", expectedHash: hash));
        Assert.Equal(ToolErrorCodes.BadArgument, refused.Code);
        Assert.Contains("already lists 'cinder'", refused.Message, StringComparison.Ordinal);

        var planned = tools.CreateBook("Cinder", "starfall", "long-dark", expectedHash: hash);
        Assert.Equal([planned.Path], planned.Files.Select(f => f.Path));
        Assert.Contains(planned.Effects, e => e.Contains("already listed 'cinder', so it takes that place", StringComparison.Ordinal));
        Assert.Equal(["ember", "cinder", "ash", "kindling"], tools.GetWorks("starfall", "long-dark").Works.Select(w => w.Key));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-70")]
    public void ARefusedCreateWritesNothing()
    {
        var den = Den(TempDen.Starfall());
        var tools = new DenTools(den.Root);
        var files = Snapshot(den);

        void Refused(string code, string text, Action create)
        {
            var e = Assert.Throws<DenToolException>(create);
            Assert.Equal(code, e.Code);
            Assert.Contains(text, e.Message, StringComparison.Ordinal);
        }

        // Names: empty, more than one line, too long.
        Refused(ToolErrorCodes.BadArgument, "it's empty", () => tools.CreateBook("  ", "starfall"));
        Refused(ToolErrorCodes.BadArgument, "line break", () => tools.CreateSeries("Ash" + (char)0x2028 + "Fall", "starfall"));
        Refused(ToolErrorCodes.BadArgument, "at most 200", () => tools.CreateBook(new string('a', 201), "starfall"));

        // Keys: one that must start with a letter, and clashes (F-KEY-5), ignoring case.
        Refused(ToolErrorCodes.BadArgument, "must start with a letter (F-KEY-2)", () => tools.CreateBook("1984", "starfall"));
        Refused(ToolErrorCodes.BadArgument, "no letters or digits", () => tools.CreateBook("???", "starfall"));
        Refused(ToolErrorCodes.AlreadyExists, "already has a book 'ember'", () => tools.CreateBook("Ember", "starfall"));
        Refused(ToolErrorCodes.AlreadyExists, "already has a book 'ember'", () => tools.CreateBook("EMBER", "starfall", "drift-war"));
        Refused(ToolErrorCodes.AlreadyExists, "already has a series 'long-dark'", () => tools.CreateSeries("Long Dark", "starfall"));
        Refused(ToolErrorCodes.AlreadyExists, "has a standalone book 'a-quiet-moon'", () => tools.CreateSeries("A Quiet Moon", "starfall"));
        Refused(ToolErrorCodes.AlreadyExists, "has a series 'drift-war'", () => tools.CreateBook("Drift War", "starfall"));
        Directory.CreateDirectory(Path.Combine(den.Root, "universes", "starfall", "books", "Notes"));
        Refused(ToolErrorCodes.AlreadyExists, "universes/starfall/books/Notes", () => tools.CreateBook("Notes", "starfall"));
        Directory.Delete(Path.Combine(den.Root, "universes", "starfall", "books", "Notes"));

        // Places: an after that isn't there, a series' book as a universe's work, an unknown series.
        Refused(ToolErrorCodes.BadArgument, "Its books, in story order: ember, ash.", () => tools.CreateBook("Cinder", "starfall", "long-dark", after: "a-quiet-moon"));
        Refused(ToolErrorCodes.BadArgument, "long-dark, a-quiet-moon, drift-war", () => tools.CreateSeries("Far Shore", "starfall", after: "ember"));
        Refused(ToolErrorCodes.BadArgument, "It has no books yet, so leave that out.", () => tools.CreateBook("First", "starfall", "drift-war", after: "x"));
        Refused(ToolErrorCodes.UnknownScope, "has no series 'sequels'", () => tools.CreateBook("Cinder", "starfall", "sequels"));

        // Chapters: 1 to 1000.
        Refused(ToolErrorCodes.BadArgument, "1 to 1000 chapters, not 0", () => tools.CreateBook("Cinder", "starfall", chapters: 0));
        Refused(ToolErrorCodes.BadArgument, "not 1001", () => tools.CreateBook("Cinder", "starfall", chapters: 1001));

        // A stale universe.md or series.md.
        var hash = tools.GetWorks("starfall", "long-dark").Hash;
        den.Write(LongDark, den.Read(LongDark) + "\nEdited elsewhere.\n");
        Refused(ToolErrorCodes.StaleFile, "has changed since it was read", () => tools.CreateBook("Cinder", "starfall", "long-dark", expectedHash: hash));
        den.Write(LongDark, files[LongDark]);

        // A session pinned to one universe creates only in it.
        var pinned = new DenTools(den.Root) { PinnedUniverse = "starfall" };
        Assert.Throws<DenToolException>(() => pinned.CreateBook("Undertow", "drift"));

        Assert.Equal(files, Snapshot(den));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-70")]
    public void FinalBooksDontStopANewBookBesideThem()
    {
        // The series' file and the universe's aren't in a final book's scope (F-BOOK-1), so a freeze doesn't refuse.
        var den = Den(TempDen.Dracula());
        den.Write(DraculaBook, den.Read(DraculaBook).Replace("\nstatus: draft\n", "\nstatus: final\n", StringComparison.Ordinal));
        var tools = new DenTools(den.Root);

        var made = tools.CreateBook("Dracula's Guest", "dracula");

        Assert.Contains("order: [dracula, dracula-s-guest]", den.Read(DraculaUniverse), StringComparison.Ordinal);
        Assert.Contains("status: final", den.Read(DraculaBook), StringComparison.Ordinal);
        Assert.DoesNotContain(made.Files, f => f.Path.StartsWith("universes/dracula/books/dracula/", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-70")]
    public void AListThatCantBeWrittenLeavesNoNewFolderBehind()
    {
        var den = Den(TempDen.Starfall());
        var files = Snapshot(den);
        var tools = new DenTools(den.Root) { Store = new WriteToolTests.FailingStore(den.Root, LongDark) };

        var e = Assert.Throws<DenToolException>(() => tools.CreateBook("Cinder", "starfall", "long-dark"));

        Assert.Equal(ToolErrorCodes.WriteFailed, e.Code);
        Assert.False(Directory.Exists(Path.Combine(den.Root, "universes", "starfall", "series", "long-dark", "books", "cinder")));
        Assert.Equal(files, Snapshot(den));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-69")]
    public void TheWorksAreInStoryOrderWithTheHashOfTheFileThatListsThem()
    {
        var den = Den(TempDen.Starfall());
        var tools = new DenTools(den.Root);

        var universe = tools.GetWorks("starfall");
        Assert.Equal(("starfall", null, "Starfall", Universe), (universe.Universe, universe.Series, universe.Name, universe.Path));
        Assert.Equal(
            [("series", "long-dark", "The Long Dark"), ("book", "a-quiet-moon", "A Quiet Moon"), ("series", "drift-war", "The Drift War")],
            universe.Works.Select(w => (w.Kind, w.Key, w.Name)));
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(den.Root, Universe)))), universe.Hash);

        var series = tools.GetWorks("starfall", "long-dark");
        Assert.Equal(LongDark, series.Path);
        Assert.Equal(["ember", "ash"], series.Works.Select(w => w.Key));
        Assert.Empty(tools.GetWorks("starfall", "drift-war").Works);
        Assert.Equal(ToolErrorCodes.UnknownScope, Assert.Throws<DenToolException>(() => tools.GetWorks("starfall", "nope")).Code);
    }

    // ------------------------------------------------------------ helpers

    private TempDen Den(TempDen den)
    {
        _dens.Add(den);
        return den;
    }

    /// <summary>Every diagnostic check reports, by file, rule, and message.</summary>
    private static List<string> Diagnostics(DenTools tools) =>
        tools.Check(limit: 5000).Diagnostics.Select(d => $"{d.Path}: {d.Rule}: {d.Message}").ToList();

    /// <summary>Every file in the den but the index, by path.</summary>
    private static Dictionary<string, string> Snapshot(TempDen den) =>
        Directory.EnumerateFiles(den.Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(den.Root, f).Replace('\\', '/'))
            .Where(p => !p.StartsWith(".wolfden/", StringComparison.Ordinal))
            .ToDictionary(p => p, den.Read, StringComparer.Ordinal);
}

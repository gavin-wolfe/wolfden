namespace Wolfden.Mcp.Tests;

/// <summary>
/// Adding a chapter to a book (#215; format.md §6.4, F-BOOK-1..6): the
/// writer's operation behind the web UI's Add chapter and Insert chapter…
/// and <c>wolfden new chapter</c>, over copies of the invented Starfall den
/// and the Dracula sample.
/// </summary>
public sealed class AddChapterTests : IDisposable
{
    private const string Ember = "universes/starfall/series/long-dark/books/ember/book.md";
    private const string Ash = "universes/starfall/series/long-dark/books/ash/book.md";
    private const string Dracula = "universes/dracula/books/dracula/book.md";

    private readonly List<TempDen> _dens = [];

    public void Dispose()
    {
        foreach (var den in _dens)
        {
            den.Dispose();
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void AnInsertedChapterGetsALetterKeyAndTheNumbersAfterItMoveUp()
    {
        var den = Den(TempDen.Starfall());
        var tools = new DenTools(den.Root);
        var before = Diagnostics(tools);
        var hash = tools.GetChapter("ember", universe: "starfall").Hash;

        var made = tools.AddChapter("ember", "The Quiet Hour", after: "2", expectedHash: hash, universe: "starfall");

        // The rest of book.md is as it was: the comment, the flow items, and the quoting of the item before.
        Assert.Equal(
            "---\nid: 01JAQKFBF5KZNWJ47TAN9ZT24M\nname: Ember\nstatus: draft\nchapters:\n" +
            "  - { key: prologue, title: Before the Dark }\n" +
            "  - { key: \"1\", number: 1, title: Kessan Reach }\n" +
            "  - { key: \"2\", number: 2 }\n" +
            "  - { key: \"2c\", number: 3, title: The Quiet Hour }\n" +
            "  - { key: 2b, number: 4, title: The Long Watch }   # added in revision\n" +
            "  - { key: \"3\", number: 5 }\n---\n",
            den.Read(Ember).ReplaceLineEndings("\n"));
        Assert.Equal(("ember", "2c", "ember:2c", 3L, "The Quiet Hour", 3), (made.Book, made.Key, made.Position, made.Number, made.Title, made.Index));
        Assert.Equal([new RenumberedView("2b", 3, 4), new RenumberedView("3", 4, 5)], made.Renumbered);
        Assert.Contains(made.Effects, e => e.Contains("Added chapter '2c' to book 'ember', after '2', with number 3", StringComparison.Ordinal));
        Assert.Contains(made.Effects, e => e.Contains("'2b' 3 → 4, '3' 4 → 5", StringComparison.Ordinal));
        Assert.Equal([Ember], made.Files.Select(f => f.Path));
        Assert.Equal(made.Hash, tools.GetChapter("ember", universe: "starfall").Hash);
        Assert.Empty(made.Warnings);
        Assert.Empty(Diagnostics(tools).Except(before));

        // Keys never change, so ember:3 is still the chapter it was, now after 2c.
        Assert.Equal(["prologue", "1", "2", "2c", "2b", "3"], tools.GetChapter("ember", universe: "starfall").Chapters.Select(c => c.Key));
        Assert.Equal(5L, tools.GetChapter("ember", "3", "starfall").Chapters[0].Number);
        Assert.Equal("ember:2c", tools.CanonAsOf("starfall", "ember:2c").Position);

        // After 2b, 2ba; after 2 again, the next unused letter; after the prologue, a letter too.
        Assert.Equal("2ba", tools.AddChapter("ember", after: "2b", universe: "starfall").Key);
        Assert.Equal("2d", tools.AddChapter("ember", after: "2", universe: "starfall").Key);
        var prologue = tools.AddChapter("ember", after: "prologue", universe: "starfall");
        Assert.Equal(("prologuea", 1L), (prologue.Key, prologue.Number));
        Assert.Equal(
            ["prologue:", "prologuea:1", "1:2", "2:3", "2d:4", "2c:5", "2b:6", "2ba:7", "3:8"],
            tools.GetChapter("ember", universe: "starfall").Chapters.Select(c => $"{c.Key}:{c.Number}"));
        Assert.Empty(Diagnostics(tools).Except(before));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void AddingAtTheEndGivesTheNextWholeNumberInTheListsOwnStyle()
    {
        // Dracula: block items, and a closing 'note' chapter with no number after 27.
        var den = Den(TempDen.Dracula());
        var tools = new DenTools(den.Root);
        var before = Diagnostics(tools);
        var text = den.Read(Dracula).ReplaceLineEndings("\n");

        var end = tools.AddChapter("dracula", "Afterword");

        Assert.Equal(("28", 28L, 28), (end.Key, end.Number, end.Index));
        Assert.Empty(end.Renumbered);
        Assert.Contains("at the end, after 'note'", end.Effects[0], StringComparison.Ordinal);
        var close = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        Assert.Equal(
            text[..close] + "\n  - key: \"28\"\n    number: 28\n    title: Afterword" + text[close..],
            den.Read(Dracula).ReplaceLineEndings("\n"));
        Assert.Empty(Diagnostics(tools).Except(before));

        // Ash: a flow list of bare keys and no numbers. A bare key stays bare; with a title, it's a mapping.
        var star = Den(TempDen.Starfall());
        var starTools = new DenTools(star.Root);
        var starBefore = Diagnostics(starTools);
        var inserted = starTools.AddChapter("ash", after: "2", universe: "starfall");
        Assert.Equal(("2b", (long?)null), (inserted.Key, inserted.Number));
        Assert.Contains("no number, as the book's other chapters have none", inserted.Effects[0], StringComparison.Ordinal);
        starTools.AddChapter("ash", "Embers", universe: "starfall");
        Assert.Contains("chapters: [1, 2, \"2b\", 3, 4, epilogue, { key: \"5\", title: Embers }]\n", star.Read(Ash).ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Empty(Diagnostics(starTools).Except(starBefore));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void ABookWithNoChaptersGetsChapterOne()
    {
        var den = Den(TempDen.Starfall());
        var tools = new DenTools(den.Root);
        var book = tools.CreateBook("Kindling", "starfall", "long-dark");

        var first = tools.AddChapter("kindling", universe: "starfall");

        Assert.Equal(("1", 1L, 0), (first.Key, first.Number, first.Index));
        Assert.Contains("its first chapter", first.Effects[0], StringComparison.Ordinal);
        Assert.Equal($"---\nid: {book.Id}\nname: Kindling\nchapters:\n  - {{ key: \"1\", number: 1 }}\n---\n", den.Read(book.Path).ReplaceLineEndings("\n"));
        Assert.Equal("2", tools.AddChapter("kindling", universe: "starfall").Key);
        Assert.Equal("kindling:2", tools.CanonAsOf("starfall", "kindling:2").Position);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-72")]
    public void ARefusedAddWritesNothing()
    {
        var den = Den(TempDen.Starfall());
        var tools = new DenTools(den.Root);
        var ember = den.Read(Ember);
        var hash = tools.GetChapter("ember", universe: "starfall").Hash;

        void Refused(Action add, string code, string text)
        {
            var e = Assert.Throws<DenToolException>(add);
            Assert.Equal(code, e.Code);
            Assert.Contains(text, e.Message, StringComparison.Ordinal);
            Assert.Equal(ember, den.Read(Ember));
        }

        Refused(() => tools.AddChapter("ember", after: "9", universe: "starfall"), ToolErrorCodes.UnknownChapter, "Book 'ember' has no chapter '9'. Its chapters: prologue, 1, 2, 2b, 3.");
        Refused(() => tools.AddChapter("ember", "Two\nlines", universe: "starfall"), ToolErrorCodes.BadArgument, "title of one line, at most 200 characters");
        Refused(() => tools.AddChapter("ember", "Two" + char.ConvertFromUtf32(0x2028) + "lines", universe: "starfall"), ToolErrorCodes.BadArgument, "title of one line");
        Refused(() => tools.AddChapter("ember", new string('x', 201), universe: "starfall"), ToolErrorCodes.BadArgument, "201 characters");
        Refused(() => tools.AddChapter("ember", expectedHash: new string('0', 64), universe: "starfall"), ToolErrorCodes.StaleFile, "has changed since it was read");
        Refused(() => tools.AddChapter("cinder", universe: "starfall"), ToolErrorCodes.UnknownScope, "cinder");
        Refused(() => tools.AddChapter(" ", universe: "starfall"), ToolErrorCodes.BadArgument, "Give the key of the book");

        // A title of exactly 200 characters, and a blank one (no title), are fine.
        Assert.Equal(new string('x', 200), tools.AddChapter("ember", new string('x', 200), expectedHash: hash, universe: "starfall").Title);
        Assert.Null(tools.AddChapter("ember", "  ", universe: "starfall").Title);

        // No chapters yet: nothing to add after.
        var empty = tools.CreateBook("Kindling", "starfall", "long-dark");
        var kindling = den.Read(empty.Path);
        var none = Assert.Throws<DenToolException>(() => tools.AddChapter("kindling", after: "1", universe: "starfall"));
        Assert.Equal(ToolErrorCodes.UnknownChapter, none.Code);
        Assert.Contains("has no chapters yet", none.Message, StringComparison.Ordinal);
        Assert.Equal(kindling, den.Read(empty.Path));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-72")]
    public void AFinalBookIsRefused()
    {
        var den = Den(TempDen.Starfall());
        den.Write(Ember, den.Read(Ember).Replace("status: draft", "status: final", StringComparison.Ordinal));
        var tools = new DenTools(den.Root);
        var ember = den.Read(Ember);

        var e = Assert.Throws<DenToolException>(() => tools.AddChapter("ember", "New", universe: "starfall"));

        Assert.Equal(ToolErrorCodes.Frozen, e.Code);
        Assert.StartsWith("Book 'ember' is final (frozen), so no chapter can be added to it until it's unfrozen (F-BOOK-1).", e.Message, StringComparison.Ordinal);
        Assert.Equal(ember, den.Read(Ember));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-72")]
    public void ABookMdWithNoIdGetsOne()
    {
        var den = Den(TempDen.Starfall());
        den.Write(Ash, den.Read(Ash).ReplaceLineEndings("\n").Replace("id: 01JAQKFBF5KZNWJ47TAN9ZT24N\n", "", StringComparison.Ordinal));
        var tools = new DenTools(den.Root);

        var made = tools.AddChapter("ash", universe: "starfall");

        Assert.NotNull(made.IdAdded);
        Assert.Contains($"id: {made.IdAdded}", den.Read(Ash), StringComparison.Ordinal);
        Assert.Contains(made.Effects, e => e.Contains("had no id", StringComparison.Ordinal));
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
}

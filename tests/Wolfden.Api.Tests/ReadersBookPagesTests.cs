using System.Net;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// What the Reader's view's book and chapter pages and position bar ask for (#300): a word
/// readers at a position don't know appears in no answer they get, and a word they do, does.
/// <c>web/src/ReaderBookPages.test.tsx</c> holds the other half: those pages ask for nothing else.
/// </summary>
public sealed class ReadersBookPagesTests : IDisposable
{
    private const string Universe = "saltmarch";

    /// <summary>Each word, with the first position whose readers know it, or null for one a reader's page never shows.</summary>
    private static readonly (string Word, string? From)[] Words =
    [
        ("Brightharbor", "brightharbor:1"),   // A book's name.
        ("Gullmere", "brightharbor:1"),       // A chapter's title.
        ("Sandpiper", "brightharbor:1"),      // A chapter's field.
        ("Heronfell", "brightharbor:1"),      // A link to a note not met, shown as written, in a chapter reached.
        ("Marrowcove", "brightharbor:1"),     // The label of a link to a note not met.
        ("Oystergate", "brightharbor:2"),     // The next chapter's title.
        ("Curlewdown", "brightharbor:2"),     // The next chapter's field.
        ("Saltbarrow", "brightharbor:3"),     // A chapter's title, later.
        ("Tarnwick", "tarnwick:1"),           // A later book's name.
        ("Nightsedge", "tarnwick:1"),         // A later series' name.
        ("Wintershoal", "tarnwick:1"),        // A later book's chapter's title.
        ("Foxglare", "tarnwick:1"),           // Its field.
        ("Harrowgull", "tarnwick:2"),         // The title of the chapter after it.
        ("Dunlinhook", "tarnwick:2"),         // A book's chapter default, which chapter 1 has its own value for.
        ("the-keeper", "tarnwick:1"),         // A target behind a label, until its note is met, and then its path.
        ("Oldkeeper", null),                  // That note's alias.
        ("Reedlantern", null),                // An alias behind a label, to a note met.
    ];

    /// <summary>The story in order.</summary>
    private static readonly string[] Order = ["brightharbor:1", "brightharbor:2", "brightharbor:3", "tarnwick:1", "tarnwick:2"];

    private readonly TempDen _den = TempDen.Starfall();

    public ReadersBookPagesTests()
    {
        _den.Write("packs/starfall/chapter.yaml", "fields:\n  turns_at: { type: position }\n");
        _den.Write($"universes/{Universe}/universe.md", "---\nid: 01JB0000000000000000000000\nname: Saltmarch\norder: [brightharbor, nightsedge]\n---\n\nThe universe's own text.\n");
        _den.Write(
            $"universes/{Universe}/books/brightharbor/book.md",
            """
            ---
            id: 01JB0000000000000000000001
            name: Brightharbor
            status: draft
            chapters:
              - { key: "1", number: 1, title: Gullmere, fields: { when: "Sandpiper", pov: ["[[First Mate]]", "[[Heronfell]]", "[[the-keeper|Marrowcove]]", "[[Reedlantern|the mate]]"], turns_at: "tarnwick:1" } }
              - { key: "2", number: 2, title: Oystergate, fields: { when: "Curlewdown" } }
              - { key: "3", number: 3, title: Saltbarrow }
            ---
            """);
        _den.Write($"universes/{Universe}/series/nightsedge/series.md", "---\nid: 01JB0000000000000000000002\nname: Nightsedge\nbooks: [tarnwick]\n---\n");
        _den.Write(
            $"universes/{Universe}/series/nightsedge/books/tarnwick/book.md",
            """
            ---
            id: 01JB0000000000000000000003
            name: Tarnwick
            status: draft
            chapter_defaults:
              when: Dunlinhook
            chapters:
              - { key: "1", number: 1, title: Wintershoal, fields: { when: "Foxglare" } }
              - { key: "2", number: 2, title: Harrowgull }
            ---
            """);
        _den.Write(
            $"universes/{Universe}/entities/notes/first-mate.md",
            "---\ntype: character\nname: First Mate\naliases: [Reedlantern]\nstatus: canon\n---\n\nOn deck.\n");
        _den.Write(
            $"universes/{Universe}/entities/notes/the-keeper.md",
            "---\ntype: character\nname: The keeper\naliases: [Oldkeeper]\nstatus: canon\nfrom: \"tarnwick\"\n---\n\nOf the light.\n");
        _den.Write(
            $"universes/{Universe}/entities/notes/heronfell.md",
            "---\ntype: character\nname: Heronfell\nstatus: canon\nfrom: \"tarnwick\"\n---\n\nA later character.\n");
    }

    public void Dispose() => _den.Dispose();

    private Task<Server> Start() => Server.StartAsync(new WolfdenApiOptions(_den.Root));

    /// <summary>Every answer the Reader's view's book and chapter pages and position bar get at a position.</summary>
    private static async Task<string> WhatReadersGet(Server server, string at)
    {
        var answers = new List<string>();
        var hierarchy = await server.GetJsonAsync($"/api/hierarchy?universe={Universe}&asOf={at}&readers=true", HttpStatusCode.OK);
        answers.Add(hierarchy.GetRawText());
        answers.Add((await server.GetJsonAsync($"/api/positions?universe={Universe}&asOf={at}&readers=true", HttpStatusCode.OK)).GetRawText());
        answers.Add((await server.GetJsonAsync($"/api/entities?universe={Universe}&asOf={at}&limit=1000", HttpStatusCode.OK)).GetRawText());
        var u = hierarchy.GetProperty("universe");
        var books = u.GetProperty("books").EnumerateArray().Concat(u.GetProperty("series").EnumerateArray().SelectMany(s => s.GetProperty("books").EnumerateArray()));
        foreach (var book in books)
        {
            answers.Add((await server.GetJsonAsync($"/api/book/chapter-fields?universe={Universe}&book={book.GetProperty("key").GetString()}&asOf={at}&readers=true", HttpStatusCode.OK)).GetRawText());
        }

        return string.Join('\n', answers);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-110")]
    [InlineData("brightharbor")]
    [InlineData("brightharbor:1")]
    [InlineData("brightharbor:2")]
    [InlineData("brightharbor:3")]
    [InlineData("tarnwick")]
    [InlineData("tarnwick:1")]
    [InlineData("tarnwick:2")]
    public async Task AWordReadersDontKnowYetIsInNoAnswerAndOneTheyDoIs(string at)
    {
        await using var server = await Start();

        // A bare book is the same point as its first chapter (F-POS-7).
        static int Place(string position) => Array.IndexOf(Order, position.Contains(':', StringComparison.Ordinal) ? position : $"{position}:1");

        var got = await WhatReadersGet(server, at);

        foreach (var (word, from) in Words)
        {
            if (from is not null && Place(from) <= Place(at))
            {
                Assert.True(got.Contains(word, StringComparison.Ordinal), $"'{word}' isn't in what readers at {at} get.");
            }
            else
            {
                Assert.False(got.Contains(word, StringComparison.Ordinal), $"'{word}' is in what readers at {at} get.");
            }
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-110")]
    public async Task AChaptersFieldsShowLinksAsTheyShowOnANotesPage()
    {
        await using var server = await Start();
        var answer = await server.GetJsonAsync($"/api/book/chapter-fields?universe={Universe}&book=brightharbor&asOf=brightharbor:1&readers=true", HttpStatusCode.OK);

        var chapter = Assert.Single(answer.GetProperty("chapters").EnumerateArray());
        var fields = chapter.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!);
        Assert.Equal("Sandpiper", fields["when"].GetProperty("value").GetString());

        // A note met, by its key, with its name; a note not met, as written; a label; a label on a met note's alias.
        var pov = fields["pov"];
        var items = pov.GetProperty("value").EnumerateArray().Select(v => v.GetString()!).ToList();
        var mate = pov.GetProperty("links").EnumerateArray().Select(l => (Text: l.GetProperty("text").GetString()!, Label: l.GetProperty("label").GetString()!)).ToList();
        Assert.Equal(2, mate.Count);
        Assert.Equal(mate[0].Text, items[0]);
        Assert.Equal("First Mate", mate[0].Label);
        Assert.Equal("Heronfell", items[1]);
        Assert.Equal("Marrowcove", items[2]);
        Assert.Equal(mate[1].Text, items[3]);
        Assert.Equal("the mate", mate[1].Label);

        // A position value later than the position isn't there at all.
        Assert.False(fields.ContainsKey("turns_at"));

        // Nothing an edit needs.
        var written = pov.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "name", "value", "from", "fromKey", "none", "links" }, written);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-110")]
    public async Task APositionValueShowsOnceItsReached()
    {
        await using var server = await Start();
        var answer = await server.GetJsonAsync($"/api/book/chapter-fields?universe={Universe}&book=brightharbor&asOf=tarnwick:1&readers=true", HttpStatusCode.OK);

        var first = answer.GetProperty("chapters").EnumerateArray().First();
        Assert.Contains(first.GetProperty("fields").EnumerateArray(), f => f.GetProperty("name").GetString() == "turns_at");
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-110")]
    [InlineData("/api/hierarchy?universe=saltmarch&readers=true")]
    [InlineData("/api/positions?universe=saltmarch&readers=true")]
    [InlineData("/api/positions?universe=saltmarch&asOf=brightharbor:1")]
    [InlineData("/api/book/chapter-fields?universe=saltmarch&book=brightharbor&readers=true")]
    [InlineData("/api/book/chapter-fields?universe=saltmarch&book=brightharbor&asOf=brightharbor:1")]
    public async Task ReadersGoesOnlyWithAPosition(string path)
    {
        await using var server = await Start();
        var answer = await server.GetJsonAsync(path, HttpStatusCode.BadRequest);
        Assert.Equal("bad_argument", answer.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-110")]
    public async Task ABooksFieldsDontNameOtherBooksWhenItIsntThere()
    {
        await using var server = await Start();
        var answer = await server.GetJsonAsync($"/api/book/chapter-fields?universe={Universe}&book=nothing&asOf=brightharbor:1&readers=true", HttpStatusCode.NotFound);
        Assert.DoesNotContain("Tarnwick", answer.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-105")]
    public async Task TheTreeKeepsOnlyWhatsReachedAndWhereAMetNoteIsFiled()
    {
        // A note met at the first chapter, filed in the later book.
        _den.Write(
            $"universes/{Universe}/series/nightsedge/books/tarnwick/entities/notes/stowaway.md",
            "---\ntype: note\nname: A stowaway\nstatus: canon\nfrom: \"brightharbor:1\"\n---\n\nAboard.\n");
        await using var server = await Start();

        var kept = (await server.GetJsonAsync($"/api/hierarchy?universe={Universe}&asOf=brightharbor:2&readers=true", HttpStatusCode.OK)).GetProperty("universe");
        Assert.Equal(["1", "2"], kept.GetProperty("books")[0].GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("key").GetString()));

        // Its book stays, so the note has a place, with no later chapters.
        var tarnwick = Assert.Single(kept.GetProperty("series")[0].GetProperty("books").EnumerateArray());
        Assert.Equal("Tarnwick", tarnwick.GetProperty("name").GetString());
        Assert.Empty(tarnwick.GetProperty("chapters").EnumerateArray());

        // A bare book reaches its first chapter (F-POS-7).
        var bare = (await server.GetJsonAsync($"/api/hierarchy?universe={Universe}&asOf=tarnwick&readers=true", HttpStatusCode.OK)).GetProperty("universe");
        Assert.Equal(["1"], bare.GetProperty("series")[0].GetProperty("books")[0].GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("key").GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-105")]
    public async Task ASeriesWithNothingYetIsNotInTheTree()
    {
        await using var server = await Start();
        var u = (await server.GetJsonAsync($"/api/hierarchy?universe={Universe}&asOf=brightharbor:3&readers=true", HttpStatusCode.OK)).GetProperty("universe");
        Assert.Empty(u.GetProperty("series").EnumerateArray());
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-111")]
    [InlineData("brightharbor:1", "chapter", "brightharbor:2")]
    [InlineData("brightharbor:3", "chapter", "tarnwick:1")]
    [InlineData("tarnwick:2", "chapter", null)]
    [InlineData("brightharbor", "chapter", "brightharbor:1")]
    [InlineData("brightharbor:2", "book", "tarnwick:1")]
    [InlineData("tarnwick", "book", null)]
    public async Task MovingForwardGivesOnlyTheOnePosition(string after, string step, string? next)
    {
        await using var server = await Start();
        var answer = await server.GetJsonAsync($"/api/positions/next?universe={Universe}&after={after}&step={step}", HttpStatusCode.OK);
        Assert.Equal(next, answer.GetProperty("position").GetString());
        Assert.Equal(["version", "position"], answer.EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-111")]
    [InlineData("nowhere:1", "chapter", "bad_position")]
    [InlineData("brightharbor:1", "sideways", "bad_argument")]
    public async Task MovingForwardRefusesWhatItCantRead(string after, string step, string code)
    {
        await using var server = await Start();
        var answer = await server.GetJsonAsync($"/api/positions/next?universe={Universe}&after={after}&step={step}", HttpStatusCode.BadRequest);
        Assert.Equal(code, answer.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-103")]
    public async Task TheCountOfTheDensProblemsNamesNoFile()
    {
        _den.Write($"universes/{Universe}/entities/notes/broken-later.md", "---\ntype: note\nname: Broken\nstatus: nonsense\n: :\n---\n");
        await using var server = await Start();

        var whole = await server.GetJsonAsync("/api/den/errors", HttpStatusCode.OK);
        var count = await server.GetJsonAsync("/api/den/errors?countOnly=true", HttpStatusCode.OK);

        Assert.True(whole.GetProperty("count").GetInt32() > 0);
        Assert.Equal(whole.GetProperty("count").GetInt32(), count.GetProperty("count").GetInt32());
        Assert.Empty(count.GetProperty("errors").EnumerateArray());
        Assert.DoesNotContain("broken-later", count.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>A note whose text has every kind of link the Reader's page has to read for itself.</summary>
    private void WriteLinkyNote()
    {
        _den.Write(
            $"universes/{Universe}/entities/notes/first-mate.md",
            "---\ntype: character\nname: First Mate\naliases: [Reedlantern]\nstatus: canon\n---\n\nOn deck.\n\n## Knownpart\nKnown.\n\n## Secondwatch\n<!-- wolfden: from=tarnwick:1 -->\nLater.\n");
        _den.Write(
            $"universes/{Universe}/entities/notes/linky.md",
            """
            ---
            type: note
            name: Linky
            status: canon
            fields:
              mapped: { "[[the-keeper|Keylabel]]": Keyvalue }
            ---

            Lead.

            ## [[the-keeper|Headlabel]]
            Under it.

            ## Tablepart
            | [[the-keeper\|Celllabel]] | plain |

            ## Pictures
            ![[[the-keeper|Altlabel]]](nothing.png)

            ## Split
            See [[the-keeper
            |Splitlabel]] there.

            ## Aimed
            Aim [[First Mate#Secondwatch]] and [[First Mate#Knownpart]] and [[Heronfell#Noheadhere]].

            """);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-106")]
    [InlineData("Headlabel", true)]
    [InlineData("Celllabel", true)]
    [InlineData("Altlabel", true)]
    [InlineData("Splitlabel", true)]
    [InlineData("Keylabel", true)]
    [InlineData("Knownpart", true)]
    [InlineData("the-keeper", false)]
    [InlineData("Secondwatch", false)]
    [InlineData("Noheadhere", false)]
    public async Task AReadersSearchFindsALinkByWhatItShowsWhereverItIs(string word, bool found)
    {
        WriteLinkyNote();
        await using var server = await Start();

        var answer = await server.GetJsonAsync($"/api/search?universe={Universe}&query={word}&asOf=brightharbor:1&readers=true&limit=200", HttpStatusCode.OK);

        var inLinky = answer.GetProperty("hits").EnumerateArray().Where(h => h.GetProperty("entity").GetProperty("name").GetString() == "Linky").ToList();
        if (word == "Knownpart")
        {
            // In its own note's heading too, so only Linky's hit is asked about.
            Assert.Contains(inLinky, h => h.GetProperty("text").GetString()!.Contains("First Mate: Knownpart", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal(found, inLinky.Count > 0);
        }

        Assert.DoesNotContain("the-keeper", string.Concat(inLinky.Select(h => h.GetRawText())), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-107")]
    public async Task TheAuthorsSearchSaysAWordInATableCellsTargetIsOnlyInATarget()
    {
        WriteLinkyNote();
        await using var server = await Start();

        var answer = await server.GetJsonAsync($"/api/search?universe={Universe}&query=Celllabel&marksAt=brightharbor:1&limit=200", HttpStatusCode.OK);
        Assert.True(Assert.Single(answer.GetProperty("hits").EnumerateArray()).GetProperty("met").GetBoolean());

        var target = await server.GetJsonAsync($"/api/search?universe={Universe}&query=keeper&marksAt=brightharbor:1&limit=200&field=", HttpStatusCode.OK);
        var cell = target.GetProperty("hits").EnumerateArray().Single(h => h.TryGetProperty("section", out var s) && s.GetString() == "Tablepart");
        Assert.False(cell.GetProperty("met").GetBoolean());
        Assert.Equal("linkTarget", cell.GetProperty("hiddenIn").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-101")]
    public async Task AReadersPageHasAHeadingAsItShowsAndAKnownSectionOnly()
    {
        WriteLinkyNote();
        await using var server = await Start();
        var page = (await server.GetJsonAsync($"/api/entity?universe={Universe}&entity=Linky&asOf=brightharbor:1&readers=true", HttpStatusCode.OK)).GetProperty("entity");

        var sections = page.GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal("Headlabel", sections[0].GetProperty("name").GetString());
        Assert.Empty(sections[0].GetProperty("links").EnumerateArray());

        var aimed = sections.Single(s => s.GetProperty("name").GetString() == "Aimed").GetProperty("links").EnumerateArray().ToList();
        var later = aimed.Single(l => l.GetProperty("text").GetString() == "[[First Mate#Secondwatch]]");
        Assert.Equal(JsonValueKind.Null, later.GetProperty("heading").ValueKind);
        Assert.Equal(JsonValueKind.Null, later.GetProperty("section").ValueKind);
        var known = aimed.Single(l => l.GetProperty("text").GetString() == "[[First Mate#Knownpart]]");
        Assert.Equal("Knownpart", known.GetProperty("heading").GetString());
        Assert.Equal("Knownpart", known.GetProperty("section").GetString());
        var notMet = aimed.Single(l => l.GetProperty("text").GetString() == "[[Heronfell#Noheadhere]]");
        Assert.Equal(JsonValueKind.Null, notMet.GetProperty("heading").ValueKind);

        // Once it's reached, it's named.
        var at = (await server.GetJsonAsync($"/api/entity?universe={Universe}&entity=Linky&asOf=tarnwick:1&readers=true", HttpStatusCode.OK)).GetProperty("entity");
        var reached = at.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "Aimed").GetProperty("links").EnumerateArray()
            .Single(l => l.GetProperty("text").GetString() == "[[First Mate#Secondwatch]]");
        Assert.Equal("Secondwatch", reached.GetProperty("heading").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-102")]
    public async Task TheAuthorsPageAndTheFullViewKeepAHeadingAsWritten()
    {
        WriteLinkyNote();
        await using var server = await Start();
        foreach (var query in new[] { "", "&marksAt=brightharbor:1" })
        {
            var page = (await server.GetJsonAsync($"/api/entity?universe={Universe}&entity=Linky{query}", HttpStatusCode.OK)).GetProperty("entity");
            Assert.Equal("[[the-keeper|Headlabel]]", page.GetProperty("sections")[0].GetProperty("name").GetString());
        }
    }

    [Fact]
    public async Task NoPositionsAnswerChanges()
    {
        await using var server = await Start();
        var whole = await server.GetJsonAsync($"/api/positions?universe={Universe}", HttpStatusCode.OK);
        Assert.Equal(5, whole.GetProperty("count").GetInt32());
        var fields = await server.GetJsonAsync($"/api/book/chapter-fields?universe={Universe}&book=brightharbor", HttpStatusCode.OK);
        Assert.Equal(3, fields.GetProperty("chapters").GetArrayLength());
        Assert.True(fields.TryGetProperty("hash", out _));
    }
}

using System.Net;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Wolfden.Mcp;

namespace Wolfden.Api.Tests;

/// <summary>
/// <c>marksAt</c> on <c>/api/entity</c> and <c>/api/search</c> (#300): the
/// writer gets everything, with what readers at the position don't know marked.
/// <c>asOf</c> and every MCP read stay as strict as they were.
/// </summary>
public sealed class MarksAtTests : IDisposable
{
    private const string Tithe = "The%20salt%20tithe";
    private const string TithePath = "universes/starfall/entities/threads/the-salt-tithe.md";
    private const string HeirPath = "universes/starfall/entities/notes/the-salt-heir.md";

    private const string TitheText = """
        ---
        type: thread
        name: The salt tithe
        status: canon
        from: "ember:1"
        fields:
          planted: { value: ["ember:1", "ash:2"], pin: true }
          paid_off: { value: "ash:3", pin: true }
          echoes: "ash:4"
          keeper:
            - { value: Orrin Vale, from: "ember:1", until: "ash:2", pin: true }
            - { value: Morvane, from: "ash:2" }
          ward:
            - { value: Tessaly, from: "ember:1" }
            - { value: Corvin, from: "ash:3" }
          rumor: { value: Cinderlace, status: idea }
          seal:
            - { value: Brackwater, from: "ash:3", pin: true }
          omen: { value: Larkspire, status: candidate, from: "ash:4" }
          old_name: { value: Quillfeather, status: retconned }
        ---

        # The salt tithe

        Every ship pays it at the Reach.

        ## Overview
        A toll in salt.

        ### The audit
        <!-- wolfden: from=ash:2 -->
        Sablewick reads the ledger.

        ## Terms
        <!-- wolfden: from=ember:1 until=ash:2 pin=true -->
        Paid each season.

        ## Payoff
        Drennick stops paying.

        ## Tamsin's notion
        <!-- wolfden: status=idea from=ash:4 -->
        Hallowmere might own the toll.

        ## Zanthic reveal
        <!-- wolfden: status=maybe from=ash:4 -->
        Vespergale wrote the toll.

        ### Ostrelline
        Under the reveal.

        ## The reckoning
        <!-- wolfden: from=ash:3 pin=true -->
        Fenwraith closes the toll.

        ## The ending
        <!-- wolfden: status=undecided from=ash:4 -->
        Wyrmsedge is one way.

        ## Session notes
        Pellucid drafts ahead.

        """;

    /// <summary>The words a reader at <c>ember:3</c> hasn't met, each from one place above.</summary>
    private static readonly string[] LaterWords =
        ["Morvane", "Corvin", "Sablewick", "Drennick", "Cinderlace", "Brackwater", "Larkspire", "Quillfeather", "Hallowmere", "Vespergale", "Ostrelline", "Fenwraith", "Wyrmsedge", "Pellucid", "ash:"];

    private readonly TempDen _den = TempDen.Starfall();

    public MarksAtTests()
    {
        _den.Write("packs/starfall/extensions/thread.yaml", "fields:\n  echoes: { type: position }\n");
        _den.Write(TithePath, TitheText);
        _den.Write(HeirPath, "---\ntype: note\nname: The salt heir\naliases: [The heir apparent]\nstatus: canon\nfrom: \"ash:3\"\nfields:\n  claim: Ysmerine\n---\n\nThe heir inherits the toll.\n\n## Early life\nRaised at Ysmerine.\n\n## The crowning\n<!-- wolfden: from=ash:4 -->\nCrowned in the rain.\n");
    }

    public void Dispose() => _den.Dispose();

    private Task<Server> Start() => Server.StartAsync(new WolfdenApiOptions(_den.Root));

    private static JsonElement Field(JsonElement page, string name, string? value = null) =>
        page.GetProperty("entity").GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == name && (value is null || f.GetProperty("value").GetString() == value));

    private static JsonElement Section(JsonElement page, string name) =>
        page.GetProperty("entity").GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    private static void AssertMark(JsonElement item, string kind, string at)
    {
        var mark = item.GetProperty("readerMark");
        Assert.Equal(kind, mark.GetProperty("kind").GetString());
        Assert.Equal(at, mark.GetProperty("at").GetString());
    }

    private static void AssertNoMark(JsonElement item) => Assert.False(item.TryGetProperty("readerMark", out _), item.GetRawText());

    private static HashSet<string> Keys(JsonElement element)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        Walk(element);
        return keys;

        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    keys.Add(p.Name);
                    Walk(p.Value);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray())
                {
                    Walk(item);
                }
            }
        }
    }

    private static void AssertNoMarks(JsonElement answer)
    {
        foreach (var key in new[] { "readerMark", "aheadValues", "unplacedValues", "marksAt" })
        {
            Assert.DoesNotContain(key, Keys(answer));
        }
    }

    // --- Without marksAt, nothing changes. ---

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData("/api/entity?universe=starfall&entity=" + Tithe)]
    [InlineData("/api/entity?universe=starfall&entity=" + Tithe + "&asOf=ember:3")]
    [InlineData("/api/search?universe=starfall&query=salt")]
    [InlineData("/api/search?universe=starfall&query=salt&asOf=ember:3")]
    public async Task WithoutMarksAtAnAnswerHasNoMarks(string path)
    {
        await using var server = await Start();
        AssertNoMarks(await server.GetJsonAsync(path, HttpStatusCode.OK));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task AsOfStaysTheStrictView()
    {
        await using var server = await Start();

        var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&asOf=ember:3", HttpStatusCode.OK);
        Assert.Equal("canon_as_of", page.GetProperty("view").GetString());
        foreach (var word in new[] { "Sablewick", "Morvane", "Drennick", "Fenwraith", "Zanthic", "Hallowmere" })
        {
            Assert.DoesNotContain(word, page.GetRawText(), StringComparison.Ordinal);
        }

        var heir = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20salt%20heir&asOf=ember:3", HttpStatusCode.OK);
        Assert.False(heir.GetProperty("inView").GetBoolean());

        var search = await server.GetJsonAsync("/api/search?universe=starfall&query=Sablewick&asOf=ember:3", HttpStatusCode.OK);
        Assert.Equal(0, search.GetProperty("count").GetInt32());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task AMarkedReadIsTheFullViewWithMarksAdded()
    {
        await using var server = await Start();

        var full = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}", HttpStatusCode.OK);
        var marked = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ember:3", HttpStatusCode.OK);

        Assert.Equal("ember:3", marked.GetProperty("marksAt").GetString());
        Assert.Equal("full", marked.GetProperty("view").GetString());
        Assert.Equal(JsonValueKind.Null, marked.GetProperty("position").ValueKind);

        // Every member of the full view is there, unchanged: the same question SameAsMcp asks.
        SameAsMcp.Assert(full, marked);
    }

    // --- What a whole note's marks say. ---

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task AheadOfThePositionIsMarkedAndNothingIsHidden()
    {
        await using var server = await Start();
        var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ember:3", HttpStatusCode.OK);

        AssertNoMark(page.GetProperty("entity"));

        // Later entries of a field.
        AssertMark(Field(page, "keeper", "Morvane"), "ahead", "ash:2");
        AssertMark(Field(page, "seal"), "ahead", "ash:3");

        // A later section and subsection, and a thread's Payoff, which starts at paid_off with no from of its own (F-CORE-4).
        AssertMark(Section(page, "The audit"), "ahead", "ash:2");
        AssertMark(Section(page, "The reckoning"), "ahead", "ash:3");
        AssertMark(Section(page, "Payoff"), "ahead", "ash:3");

        // In effect here.
        AssertNoMark(Section(page, "Overview"));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-97")]
    public async Task ALaterUntilOnAFactInEffectIsTheWebsTagAndNotAMark()
    {
        // The read gives the until and the page's isAheadOf tags it (#288), so there's one rule for it.
        await using var server = await Start();
        var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ember:3", HttpStatusCode.OK);

        var keeper = Field(page, "keeper", "Orrin Vale");
        Assert.Equal("ash:2", keeper.GetProperty("until").GetString());
        AssertNoMark(keeper);

        var terms = Section(page, "Terms");
        Assert.Equal("ash:2", terms.GetProperty("until").GetString());
        AssertNoMark(terms);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData("ash:2")]
    [InlineData("ash:3")]
    public async Task AFactWhoseUntilHasComeIsNoLongerTrue(string position)
    {
        await using var server = await Start();
        var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt={position}", HttpStatusCode.OK);

        AssertMark(Field(page, "keeper", "Orrin Vale"), "ended", "ash:2");
        AssertMark(Section(page, "Terms"), "ended", "ash:2");
        AssertNoMark(Field(page, "keeper", "Morvane"));
        AssertNoMark(Section(page, "The audit"));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task AnEntryThatHandsOverEndsWhereTheNextStartsThoughNoUntilIsWritten()
    {
        await using var server = await Start();

        var before = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ash:2", HttpStatusCode.OK);
        AssertNoMark(Field(before, "ward", "Tessaly"));
        AssertMark(Field(before, "ward", "Corvin"), "ahead", "ash:3");

        var after = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ash:3", HttpStatusCode.OK);
        var tessaly = Field(after, "ward", "Tessaly");
        Assert.Equal(JsonValueKind.Null, tessaly.GetProperty("until").ValueKind);
        AssertMark(tessaly, "ended", "ash:3");
        AssertNoMark(Field(after, "ward", "Corvin"));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ASectionThatHandsOverEndsWhereTheNextStarts()
    {
        var path = "universes/starfall/entities/notes/the-reach-gate.md";
        _den.Write(path, "---\ntype: character\nname: The reach gate\nstatus: canon\n---\n\n## Appearance\nRusted.\n\n## Appearance\n<!-- wolfden: from=ash:2 -->\nRebuilt.\n");
        await using var server = await Start();

        var page = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20reach%20gate&marksAt=ash:3", HttpStatusCode.OK);

        var sections = page.GetProperty("entity").GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal("Appearance", sections[0].GetProperty("name").GetString());
        AssertMark(sections[0], "ended", "ash:2");
        AssertNoMark(sections[1]);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task APositionFieldsValuesAheadAreListedOneByOne()
    {
        await using var server = await Start();

        var early = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ember:3", HttpStatusCode.OK);
        Assert.Equal(["ash:2"], Field(early, "planted").GetProperty("aheadValues").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(["ash:3"], Field(early, "paid_off").GetProperty("aheadValues").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(["ash:4"], Field(early, "echoes").GetProperty("aheadValues").EnumerateArray().Select(v => v.GetString()));

        // The field itself holds, so it has no mark of its own, and all of its value is still given.
        AssertNoMark(Field(early, "planted"));
        Assert.Equal(2, Field(early, "planted").GetProperty("value").GetArrayLength());

        // A value at the position isn't later.
        var reached = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ash:3", HttpStatusCode.OK);
        Assert.False(Field(reached, "planted").TryGetProperty("aheadValues", out _));
        Assert.False(Field(reached, "paid_off").TryGetProperty("aheadValues", out _));
        Assert.Equal(["ash:4"], Field(reached, "echoes").GetProperty("aheadValues").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ABareBookIsTheSamePointAsItsFirstChapter()
    {
        _den.Write("universes/starfall/entities/notes/the-toll-bell.md", "---\ntype: thread\nname: The toll bell\nstatus: canon\nfrom: \"ember:1\"\nfields:\n  planted: [\"ash:1\", \"ash:2\"]\n  ringer:\n    - { value: Halloran, from: \"ember:1\", until: \"ash:1\" }\n    - { value: Sorrel, from: \"ash:1\" }\n---\n\nIt rings.\n");
        await using var server = await Start();

        var page = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20toll%20bell&marksAt=ash", HttpStatusCode.OK);

        AssertMark(Field(page, "ringer", "Halloran"), "ended", "ash:1");
        AssertNoMark(Field(page, "ringer", "Sorrel"));
        Assert.Equal(["ash:2"], Field(page, "planted").GetProperty("aheadValues").EnumerateArray().Select(v => v.GetString()));
    }

    // --- Notes. ---

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ANoteReadersMeetLaterIsWholeAndMarkedWhereItEnters()
    {
        await using var server = await Start();
        var page = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20salt%20heir&marksAt=ember:3", HttpStatusCode.OK);

        Assert.True(page.GetProperty("inView").GetBoolean());
        AssertMark(page.GetProperty("entity"), "ahead", "ash:3");
        Assert.Contains("Ysmerine", page.GetRawText(), StringComparison.Ordinal);

        // The note's mark says it for what enters with it. What enters later still has its own.
        AssertNoMark(Section(page, "Early life"));
        AssertNoMark(Field(page, "claim"));
        AssertMark(Section(page, "The crowning"), "ahead", "ash:4");

        // A name the note only has as an alias still finds it, as a full read does.
        var alias = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20heir%20apparent&marksAt=ember:3", HttpStatusCode.OK);
        AssertMark(alias.GetProperty("entity"), "ahead", "ash:3");

        // Once reached, the note has no mark, and only what's still ahead does.
        var met = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20salt%20heir&marksAt=ash:3", HttpStatusCode.OK);
        AssertNoMark(met.GetProperty("entity"));
        AssertNoMark(Section(met, "Early life"));
        AssertMark(Section(met, "The crowning"), "ahead", "ash:4");
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ANoteFiledInALaterBookEntersAtThatBook()
    {
        _den.Write("universes/starfall/series/long-dark/books/ash/entities/locations/tarnhollow.md", "---\ntype: location\nname: Tarnhollow\nstatus: canon\n---\n\nA later place.\n");
        await using var server = await Start();

        var page = await server.GetJsonAsync("/api/entity?universe=starfall&entity=Tarnhollow&marksAt=ember:3", HttpStatusCode.OK);

        AssertMark(page.GetProperty("entity"), "ahead", "ash");
        Assert.Equal("ash", page.GetProperty("entity").GetProperty("entry").GetProperty("position").GetString());
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData("idea")]
    [InlineData("undecided")]
    [InlineData("candidate")]
    [InlineData("retconned")]
    public async Task ANoteOutOfCanonKeepsItsStatusAndGetsNoReaderMarks(string status)
    {
        _den.Write("universes/starfall/entities/notes/the-dry-well.md", $"---\ntype: note\nname: The dry well\nstatus: {status}\nfrom: \"ash:3\"\n---\n\nPerhaps.\n\n## Later\n<!-- wolfden: from=ash:4 -->\nGlimmerfen.\n");
        await using var server = await Start();

        var page = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20dry%20well&marksAt=ember:3", HttpStatusCode.OK);

        Assert.Equal(status, page.GetProperty("entity").GetProperty("status").GetString());
        Assert.Equal("ember:3", page.GetProperty("marksAt").GetString());
        foreach (var key in new[] { "readerMark", "aheadValues" })
        {
            Assert.DoesNotContain(key, Keys(page));
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ANoteAnErrorLeavesOutAnswersAsAFullReadDoes()
    {
        const string oath = "universes/starfall/entities/notes/the-brine-oath.md";
        _den.Write(oath, "---\ntype: note\nname: The brine oath\nstatus: maybe\n---\n\nCorvantis swore it.\n");
        await using var server = await Start();

        var full = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={oath}", HttpStatusCode.NotFound);
        var marked = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={oath}&marksAt=ember:3", HttpStatusCode.NotFound);

        Assert.Equal(full.GetRawText(), marked.GetRawText());
        Assert.Equal(JsonValueKind.Number, marked.GetProperty("leftOut").GetProperty("line").ValueKind);
    }

    // --- What keeps its own badge. ---

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task WhatKeepsItsOwnBadgeGetsNoReaderMark()
    {
        await using var server = await Start();
        var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ember:3", HttpStatusCode.OK);

        // Each of these says a later from, or is under one, and none is a fact a reader meets.
        var notion = Section(page, "Tamsin's notion");
        Assert.Equal("idea", notion.GetProperty("status").GetString());
        AssertNoMark(notion);

        var reveal = Section(page, "Zanthic reveal");
        Assert.Equal(JsonValueKind.Array, reveal.GetProperty("leftOut").ValueKind);
        AssertNoMark(reveal);
        AssertNoMark(Section(page, "Ostrelline"));

        var ending = Section(page, "The ending");
        Assert.Equal("undecided", ending.GetProperty("status").GetString());
        AssertNoMark(ending);

        var notes = Section(page, "Session notes");
        Assert.True(notes.GetProperty("bookkeeping").GetBoolean());
        AssertNoMark(notes);

        AssertNoMark(Field(page, "rumor"));
        AssertNoMark(Field(page, "omen"));
        AssertNoMark(Field(page, "old_name"));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task WhatNoPositionReachesIsMarkedAtEveryPosition()
    {
        WritePlannedNotes();
        await using var server = await Start();

        foreach (var at in new[] { "ember:prologue", "ember:3", "ash:epilogue" })
        {
            var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity=The%20toll%20bell&marksAt={at}", HttpStatusCode.OK);

            // A position that doesn't resolve: a from, an until, a thread's paid_off, and what's under them.
            AssertMark(Section(page, "Payoff"), "unplaced", "dusk:9");
            AssertMark(Section(page, "The last peal"), "unplaced", "dusk:9");
            AssertMark(Section(page, "Planned"), "unplaced", "dusk:2");
            AssertMark(Section(page, "Runs out"), "unplaced", "dusk:3");
            AssertMark(Field(page, "ringer", "Halloran"), "unplaced", "tide:1");
            AssertMark(Field(page, "ringer", "Sorrel"), "unplaced", "ash:99");

            // Positions that resolve, and a range that holds nothing.
            AssertMark(Section(page, "Backwards"), "never", "ash:3");
            AssertMark(Section(page, "Too late"), "never", "ash:3");
            AssertMark(Field(page, "ringer", "Wrenna"), "never", "ash:3");

            // A value that can't be compared, beside one that can.
            var planted = Field(page, "planted");
            Assert.Equal(["ember:99", "7"], planted.GetProperty("unplacedValues").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal(["dusk:9"], Field(page, "paid_off").GetProperty("unplacedValues").EnumerateArray().Select(v => v.GetString()));
        }

        var early = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20toll%20bell&marksAt=ember:3", HttpStatusCode.OK);
        Assert.Equal(["ash:2"], Field(early, "planted").GetProperty("aheadValues").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ANoteNoPositionPlacesHasTheOneMark()
    {
        WritePlannedNotes();
        await using var server = await Start();

        var page = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20lost%20bell&marksAt=ember:3", HttpStatusCode.OK);

        AssertMark(page.GetProperty("entity"), "unplaced", "ash:99");
        AssertNoMark(Section(page, "After"));
        AssertNoMark(Field(page, "keeper"));

        // Out of canon, its status says it instead.
        _den.Write("universes/starfall/entities/notes/the-lost-bell.md", _den.Read("universes/starfall/entities/notes/the-lost-bell.md").Replace("status: canon", "status: idea", StringComparison.Ordinal));
        var idea = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20lost%20bell&marksAt=ember:3", HttpStatusCode.OK);
        Assert.DoesNotContain("readerMark", Keys(idea));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ALaterNotesPlannedFactStillSaysSo()
    {
        _den.Write(HeirPath, _den.Read(HeirPath) + "\n## Planned\n<!-- wolfden: from=dusk:2 -->\nLater still.\n");
        await using var server = await Start();

        var page = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20salt%20heir&marksAt=ember:3", HttpStatusCode.OK);

        AssertMark(page.GetProperty("entity"), "ahead", "ash:3");
        AssertMark(Section(page, "Planned"), "unplaced", "dusk:2");
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ASearchMarksWhatNoPositionReaches()
    {
        WritePlannedNotes();
        await using var server = await Start();

        async Task<JsonElement> Find(string query) => await server.GetJsonAsync($"/api/search?query={query}&marksAt=ember:3", HttpStatusCode.OK);

        AssertMark(Hit(await Find("Clapperbone"), h => InSection(h, "Payoff")), "unplaced", "dusk:9");
        AssertMark(Hit(await Find("Halloran"), h => h.GetProperty("field").GetString() == "ringer"), "unplaced", "tide:1");
        AssertMark(Hit(await Find("Backwardly"), h => InSection(h, "Backwards")), "never", "ash:3");

        // Through the note: its name, its lead, a section, and a field.
        AssertMark((await Find("Lost%20bell")).GetProperty("hits")[0], "unplaced", "ash:99");
        AssertMark(Hit(await Find("Foundry"), h => InSection(h, "After")), "unplaced", "ash:99");
        AssertMark(Hit(await Find("Gorsewick"), h => h.GetProperty("field").GetString() == "keeper"), "unplaced", "ash:99");

        // A position field's value.
        var paidOff = Hit(await Find("dusk%209"), h => h.GetProperty("field").GetString() == "paid_off");
        Assert.Equal(["dusk:9"], paidOff.GetProperty("unplacedValues").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task AMarkNamesAPointAsTheNoteWritesIt()
    {
        _den.Write("universes/starfall/entities/notes/the-toll-bell.md", "---\ntype: thread\nname: The toll bell\nstatus: canon\nfrom: \"ember:1\"\nfields:\n  paid_off: ash\n---\n\nIt rings.\n\n## Payoff\nIt stops.\n");
        await using var server = await Start();

        var page = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20toll%20bell&marksAt=ember:3", HttpStatusCode.OK);

        AssertMark(Section(page, "Payoff"), "ahead", "ash");
    }

    /// <summary>Notes with what no position reaches: planned in a book the den doesn't list, or dated so nothing holds.</summary>
    private void WritePlannedNotes()
    {
        _den.Write(
            "universes/starfall/entities/notes/the-toll-bell.md",
            """
            ---
            type: thread
            name: The toll bell
            status: canon
            from: "ember:1"
            fields:
              planted: ["ember:1", "ember:99", "ash:2", 7]
              paid_off: "dusk:9"
              ringer:
                - { value: Halloran, from: "tide:1" }
                - { value: Sorrel, from: "ash:2", until: "ash:99" }
                - { value: Wrenna, from: "ash:3", until: "ash:1" }
            ---

            It rings.

            ## Payoff
            Clapperbone falls.

            ### The last peal
            Heard once.

            ## Planned
            <!-- wolfden: from=dusk:2 -->
            Not outlined yet.

            ## Runs out
            <!-- wolfden: from=ember:1 until=dusk:3 -->
            Until a chapter not listed.

            ## Backwards
            <!-- wolfden: from=ash:3 until=ash:2 -->
            Backwardly dated.

            ## Short
            <!-- wolfden: from=ember:1 until=ash:1 -->
            Holds a while.

            ### Too late
            <!-- wolfden: from=ash:3 -->
            Starts after its parent ends.

            """);
        _den.Write("universes/starfall/entities/notes/the-lost-bell.md", "---\ntype: note\nname: The lost bell\nstatus: canon\nfrom: \"ash:99\"\nfields:\n  keeper: Gorsewick\n---\n\nIt's gone.\n\n## After\n<!-- wolfden: from=ash:4 -->\nFoundry.\n");
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task AnEntryThatLosesAnOverlapHasNoMark()
    {
        // Neither ahead nor ended, though the view shows the other. F-TMP-5 warns, and the Author's view shows both.
        _den.Write("universes/starfall/entities/notes/the-toll-bell.md", "---\ntype: note\nname: The toll bell\nstatus: canon\nfrom: \"ember:1\"\nfields:\n  ringer:\n    - { value: Halloran, from: \"ember:1\", until: \"ash:3\" }\n    - { value: Sorrel, from: \"ember:2\" }\n---\n\nIt rings.\n");
        await using var server = await Start();

        var page = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20toll%20bell&marksAt=ember:3", HttpStatusCode.OK);
        AssertNoMark(Field(page, "ringer", "Halloran"));
        AssertNoMark(Field(page, "ringer", "Sorrel"));

        var strict = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20toll%20bell&asOf=ember:3", HttpStatusCode.OK);
        Assert.Equal("Sorrel", Field(strict, "ringer").GetProperty("value").GetString());
    }

    // --- The marks and the Reader's view are one rule, at every position. ---

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task AtEveryPositionWhatHasNoMarkIsWhatAReaderSees()
    {
        WritePlannedNotes();
        await using var server = await Start();
        var positions = await server.GetJsonAsync("/api/positions?universe=starfall", HttpStatusCode.OK);

        foreach (var at in positions.GetProperty("positions").EnumerateArray().Select(p => p.GetProperty("position").GetString()!))
        {
            foreach (var name in new[] { Tithe, "The%20salt%20heir", "Ilse%20Tarrow", "The%20missing%20manifest", "The%20toll%20bell", "The%20lost%20bell" })
            {
                var marked = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={name}&marksAt={at}", HttpStatusCode.OK);
                var strict = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={name}&asOf={at}", HttpStatusCode.OK);

                var entity = marked.GetProperty("entity");
                Assert.Equal(strict.GetProperty("inView").GetBoolean(), !entity.TryGetProperty("readerMark", out _));
                if (!strict.GetProperty("inView").GetBoolean())
                {
                    continue;
                }

                var reader = strict.GetProperty("entity");
                Assert.Equal(
                    reader.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("line").GetInt32()).Order(),
                    entity.GetProperty("sections").EnumerateArray().Where(ReachesAReader).Select(s => s.GetProperty("line").GetInt32()).Order());
                Assert.Equal(
                    reader.GetProperty("fields").EnumerateArray().Where(f => f.GetProperty("kind").GetString() != "default").Select(f => f.GetProperty("line").GetInt32()).Order(),
                    entity.GetProperty("fields").EnumerateArray().Where(ReachesAReader).Select(f => f.GetProperty("line").GetInt32()).Order());
            }
        }

        static bool ReachesAReader(JsonElement fact) =>
            !fact.TryGetProperty("readerMark", out _)
            && fact.GetProperty("status").GetString() is "draft" or "canon"
            && !(fact.TryGetProperty("bookkeeping", out var bookkeeping) && bookkeeping.ValueKind == JsonValueKind.True)
            && !(fact.TryGetProperty("leftOut", out var leftOut) && leftOut.ValueKind == JsonValueKind.Array);
    }

    // --- Positions that can't be read. ---

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData("EMBER:3")]
    [InlineData("ember:99")]
    [InlineData("nowhere:1")]
    [InlineData("tide:1")] // A book of another universe.
    public async Task APositionThatCantBeReadIsRefusedAsAsOfRefusesIt(string position)
    {
        await using var server = await Start();

        foreach (var path in new[] { $"/api/entity?universe=starfall&entity={Tithe}", "/api/search?universe=starfall&query=salt" })
        {
            var marked = await server.GetJsonAsync($"{path}&marksAt={position}", HttpStatusCode.BadRequest);
            var strict = await server.GetJsonAsync($"{path}&asOf={position}", HttpStatusCode.BadRequest);

            Assert.Equal("bad_position", marked.GetProperty("code").GetString());
            Assert.Equal(strict.GetProperty("message").GetString(), marked.GetProperty("message").GetString());
            AssertNoMarks(marked);
            foreach (var word in LaterWords)
            {
                Assert.DoesNotContain(word, marked.GetRawText(), StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData("")]
    [InlineData("%20%20")]
    public async Task AnEmptyMarksAtIsRefusedSoItFailsClosed(string position)
    {
        await using var server = await Start();

        foreach (var path in new[] { $"/api/entity?universe=starfall&entity={Tithe}", "/api/search?universe=starfall&query=salt" })
        {
            var answer = await server.GetJsonAsync($"{path}&marksAt={position}", HttpStatusCode.BadRequest);
            Assert.Equal("bad_argument", answer.GetProperty("code").GetString());
            Assert.Contains("'marksAt' is empty", answer.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData("/api/entity?universe=starfall&entity=" + Tithe)]
    [InlineData("/api/search?universe=starfall&query=salt")]
    public async Task AsOfAndMarksAtDontGoTogether(string path)
    {
        await using var server = await Start();

        var answer = await server.GetJsonAsync($"{path}&asOf=ember:3&marksAt=ember:3", HttpStatusCode.BadRequest);

        Assert.Equal("bad_argument", answer.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ThePositionSettlesTheUniverseAndEveryUniverseDoesntGoWithOne()
    {
        await using var server = await Start();

        var page = await server.GetJsonAsync($"/api/entity?entity={Tithe}&marksAt=ember:3", HttpStatusCode.OK);
        Assert.Equal("starfall", page.GetProperty("entity").GetProperty("universe").GetString());

        var refused = await server.GetJsonAsync("/api/search?universe=*&query=salt&marksAt=ember:3", HttpStatusCode.BadRequest);
        Assert.Equal("bad_argument", refused.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task AServerPinnedToAUniverseMarksOnlyInIt()
    {
        await using var server = await Server.StartAsync(new WolfdenApiOptions(_den.Root) { PinnedUniverse = "starfall" });

        var page = await server.GetJsonAsync($"/api/entity?entity={Tithe}&marksAt=ember:3", HttpStatusCode.OK);
        Assert.Equal("ember:3", page.GetProperty("marksAt").GetString());

        Assert.Equal("bad_position", (await server.GetJsonAsync($"/api/entity?entity={Tithe}&marksAt=tide:1", HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        Assert.Equal("bad_argument", (await server.GetJsonAsync($"/api/entity?universe=drift&entity={Tithe}&marksAt=tide:1", HttpStatusCode.BadRequest)).GetProperty("code").GetString());
    }

    // --- Search. ---

    private static JsonElement Hit(JsonElement answer, Func<JsonElement, bool> which) =>
        answer.GetProperty("hits").EnumerateArray().Single(which);

    private static bool InSection(JsonElement hit, string name) =>
        hit.GetProperty("section").ValueKind == JsonValueKind.String && hit.GetProperty("section").GetString() == name;

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ASearchFindsLaterMaterialAndMarksIt()
    {
        await using var server = await Start();

        async Task<JsonElement> Find(string query, string at = "ember:3") =>
            await server.GetJsonAsync($"/api/search?query={query}&marksAt={at}", HttpStatusCode.OK);

        var audit = await Find("Sablewick");
        Assert.Equal("ember:3", audit.GetProperty("marksAt").GetString());
        AssertMark(Hit(audit, h => InSection(h, "The audit")), "ahead", "ash:2");

        // Past its date.
        var terms = await Find("season", "ash:3");
        AssertMark(Hit(terms, h => InSection(h, "Terms")), "ended", "ash:2");

        // A field's value, later and past.
        AssertMark(Hit(await Find("Morvane"), h => h.GetProperty("field").GetString() == "keeper"), "ahead", "ash:2");
        AssertMark(Hit(await Find("Orrin", "ash:2"), h => h.GetProperty("field").GetString() == "keeper"), "ended", "ash:2");
        AssertNoMark(Hit(await Find("Orrin"), h => h.GetProperty("field").GetString() == "keeper"));

        // A position field's value.
        var echoes = Hit(await Find("ash%204", "ember:3"), h => h.GetProperty("field").GetString() == "echoes");
        Assert.Equal(["ash:4"], echoes.GetProperty("aheadValues").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task ASearchMarksANoteReadersMeetLaterWhereverItsFound()
    {
        await using var server = await Start();

        async Task<JsonElement> Find(string query, string at = "ember:3") =>
            await server.GetJsonAsync($"/api/search?query={query}&marksAt={at}", HttpStatusCode.OK);

        // Its name, its lead, a field, and a section that says nothing of its own.
        foreach (var query in new[] { "heir%20apparent", "inherits", "rain", "Raised" })
        {
            var hit = (await Find(query)).GetProperty("hits").EnumerateArray().First(h => h.GetProperty("entity").GetProperty("name").GetString() == "The salt heir");
            AssertMark(hit, "ahead", query == "rain" ? "ash:4" : "ash:3");
        }

        var claim = Hit(await Find("Ysmerine"), h => h.GetProperty("field").ValueKind == JsonValueKind.String);
        AssertMark(claim, "ahead", "ash:3");

        // Once it's met, none of that's left but what's still ahead.
        AssertNoMark((await Find("inherits", "ash:3")).GetProperty("hits")[0]);
        AssertMark((await Find("rain", "ash:3")).GetProperty("hits")[0], "ahead", "ash:4");
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData("Hallowmere", "Tamsin's notion")] // An idea.
    [InlineData("Vespergale", "Zanthic reveal")] // A section an error leaves out.
    [InlineData("Wyrmsedge", "The ending")] // Undecided.
    [InlineData("Pellucid", "Session notes")] // Bookkeeping.
    public async Task ASearchHitInWhatKeepsItsOwnBadgeGetsNoReaderMark(string word, string section)
    {
        await using var server = await Start();

        var answer = await server.GetJsonAsync($"/api/search?query={word}&marksAt=ember:3", HttpStatusCode.OK);

        AssertNoMark(Hit(answer, h => InSection(h, section)));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task AHitInABadgedSectionOfALaterNoteHasNoMarkEither()
    {
        _den.Write(HeirPath, _den.Read(HeirPath) + "\n## Session notes\nThistledown drafts ahead.\n");
        await using var server = await Start();

        var answer = await server.GetJsonAsync("/api/search?query=Thistledown&marksAt=ember:3", HttpStatusCode.OK);

        AssertNoMark(Hit(answer, h => InSection(h, "Session notes")));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task AMarkedSearchFindsWhatAFullSearchOfThatUniverseFinds()
    {
        await using var server = await Start();

        var full = await server.GetJsonAsync("/api/search?universe=starfall&query=salt", HttpStatusCode.OK);
        var marked = await server.GetJsonAsync("/api/search?query=salt&marksAt=ember:3", HttpStatusCode.OK);

        SameAsMcp.Assert(full, marked);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task EntriesOnOneLineEachHaveTheirOwnMarkInASearch()
    {
        _den.Write("universes/starfall/entities/notes/the-toll-bell.md", "---\ntype: note\nname: The toll bell\nstatus: canon\nfrom: \"ember:1\"\nfields:\n  ringer: [{ value: Halloran, from: \"ember:1\", until: \"ash:2\" }, { value: Sorrel, from: \"ash:2\" }]\n---\n\nIt rings.\n");
        await using var server = await Start();

        AssertNoMark((await server.GetJsonAsync("/api/search?query=Halloran&marksAt=ember:3", HttpStatusCode.OK)).GetProperty("hits")[0]);
        AssertMark((await server.GetJsonAsync("/api/search?query=Sorrel&marksAt=ember:3", HttpStatusCode.OK)).GetProperty("hits")[0], "ahead", "ash:2");

        // And on the page.
        var page = await server.GetJsonAsync("/api/entity?universe=starfall&entity=The%20toll%20bell&marksAt=ember:3", HttpStatusCode.OK);
        AssertNoMark(Field(page, "ringer", "Halloran"));
        AssertMark(Field(page, "ringer", "Sorrel"), "ahead", "ash:2");
    }

    // --- The same read through MCP. ---

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData("ember:1")]
    [InlineData("ember:3")]
    public async Task TheSameReadThroughMcpStaysStrict(string position)
    {
        await using var server = await Start();
        var tools = new WolfdenTools(new DenTools(_den.Root));

        var marked = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt={position}", HttpStatusCode.OK);
        Assert.Contains("Morvane", marked.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("readerMark", marked.GetRawText(), StringComparison.Ordinal);

        foreach (var answer in new[]
        {
            Text(tools.GetEntity("The salt tithe", "starfall", position)),
            Text(tools.CanonAsOf(position, "starfall")),
        })
        {
            foreach (var word in LaterWords)
            {
                Assert.DoesNotContain(word, answer, StringComparison.Ordinal);
            }

            foreach (var mark in new[] { "reader_mark", "readerMark", "ahead_values", "aheadValues", "unplaced_values", "unplacedValues", "marks_at", "marksAt", "\"ahead\"", "\"ended\"", "\"unplaced\"", "\"never\"" })
            {
                Assert.DoesNotContain(mark, answer, StringComparison.Ordinal);
            }
        }

        // A note readers meet later says nothing, where the marked read gives all of it.
        using var heir = JsonDocument.Parse(Text(tools.GetEntity("The salt heir", "starfall", position)));
        Assert.False(heir.RootElement.GetProperty("in_view").GetBoolean());
        Assert.DoesNotContain("Ysmerine", heir.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public void TheMcpToolsHaveNoWayToAskForMarks()
    {
        foreach (var method in typeof(WolfdenTools).GetMethods())
        {
            Assert.DoesNotContain(method.GetParameters(), p => p.Name!.Replace("_", "", StringComparison.Ordinal).Contains("mark", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using Wolfden.Index;
using Wolfden.Mcp;

namespace Wolfden.Api.Tests;

/// <summary>
/// Search in the Author's view and the Reader's view (#300). <c>readers=true</c> finds a
/// word exactly when an AI assistant's <c>get_entity</c> at that position shows it, and
/// <c>marksAt</c> finds everything, what a reader can find first.
/// </summary>
public sealed class ReadersSearchTests : IDisposable
{
    private const string LedgerPath = "universes/starfall/entities/notes/the-word-ledger.md";
    private const string CrownPath = "universes/starfall/entities/notes/the-word-crown.md";
    private const string DreamPath = "universes/starfall/entities/notes/the-word-dream.md";
    private const string TallyPath = "universes/starfall/entities/threads/the-word-tally.md";

    private const string LedgerText = """
        ---
        type: note
        name: The word ledger
        aliases: [Wordkeep Alderquill]
        status: canon
        from: "ember:1"
        fields:
          steady: Bramblewick
          ended: { value: Copperlark, from: "ember:1", until: "ash:2" }
          coming: { value: Dunmoth, from: "ash:2" }
          dreamt: { value: Eastvane, status: idea }
          crown: "[[The word crown]]"
          dream: "[[The word dream]]"
        ---

        # The word ledger

        Kept with Gildersong. <!-- Fenwhistle is only in the lead's comment. -->

        ## Entries
        Inkwater is written. <!-- Hollowmere is only in this comment. -->

        ## Never closed
        Jessamine is plain. <!-- Kestrelmoor is in a comment nothing closes.

        ## Heading <!-- Lanternfly --> words
        Mothgrave is under it.

        ## Later part
        <!-- wolfden: from=ash:2 -->
        Nettlebloom comes later.

        ### Later sub
        Oakenshade comes later too.

        ## Ended part
        <!-- wolfden: from=ember:1 until=ash:2 -->
        Pinfeather was true.

        ## Planned end
        <!-- wolfden: from=ember:1 until=ash:4 -->
        Quillmere is still true.

        ## Session notes
        Rookfallow is bookkeeping.

        ## Idea part
        <!-- wolfden: status=idea -->
        Sedgewine might be.

        """;

    private const string CrownText = """
        ---
        type: note
        name: The word crown
        aliases: [Thornveil Umberly]
        status: canon
        from: "ash:3"
        fields:
          heir: Vinebright
        ---

        # The word crown

        Wrenhaven holds it.

        ## Early life
        Yarrowdeep raised it.

        """;

    private const string DreamText = """
        ---
        type: note
        name: The word dream
        aliases: [Zephyrlode]
        status: idea
        ---

        # The word dream

        Amberwhit is an idea.

        """;

    private const string TallyText = """
        ---
        type: thread
        name: The word tally
        status: canon
        from: "ember:1"
        fields:
          planted: ["ember:1", "ash:2", "bellglass:9"]
          paid_off: "ash:3"
          echoes: "bellglass:9"
        ---

        # The word tally

        Crumbleford keeps count.

        """;

    /// <summary>One word from each kind of text in the notes above.</summary>
    private static readonly string[] Words =
    [
        "Alderquill", "Bramblewick", "Copperlark", "Dunmoth", "Eastvane", "Gildersong", "Fenwhistle", "Inkwater", "Hollowmere", "Jessamine",
        "Kestrelmoor", "Lanternfly", "Mothgrave", "Nettlebloom", "Oakenshade", "Pinfeather", "Quillmere", "Rookfallow", "Sedgewine", "Thornveil",
        "Vinebright", "Wrenhaven", "Yarrowdeep", "Zephyrlode", "Amberwhit", "Crumbleford", "ash:2", "ash:3", "bellglass:9", "Umberly",
    ];

    private readonly TempDen _den = TempDen.Starfall();

    public ReadersSearchTests()
    {
        _den.Write("packs/starfall/extensions/thread.yaml", "fields:\n  echoes: { type: position }\n");
        _den.Write(LedgerPath, LedgerText);
        _den.Write(CrownPath, CrownText);
        _den.Write(DreamPath, DreamText);
        _den.Write(TallyPath, TallyText);
    }

    public void Dispose() => _den.Dispose();

    private Task<Server> Start() => Server.StartAsync(new WolfdenApiOptions(_den.Root));

    private static string Readers(string query, string at, string more = "") =>
        $"/api/search?universe=starfall&query={Uri.EscapeDataString(query)}&asOf={at}&readers=true&limit=200{more}";

    private static string Marked(string query, string at, string more = "") =>
        $"/api/search?universe=starfall&query={Uri.EscapeDataString(query)}&marksAt={at}&limit=200{more}";

    private static List<JsonElement> Hits(JsonElement answer) => answer.GetProperty("hits").EnumerateArray().ToList();

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    /// <summary>All an assistant's <c>get_entity</c> at <paramref name="at"/> shows of a note that a search can look at: its name, lead, written field values, and sections.</summary>
    private static List<string> ShownBy(WolfdenTools tools, string path, string at)
    {
        using var read = JsonDocument.Parse(Text(tools.GetEntity(path, "starfall", at)));
        if (!read.RootElement.GetProperty("in_view").GetBoolean())
        {
            return [];
        }

        var entity = read.RootElement.GetProperty("entity");
        var shown = new List<string> { entity.GetProperty("name").GetString()!, entity.GetProperty("lead").GetString() ?? "" };
        foreach (var field in entity.GetProperty("fields").EnumerateArray().Where(f => f.GetProperty("kind").GetString() != "default"))
        {
            var value = field.GetProperty("value");
            shown.Add(value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText());
        }

        foreach (var section in entity.GetProperty("sections").EnumerateArray())
        {
            shown.Add(section.GetProperty("name").GetString()!);
            shown.Add(section.GetProperty("body").GetString()!);
        }

        return shown;
    }

    /// <summary>The paths of every note, whatever its status.</summary>
    private static async Task<List<string>> Notes(Server server)
    {
        var list = await server.GetJsonAsync("/api/entities?universe=starfall&limit=1000", HttpStatusCode.OK);
        return list.GetProperty("entities").EnumerateArray().Select(e => e.GetProperty("path").GetString()!).ToList();
    }

    /// <summary>
    /// What the Reader's page of a note shows of its text and fields: each link is replaced
    /// as <c>Links.tsx</c> replaces it, by its label, else the note it reaches, else its target.
    /// </summary>
    private static async Task<List<string>> PageShows(Server server, string path, string at)
    {
        var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Uri.EscapeDataString(path)}&asOf={at}&readers=true", HttpStatusCode.OK);
        if (!page.GetProperty("inView").GetBoolean())
        {
            return [];
        }

        static string Shown(string text, JsonElement links)
        {
            foreach (var link in links.EnumerateArray())
            {
                var shown = link.GetProperty("label").GetString()
                    ?? (link.GetProperty("entity").ValueKind == JsonValueKind.Object ? link.GetProperty("entity").GetProperty("name").GetString() : null)
                    ?? link.GetProperty("target").GetString()!;
                text = text.Replace(link.GetProperty("text").GetString()!, shown, StringComparison.Ordinal);
            }

            return text;
        }

        var entity = page.GetProperty("entity");
        var shown = new List<string> { entity.GetProperty("name").GetString()!, Shown(entity.GetProperty("lead").GetString() ?? "", entity.GetProperty("leadLinks")) };
        foreach (var field in entity.GetProperty("fields").EnumerateArray().Where(f => f.GetProperty("kind").GetString() != "default"))
        {
            var value = field.GetProperty("value");
            shown.Add(Shown(value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText(), field.GetProperty("links")));
        }

        foreach (var section in entity.GetProperty("sections").EnumerateArray())
        {
            shown.Add(section.GetProperty("name").GetString()!);
            shown.Add(Shown(section.GetProperty("body").GetString()!, section.GetProperty("links")));
        }

        return shown;
    }

    private static bool Holds(string word, IEnumerable<string> texts)
    {
        var words = SearchWords.Parse(word);
        return texts.Any(t => words.Find(t).All(f => f.Count > 0));
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-106")]
    [InlineData("ember:3")]
    [InlineData("ash:2")]
    [InlineData("ash:3")]
    public async Task AReadersSearchFindsAWordExactlyWhenGetEntityAtThatPositionShowsIt(string at)
    {
        await using var server = await Start();
        var tools = new WolfdenTools(new DenTools(_den.Root));
        var shown = (await Notes(server)).SelectMany(path => ShownBy(tools, path, at)).ToList();
        var hidden = Words.Where(w => !Holds(w, shown)).ToList();

        foreach (var word in Words)
        {
            var answer = await server.GetJsonAsync(Readers(word, at), HttpStatusCode.OK);
            Assert.True(
                Holds(word, shown) == Hits(answer).Count > 0,
                $"'{word}' at {at}: get_entity {(Holds(word, shown) ? "shows it" : "doesn't")}, and the search has {Hits(answer).Count} hits: {answer.GetRawText()}");

            // A hit's text says nothing of what's hidden, whatever it found.
            foreach (var other in hidden)
            {
                Assert.DoesNotContain(other, answer.GetRawText().Replace(word, "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
            }
        }

        // The test is only worth something if both kinds were there.
        Assert.Contains("Hollowmere", hidden);
        Assert.Contains("Inkwater", Words.Except(hidden));
    }

    /// <summary>Every word of every note, frontmatter and comments too, part way through the story, rather than only words put there for this.</summary>
    [Fact]
    [Trait("Scenario", "SC-WEB-106")]
    public async Task EveryWordOfEveryNoteIsFoundExactlyWhenGetEntityShowsIt()
    {
        const string at = "ash:1";
        await using var server = await Start();
        var tools = new WolfdenTools(new DenTools(_den.Root));
        var notes = await Notes(server);
        var shown = notes.SelectMany(path => ShownBy(tools, path, at)).ToList();
        var vocabulary = notes
            .SelectMany(path => Regex.Matches(_den.Read(path), @"\p{L}[\p{L}\p{N}]+").Select(m => m.Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(vocabulary.Count > 200, $"Only {vocabulary.Count} words.");

        var wrong = new List<string>();
        foreach (var word in vocabulary)
        {
            var found = Hits(await server.GetJsonAsync(Readers(word, at), HttpStatusCode.OK)).Count > 0;
            if (found != Holds(word, shown))
            {
                wrong.Add($"{word}: search {found}, get_entity {Holds(word, shown)}");
            }
        }

        Assert.Empty(wrong);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-106")]
    [InlineData("Hollowmere")] // A section's comment.
    [InlineData("Fenwhistle")] // The lead's comment.
    [InlineData("Kestrelmoor")] // A comment nothing closes.
    [InlineData("Lanternfly")] // A comment in a heading.
    public async Task AWordOnlyInACommentGivesNoHitThoughTheAuthorsViewFindsItInOne(string word)
    {
        await using var server = await Start();

        var readers = await server.GetJsonAsync(Readers(word, "ember:1"), HttpStatusCode.OK);
        Assert.Empty(Hits(readers));

        var hit = Assert.Single(Hits(await server.GetJsonAsync(Marked(word, "ember:1"), HttpStatusCode.OK)));
        Assert.True(hit.GetProperty("inComment").GetBoolean());
        Assert.False(hit.GetProperty("met").GetBoolean());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-106")]
    public async Task AHitNextToACommentDoesntShowItAndAHeadingReadsWithoutOne()
    {
        await using var server = await Start();

        foreach (var (word, hidden) in new[] { ("Inkwater", "Hollowmere"), ("Gildersong", "Fenwhistle"), ("Jessamine", "Kestrelmoor") })
        {
            var hit = Assert.Single(Hits(await server.GetJsonAsync(Readers(word, "ember:1"), HttpStatusCode.OK)));
            Assert.Contains(word, hit.GetProperty("text").GetString()!, StringComparison.Ordinal);
            Assert.DoesNotContain(hidden, hit.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("<!--", hit.GetRawText(), StringComparison.Ordinal);
        }

        var heading = Assert.Single(Hits(await server.GetJsonAsync(Readers("Mothgrave", "ember:1"), HttpStatusCode.OK)));
        Assert.Equal("Heading words", heading.GetProperty("section").GetString());

        // The words either side of the comment still find it.
        var byHeading = Assert.Single(Hits(await server.GetJsonAsync(Readers("\"Heading\" words", "ember:1"), HttpStatusCode.OK)));
        Assert.Equal("Heading words", byHeading.GetProperty("section").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-106")]
    public async Task ALaterPositionValueNeitherMatchesNorShowsAndAReachedOneDoes()
    {
        await using var server = await Start();

        Assert.Empty(Hits(await server.GetJsonAsync(Readers("ash:3", "ember:1"), HttpStatusCode.OK)));
        var hit = Assert.Single(Hits(await server.GetJsonAsync(Readers("ember:1", "ember:1", "&field=planted"), HttpStatusCode.OK)));
        Assert.Equal("ember:1", hit.GetProperty("text").GetString());

        // A value no position reaches never shows.
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("bellglass", "a-quiet-moon:1"), HttpStatusCode.OK)));

        var reached = await server.GetJsonAsync(Readers("ash:3", "ash:3", "&field=paid_off"), HttpStatusCode.OK);
        Assert.Equal("ash:3", Assert.Single(Hits(reached)).GetProperty("text").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-106")]
    public async Task ALaterUntilDoesntShowAsTextOnASectionOrEntry()
    {
        await using var server = await Start();

        var section = Assert.Single(Hits(await server.GetJsonAsync(Readers("Quillmere", "ember:1"), HttpStatusCode.OK)));
        Assert.DoesNotContain("ash", section.GetProperty("text").GetString()!, StringComparison.Ordinal);
        var entry = Assert.Single(Hits(await server.GetJsonAsync(Readers("Copperlark", "ember:1"), HttpStatusCode.OK)));
        Assert.DoesNotContain("ash", entry.GetRawText().Replace(entry.GetProperty("entity").GetRawText(), "", StringComparison.Ordinal), StringComparison.Ordinal);

        // The bare position in its marker is a comment's, so it finds nothing here.
        Assert.DoesNotContain(
            Hits(await server.GetJsonAsync(Readers("ash:4", "ember:1"), HttpStatusCode.OK)),
            h => h.GetProperty("entity").GetProperty("name").GetString() == "The word ledger");
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-106")]
    [InlineData("Thornveil")] // A later note's alias, through a link in a value readers can see.
    [InlineData("Zephyrlode")] // An idea's alias.
    [InlineData("Alderquill")] // A met note's own alias.
    public async Task ANameFromANoteReadersHaventMetMatchesOnlyInTheAuthorsViewWhichTagsIt(string alias)
    {
        await using var server = await Start();

        Assert.Empty(Hits(await server.GetJsonAsync(Readers(alias, "ember:1"), HttpStatusCode.OK)));

        if (alias == "Alderquill")
        {
            var own = Hits(await server.GetJsonAsync(Marked(alias, "ember:1"), HttpStatusCode.OK)).Single();
            Assert.False(own.GetProperty("met").GetBoolean());
            Assert.Equal("alias", own.GetProperty("hiddenIn").GetString());
            return;
        }

        var hit = Hits(await server.GetJsonAsync(Marked(alias, "ember:1", "&field=" + (alias == "Thornveil" ? "crown" : "dream")), HttpStatusCode.OK)).Single();
        Assert.Contains(alias, hit.GetProperty("text").GetString()!, StringComparison.Ordinal);
        Assert.False(hit.GetProperty("met").GetBoolean());
        var later = hit.GetProperty("laterLink");
        if (alias == "Thornveil")
        {
            Assert.Equal("ahead", later.GetProperty("readerMark").GetProperty("kind").GetString());
            Assert.Equal("ash:3", later.GetProperty("readerMark").GetProperty("at").GetString());
        }
        else
        {
            Assert.Equal("idea", later.GetProperty("status").GetString());
            Assert.False(later.TryGetProperty("readerMark", out _));
        }

        // Once readers have met it, its alias still doesn't match, and neither view says it does.
        var met = Hits(await server.GetJsonAsync(Marked("Thornveil", "ash:3", "&field=crown"), HttpStatusCode.OK)).Single();
        Assert.False(met.TryGetProperty("laterLink", out _));
        Assert.False(met.GetProperty("met").GetBoolean());
        Assert.Equal("alias", met.GetProperty("hiddenIn").GetString());
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("Thornveil", "ash:3"), HttpStatusCode.OK)));
    }

    /// <summary>A note with labelled links to a note met later and one met already, in text, a field, and code.</summary>
    private void WriteLabelledLinks() => _den.Write(
        "universes/starfall/entities/notes/the-word-pilot.md",
        "---\ntype: note\nname: The word pilot\nstatus: canon\nfrom: \"ember:1\"\nfields:\n" +
        "  guide: \"[[The word crown|the silent keeper]]\"\n  friend: \"[[The word ledger|the old book]]\"\n  plain: \"[[The word crown]]\"\n---\n\n" +
        "Nearby opening words, led by [[The word crown|the quiet regent]].\n\n" +
        "## Nearby\nGlassmoth stands by [[The word crown#Early life|a stranger]] and [[The word ledger|that dusty thing]]. In code: `[[Sparrowgate|Silkrun]]`.\n");

    [Theory]
    [Trait("Scenario", "SC-WEB-106")]
    [InlineData("ember:3")]
    [InlineData("ash:3")]
    public async Task ALabelledLinkIsOnlyItsLabelAsOnTheReadersPage(string at)
    {
        WriteLabelledLinks();
        await using var server = await Start();
        var shown = (await Notes(server)).Select(path => PageShows(server, path, at)).SelectMany(t => t.Result).ToList();

        // A word only in the target of a labelled link, or its heading, isn't there, and the labels are.
        foreach (var word in new[] { "silent", "keeper", "quiet", "regent", "stranger", "dusty", "old", "Glassmoth", "Sparrowgate", "Silkrun", "crown", "Early" })
        {
            Assert.True(
                Holds(word, shown) == Hits(await server.GetJsonAsync(Readers(word, at), HttpStatusCode.OK)).Count > 0,
                $"'{word}' at {at}: the page {(Holds(word, shown) ? "shows it" : "doesn't")}.");
        }

        // A search that lands near a link shows only its label.
        var near = Assert.Single(Hits(await server.GetJsonAsync(Readers("Glassmoth", at), HttpStatusCode.OK)));
        Assert.Contains("a stranger", near.GetProperty("text").GetString()!, StringComparison.Ordinal);
        Assert.DoesNotContain("[[", near.GetProperty("text").GetString()!.Replace("`[[Sparrowgate|Silkrun]]`", "", StringComparison.Ordinal), StringComparison.Ordinal);

        var field = Assert.Single(Hits(await server.GetJsonAsync(Readers("silent", at), HttpStatusCode.OK)));
        Assert.Equal("the silent keeper", field.GetProperty("text").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-106")]
    public async Task ALabelledLinksTargetOnlyHidesItInTheReadersSearchAndTheAuthorsViewSaysSo()
    {
        WriteLabelledLinks();
        _den.Write(
            "universes/starfall/entities/notes/the-word-secret.md",
            "---\ntype: note\nname: The word secret\nstatus: canon\nfrom: \"ember:1\"\n---\n\nHe is [[Thistlemere|the visitor]].\n\n## Later on\nAlso [[Thistlemere|someone]].\n");
        await using var server = await Start();

        Assert.Empty(Hits(await server.GetJsonAsync(Readers("Thistlemere", "ember:1"), HttpStatusCode.OK)));
        var hits = Hits(await server.GetJsonAsync(Marked("Thistlemere", "ember:1"), HttpStatusCode.OK));
        Assert.Equal(2, hits.Count);
        foreach (var hit in hits)
        {
            Assert.False(hit.GetProperty("met").GetBoolean());
            Assert.Equal("linkTarget", hit.GetProperty("hiddenIn").GetString());
            Assert.False(hit.GetProperty("inComment").GetBoolean());
        }

        // A met note's name still finds a link to it in a value, labelled or not, in both views.
        var friend = Assert.Single(Hits(await server.GetJsonAsync(Readers("ledger", "ember:1", "&field=friend"), HttpStatusCode.OK)));
        Assert.Equal("the old book → The word ledger", friend.GetProperty("text").GetString());
        Assert.True(Hits(await server.GetJsonAsync(Marked("ledger", "ember:1", "&field=friend"), HttpStatusCode.OK)).Single().GetProperty("met").GetBoolean());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-107")]
    public async Task AHitAReaderFindsByALinkAsWrittenIsMetAndKeepsItsTag()
    {
        await using var server = await Start();

        // `crown: [[The word crown]]`, whose note readers meet later.
        Assert.Single(Hits(await server.GetJsonAsync(Readers("crown", "ember:1", "&field=crown"), HttpStatusCode.OK)));
        var hit = Hits(await server.GetJsonAsync(Marked("crown", "ember:1", "&field=crown"), HttpStatusCode.OK)).Single();

        Assert.True(hit.GetProperty("met").GetBoolean());
        Assert.Equal("ahead", hit.GetProperty("laterLink").GetProperty("readerMark").GetProperty("kind").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-107")]
    public async Task FieldValuesAreReadFromMetNotesFirstAndOverTheTextLimitToo()
    {
        for (var i = 0; i < 1010; i++)
        {
            _den.Write($"universes/starfall/entities/notes/a-crowd-{i}.md", $"---\ntype: note\nname: Quaywhistle {i}\nstatus: canon\nfrom: \"ash:3\"\nfields:\n  role: Ferrowmist\n---\n\nQuaywhistle.\n");
        }

        // Filed last by path, and a text word as common as the later notes' names.
        _den.Write("universes/starfall/entities/notes/z-met-one.md", "---\ntype: note\nname: The met one\nstatus: canon\nfrom: \"ember:1\"\nfields:\n  role: Ferrowmist\n  ship: Quaywhistle\n---\n\nNothing here.\n");
        await using var server = await Start();

        foreach (var path in new[] { Marked("Ferrowmist", "ember:1", "&field=role"), Marked("Quaywhistle", "ember:1") })
        {
            var hits = Hits(await server.GetJsonAsync(path, HttpStatusCode.OK));
            Assert.Equal("The met one", hits[0].GetProperty("entity").GetProperty("name").GetString());
            Assert.True(hits[0].GetProperty("met").GetBoolean());
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-108")]
    public async Task AMadeUpBookInAReadersSearchNamesNoOtherBook()
    {
        await using var server = await Start();

        var refused = await server.GetJsonAsync(Readers("word", "ember:1", "&book=madeup"), HttpStatusCode.NotFound);

        Assert.DoesNotContain("ash", refused.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-106")]
    public async Task ATypesDefaultIsNotSearchedThoughGetEntityShowsIt()
    {
        _den.Write("packs/starfall/extensions/thread.yaml", "fields:\n  echoes: { type: position }\n  mood: { type: text, default: Silverbrack }\n");
        await using var server = await Start();
        var tools = new WolfdenTools(new DenTools(_den.Root));

        Assert.Contains("Silverbrack", Text(tools.GetEntity("The word tally", "starfall", "ember:1")), StringComparison.Ordinal);
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("Silverbrack", "ember:1"), HttpStatusCode.OK)));
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-107")]
    [InlineData("ember:1")]
    [InlineData("ember:3")]
    [InlineData("ash:2")]
    [InlineData("ash:4")]
    public async Task WhatTheAuthorsViewPutsFirstIsWhatAReadersSearchFinds(string at)
    {
        await using var server = await Start();

        foreach (var query in Words.Append("word").Append("salt").Append("the"))
        {
            static string Place(JsonElement h) =>
                $"{h.GetProperty("entity").GetProperty("path").GetString()}|{h.GetProperty("line")}|{h.GetProperty("field")}";

            var readers = Hits(await server.GetJsonAsync(Readers(query, at), HttpStatusCode.OK)).Select(Place).ToHashSet();
            var marked = Hits(await server.GetJsonAsync(Marked(query, at), HttpStatusCode.OK));

            // Everything a reader finds is there, with no later value it can't show
            // listed as met, and what's met is what they find.
            var authors = marked.Select(Place).ToHashSet();
            Assert.Subset(authors, readers);
            var met = marked.Where(h => h.GetProperty("met").GetBoolean()).Select(Place).ToHashSet();
            Assert.Subset(readers, met);

            // Met first, however the ranks fall.
            var flags = marked.Select(h => h.GetProperty("met").GetBoolean()).ToList();
            Assert.Equal(flags.OrderByDescending(f => f), flags);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-107")]
    public async Task AMarkedHitsTagsSayWhyItsAfterTheLine()
    {
        await using var server = await Start();

        var later = Hits(await server.GetJsonAsync(Marked("Wrenhaven", "ember:1"), HttpStatusCode.OK)).Single();
        Assert.False(later.GetProperty("met").GetBoolean());
        Assert.Equal("ahead", later.GetProperty("noteMark").GetProperty("kind").GetString());

        var value = Hits(await server.GetJsonAsync(Marked("ember:1", "ember:1", "&field=planted"), HttpStatusCode.OK)).Single();
        Assert.False(value.GetProperty("met").GetBoolean());
        Assert.Equal(["ash:2"], value.GetProperty("aheadValues").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(["bellglass:9"], value.GetProperty("unplacedValues").EnumerateArray().Select(v => v.GetString()));

        var plain = Hits(await server.GetJsonAsync(Marked("Inkwater", "ember:1"), HttpStatusCode.OK)).Single();
        Assert.True(plain.GetProperty("met").GetBoolean());
        Assert.False(plain.TryGetProperty("noteMark", out _));
        Assert.False(plain.TryGetProperty("readerMark", out _));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-107")]
    public async Task ALimitCutsWhatReadersCantFindBeforeWhatTheyCan()
    {
        // Twenty later notes, each a better match by name than the one note readers have met.
        for (var i = 0; i < 20; i++)
        {
            _den.Write($"universes/starfall/entities/notes/later-{i}.md", $"---\ntype: note\nname: Copperwhin {i}\nstatus: canon\nfrom: \"ash:3\"\n---\n\nCopperwhin Copperwhin.\n");
        }

        _den.Write("universes/starfall/entities/notes/met-one.md", "---\ntype: note\nname: The met one\nstatus: canon\nfrom: \"ember:1\"\n---\n\nSome long text before it finally says copperwhin once.\n");
        await using var server = await Start();

        var answer = await server.GetJsonAsync("/api/search?universe=starfall&query=Copperwhin&marksAt=ember:1&limit=5", HttpStatusCode.OK);

        Assert.Equal(5, answer.GetProperty("count").GetInt32());
        Assert.True(answer.GetProperty("truncated").GetBoolean());
        Assert.Equal("The met one", Hits(answer)[0].GetProperty("entity").GetProperty("name").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-107")]
    public async Task AWordInMoreNotesThanAreReadStillFindsWhatReadersHaveMet()
    {
        // More later hits, all better matches, than the 1,000 a marked search reads.
        for (var i = 0; i < 1010; i++)
        {
            _den.Write($"universes/starfall/entities/notes/crowd-{i}.md", $"---\ntype: note\nname: Tamberlode {i}\nstatus: canon\nfrom: \"ash:3\"\n---\n\nTamberlode.\n");
        }

        _den.Write("universes/starfall/entities/notes/met-one.md", "---\ntype: note\nname: The met one\nstatus: canon\nfrom: \"ember:1\"\n---\n\nA long text that names tamberlode only once, far from anything else of note.\n");
        await using var server = await Start();

        var readers = await server.GetJsonAsync(Readers("Tamberlode", "ember:1"), HttpStatusCode.OK);
        Assert.Equal("The met one", Assert.Single(Hits(readers)).GetProperty("entity").GetProperty("name").GetString());

        var marked = await server.GetJsonAsync(Marked("Tamberlode", "ember:1"), HttpStatusCode.OK);
        Assert.Equal("The met one", Hits(marked)[0].GetProperty("entity").GetProperty("name").GetString());
        Assert.True(Hits(marked)[0].GetProperty("met").GetBoolean());
        Assert.True(marked.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-108")]
    [InlineData("/api/search?universe=starfall&query=salt&readers=true")]
    [InlineData("/api/search?universe=starfall&query=salt&marksAt=ember:1&readers=true")]
    [InlineData("/api/search?universe=starfall&query=salt&asOf=ember:1&marksAt=ember:1&readers=true")]
    [InlineData("/api/fields?universe=starfall&readers=true")]
    public async Task ReadersGoesOnlyWithAsOf(string path)
    {
        await using var server = await Start();

        var refused = await server.GetJsonAsync(path, HttpStatusCode.BadRequest);

        Assert.Equal("bad_argument", refused.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-108")]
    public async Task WithoutTheNewParametersASearchIsAsItWas()
    {
        await using var server = await Start();

        foreach (var path in new[] { "/api/search?universe=starfall&query=word", "/api/search?universe=starfall&query=word&asOf=ember:1" })
        {
            var answer = await server.GetJsonAsync(path, HttpStatusCode.OK);
            foreach (var member in new[] { "marksAt", "readerMark", "noteMark", "laterLink", "met", "aheadValues", "unplacedValues", "linkedNotes", "hiddenIn" })
            {
                Assert.DoesNotContain($"\"{member}\"", answer.GetRawText(), StringComparison.Ordinal);
            }

            var same = await server.GetJsonAsync(path + "&readers=false", HttpStatusCode.OK);
            Assert.Equal(answer.GetRawText(), same.GetRawText());
        }

        // asOf alone still finds comments, as the writer's read it has always been.
        Assert.NotEmpty(Hits(await server.GetJsonAsync("/api/search?universe=starfall&query=Hollowmere&asOf=ember:1", HttpStatusCode.OK)));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-108")]
    public async Task TheFieldFilterNamesOnlyFieldsWithAValueAReaderCanSee()
    {
        await using var server = await Start();

        static IEnumerable<string> Names(JsonElement a) => a.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!);

        var readers = Names(await server.GetJsonAsync("/api/fields?universe=starfall&asOf=ember:1&readers=true", HttpStatusCode.OK)).ToList();
        Assert.Contains("steady", readers);
        Assert.Contains("planted", readers);
        Assert.DoesNotContain("paid_off", readers);
        Assert.DoesNotContain("echoes", readers);
        Assert.DoesNotContain("coming", readers);

        // A field only with later values is refused as a made-up one is.
        var later = await server.GetJsonAsync(Readers("ash", "ember:1", "&field=echoes"), HttpStatusCode.BadRequest);
        var madeUp = await server.GetJsonAsync(Readers("ash", "ember:1", "&field=madeup"), HttpStatusCode.BadRequest);
        Assert.Equal(later.GetProperty("message").GetString()!.Replace("echoes", "madeup", StringComparison.Ordinal), madeUp.GetProperty("message").GetString());

        // The Author's view asks with no position, as it finds everything.
        Assert.Contains("paid_off", Names(await server.GetJsonAsync("/api/fields?universe=starfall", HttpStatusCode.OK)));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-108")]
    public async Task AReadersSearchTakesTheFiltersAPlainOneDoes()
    {
        await using var server = await Start();

        Assert.Single(Hits(await server.GetJsonAsync(Readers("Inkwater", "ember:1", "&type=note"), HttpStatusCode.OK)));
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("Inkwater", "ember:1", "&type=thread"), HttpStatusCode.OK)));
        Assert.Single(Hits(await server.GetJsonAsync(Readers("Inkwater", "ember:1", "&status=canon"), HttpStatusCode.OK)));
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("Inkwater", "ember:1", "&status=draft"), HttpStatusCode.OK)));
        Assert.Single(Hits(await server.GetJsonAsync(Readers("Inkwater", "ember:1", "&canonOnly=true"), HttpStatusCode.OK)));

        var cut = await server.GetJsonAsync($"/api/search?universe=starfall&query=word&asOf=ember:1&readers=true&limit=1", HttpStatusCode.OK);
        Assert.Equal(1, cut.GetProperty("count").GetInt32());
        Assert.True(cut.GetProperty("truncated").GetBoolean());
        var refused = await server.GetJsonAsync($"/api/search?universe=*&query=word&asOf=ember:1&readers=true", HttpStatusCode.BadRequest);
        Assert.Equal("bad_argument", refused.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-106")]
    public async Task MatchedWordsAreMarkedWhereTheTextIs()
    {
        await using var server = await Start();

        var hit = Assert.Single(Hits(await server.GetJsonAsync(Readers("Inkwater", "ember:1"), HttpStatusCode.OK)));
        var text = hit.GetProperty("text").GetString()!;
        var match = Assert.Single(hit.GetProperty("matches").EnumerateArray().ToList());

        Assert.Equal("Inkwater", text.Substring(match.GetProperty("start").GetInt32(), match.GetProperty("length").GetInt32()));
        Assert.Equal("[Inkwater] is written.", hit.GetProperty("snippet").GetString());
    }
}

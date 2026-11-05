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
                var label = link.GetProperty("label").GetString();
                var shown = label
                    ?? (link.GetProperty("entity").ValueKind == JsonValueKind.Object ? link.GetProperty("entity").GetProperty("name").GetString() : null)
                    ?? link.GetProperty("target").GetString()!;
                var heading = link.GetProperty("heading").GetString();
                text = text.Replace(link.GetProperty("text").GetString()!, heading is not null && label is null ? $"{shown}: {heading}" : shown, StringComparison.Ordinal);
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

    /// <summary>What the Reader's pages of every note show at <paramref name="at"/>.</summary>
    private static async Task<List<string>> AllPagesShow(Server server, string at)
    {
        var shown = new List<string>();
        foreach (var path in await Notes(server))
        {
            shown.AddRange(await PageShows(server, path, at));
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
    public async Task AReadersSearchFindsAWordExactlyWhenTheReadersPageShowsIt(string at)
    {
        await using var server = await Start();
        var shown = await AllPagesShow(server, at);
        var hidden = Words.Where(w => !Holds(w, shown)).ToList();

        foreach (var word in Words)
        {
            var answer = await server.GetJsonAsync(Readers(word, at), HttpStatusCode.OK);
            Assert.True(
                Holds(word, shown) == Hits(answer).Count > 0,
                $"'{word}' at {at}: the page {(Holds(word, shown) ? "shows it" : "doesn't")}, and the search has {Hits(answer).Count} hits: {answer.GetRawText()}");

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
    public async Task EveryWordOfEveryNoteIsFoundExactlyWhenTheReadersPageShowsIt()
    {
        const string at = "ash:1";
        await using var server = await Start();
        var notes = await Notes(server);
        var shown = await AllPagesShow(server, at);
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
                wrong.Add($"{word}: search {found}, page {Holds(word, shown)}");
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
        var shown = await AllPagesShow(server, at);

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

        // A labelled link in a value isn't found by its note's name, even one readers have met, so a label isn't tied to a name.
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("ledger", "ember:1", "&field=friend"), HttpStatusCode.OK)));
        var friend = Hits(await server.GetJsonAsync(Marked("ledger", "ember:1", "&field=friend"), HttpStatusCode.OK)).Single();
        Assert.False(friend.GetProperty("met").GetBoolean());
        Assert.Equal("linkTarget", friend.GetProperty("hiddenIn").GetString());
    }

    /// <summary>A met note and one met later, each linked by its alias, by its key, and with a heading, in a met note's text and fields.</summary>
    private void WriteLinksByOtherNames()
    {
        _den.Write(
            "universes/starfall/entities/notes/met-friend.md",
            "---\ntype: note\nname: Metnamed Friend\naliases: [Secretaliasone]\nstatus: canon\nfrom: \"ember:1\"\n---\n\nA friend.\n\n## Early life\nYoung.\n");
        _den.Write(
            "universes/starfall/entities/notes/later-friend.md",
            "---\ntype: note\nname: Laternamed Friend\naliases: [Lateraliasword]\nstatus: canon\nfrom: \"ash:3\"\n---\n\nAnother.\n");
        _den.Write(
            "universes/starfall/entities/notes/the-linker.md",
            "---\ntype: note\nname: The linker\nstatus: canon\nfrom: \"ember:1\"\nfields:\n" +
            "  f1: \"[[Secretaliasone]]\"\n  f2: \"[[met-friend|lblfieldone]]\"\n  f3: \"[[Lateraliasword]]\"\n  f4: \"[[later-friend|lblfieldtwo]]\"\n---\n\n" +
            "Opening by [[met-friend]].\n\n## Links\nOne [[Secretaliasone]], two [[Metnamed Friend#Early life]], three [[Lateraliasword]], and four [[later-friend|lbltextfour]].\n");
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-106")]
    [InlineData("ember:3")]
    [InlineData("ash:3")]
    public async Task EveryLinkIsWhatTheReadersPageShows(string at)
    {
        WriteLinksByOtherNames();
        await using var server = await Start();
        var shown = await AllPagesShow(server, at);

        foreach (var word in new[] { "Secretaliasone", "Metnamed", "Early", "Lateraliasword", "Laternamed", "lblfieldone", "lblfieldtwo", "lbltextfour", "linker" })
        {
            var hits = Hits(await server.GetJsonAsync(Readers(word, at), HttpStatusCode.OK));
            Assert.True(Holds(word, shown) == hits.Count > 0, $"'{word}' at {at}: the page {(Holds(word, shown) ? "shows it" : "doesn't")}, and the search has {hits.Count} hits.");
        }

        // An alias of a met note is never written out, in text or a value, nor paired with its name.
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("Secretaliasone", at), HttpStatusCode.OK)));
        foreach (var hit in Hits(await server.GetJsonAsync(Readers("Metnamed", at), HttpStatusCode.OK)))
        {
            Assert.DoesNotContain("Secretaliasone", hit.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("[[", hit.GetProperty("text").GetString()!, StringComparison.Ordinal);
        }

        // The page shows the name there, so search finds the section by it, with its heading.
        var section = Hits(await server.GetJsonAsync(Readers("Metnamed", at), HttpStatusCode.OK)).Single(h => h.GetProperty("section").GetString() == "Links");
        Assert.Contains("One Metnamed Friend, two Metnamed Friend: Early life", section.GetProperty("text").GetString()!, StringComparison.Ordinal);

        // A value too.
        Assert.Equal("Metnamed Friend", Assert.Single(Hits(await server.GetJsonAsync(Readers("Metnamed", at, "&field=f1"), HttpStatusCode.OK))).GetProperty("text").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-106")]
    public async Task ALaterNotesNameDoesntMatchThroughALinkUntilReadersMeetIt()
    {
        WriteLinksByOtherNames();
        await using var server = await Start();

        // Nothing else on any page shows this name before ash:3: not the links by alias, nor the labelled ones.
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("Laternamed", "ember:3"), HttpStatusCode.OK)));
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("Laternamed", "ember:3", "&field=f3"), HttpStatusCode.OK)));

        // As written, as the page shows it until then.
        Assert.Equal("Lateraliasword", Assert.Single(Hits(await server.GetJsonAsync(Readers("Lateraliasword", "ember:3", "&field=f3"), HttpStatusCode.OK))).GetProperty("text").GetString());

        // Once met, the unlabelled links show it, and a labelled one still doesn't.
        Assert.Equal("Laternamed Friend", Assert.Single(Hits(await server.GetJsonAsync(Readers("Laternamed", "ash:3", "&field=f3"), HttpStatusCode.OK))).GetProperty("text").GetString());
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("Lateraliasword", "ash:3"), HttpStatusCode.OK)));
        Assert.Empty(Hits(await server.GetJsonAsync(Readers("Laternamed", "ash:3", "&field=f4"), HttpStatusCode.OK)));

        // And the Author's view finds the name, tagged, and says a reader wouldn't.
        var marked = Hits(await server.GetJsonAsync(Marked("Laternamed", "ember:3", "&field=f3"), HttpStatusCode.OK)).Single();
        Assert.False(marked.GetProperty("met").GetBoolean());
        Assert.Equal("ahead", marked.GetProperty("laterLink").GetProperty("readerMark").GetProperty("kind").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-107")]
    public async Task WhereAReadersSearchFindsALinkByTheNameItShowsMetAgrees()
    {
        WriteLinksByOtherNames();
        await using var server = await Start();

        var byName = Hits(await server.GetJsonAsync(Marked("Metnamed", "ember:3", "&field=f1"), HttpStatusCode.OK)).Single();
        Assert.True(byName.GetProperty("met").GetBoolean());

        // Its alias is on no page, so the Author's view says so, and a label too.
        var byAlias = Hits(await server.GetJsonAsync(Marked("Secretaliasone", "ember:3", "&field=f1"), HttpStatusCode.OK)).Single();
        Assert.False(byAlias.GetProperty("met").GetBoolean());
        var labelled = Hits(await server.GetJsonAsync(Marked("Metnamed", "ember:3", "&field=f2"), HttpStatusCode.OK)).Single();
        Assert.False(labelled.GetProperty("met").GetBoolean());
        Assert.Equal("linkTarget", labelled.GetProperty("hiddenIn").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-106")]
    public void TheServersRuleForWhatALinkShowsIsThePages()
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "link-text-cases.json")));
        Assert.NotEmpty(cases.RootElement.EnumerateArray());
        foreach (var c in cases.RootElement.EnumerateArray())
        {
            Assert.Equal(
                c.GetProperty("shown").GetString(),
                ReadersLinks.Shown(c.GetProperty("label").GetString(), c.GetProperty("name").GetString(), c.GetProperty("target").GetString()!, c.GetProperty("heading").GetString()));
        }
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

    /// <summary>
    /// There's no second rule (#67): an assistant's <c>search</c> and <c>list_entities</c> at a
    /// position find what the Reader's view finds, in the same order, and differ only in that a
    /// hit leaves out the page's <c>text</c> and <c>matches</c>. It's one word from each kind of
    /// text, and words many notes share for the order. <see cref="EveryWordOfEveryNoteIsFoundExactlyWhenTheReadersPageShowsIt"/>
    /// holds the search itself to every word.
    /// </summary>
    [Theory]
    [Trait("Scenario", "SC-MCP-50")]
    [InlineData("ember:3")]
    [InlineData("ash:1")]
    [InlineData("ash:2")]
    [InlineData("ash:3")]
    public async Task AnAssistantAtAPositionFindsWhatTheReadersViewFinds(string at)
    {
        await using var server = await Start();
        var tools = new WolfdenTools(new DenTools(_den.Root));
        var wrong = new List<string>();
        var mostHits = 0;
        foreach (var word in Words.Concat(["the", "word", "is", "later", "a", "of"]))
        {
            var page = Hits(await server.GetJsonAsync(Readers(word, at), HttpStatusCode.OK))
                .Select(h => (Note: h.GetProperty("entity").GetProperty("path").GetString(), Section: Member(h, "section"), Line: Member(h, "line"), Field: Member(h, "field"), Snippet: h.GetProperty("snippet").GetString(), Status: h.GetProperty("status").GetString()))
                .ToList();
            mostHits = Math.Max(mostHits, page.Count);
            using var answer = JsonDocument.Parse(Text(tools.Search(word, "starfall", limit: 200, as_of: at)));
            var mcp = answer.RootElement.GetProperty("hits").EnumerateArray()
                .Select(h => (Note: h.GetProperty("entity").GetProperty("path").GetString(), Section: Member(h, "section"), Line: Member(h, "line"), Field: Member(h, "field"), Snippet: h.GetProperty("snippet").GetString(), Status: h.GetProperty("status").GetString()));
            if (!page.SequenceEqual(mcp))
            {
                wrong.Add(word);
            }
        }

        Assert.Empty(wrong);
        Assert.True(mostHits > 3, $"The order is only worth comparing with several hits, and the most was {mostHits}.");

        var listed = await server.GetJsonAsync($"/api/entities?universe=starfall&asOf={at}&readers=true&limit=1000", HttpStatusCode.OK);
        using var list = JsonDocument.Parse(Text(tools.ListEntities("starfall", limit: 1000, as_of: at)));
        Assert.Equal(
            listed.GetProperty("entities").EnumerateArray().Select(e => e.GetProperty("path").GetString()),
            list.RootElement.GetProperty("entities").EnumerateArray().Select(e => e.GetProperty("path").GetString()));

        static string? Member(JsonElement hit, string name) => hit.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
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

using System.Net;
using System.Text;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// Names readers learn partway through the story (<c>known_as</c>, #67) in the web UI's
/// API: the Author's view lists and marks them, marks a link written by one, and tags a
/// search hit only in one; the Reader's view lists only the names readers know; a link
/// by a name readers know shows as written; the link lookup says which name matched; and
/// the note's names are edited through <c>/api/edit/name</c> and <c>/api/edit/name/add</c>.
/// </summary>
public sealed class KnownAsWebTests : IDisposable
{
    private const string CourierPath = "universes/starfall/entities/notes/the-courier.md";
    private const string PostBookPath = "universes/starfall/entities/notes/the-post-book.md";

    private readonly TempDen _den = TempDen.Starfall();

    public KnownAsWebTests()
    {
        _den.Write(
            CourierPath,
            "---\ntype: note\nname: The Courier\naliases: [the Messenger]\nknown_as:\n  - { value: Mara Voss, from: \"ash:2\" }\n  - { value: Quillon, from: \"ash:4\" }\n" +
            "status: canon\nfrom: \"ember:1\"\n---\n\nCarries the post.\n\n## Overview\nQuiet.\n");
        _den.Write(
            PostBookPath,
            "---\ntype: note\nname: The post book\nstatus: canon\nfrom: \"ember:1\"\n---\n\nSigned by [[Mara Voss]].\n\n" +
            "## Entries\n[[Mara Voss|The stranger]] paid. [[The Messenger]] left.\n");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _den.Dispose();

    private Task<Server> Start() => Server.StartAsync(new WolfdenApiOptions(_den.Root));

    private static string Page(string entity, string query) => $"/api/entity?universe=starfall&entity={Uri.EscapeDataString(entity)}{query}";

    private static JsonElement Name(JsonElement page, string value) =>
        page.GetProperty("entity").GetProperty("knownAs").EnumerateArray().Single(n => n.GetProperty("value").GetString() == value);

    private static string? MarkOf(JsonElement element, string member) =>
        element.TryGetProperty(member, out var mark) ? $"{mark.GetProperty("kind").GetString()} {mark.GetProperty("at").GetString()}" : null;

    [Fact]
    [Trait("Scenario", "SC-WEB-112")]
    public async Task TheAuthorSeesEveryNameMarkedAndTheReaderOnlyTheNamesTheyKnow()
    {
        await using var server = await Start();

        // The Author's view at ember:3: every name, each marked with where readers learn it.
        var author = await server.GetJsonAsync(Page("The Courier", "&marksAt=ember:3"), HttpStatusCode.OK);
        Assert.Equal(["the Messenger"], author.GetProperty("entity").GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("ash:2", Name(author, "Mara Voss").GetProperty("from").GetString());
        Assert.Equal("ahead ash:2", MarkOf(Name(author, "Mara Voss"), "readerMark"));
        Assert.Equal("ahead ash:4", MarkOf(Name(author, "Quillon"), "readerMark"));

        // At its from, a name has no mark; a later one still does.
        var later = await server.GetJsonAsync(Page("The Courier", "&marksAt=ash:2"), HttpStatusCode.OK);
        Assert.Null(MarkOf(Name(later, "Mara Voss"), "readerMark"));
        Assert.Equal("ahead ash:4", MarkOf(Name(later, "Quillon"), "readerMark"));

        // With no position, each name with its from, and no marks.
        var whole = await server.GetJsonAsync(Page("The Courier", ""), HttpStatusCode.OK);
        Assert.Equal(["Mara Voss ash:2", "Quillon ash:4"], whole.GetProperty("entity").GetProperty("knownAs").EnumerateArray()
            .Select(n => $"{n.GetProperty("value").GetString()} {n.GetProperty("from").GetString()}"));
        Assert.Null(MarkOf(Name(whole, "Mara Voss"), "readerMark"));

        // The Reader's view lists only the names readers know there, as plain aliases, and nothing else of them.
        var early = await server.GetJsonAsync(Page("The Courier", "&asOf=ember:3&readers=true"), HttpStatusCode.OK);
        Assert.Equal(["the Messenger"], early.GetProperty("entity").GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
        Assert.False(early.GetProperty("entity").TryGetProperty("knownAs", out _));
        Assert.DoesNotContain("Voss", early.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Quillon", early.GetRawText(), StringComparison.Ordinal);
        var reached = await server.GetJsonAsync(Page("The Courier", "&asOf=ash:2&readers=true"), HttpStatusCode.OK);
        Assert.Equal(["the Messenger", "Mara Voss"], reached.GetProperty("entity").GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
        Assert.DoesNotContain("Quillon", reached.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-112")]
    public async Task ANameThatIsntPlacedOrComesBeforeTheNoteIsMarkedByWhatReadersCanKnow()
    {
        _den.Write(
            CourierPath,
            "---\ntype: note\nname: The Courier\nknown_as:\n  - { value: Mara Voss, from: \"ember:1\" }\n  - { value: Quillon, from: \"ash:49\" }\n  - Tamsin Reed\n" +
            "status: canon\nfrom: \"ember:2\"\n---\n\nCarries the post.\n");
        await using var server = await Start();

        var author = await server.GetJsonAsync(Page("The Courier", "&marksAt=ember:1"), HttpStatusCode.OK);

        // A name can't be known before its note is met: it's ahead until the note's entry.
        Assert.Equal("ahead ember:2", MarkOf(Name(author, "Mara Voss"), "readerMark"));

        // A from the universe doesn't have, or none, places the name nowhere, so no position knows it.
        Assert.Equal("unplaced ash:49", MarkOf(Name(author, "Quillon"), "readerMark"));
        var bare = Name(author, "Tamsin Reed");
        Assert.Equal(JsonValueKind.Null, bare.GetProperty("from").ValueKind);
        Assert.Equal("unplaced", bare.GetProperty("readerMark").GetProperty("kind").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-112")]
    public async Task ALinkByALaterNameShowsAsWrittenAndIsMarkedInTheAuthorsView()
    {
        await using var server = await Start();

        var author = await server.GetJsonAsync(Page("The post book", "&marksAt=ember:3"), HttpStatusCode.OK);
        var lead = Assert.Single(author.GetProperty("entity").GetProperty("leadLinks").EnumerateArray());
        Assert.Equal("[[Mara Voss]]", lead.GetProperty("text").GetString());
        Assert.True(lead.GetProperty("byKnownName").GetBoolean());
        Assert.Equal("ahead ash:2", MarkOf(lead, "nameMark"));

        // A labelled link shows only its label, so it has no mark; a link by a plain alias is an ordinary link.
        var links = author.GetProperty("entity").GetProperty("sections")[0].GetProperty("links").EnumerateArray().ToList();
        Assert.Null(MarkOf(links[0], "nameMark"));
        Assert.False(links[1].TryGetProperty("byKnownName", out _));

        // Once readers know the name, no mark.
        var later = await server.GetJsonAsync(Page("The post book", "&marksAt=ash:2"), HttpStatusCode.OK);
        Assert.Null(MarkOf(later.GetProperty("entity").GetProperty("leadLinks")[0], "nameMark"));

        // The note's own Linked from marks the backlink written by the later name.
        var courier = await server.GetJsonAsync(Page("The Courier", "&marksAt=ember:3"), HttpStatusCode.OK);
        var backlinks = courier.GetProperty("entity").GetProperty("backlinks").EnumerateArray().ToList();
        Assert.Equal("ahead ash:2", MarkOf(backlinks.Single(b => b.GetProperty("text").GetString() == "[[Mara Voss]]"), "nameMark"));
        Assert.Null(MarkOf(backlinks.Single(b => b.GetProperty("text").GetString() == "[[The Messenger]]"), "nameMark"));

        // With no position, as written, with nothing to mark.
        var whole = await server.GetJsonAsync(Page("The post book", ""), HttpStatusCode.OK);
        Assert.True(whole.GetProperty("entity").GetProperty("leadLinks")[0].GetProperty("byKnownName").GetBoolean());
        Assert.Null(MarkOf(whole.GetProperty("entity").GetProperty("leadLinks")[0], "nameMark"));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-113")]
    public async Task ALinkByANameReadersKnowShowsAsWrittenInTheReadersView()
    {
        _den.Write(
            "universes/starfall/entities/notes/the-man-in-the-grey-coat.md",
            "---\ntype: note\nname: The man in the grey coat\nknown_as: [{ value: Corin Hask, from: \"ash:2\" }]\nstatus: canon\nfrom: \"ember:1\"\n---\n\nWatches the docks.\n");
        _den.Write(
            "universes/starfall/entities/notes/the-dock-log.md",
            "---\ntype: note\nname: The dock log\nstatus: canon\nfrom: \"ember:1\"\n---\n\nRecords ships.\n\n## After the reveal\n<!-- wolfden: from=ash:2 -->\n[[Corin Hask]] signed it.\n");
        await using var server = await Start();

        // At ash:2 the title is unchanged, its names list the real one, and the link shows as written.
        var man = await server.GetJsonAsync(Page("Corin Hask", "&asOf=ash:2&readers=true"), HttpStatusCode.OK);
        Assert.Equal("The man in the grey coat", man.GetProperty("entity").GetProperty("name").GetString());
        Assert.Equal(["Corin Hask"], man.GetProperty("entity").GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
        var log = await server.GetJsonAsync(Page("The dock log", "&asOf=ash:2&readers=true"), HttpStatusCode.OK);
        var link = Assert.Single(log.GetProperty("entity").GetProperty("sections")[0].GetProperty("links").EnumerateArray());
        Assert.Equal("[[Corin Hask]]", link.GetProperty("text").GetString());
        Assert.True(link.GetProperty("byKnownName").GetBoolean());

        // So the Reader's search finds the link by the words it shows, the real name, not the note's.
        var hits = await server.GetJsonAsync("/api/search?universe=starfall&query=Hask%20signed&asOf=ash:2&readers=true", HttpStatusCode.OK);
        Assert.Equal("The dock log", Assert.Single(hits.GetProperty("hits").EnumerateArray()).GetProperty("entity").GetProperty("name").GetString());
        var none = await server.GetJsonAsync("/api/search?universe=starfall&query=grey%20coat%20signed&asOf=ash:2&readers=true", HttpStatusCode.OK);
        Assert.Equal(0, none.GetProperty("count").GetInt32());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-112")]
    public async Task ASearchHitOnlyInALaterNameIsTaggedWithWhereReadersLearnIt()
    {
        await using var server = await Start();

        var marked = await server.GetJsonAsync("/api/search?universe=starfall&query=Voss&marksAt=ember:3", HttpStatusCode.OK);
        var hits = marked.GetProperty("hits").EnumerateArray().ToList();
        var courier = hits.Single(h => h.GetProperty("entity").GetProperty("name").GetString() == "The Courier" && h.GetProperty("section").ValueKind == JsonValueKind.Null);
        Assert.Equal("laterName", courier.GetProperty("hiddenIn").GetString());
        Assert.Equal("ahead ash:2", MarkOf(courier, "nameMark"));
        Assert.False(courier.GetProperty("met").GetBoolean());

        // The Reader's view there finds nothing by it.
        var readers = await server.GetJsonAsync("/api/search?universe=starfall&query=Voss&asOf=ember:3&readers=true", HttpStatusCode.OK);
        Assert.Equal(0, readers.GetProperty("count").GetInt32());

        // Once readers know the name, the hit has no tag and is met.
        var later = await server.GetJsonAsync("/api/search?universe=starfall&query=Voss&marksAt=ash:2", HttpStatusCode.OK);
        var known = later.GetProperty("hits").EnumerateArray().Single(h => h.GetProperty("entity").GetProperty("name").GetString() == "The Courier");
        Assert.False(known.TryGetProperty("hiddenIn", out _));
        Assert.True(known.GetProperty("met").GetBoolean());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-112")]
    public async Task AHitOnlyInALaterNameComesAfterWhatReadersCanFind()
    {
        _den.Write(
            "universes/starfall/entities/notes/a-voss-ledger.md",
            "---\ntype: note\nname: A ledger\nstatus: canon\nfrom: \"ember:1\"\n---\n\nThe Voss accounts.\n");
        await using var server = await Start();

        var marked = await server.GetJsonAsync("/api/search?universe=starfall&query=Voss&marksAt=ember:3", HttpStatusCode.OK);
        var names = marked.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("entity").GetProperty("name").GetString()).ToList();

        Assert.Equal("A ledger", names[0]);
        Assert.Contains("The Courier", names.Skip(1));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-112")]
    public async Task AHiddenNameDoesntRaiseANoteInTheReadersSearch()
    {
        // Two notes alike but for their names, one with a later name that repeats the word searched for.
        _den.Write(
            "universes/starfall/entities/notes/alder.md",
            "---\ntype: note\nname: Alder note\nstatus: canon\nfrom: \"ember:1\"\n---\n\nPellam waits at the gate.\n");
        _den.Write(
            "universes/starfall/entities/notes/birch.md",
            "---\ntype: note\nname: Birch note\nknown_as: [{ value: Pellam Pellam Pellam, from: \"ash:2\" }]\nstatus: canon\nfrom: \"ember:1\"\n---\n\nPellam waits at the gate.\n");
        await using var server = await Start();

        var early = await server.GetJsonAsync("/api/search?universe=starfall&query=Pellam&asOf=ember:3&readers=true", HttpStatusCode.OK);

        // Equal as readers see them, so the order is by path, whatever the hidden name says.
        Assert.Equal(["Alder note", "Birch note"], early.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("entity").GetProperty("name").GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-114")]
    public async Task PronounsUntilTheRevealReadPlainlyToReadersAndMarkedToTheAuthor()
    {
        _den.Write(
            "universes/starfall/entities/characters/wren-tolly.md",
            "---\ntype: character\nname: Wren Tolly\nstatus: canon\nfrom: \"ember:1\"\nfields:\n  pronouns:\n" +
            "    - { value: they/them, until: \"ash:3\" }\n    - { value: he/him, from: \"ash:3\" }\n---\n\nA dock hand.\n");
        await using var server = await Start();

        var readers = await server.GetJsonAsync(Page("Wren Tolly", "&asOf=ember:3&readers=true"), HttpStatusCode.OK);
        var shown = Assert.Single(readers.GetProperty("entity").GetProperty("fields").EnumerateArray(), f => f.GetProperty("name").GetString() == "pronouns");
        Assert.Equal("they/them", shown.GetProperty("value").GetString());
        Assert.DoesNotContain("he/him", readers.GetRawText(), StringComparison.Ordinal);

        var author = await server.GetJsonAsync(Page("Wren Tolly", "&marksAt=ember:3"), HttpStatusCode.OK);
        var entries = author.GetProperty("entity").GetProperty("fields").EnumerateArray().Where(f => f.GetProperty("name").GetString() == "pronouns").ToList();
        Assert.Equal(["they/them", "he/him"], entries.Select(e => e.GetProperty("value").GetString()));
        Assert.Null(MarkOf(entries[0], "readerMark"));
        Assert.Equal("ahead ash:3", MarkOf(entries[1], "readerMark"));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-116")]
    public async Task TheLinkLookupSaysWhichNameMatchedAndWhereReadersLearnIt()
    {
        await using var server = await Start();

        var found = await server.GetJsonAsync("/api/entities/lookup?universe=starfall&query=mara", HttpStatusCode.OK);

        var hit = Assert.Single(found.GetProperty("matches").EnumerateArray());
        Assert.Equal("The Courier", hit.GetProperty("entity").GetProperty("name").GetString());
        Assert.Equal("alias", hit.GetProperty("matchedBy").GetString());
        Assert.Equal("Mara Voss", hit.GetProperty("alias").GetString());
        Assert.True(hit.GetProperty("knownAs").GetBoolean());
        Assert.Equal("ash:2", hit.GetProperty("from").GetString());

        // A plain alias says nothing of the kind.
        var plain = Assert.Single((await server.GetJsonAsync("/api/entities/lookup?universe=starfall&query=messenger", HttpStatusCode.OK)).GetProperty("matches").EnumerateArray());
        Assert.False(plain.TryGetProperty("knownAs", out var knownAs) && knownAs.ValueKind == JsonValueKind.True);
    }

    private async Task<string> HashAsync(Server server) =>
        (await server.GetJsonAsync($"/api/entity/source?universe=starfall&entity={Uri.EscapeDataString(CourierPath)}", HttpStatusCode.OK)).GetProperty("hash").GetString()!;

    private async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(Server server, string path, Dictionary<string, object> body)
    {
        body["entity"] = CourierPath;
        body["universe"] = "starfall";
        body["expectedHash"] = await HashAsync(server);
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await server.Client.PostAsync(path, content, Token);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-115")]
    public async Task TheEditorAddsANameWithWhereReadersLearnItAndRefusesOneWithout()
    {
        await using var server = await Start();

        var (status, json) = await PostAsync(server, "/api/edit/name/add", new() { ["name"] = "Tam", ["knownAs"] = false });
        Assert.True(status == HttpStatusCode.OK, json.ToString());
        (status, json) = await PostAsync(server, "/api/edit/name/add", new() { ["name"] = "Mara Holt", ["knownAs"] = true, ["from"] = "ash:3" });
        Assert.True(status == HttpStatusCode.OK, json.ToString());
        var file = _den.Read(CourierPath);
        Assert.Contains("aliases: [the Messenger, Tam]", file, StringComparison.Ordinal);
        Assert.Contains("value: Mara Holt", file, StringComparison.Ordinal);
        Assert.Contains("from: \"ash:3\"", file, StringComparison.Ordinal);

        // A name readers learn later must say where; a from the universe doesn't have is refused too. Nothing changes.
        var before = _den.Read(CourierPath);
        (status, json) = await PostAsync(server, "/api/edit/name/add", new() { ["name"] = "Wren", ["knownAs"] = true });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.StartsWith("Say where readers learn the name", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        (status, json) = await PostAsync(server, "/api/edit/name/add", new() { ["name"] = "Wren", ["knownAs"] = true, ["from"] = "ash:49" });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("bad_position", json.GetProperty("code").GetString());
        (status, _) = await PostAsync(server, "/api/edit/name/add", new() { ["name"] = "Wren", ["from"] = "ash:3" });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(before, _den.Read(CourierPath));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-115")]
    public async Task TheEditorRenamesMovesAndRemovesANameThroughChangeAlias()
    {
        await using var server = await Start();

        // Renamed: links follow, and the entry keeps its from.
        var (status, json) = await PostAsync(server, "/api/edit/name", new() { ["name"] = "Mara Voss", ["newName"] = "Mara Holt" });
        Assert.True(status == HttpStatusCode.OK, json.ToString());
        Assert.Contains("{ value: Mara Holt, from: \"ash:2\" }", _den.Read(CourierPath), StringComparison.Ordinal);
        Assert.Contains("Signed by [[Mara Holt]].", _den.Read(PostBookPath), StringComparison.Ordinal);
        Assert.Contains("[[Mara Holt|The stranger]]", _den.Read(PostBookPath), StringComparison.Ordinal);

        // Readers learn it earlier: only the from changes, and no link.
        var post = _den.Read(PostBookPath);
        (status, json) = await PostAsync(server, "/api/edit/name", new() { ["name"] = "Mara Holt", ["from"] = "ember:1" });
        Assert.True(status == HttpStatusCode.OK, json.ToString());
        Assert.Contains("{ value: Mara Holt, from: \"ember:1\" }", _den.Read(CourierPath), StringComparison.Ordinal);
        Assert.Equal(post, _den.Read(PostBookPath));

        // A plain alias becomes a name readers learn later, and back, with no link changed.
        (status, json) = await PostAsync(server, "/api/edit/name", new() { ["name"] = "the Messenger", ["from"] = "ember:3" });
        Assert.True(status == HttpStatusCode.OK, json.ToString());
        Assert.Contains("value: the Messenger", _den.Read(CourierPath), StringComparison.Ordinal);
        (status, json) = await PostAsync(server, "/api/edit/name", new() { ["name"] = "the Messenger", ["plain"] = true });
        Assert.True(status == HttpStatusCode.OK, json.ToString());
        Assert.Contains(json.GetProperty("effects").EnumerateArray(), e => e.GetString()!.Contains("gives it away", StringComparison.Ordinal));
        Assert.Equal(post, _den.Read(PostBookPath));

        // Removing is asked for by itself, so an empty rename doesn't remove a name.
        var before = _den.Read(CourierPath);
        (status, _) = await PostAsync(server, "/api/edit/name", new() { ["name"] = "Quillon", ["newName"] = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(before, _den.Read(CourierPath));
        (status, json) = await PostAsync(server, "/api/edit/name", new() { ["name"] = "Quillon", ["remove"] = true });
        Assert.True(status == HttpStatusCode.OK, json.ToString());
        Assert.DoesNotContain("Quillon", _den.Read(CourierPath), StringComparison.Ordinal);
    }
}

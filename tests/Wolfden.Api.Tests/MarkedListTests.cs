using System.Net;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Wolfden.Mcp;

namespace Wolfden.Api.Tests;

/// <summary>
/// <c>/api/entities</c> at a position (#300): with <c>marksAt</c>, the Author's view
/// lists everything, what readers have met first and the rest marked. With
/// <c>asOf</c>, the Reader's view lists only what an assistant's <c>canon_as_of</c> holds.
/// </summary>
public sealed class MarkedListTests : IDisposable
{
    private const string List = "/api/entities?universe=starfall&limit=1000";
    private const string Later = "The salt heir";
    private const string Idea = "A salt rumor";
    private const string Unplaced = "The dusk toll";

    private readonly TempDen _den = TempDen.Starfall();

    public MarkedListTests()
    {
        _den.Write("universes/starfall/entities/notes/the-salt-heir.md", "---\ntype: note\nname: The salt heir\nstatus: canon\nfrom: \"ash:3\"\n---\n\nThe heir.\n");
        _den.Write("universes/starfall/entities/notes/a-salt-rumor.md", "---\ntype: note\nname: A salt rumor\nstatus: idea\n---\n\nMaybe.\n");
        _den.Write("universes/starfall/entities/notes/the-dusk-toll.md", "---\ntype: note\nname: The dusk toll\nstatus: canon\nfrom: \"dusk:9\"\n---\n\nLater still.\n");
    }

    public void Dispose() => _den.Dispose();

    private Task<Server> Start() => Server.StartAsync(new WolfdenApiOptions(_den.Root));

    private static List<JsonElement> Entities(JsonElement answer) => answer.GetProperty("entities").EnumerateArray().ToList();

    private static string Name(JsonElement entity) => entity.GetProperty("name").GetString()!;

    private static string Path(JsonElement entity) => entity.GetProperty("path").GetString()!;

    private static bool Met(JsonElement entity) => entity.GetProperty("met").GetBoolean();

    private static async Task<List<string>> PositionsAsync(Server server) =>
        (await server.GetJsonAsync("/api/positions?universe=starfall", HttpStatusCode.OK))
            .GetProperty("positions").EnumerateArray().Select(p => p.GetProperty("position").GetString()!).ToList();

    private static JsonElement CanonAsOf(WolfdenTools tools, string at, string? series = null, string? book = null, string? type = null)
    {
        using var result = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(tools.CanonAsOf(at, "starfall", series, book, type, brief: true).Content)).Text);
        return result.RootElement.GetProperty("entities").Clone();
    }

    private static JsonElement Note(JsonElement answer, string name) => Entities(answer).Single(e => Name(e) == name);

    // --- The answers without marksAt don't change. ---

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData(List)]
    [InlineData(List + "&asOf=ember:3")]
    public async Task WithoutMarksAtAListHasNoMarks(string path)
    {
        await using var server = await Start();

        var answer = (await server.GetJsonAsync(path, HttpStatusCode.OK)).GetRawText();

        Assert.DoesNotContain("readerMark", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("marksAt", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("\"met\"", answer, StringComparison.Ordinal);
    }

    // --- The Author's view. ---

    [Fact]
    [Trait("Scenario", "SC-WEB-104")]
    public async Task AMarkedListHasEveryNoteWithWhatReadersHaveMetFirstEachPartInItsOrder()
    {
        await using var server = await Start();
        var full = Entities(await server.GetJsonAsync(List, HttpStatusCode.OK)).Select(Path).ToList();

        foreach (var at in await PositionsAsync(server))
        {
            var answer = await server.GetJsonAsync($"{List}&marksAt={at}", HttpStatusCode.OK);
            var marked = Entities(answer);

            Assert.Equal(at, answer.GetProperty("marksAt").GetString());
            Assert.Equal(full.Count, answer.GetProperty("total").GetInt32());
            Assert.Equal(full.Order(StringComparer.Ordinal), marked.Select(Path).Order(StringComparer.Ordinal));

            var met = marked.TakeWhile(Met).Select(Path).ToList();
            var rest = marked.Skip(met.Count).ToList();
            Assert.DoesNotContain(rest, Met);
            Assert.Equal(full.Where(met.Contains), met);
            Assert.Equal(full.Where(rest.Select(Path).Contains), rest.Select(Path));
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-104")]
    public async Task ALaterNoteSaysWhereItEntersAndIsntMet()
    {
        await using var server = await Start();

        var at = await server.GetJsonAsync($"{List}&marksAt=ember:3", HttpStatusCode.OK);

        var mark = Note(at, Later).GetProperty("readerMark");
        Assert.False(Met(Note(at, Later)));
        Assert.Equal("ahead", mark.GetProperty("kind").GetString());
        Assert.Equal("ash:3", mark.GetProperty("at").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-104")]
    public async Task ANoteFiledInALaterBookEntersAtThatBook()
    {
        _den.Write("universes/starfall/series/long-dark/books/ash/entities/notes/an-ash-note.md", "---\ntype: note\nname: An ash note\nstatus: canon\n---\n\nFiled in the book.\n");
        await using var server = await Start();

        var mark = Note(await server.GetJsonAsync($"{List}&marksAt=ember:3", HttpStatusCode.OK), "An ash note").GetProperty("readerMark");

        Assert.Equal("ahead", mark.GetProperty("kind").GetString());
        Assert.Equal("ash", mark.GetProperty("at").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-104")]
    public async Task ANoteNoPositionPlacesIsUnplacedAtEveryPosition()
    {
        await using var server = await Start();

        foreach (var at in new[] { "ember:1", "ash:3" })
        {
            var note = Note(await server.GetJsonAsync($"{List}&marksAt={at}", HttpStatusCode.OK), Unplaced);
            Assert.False(Met(note));
            Assert.Equal("unplaced", note.GetProperty("readerMark").GetProperty("kind").GetString());
            Assert.Equal("dusk:9", note.GetProperty("readerMark").GetProperty("at").GetString());
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-104")]
    public async Task AnIdeaIsntMetAndHasNoMark()
    {
        await using var server = await Start();

        var note = Note(await server.GetJsonAsync($"{List}&marksAt=ash:3", HttpStatusCode.OK), Idea);

        Assert.False(Met(note));
        Assert.False(note.TryGetProperty("readerMark", out _));
        Assert.Equal("idea", note.GetProperty("status").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-104")]
    public async Task ALimitCutsWhatReadersHaventMetFirst()
    {
        await using var server = await Start();
        var whole = await server.GetJsonAsync($"{List}&marksAt=ember:1", HttpStatusCode.OK);
        var met = Entities(whole).Count(Met);
        Assert.True(met > 0 && met < Entities(whole).Count, "This needs a mix to say anything.");

        var cut = await server.GetJsonAsync($"/api/entities?universe=starfall&limit={met}&marksAt=ember:1", HttpStatusCode.OK);

        Assert.True(cut.GetProperty("truncated").GetBoolean());
        Assert.Equal(met, cut.GetProperty("count").GetInt32());
        Assert.Equal(Entities(whole).Count, cut.GetProperty("total").GetInt32());
        Assert.All(Entities(cut), e => Assert.True(Met(e)));
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-104")]
    [InlineData("&type=note", "&type=note")]
    [InlineData("&status=idea", "&status=idea")]
    [InlineData("&book=ember", "&book=ember")]
    [InlineData("&series=long-dark", "&series=long-dark")]
    [InlineData("&book=ash&type=note", "&book=ash&type=note")]
    public async Task AFilteredListHasWhatTheFilterKeepsSoATypeOrScopeListWorksToo(string filter, string same)
    {
        await using var server = await Start();

        foreach (var at in new[] { "ember:1", "ash:3" })
        {
            var plain = Entities(await server.GetJsonAsync($"{List}{same}", HttpStatusCode.OK)).Select(Path).Order(StringComparer.Ordinal);
            var strict = Entities(await server.GetJsonAsync($"{List}{same}&asOf={at}", HttpStatusCode.OK)).Select(Path).Order(StringComparer.Ordinal);
            var marked = Entities(await server.GetJsonAsync($"{List}{filter}&marksAt={at}", HttpStatusCode.OK));

            Assert.Equal(plain, marked.Select(Path).Order(StringComparer.Ordinal));
            Assert.Equal(strict, marked.Where(Met).Select(Path).Order(StringComparer.Ordinal));
        }
    }

    // --- A position that can't be read, and what doesn't go together. ---

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData("EMBER:3")]
    [InlineData("ember:99")]
    [InlineData("nowhere:1")]
    public async Task APositionThatCantBeReadIsRefusedAsAsOfRefusesIt(string position)
    {
        await using var server = await Start();

        var marked = await server.GetJsonAsync($"{List}&marksAt={position}", HttpStatusCode.BadRequest);
        var strict = await server.GetJsonAsync($"{List}&asOf={position}", HttpStatusCode.BadRequest);

        Assert.Equal("bad_position", marked.GetProperty("code").GetString());
        Assert.Equal(strict.GetProperty("message").GetString(), marked.GetProperty("message").GetString());
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-100")]
    [InlineData("&marksAt=", "'marksAt' is empty")]
    [InlineData("&marksAt=ember:1&asOf=ember:1", "not both")]
    [InlineData("&marksAt=ember:1&limit=0", "limit")]
    [InlineData("&marksAt=ember:1&limit=1001", "limit")]
    [InlineData("&marksAt=ember:1&status=nonsense", "status")]
    public async Task WhatDoesntMakeSenseIsRefused(string extra, string says)
    {
        await using var server = await Start();

        var answer = await server.GetJsonAsync($"/api/entities?universe=starfall{extra}", HttpStatusCode.BadRequest);

        Assert.Contains(says, answer.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-100")]
    public async Task EveryUniverseDoesntGoWithAPosition()
    {
        await using var server = await Start();

        var strict = await server.GetJsonAsync("/api/entities?universe=*&asOf=ember:1", HttpStatusCode.BadRequest);
        var marked = await server.GetJsonAsync("/api/entities?universe=*&marksAt=ember:1", HttpStatusCode.BadRequest);

        Assert.Equal(strict.GetProperty("code").GetString(), marked.GetProperty("code").GetString());
    }

    // --- The Reader's view, and the two together. ---

    [Fact]
    [Trait("Scenario", "SC-WEB-105")]
    public async Task AtEveryPositionTheReadersListAndWhatsMetAreWhatCanonAsOfHolds()
    {
        await using var server = await Start();
        var tools = new WolfdenTools(new DenTools(_den.Root));

        foreach (var at in await PositionsAsync(server))
        {
            var mcp = CanonAsOf(tools, at).EnumerateArray().Select(e => e.GetProperty("path").GetString()!).ToList();
            var readers = Entities(await server.GetJsonAsync($"{List}&asOf={at}", HttpStatusCode.OK)).Select(Path).ToList();
            var met = Entities(await server.GetJsonAsync($"{List}&marksAt={at}", HttpStatusCode.OK)).Where(Met).Select(Path).ToList();

            Assert.Equal(mcp, readers);
            Assert.Equal(mcp, met);
        }
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-105")]
    [InlineData("ember", null, null)]
    [InlineData(null, "long-dark", null)]
    [InlineData(null, null, "note")]
    public async Task ATypeOrScopeListIsWhatCanonAsOfHoldsThereToo(string? book, string? series, string? type)
    {
        await using var server = await Start();
        var tools = new WolfdenTools(new DenTools(_den.Root));
        var filter = $"{(book is null ? "" : $"&book={book}")}{(series is null ? "" : $"&series={series}")}{(type is null ? "" : $"&type={type}")}";

        foreach (var at in await PositionsAsync(server))
        {
            var mcp = CanonAsOf(tools, at, series, book, type).EnumerateArray().Select(e => e.GetProperty("path").GetString()!);
            var readers = Entities(await server.GetJsonAsync($"{List}{filter}&asOf={at}", HttpStatusCode.OK)).Select(Path);

            Assert.Equal(mcp, readers);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-105")]
    public async Task TheReadersListNamesNothingLaterOrCountsIt()
    {
        await using var server = await Start();

        var answer = await server.GetJsonAsync($"{List}&asOf=ember:1", HttpStatusCode.OK);
        var text = answer.GetRawText();

        foreach (var hidden in new[] { Later, Idea, Unplaced, "the-salt-heir", "a-salt-rumor", "the-dusk-toll" })
        {
            Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
        }

        Assert.Equal(Entities(answer).Count, answer.GetProperty("total").GetInt32());
        Assert.False(answer.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-104")]
    public async Task MarkingAListDoesntChangeWhatAnAssistantLists()
    {
        var tools = new WolfdenTools(new DenTools(_den.Root));

        var listed = Assert.IsType<TextContentBlock>(Assert.Single(tools.ListEntities(universe: "starfall").Content)).Text;

        Assert.DoesNotContain("reader_mark", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("\"met\"", listed, StringComparison.Ordinal);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Wolfden.Mcp;

namespace Wolfden.Api.Tests;

/// <summary>
/// <c>readers=true</c> on <c>/api/entity</c> (#300): the web UI's Reader's view is
/// what an AI assistant reads at that position, by one shared rule, so the two
/// can't drift. And the backlinks a marked read gives say what readers don't know.
/// </summary>
public sealed class ReadersViewTests : IDisposable
{
    private const string Tithe = "The%20salt%20tithe";
    private const string CommentsPath = "universes/starfall/entities/notes/the-salt-ledger.md";
    private const string LaterPath = "universes/starfall/entities/notes/the-salt-heir.md";

    private readonly TempDen _den = TempDen.Starfall();

    public ReadersViewTests()
    {
        _den.Write("packs/starfall/extensions/thread.yaml", "fields:\n  echoes: { type: position }\n");
        _den.Write(MarksAtTests.TithePath, MarksAtTests.TitheText);
        _den.Write(LaterPath, "---\ntype: note\nname: The salt heir\nstatus: canon\nfrom: \"ash:3\"\nfields:\n  claim: Ysmerine\n---\n\nThe heir inherits [[The salt tithe]].\n\n## Early life\nRaised at Ysmerine.\n");
        _den.Write(
            CommentsPath,
            "---\ntype: note\nname: The salt ledger\nstatus: canon\nfrom: \"ember:1\"\n---\n\nKept at the Reach. <!-- Thistlewick burns it in Ash. -->\n\n" +
            "## Entries\nEach toll is written down. <!-- [[The salt tithe]] is why, says Thistlewick.\n\n" +
            "## Audits\n<!-- wolfden: from=ember:1 until=ash:2 -->\nRead every year. See [[The salt tithe]].\n");
    }

    public void Dispose() => _den.Dispose();

    private Task<Server> Start() => Server.StartAsync(new WolfdenApiOptions(_den.Root));

    private static string Readers(string entity, string at) => $"/api/entity?universe=starfall&entity={entity}&asOf={at}&readers=true";

    /// <summary>A result's facts, in one form: snake_case names, sorted, with no nulls, and without what only the page has.</summary>
    private static string Facts(JsonElement entity, params string[] members)
    {
        var whole = JsonNode.Parse(entity.GetRawText())!.AsObject();
        var picked = new JsonObject();
        foreach (var member in members)
        {
            picked[member] = Normal(whole[member] ?? whole[Snake(member)]);
        }

        return picked.ToJsonString();

        static JsonNode? Normal(JsonNode? node) => node switch
        {
            JsonObject o => new JsonObject(o
                .Where(p => p.Value is not null && p.Key != "links")
                .OrderBy(p => Snake(p.Key), StringComparer.Ordinal)
                .Select(p => KeyValuePair.Create(Snake(p.Key), Normal(p.Value)))),
            JsonArray a => new JsonArray(a.Select(Normal).ToArray()),
            null => null,
            _ => node.DeepClone(),
        };
    }

    private static string Snake(string name)
    {
        var snake = new StringBuilder();
        foreach (var c in name)
        {
            snake.Append(char.IsUpper(c) ? $"_{char.ToLowerInvariant(c)}" : c.ToString());
        }

        return snake.ToString();
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    [Theory]
    [Trait("Scenario", "SC-WEB-101")]
    [InlineData("The salt tithe")]
    [InlineData("The salt heir")]
    [InlineData("The salt ledger")]
    [InlineData("Ilse Tarrow")]
    [InlineData("The missing manifest")]
    public async Task AtEveryPositionTheReadersPageIsWhatAnAssistantReads(string name)
    {
        await using var server = await Start();
        var tools = new WolfdenTools(new DenTools(_den.Root));
        var positions = await server.GetJsonAsync("/api/positions?universe=starfall", HttpStatusCode.OK);
        var compared = 0;

        foreach (var at in positions.GetProperty("positions").EnumerateArray().Select(p => p.GetProperty("position").GetString()!))
        {
            var page = await server.GetJsonAsync(Readers(Uri.EscapeDataString(name), at), HttpStatusCode.OK);
            using var mcp = JsonDocument.Parse(Text(tools.GetEntity(name, "starfall", at)));

            Assert.Equal(mcp.RootElement.GetProperty("in_view").GetBoolean(), page.GetProperty("inView").GetBoolean());
            if (!page.GetProperty("inView").GetBoolean())
            {
                Assert.Equal(JsonValueKind.Null, page.GetProperty("entity").ValueKind);
                continue;
            }

            string[] members = ["lead", "fields", "sections", "pinned"];
            Assert.Equal(Facts(mcp.RootElement.GetProperty("entity"), members), Facts(page.GetProperty("entity"), members));
            compared++;
        }

        Assert.True(compared > 0, $"{name} was never in view, so nothing was compared.");
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-101")]
    public async Task NothingOfAReadersPageSaysWhatReadersDontKnowYet()
    {
        await using var server = await Start();

        var page = (await server.GetJsonAsync(Readers(Tithe, "ember:3"), HttpStatusCode.OK)).GetRawText();

        foreach (var word in MarksAtTests.LaterWords)
        {
            Assert.DoesNotContain(word, page, StringComparison.Ordinal);
        }

        foreach (var mark in new[] { "readerMark", "aheadValues", "unplacedValues", "marksAt" })
        {
            Assert.DoesNotContain(mark, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-101")]
    public async Task ThePlainAsOfReadKeepsWhatTheReadersOneHides()
    {
        await using var server = await Start();

        var plain = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&asOf=ember:1", HttpStatusCode.OK);
        var readers = await server.GetJsonAsync(Readers(Tithe, "ember:1"), HttpStatusCode.OK);

        Assert.Contains("\"until\":\"ash:2\"", plain.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"paid_off\"", plain.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("ash:", readers.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"paid_off\"", readers.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-101")]
    public async Task AReadersPageHasNoComments()
    {
        await using var server = await Start();
        const string Ledger = "The%20salt%20ledger";

        var readers = await server.GetJsonAsync(Readers(Ledger, "ember:1"), HttpStatusCode.OK);
        var author = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Ledger}&marksAt=ember:1", HttpStatusCode.OK);

        Assert.DoesNotContain("Thistlewick", readers.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("Thistlewick", author.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-101")]
    public async Task ALinkInACommentIsntALinkOrABacklinkInAReadersPage()
    {
        await using var server = await Start();

        var ledger = (await server.GetJsonAsync(Readers("The%20salt%20ledger", "ember:1"), HttpStatusCode.OK)).GetProperty("entity");
        var entries = ledger.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "Entries");
        Assert.Empty(entries.GetProperty("links").EnumerateArray());

        // The same note's link in the section's own words does count.
        var tithe = (await server.GetJsonAsync(Readers(Tithe, "ember:1"), HttpStatusCode.OK)).GetProperty("entity");
        var places = tithe.GetProperty("backlinks").EnumerateArray().Where(b => b.GetProperty("path").GetString() == CommentsPath).ToList();
        Assert.Equal("Audits", Assert.Single(places).GetProperty("section").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-101")]
    public async Task ACommentInAHeadingDoesntComeThroughABacklinkOrALink()
    {
        _den.Write(
            "universes/starfall/entities/notes/the-toll-book.md",
            "---\ntype: note\nname: The toll book\nstatus: canon\nfrom: \"ember:1\"\n---\n\nKept by clerks.\n\n" +
            "## Heading <!-- Morvane did it --> place\nSee [[The salt tithe]] and [[The salt ledger]].\n");
        _den.Write(
            "universes/starfall/entities/notes/the-toll-map.md",
            "---\ntype: note\nname: The toll map\nstatus: canon\nfrom: \"ember:1\"\n---\n\nSee [[The toll book#Heading place]] and [[The toll book#Heading <!-- Morvane did it --> place]].\n");
        await using var server = await Start();

        foreach (var entity in new[] { Tithe, "The%20toll%20map", "The%20toll%20book" })
        {
            var readers = await server.GetJsonAsync(Readers(entity, "ember:1"), HttpStatusCode.OK);
            Assert.DoesNotContain("Morvane", readers.GetRawText(), StringComparison.Ordinal);
        }

        var tithe = await server.GetJsonAsync(Readers(Tithe, "ember:1"), HttpStatusCode.OK);
        var place = Backlinks(tithe).Single(b => b.GetProperty("path").GetString() == "universes/starfall/entities/notes/the-toll-book.md");
        Assert.DoesNotContain("<!--", place.GetProperty("section").GetString(), StringComparison.Ordinal);

        // The writer's read keeps the name as written.
        var author = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ember:1", HttpStatusCode.OK);
        Assert.Contains("Morvane did it", author.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-101")]
    [InlineData("&readers=true")]
    [InlineData("&readers=true&marksAt=ember:1")]
    [InlineData("&readers=true&asOf=ember:1&marksAt=ember:1")]
    [InlineData("&readers=maybe&asOf=ember:1")]
    [InlineData("&readers=true&readers=true&asOf=ember:1")]
    public async Task ReadersGoesOnlyWithAPosition(string query)
    {
        await using var server = await Start();

        var error = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}{query}", HttpStatusCode.BadRequest);

        Assert.Equal("bad_argument", error.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-101")]
    public async Task ReadersFalseIsTheSameAsLeavingItOut()
    {
        await using var server = await Start();

        var full = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}", HttpStatusCode.OK);
        var off = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&readers=false", HttpStatusCode.OK);

        Assert.Equal(full.GetRawText(), off.GetRawText());
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-101")]
    [InlineData("/api/canon?universe=starfall&asOf=ember:1&readers=true")]
    [InlineData("/api/search?universe=starfall&query=toll&asOf=ember:1&readers=true")]
    [InlineData("/api/entities?universe=starfall&asOf=ember:1&readers=true")]
    public async Task OnlyANotesPageTakesReaders(string path)
    {
        await using var server = await Start();

        var error = await server.GetJsonAsync(path, HttpStatusCode.BadRequest);

        Assert.Equal("bad_argument", error.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-101")]
    public void TheWebApiDoesntReferenceTheMcpProject()
    {
        Assert.DoesNotContain(typeof(ReadEndpoints).Assembly.GetReferencedAssemblies(), a => a.Name == "Wolfden.Mcp");
    }

    // --- The backlinks of a marked read. ---

    private static IEnumerable<JsonElement> Backlinks(JsonElement page) => page.GetProperty("entity").GetProperty("backlinks").EnumerateArray();

    [Fact]
    [Trait("Scenario", "SC-WEB-102")]
    public async Task ABacklinkHeldBySomethingLaterIsMarked()
    {
        await using var server = await Start();

        var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ember:1", HttpStatusCode.OK);

        var heir = Backlinks(page).Single(b => b.GetProperty("path").GetString() == LaterPath);
        Assert.Equal("ahead", heir.GetProperty("readerMark").GetProperty("kind").GetString());
        Assert.Equal("ash:3", heir.GetProperty("readerMark").GetProperty("at").GetString());
        var audits = Backlinks(page).Single(b => b.GetProperty("path").GetString() == CommentsPath && b.GetProperty("section").GetString() == "Audits");
        Assert.False(audits.TryGetProperty("readerMark", out _));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-102")]
    public async Task ABacklinkInAFactThatHasEndedSaysSo()
    {
        await using var server = await Start();

        var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ash:3", HttpStatusCode.OK);

        var audits = Backlinks(page).Single(b => b.GetProperty("path").GetString() == CommentsPath && b.GetProperty("section").GetString() == "Audits");
        Assert.Equal("ended", audits.GetProperty("readerMark").GetProperty("kind").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-102")]
    public async Task ABacklinkInASectionLeftOutSaysSoForTheWriterAndIsntInAReadersPage()
    {
        const string Path = "universes/starfall/entities/notes/the-toll-book.md";
        _den.Write(Path, "---\ntype: note\nname: The toll book\nstatus: canon\nfrom: \"ember:1\"\n---\n\nKept by clerks.\n\n## A rumor\n<!-- wolfden: status=maybe -->\nSee [[The salt tithe]].\n\n## Plain\nSee [[The salt tithe]].\n");
        await using var server = await Start();

        var author = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ember:1", HttpStatusCode.OK);
        var rumor = Backlinks(author).Single(b => b.GetProperty("path").GetString() == Path && b.GetProperty("section").GetString() == "A rumor");
        Assert.True(rumor.GetProperty("leftOut").GetBoolean());
        Assert.False(rumor.TryGetProperty("readerMark", out _));
        var plain = Backlinks(author).Single(b => b.GetProperty("path").GetString() == Path && b.GetProperty("section").GetString() == "Plain");
        Assert.False(plain.TryGetProperty("leftOut", out _));

        var readers = await server.GetJsonAsync(Readers(Tithe, "ember:1"), HttpStatusCode.OK);
        Assert.Equal("Plain", Assert.Single(Backlinks(readers), b => b.GetProperty("path").GetString() == Path).GetProperty("section").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-102")]
    public async Task ABacklinkInAContainersOwnTextHasNoMark()
    {
        var universe = _den.Read("universes/starfall/universe.md");
        _den.Write("universes/starfall/universe.md", universe.TrimEnd() + "\n\nThe calendar turns on [[The salt tithe]].\n");
        await using var server = await Start();

        var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}&marksAt=ember:1", HttpStatusCode.OK);

        var link = Backlinks(page).Single(b => b.GetProperty("path").GetString() == "universes/starfall/universe.md");
        Assert.Equal("container_body", link.GetProperty("site").GetString());
        Assert.False(link.TryGetProperty("readerMark", out _));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-102")]
    public async Task WithoutMarksAtABacklinkHasNoMark()
    {
        await using var server = await Start();

        foreach (var query in new[] { "", "&asOf=ash:3", "&asOf=ash:3&readers=true" })
        {
            var page = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={Tithe}{query}", HttpStatusCode.OK);
            Assert.DoesNotContain("readerMark", page.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-102")]
    public async Task AtEveryPositionABacklinkWithNoMarkIsOneAReaderSees()
    {
        await using var server = await Start();
        var positions = await server.GetJsonAsync("/api/positions?universe=starfall", HttpStatusCode.OK);

        foreach (var at in positions.GetProperty("positions").EnumerateArray().Select(p => p.GetProperty("position").GetString()!))
        {
            foreach (var name in new[] { Tithe, "Ilse%20Tarrow", "Halden%20Pike", "The%20missing%20manifest", "01JA7Q8M4XK3V9R2D5W8HN6TQF" })
            {
                var strict = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={name}&asOf={at}", HttpStatusCode.OK);
                if (!strict.GetProperty("inView").GetBoolean())
                {
                    continue;
                }

                var marked = await server.GetJsonAsync($"/api/entity?universe=starfall&entity={name}&marksAt={at}", HttpStatusCode.OK);
                Assert.Equal(
                    Backlinks(strict).Select(Place).Order(StringComparer.Ordinal),
                    Backlinks(marked).Where(ReachesAReader).Select(Place).Order(StringComparer.Ordinal));
            }
        }

        static string Place(JsonElement b) => $"{b.GetProperty("path").GetString()}:{b.GetProperty("line").GetInt32()}:{b.GetProperty("column").GetInt32()}";

        static bool ReachesAReader(JsonElement b) =>
            !b.TryGetProperty("readerMark", out _)
            && !b.TryGetProperty("leftOut", out _)
            && b.GetProperty("site").GetString() != "container_body"
            && !b.GetProperty("bookkeeping").GetBoolean()
            && (b.GetProperty("status").ValueKind == JsonValueKind.Null || b.GetProperty("status").GetString() is "draft" or "canon");
    }
}

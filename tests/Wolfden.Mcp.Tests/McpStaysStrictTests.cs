using System.Diagnostics;
using System.IO.Pipelines;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Wolfden.Git;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// What an assistant can see, end to end, before the web UI changes to show
/// everything and tag it (#300). A real MCP client calls the real server over
/// a pair of pipes, against an invented den where everything a reader at the
/// position doesn't know is a word nothing else in the den uses. The tests
/// look at the whole of each response, so a new member that carries later
/// content fails them whatever it's called.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FutureUntilTests"/> and <see cref="FuturePositionFieldTests"/>
/// pin each rule's details, field by field. These pin the sum of them: nothing
/// new came through. If the marked view needs one of these to change, that's a
/// change to what agents see, which the maintainer decided against on #300.
/// </para>
/// <para>
/// <b>Tools with no position aren't filtered, and that's on purpose.</b> They
/// aren't story views, and each tool's description says so:
/// </para>
/// <list type="bullet">
/// <item><c>search</c> finds text of every status at any point in the story,
/// ideas and later reveals included. A match in a section an error leaves out
/// says <c>left_out: true</c>, and its snippet has the text.</item>
/// <item><c>list_entities</c> lists entities of every status with where each one
/// enters canon, so it names notes the reader meets later. A note an error leaves
/// out isn't listed.</item>
/// <item><c>get_history</c> gives checkpoint messages and diffs of the file as
/// written, so it has later facts, <c>until</c>s, and sections an error leaves
/// out.</item>
/// <item><c>check</c>, <c>get_chapter</c>, and <c>get_chapter_defaults</c> take
/// no position either. They're read tools over the files, and only the argument
/// lists pin them here.</item>
/// </list>
/// <para>
/// So they stay as they are, with no marks: only <c>get_entity</c> with
/// <c>as_of</c> and <c>canon_as_of</c> are reads at a position.
/// </para>
/// </remarks>
public sealed class McpStaysStrictTests : IAsyncLifetime
{
    private const string Tithe = "The salt tithe";
    private const string TithePath = "universes/starfall/entities/threads/the-salt-tithe.md";
    private const string TitheId = "01M4TF5F0EKGGWD5FNVBAEY8Z7";
    private const string Heir = "The salt heir";
    private const string HeirPath = "universes/starfall/entities/notes/the-salt-heir.md";
    private const string HeirId = "01M4TF5F0EKGGWD5FNVBAEY8Z8";
    private const string BrinePath = "universes/starfall/entities/notes/the-brine-oath.md";
    private const string BrineId = "01M4TF5F0EKGGWD5FNVBAEY8Z9";

    private const string TitheText = $$"""
        ---
        id: {{TitheId}}
        type: thread
        name: The salt tithe
        aliases: [The toll]
        status: canon
        from: "ember:1"
        fields:
          planted: { value: ["ember:1", "ash:2"], pin: true }
          paid_off: { value: "ash:3", pin: true }
          echoes: "ash:4"
          keeper:
            - { value: Orrin Vale, from: "ember:1", until: "ash:2", pin: true }
            - { value: Morvane, from: "ash:2" }
          rumor: { value: Cinderlace, status: idea }
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
        <!-- wolfden: status=idea -->
        Hallowmere might own the toll.

        ## Zanthic reveal
        <!-- wolfden: status=maybe pin=true -->
        Vespergale wrote the toll.

        ### Ostrelline
        Under the reveal.

        ## Session notes
        Pellucid drafts ahead.

        """;

    /// <summary>Every word a reader at <c>ember:3</c> hasn't met, each from one place above or below.</summary>
    private static readonly string[] LaterWords =
    [
        "Morvane", // A later entry of a field.
        "Sablewick", "The audit", // A later subsection.
        "Drennick", // A thread's Payoff, which enters at paid_off.
        "Cinderlace", // A field entry that's an idea.
        "Tamsin", "Hallowmere", // A section that's an idea.
        "Zanthic", "Vespergale", "Ostrelline", // A section and subsection an error leaves out.
        "Pellucid", "Session notes", // A bookkeeping section.
        "The salt heir", "Ysmerine", "the-salt-heir", HeirId, "The heir apparent", // A note the reader meets later.
        "The dry well", "Glimmerfen", // An idea.
        "The old pact", "Thornmarch", // A retconned note.
        "The brine oath", "the-brine-oath", "Corvantis", // A note an error leaves out.
        "Halden Pike", "Tarnhollow", // Notes filed in the later book, so they enter there.
        "ash:", "a-quiet-moon:", // Every position later than the first book.
    ];

    /// <summary>Whatever a marked view might say or be called, in the spellings a member or a tag would use.</summary>
    private static readonly string[] Marks =
    [
        "ahead of readers", "ahead_of_readers", "aheadofreaders", "ahead-of-readers",
        "no longer true", "no_longer_true", "nolongertrue",
        "enters at", "enters_at", "entersat",
        "marks_at", "marksat", "marks-at", "reader_marks", "readermarks", "reader-marks",
        "include_ahead", "ahead_of", "aheadof", "reader's view", "author's view",
    ];

    private readonly TempDen _den = TempDen.Starfall();
    private readonly StringWriter _log = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Pipe _toServer = new();
    private readonly Pipe _toClient = new();
    private readonly string _gitConfig = Path.Combine(Path.GetTempPath(), "wolfden-mcp-tests", $"gitconfig-{Guid.NewGuid():N}");
    private Task? _server;
    private McpClient? _client;

    private McpClient Client => _client!;

    private GitOptions Git => new()
    {
        Environment = new Dictionary<string, string?>
        {
            ["GIT_CONFIG_GLOBAL"] = _gitConfig,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CEILING_DIRECTORIES"] = Path.Combine(Path.GetTempPath(), "wolfden-mcp-tests"),
            ["GIT_DIR"] = null,
            ["GIT_WORK_TREE"] = null,
            ["GIT_INDEX_FILE"] = null,
        },
    };

    public async ValueTask InitializeAsync()
    {
        File.WriteAllText(
            _gitConfig,
            "[user]\n\tname = Test Writer\n\temail = writer@example.com\n[commit]\n\tgpgsign = false\n[tag]\n\tgpgsign = false\n[core]\n\tautocrlf = false\n",
            new UTF8Encoding(false));

        _den.Write("packs/starfall/extensions/thread.yaml", "fields:\n  echoes: { type: position }\n");
        _den.Write(TithePath, TitheText);
        _den.Write(
            HeirPath,
            $"""
            ---
            id: {HeirId}
            type: note
            name: {Heir}
            aliases: [The heir apparent, The toll]
            status: canon
            from: "ash:3"
            fields:
              claim: {"{"} value: Ysmerine, pin: true {"}"}
            ---

            Ysmerine inherits the toll.
            """);
        _den.Write("universes/starfall/entities/notes/the-dry-well.md", "---\ntype: note\nname: The dry well\nstatus: idea\n---\n\nGlimmerfen, perhaps.\n");
        _den.Write("universes/starfall/entities/notes/the-old-pact.md", "---\ntype: note\nname: The old pact\nstatus: retconned\n---\n\nThornmarch signed it.\n");
        _den.Write(BrinePath, $"---\nid: {BrineId}\ntype: note\nname: The brine oath\nstatus: maybe\n---\n\nCorvantis swore it.\n");

        // A note filed in the later book with no from, as the Starfall den's Halden Pike has one.
        _den.Write("universes/starfall/series/long-dark/books/ash/entities/locations/tarnhollow.md", "---\ntype: location\nname: Tarnhollow\nstatus: canon\n---\n\nA later place.\n");

        var transport = new StreamServerTransport(_toServer.Reader.AsStream(), _toClient.Writer.AsStream(), "wolfden");
        var tools = new DenTools(_den.Root) { History = () => GitHistory.ForDen(_den.Root, Git) };
        _server = WolfdenMcpServer.RunAsync(transport, tools, "0.1.0-test", _log, watch: false, _stop.Token);
        _client = await McpClient.CreateAsync(
            new StreamClientTransport(_toServer.Writer.AsStream(), _toClient.Reader.AsStream()),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        await _stop.CancelAsync();
        try
        {
            await _server!;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
        _den.Dispose();
        try
        {
            File.Delete(_gitConfig);
        }
        catch (IOException)
        {
            // Best effort; the OS cleans its temp folder.
        }
    }

    // --- The den really holds what's kept out, so "it isn't there" says something. ---

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task AFullReadHasWhatAReadAtAPositionLeavesOut()
    {
        var full = await Call("get_entity", new() { ["entity"] = Tithe });

        foreach (var word in new[] { "Morvane", "Sablewick", "Drennick", "Cinderlace", "Tamsin", "Zanthic", "Vespergale", "Ostrelline", "Pellucid", "ash:2", "ash:3", "ash:4" })
        {
            Assert.Contains(word, full.Whole, StringComparison.Ordinal);
        }

        Assert.Contains("entity.fields.until", full.Keys);
        Assert.Contains("entity.fields.entry_index", full.Keys);
        Assert.Contains("entity.sections.until", full.Keys);
        Assert.Contains("entity.sections.left_out", full.Keys);
        Assert.Contains("entity.sections.bookkeeping", full.Keys);
        Assert.Contains("entity.aliases", full.Keys);
        Assert.Contains(full.Json.GetProperty("entity").GetProperty("fields").EnumerateArray(), f => f.GetProperty("name").GetString() == "echoes");

        // The heir is in view once the reader reaches it.
        var heir = await Call("get_entity", new() { ["entity"] = Heir, ["as_of"] = "ash:3" });
        Assert.True(heir.Json.GetProperty("in_view").GetBoolean());
        Assert.Contains("Ysmerine", heir.Whole, StringComparison.Ordinal);
    }

    // --- Reads at a position: the whole response. ---

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task GetEntityAtAPositionIsExactlyThis()
    {
        // The whole answer, to the member: what's in it is what an assistant drafting ember:3 knows. The hash is of the file, so it's taken from the file's read.
        var reply = await Call("get_entity", new() { ["entity"] = Tithe, ["as_of"] = "ember:3" });

        var expected = JsonNode.Parse(
            $$"""
            {
              "version": 1,
              "view": "canon_as_of",
              "position": "ember:3",
              "in_view": true,
              "entity": {
                "key": "{{TitheId}}",
                "id": "{{TitheId}}",
                "name": "The salt tithe",
                "type": "thread",
                "status": "canon",
                "universe": "starfall",
                "scope_kind": "universe",
                "scope_key": "starfall",
                "path": "{{TithePath}}",
                "from": "ember:1",
                "lead": "# The salt tithe\n\nEvery ship pays it at the Reach.",
                "pinned": {
                  "fields": [
                    { "name": "planted", "value": ["ember:1"], "kind": "entry", "status": "canon", "pinned": true, "line": 9 },
                    { "name": "keeper", "value": "Orrin Vale", "kind": "temporal", "status": "canon", "pinned": true, "from": "ember:1", "line": 13 }
                  ],
                  "sections": [
                    { "name": "Terms", "level": 2, "heading_path": ["Terms"], "status": "canon", "pinned": true, "from": "ember:1", "line": 29, "body": "Paid each season." }
                  ]
                },
                "fields": [
                  { "name": "planted", "value": ["ember:1"], "kind": "entry", "status": "canon", "pinned": true, "line": 9 },
                  { "name": "keeper", "value": "Orrin Vale", "kind": "temporal", "status": "canon", "pinned": true, "from": "ember:1", "line": 13 }
                ],
                "sections": [
                  { "name": "Overview", "level": 2, "heading_path": ["Overview"], "status": "canon", "pinned": false, "line": 22, "body": "A toll in salt." },
                  { "name": "Terms", "level": 2, "heading_path": ["Terms"], "status": "canon", "pinned": true, "from": "ember:1", "line": 29, "body": "Paid each season." }
                ],
                "hash": "{{reply.Json.GetProperty("entity").GetProperty("hash").GetString()}}"
              },
              "den_notice": "This den has 2 errors, so some notes or sections may be missing from this answer. Call check with severity 'error' to see them."
            }
            """);

        var actual = JsonNode.Parse(reply.Text);
        Assert.True(JsonNode.DeepEquals(expected, actual), $"The response was:\n{actual}");
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-47")]
    [InlineData("ember:1")]
    [InlineData("ember:2b")]
    [InlineData("ember:3")]
    public async Task GetEntityAtAPositionHasNothingLater(string position)
    {
        var reply = await Call("get_entity", new() { ["entity"] = Tithe, ["as_of"] = position });

        Assert.True(reply.Json.GetProperty("in_view").GetBoolean());
        AssertNothingLater(reply);
        AssertNoMarks(reply);
        AssertOnlyKeys(EntityResultKeys, reply);
        Assert.DoesNotContain("until", reply.Whole, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("left out", reply.Json.GetProperty("entity").GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-47")]
    [InlineData("ember", false)]
    [InlineData("ember:1", false)]
    [InlineData("ember:3", false)]
    [InlineData("ember", true)]
    [InlineData("ember:3", true)]
    public async Task CanonAsOfAtAPositionHasNothingLater(string position, bool brief)
    {
        var reply = await Call("canon_as_of", new() { ["position"] = position, ["brief"] = brief });

        AssertNothingLater(reply);
        AssertNoMarks(reply);
        AssertOnlyKeys(CanonResultKeys, reply);
        Assert.DoesNotContain("until", reply.Whole, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-47")]
    [InlineData("series", "long-dark")]
    [InlineData("book", "ember")]
    [InlineData("book", "ash")] // A filter to a later book doesn't reach ahead of the position.
    [InlineData("type", "thread")]
    public async Task AFilterDoesntLetAnythingLaterIn(string filter, string value)
    {
        var reply = await Call("canon_as_of", new() { ["position"] = "ember:3", [filter] = value });

        AssertNothingLater(reply);
        AssertNoMarks(reply);
        AssertOnlyKeys(CanonResultKeys, reply);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task EveryPinnedFactInACanonViewIsOneTheReaderHasReached()
    {
        var full = await Call("canon_as_of", new() { ["position"] = "ember:3" });
        var brief = await Call("canon_as_of", new() { ["position"] = "ember:3", ["brief"] = true });

        foreach (var reply in new[] { full, brief })
        {
            var tithe = reply.Json.GetProperty("pinned").EnumerateArray().Single(p => p.GetProperty("name").GetString() == Tithe);
            Assert.Equal(["planted", "keeper"], tithe.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()));
            Assert.Equal(["ember:1"], tithe.GetProperty("fields")[0].GetProperty("value").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal(["Terms"], tithe.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        }

        // Brief lists entities with nothing of their content; pinned canon is still given in full.
        var entity = brief.Json.GetProperty("entities").EnumerateArray().Single(e => e.GetProperty("name").GetString() == Tithe);
        Assert.False(entity.TryGetProperty("fields", out _));
        Assert.False(entity.TryGetProperty("sections", out _));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task PartWayThroughTheStoryOnlyWhatsAheadOfThatPointGoes()
    {
        var entity = await Call("get_entity", new() { ["entity"] = Tithe, ["as_of"] = "ash:2" });
        var canon = await Call("canon_as_of", new() { ["position"] = "ash:2" });
        var brief = await Call("canon_as_of", new() { ["position"] = "ash:2", ["brief"] = true });

        // What's arrived by then is there, and has no end date, and the fact whose date has come is gone.
        var fields = entity.Json.GetProperty("entity").GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal("Morvane", fields.Single(f => f.GetProperty("name").GetString() == "keeper").GetProperty("value").GetString());
        Assert.Equal(["ember:1", "ash:2"], fields.Single(f => f.GetProperty("name").GetString() == "planted").GetProperty("value").EnumerateArray().Select(v => v.GetString()));
        Assert.Contains("Sablewick", entity.Whole, StringComparison.Ordinal);
        Assert.DoesNotContain("Paid each season", entity.Whole, StringComparison.Ordinal);

        foreach (var reply in new[] { entity, canon, brief })
        {
            foreach (var later in new[] { "ash:3", "ash:4", "ash:epilogue", "a-quiet-moon:", "Drennick", "Ysmerine", "The salt heir", "Zanthic", "Vespergale", "Ostrelline", "Pellucid", "Tamsin", "Hallowmere", "Cinderlace" })
            {
                Assert.DoesNotContain(later, reply.Whole, StringComparison.Ordinal);
            }

            AssertNoMarks(reply);
        }

        AssertOnlyKeys(EntityResultKeys, entity);
        AssertOnlyKeys(CanonResultKeys, canon);
        AssertOnlyKeys(CanonResultKeys, brief);
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-47")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EMBER:3")]
    [InlineData(" ember:3 ")]
    [InlineData("ember:99")]
    [InlineData("tide:1")] // A book of another universe.
    public async Task APositionThatCantBeReadIsRefusedAndNotAFullRead(string position)
    {
        var entity = await Call("get_entity", new() { ["entity"] = Tithe, ["as_of"] = position }, error: true);
        var canon = await Call("canon_as_of", new() { ["position"] = position }, error: true);

        foreach (var reply in new[] { entity, canon })
        {
            Assert.Equal("bad_position", reply.Json.GetProperty("error").GetProperty("code").GetString());
            AssertNothingLater(reply);
            AssertNoMarks(reply);
        }
    }

    // --- A note that's left out reads as not there. ---

    [Theory]
    [Trait("Scenario", "SC-MCP-47")]
    [InlineData(Heir)] // Met later, by name,
    [InlineData("The heir apparent")] // by an alias only it has,
    [InlineData("the-salt-heir")] // by its file name,
    [InlineData(HeirId)] // by its id,
    [InlineData(HeirPath)] // and by its path.
    [InlineData("The dry well")] // An idea.
    [InlineData("The old pact")] // Retconned.
    public async Task ANoteOutOfTheViewIsNotInViewAndNothingElse(string name)
    {
        var reply = await Call("get_entity", new() { ["entity"] = name, ["as_of"] = "ember:3" });

        // The note only repeats what was asked for: the name given is the one thing it says.
        var expected = JsonNode.Parse(
            $$"""
            {
              "version": 1,
              "view": "canon_as_of",
              "position": "ember:3",
              "in_view": false,
              "note": "'{{name}}' isn't in canon as of ember:3: the reader hasn't met it by then, or it isn't draft or canon.",
              "den_notice": "This den has 2 errors, so some notes or sections may be missing from this answer. Call check with severity 'error' to see them."
            }
            """);
        var actual = JsonNode.Parse(reply.Text);
        Assert.True(JsonNode.DeepEquals(expected, actual), $"The response was:\n{actual}");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task ANoteAnErrorLeavesOutIsAnUnknownNameAtAPosition()
    {
        var oath = await Call("get_entity", new() { ["entity"] = "The brine oath", ["as_of"] = "ember:3" }, error: true);
        var nothing = await Call("get_entity", new() { ["entity"] = "The brine oat", ["as_of"] = "ember:3" }, error: true);

        Assert.Equal("unknown_entity", oath.Json.GetProperty("error").GetProperty("code").GetString());

        // Only what was asked for differs from a name no note has.
        Assert.Equal(
            nothing.Text.Replace("The brine oat", "The brine oath", StringComparison.Ordinal),
            oath.Text);
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-47")]
    [InlineData(BrinePath)]
    [InlineData(BrineId)]
    public async Task ANoteAnErrorLeavesOutNamedExactlySaysNothingOfItAtAPosition(string name)
    {
        // Whoever gives its path or id already knows of the file (DenReader.UnknownOrLeftOut), so it's told only that it can't be shown.
        var reply = await Call("get_entity", new() { ["entity"] = name, ["as_of"] = "ember:3" }, error: true);

        var error = reply.Json.GetProperty("error");
        Assert.Equal("unknown_entity", error.GetProperty("code").GetString());
        Assert.Equal($"'{name}' can't be shown as of ember:3: an error leaves it out of the den at every story position.", error.GetProperty("message").GetString());
        Assert.Equal(["code", "message"], error.EnumerateObject().Select(p => p.Name));
        foreach (var word in new[] { "The brine oath", "Corvantis", "status", "maybe", "F-" })
        {
            Assert.DoesNotContain(word, reply.Whole, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task ALaterNoteIsntAMatchForANameAtAPosition()
    {
        // The tithe and the heir share an alias, and only the tithe has been met. With no position the name is ambiguous, and with one it isn't.
        var met = await Call("get_entity", new() { ["entity"] = "The toll", ["as_of"] = "ember:3" });
        Assert.Equal(Tithe, met.Json.GetProperty("entity").GetProperty("name").GetString());
        AssertNothingLater(met);

        var full = await Call("get_entity", new() { ["entity"] = "The toll" }, error: true);
        Assert.Equal("ambiguous_entity", full.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(Heir, full.Whole, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task AnAmbiguousNameAtAPositionListsOnlyNotesTheReaderHasMet()
    {
        _den.Write("universes/starfall/entities/notes/the-reach-toll.md", "---\ntype: note\nname: The reach toll\naliases: [The toll]\nstatus: canon\nfrom: \"ember:1\"\n---\n\nAnother.\n");

        var reply = await Call("get_entity", new() { ["entity"] = "The toll", ["as_of"] = "ember:3" }, error: true);

        var error = reply.Json.GetProperty("error");
        Assert.Equal("ambiguous_entity", error.GetProperty("code").GetString());
        Assert.Equal(["The reach toll", Tithe], error.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).Order(StringComparer.Ordinal));
        AssertNothingLater(reply);
    }

    // --- The keys. ---

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task TheKeysOfAReadAtAPositionAreExactlyThese()
    {
        var inView = await Call("get_entity", new() { ["entity"] = Tithe, ["as_of"] = "ember:3" });
        var notInView = await Call("get_entity", new() { ["entity"] = Heir, ["as_of"] = "ember:3" });
        Assert.Equal(EntityResultKeys.Order(StringComparer.Ordinal), inView.Keys.Union(notInView.Keys).Order(StringComparer.Ordinal));

        var canon = await Call("canon_as_of", new() { ["position"] = "ember:3", ["series"] = "long-dark" });
        var brief = await Call("canon_as_of", new() { ["position"] = "ember:3", ["brief"] = true });
        Assert.Equal(CanonResultKeys.Order(StringComparer.Ordinal), canon.Keys.Union(brief.Keys).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task OnlyTheTwoStoryViewsTakeAPositionAndNoToolTakesAMark()
    {
        var tools = await Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var arguments = tools.ToDictionary(
            t => t.Name,
            t => t.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList());

        Assert.Equal(["entity", "universe", "as_of"], arguments["get_entity"]);
        Assert.Equal(["position", "universe", "series", "book", "type", "brief"], arguments["canon_as_of"]);
        Assert.Equal(["query", "universe", "type", "canon_only", "limit", "field"], arguments["search"]);
        Assert.Equal(["universe", "type", "status", "series", "book", "limit"], arguments["list_entities"]);
        Assert.Equal(["entity", "universe", "limit", "before", "diff", "diff_from"], arguments["get_history"]);
        Assert.Equal(["severity", "path", "limit", "universe"], arguments["check"]);
        Assert.Equal(["book", "chapter", "universe"], arguments["get_chapter"]);
        Assert.Equal(["universe", "series", "book"], arguments["get_chapter_defaults"]);

        Assert.Equal(["as_of", "position"], arguments.Values.SelectMany(a => a).Where(IsPositionArgument).Distinct().Order(StringComparer.Ordinal));
        Assert.DoesNotContain(arguments.Values.SelectMany(a => a), a => a.Contains("mark", StringComparison.OrdinalIgnoreCase) || a.Contains("ahead", StringComparison.OrdinalIgnoreCase) || a.Contains("reader", StringComparison.OrdinalIgnoreCase));

        static bool IsPositionArgument(string name) => name is "as_of" or "at" or "position";
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task NoToolSaysItCanMarkAnything()
    {
        var tools = await Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        foreach (var text in tools.Select(t => t.Description).Append(Client.ServerInstructions))
        {
            // "enters at" is everyday wording in a description, and only a mark in a response.
            foreach (var mark in Marks.Where(m => m != "enters at"))
            {
                Assert.DoesNotContain(mark, text ?? "", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // --- Tools with no position stay as they are: see the remarks on this class. ---

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task SearchHasNoPositionSoItFindsLaterMaterialUnmarked()
    {
        var later = await Call("search", new() { ["query"] = "Sablewick" });
        Assert.Equal(1, later.Json.GetProperty("count").GetInt32());
        Assert.Equal("The audit", later.Json.GetProperty("hits")[0].GetProperty("section").GetString());

        var note = await Call("search", new() { ["query"] = "Ysmerine" });
        Assert.Equal(Heir, note.Json.GetProperty("hits")[0].GetProperty("entity").GetProperty("name").GetString());

        var idea = await Call("search", new() { ["query"] = "Hallowmere" });
        Assert.Equal("idea", idea.Json.GetProperty("hits")[0].GetProperty("status").GetString());
        Assert.False(idea.Json.GetProperty("hits")[0].GetProperty("in_canon").GetBoolean());

        var leftOut = await Call("search", new() { ["query"] = "Vespergale" });
        Assert.True(leftOut.Json.GetProperty("hits")[0].GetProperty("left_out").GetBoolean());
        Assert.Contains("Vespergale", leftOut.Json.GetProperty("hits")[0].GetProperty("snippet").GetString(), StringComparison.Ordinal);

        foreach (var reply in new[] { later, note, idea, leftOut })
        {
            AssertNoMarks(reply);
            AssertOnlyKeys(SearchResultKeys, reply);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task ListEntitiesHasNoPositionSoItNamesLaterNotesUnmarked()
    {
        var reply = await Call("list_entities", new());

        var byName = reply.Json.GetProperty("entities").EnumerateArray().ToLookup(e => e.GetProperty("name").GetString()!).ToDictionary(g => g.Key, g => g.First());
        Assert.Equal("ash:3", byName[Heir].GetProperty("from").GetString());
        Assert.Equal("idea", byName["The dry well"].GetProperty("status").GetString());
        Assert.Equal("retconned", byName["The old pact"].GetProperty("status").GetString());
        Assert.DoesNotContain("The brine oath", byName.Keys);

        AssertNoMarks(reply);
        AssertOnlyKeys(ListResultKeys, reply);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task GetHistoryHasNoPositionSoItShowsTheFileAsWrittenUnmarked()
    {
        // The tithe arrives in a second checkpoint, so a diff from the first is the whole file.
        File.Delete(Path.Combine(_den.Root, TithePath));
        RunGit("init", "--quiet");
        RunGit("add", "--all");
        RunGit("commit", "--quiet", "-m", "Start the Starfall notes");
        var start = RunGit("rev-parse", "HEAD").Trim();
        _den.Write(TithePath, TitheText);
        RunGit("add", "--all");
        RunGit("commit", "--quiet", "-m", "Add the salt tithe");

        var reply = await Call("get_history", new() { ["entity"] = Tithe, ["diff_from"] = start });

        Assert.True(reply.Json.GetProperty("available").GetBoolean());
        Assert.Equal("Add the salt tithe", reply.Json.GetProperty("entries")[0].GetProperty("message").GetString()!.Trim());
        var patch = reply.Json.GetProperty("diff").GetProperty("files")[0].GetProperty("patch").GetString();
        foreach (var written in new[] { "until=ash:2", "paid_off", "ash:3", "Morvane", "Zanthic reveal", "Vespergale", "Hallowmere", "Pellucid" })
        {
            Assert.Contains(written, patch, StringComparison.Ordinal);
        }

        AssertNoMarks(reply);
        AssertOnlyKeys(HistoryResultKeys, reply);
    }

    // --- The separation in the code: the web's marked view can't reach an assistant by accident. ---

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public void TheMcpProjectDoesntReachTheWebApiThroughAnyProject()
    {
        // The web API's marks (marksAt, ReaderMarks) go in Wolfden.Api, so a Wolfden.Mcp that can't see that project can't serve them.
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(Path.Combine(TempDen.RepoRoot, "src", "Wolfden.Mcp", "Wolfden.Mcp.csproj"));

        Assert.Contains("Wolfden.Core", reached);
        Assert.Contains("Wolfden.Index", reached);
        Assert.DoesNotContain("Wolfden.Api", reached);
        Assert.DoesNotContain("Wolfden.Cli", reached);

        void Walk(string project)
        {
            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));
                if (reached.Add(Path.GetFileNameWithoutExtension(path)))
                {
                    Walk(path);
                }
            }
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public void WhatAnAssistantRunsOnDoesntReferenceTheWebApi()
    {
        foreach (var assembly in AssistantAssemblies)
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name is "Wolfden.Api" or "Wolfden.Cli");
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public void TheCodeAnAssistantRunsOnHasNoMarkedView()
    {
        // If marks move into shared code they could reach MCP's results, so that takes a decision, not just a move.
        var types = AssistantAssemblies.SelectMany(a => a.GetTypes()).Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false));
        Assert.DoesNotContain(types, t => t.Name.Contains("ReaderMarks", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("MarksAt", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("AheadOfReaders", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-47")]
    [InlineData(typeof(GetEntityResult))]
    [InlineData(typeof(CanonAsOfResult))]
    [InlineData(typeof(SearchResult))]
    [InlineData(typeof(ListEntitiesResult))]
    [InlineData(typeof(GetHistoryResult))]
    public void NoResultCanCarryAMark(Type result)
    {
        var seen = new HashSet<Type>();
        var names = new List<string>();
        Walk(result);

        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, n => n.Contains("mark", StringComparison.OrdinalIgnoreCase) || n.Contains("ahead", StringComparison.OrdinalIgnoreCase) || n.Contains("reader", StringComparison.OrdinalIgnoreCase));

        void Walk(Type type)
        {
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    Walk(argument);
                }

                return;
            }

            if (type.Namespace is null || !type.Namespace.StartsWith("Wolfden", StringComparison.Ordinal) || !seen.Add(type))
            {
                return;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => !p.IsDefined(typeof(JsonIgnoreAttribute))))
            {
                names.Add(property.Name);
                Walk(property.PropertyType);
            }
        }
    }

    private static Assembly[] AssistantAssemblies =>
        [typeof(WolfdenTools).Assembly, typeof(DenTools).Assembly, typeof(Wolfden.Core.Types.TypeDefinition).Assembly];

    // --- What the responses may hold. ---

    private static readonly string[] SummaryKeys = ["key", "id", "name", "type", "status", "universe", "scope_kind", "scope_key", "path", "from"];

    private static readonly string[] FieldKeys = ["name", "value", "kind", "status", "pinned", "from", "line"];

    private static readonly string[] SectionKeys = ["name", "level", "heading_path", "status", "pinned", "from", "line", "body"];

    private static string[] Under(string prefix, params IEnumerable<string>[] keys) =>
        [.. keys.SelectMany(k => k).Select(k => prefix.Length == 0 ? k : $"{prefix}.{k}")];


    /// <summary>
    /// The content of an entity at a position. A field's <c>value</c> is the
    /// writer's, whatever keys a mapping of theirs has. There's no <c>until</c>
    /// (F-VIEW-4), and none of the full view's <c>aliases</c>, <c>entry_index</c>,
    /// <c>live</c>, <c>bookkeeping</c>, or <c>left_out</c> (F-GEN-2).
    /// </summary>
    private static string[] Content(string entity) =>
    [
        .. Under(entity, ["lead", "fields", "sections"]),
        .. Under($"{entity}.fields", FieldKeys),
        .. Under($"{entity}.sections", SectionKeys),
    ];

    private static readonly string[] EntityResultKeys =
    [
        "version", "view", "position", "in_view", "note", "den_notice", "entity",
        .. Under("entity", SummaryKeys, ["hash", "pinned", "pinned.fields", "pinned.sections"]),
        .. Under("entity.pinned.fields", FieldKeys),
        .. Under("entity.pinned.sections", SectionKeys),
        .. Content("entity"),
    ];

    private static readonly string[] CanonResultKeys =
    [
        "version", "universe", "position", "at", "filter", "filter.kind", "filter.key", "count", "den_notice", "pinned", "entities",
        .. Under("pinned", ["key", "name", "type", "fields", "sections"]),
        .. Under("pinned.fields", FieldKeys),
        .. Under("pinned.sections", SectionKeys),
        .. Under("entities", SummaryKeys),
        .. Content("entities"),
    ];

    private static readonly string[] SearchResultKeys =
    [
        "version", "query", "count", "truncated", "den_notice", "hits",
        .. Under("hits", ["entity", "section", "line", "snippet", "status", "in_canon", "in_comment", "field", "left_out"]),
        .. Under("hits.entity", SummaryKeys),
    ];

    private static readonly string[] ListResultKeys =
    [
        "version", "count", "total", "truncated", "den_notice", "entities",
        .. Under("entities", SummaryKeys),
    ];

    private static readonly string[] HistoryChangeKeys = ["kind", "path", "old_path"];

    private static readonly string[] HistoryResultKeys =
    [
        "version", "entity", "backend", "available", "reason", "count", "truncated", "next_before", "entries", "diff", "note", "den_notice",
        .. Under("entity", SummaryKeys),
        .. Under("entries", ["id", "when", "author", "message", "changes"]),
        .. Under("entries.changes", HistoryChangeKeys),
        .. Under("diff", ["from", "to", "files"]),
        .. Under("diff.files", ["change", "patch"]),
        .. Under("diff.files.change", HistoryChangeKeys),
    ];

    private static void AssertOnlyKeys(string[] allowed, Reply reply) => Assert.Empty(reply.Keys.Except(allowed, StringComparer.Ordinal));

    private static void AssertNothingLater(Reply reply)
    {
        foreach (var word in LaterWords)
        {
            Assert.DoesNotContain(word, reply.Whole, StringComparison.Ordinal);
        }
    }

    private static void AssertNoMarks(Reply reply)
    {
        foreach (var mark in Marks)
        {
            Assert.DoesNotContain(mark, reply.Whole, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(reply.Keys, k => k.Contains("mark", StringComparison.OrdinalIgnoreCase) || k.Contains("ahead", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Reply> Call(string tool, Dictionary<string, object?> args, bool error = false)
    {
        // The den has two universes, and a position's book settles which only for the tools that take one.
        args.TryAdd("universe", "starfall");
        var result = await Client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(error, result.IsError == true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var document = JsonDocument.Parse(text);

        // The two forms of the answer are the same, so looking at one looks at both.
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(text), JsonNode.Parse(structured.GetRawText())), "The text and the structured content differ.");
        var json = document.RootElement.Clone();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        CollectKeys(json, "", keys);
        return new Reply(json, text, text + "\n" + structured.GetRawText(), keys);
    }

    /// <summary>Every member's dotted path in <paramref name="element"/>, leaving out what's inside a field's <c>value</c>, which is the writer's.</summary>
    private static void CollectKeys(JsonElement element, string path, HashSet<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                keys.Add(key);
                if (property.Name != "value")
                {
                    CollectKeys(property.Value, key, keys);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectKeys(item, path, keys);
            }
        }
    }

    private string RunGit(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = _den.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        foreach (var (name, value) in Git.Environment!)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {error.GetAwaiter().GetResult()}");
        return output;
    }

    /// <summary>A tool's answer: its JSON, its text, its text and structured content together, and its members.</summary>
    private sealed record Reply(JsonElement Json, string Text, string Whole, HashSet<string> Keys);
}

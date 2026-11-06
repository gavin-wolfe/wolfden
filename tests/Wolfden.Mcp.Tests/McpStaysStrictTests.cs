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
/// position doesn't know is a word nothing else in the den uses.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FutureUntilTests"/> and <see cref="FuturePositionFieldTests"/>
/// pin each rule's details, field by field. These pin the sum of them: nothing
/// new came through. If the marked view needs one of these to change, that's a
/// change to what agents see, which the maintainer decided against on #300.
/// </para>
/// <para>
/// <b>What's looked at.</b> Each answer's text, structured content, and
/// <c>_meta</c>, the tools and their arguments, and that the server offers no
/// prompts or resources. A new member of a position read's answer fails whatever
/// it's called, but what's inside a field's <c>value</c> is the writer's, so
/// it isn't checked by member. The checks on the code's names are a tripwire for
/// the names expected: the checks on the answers are the real guard.
/// </para>
/// <para>
/// <b>What a read at a position doesn't keep out,</b> as designed (format.md
/// §9.5): text the writer put in a lead or section, including <c>[[links]]</c> to
/// notes the reader hasn't met, though not their HTML comments (F-VIEW-6) unless
/// shown as code; a field the type doesn't define, a <c>ref</c> field's value, any
/// field's value with a comment written in it, and a mapping with its own
/// <c>until</c> key; <c>line</c> numbers, which leave gaps where something's hidden;
/// and <c>kind: temporal</c>, which shows that a dated field changes over time. And
/// <c>in_view: false</c>, unlike <c>unknown_entity</c>, confirms a note exists.
/// </para>
/// <para>
/// <b>Reads with no position aren't filtered, and that's on purpose.</b> They
/// aren't story views, and their descriptions say so:
/// </para>
/// <list type="bullet">
/// <item><c>search</c> with no <c>as_of</c> finds text of every status at any point
/// in the story, ideas and later reveals included. A match in a section an error
/// leaves out says <c>left_out: true</c>, and its snippet has the text.</item>
/// <item><c>list_entities</c> with no <c>as_of</c> lists entities of every status
/// with where each one enters canon, so it names notes the reader meets later. A
/// note an error leaves out isn't listed.</item>
/// <item><c>get_history</c> gives checkpoint messages and diffs of the file as
/// written, so it has later facts, <c>until</c>s, and sections an error leaves
/// out.</item>
/// <item><c>check</c>, <c>get_chapter</c>, and <c>get_chapter_defaults</c> take
/// no position either. Only their argument lists are pinned here: what they
/// return isn't looked at.</item>
/// </list>
/// <para>
/// So they stay as they are, with no marks. The reads at a position are
/// <c>canon_as_of</c>, and <c>get_entity</c>, <c>search</c>, and <c>list_entities</c>
/// with <c>as_of</c> (#67). <c>search</c>'s is the Reader's view's search, which is in
/// <c>Wolfden.Index</c>; its marks are in the web's Author's path, which isn't.
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
        known_as: [{ value: Wraithmoor, from: "ash:2" }]
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
          seal:
            - { value: Brackwater, from: "ash:3", pin: true }
          omen: { value: Larkspire, status: candidate }
          old_name: { value: Quillfeather, status: retconned }
        ---

        # The salt tithe

        Every ship pays it at the Reach. <!-- Nightquill seizes the Reach. -->

        ## Overview
        A toll in salt, until the ambush. The fleet enters at dawn, and what she says is no longer true. <!-- Nightquill ends it. -->

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

        ## The reckoning
        <!-- wolfden: from=ash:3 pin=true -->
        Fenwraith closes the toll.

        ## The ending
        <!-- wolfden: status=undecided -->
        Wyrmsedge is one way.

        ### Another way
        <!-- wolfden: status=candidate -->
        Elderwane is another.

        ## Old terms
        <!-- wolfden: status=retconned -->
        Gallowmere struck them.

        ## Session notes
        Pellucid drafts ahead.

        """;

    /// <summary>Every word a reader at <c>ember:3</c> hasn't met, each from one place above or below.</summary>
    private static readonly string[] LaterWords =
    [
        "Nightquill", // A writer's comment in a lead and a section.
        "Morvane", // A later entry of a field.
        "Sablewick", "The audit", // A later subsection.
        "Drennick", // A thread's Payoff, which enters at paid_off.
        "Cinderlace", "Larkspire", "Quillfeather", // Field entries out of canon: an idea, a candidate, retconned.
        "Brackwater", // A pinned field entry that enters later.
        "The reckoning", "Fenwraith", // A pinned section that enters later.
        "The ending", "Wyrmsedge", "Another way", "Elderwane", // An undecided section and a candidate under it.
        "Old terms", "Gallowmere", // A retconned section.
        "Tamsin", "Hallowmere", // A section that's an idea.
        "Zanthic", "Vespergale", "Ostrelline", // A section and subsection an error leaves out.
        "Pellucid", "Session notes", // A bookkeeping section.
        "The salt heir", "Ysmerine", "the-salt-heir", HeirId, "The heir apparent", // A note the reader meets later.
        "The dry well", "Glimmerfen", // An idea.
        "The old pact", "Thornmarch", // A retconned note.
        "The brine oath", "the-brine-oath", "Corvantis", // A note an error leaves out.
        "Halden Pike", "Tarnhollow", // Notes filed in the later book, so they enter there.
        "Wraithmoor", // A name readers learn later, of a note they've met (known_as, #67).
        "ash:", "a-quiet-moon:", // Every position later than the first book.
    ];

    /// <summary>The keys of the books later than the first, which a message can name bare as well as in a position.</summary>
    private static readonly string[] LaterBooks = ["ash", "a-quiet-moon"];

    /// <summary>What a marked view might be called, in the spellings a member or a tag would use. None of these is ordinary prose, so they're searched for anywhere.</summary>
    private static readonly string[] MarkPhrases =
    [
        "ahead of readers", "ahead_of_readers", "aheadofreaders", "ahead-of-readers",
        "no_longer_true", "nolongertrue", "enters_at", "entersat",
        "marks_at", "marksat", "marks-at", "reader_marks", "readermarks", "reader-marks", "include_ahead",
    ];

    /// <summary>The whole words of a member's name that would make it a mark. "Bookmark" and "markdown" aren't.</summary>
    private static readonly string[] MarkWords = ["mark", "marks", "marked", "ahead"];

    private const string DenNotice = "This den has 2 errors, so some notes or sections may be missing from this answer. Call check with severity 'error' to see them.";

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
            TestClient.Options(),
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

        foreach (var word in new[] { "Nightquill", "Morvane", "Sablewick", "Drennick", "Cinderlace", "Larkspire", "Quillfeather", "Brackwater", "Fenwraith", "Wyrmsedge", "Elderwane", "Gallowmere", "Tamsin", "Zanthic", "Vespergale", "Ostrelline", "Pellucid", "ash:2", "ash:3", "ash:4" })
        {
            Assert.Contains(word, full.Whole, StringComparison.Ordinal);
        }

        Assert.Contains("entity.fields.until", full.Keys);
        Assert.Contains("entity.fields.entry_index", full.Keys);
        Assert.Contains("entity.sections.until", full.Keys);
        Assert.Contains("entity.sections.left_out", full.Keys);
        Assert.Contains("entity.sections.bookkeeping", full.Keys);
        Assert.Contains("entity.aliases", full.Keys);
        Assert.Contains("entity.known_as", full.Keys);
        Assert.Contains("Wraithmoor", full.Whole, StringComparison.Ordinal);
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
                "aliases": ["The toll"],
                "lead": "# The salt tithe\n\nEvery ship pays it at the Reach.",
                "pinned": {
                  "fields": [
                    { "name": "planted", "value": ["ember:1"], "kind": "entry", "status": "canon", "pinned": true, "line": 10 },
                    { "name": "keeper", "value": "Orrin Vale", "kind": "temporal", "status": "canon", "pinned": true, "from": "ember:1", "line": 14 }
                  ],
                  "sections": [
                    { "name": "Terms", "level": 2, "heading_path": ["Terms"], "status": "canon", "pinned": true, "from": "ember:1", "line": 34, "body": "Paid each season." }
                  ]
                },
                "fields": [
                  { "name": "planted", "value": ["ember:1"], "kind": "entry", "status": "canon", "pinned": true, "line": 10 },
                  { "name": "keeper", "value": "Orrin Vale", "kind": "temporal", "status": "canon", "pinned": true, "from": "ember:1", "line": 14 }
                ],
                "sections": [
                  { "name": "Overview", "level": 2, "heading_path": ["Overview"], "status": "canon", "pinned": false, "line": 27, "body": "A toll in salt, until the ambush. The fleet enters at dawn, and what she says is no longer true." },
                  { "name": "Terms", "level": 2, "heading_path": ["Terms"], "status": "canon", "pinned": true, "from": "ember:1", "line": 34, "body": "Paid each season." }
                ],
                "hash": "{{reply.Json.GetProperty("entity").GetProperty("hash").GetString()}}"
              },
              "den_notice": "{{DenNotice}}"
            }
            """);

        AssertIsExactly(expected, reply);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task CanonAsOfAtAPositionIsExactlyThis()
    {
        // As for get_entity, so a member that's allowed can't carry a hint either. The manifest goes first, so the answer is only this class's note, whatever the shared den says.
        File.Delete(Path.Combine(_den.Root, "universes/starfall/entities/threads/the-missing-manifest.md"));
        var reply = await Call("canon_as_of", new() { ["position"] = "ember:3", ["type"] = "thread" });

        var expected = JsonNode.Parse(
            $$"""
            {
              "version": 1,
              "universe": "starfall",
              "position": "ember:3",
              "at": "as of ember:3",
              "count": 1,
              "pinned": [
                {
                  "key": "{{TitheId}}",
                  "name": "The salt tithe",
                  "type": "thread",
                  "fields": [
                    { "name": "planted", "value": ["ember:1"], "kind": "entry", "status": "canon", "pinned": true, "line": 10 },
                    { "name": "keeper", "value": "Orrin Vale", "kind": "temporal", "status": "canon", "pinned": true, "from": "ember:1", "line": 14 }
                  ],
                  "sections": [
                    { "name": "Terms", "level": 2, "heading_path": ["Terms"], "status": "canon", "pinned": true, "from": "ember:1", "line": 34, "body": "Paid each season." }
                  ]
                }
              ],
              "entities": [
                {
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
                  "aliases": ["The toll"],
                  "lead": "# The salt tithe\n\nEvery ship pays it at the Reach.",
                  "fields": [
                    { "name": "planted", "value": ["ember:1"], "kind": "entry", "status": "canon", "pinned": true, "line": 10 },
                    { "name": "keeper", "value": "Orrin Vale", "kind": "temporal", "status": "canon", "pinned": true, "from": "ember:1", "line": 14 }
                  ],
                  "sections": [
                    { "name": "Overview", "level": 2, "heading_path": ["Overview"], "status": "canon", "pinned": false, "line": 27, "body": "A toll in salt, until the ambush. The fleet enters at dawn, and what she says is no longer true." },
                    { "name": "Terms", "level": 2, "heading_path": ["Terms"], "status": "canon", "pinned": true, "from": "ember:1", "line": 34, "body": "Paid each season." }
                  ]
                }
              ],
              "den_notice": "{{DenNotice}}"
            }
            """);

        AssertIsExactly(expected, reply);
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
        Assert.Equal(reply.Json.GetProperty("entities").GetArrayLength(), reply.Json.GetProperty("count").GetInt32());
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

        AssertNothingLater(reply, asked: value);
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
            foreach (var later in new[] { "Nightquill", "ash:3", "ash:4", "ash:epilogue", "a-quiet-moon:", "Drennick", "Ysmerine", "The salt heir", "Brackwater", "The reckoning", "Fenwraith", "Zanthic", "Vespergale", "Ostrelline", "Pellucid", "Tamsin", "Hallowmere", "Cinderlace" })
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
        var search = await Call("search", new() { ["query"] = "Sablewick", ["as_of"] = position }, error: true);
        var list = await Call("list_entities", new() { ["as_of"] = position }, error: true);

        foreach (var reply in new[] { entity, canon, search, list })
        {
            Assert.Equal("bad_position", reply.Json.GetProperty("error").GetProperty("code").GetString());
            AssertNothingLater(reply);
            AssertNoMarks(reply);
            AssertOnlyKeys(ErrorResultKeys, reply);
        }
    }

    // --- The writer's comments. ---

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task AMisplacedWolfdenCommentSaysNothingAtAPosition()
    {
        // Two blank lines put a section comment out of place (F-SEC-10), so the fact has no until, and its date is only text.
        _den.Write(
            "universes/starfall/entities/notes/the-tally.md",
            """
            ---
            type: note
            name: The tally
            status: canon
            ---

            <!-- wolfden: from=ash:3 -->
            Counted yearly.

            ## Terms


            <!-- wolfden: until=ash:2 -->
            Body after two blanks.

            <!-- wolfden: from=ash:4 -->
            And mid-body.
            """);

        var entity = await Call("get_entity", new() { ["entity"] = "The tally", ["as_of"] = "ember:3" });
        var canon = await Call("canon_as_of", new() { ["position"] = "ember:3", ["type"] = "note" });

        var note = entity.Json.GetProperty("entity");
        Assert.Equal("Counted yearly.", note.GetProperty("lead").GetString());
        Assert.Equal("Body after two blanks.\n\nAnd mid-body.", note.GetProperty("sections")[0].GetProperty("body").GetString());
        foreach (var reply in new[] { entity, canon })
        {
            AssertNothingLater(reply);
            AssertNoMarks(reply);
            Assert.DoesNotContain("wolfden:", reply.Whole, StringComparison.Ordinal);
        }

        var full = await Call("get_entity", new() { ["entity"] = "The tally" });
        Assert.Contains("wolfden: until=ash:2", full.Whole, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task ACommentShownAsCodeStaysAtAPosition()
    {
        _den.Write(
            "universes/starfall/entities/notes/the-tally.md",
            """
            ---
            type: note
            name: The tally
            status: canon
            ---

            ## Terms
            Write `<!-- a count -->` in the margin.
            """);

        var reply = await Call("get_entity", new() { ["entity"] = "The tally", ["as_of"] = "ember:3" });

        Assert.Equal("Write `<!-- a count -->` in the margin.", reply.Json.GetProperty("entity").GetProperty("sections")[0].GetProperty("body").GetString());
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
              "den_notice": "{{DenNotice}}"
            }
            """);
        AssertIsExactly(expected, reply);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task ANoteAnErrorLeavesOutIsAnUnknownNameAtAPosition()
    {
        var oath = await Call("get_entity", new() { ["entity"] = "The brine oath", ["as_of"] = "ember:3" }, error: true);
        var nothing = await Call("get_entity", new() { ["entity"] = "The brine oat", ["as_of"] = "ember:3" }, error: true);

        Assert.Equal("unknown_entity", oath.Json.GetProperty("error").GetProperty("code").GetString());

        // The message is exact, so it can't go on to suggest notes the reader hasn't met, and only what was asked for differs from a name no note has.
        foreach (var (reply, name) in new[] { (oath, "The brine oath"), (nothing, "The brine oat") })
        {
            var message = reply.Json.GetProperty("error").GetProperty("message").GetString();
            Assert.Equal($"No entity in universe 'starfall' matches '{name}' by id, path, file name, name, or alias. Use search or list_entities to find it.", message);
            AssertOnlyKeys(ErrorResultKeys, reply);
            AssertNoMarks(reply);
        }

        AssertNothingLater(nothing);
        foreach (var word in LaterWords.Where(w => w != "The brine oath"))
        {
            Assert.DoesNotContain(word, oath.Whole, StringComparison.Ordinal);
        }
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
        AssertOnlyKeys(ErrorResultKeys, reply);
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
    public async Task OnlyTheStoryViewsTakeAPosition()
    {
        var tools = await Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var actual = tools
            .Select(t => $"{t.Name}({string.Join(", ", t.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name))})")
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(
            ToolsAndArguments.SequenceEqual(actual),
            "The tools or their arguments changed. Before updating the list in McpStaysStrictTests, check: does the new tool or argument let an assistant read as of a " +
            "story position, however it's named, or read anything later without saying so? Only get_entity, search, and list_entities (as_of) and canon_as_of (position) do, and they " +
            "go through the canon view and AsOfReads. Showing an assistant more is a decision for the maintainer (#300). If it doesn't, add it to the list. The tools now:\n" + string.Join("\n", actual));

        Assert.Equal(
            ["canon_as_of", "check", "get_chapter", "get_chapter_defaults", "get_entity", "get_history", "list_entities", "search"],
            tools.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == true).Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task NoToolSaysItCanMarkAnything()
    {
        var tools = await Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        foreach (var text in tools.Select(t => t.Description).Append(Client.ServerInstructions))
        {
            foreach (var mark in MarkPhrases)
            {
                Assert.DoesNotContain(mark, text ?? "", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public async Task ADenWithNoErrorsAnswersByTheSameRules()
    {
        // An answer without a den_notice goes back as the tool made it, not rebuilt with the notice, so it's another route to the client, _meta included.
        File.Delete(Path.Combine(_den.Root, BrinePath));
        _den.Write(TithePath, TitheText.Replace("status=maybe pin=true", "status=idea", StringComparison.Ordinal));

        var entity = await Call("get_entity", new() { ["entity"] = Tithe, ["as_of"] = "ember:3" });
        var canon = await Call("canon_as_of", new() { ["position"] = "ember:3" });
        var search = await Call("search", new() { ["query"] = "toll", ["as_of"] = "ember:3" });
        var list = await Call("list_entities", new() { ["as_of"] = "ember:3" });

        foreach (var reply in new[] { entity, canon, search, list })
        {
            Assert.DoesNotContain("den_notice", reply.Keys);
            AssertNothingLater(reply);
            AssertNoMarks(reply);
        }

        AssertOnlyKeys(EntityResultKeys, entity);
        AssertOnlyKeys(CanonResultKeys, canon);
        AssertOnlyKeys(SearchAtAPositionKeys, search);
        AssertOnlyKeys(ListResultKeys, list);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public void TheServerOffersNoPromptsOrResources()
    {
        // Either would be a way to hand an assistant later content that no check on a tool's answer sees. Call checks each answer's _meta.
        Assert.Null(Client.ServerCapabilities.Prompts);
        Assert.Null(Client.ServerCapabilities.Resources);
    }

    // --- Search and list at a position: the same rules, end to end. ---

    /// <summary>A search's answer when nothing's found: the query is the one thing it can say.</summary>
    private static JsonObject NoHits(string query) => new()
    {
        ["version"] = 1,
        ["query"] = query,
        ["count"] = 0,
        ["truncated"] = false,
        ["hits"] = new JsonArray(),
        ["den_notice"] = DenNotice,
    };

    /// <summary>The words of <see cref="LaterWords"/> as a search takes them. A position's book alone is a word notes can use for something else, so it's asked for with a chapter.</summary>
    public static TheoryData<string> LaterSearches()
    {
        var data = new TheoryData<string>();
        foreach (var word in LaterWords.Where(w => !w.EndsWith(':')).Concat(["ash:2", "ash:3", "ash:4", "a-quiet-moon:1"]))
        {
            data.Add(word);
        }

        return data;
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-50")]
    [MemberData(nameof(LaterSearches))]
    public async Task SearchAtAPositionFindsNoLaterWord(string word)
    {
        // The query's own words come back in 'query', so this is the whole answer rather than a look for the word.
        var reply = await Call("search", new() { ["query"] = word, ["as_of"] = "ember:3" });

        AssertIsExactly(NoHits(word), reply);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task SearchWithNoPositionFindsWhatASearchAtAPositionLeavesOut()
    {
        var full = await Call("search", new() { ["query"] = "toll" });
        var at = await Call("search", new() { ["query"] = "toll", ["as_of"] = "ember:3" });

        foreach (var written in new[] { "Hallowmere", "Vespergale", "Fenwraith", Heir })
        {
            Assert.Contains(written, full.Whole, StringComparison.Ordinal);
            Assert.DoesNotContain(written, at.Whole, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task SearchAtAPositionIsExactlyThis()
    {
        // A word in text of every kind, later and out of canon too, is found only in the one section a reader there has, and in
        // the plain alias of the note they've met, which readers know wherever the note is (D3 on #67). The later note's alias doesn't match.
        var reply = await Call("search", new() { ["query"] = "toll", ["as_of"] = "ember:3" });

        var expected = JsonNode.Parse(
            $$"""
            {
              "version": 1,
              "query": "toll",
              "count": 2,
              "truncated": false,
              "hits": [
                {
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
                    "from": "ember:1"
                  },
                  "snippet": "The salt tithe\nThe [toll]",
                  "status": "canon",
                  "in_canon": true,
                  "in_comment": false
                },
                {
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
                    "from": "ember:1"
                  },
                  "section": "Overview",
                  "line": 27,
                  "snippet": "A [toll] in salt, until the ambush. The fleet enters at dawn, and what she says is no longer true.",
                  "status": "canon",
                  "in_canon": true,
                  "in_comment": false
                }
              ],
              "den_notice": "{{DenNotice}}"
            }
            """);
        AssertIsExactly(expected, reply);
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-50")]
    [InlineData("ember", "the", null, false)]
    [InlineData("ember:1", "the", null, false)]
    [InlineData("ember:2b", "the", null, false)]
    [InlineData("ember:3", "the", null, false)]
    [InlineData("ember:3", "the", null, true)]
    [InlineData("ember:3", "the", "thread", false)]
    [InlineData("ember:3", "salt", null, false)]
    [InlineData("ember:3", "toll*", null, false)]
    [InlineData("ember:3", "ember", null, false)]
    [InlineData("ember:3", "Orrin", null, false)]
    public async Task SearchAtAPositionHasNothingLaterInItsHits(string position, string query, string? type, bool canonOnly)
    {
        var reply = await Call("search", new() { ["query"] = query, ["as_of"] = position, ["limit"] = 200, ["type"] = type, ["canon_only"] = canonOnly });

        // At the start of a book nothing of it has been met yet; past that, the hits are what's being looked at.
        Assert.True(position == "ember" || reply.Json.GetProperty("count").GetInt32() > 0);
        AssertNothingLater(reply);
        AssertNoMarks(reply);
        AssertOnlyKeys(SearchAtAPositionKeys, reply);
        foreach (var hit in reply.Json.GetProperty("hits").EnumerateArray())
        {
            Assert.False(hit.GetProperty("in_comment").GetBoolean());
            Assert.True(hit.GetProperty("in_canon").GetBoolean());
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task ASearchOfAFieldAtAPositionHasOnlyTheValuesInView()
    {
        var keeper = await Call("search", new() { ["query"] = "Orrin", ["field"] = "keeper", ["as_of"] = "ember:3" });
        var hit = Assert.Single(keeper.Json.GetProperty("hits").EnumerateArray());
        Assert.Equal("keeper", hit.GetProperty("field").GetString());
        Assert.Equal("[Orrin] Vale", hit.GetProperty("snippet").GetString());
        AssertNothingLater(keeper);
        AssertOnlyKeys(SearchAtAPositionKeys, keeper);

        // A later entry of a field the reader knows finds nothing, as a word no note has does.
        AssertIsExactly(NoHits("Morvane"), await Call("search", new() { ["query"] = "Morvane", ["field"] = "keeper", ["as_of"] = "ember:3" }));

        // A field only later or out-of-canon values give is refused as a made-up one is, so the refusal doesn't confirm it.
        var madeUp = await Call("search", new() { ["query"] = "toll", ["field"] = "madeup", ["as_of"] = "ember:3" }, error: true);
        foreach (var field in new[] { "seal", "paid_off", "echoes", "rumor", "omen", "old_name", "claim" })
        {
            var later = await Call("search", new() { ["query"] = "toll", ["field"] = field, ["as_of"] = "ember:3" }, error: true);
            Assert.Equal("bad_argument", later.Json.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(
                madeUp.Json.GetProperty("error").GetProperty("message").GetString()!.Replace("madeup", field, StringComparison.Ordinal),
                later.Json.GetProperty("error").GetProperty("message").GetString());
            AssertNothingLater(later);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task ALinkToALaterNoteDoesntFindThatNotesNames()
    {
        // With no position, a link in a field matches by its note's name and aliases. At one, it finds only by what it shows.
        _den.Write(
            "universes/starfall/entities/notes/the-tally.md",
            """
            ---
            type: note
            name: The tally
            status: canon
            fields:
              rival: "[[the-salt-heir|the claimant]]"
            ---

            Counted yearly.
            """);

        var full = await Call("search", new() { ["query"] = "apparent", ["field"] = "rival" });
        Assert.Equal(1, full.Json.GetProperty("count").GetInt32());

        foreach (var word in new[] { "apparent", "heir", "Ysmerine" })
        {
            AssertIsExactly(NoHits(word), await Call("search", new() { ["query"] = word, ["as_of"] = "ember:3", ["limit"] = 200 }));
        }

        var shown = await Call("search", new() { ["query"] = "claimant", ["as_of"] = "ember:3" });
        Assert.Equal("the [claimant]", Assert.Single(shown.Json.GetProperty("hits").EnumerateArray()).GetProperty("snippet").GetString());
        AssertNothingLater(shown);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task AWordKeptOutOfANoteTheReaderKnowsStopsTheHit()
    {
        // Each query's first word is in the note the reader knows, and its second is only in a comment or a later section of that note.
        AssertIsExactly(NoHits("toll Nightquill"), await Call("search", new() { ["query"] = "toll Nightquill", ["as_of"] = "ember:3" }));
        AssertIsExactly(NoHits("ship Sablewick"), await Call("search", new() { ["query"] = "ship Sablewick", ["as_of"] = "ember:3" }));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task PartWayThroughTheStorySearchAndListGoAsFarAsThatPoint()
    {
        // Sablewick's section enters at ash:2, and the heir and the payoff at ash:3.
        var audit = await Call("search", new() { ["query"] = "Sablewick", ["as_of"] = "ash:2" });
        Assert.Equal("The audit", Assert.Single(audit.Json.GetProperty("hits").EnumerateArray()).GetProperty("section").GetString());
        AssertIsExactly(NoHits("Ysmerine"), await Call("search", new() { ["query"] = "Ysmerine", ["as_of"] = "ash:2" }));
        AssertIsExactly(NoHits("Drennick"), await Call("search", new() { ["query"] = "Drennick", ["as_of"] = "ash:2" }));
        Assert.DoesNotContain(Heir, (await Call("list_entities", new() { ["as_of"] = "ash:2" })).Whole, StringComparison.Ordinal);

        var heir = await Call("search", new() { ["query"] = "Ysmerine", ["as_of"] = "ash:3" });
        Assert.Equal(Heir, heir.Json.GetProperty("hits")[0].GetProperty("entity").GetProperty("name").GetString());
        var payoff = await Call("search", new() { ["query"] = "Drennick", ["as_of"] = "ash:3" });
        Assert.Equal("Payoff", Assert.Single(payoff.Json.GetProperty("hits").EnumerateArray()).GetProperty("section").GetString());
        Assert.Contains(Heir, (await Call("list_entities", new() { ["as_of"] = "ash:3" })).Whole, StringComparison.Ordinal);

        // Out of canon stays out, at any position.
        foreach (var word in new[] { "Hallowmere", "Wyrmsedge", "Gallowmere", "Vespergale", "Pellucid", "Glimmerfen", "Thornmarch", "Corvantis" })
        {
            AssertIsExactly(NoHits(word), await Call("search", new() { ["query"] = word, ["as_of"] = "ash:epilogue" }));
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task ListEntitiesAtAPositionIsExactlyThis()
    {
        File.Delete(Path.Combine(_den.Root, "universes/starfall/entities/threads/the-missing-manifest.md"));
        var reply = await Call("list_entities", new() { ["as_of"] = "ember:3", ["type"] = "thread" });

        var expected = JsonNode.Parse(
            $$"""
            {
              "version": 1,
              "count": 1,
              "total": 1,
              "truncated": false,
              "entities": [
                {
                  "key": "{{TitheId}}",
                  "id": "{{TitheId}}",
                  "name": "The salt tithe",
                  "type": "thread",
                  "status": "canon",
                  "universe": "starfall",
                  "scope_kind": "universe",
                  "scope_key": "starfall",
                  "path": "{{TithePath}}",
                  "from": "ember:1"
                }
              ],
              "den_notice": "{{DenNotice}}"
            }
            """);
        AssertIsExactly(expected, reply);
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-50")]
    [InlineData("ember", null, null)]
    [InlineData("ember:1", null, null)]
    [InlineData("ember:2b", null, null)]
    [InlineData("ember:3", null, null)]
    [InlineData("ember:3", "series", "long-dark")]
    [InlineData("ember:3", "book", "ember")]
    [InlineData("ember:3", "book", "ash")] // A filter to a later book doesn't reach ahead of the position.
    [InlineData("ember:3", "type", "note")]
    [InlineData("ember:3", "type", "location")]
    [InlineData("ember:3", "status", "canon")]
    [InlineData("ember:3", "status", "idea")] // Out of canon is out of the view, whatever the filter asks for.
    [InlineData("ember:3", "status", "retconned")]
    public async Task ListEntitiesAtAPositionHasNothingLater(string position, string? filter, string? value)
    {
        var args = new Dictionary<string, object?> { ["as_of"] = position };
        if (filter is not null)
        {
            args[filter] = value;
        }

        var reply = await Call("list_entities", args);

        AssertNothingLater(reply);
        AssertNoMarks(reply);
        AssertOnlyKeys(ListResultKeys, reply);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task ListEntitiesAtAPositionSaysOfEachNoteWhatCanonAsOfSays()
    {
        // Filed in the later book but entering earlier, so it's in both views, and where it's filed comes with it (format.md F-VIEW-1).
        _den.Write("universes/starfall/series/long-dark/books/ash/entities/notes/the-early-omen.md", "---\ntype: note\nname: The early omen\nstatus: canon\nfrom: \"ember:2\"\n---\n\nSeen early.\n");

        foreach (var position in new[] { "ember:1", "ember:3", "ash:2", "ash:3" })
        {
            var list = await Call("list_entities", new() { ["as_of"] = position, ["limit"] = 1000 });
            var canon = await Call("canon_as_of", new() { ["position"] = position, ["brief"] = true });

            var listed = list.Json.GetProperty("entities").EnumerateArray().Select(e => e.GetRawText()).ToList();
            var viewed = canon.Json.GetProperty("entities").EnumerateArray().Select(e => e.GetRawText()).ToList();
            Assert.Equal(viewed, listed);
            Assert.Equal(listed.Count, list.Json.GetProperty("total").GetInt32());
            Assert.Equal(listed.Count, list.Json.GetProperty("count").GetInt32());
        }

        var early = await Call("list_entities", new() { ["as_of"] = "ember:3" });
        Assert.Equal("ash", early.Json.GetProperty("entities").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "The early omen").GetProperty("scope_key").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task AListCutShortAtAPositionCountsOnlyWhatsInView()
    {
        var whole = await Call("list_entities", new() { ["as_of"] = "ember:3", ["limit"] = 1000 });
        var all = await Call("list_entities", new() { ["limit"] = 1000 });
        var cut = await Call("list_entities", new() { ["as_of"] = "ember:3", ["limit"] = 1 });

        Assert.True(all.Json.GetProperty("total").GetInt32() > whole.Json.GetProperty("total").GetInt32());
        Assert.Equal(whole.Json.GetProperty("total").GetInt32(), cut.Json.GetProperty("total").GetInt32());
        Assert.Equal(1, cut.Json.GetProperty("count").GetInt32());
        Assert.True(cut.Json.GetProperty("truncated").GetBoolean());
        AssertNothingLater(cut);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task AKeyTwoBooksShareIsSaidToBeSharedAtAPosition()
    {
        // F-KEY-5. The book is there, so "there's no book" would send the writer the wrong way, and the true message names only the key given.
        _den.Write("universes/starfall/series/drift-war/series.md", "---\nid: 01JA3E28JT97KB6CQ643DZVMXY\nname: The Drift War\nbooks: [ember]\n---\n");
        _den.Write("universes/starfall/series/drift-war/books/ember/book.md", "---\nid: 01JB0000000000000000000A02\nname: Ember of the Drift\nchapters: [1, 2]\n---\n");

        var reply = await Call("list_entities", new() { ["as_of"] = "ash:2", ["book"] = "ember" }, error: true);

        var error = reply.Json.GetProperty("error");
        Assert.Equal("unknown_scope", error.GetProperty("code").GetString());
        Assert.StartsWith("More than one book of universe 'starfall' has the key 'ember' (F-KEY-5)", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        foreach (var other in new[] { "ash", "a-quiet-moon", "drift-war" })
        {
            Assert.DoesNotContain(other, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-50")]
    public async Task AScopeThatIsntThereNamesNoOtherAtAPosition()
    {
        var book = await Call("list_entities", new() { ["as_of"] = "ember:3", ["book"] = "nope" }, error: true);
        var series = await Call("list_entities", new() { ["as_of"] = "ember:3", ["series"] = "nope" }, error: true);

        Assert.Equal("unknown_scope", book.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("There's no book 'nope' here.", book.Json.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal("There's no series 'nope' here.", series.Json.GetProperty("error").GetProperty("message").GetString());

        // With no position it's the full view, which can say.
        var full = await Call("list_entities", new() { ["book"] = "nope" }, error: true);
        Assert.Contains("ash", full.Json.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-50")]
    [InlineData("search")]
    [InlineData("list_entities")]
    public async Task EveryUniverseDoesntGoWithAPosition(string tool)
    {
        var args = new Dictionary<string, object?> { ["universe"] = "*", ["as_of"] = "ember:3" };
        if (tool == "search")
        {
            args["query"] = "toll";
        }

        var reply = await Call(tool, args, error: true);

        Assert.Equal("bad_argument", reply.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("doesn't go with a position", reply.Json.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        AssertOnlyKeys(ErrorResultKeys, reply);
    }

    // --- Names readers learn partway through the story (known_as, #67). ---

    private const string CourierPath = "universes/starfall/entities/notes/the-courier.md";
    private const string LedgerPath = "universes/starfall/entities/notes/the-ledger.md";

    /// <summary>A note met at ember:1 whose real name readers learn at ash:2, and a note met at ember:1 whose text links by that name.</summary>
    private void WriteCourier()
    {
        _den.Write(
            CourierPath,
            """
            ---
            type: note
            name: The Courier
            aliases: [the Messenger]
            known_as:
              - { value: Mara Voss, from: "ash:2" }
            status: canon
            from: "ember:1"
            ---

            Carries the post.
            """);
        _den.Write(
            LedgerPath,
            """
            ---
            type: note
            name: The ledger
            status: canon
            from: "ember:1"
            fields:
              signer: "[[Mara Voss]]"
            ---

            Kept at the Reach.

            ## Entries
            [[Mara Voss]] signed it. [[Mara Voss|The stranger]] paid. [[Mara Voss#Overview|she]] left.
            """);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-49")]
    public async Task ANameIHaventReachedDoesntExist()
    {
        WriteCourier();

        // At ember:3, the later name is no name: word for word what a made-up name gets.
        var hidden = await Call("get_entity", new() { ["entity"] = "Mara Voss", ["as_of"] = "ember:3" }, error: true);
        var madeUp = await Call("get_entity", new() { ["entity"] = "Mara Vass", ["as_of"] = "ember:3" }, error: true);
        Assert.Equal("unknown_entity", hidden.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(
            madeUp.Json.GetProperty("error").GetProperty("message").GetString()!.Replace("Mara Vass", "Mara Voss", StringComparison.Ordinal),
            hidden.Json.GetProperty("error").GetProperty("message").GetString());
        AssertOnlyKeys(ErrorResultKeys, hidden);

        // The note opens, lists only the plain alias, and has the name nowhere.
        var courier = await Call("get_entity", new() { ["entity"] = "The Courier", ["as_of"] = "ember:3" });
        Assert.Equal(["the Messenger"], courier.Json.GetProperty("entity").GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
        Assert.DoesNotContain("Mara", courier.Whole, StringComparison.Ordinal);
        Assert.DoesNotContain("known_as", courier.Whole, StringComparison.Ordinal);
        AssertOnlyKeys(EntityResultKeys, courier);
        AssertNothingLater(courier);

        // From ash:2, both open, and aliases has the name as a plain string, with no from.
        foreach (var name in new[] { "Mara Voss", "The Courier" })
        {
            var known = await Call("get_entity", new() { ["entity"] = name, ["as_of"] = "ash:2" });
            Assert.Equal("The Courier", known.Json.GetProperty("entity").GetProperty("name").GetString());
            Assert.Equal(["the Messenger", "Mara Voss"], known.Json.GetProperty("entity").GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
            Assert.DoesNotContain("known_as", known.Whole, StringComparison.Ordinal);
            AssertOnlyKeys(EntityResultKeys, known);
        }

        // With no position, both open, with known_as and its from.
        var full = await Call("get_entity", new() { ["entity"] = "Mara Voss" });
        var entity = full.Json.GetProperty("entity");
        Assert.Equal(["the Messenger"], entity.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
        var knownAs = Assert.Single(entity.GetProperty("known_as").EnumerateArray());
        Assert.Equal(("Mara Voss", "ash:2"), (knownAs.GetProperty("value").GetString(), knownAs.GetProperty("from").GetString()));

        // A canon view and a list say nothing of it either; canon_as_of lists the plain alias with the note's content.
        var canon = await Call("canon_as_of", new() { ["position"] = "ember:3", ["type"] = "note" });
        var brief = await Call("canon_as_of", new() { ["position"] = "ember:3", ["brief"] = true });
        var list = await Call("list_entities", new() { ["as_of"] = "ember:3" });
        foreach (var reply in new[] { canon, brief, list })
        {
            Assert.DoesNotContain("Mara", reply.Whole, StringComparison.Ordinal);
            AssertNothingLater(reply);
        }

        Assert.Equal(["the Messenger"], canon.Json.GetProperty("entities").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "The Courier").GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
        Assert.DoesNotContain("aliases", brief.Keys.Select(k => k.Split('.')[^1]));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-49")]
    public async Task SearchingByANameIHaventReachedFindsNothing()
    {
        WriteCourier();

        AssertIsExactly(NoHits("Mara Voss"), await Call("search", new() { ["query"] = "Mara Voss", ["as_of"] = "ember:3" }));
        AssertIsExactly(NoHits("Voss"), await Call("search", new() { ["query"] = "Voss", ["as_of"] = "ember:3", ["limit"] = 200 }));

        // A plain alias is found at any position the note is in (D3), and the later name once it's reached.
        var messenger = await Call("search", new() { ["query"] = "Messenger", ["as_of"] = "ember:3" });
        Assert.Equal("The Courier", Assert.Single(messenger.Json.GetProperty("hits").EnumerateArray()).GetProperty("entity").GetProperty("name").GetString());
        var reached = await Call("search", new() { ["query"] = "Mara Voss", ["as_of"] = "ash:2" });
        Assert.Contains(reached.Json.GetProperty("hits").EnumerateArray(), h => h.GetProperty("entity").GetProperty("name").GetString() == "The Courier");

        // With no position, it's found.
        var full = await Call("search", new() { ["query"] = "Voss" });
        Assert.Contains(full.Json.GetProperty("hits").EnumerateArray(), h => h.GetProperty("entity").GetProperty("name").GetString() == "The Courier");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-51")]
    public async Task MyEarlierTextLinkedByTheLaterNameReadsByTheNotesName()
    {
        WriteCourier();

        var at = await Call("get_entity", new() { ["entity"] = "The ledger", ["as_of"] = "ember:3" });
        var full = await Call("get_entity", new() { ["entity"] = "The ledger" });

        // The link is shown as written by the note's name, keeping its label; a heading readers don't know goes.
        var section = at.Json.GetProperty("entity").GetProperty("sections")[0];
        Assert.Equal("[[The Courier]] signed it. [[The Courier|The stranger]] paid. [[The Courier|she]] left.", section.GetProperty("body").GetString());
        Assert.Equal(full.Json.GetProperty("entity").GetProperty("sections")[0].GetProperty("line").GetInt32(), section.GetProperty("line").GetInt32());
        Assert.Equal("[[The Courier]]", at.Json.GetProperty("entity").GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "signer").GetProperty("value").GetString());
        Assert.DoesNotContain("Mara", at.Whole, StringComparison.Ordinal);

        // A search there finds what the text shows, not what it's written by.
        AssertIsExactly(NoHits("Voss"), await Call("search", new() { ["query"] = "Voss", ["as_of"] = "ember:3", ["limit"] = 200 }));
        var shown = await Call("search", new() { ["query"] = "Courier signed", ["as_of"] = "ember:3" });
        Assert.Equal("The ledger", Assert.Single(shown.Json.GetProperty("hits").EnumerateArray()).GetProperty("entity").GetProperty("name").GetString());

        // With no position, and once readers know the name, as written.
        Assert.Contains("[[Mara Voss]] signed it.", full.Json.GetProperty("entity").GetProperty("sections")[0].GetProperty("body").GetString(), StringComparison.Ordinal);
        var later = await Call("get_entity", new() { ["entity"] = "The ledger", ["as_of"] = "ash:2" });
        Assert.Equal("[[Mara Voss]] signed it. [[Mara Voss|The stranger]] paid. [[Mara Voss#Overview|she]] left.", later.Json.GetProperty("entity").GetProperty("sections")[0].GetProperty("body").GetString());

        // check warns, naming both positions.
        var check = await Call("check", new() { ["path"] = LedgerPath });
        Assert.Contains("'Mara Voss' is a name of 'The Courier' that readers learn at ash:2, after this link's position, ember:1", check.Whole, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-52")]
    public async Task AnAliasTheReaderKnowsBesideOneTheyDont()
    {
        // Two notes share Old Tam: one plain, one learned at ash:2. Both are met in ember.
        _den.Write("universes/starfall/entities/notes/the-harbourmaster.md", "---\ntype: note\nname: The Harbourmaster\naliases: [Old Tam]\nstatus: canon\nfrom: \"ember:1\"\n---\n\nKeeps the harbour.\n");
        _den.Write("universes/starfall/entities/notes/the-lamplighter.md", "---\ntype: note\nname: The Lamplighter\nknown_as: [{ value: Old Tam, from: \"ash:2\" }]\nstatus: canon\nfrom: \"ember:1\"\n---\n\nLights the lamps.\n");

        var early = await Call("get_entity", new() { ["entity"] = "Old Tam", ["as_of"] = "ember:3" });
        Assert.Equal("The Harbourmaster", early.Json.GetProperty("entity").GetProperty("name").GetString());
        Assert.DoesNotContain("Lamplighter", early.Whole, StringComparison.Ordinal);

        foreach (var reply in new[]
        {
            await Call("get_entity", new() { ["entity"] = "Old Tam", ["as_of"] = "ash:2" }, error: true),
            await Call("get_entity", new() { ["entity"] = "Old Tam" }, error: true),
        })
        {
            var error = reply.Json.GetProperty("error");
            Assert.Equal("ambiguous_entity", error.GetProperty("code").GetString());
            Assert.Equal(["The Harbourmaster", "The Lamplighter"], error.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).Order(StringComparer.Ordinal));
        }

        var searchEarly = await Call("search", new() { ["query"] = "Old Tam", ["as_of"] = "ember:3" });
        Assert.Equal(["The Harbourmaster"], searchEarly.Json.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("entity").GetProperty("name").GetString()));
        var searchLate = await Call("search", new() { ["query"] = "Old Tam", ["as_of"] = "ash:2" });
        Assert.Equal(["The Harbourmaster", "The Lamplighter"], searchLate.Json.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("entity").GetProperty("name").GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-54")]
    public async Task NamesThatArriveAtDifferentTimes()
    {
        // Each spelling is its own entry, with its own from; one with no from, or a from that doesn't resolve, is never known at a position.
        _den.Write(
            "universes/starfall/entities/notes/the-man-in-the-grey-coat.md",
            """
            ---
            type: note
            name: The man in the grey coat
            known_as:
              - { value: Corin, from: "ember:2" }
              - { value: Corin Hask, from: "ash:1" }
              - { value: Søren Hask, from: "ash:3" }
              - { value: Soren Hask, from: "ash:3" }
              - { value: Brother Hask }
              - { value: The Abbot, from: "ash:9" }
            status: canon
            from: "ember:1"
            ---

            Watches the docks.
            """);

        async Task<string[]> Names(string position) =>
            [.. (await Call("get_entity", new() { ["entity"] = "The man in the grey coat", ["as_of"] = position })).Json.GetProperty("entity").GetProperty("aliases").EnumerateArray().Select(a => a.GetString()!)];

        foreach (var (position, expected) in new (string, string[])[]
        {
            ("ember:1", []),
            ("ember:3", ["Corin"]),
            ("ash:2", ["Corin", "Corin Hask"]),
            ("ash:epilogue", ["Corin", "Corin Hask", "Søren Hask", "Soren Hask"]),
        })
        {
            var names = await Names(position);
            Assert.Equal(expected, names);
        }

        // A spelling not reached is unknown, accented or not, by lookup and by search.
        foreach (var name in new[] { "Soren Hask", "Søren Hask", "Brother Hask", "The Abbot" })
        {
            var reply = await Call("get_entity", new() { ["entity"] = name, ["as_of"] = "ash:2" }, error: true);
            Assert.Equal("unknown_entity", reply.Json.GetProperty("error").GetProperty("code").GetString());
            AssertIsExactly(NoHits(name), await Call("search", new() { ["query"] = name, ["as_of"] = "ash:2" }));
        }

        // Each is a name with no position.
        var full = await Call("get_entity", new() { ["entity"] = "Brother Hask" });
        Assert.Equal(6, full.Json.GetProperty("entity").GetProperty("known_as").GetArrayLength());
    }

    // --- Reads with no position stay as they are: see the remarks on this class. ---

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

        var comment = await Call("search", new() { ["query"] = "Nightquill" });
        Assert.True(comment.Json.GetProperty("hits")[0].GetProperty("in_comment").GetBoolean());

        foreach (var reply in new[] { later, note, idea, leftOut, comment })
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
        foreach (var written in new[] { "Nightquill", "until=ash:2", "paid_off", "ash:3", "Morvane", "Zanthic reveal", "Vespergale", "Hallowmere", "Pellucid" })
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
        var reached = ReachableProjects();

        Assert.Contains("Wolfden.Core", reached);
        Assert.Contains("Wolfden.Index", reached);
        Assert.DoesNotContain("Wolfden.Api", reached);
        Assert.DoesNotContain("Wolfden.Cli", reached);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public void WhatAnAssistantRunsOnDoesntReferenceTheWebApi()
    {
        foreach (var assembly in AssistantAssemblies())
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name is "Wolfden.Api" or "Wolfden.Cli");
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public void TheCodeAnAssistantRunsOnHasNoMarkedViewByTheNamesExpected()
    {
        // A tripwire for the names expected, in whatever Wolfden.Mcp reaches: a new name gets past this, and is caught by the checks on results and answers.
        var types = AssistantAssemblies().SelectMany(a => a.GetTypes());
        Assert.DoesNotContain(types, t => t.Name.Contains("ReaderMarks", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("MarksAt", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("AheadOfReaders", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-47")]
    public void WhatAResultCanHoldIsExactlyThis()
    {
        // The types, not an answer, so a member that stays null on MCP's route, where the checks on answers can't see it, still has to be looked at here.
        var seen = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var result in new[] { typeof(GetEntityResult), typeof(CanonAsOfResult), typeof(SearchResult), typeof(ListEntitiesResult), typeof(GetHistoryResult), typeof(ToolErrorResult) })
        {
            Walk(result);
        }

        var actual = seen.Select(t => $"{t.Key}: {t.Value}").ToList();
        Assert.True(
            ResultMembers.SequenceEqual(actual),
            "A type in the results of the read tools changed. If the new member can hold anything later than a position, or a mark, serving it to an assistant is a decision for " +
            "the maintainer (#300): keep it out of what MCP returns. If it can't, add it to the list in McpStaysStrictTests. The types now:\n" + string.Join("\n", actual));

        void Walk(Type type)
        {
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    Walk(argument);
                }
            }
            else if (type.Namespace?.StartsWith("Wolfden", StringComparison.Ordinal) == true && !seen.ContainsKey(type.Name))
            {
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always).ToList();
                seen[type.Name] = string.Join(", ", properties.Select(p => p.Name).Order(StringComparer.Ordinal));
                foreach (var property in properties)
                {
                    Walk(property.PropertyType);
                }
            }
        }
    }

    /// <summary>The projects <c>Wolfden.Mcp</c> reaches, following project references all the way.</summary>
    private static HashSet<string> ReachableProjects()
    {
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(Path.Combine(TempDen.RepoRoot, "src", "Wolfden.Mcp", "Wolfden.Mcp.csproj"));
        return reached;

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

    /// <summary><c>Wolfden.Mcp</c> and every assembly of ours it reaches, by project or as compiled.</summary>
    private static List<Assembly> AssistantAssemblies()
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ReachableProjects().Append("Wolfden.Mcp"))
        {
            Add(new AssemblyName(name));
        }

        return [.. assemblies.Values];

        void Add(AssemblyName name)
        {
            if (name.Name!.StartsWith("Wolfden.", StringComparison.Ordinal) && !assemblies.ContainsKey(name.Name))
            {
                var assembly = Assembly.Load(name);
                assemblies[name.Name] = assembly;
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    Add(reference);
                }
            }
        }
    }

    // --- What the responses may hold. ---

    /// <summary>
    /// Every tool and its arguments. A write tool's <c>from</c> and <c>until</c>
    /// set where a fact starts and ends: they don't read as of a position.
    /// </summary>
    private static readonly string[] ToolsAndArguments =
    [
        "add_idea(text, entity, section, expected_hash, name, type, universe, series, book, fields, allow_relinks)",
        "append_section(entity, section, text, expected_hash, universe, under, section_line, new_section, from, until, status, pin)",
        "canon_as_of(position, universe, series, book, type, brief)",
        "change_alias(entity, alias, expected_hash, new_alias, keep_old_alias, universe, allow_relinks, from)",
        "check(severity, path, limit, universe)",
        "checkpoint(message, co_author, paths)",
        "copy_entity(entity, new_name, universe, to_universe, to_series, to_book, slug, allow_relinks, expected_hash)",
        "create_entity(type, name, universe, series, book, slug, aliases, status, from, fields, text, allow_relinks, known_as)",
        "get_chapter(book, chapter, universe)",
        "get_chapter_defaults(universe, series, book)",
        "get_entity(entity, universe, as_of)",
        "get_history(entity, universe, limit, before, diff, diff_from)",
        "list_entities(universe, type, status, series, book, limit, as_of)",
        "move_entity(entity, expected_hash, to_series, to_book, keep_entry, enter_at_new_scope, universe, allow_relinks)",
        "rename_entity(entity, new_name, expected_hash, keep_old_name, universe, allow_relinks)",
        "rename_section(entity, section, new_name, expected_hash, universe, section_line)",
        "search(query, universe, type, canon_only, limit, field, as_of)",
        "update_chapter_defaults(expected_hash, changes, universe, series, book)",
        "update_chapter_fields(book, chapter, expected_hash, changes, universe)",
        "update_fields(entity, expected_hash, changes, universe)",
    ];

    /// <summary>Every member a read tool's result can have, by type. <see cref="AsOfReads"/> and the view leave some of these out at a position.</summary>
    private static readonly string[] ResultMembers =
    [
        "CanonAsOfResult: At, Count, Entities, Filter, Pinned, Position, Universe, Version",
        "EntitySummary: From, Id, Key, Name, Path, ScopeKey, ScopeKind, Status, Type, Universe",
        "EntityView: Aliases, Fields, From, Hash, Id, Key, KnownAs, Lead, Name, Path, Pinned, ScopeKey, ScopeKind, Sections, Status, Type, Universe",
        "FieldView: EntryIndex, From, Kind, Line, Live, Name, Pinned, Status, Until, Value",
        "GetEntityResult: Entity, InView, Note, Position, Version, View",
        "GetHistoryResult: Available, Backend, Count, Diff, Entity, Entries, NextBefore, Note, Reason, Truncated, Version",
        "HistoryChangeView: Kind, OldPath, Path",
        "HistoryDiffView: Files, From, To",
        "HistoryEntryView: Author, Changes, Id, Message, When",
        "HistoryFileDiffView: Change, Patch",
        "KnownNameView: From, Value",
        "ListEntitiesResult: Count, Entities, Total, Truncated, Version",
        "PinnedEntity: Fields, Key, Name, Sections, Type",
        "PinnedFacts: Fields, Sections",
        "ScopeFilter: Key, Kind",
        "SearchHitView: Entity, Field, InCanon, InComment, LeftOut, Line, Section, Snippet, Status",
        "SearchResult: Count, Hits, Query, Truncated, Version",
        "SectionProblem: Line, Message, Rule",
        "SectionView: Body, Bookkeeping, From, HeadingPath, LeftOut, Level, Line, Name, Pinned, Status, Until",
        "ToolError: Candidates, Code, Message",
        "ToolErrorResult: Error, Version",
    ];

    private static readonly string[] SummaryKeys = ["key", "id", "name", "type", "status", "universe", "scope_kind", "scope_key", "path", "from"];

    private static readonly string[] FieldKeys = ["name", "value", "kind", "status", "pinned", "from", "line"];

    private static readonly string[] SectionKeys = ["name", "level", "heading_path", "status", "pinned", "from", "line", "body"];

    private static string[] Under(string prefix, params IEnumerable<string>[] keys) =>
        [.. keys.SelectMany(k => k).Select(k => prefix.Length == 0 ? k : $"{prefix}.{k}")];


    /// <summary>
    /// The content of an entity at a position. A field's <c>value</c> is the
    /// writer's, whatever keys a mapping of theirs has. There's no <c>until</c>
    /// (F-VIEW-4), and none of the full view's <c>known_as</c>, <c>entry_index</c>,
    /// <c>live</c>, <c>bookkeeping</c>, or <c>left_out</c> (F-GEN-2). <c>aliases</c>
    /// holds the names readers know there, as plain strings (D3 on #67).
    /// </summary>
    private static string[] Content(string entity) =>
    [
        .. Under(entity, ["aliases", "lead", "fields", "sections"]),
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

    /// <summary>A search at a position only reads what's in the view, so no hit is in a section an error leaves out.</summary>
    private static readonly string[] SearchAtAPositionKeys = [.. SearchResultKeys.Where(k => k != "hits.left_out")];

    private static readonly string[] ListResultKeys =
    [
        "version", "count", "total", "truncated", "den_notice", "entities",
        .. Under("entities", SummaryKeys),
    ];

    private static readonly string[] ErrorResultKeys =
    [
        "version", "den_notice", "error", "error.code", "error.message", "error.candidates",
        .. Under("error.candidates", SummaryKeys),
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

    private static void AssertOnlyKeys(string[] allowed, Reply reply)
    {
        var extra = reply.Keys.Except(allowed, StringComparer.Ordinal).ToList();
        Assert.True(
            extra.Count == 0,
            $"The answer has a new member: {string.Join(", ", extra)}. If it can hold anything later than the position, or a mark, showing it to an assistant is a decision " +
            "for the maintainer (#300). If it can't, add it to the list in McpStaysStrictTests.");
    }

    private static void AssertIsExactly(JsonNode? expected, Reply reply)
    {
        var actual = JsonNode.Parse(reply.Text);
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"This answer changed. If what's new can hold anything later than the position, or a mark, showing it to an assistant is a decision for the maintainer (#300). The answer was:\n{actual}");
    }

    /// <param name="reply">The answer.</param>
    /// <param name="asked">A later book the call itself named, which the answer may say back.</param>
    private static void AssertNothingLater(Reply reply, string? asked = null)
    {
        foreach (var word in LaterWords)
        {
            Assert.DoesNotContain(word, reply.Whole, StringComparison.Ordinal);
        }

        // A bare key too, as an error might list the books (#67): not inside a longer key or word.
        foreach (var book in LaterBooks.Where(b => b != asked))
        {
            Assert.DoesNotMatch($@"(?<![\w-]){System.Text.RegularExpressions.Regex.Escape(book)}(?![\w-])", reply.Whole);
        }
    }

    /// <summary>Looks for a mark's phrases, a member named for one, and a value that's only a tag. A note's prose can say "no longer true" or "enters at dawn".</summary>
    private static void AssertNoMarks(Reply reply)
    {
        foreach (var mark in MarkPhrases)
        {
            Assert.DoesNotContain(mark, reply.Whole, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(reply.Keys, k => k.Split('.', '_').Any(w => MarkWords.Contains(w, StringComparer.OrdinalIgnoreCase)));
        Assert.DoesNotContain(Strings(reply.Json), IsATag);

        static bool IsATag(string value) =>
            value.Equals("no longer true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Reader's view", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Author's view", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("enters at chapter", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("enters at ch.", StringComparison.OrdinalIgnoreCase);

        static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => [element.GetString()!],
            JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
            JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Strings(p.Value)),
            _ => [],
        };
    }

    private async Task<Reply> Call(string tool, Dictionary<string, object?> args, bool error = false)
    {
        // The den has two universes, and a position's book settles which only for the tools that take one.
        args.TryAdd("universe", "starfall");
        var result = await Client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(error, result.IsError == true);
        var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        var text = block.Text;
        using var document = JsonDocument.Parse(text);

        // The two forms of the answer are the same, so looking at one looks at both.
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(text), JsonNode.Parse(structured.GetRawText())), "The text and the structured content differ.");

        // Nothing beside the answer: only the SDK's own note of the server, which comes from the server's name and version.
        Assert.Null(block.Meta);
        Assert.Null(block.Annotations);
        var meta = result.Meta?.ToJsonString() ?? "";
        Assert.Empty((result.Meta?.Select(m => m.Key) ?? []).Except(["io.modelcontextprotocol/serverInfo"], StringComparer.Ordinal));
        var json = document.RootElement.Clone();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        CollectKeys(json, "", keys);
        return new Reply(json, text, $"{text}\n{structured.GetRawText()}\n{meta}", keys);
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

    /// <summary>A tool's answer: its JSON, its text, its text, structured content, and <c>_meta</c> together, and its members.</summary>
    private sealed record Reply(JsonElement Json, string Text, string Whole, HashSet<string> Keys);
}

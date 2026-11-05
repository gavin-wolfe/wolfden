using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// The server end to end, in process: a real MCP client connects over a
/// pair of pipes, lists the tools, and calls each of them (#20).
/// </summary>
public sealed class McpServerTests : IAsyncLifetime
{
    private static readonly string[] ReadToolNames = ["canon_as_of", "check", "get_chapter", "get_chapter_defaults", "get_entity", "get_history", "list_entities", "search"];

    private static readonly string[] WriteToolNames =
        ["add_idea", "append_section", "change_alias", "checkpoint", "copy_entity", "create_entity", "move_entity", "rename_entity", "rename_section", "update_chapter_defaults", "update_chapter_fields", "update_fields"];

    private readonly TempDen _den = TempDen.Dracula();
    private readonly StringWriter _log = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Pipe _toServer = new();
    private readonly Pipe _toClient = new();
    private Task? _server;
    private McpClient? _client;

    private McpClient Client => _client!;

    public async ValueTask InitializeAsync()
    {
        var transport = new StreamServerTransport(_toServer.Reader.AsStream(), _toClient.Writer.AsStream(), "wolfden");
        _server = WolfdenMcpServer.RunAsync(transport, new DenTools(_den.Root), "0.1.0-test", _log, watch: true, _stop.Token);
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
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-10")]
    [Trait("Scenario", "SC-MCP-44")]
    public async Task TheClientSeesTheServerAndItsTools()
    {
        Assert.Equal("wolfden", Client.ServerInfo!.Name);
        Assert.Contains("story position", Client.ServerInstructions!, StringComparison.Ordinal);
        Assert.Contains("checkpoint", Client.ServerInstructions!, StringComparison.Ordinal);
        Assert.Contains("Read without as_of before changing a fact", Client.ServerInstructions!, StringComparison.Ordinal);

        // When to read ahead, and that later facts stay out of the prose (#300).
        Assert.All(
            new[] { "Read ahead (get_entity without as_of)", "who knows more than the reader", "A later fact can shape hints", "who'd already know", "the prose must never state it or have a narrator", "If you aren't sure which read applies, ask the writer" },
            s => Assert.Contains(s, Client.ServerInstructions!, StringComparison.Ordinal));

        // The traps an agent must know before its first write (#206): each refusal code it can meet, and what to do.
        Assert.All(
            new[] { "stale_file", "frozen", "allow_relinks", "bad_position", "co_author", ".wolfden/" },
            s => Assert.Contains(s, Client.ServerInstructions!, StringComparison.Ordinal));

        var tools = await Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ReadToolNames.Concat(WriteToolNames).Order(StringComparer.Ordinal), tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, t =>
        {
            Assert.Equal(ReadToolNames.Contains(t.Name), t.ProtocolTool.Annotations?.ReadOnlyHint);
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
        });

        var getEntity = tools.Single(t => t.Name == "get_entity").JsonSchema;
        var properties = getEntity.GetProperty("properties");
        Assert.True(properties.TryGetProperty("entity", out _));
        Assert.True(properties.TryGetProperty("as_of", out _));
        Assert.Equal(["entity"], getEntity.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-10")]
    public async Task EachToolAnswersWithJson()
    {
        var calls = new (string Tool, Dictionary<string, object?> Args, string Expect)[]
        {
            ("search", new() { ["query"] = "Voivode" }, "\"hits\":"),
            ("get_entity", new() { ["entity"] = "Mina", ["as_of"] = "dracula:9" }, "\"view\":\"canon_as_of\""),
            ("list_entities", new() { ["type"] = "location" }, "\"entities\":"),
            ("canon_as_of", new() { ["position"] = "dracula:18" }, "\"pinned\":"),
            ("check", new() { ["severity"] = "error" }, "\"loaded\":true"),
            // This den has no history backend: that's an answer, not an error.
            ("get_history", new() { ["entity"] = "Lucy" }, "\"available\":false"),
            ("get_chapter", new() { ["book"] = "dracula", ["chapter"] = "5" }, "\"hash\":"),
            ("get_chapter_defaults", new() { ["book"] = "dracula" }, "\"books\":"),
        };
        foreach (var (tool, args, expect) in calls)
        {
            var result = await Call(tool, args);
            Assert.NotEqual(true, result.IsError);
            var text = Text(result);
            Assert.Contains(expect, text, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(text);
            Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-16")]
    public async Task OverTheWireAWriteTakesItsChangesAsJsonAndAStaleHashIsRefused()
    {
        using var read = JsonDocument.Parse(Text(await Call("get_entity", new() { ["entity"] = "Mr. Swales" })));
        var hash = read.RootElement.GetProperty("entity").GetProperty("hash").GetString();
        var changes = JsonDocument.Parse("""[{"op": "add", "path": "aliases", "value": "old Swales"}]""").RootElement;

        var result = await Call("update_fields", new() { ["entity"] = "Mr. Swales", ["expected_hash"] = hash, ["changes"] = changes });

        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(Text(result));
        Assert.True(json.RootElement.GetProperty("changed").GetBoolean());
        Assert.NotEqual(hash, json.RootElement.GetProperty("hash").GetString());
        Assert.Contains("aliases: [old Swales]", _den.Read("universes/dracula/books/dracula/entities/characters/mr-swales.md"), StringComparison.Ordinal);

        var stale = await Call("update_fields", new() { ["entity"] = "Mr. Swales", ["expected_hash"] = hash, ["changes"] = changes });
        Assert.True(stale.IsError);
        Assert.Contains("\"code\":\"stale_file\"", Text(stale), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-2")]
    public async Task OverTheWireAnEarlyViewHoldsNoSpoilers()
    {
        var text = Text(await Call("canon_as_of", new() { ["position"] = "dracula:4" }));

        foreach (var term in new[] { "Lucy", "Renfield", "Voivode", "Un-Dead" })
        {
            Assert.DoesNotContain(term, text, StringComparison.Ordinal);
        }

        var lucy = Text(await Call("get_entity", new() { ["entity"] = "Lucy", ["as_of"] = "dracula:4" }));
        Assert.Contains("\"in_view\":false", lucy, StringComparison.Ordinal);
        Assert.DoesNotContain("Westenra", lucy, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-44")]
    public async Task ReadingAheadHasWhatACharacterKnowsAndTheReaderDoesnt()
    {
        var renfieldAt = Text(await Call("get_entity", new() { ["entity"] = "Renfield", ["as_of"] = "dracula:20" }));
        Assert.Contains("\"in_view\":true", renfieldAt, StringComparison.Ordinal);
        Assert.DoesNotContain("how he let the Count in", renfieldAt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"died\"", renfieldAt, StringComparison.Ordinal);
        Assert.DoesNotContain("turns on his master", renfieldAt, StringComparison.Ordinal);

        var eventAt = Text(await Call("get_entity", new() { ["entity"] = "Renfield lets the Count in", ["as_of"] = "dracula:20" }));
        Assert.Contains("\"in_view\":false", eventAt, StringComparison.Ordinal);
        Assert.DoesNotContain("Lord and Master", eventAt, StringComparison.Ordinal);

        var renfieldAhead = Text(await Call("get_entity", new() { ["entity"] = "Renfield" }));
        Assert.Contains("how he let the Count in", renfieldAhead, StringComparison.Ordinal);
        Assert.Contains("\"died\"", renfieldAhead, StringComparison.Ordinal);
        Assert.Contains("turns on his master", renfieldAhead, StringComparison.Ordinal);

        var eventAhead = Text(await Call("get_entity", new() { ["entity"] = "Renfield lets the Count in" }));
        Assert.Contains("Lord and Master", eventAhead, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-6")]
    public async Task ABadCallIsAnErrorResultWithACode()
    {
        var result = await Call("get_entity", new() { ["entity"] = "Mina", ["as_of"] = "dracula:99" });

        Assert.True(result.IsError);
        using var json = JsonDocument.Parse(Text(result));
        Assert.Equal("bad_position", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        var unknown = await Call("get_entity", new() { ["entity"] = "Sherlock Holmes" });
        Assert.True(unknown.IsError);
        Assert.Contains("\"code\":\"unknown_entity\"", Text(unknown), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-6")]
    [InlineData("search", "{\"query\":\"Mina\",\"limit\":\"lots\"}")]
    [InlineData("search", "{}")]
    [InlineData("get_entity", "{\"entity\":42}")]
    [InlineData("canon_as_of", "{\"brief\":\"yes\"}")]
    public async Task AnArgumentOfTheWrongTypeIsABadArgument(string tool, string args)
    {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(args)!;
        var result = await Call(tool, parsed);

        Assert.True(result.IsError);
        using var json = JsonDocument.Parse(Text(result));
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("bad_argument", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-7")]
    public async Task AnEditWhileConnectedShowsInTheNextCall()
    {
        var path = "universes/dracula/books/dracula/entities/characters/mr-swales.md";
        var before = Text(await Call("get_entity", new() { ["entity"] = "mr-swales" }));
        Assert.DoesNotContain("Pinned by the sea", before, StringComparison.Ordinal);

        _den.Write(path, _den.Read(path).ReplaceLineEndings("\n") + "\n## Pinned by the sea\nAn added section.\n");

        // The watcher hosted by the server, or the call's own check of the files, brings it in.
        var after = Text(await Call("get_entity", new() { ["entity"] = "mr-swales" }));
        Assert.Contains("Pinned by the sea", after, StringComparison.Ordinal);
        Assert.Contains("keeping its index up to date", _log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-40")]
    public async Task AResultCarriesAShortNoticeWhileTheDenHasErrors()
    {
        const string Path = "universes/dracula/entities/notes/ember-vault.md";
        const string Exact = "This den has 2 errors, so some notes or sections may be missing from this answer. Call check with severity 'error' to see them.";
        using (var clean = JsonDocument.Parse(Text(await Call("list_entities", new()))))
        {
            Assert.False(clean.RootElement.TryGetProperty("den_notice", out _));
        }

        _den.Write(Path, "---\ntype: note\nname: Ember Vault\nstatus: maybe\n---\n\nA vault.\n");
        const string Swales = "universes/dracula/books/dracula/entities/characters/mr-swales.md";
        _den.Write(Swales, _den.Read(Swales) + "\n## The secret twin\n<!-- wolfden: status=maybe -->\nWhispers.\n");

        // The same small line in every kind of answer, naming nothing, so it's safe at a position.
        foreach (var (tool, args) in new (string, Dictionary<string, object?>)[]
        {
            ("list_entities", new()),
            ("get_entity", new() { ["entity"] = "Mina Harker", ["as_of"] = "dracula:1" }),
            ("canon_as_of", new() { ["position"] = "dracula:1", ["brief"] = true }),
            ("search", new() { ["query"] = "Mina" }),
        })
        {
            var result = await Call(tool, args);
            using var json = JsonDocument.Parse(Text(result));
            Assert.Equal(Exact, json.RootElement.GetProperty("den_notice").GetString());
            Assert.Equal(Exact, result.StructuredContent!.Value.GetProperty("den_notice").GetString());
        }

        // An error result too, and it's still Wolfden's error.
        var refused = await Call("get_entity", new() { ["entity"] = "Sherlock Holmes" });
        Assert.True(refused.IsError);
        using (var json = JsonDocument.Parse(Text(refused)))
        {
            Assert.Equal("unknown_entity", json.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(Exact, json.RootElement.GetProperty("den_notice").GetString());
        }

        // check lists them itself.
        using (var check = JsonDocument.Parse(Text(await Call("check", new() { ["severity"] = "error" }))))
        {
            Assert.False(check.RootElement.TryGetProperty("den_notice", out _));
            Assert.Equal(2, check.RootElement.GetProperty("errors").GetInt32());
        }

        // It clears when they're fixed.
        _den.Write(Path, "---\ntype: note\nname: Ember Vault\nstatus: canon\n---\n\nA vault.\n");
        _den.Write(Swales, _den.Read(Swales).Replace("status=maybe", "status=canon", StringComparison.Ordinal));
        using var after = JsonDocument.Parse(Text(await Call("list_entities", new())));
        Assert.False(after.RootElement.TryGetProperty("den_notice", out _));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-40")]
    public async Task ANoteLeftOutSaysWhyButNotAtAPosition()
    {
        const string Path = "universes/dracula/entities/notes/ember-vault.md";
        _den.Write(Path, "---\ntype: note\nname: Ember Vault\nstatus: maybe\n---\n\nA vault.\n");

        var full = Text(await Call("get_entity", new() { ["entity"] = "Ember Vault" }));
        Assert.Contains("left out of the den by an error in " + Path + ", line 4", full, StringComparison.Ordinal);

        var at = Text(await Call("get_entity", new() { ["entity"] = "Ember Vault", ["as_of"] = "dracula:1" }));
        Assert.DoesNotContain("left out", at, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"unknown_entity\"", at, StringComparison.Ordinal);
    }

    private async Task<CallToolResult> Call(string tool, Dictionary<string, object?> args) =>
        await Client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}

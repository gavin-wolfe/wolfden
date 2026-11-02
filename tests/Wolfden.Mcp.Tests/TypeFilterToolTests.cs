using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// A type filter over the wire (types.md F-TYPE-13): a real MCP client asks a
/// den whose types and notes were in place before the server started, so no
/// watcher is running while they change.
/// </summary>
public sealed class TypeFilterToolTests : IAsyncLifetime
{
    private readonly TempDen _den = TempDen.Dracula();
    private readonly CancellationTokenSource _stop = new();
    private readonly Pipe _toServer = new();
    private readonly Pipe _toClient = new();
    private Task? _server;
    private McpClient? _client;

    private McpClient Client => _client!;

    public async ValueTask InitializeAsync()
    {
        _den.Write("den.yaml", _den.Read("den.yaml") + "packs: [space-opera, starfall]\n");
        _den.Write("packs/starfall/pack.yaml", "name: Starfall\n");
        _den.Write("packs/starfall/types/moon.yaml", "name: Moon\nplural: moons\nkind_of: world\n");
        _den.Write("universes/dracula/entities/worlds/vesta.md", "---\ntype: world\nname: Vesta\nstatus: canon\n---\n# Vesta\n");
        _den.Write("universes/dracula/entities/moons/little-vesta.md", "---\ntype: moon\nname: Little Vesta\nstatus: canon\n---\n# Little Vesta\n");

        var transport = new StreamServerTransport(_toServer.Reader.AsStream(), _toClient.Writer.AsStream(), "wolfden");
        _server = WolfdenMcpServer.RunAsync(transport, new DenTools(_den.Root), "0.1.0-test", TextWriter.Null, watch: false, _stop.Token);
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
    }

    private async Task<Dictionary<string, string>> Listed(string type)
    {
        var result = await Client.CallToolAsync("list_entities", new Dictionary<string, object?> { ["type"] = type }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        return json.RootElement.GetProperty("entities").EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => e.GetProperty("type").GetString()!);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-52")]
    public async Task ListEntitiesIncludesKindsOfTheTypeAndShowsEachOwnType()
    {
        var locations = await Listed("location");
        Assert.Equal("world", locations["Vesta"]);
        Assert.Equal("moon", locations["Little Vesta"]);
        Assert.Contains("location", locations.Values);

        Assert.Equal(new Dictionary<string, string> { ["Vesta"] = "world", ["Little Vesta"] = "moon" }, await Listed("world"));
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-52")]
    [InlineData("list_entities")]
    [InlineData("search")]
    [InlineData("canon_as_of")]
    public async Task AToolsDescriptionOfTypeSaysItIncludesKinds(string tool)
    {
        var tools = await Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var type = tools.Single(t => t.Name == tool).JsonSchema.GetProperty("properties").GetProperty("type");

        Assert.Contains("a kind of it", type.GetProperty("description").GetString(), StringComparison.Ordinal);
    }
}

using ModelContextProtocol.Client;

namespace Wolfden.Mcp.Tests;

/// <summary>What the tests' MCP clients have in common.</summary>
internal static class TestClient
{
    /// <summary>
    /// Client options that wait longer for <c>server/discover</c>. The SDK's
    /// client waits 5 seconds by default, and on a busy CI machine the answer
    /// can take longer. The client then falls back to <c>initialize</c>, which
    /// the server refuses once it has answered the probe after all (#135, #268).
    /// </summary>
    public static McpClientOptions Options() => new() { DiscoverProbeTimeout = TimeSpan.FromSeconds(30) };
}

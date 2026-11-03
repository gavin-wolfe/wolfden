using System.Net;
using System.Text;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// format.md F-GEN-2 (#50) on the web API: a note's page in the full view and
/// its source flag a section an error leaves out, in place, by its line, and a
/// save that adds no error goes through.
/// </summary>
public sealed class SectionErrorEndpointTests
{
    private const string Mina = "universes/dracula/books/dracula/entities/characters/mina-harker.md";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static TempDen NewDen()
    {
        var den = TempDen.Dracula();
        den.Write(Mina, den.Read(Mina) + "\n## A rumor\n<!-- wolfden: status=maybe -->\nWhispers of [[Jonathan Harker]].\n\n### Its source\nA letter.\n");
        return den;
    }

    private static JsonElement Named(JsonElement sections, string name) =>
        sections.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    [Fact]
    [Trait("Scenario", "SC-WEB-83")]
    public async Task ThePageShowsItInPlaceFlaggedByLineWithItsLinksAndTheRestAsUsual()
    {
        using var den = NewDen();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var line = den.Read(Mina).ReplaceLineEndings("\n").Split('\n').ToList().IndexOf("<!-- wolfden: status=maybe -->") + 1;

        var page = (await server.GetJsonAsync("/api/entity?entity=Mina%20Harker", HttpStatusCode.OK)).GetProperty("entity");

        var sections = page.GetProperty("sections");
        var rumor = Named(sections, "A rumor");
        var problem = Assert.Single(rumor.GetProperty("leftOut").EnumerateArray());
        Assert.Equal(line, problem.GetProperty("line").GetInt32());
        Assert.Equal("F-STAT-1", problem.GetProperty("rule").GetString());
        Assert.Contains("'maybe' isn't a status", problem.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("Whispers of [[Jonathan Harker]].", rumor.GetProperty("body").GetString());
        Assert.Equal("resolved", Assert.Single(rumor.GetProperty("links").EnumerateArray()).GetProperty("outcome").GetString());
        Assert.Equal(line, Assert.Single(Named(sections, "Its source").GetProperty("leftOut").EnumerateArray()).GetProperty("line").GetInt32());
        Assert.Equal(JsonValueKind.Null, Named(sections, "Overview").GetProperty("leftOut").ValueKind);

        // Without a position, a link in it still counts as a backlink.
        var jonathan = (await server.GetJsonAsync("/api/entity?entity=Jonathan%20Harker", HttpStatusCode.OK)).GetProperty("entity");
        Assert.Contains(jonathan.GetProperty("backlinks").EnumerateArray(), b => b.GetProperty("section").ValueKind == JsonValueKind.String && b.GetProperty("section").GetString() == "A rumor");

        // A search without a position marks it.
        var hit = Assert.Single((await server.GetJsonAsync("/api/search?query=whispers", HttpStatusCode.OK)).GetProperty("hits").EnumerateArray());
        Assert.True(hit.GetProperty("leftOut").GetBoolean());
        Assert.False(hit.GetProperty("inCanon").GetBoolean());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-83")]
    public async Task TheSourceFlagsItAndAFileSaveThatFixesItWorks()
    {
        using var den = NewDen();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var source = await server.GetJsonAsync("/api/entity/source?entity=Mina%20Harker", HttpStatusCode.OK);
        Assert.Equal("F-STAT-1", Assert.Single(Named(source.GetProperty("sections"), "A rumor").GetProperty("leftOut").EnumerateArray()).GetProperty("rule").GetString());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/edit/file")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    entity = "Mina Harker",
                    text = source.GetProperty("text").GetString()!.Replace("status=maybe", "status=draft", StringComparison.Ordinal),
                    expectedHash = source.GetProperty("hash").GetString(),
                }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Origin", server.Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        using var response = await server.Client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await server.GetJsonAsync("/api/entity?entity=Mina%20Harker&asOf=dracula:note", HttpStatusCode.OK)).GetProperty("entity");
        Assert.Contains(page.GetProperty("sections").EnumerateArray(), s => s.GetProperty("name").GetString() == "A rumor");
    }
}

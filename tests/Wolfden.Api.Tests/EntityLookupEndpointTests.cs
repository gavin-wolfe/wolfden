using System.Net;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// <c>/api/entities/lookup</c> (#198): the link pickers' lookup of notes by
/// name, alias, slug, or id, a word at a time.
/// </summary>
public class EntityLookupEndpointTests
{
    private const string MinaId = "01M4TF5F0B7KDTSAS8D2NYT37J";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static List<JsonElement> Matches(JsonElement json) => json.GetProperty("matches").EnumerateArray().ToList();

    private static List<string> Names(JsonElement json) => Matches(json).Select(m => m.GetProperty("entity").GetProperty("name").GetString()!).ToList();

    [Fact]
    [Trait("Scenario", "SC-WEB-78")]
    public async Task ALookupFindsANoteByAnAliasAndSaysWhichOne()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var json = await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=madam", HttpStatusCode.OK);
        var match = Assert.Single(Matches(json));
        Assert.Equal("Mina Harker", match.GetProperty("entity").GetProperty("name").GetString());
        Assert.Equal(MinaId, match.GetProperty("entity").GetProperty("id").GetString());
        Assert.Equal("alias", match.GetProperty("matchedBy").GetString());
        Assert.Equal("Madam Mina", match.GetProperty("alias").GetString());
        Assert.Equal("dracula", json.GetProperty("universe").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("position").ValueKind);

        // Each word typed starts a word, in any order, ignoring case: "mur wil" is Wilhelmina Murray.
        match = Assert.Single(Matches(await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=MUR%20wil", HttpStatusCode.OK)));
        Assert.Equal("Wilhelmina Murray", match.GetProperty("alias").GetString());

        // A name match says no alias.
        match = Matches(await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=jonathan%20har", HttpStatusCode.OK))[0];
        Assert.Equal("Jonathan Harker", match.GetProperty("entity").GetProperty("name").GetString());
        Assert.Equal("name", match.GetProperty("matchedBy").GetString());
        Assert.Equal(JsonValueKind.Null, match.GetProperty("alias").ValueKind);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-78")]
    public async Task MatchesAreRankedExactThenFromTheStartThenAnywhere()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        // "Mina" is exactly one of Mina Harker's aliases; one event's name starts with it, another's holds it.
        var json = await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=mina", HttpStatusCode.OK);
        Assert.Equal(["Mina Harker", "Mina and Jonathan marry", "The Count visits Mina"], Names(json).Take(3));

        // An exact name comes before a name that only starts so.
        Assert.Equal("Count Dracula", Names(await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=count%20dracula", HttpStatusCode.OK))[0]);

        // A slug matches where the name doesn't ("lucys" isn't a word of "Lucy's three proposals").
        var slug = Assert.Single(Matches(await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=lucys", HttpStatusCode.OK)));
        Assert.Equal("slug", slug.GetProperty("matchedBy").GetString());

        // An id, whole or by its first ten characters or more.
        var id = Assert.Single(Matches(await server.GetJsonAsync($"/api/entities/lookup?universe=dracula&query={MinaId}", HttpStatusCode.OK)));
        Assert.Equal("id", id.GetProperty("matchedBy").GetString());
        Assert.Equal("Mina Harker", Assert.Single(Names(await server.GetJsonAsync($"/api/entities/lookup?universe=dracula&query={MinaId[..14]}", HttpStatusCode.OK))));
        Assert.Empty(Matches(await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=01M4TF", HttpStatusCode.OK)));

        // limit cuts the list and says so.
        json = await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=the&limit=2", HttpStatusCode.OK);
        Assert.Equal(2, json.GetProperty("count").GetInt32());
        Assert.True(json.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-78")]
    public async Task TypesNarrowALookupToAFieldsToTypes()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        Assert.Equal(["Mina Harker"], Names(await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=mina&type=character", HttpStatusCode.OK)));
        Assert.Equal(
            ["Mina Harker", "Mina and Jonathan marry", "The Count visits Mina"],
            Names(await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=mina&type=character,%20event", HttpStatusCode.OK)));

        var error = await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=mina&type=charcter", HttpStatusCode.BadRequest);
        Assert.Equal("unknown_type", error.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-78")]
    public async Task AsOfAPositionOnlyNotesTheReaderHasMetAreOffered()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        // Van Helsing enters at chapter 9; the editors never send asOf, so they offer him anywhere.
        Assert.Contains("Abraham Van Helsing", Names(await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=professor", HttpStatusCode.OK)));
        var early = await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=professor&asOf=dracula:1", HttpStatusCode.OK);
        Assert.Empty(Matches(early));
        Assert.Equal("dracula:1", early.GetProperty("position").GetString());
        Assert.Contains(
            "Abraham Van Helsing",
            Names(await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=professor&asOf=dracula:9", HttpStatusCode.OK)));

        // A blank position fails closed, as everywhere.
        await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=mina&asOf=", HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-79")]
    public async Task ALookupIsGuarded()
    {
        using var den = TempDen.Starfall();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root) { PinnedUniverse = "drift" });

        // Pinned: another universe is refused; the pinned one is the default.
        var refused = await server.GetJsonAsync("/api/entities/lookup?universe=starfall&query=mara", HttpStatusCode.BadRequest);
        Assert.Contains("wolfden serve --universe", refused.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("drift", (await server.GetJsonAsync("/api/entities/lookup?query=a", HttpStatusCode.OK)).GetProperty("universe").GetString());
        Assert.Equal("bad_argument", (await server.GetJsonAsync("/api/entities/lookup?universe=*&query=mara", HttpStatusCode.BadRequest)).GetProperty("code").GetString());

        // No words, a parameter it doesn't take, and a limit out of range.
        Assert.Equal("bad_argument", (await server.GetJsonAsync("/api/entities/lookup?query=%20-%20", HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        Assert.Equal("bad_argument", (await server.GetJsonAsync("/api/entities/lookup?q=mara", HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        Assert.Equal("bad_argument", (await server.GetJsonAsync("/api/entities/lookup?query=mara&limit=101", HttpStatusCode.BadRequest)).GetProperty("code").GetString());

        // GET only, and cross-site requests are refused like any other.
        using (var post = await server.Client.PostAsync("/api/entities/lookup?query=mara", null, Token))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/entities/lookup?query=mara");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var response = await server.Client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

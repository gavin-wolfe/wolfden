using System.Net;
using System.Text;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// Creating series and books from the web UI (#213): <c>GET /api/works</c>,
/// <c>POST /api/series/create</c>, and <c>POST /api/book/create</c>, through
/// the write tools' code, over a copy of the Dracula sample and the invented
/// Starfall den.
/// </summary>
public sealed class ContainerEndpointTests : IDisposable
{
    private const string DraculaUniverse = "universes/dracula/universe.md";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly List<TempDen> _dens = [];

    public void Dispose()
    {
        foreach (var den in _dens)
        {
            den.Dispose();
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-69")]
    public async Task ANewBookAndSeriesAreMadeWhereTheWriterSaysAndTheAnswerNamesThem()
    {
        var den = Den(TempDen.Dracula());
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var works = await server.GetJsonAsync("/api/works?universe=dracula", HttpStatusCode.OK);
        Assert.Equal(DraculaUniverse, works.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, works.GetProperty("series").ValueKind);
        Assert.Equal([("book", "dracula", "Dracula")], works.GetProperty("works").EnumerateArray().Select(w => (w.GetProperty("kind").GetString(), w.GetProperty("key").GetString(), w.GetProperty("name").GetString())));

        var (code, made) = await PostAsync(server, "/api/book/create", new
        {
            name = "Dracula's Guest", universe = "dracula", chapters = 2, expectedHash = works.GetProperty("hash").GetString(),
        });

        Assert.Equal(HttpStatusCode.Created, code);
        const string Path = "universes/dracula/books/dracula-s-guest/book.md";
        Assert.Equal(Path, made.GetProperty("path").GetString());
        Assert.Equal("book", made.GetProperty("kind").GetString());
        Assert.Equal("dracula-s-guest", made.GetProperty("key").GetString());
        Assert.Equal(2, made.GetProperty("chapters").GetInt32());
        Assert.Equal(JsonValueKind.Null, made.GetProperty("series").ValueKind);
        Assert.Equal([Path, DraculaUniverse], made.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("path").GetString()));
        Assert.Contains("\nname: Dracula's Guest\n", den.Read(Path), StringComparison.Ordinal);
        Assert.Contains("\norder: [dracula, dracula-s-guest]\n", den.Read(DraculaUniverse).ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // The tree has it, with its chapters, and the page can open it by key.
        var hierarchy = await server.GetJsonAsync("/api/hierarchy?universe=dracula", HttpStatusCode.OK);
        var book = hierarchy.GetProperty("universe").GetProperty("books").EnumerateArray().Single(b => b.GetProperty("key").GetString() == "dracula-s-guest");
        Assert.Equal(["dracula-s-guest:1", "dracula-s-guest:2"], book.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("position").GetString()));

        // A series, then a book in it, placed after the first; each create takes the hash it was based on.
        var (seriesCode, series) = await PostAsync(server, "/api/series/create", new
        {
            name = "The Harker Papers", after = "dracula", expectedHash = (await server.GetJsonAsync("/api/works", HttpStatusCode.OK)).GetProperty("hash").GetString(),
        });
        Assert.Equal(HttpStatusCode.Created, seriesCode);
        Assert.Equal("the-harker-papers", series.GetProperty("key").GetString());
        Assert.Contains("\norder: [dracula, the-harker-papers, dracula-s-guest]\n", den.Read(DraculaUniverse).ReplaceLineEndings("\n"), StringComparison.Ordinal);

        var seriesWorks = await server.GetJsonAsync("/api/works?universe=dracula&series=the-harker-papers", HttpStatusCode.OK);
        Assert.Equal("the-harker-papers", seriesWorks.GetProperty("series").GetString());
        Assert.Empty(seriesWorks.GetProperty("works").EnumerateArray());
        var (inSeriesCode, inSeries) = await PostAsync(server, "/api/book/create", new
        {
            name = "Mina's Journal", series = "the-harker-papers", expectedHash = seriesWorks.GetProperty("hash").GetString(),
        });
        Assert.Equal(HttpStatusCode.Created, inSeriesCode);
        Assert.Equal("the-harker-papers", inSeries.GetProperty("series").GetString());
        Assert.Equal("universes/dracula/series/the-harker-papers/books/mina-s-journal/book.md", inSeries.GetProperty("path").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-70")]
    public async Task AStaleClashingOrMalformedCreateIsRefusedAndWritesNothing()
    {
        var den = Den(TempDen.Starfall());
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var universe = await server.GetJsonAsync("/api/works?universe=starfall", HttpStatusCode.OK);
        var hash = universe.GetProperty("hash").GetString();
        var before = Files(den);

        async Task Refused(string path, object body, HttpStatusCode status, string code, string? text = null)
        {
            var (got, json) = await PostAsync(server, path, body);
            Assert.Equal(status, got);
            Assert.Equal(code, json.GetProperty("code").GetString());
            if (text is not null)
            {
                Assert.Contains(text, json.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
        }

        await Refused("/api/book/create", new { name = "Cinder", universe = "starfall" }, HttpStatusCode.BadRequest, "bad_argument", "Give 'expectedHash'");
        await Refused("/api/book/create", new { universe = "starfall", expectedHash = hash }, HttpStatusCode.BadRequest, "bad_argument", "Give the new book a name.");
        await Refused("/api/series/create", new { name = " ", universe = "starfall", expectedHash = hash }, HttpStatusCode.BadRequest, "bad_argument", "it's empty");
        await Refused("/api/book/create", new { name = "Ember", universe = "starfall", expectedHash = hash }, HttpStatusCode.Conflict, "already_exists", "F-KEY-5");
        await Refused("/api/book/create", new { name = "Cinder", universe = "starfall", after = "ember", expectedHash = hash }, HttpStatusCode.BadRequest, "bad_argument", "long-dark, a-quiet-moon, drift-war");
        await Refused("/api/book/create", new { name = "Cinder", universe = "starfall", series = "nope", expectedHash = hash }, HttpStatusCode.NotFound, "unknown_scope");
        await Refused("/api/book/create", new { name = "Cinder", universe = "starfall", chapters = "3", expectedHash = hash }, HttpStatusCode.BadRequest, "bad_argument", "whole number");
        await Refused("/api/book/create", new { name = "Cinder", universe = "starfall", chapters = 1001, expectedHash = hash }, HttpStatusCode.BadRequest, "bad_argument", "1 to 1000");
        await Refused("/api/series/create", new { name = "Cinder", universe = "starfall", chapters = 3, expectedHash = hash }, HttpStatusCode.BadRequest, "bad_argument", "doesn't take 'chapters'");
        await Refused("/api/series/create", new { name = "Cinder", universe = "starfall", expectedHash = "0000" }, HttpStatusCode.Conflict, "stale_file", "has changed since it was read");

        // A series' hash isn't the universe's.
        var series = (await server.GetJsonAsync("/api/works?universe=starfall&series=long-dark", HttpStatusCode.OK)).GetProperty("hash").GetString();
        await Refused("/api/book/create", new { name = "Cinder", universe = "starfall", expectedHash = series }, HttpStatusCode.Conflict, "stale_file");

        var (form, _) = await PostAsync(server, "/api/book/create", "name=Cinder", "application/x-www-form-urlencoded");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, form);
        using var get = await server.Client.GetAsync("/api/book/create", Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Equal("POST", get.Content.Headers.Allow.Single());
        using var post = await server.Client.PostAsync("/api/works", new StringContent("{}", Encoding.UTF8, "application/json"), Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        using var extra = await server.Client.GetAsync("/api/works?universe=starfall&book=ember", Token);
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);

        Assert.Equal(before, Files(den));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-70")]
    public async Task APinnedServerCreatesOnlyInItsUniverse()
    {
        var den = Den(TempDen.Starfall());
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root) { PinnedUniverse = "starfall" });

        using var other = await server.Client.GetAsync("/api/works?universe=drift", Token);
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        var (refused, _) = await PostAsync(server, "/api/book/create", new { name = "Undertow", universe = "drift", expectedHash = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, refused);
        Assert.False(Directory.Exists(System.IO.Path.Combine(den.Root, "universes", "drift", "books", "undertow")));

        var hash = (await server.GetJsonAsync("/api/works", HttpStatusCode.OK)).GetProperty("hash").GetString();
        var (code, made) = await PostAsync(server, "/api/book/create", new { name = "Salt and Iron", expectedHash = hash });
        Assert.Equal(HttpStatusCode.Created, code);
        Assert.Equal("starfall", made.GetProperty("universe").GetString());
    }

    // ------------------------------------------------------------ helpers

    private TempDen Den(TempDen den)
    {
        _dens.Add(den);
        return den;
    }

    private static Dictionary<string, string> Files(TempDen den) =>
        Directory.EnumerateFiles(den.Root, "*", SearchOption.AllDirectories)
            .Select(f => System.IO.Path.GetRelativePath(den.Root, f).Replace('\\', '/'))
            .Where(p => !p.StartsWith(".wolfden/", StringComparison.Ordinal))
            .ToDictionary(p => p, den.Read, StringComparer.Ordinal);

    private static async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(Server server, string path, object body, string contentType = "application/json")
    {
        using var content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, contentType);
        using var response = await server.Client.PostAsync(path, content, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }
}

using System.Net;
using System.Text;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// Adding a chapter from the web UI (#215): <c>GET /api/book/chapters</c>
/// and <c>POST /api/chapter/create</c>, through the write tools' code, over
/// copies of the invented Starfall den.
/// </summary>
public sealed class ChapterEndpointTests : IDisposable
{
    private const string Ember = "universes/starfall/series/long-dark/books/ember/book.md";

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
    [Trait("Scenario", "SC-WEB-71")]
    public async Task AChapterIsAddedWhereTheWriterSaysAndTheAnswerSaysWhatChanged()
    {
        var den = Den(TempDen.Starfall());
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var chapters = await server.GetJsonAsync("/api/book/chapters?universe=starfall&book=ember", HttpStatusCode.OK);
        Assert.Equal(Ember, chapters.GetProperty("path").GetString());
        Assert.False(chapters.GetProperty("final").GetBoolean());
        Assert.Equal(["prologue", "1", "2", "2b", "3"], chapters.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("key").GetString()));

        var (code, made) = await PostAsync(server, "/api/chapter/create", new
        {
            book = "ember", universe = "starfall", title = "The Quiet Hour", after = "2", expectedHash = chapters.GetProperty("hash").GetString(),
        });

        Assert.Equal(HttpStatusCode.Created, code);
        Assert.Equal("2c", made.GetProperty("key").GetString());
        Assert.Equal("ember:2c", made.GetProperty("position").GetString());
        Assert.Equal(3, made.GetProperty("number").GetInt64());
        Assert.Equal("The Quiet Hour", made.GetProperty("title").GetString());
        Assert.Equal([("2b", 3L, 4L), ("3", 4L, 5L)], made.GetProperty("renumbered").EnumerateArray().Select(r => (r.GetProperty("key").GetString(), r.GetProperty("from").GetInt64(), r.GetProperty("to").GetInt64())));
        Assert.Equal([Ember], made.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("path").GetString()));
        Assert.Contains("  - { key: \"2c\", number: 3, title: The Quiet Hour }\n", den.Read(Ember).ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // The tree has it in its place; at the end, with the new hash, the next whole number.
        var hierarchy = await server.GetJsonAsync("/api/hierarchy?universe=starfall", HttpStatusCode.OK);
        var ember = hierarchy.GetProperty("universe").GetProperty("series").EnumerateArray()
            .SelectMany(s => s.GetProperty("books").EnumerateArray()).Single(b => b.GetProperty("key").GetString() == "ember");
        Assert.Equal(["prologue", "1", "2", "2c", "2b", "3"], ember.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("key").GetString()));

        var (endCode, end) = await PostAsync(server, "/api/chapter/create", new { book = "ember", universe = "starfall", expectedHash = made.GetProperty("hash").GetString() });
        Assert.Equal(HttpStatusCode.Created, endCode);
        Assert.Equal("4", end.GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Null, end.GetProperty("title").ValueKind);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-72")]
    public async Task AStaleFrozenOrMalformedAddIsRefusedAndWritesNothing()
    {
        var den = Den(TempDen.Starfall());
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = (await server.GetJsonAsync("/api/book/chapters?universe=starfall&book=ember", HttpStatusCode.OK)).GetProperty("hash").GetString();
        var before = den.Read(Ember);

        async Task Refused(object body, HttpStatusCode status, string code, string? text = null)
        {
            var (got, json) = await PostAsync(server, "/api/chapter/create", body);
            Assert.Equal(status, got);
            Assert.Equal(code, json.GetProperty("code").GetString());
            if (text is not null)
            {
                Assert.Contains(text, json.GetProperty("message").GetString(), StringComparison.Ordinal);
            }

            Assert.Equal(before, den.Read(Ember));
        }

        await Refused(new { book = "ember", universe = "starfall" }, HttpStatusCode.BadRequest, "bad_argument", "Give 'expectedHash'");
        await Refused(new { universe = "starfall", expectedHash = hash }, HttpStatusCode.BadRequest, "bad_argument", "Give the key of the book");
        await Refused(new { book = "ember", universe = "starfall", after = "9", expectedHash = hash }, HttpStatusCode.BadRequest, "unknown_chapter", "prologue, 1, 2, 2b, 3");
        await Refused(new { book = "ember", universe = "starfall", title = "Two\nlines", expectedHash = hash }, HttpStatusCode.BadRequest, "bad_argument", "one line");
        await Refused(new { book = "ember", universe = "starfall", title = new string('x', 201), expectedHash = hash }, HttpStatusCode.BadRequest, "bad_argument", "at most 200");
        await Refused(new { book = "ember", universe = "starfall", expectedHash = "0000" }, HttpStatusCode.Conflict, "stale_file", "has changed since it was read");
        await Refused(new { book = "ember", universe = "starfall", number = 3, expectedHash = hash }, HttpStatusCode.BadRequest, "bad_argument");

        var (form, _) = await PostAsync(server, "/api/chapter/create", "book=ember", "application/x-www-form-urlencoded");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, form);
        using var get = await server.Client.GetAsync("/api/chapter/create", Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        using var post = await server.Client.PostAsync("/api/book/chapters", new StringContent("{}", Encoding.UTF8, "application/json"), Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);

        // A final book.
        den.Write(Ember, before.Replace("status: draft", "status: final", StringComparison.Ordinal));
        before = den.Read(Ember);
        var final = await server.GetJsonAsync("/api/book/chapters?universe=starfall&book=ember", HttpStatusCode.OK);
        Assert.True(final.GetProperty("final").GetBoolean());
        await Refused(new { book = "ember", universe = "starfall", expectedHash = final.GetProperty("hash").GetString() }, HttpStatusCode.Conflict, "frozen", "is final (frozen)");
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-72")]
    public async Task APinnedServerAddsChaptersOnlyInItsUniverse()
    {
        var den = Den(TempDen.Starfall());
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root) { PinnedUniverse = "starfall" });
        const string Tide = "universes/drift/books/tide/book.md";
        var tide = den.Read(Tide);

        using var other = await server.Client.GetAsync("/api/book/chapters?universe=drift&book=tide", Token);
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        var (refused, _) = await PostAsync(server, "/api/chapter/create", new { book = "tide", universe = "drift", expectedHash = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, refused);
        Assert.Equal(tide, den.Read(Tide));
    }

    // ------------------------------------------------------------ helpers

    private TempDen Den(TempDen den)
    {
        _dens.Add(den);
        return den;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(Server server, string path, object body, string contentType = "application/json")
    {
        using var content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, contentType);
        using var response = await server.Client.PostAsync(path, content, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }
}

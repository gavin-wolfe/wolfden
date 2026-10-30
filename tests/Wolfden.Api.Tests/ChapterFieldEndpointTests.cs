using System.Net;
using System.Text;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// Chapter fields in the web UI (#225): <c>GET /api/book/chapter-fields</c>,
/// <c>POST /api/chapter/edit</c>, <c>GET /api/chapter-defaults</c>,
/// <c>POST /api/chapter-defaults/edit</c>, and fields on
/// <c>POST /api/chapter/create</c>, through the MCP write tools' code, over
/// copies of the invented Starfall den.
/// </summary>
public sealed class ChapterFieldEndpointTests : IDisposable
{
    private const string LongDark = "universes/starfall/series/long-dark";
    private const string Ember = LongDark + "/books/ember/book.md";
    private const string Ash = LongDark + "/books/ash/book.md";
    private const string Series = LongDark + "/series.md";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly TempDen _den = TempDen.Starfall();

    public ChapterFieldEndpointTests()
    {
        // The series is told in the past tense; Ember is Mara Voss's, but its prologue has no POV and is set earlier.
        _den.Write(Series, """
            ---
            id: 01JA3E28JT97KB6CQ643DZVMXX
            name: The Long Dark
            books: [ember, ash]
            chapter_defaults: { tense: past }
            ---
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        _den.Write(Ember, """
            ---
            id: 01JAQKFBF5KZNWJ47TAN9ZT24M
            name: Ember
            status: draft
            chapter_defaults: { pov: ["[[mara-voss]]"] }
            chapters:
              - { key: prologue, title: Before the Dark, fields: { pov: [], when: "2284" } }
              - { key: "1", number: 1, title: Kessan Reach }
              - { key: "2", number: 2 }
              - { key: 2b, number: 3, title: The Long Watch }   # added in revision
              - { key: "3", number: 4, fields: { tense: futur } }
            ---
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    public void Dispose() => _den.Dispose();

    [Fact]
    [Trait("Scenario", "SC-WEB-73")]
    public async Task EachChaptersFieldsSayWhatHoldsAndWhereItComesFrom()
    {
        await using var server = await Server.StartAsync(new WolfdenApiOptions(_den.Root));
        var book = await server.GetJsonAsync("/api/book/chapter-fields?universe=starfall&book=ember", HttpStatusCode.OK);

        Assert.Equal(Ember, book.GetProperty("path").GetString());
        Assert.False(book.GetProperty("final").GetBoolean());
        // The den's chapter fields, from its packs (the core pack here), each with what a form needs.
        var types = book.GetProperty("fieldTypes").EnumerateArray().ToList();
        Assert.Equal(["pov", "narration", "tense", "when", "setting"], types.Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(("ref", true, "character"), (types[0].GetProperty("type").GetString(), types[0].GetProperty("list").GetBoolean(), types[0].GetProperty("to")[0].GetString()));
        Assert.Equal(["past", "present", "future"], types[2].GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        Assert.All(types, t => Assert.False(t.GetProperty("temporal").GetBoolean()));

        var chapters = book.GetProperty("chapters").EnumerateArray().ToList();
        Assert.Equal(["prologue", "1", "2", "2b", "3"], chapters.Select(c => c.GetProperty("key").GetString()));
        Assert.Equal("ember:prologue", chapters[0].GetProperty("position").GetString());
        JsonElement Field(int chapter, string name) => chapters[chapter].GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == name);

        // The prologue's own [] is "none here", and the book's default is what it would have without it.
        var pov = Field(0, "pov");
        Assert.True(pov.GetProperty("set").GetBoolean());
        Assert.Equal("[]", pov.GetProperty("own").GetRawText());
        Assert.True(pov.GetProperty("none").GetBoolean());
        Assert.Equal("chapter", pov.GetProperty("from").GetString());
        Assert.Equal("[\"[[mara-voss]]\"]", pov.GetProperty("default").GetRawText());
        Assert.Equal(("book", "ember"), (pov.GetProperty("defaultFrom").GetString(), pov.GetProperty("defaultFromKey").GetString()));
        Assert.Equal("\"2284\"", Field(0, "when").GetProperty("value").GetRawText());

        // Chapter 1 inherits its POV from the book, with the link resolved, and its tense from the series.
        var inherited = Field(1, "pov");
        Assert.False(inherited.GetProperty("set").GetBoolean());
        Assert.Equal(JsonValueKind.Null, inherited.GetProperty("own").ValueKind);
        Assert.Equal(("book", "ember"), (inherited.GetProperty("from").GetString(), inherited.GetProperty("fromKey").GetString()));
        var link = Assert.Single(inherited.GetProperty("links").EnumerateArray());
        Assert.Equal(("[[mara-voss]]", "resolved", "Mara Voss"), (link.GetProperty("text").GetString(), link.GetProperty("outcome").GetString(), link.GetProperty("name").GetString()));
        Assert.Equal("01JA7Q8M4XK3V9R2D5W8HN6TQB", link.GetProperty("entity").GetString());
        Assert.Equal(("series", "long-dark", "\"past\""), (Field(1, "tense").GetProperty("from").GetString(), Field(1, "tense").GetProperty("fromKey").GetString(), Field(1, "tense").GetProperty("value").GetRawText()));
        // Each field carries its chapter item's lines as written, for a stale save's check.
        Assert.Equal("  - { key: \"1\", number: 1, title: Kessan Reach }", inherited.GetProperty("text").GetString());
        Assert.Equal("  - { key: 2b, number: 3, title: The Long Watch }   # added in revision", Field(3, "pov").GetProperty("text").GetString());

        // A field nothing sets is listed, with no value.
        Assert.Equal(JsonValueKind.Null, Field(1, "narration").GetProperty("value").ValueKind);

        // A value that doesn't fit is written but ignored, so the chapter inherits (F-CHAP-3).
        var futur = Field(4, "tense");
        Assert.True(futur.GetProperty("set").GetBoolean());
        Assert.True(futur.GetProperty("ignored").GetBoolean());
        Assert.Equal("\"futur\"", futur.GetProperty("own").GetRawText());
        Assert.Equal(("series", "\"past\""), (futur.GetProperty("from").GetString(), futur.GetProperty("value").GetRawText()));
        Assert.Equal("tense is one of past, present, future, not 'futur'. Did you mean 'future'?", futur.GetProperty("ignoredReason").GetString());
        Assert.Equal(JsonValueKind.Null, Field(1, "tense").GetProperty("ignoredReason").ValueKind);

        // The chapters' read lists it too, as written and why (#233).
        var read = await server.GetJsonAsync("/api/book/chapters?universe=starfall&book=ember", HttpStatusCode.OK);
        var ignored = Assert.Single(read.GetProperty("chapters")[4].GetProperty("ignored").EnumerateArray());
        Assert.Equal(("tense", "\"futur\""), (ignored.GetProperty("field").GetString(), ignored.GetProperty("asWritten").GetRawText()));
        Assert.Equal("tense is one of past, present, future, not 'futur'. Did you mean 'future'?", ignored.GetProperty("reason").GetString());

        // Ash writes its chapters as bare keys.
        var ash = await server.GetJsonAsync("/api/book/chapter-fields?universe=starfall&book=ash", HttpStatusCode.OK);
        Assert.All(ash.GetProperty("chapters").EnumerateArray(), c => Assert.True(c.GetProperty("bare").GetBoolean()));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-74")]
    public async Task AChaptersFieldIsSetThenEmptiedThenClearedBackToTheDefault()
    {
        await using var server = await Server.StartAsync(new WolfdenApiOptions(_den.Root));
        var hash = await HashAsync(server);

        // Chapter 2 gets its own POV: Halden Pike as well as Mara Voss.
        var set = await EditAsync(server, new { book = "ember", universe = "starfall", chapter = "2", field = "pov", expectedHash = hash, changes = new object[] { new { op = "set", value = new[] { "[[mara-voss]]", "[[halden-pike]]" } } } });
        Assert.True(set.GetProperty("changed").GetBoolean());
        Assert.Contains("  - { key: \"2\", number: 2, fields: { pov: [\"[[mara-voss]]\", \"[[halden-pike]]\"] } }\n", _den.Read(Ember), StringComparison.Ordinal);
        Assert.Contains("its own value", set.GetProperty("effects")[0].GetString(), StringComparison.Ordinal);

        // Removing both items leaves [], "none here"; it doesn't go back to the default.
        var one = await EditAsync(server, new { book = "ember", universe = "starfall", chapter = "2", field = "pov", expectedHash = set.GetProperty("hash").GetString(), changes = new object[] { new { op = "remove", item = 1 } } });
        var none = await EditAsync(server, new { book = "ember", universe = "starfall", chapter = "2", field = "pov", expectedHash = one.GetProperty("hash").GetString(), changes = new object[] { new { op = "remove", item = 0 } } });
        Assert.Contains("  - { key: \"2\", number: 2, fields: { pov: [] } }\n", _den.Read(Ember), StringComparison.Ordinal);
        Assert.Contains("to go back to that default, clear the field", none.GetProperty("effects")[0].GetString(), StringComparison.Ordinal);

        // Clear goes back to the default.
        var cleared = await EditAsync(server, new { book = "ember", universe = "starfall", chapter = "2", field = "pov", expectedHash = none.GetProperty("hash").GetString(), changes = new object[] { new { op = "clear" } } });
        Assert.Contains("now inherits pov: [[mara-voss]] from book 'ember' chapter_defaults", cleared.GetProperty("effects")[0].GetString(), StringComparison.Ordinal);
        var book = await server.GetJsonAsync("/api/book/chapter-fields?universe=starfall&book=ember", HttpStatusCode.OK);
        var pov = book.GetProperty("chapters")[2].GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "pov");
        Assert.Equal(("book", false), (pov.GetProperty("from").GetString(), pov.GetProperty("set").GetBoolean()));

        // A date with a sort key, sent as the form sends numbers.
        var when = await EditAsync(server, new
        {
            book = "ember", universe = "starfall", chapter = "1", field = "when", expectedHash = cleared.GetProperty("hash").GetString(),
            changes = new object[] { new { op = "set", value = new Dictionary<string, object> { ["text"] = "Year 2290", ["sort"] = new Dictionary<string, string> { ["$number"] = "2290" } } } },
        });
        Assert.True(when.GetProperty("changed").GetBoolean());
        Assert.Contains("title: Kessan Reach, fields: { when: { text: Year 2290, sort: 2290 } } }", _den.Read(Ember), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-76")]
    public async Task AnItemsCommentThatGoesWithItIsReported()
    {
        _den.Write(Ember, _den.Read(Ember).Replace(
            "  - { key: \"2\", number: 2 }\n",
            "  - key: \"2\"\n    number: 2\n    fields:\n      pov:\n        - \"[[mara-voss]]\"  # her watch\n        - \"[[halden-pike]]\"\n",
            StringComparison.Ordinal));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(_den.Root));

        // Moved: taken out and added again after Pike.
        var moved = await EditAsync(server, new
        {
            book = "ember", universe = "starfall", chapter = "2", field = "pov", expectedHash = await HashAsync(server),
            changes = new object[] { new { op = "remove", item = 0 }, new { op = "add", value = "[[mara-voss]]" } },
        });
        Assert.Contains("Moved an item of pov of chapter '2' to item 1, written afresh, so the comment on it was dropped: '# her watch'. Put it back by hand if you want it.", moved.GetProperty("effects").EnumerateArray().Select(e => e.GetString()));

        // Rewritten: the web UI changes an item in place as a remove and an add at the same place.
        Assert.Contains("        - \"[[mara-voss]]\"\n", _den.Read(Ember), StringComparison.Ordinal);
        _den.Write(Ember, _den.Read(Ember).Replace("        - \"[[mara-voss]]\"\n", "        - \"[[mara-voss]]\"  # main\n", StringComparison.Ordinal));
        var rewritten = await EditAsync(server, new
        {
            book = "ember", universe = "starfall", chapter = "2", field = "pov", expectedHash = await HashAsync(server),
            changes = new object[] { new { op = "remove", item = 1 }, new { op = "add", value = "[[tomas-reyes]]" } },
        });
        var rewriteEffects = rewritten.GetProperty("effects").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Rewrote item 1 of pov of chapter '2'; the comment on it was dropped: '# main'. Put it back by hand if you want it.", rewriteEffects);
        Assert.DoesNotContain(rewriteEffects, e => e!.StartsWith("Removed item", StringComparison.Ordinal));

        // Removed: the comment goes with it, and the effects say so.
        _den.Write(Ember, _den.Read(Ember).Replace("        - \"[[halden-pike]]\"\n", "        - \"[[halden-pike]]\"  # relief pilot\n", StringComparison.Ordinal));
        var removed = await EditAsync(server, new
        {
            book = "ember", universe = "starfall", chapter = "2", field = "pov", expectedHash = await HashAsync(server),
            changes = new object[] { new { op = "remove", item = 0 } },
        });
        Assert.Contains("Removed item 0 of pov of chapter '2' with the comment on it: '# relief pilot'.", removed.GetProperty("effects").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-76")]
    public async Task ChapterFieldEditsAreGuarded()
    {
        await using var server = await Server.StartAsync(new WolfdenApiOptions(_den.Root));
        var hash = await HashAsync(server);
        var before = _den.Read(Ember);

        async Task Refused(string path, object body, HttpStatusCode status, string code, string? text = null)
        {
            var (got, json) = await PostAsync(server, path, body);
            Assert.Equal(status, got);
            Assert.Equal(code, json.GetProperty("code").GetString());
            if (text is not null)
            {
                Assert.Contains(text, json.GetProperty("message").GetString(), StringComparison.Ordinal);
            }

            Assert.Equal(before, _den.Read(Ember));
        }

        object Tense(string chapter = "1", string? expected = null) => new { book = "ember", universe = "starfall", chapter, field = "tense", expectedHash = expected ?? hash, changes = new object[] { new { op = "set", value = "present" } } };
        await Refused("/api/chapter/edit", Tense(expected: "0000"), HttpStatusCode.Conflict, "stale_file");
        await Refused("/api/chapter/edit", Tense(chapter: "9"), HttpStatusCode.BadRequest, "unknown_chapter");
        await Refused("/api/chapter/edit", new { book = "ember", universe = "starfall", chapter = "1", field = "key", expectedHash = hash, changes = new object[] { new { op = "set", value = "1b" } } }, HttpStatusCode.BadRequest, "bad_argument", "reserved");
        await Refused("/api/chapter/edit", new { book = "ember", universe = "starfall", chapter = "1", field = "tense", expectedHash = hash, changes = new object[] { new { op = "set", field = "pov", value = "x" } } }, HttpStatusCode.BadRequest, "bad_argument", "doesn't take 'field'");
        await Refused("/api/chapter/edit", new { book = "ember", universe = "starfall", chapter = "1", expectedHash = hash, changes = new object[] { new { op = "set", value = "x" } } }, HttpStatusCode.BadRequest, "bad_argument", "'field'");
        await Refused("/api/chapter/create", new { book = "ember", universe = "starfall", expectedHash = hash, fields = new { number = 3 } }, HttpStatusCode.BadRequest, "bad_argument", "reserved");
        await Refused("/api/chapter/create", new { book = "ember", universe = "starfall", expectedHash = hash, fields = new[] { "pov" } }, HttpStatusCode.BadRequest, "bad_argument", "'fields' is an object");

        // A bare-key chapter has no place for fields.
        var ash = (await server.GetJsonAsync("/api/book/chapter-fields?universe=starfall&book=ash", HttpStatusCode.OK)).GetProperty("hash").GetString();
        var (bare, bareJson) = await PostAsync(server, "/api/chapter/edit", new { book = "ash", universe = "starfall", chapter = "1", field = "tense", expectedHash = ash, changes = new object[] { new { op = "set", value = "past" } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bare);
        Assert.Equal("unsupported_edit", bareJson.GetProperty("code").GetString());
        Assert.Contains("chapter ash:1 is written as a bare key", bareJson.GetProperty("message").GetString(), StringComparison.Ordinal);

        // An add to a value that isn't a list names the chapter and says what to do on the page, never a YAML path (#233).
        await Refused(
            "/api/chapter/edit",
            new { book = "ember", universe = "starfall", chapter = "3", field = "tense", expectedHash = hash, changes = new object[] { new { op = "add", value = "past" } } },
            HttpStatusCode.BadRequest,
            "bad_argument",
            $"'tense' in chapter ember:3 ({Ember}) is written as one value, 'futur', not a list, so 'add' has no list to add an item to. That value is ignored: " +
            "tense is one of past, present, future, not 'futur'. Did you mean 'future'? To replace it, edit the field and save the whole value; to go back to what the chapter " +
            "inherits from chapter_defaults, choose Use the default.");
        var seriesHash = (await server.GetJsonAsync("/api/chapter-defaults?universe=starfall&series=long-dark", HttpStatusCode.OK)).GetProperty("hash").GetString();
        var (notList, notListJson) = await PostAsync(server, "/api/chapter-defaults/edit", new { universe = "starfall", series = "long-dark", field = "tense", expectedHash = seriesHash, changes = new object[] { new { op = "add", value = "past" } } });
        Assert.Equal(HttpStatusCode.BadRequest, notList);
        Assert.EndsWith(
            "'tense' in the chapter_defaults of series 'long-dark' (" + Series + ") is written as one value, 'past', not a list, so 'add' has no list to add an item to. " +
            "To replace it, edit the field and save the whole value; to go back to what its books inherit from above, choose Clear.",
            notListJson.GetProperty("message").GetString(),
            StringComparison.Ordinal);

        using var get = await server.Client.GetAsync("/api/chapter/edit", Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        using var post = await server.Client.PostAsync("/api/chapter-defaults", new StringContent("{}", Encoding.UTF8, "application/json"), Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);

        // A final book: its chapters and defaults are refused, saying how to unfreeze it.
        _den.Write(Ember, before.Replace("status: draft", "status: final", StringComparison.Ordinal));
        before = _den.Read(Ember);
        var final = await HashAsync(server);
        const string Unfreeze = "Unfreeze… on the page of book 'ember' opens it for editing again.";
        await Refused("/api/chapter/edit", Tense(expected: final), HttpStatusCode.Conflict, "frozen", Unfreeze);
        await Refused("/api/chapter-defaults/edit", new { universe = "starfall", book = "ember", field = "tense", expectedHash = final, changes = new object[] { new { op = "set", value = "present" } } }, HttpStatusCode.Conflict, "frozen", Unfreeze);

        // The series' defaults can't change what the final book's chapters have.
        var series = await server.GetJsonAsync("/api/chapter-defaults?universe=starfall&series=long-dark", HttpStatusCode.OK);
        var seriesBefore = _den.Read(Series);
        var (frozen, frozenJson) = await PostAsync(server, "/api/chapter-defaults/edit", new { universe = "starfall", series = "long-dark", field = "tense", expectedHash = series.GetProperty("hash").GetString(), changes = new object[] { new { op = "set", value = "present" } } });
        Assert.Equal(HttpStatusCode.Conflict, frozen);
        Assert.Equal("frozen", frozenJson.GetProperty("code").GetString());
        Assert.Contains(Unfreeze, frozenJson.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(seriesBefore, _den.Read(Series));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-75")]
    public async Task AContainersDefaultsAreReadAndEdited()
    {
        await using var server = await Server.StartAsync(new WolfdenApiOptions(_den.Root));
        var series = await server.GetJsonAsync("/api/chapter-defaults?universe=starfall&series=long-dark", HttpStatusCode.OK);
        Assert.Equal(("series", "long-dark", Series), (series.GetProperty("kind").GetString(), series.GetProperty("key").GetString(), series.GetProperty("path").GetString()));
        Assert.Equal(["pov", "narration", "tense", "when", "setting"], series.GetProperty("fieldTypes").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(["ember", "ash"], series.GetProperty("books").EnumerateArray().Select(b => b.GetProperty("key").GetString()));
        var tense = series.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "tense");
        Assert.Equal((true, "\"past\"", "series"), (tense.GetProperty("set").GetBoolean(), tense.GetProperty("own").GetRawText(), tense.GetProperty("from").GetString()));
        Assert.Equal("chapter_defaults: { tense: past }", tense.GetProperty("text").GetString());

        // The universe sets narration; the series and its books inherit it.
        var universe = await server.GetJsonAsync("/api/chapter-defaults?universe=starfall", HttpStatusCode.OK);
        Assert.Equal("universe", universe.GetProperty("kind").GetString());
        var (code, edited) = await PostAsync(server, "/api/chapter-defaults/edit", new { universe = "starfall", field = "narration", expectedHash = universe.GetProperty("hash").GetString(), changes = new object[] { new { op = "set", value = "third-limited" } } });
        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Contains("chapter_defaults:", _den.Read("universes/starfall/universe.md"), StringComparison.Ordinal);
        Assert.Contains("Book 'ember': chapters prologue–3 now have narration third-limited.", edited.GetProperty("effects").EnumerateArray().Select(e => e.GetString()));
        var again = await server.GetJsonAsync("/api/chapter-defaults?universe=starfall&series=long-dark", HttpStatusCode.OK);
        var narration = again.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "narration");
        Assert.Equal((false, "universe", "\"third-limited\""), (narration.GetProperty("set").GetBoolean(), narration.GetProperty("from").GetString(), narration.GetProperty("value").GetRawText()));
        Assert.Equal("universe", narration.GetProperty("defaultFrom").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-75")]
    public async Task ANewChapterCanStartWithItsOwnFields()
    {
        await using var server = await Server.StartAsync(new WolfdenApiOptions(_den.Root));
        var (code, made) = await PostAsync(server, "/api/chapter/create", new
        {
            book = "ember", universe = "starfall", title = "Flashback", after = "1", expectedHash = await HashAsync(server),
            fields = new Dictionary<string, object?> { ["pov"] = new[] { "[[halden-pike]]" }, ["when"] = "2270", ["tense"] = null },
        });
        Assert.Equal(HttpStatusCode.Created, code);
        Assert.Contains("  - { key: \"1b\", number: 2, title: Flashback, fields: { pov: [\"[[halden-pike]]\"], when: \"2270\" } }\n", _den.Read(Ember), StringComparison.Ordinal);
        var effects = made.GetProperty("effects").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains(effects, e => e.Contains("pov is now [[halden-pike]], its own value, in place of [[mara-voss]] from book 'ember' chapter_defaults", StringComparison.Ordinal));

        // In a list of bare keys, fields make the new chapter a mapping.
        var ash = (await server.GetJsonAsync("/api/book/chapters?universe=starfall&book=ash", HttpStatusCode.OK)).GetProperty("hash").GetString();
        var (ashCode, _) = await PostAsync(server, "/api/chapter/create", new { book = "ash", universe = "starfall", expectedHash = ash, fields = new { tense = "present" } });
        Assert.Equal(HttpStatusCode.Created, ashCode);
        Assert.Contains("chapters: [1, 2, 3, 4, epilogue, { key: \"5\", fields: { tense: present } }]", _den.Read(Ash), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ helpers

    private static async Task<string> HashAsync(Server server) =>
        (await server.GetJsonAsync("/api/book/chapter-fields?universe=starfall&book=ember", HttpStatusCode.OK)).GetProperty("hash").GetString()!;

    private static async Task<JsonElement> EditAsync(Server server, object body)
    {
        var (code, json) = await PostAsync(server, "/api/chapter/edit", body);
        Assert.True(code == HttpStatusCode.OK, json.ToString());
        return json;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(Server server, string path, object body)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await server.Client.PostAsync(path, content, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }
}

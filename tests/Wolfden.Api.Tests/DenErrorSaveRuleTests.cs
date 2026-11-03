using System.Net;
using System.Text;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// The save rules of Edit file by path (#280, review of #295): a save can't
/// change what a final book inherits, a save that brings a file back is an
/// ordinary write, and status and id count however they're written.
/// </summary>
public sealed class DenErrorSaveRuleTests
{
    private const string Series = "universes/starfall/series/long-dark/series.md";
    private const string Universe = "universes/starfall/universe.md";
    private const string Ember = "universes/starfall/series/long-dark/books/ember/book.md";
    private const string Note = "universes/starfall/entities/notes/ember-vault.md";
    private const string Other = "universes/starfall/entities/notes/ash-vault.md";
    private const string DraftBook = "universes/dracula/books/dracula/book.md";

    private const string Healthy = "---\ntype: note\nname: Ember Vault\nstatus: canon\n---\n\nA vault.\n";
    private const string LeftOut = "---\ntype: note\nname: Ember Vault\nstatus: maybe\n---\n\nA vault.\n";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(HttpStatusCode Status, JsonElement Json)> Save(Server server, string path, string text)
    {
        var hash = (await server.GetJsonAsync($"/api/file/source?path={Uri.EscapeDataString(path)}", HttpStatusCode.OK)).GetProperty("hash").GetString();
        using var body = new StringContent(JsonSerializer.Serialize(new { path, text, expectedHash = hash }), Encoding.UTF8, "application/json");
        using var response = await server.Client.PostAsync("/api/file/edit", body, Token);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone());
    }

    /// <summary>The file with <c>chapter_defaults</c> added at the end of its frontmatter.</summary>
    private static string WithDefaults(string text, string narration)
    {
        var lf = text.ReplaceLineEndings("\n");
        var end = lf.IndexOf("\n---\n", 3, StringComparison.Ordinal);
        return $"{lf[..end]}\nchapter_defaults:\n  narration: {narration}{lf[end..]}";
    }

    /// <summary>Marks a book final and takes its name, so it's left out and says it's final.</summary>
    private static string FinalAndLeftOut(string book)
    {
        var lines = book.ReplaceLineEndings("\n").Split('\n').Where(l => !l.StartsWith("name:", StringComparison.Ordinal) && !l.StartsWith("status:", StringComparison.Ordinal)).ToList();
        lines.Insert(1, "status: final");
        return string.Join('\n', lines);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-91")]
    [InlineData(Series)]
    [InlineData(Universe)]
    public async Task ADefaultAFinalBookWouldInheritCantChange(string path)
    {
        using var den = TempDen.Starfall();
        den.Write(path, WithDefaults(den.Read(path), "first"));
        den.Write(Ember, FinalAndLeftOut(den.Read(Ember)));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var before = den.Read(path);

        var (changed, json) = await Save(server, path, before.Replace("narration: first", "narration: third-limited", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Conflict, changed);
        Assert.Equal("frozen", json.GetProperty("code").GetString());
        Assert.Contains(Ember, json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, den.Read(path));

        // The same values written another way, and the text below, can be saved.
        var (same, _) = await Save(server, path, before + "\nMore text.\n");
        Assert.Equal(HttpStatusCode.OK, same);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task ASeriesLeftOutCanBeFixedAboveAFinalBookButNotChangeItsDefaults()
    {
        using var den = TempDen.Starfall();
        var good = WithDefaults(den.Read(Series), "first");
        var ember = den.Read(Ember).ReplaceLineEndings("\n").Split('\n').Where(l => !l.StartsWith("status:", StringComparison.Ordinal)).ToList();
        ember.Insert(1, "status: final");
        den.Write(Ember, string.Join('\n', ember));
        var noName = string.Join('\n', good.Split('\n').Where(l => !l.StartsWith("name:", StringComparison.Ordinal)));
        den.Write(Series, noName);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (changed, _) = await Save(server, Series, good.Replace("narration: first", "narration: third-limited", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Conflict, changed);
        Assert.Equal(noName, den.Read(Series));

        var (fixedUp, saved) = await Save(server, Series, good);
        Assert.Equal(HttpStatusCode.OK, fixedUp);
        Assert.Contains("back in the den", saved.GetProperty("effects")[0].GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-91")]
    [InlineData(Series)]
    [InlineData(Universe)]
    public async Task FrontmatterThatDoesntParseAboveAFinalBookGoesToTheWritersEditor(string path)
    {
        using var den = TempDen.Starfall();
        var good = den.Read(path).ReplaceLineEndings("\n");
        den.Write(Ember, FinalAndLeftOut(den.Read(Ember)));
        den.Write(path, "---\nbroken: [\n" + good[4..]);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var source = await server.GetJsonAsync($"/api/file/source?path={Uri.EscapeDataString(path)}", HttpStatusCode.OK);
        Assert.Contains("your own editor", source.GetProperty("frozen").GetString(), StringComparison.Ordinal);

        var (status, json) = await Save(server, path, good);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("your own editor", json.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task ADefaultAFinalBookHeldOpenWouldInheritCantChange()
    {
        using var den = TempDen.Starfall();
        den.Write(Series, WithDefaults(den.Read(Series), "first"));
        den.Write(Ember, FinalAndLeftOut(den.Read(Ember)));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var before = den.Read(Series);
        var hash = (await server.GetJsonAsync($"/api/file/source?path={Uri.EscapeDataString(Series)}", HttpStatusCode.OK)).GetProperty("hash").GetString();
        var held = Path.Combine(den.Root, Ember.Replace('/', Path.DirectorySeparatorChar));
        using var hold = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            File.ReadAllBytes(held);
            Assert.Skip("This file system doesn't stop reads of a file held open.");
        }
        catch (IOException)
        {
            // Held.
        }

        using var body = new StringContent(
            JsonSerializer.Serialize(new { path = Series, text = before.Replace("narration: first", "narration: third-limited", StringComparison.Ordinal), expectedHash = hash }),
            Encoding.UTF8,
            "application/json");
        using var response = await server.Client.PostAsync("/api/file/edit", body, Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task ASaveThatBringsANoteBackIsAnOrdinaryWrite()
    {
        using var den = TempDen.Starfall();
        den.Write(Note, LeftOut);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        // A position that doesn't resolve.
        var (position, json) = await Save(server, Note, Healthy.Replace("status: canon", "status: canon\nfrom: \"nosuchbook:9\"", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, position);
        Assert.Equal("bad_position", json.GetProperty("code").GetString());
        Assert.Equal(LeftOut, den.Read(Note));

        // A new typo in a section comment.
        var (section, _) = await Save(server, Note, Healthy + "\n## Plan\n<!-- wolfden: status=maybe -->\nA plan.\n");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, section);
        Assert.Equal(LeftOut, den.Read(Note));

        // While it stays left out, the exception still holds: the position can wait, or be fixed first.
        var (still, _) = await Save(server, Note, LeftOut.Replace("status: maybe", "status: maybe\nfrom: \"nosuchbook:9\"", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, still);

        var (back, saved) = await Save(server, Note, Healthy);
        Assert.Equal(HttpStatusCode.OK, back);
        Assert.Equal("Ember Vault", saved.GetProperty("entity").GetProperty("name").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task ATypoAlreadyInASectionCanStayWhenTheNoteComesBack()
    {
        using var den = TempDen.Starfall();
        const string Typo = "\n## Plan\n<!-- wolfden: status=maybe -->\nA plan.\n";
        den.Write(Note, LeftOut + Typo);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, saved) = await Save(server, Note, Healthy + Typo);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("error", saved.GetProperty("warnings")[0].GetProperty("severity").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task AUniverseLeftOutComesBack()
    {
        using var den = TempDen.Starfall();
        var good = den.Read(Universe).ReplaceLineEndings("\n");
        den.Write(Universe, string.Join('\n', good.Split('\n').Where(l => !l.StartsWith("name:", StringComparison.Ordinal))));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, saved) = await Save(server, Universe, good);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("back in the den", saved.GetProperty("effects")[0].GetString(), StringComparison.Ordinal);
    }

    public static TheoryData<string> FinalSpellings =>
    [
        "\"status\": final",
        "'status': final",
        "status:\n  final",
        "? status\n: final",
        "status: >-\n  final",
        "status: \"fin\\x61l\"",
        "status: Final",
    ];

    [Theory]
    [Trait("Scenario", "SC-WEB-91")]
    [MemberData(nameof(FinalSpellings))]
    public async Task ADraftBookCantBeMadeFinalHoweverItsSpelled(string spelling)
    {
        using var den = TempDen.Dracula();
        var good = den.Read(DraftBook).ReplaceLineEndings("\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, _) = await Save(server, DraftBook, good.Replace("status: draft", spelling, StringComparison.Ordinal));

        Assert.NotEqual(HttpStatusCode.OK, status);
        Assert.Equal(good, den.Read(DraftBook).ReplaceLineEndings("\n"));
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-91")]
    [MemberData(nameof(FinalSpellings))]
    public async Task ABookLeftOutThatSaysFinalCantBeUnfrozenHoweverItsSpelled(string spelling)
    {
        using var den = TempDen.Dracula();
        var good = den.Read(DraftBook).ReplaceLineEndings("\n");

        // Left out by having no name.
        var broken = string.Join('\n', good.Replace("status: draft", spelling, StringComparison.Ordinal).Split('\n').Where(l => !l.StartsWith("name:", StringComparison.Ordinal)));
        den.Write(DraftBook, broken);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await Save(server, DraftBook, good);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("your own editor", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(broken, den.Read(DraftBook));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task ABookWhoseFrontmatterDoesntParseFailsClosed()
    {
        using var den = TempDen.Dracula();
        var good = den.Read(DraftBook).ReplaceLineEndings("\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        // Saving it that way: the next save couldn't tell.
        den.Write(DraftBook, good.Replace("status: draft", "status: nope", StringComparison.Ordinal));
        var (after, _) = await Save(server, DraftBook, "---\nbroken: [\n" + good[4..]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, after);

        // Already that way: it might be final. A repeated key doesn't parse either.
        foreach (var text in new[] { "---\nbroken: [\n" + good[4..], good.Replace("status: draft", "status: draft\nstatus: final", StringComparison.Ordinal) })
        {
            den.Write(DraftBook, text);
            var (before, json) = await Save(server, DraftBook, good);
            Assert.Equal(HttpStatusCode.Conflict, before);
            Assert.Contains("your own editor", json.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Equal(text, den.Read(DraftBook));
        }
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-91")]
    [InlineData("id: {0}")]
    [InlineData("\"id\": {0}")]
    [InlineData("'id': {0}")]
    [InlineData("id:\n  {0}")]
    public async Task AnotherFilesIdIsRefusedHoweverItsSpelled(string spelling)
    {
        const string Used = "01M4TF5F0EKGGWD5FNVBAEZ901";
        using var den = TempDen.Starfall();
        den.Write(Other, $"---\nid: {Used}\ntype: note\nname: Ash Vault\nstatus: canon\n---\n\nA vault.\n");
        den.Write(Note, LeftOut);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, _) = await Save(server, Note, Healthy.Replace("type: note", string.Format(System.Globalization.CultureInfo.InvariantCulture, spelling, Used) + "\ntype: note", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(LeftOut, den.Read(Note));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task AnIdInAnotherFileLeftOutIsRefusedToo()
    {
        const string Used = "01M4TF5F0EKGGWD5FNVBAEZ902";
        using var den = TempDen.Starfall();
        den.Write(Other, $"---\nid: {Used}\ntype: note\nname: Ash Vault\nstatus: maybe\n---\n\nA vault.\n");
        den.Write(Note, LeftOut);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, _) = await Save(server, Note, Healthy.Replace("type: note", $"id: {Used}\ntype: note", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-91")]
    [InlineData("")]
    [InlineData("  \n\n")]
    public async Task EmptyTextIsRefused(string text)
    {
        using var den = TempDen.Starfall();
        den.Write(Note, LeftOut);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await Save(server, Note, text);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("empty", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(LeftOut, den.Read(Note));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-93")]
    public async Task ADenYamlThatsGoneHasNoEditFileAndTheHomePagePointsAtTheList()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        await server.GetJsonAsync("/api/den/errors", HttpStatusCode.OK);

        File.Delete(Path.Combine(den.Root, "den.yaml"));

        var error = Assert.Single((await server.GetJsonAsync("/api/den/errors", HttpStatusCode.OK)).GetProperty("errors").EnumerateArray());
        Assert.Equal("den.yaml", error.GetProperty("path").GetString());
        Assert.False(error.GetProperty("canEdit").GetBoolean());
        var home = await server.GetJsonAsync("/api/den", HttpStatusCode.ServiceUnavailable);
        Assert.Contains("top bar", home.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}

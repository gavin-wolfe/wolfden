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
    public async Task ASaveCantLeaveABookNotParsing()
    {
        using var den = TempDen.Dracula();
        var good = den.Read(DraftBook).ReplaceLineEndings("\n");
        den.Write(DraftBook, good.Replace("status: draft", "status: nope", StringComparison.Ordinal));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, _) = await Save(server, DraftBook, "---\nbroken: [\n" + good[4..]);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-91")]
    [InlineData("broken: [final")]
    [InlineData("broken: [FINAL")]
    [InlineData("status: draft\nstatus: final")]
    [InlineData("status: \"fin\\x61l\"\nbroken: [")]
    public async Task ABookThatDoesntParseAndMightBeFinalGoesToTheWritersEditor(string line)
    {
        using var den = TempDen.Dracula();
        var good = den.Read(DraftBook).ReplaceLineEndings("\n");
        var text = good.Replace("status: draft", line, StringComparison.Ordinal);
        den.Write(DraftBook, text);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await Save(server, DraftBook, good);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("your own editor", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(text, den.Read(DraftBook));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task ABookThatDoesntParseAndCantBeFinalCanBeFixedButNotMadeFinal()
    {
        using var den = TempDen.Dracula();
        var good = den.Read(DraftBook).ReplaceLineEndings("\n");
        den.Write(DraftBook, good.Replace("name: Dracula", "name: Two: The Return", StringComparison.Ordinal));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var source = await server.GetJsonAsync($"/api/file/source?path={Uri.EscapeDataString(DraftBook)}", HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, source.GetProperty("frozen").ValueKind);

        var (final, _) = await Save(server, DraftBook, good.Replace("status: draft", "status: final", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, final);

        var (fixedUp, saved) = await Save(server, DraftBook, good);
        Assert.Equal(HttpStatusCode.OK, fixedUp);
        Assert.Contains("back in the den", saved.GetProperty("effects")[0].GetString(), StringComparison.Ordinal);
    }

    private static string Without(string text, string start) =>
        string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').Where(l => !l.StartsWith(start, StringComparison.Ordinal)));

    private static async Task<List<string>> MetAt(Server server, string position) =>
        (await server.GetJsonAsync($"/api/canon?universe=starfall&brief=true&asOf={position}", HttpStatusCode.OK)).GetProperty("entities").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToList();

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task AFixAboveAFinalBookCantChangeWhatAReaderHasMetAtItsPositions()
    {
        using var den = TempDen.Starfall();
        den.Write(Ember, den.Read(Ember).Replace("status: draft", "status: final", StringComparison.Ordinal));
        var good = den.Read(Series).ReplaceLineEndings("\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var before = await MetAt(server, "ember:1");
        Assert.DoesNotContain("Halden Pike", before);

        // The series is left out, and the fix would put the other book first: the reader would meet its people here.
        var broken = Without(good, "name:");
        den.Write(Series, broken);
        var (reordered, json) = await Save(server, Series, good.Replace("books: [ember, ash]", "books: [ash, ember]", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Conflict, reordered);
        Assert.Equal("frozen", json.GetProperty("code").GetString());
        Assert.Equal(broken, den.Read(Series));

        var (fixedUp, _) = await Save(server, Series, good);
        Assert.Equal(HttpStatusCode.OK, fixedUp);
        Assert.Equal(before, await MetAt(server, "ember:1"));
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-91")]
    [InlineData(Series, "books: [ember, ash]", "books: [ash, ember]")]
    [InlineData(Universe, "order: [long-dark, a-quiet-moon, drift-war]", "order: [a-quiet-moon, long-dark, drift-war]")]
    [InlineData(Series, "books: [ember, ash]", "books: [ember, ash]\nchapter_heading: [narration]")]
    [InlineData(Universe, "order: [long-dark, a-quiet-moon, drift-war]", "order: [long-dark, a-quiet-moon, drift-war]\nchapter_heading: [narration]")]
    public async Task ALoadedSeriesOrUniverseCantChangeWhatABookLeftOutThatSaysFinalDependsOn(string path, string from, string to)
    {
        using var den = TempDen.Starfall();
        den.Write(Ember, FinalAndLeftOut(den.Read(Ember)));
        var good = den.Read(path).ReplaceLineEndings("\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await Save(server, path, good.Replace(from, to, StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("frozen", json.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task FixingARepeatedKeyAboveAFinalBookIsNoChangeButReorderingIs()
    {
        using var den = TempDen.Starfall();
        den.Write(Ember, FinalAndLeftOut(den.Read(Ember)));
        var good = den.Read(Universe).ReplaceLineEndings("\n");
        var repeated = good.Replace("order: [long-dark, a-quiet-moon, drift-war]", "order: [long-dark, long-dark, a-quiet-moon, drift-war]", StringComparison.Ordinal);
        den.Write(Universe, repeated);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (reordered, _) = await Save(server, Universe, good.Replace("order: [long-dark, a-quiet-moon, drift-war]", "order: [a-quiet-moon, long-dark, drift-war]", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Conflict, reordered);

        var (fixedUp, _) = await Save(server, Universe, good);
        Assert.Equal(HttpStatusCode.OK, fixedUp);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-91")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOrderThatIsntAListAboveAFinalBookGoesToTheWritersEditor(bool isNow)
    {
        using var den = TempDen.Starfall();
        den.Write(Ember, FinalAndLeftOut(den.Read(Ember)));
        var good = den.Read(Series).ReplaceLineEndings("\n");
        var scalar = good.Replace("books: [ember, ash]", "books: ember", StringComparison.Ordinal);
        den.Write(Series, isNow ? scalar : good);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await Save(server, Series, isNow ? good : scalar);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("your own editor", json.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task ASeriesAboveAFinalBookCantLockItself()
    {
        using var den = TempDen.Starfall();
        den.Write(Ember, FinalAndLeftOut(den.Read(Ember)));
        var good = den.Read(Series).ReplaceLineEndings("\n");
        var broken = Without(good, "name:");
        den.Write(Series, broken);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        // One error for one, but it wouldn't parse, so the next save couldn't compare it.
        var (locked, _) = await Save(server, Series, broken.Replace("books:", "name: A\nname: B\nbooks:", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, locked);
        Assert.Equal(broken, den.Read(Series));

        var (fixedUp, _) = await Save(server, Series, good);
        Assert.Equal(HttpStatusCode.OK, fixedUp);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task AnIdUnderABookLeftOutIsRefusedToo()
    {
        const string Used = "01M4TF5F0EKGGWD5FNVBAEZ903";
        using var den = TempDen.Starfall();
        den.Write("universes/starfall/series/long-dark/books/ash/entities/notes/hidden.md", $"---\nid: {Used}\ntype: note\nname: Hidden\nstatus: canon\n---\n\nText.\n");
        const string Ash = "universes/starfall/series/long-dark/books/ash/book.md";
        den.Write(Ash, Without(den.Read(Ash), "name:"));
        den.Write(Note, LeftOut);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, _) = await Save(server, Note, Healthy.Replace("type: note", $"id: {Used}\ntype: note", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-91")]
    [InlineData("id : 01M4TF5F0EKGGWD5FNVBAEZ904")]
    [InlineData("  id: 01M4TF5F0EKGGWD5FNVBAEZ904")]
    public async Task AnIdWrittenOddlyInBrokenFrontmatterStillCantChange(string line)
    {
        using var den = TempDen.Starfall();
        den.Write(Note, $"---\n{line}\ntype: note\nname: [Ember Vault\nstatus: canon\n---\n\nA vault.\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await Save(server, Note, "---\nid: 01M4TF5F0EKGGWD5FNVBAEZ905\ntype: note\nname: Ember Vault\nstatus: canon\n---\n\nA vault.\n");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("an id never changes", json.GetProperty("message").GetString(), StringComparison.Ordinal);
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
    [InlineData("\uFEFF")]
    [InlineData("\u200B")]
    [InlineData(" \uFEFF\n\u200B ")]
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

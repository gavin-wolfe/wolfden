using System.Net;
using System.Text;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// The den's errors on the web API (#280): the list behind the top bar's
/// indicator, a note left out by an error, and fixing a file by its path.
/// </summary>
public sealed class DenErrorEndpointTests
{
    private const string Swales = "universes/dracula/books/dracula/entities/characters/mr-swales.md";
    private const string Broken = "universes/dracula/entities/notes/ember-vault.md";
    private const string BookFile = "universes/dracula/books/dracula/book.md";
    private const string Id = "01M4TF5F0EKGGWD5FNVBAEZ280";

    private const string OneError = "---\nid: " + Id + "\ntype: note\nname: Ember Vault\nstatus: maybe\n---\n\n# Ember Vault\n\nA vault under the abbey.\n";
    private const string Healthy = "---\nid: " + Id + "\ntype: note\nname: Ember Vault\nstatus: canon\n---\n\n# Ember Vault\n\nA vault under the abbey.\n";
    private const string TwoErrors = "---\nid: " + Id + "\ntype: note\nstatus: maybe\n---\n\n# Ember Vault\n\nA vault under the abbey.\n";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Encoded(string text) => Uri.EscapeDataString(text);

    private static async Task<JsonElement> Errors(Server server) => await server.GetJsonAsync("/api/den/errors", HttpStatusCode.OK);

    private static async Task<(HttpStatusCode Status, JsonElement Json)> Save(Server server, string path, string text, string hash)
    {
        using var body = new StringContent(JsonSerializer.Serialize(new { path, text, expectedHash = hash }), Encoding.UTF8, "application/json");
        using var response = await server.Client.PostAsync("/api/file/edit", body, Token);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone());
    }

    private static async Task<string> HashOf(Server server, string path) =>
        (await server.GetJsonAsync($"/api/file/source?path={Encoded(path)}", HttpStatusCode.OK)).GetProperty("hash").GetString()!;

    [Fact]
    [Trait("Scenario", "SC-WEB-90")]
    public async Task AnOutsideChangeShowsInTheListAndClearsWhenFixed()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var clean = await Errors(server);
        Assert.Equal(0, clean.GetProperty("count").GetInt32());
        Assert.True(clean.GetProperty("denLoaded").GetBoolean());

        den.Write(Broken, OneError);

        var found = await Errors(server);
        Assert.Equal(1, found.GetProperty("count").GetInt32());
        var error = Assert.Single(found.GetProperty("errors").EnumerateArray());
        Assert.Equal(Broken, error.GetProperty("path").GetString());
        Assert.Equal("dracula", error.GetProperty("universe").GetString());
        Assert.Equal("note", error.GetProperty("kind").GetString());
        Assert.Equal("file", error.GetProperty("reach").GetString());
        Assert.Equal("F-ENT-5", error.GetProperty("rule").GetString());
        Assert.Equal(5, error.GetProperty("line").GetInt32());
        Assert.True(error.GetProperty("canEdit").GetBoolean());

        den.Write(Broken, Healthy);

        Assert.Equal(0, (await Errors(server)).GetProperty("count").GetInt32());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-90")]
    public async Task TheListSaysHowFarEachErrorReaches()
    {
        using var den = TempDen.Dracula();
        var goodBook = den.Read(BookFile);
        den.Write(Swales, den.Read(Swales) + "\n## A rumor\n<!-- wolfden: status=maybe -->\nWhispers.\n");
        den.Write(BookFile, goodBook.Replace("status: draft", "status: nope", StringComparison.Ordinal));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var errors = (await Errors(server)).GetProperty("errors").EnumerateArray().ToList();

        // The book's error leaves the book out, so its notes aren't read, and only it's listed.
        var book = errors.Single(e => e.GetProperty("path").GetString() == BookFile);
        Assert.Equal("container", book.GetProperty("reach").GetString());
        Assert.Equal("book", book.GetProperty("kind").GetString());

        den.Write(BookFile, goodBook);
        var rumor = Assert.Single((await Errors(server)).GetProperty("errors").EnumerateArray());
        Assert.Equal("section", rumor.GetProperty("reach").GetString());
        Assert.Equal("A rumor", rumor.GetProperty("section").GetString());
        Assert.Equal("Mr. Swales", rumor.GetProperty("name").GetString());
        Assert.True(rumor.GetProperty("sectionLine").GetInt32() > 0);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-90")]
    public async Task AnOutlineWithAnErrorIsListedAsCheckListsIt()
    {
        using var den = TempDen.Dracula();
        const string Outline = "universes/dracula/books/dracula/outline.md";
        den.Write(Outline, "---\nkey: [unclosed\n---\n\n# Outline\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var error = Assert.Single((await Errors(server)).GetProperty("errors").EnumerateArray());

        Assert.Equal(Outline, error.GetProperty("path").GetString());
        Assert.Equal("outline", error.GetProperty("kind").GetString());
        Assert.Equal("file", error.GetProperty("reach").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-93")]
    public async Task ADenYamlThatDoesntLoadIsListedAndCanBeFixed()
    {
        using var den = TempDen.Dracula();
        var good = den.Read("den.yaml");
        den.Write("den.yaml", "format: nope\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        await server.GetJsonAsync("/api/den", HttpStatusCode.ServiceUnavailable);

        var list = await Errors(server);

        Assert.False(list.GetProperty("denLoaded").GetBoolean());
        var error = Assert.Single(list.GetProperty("errors").EnumerateArray());
        Assert.Equal("den.yaml", error.GetProperty("path").GetString());
        Assert.Equal("den", error.GetProperty("reach").GetString());
        Assert.True(error.GetProperty("canEdit").GetBoolean());

        // A note waits for den.yaml.
        var note = await server.GetJsonAsync($"/api/file/source?path={Encoded(Swales)}", HttpStatusCode.UnprocessableEntity);
        Assert.Contains("Fix den.yaml first", note.GetProperty("message").GetString(), StringComparison.Ordinal);

        var (status, saved) = await Save(server, "den.yaml", good, await HashOf(server, "den.yaml"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("The den loads now, so errors its files had can show too.", saved.GetProperty("effects").EnumerateArray().Select(e => e.GetString()));
        Assert.True((await Errors(server)).GetProperty("denLoaded").GetBoolean());
        await server.GetJsonAsync("/api/den", HttpStatusCode.OK);

        // Now it loads, it doesn't open here.
        var again = await server.GetJsonAsync("/api/file/source?path=den.yaml", HttpStatusCode.UnprocessableEntity);
        Assert.Equal("unsupported_edit", again.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task AFileLeftOutOpensByPathAndTwoErrorsCanBeFixedOneAtATime()
    {
        using var den = TempDen.Dracula();
        den.Write(Broken, TwoErrors);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var source = await server.GetJsonAsync($"/api/file/source?path={Encoded(Broken)}", HttpStatusCode.OK);
        Assert.Equal(TwoErrors, source.GetProperty("text").GetString());
        Assert.Equal(2, source.GetProperty("errors").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, source.GetProperty("entity").ValueKind);

        // One of the two: saved, and it's still left out.
        var (status, first) = await Save(server, Broken, OneError, source.GetProperty("hash").GetString()!);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(first.GetProperty("changed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("entity").ValueKind);
        Assert.Contains("still has 1 error", first.GetProperty("effects")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal("error", first.GetProperty("warnings")[0].GetProperty("severity").GetString());
        Assert.Equal(OneError, den.Read(Broken));
        Assert.Equal(1, (await Errors(server)).GetProperty("count").GetInt32());

        // The other: it's back, and it has a page.
        var (status2, second) = await Save(server, Broken, Healthy, first.GetProperty("hash").GetString()!);
        Assert.Equal(HttpStatusCode.OK, status2);
        Assert.Equal("Ember Vault", second.GetProperty("entity").GetProperty("name").GetString());
        Assert.Contains("back in the den", second.GetProperty("effects")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal(0, (await Errors(server)).GetProperty("count").GetInt32());
        await server.GetJsonAsync($"/api/entity?entity={Encoded(Id)}", HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task ASaveThatAddsAnErrorOrIsStaleIsRefused()
    {
        using var den = TempDen.Dracula();
        den.Write(Broken, OneError);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = await HashOf(server, Broken);

        var (added, json) = await Save(server, Broken, TwoErrors, hash);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, added);
        Assert.Equal("invalid_edit", json.GetProperty("code").GetString());
        Assert.Equal(OneError, den.Read(Broken));

        den.Write(Broken, OneError + "\nAn outside change.\n");
        var (stale, conflict) = await Save(server, Broken, Healthy, hash);
        Assert.Equal(HttpStatusCode.Conflict, stale);
        Assert.Equal("stale_file", conflict.GetProperty("code").GetString());
        Assert.Contains("An outside change.", conflict.GetProperty("currentFile").GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("An outside change.", den.Read(Broken), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task FrontmatterThatDoesntParseCanBeFixedThoughThatRevealsMore()
    {
        using var den = TempDen.Dracula();
        den.Write(Broken, "---\nid: " + Id + "\ntype: note\nname: [Ember Vault\nstatus: canon\n---\n\nText.\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, saved) = await Save(server, Broken, TwoErrors, await HashOf(server, Broken));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("still has 2 errors", saved.GetProperty("effects")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task AnIdCantChangeAndCantCopyAnothersAndAMissingOneIsGiven()
    {
        using var den = TempDen.Dracula();
        den.Write(Broken, OneError);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = await HashOf(server, Broken);

        var (changed, json) = await Save(server, Broken, Healthy.Replace(Id, "01M4TF5F0EKGGWD5FNVBAEZ999", StringComparison.Ordinal), hash);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, changed);
        Assert.Contains("an id never changes", json.GetProperty("message").GetString(), StringComparison.Ordinal);

        const string NoId = "---\ntype: note\nname: Ember Vault\nstatus: maybe\n---\n\nText.\n";
        den.Write(Broken, NoId);
        hash = await HashOf(server, Broken);
        var (copied, _) = await Save(server, Broken, NoId.Replace("status: maybe", "status: canon\nid: 01M4TF5F0EKGGWD5FNVBAEY8X7", StringComparison.Ordinal), hash);
        Assert.Equal(HttpStatusCode.Conflict, copied);

        var (fixedUp, result) = await Save(server, Broken, NoId.Replace("status: maybe", "status: canon", StringComparison.Ordinal), hash);
        Assert.Equal(HttpStatusCode.OK, fixedUp);
        Assert.Equal(JsonValueKind.String, result.GetProperty("idAdded").ValueKind);
        Assert.Contains("\nid: ", den.Read(Broken), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task ANoteTheDenReadsKeepsEveryRuleOfEditFile()
    {
        using var den = TempDen.Dracula();
        den.Write(Swales, den.Read(Swales) + "\n## A rumor\n<!-- wolfden: status=maybe -->\nWhispers.\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var source = await server.GetJsonAsync($"/api/file/source?path={Encoded(Swales)}", HttpStatusCode.OK);
        var text = source.GetProperty("text").GetString()!;
        var hash = source.GetProperty("hash").GetString()!;
        Assert.Equal("section", source.GetProperty("errors")[0].GetProperty("reach").GetString());
        Assert.Equal("A rumor", source.GetProperty("errors")[0].GetProperty("section").GetString());

        // A frontmatter error is new, and a note that loads may not gain one (Rule 11 of #290).
        var (added, _) = await Save(server, Swales, text.Replace("status: canon", "status: nope", StringComparison.Ordinal), hash);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, added);

        var (fixedUp, saved) = await Save(server, Swales, text.Replace("status=maybe", "status=canon", StringComparison.Ordinal), hash);
        Assert.Equal(HttpStatusCode.OK, fixedUp);
        Assert.Equal("Mr. Swales", saved.GetProperty("entity").GetProperty("name").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task AContainerLeftOutCanBeFixedAndItsBookComesBack()
    {
        using var den = TempDen.Dracula();
        var good = den.Read(BookFile).ReplaceLineEndings("\n");
        den.Write(BookFile, good.Replace("status: draft", "status: nope", StringComparison.Ordinal));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        // A note under it waits for the book.
        var note = await server.GetJsonAsync($"/api/file/source?path={Encoded(Swales)}", HttpStatusCode.UnprocessableEntity);
        Assert.Contains("Fix that first", note.GetProperty("message").GetString(), StringComparison.Ordinal);

        var (status, saved) = await Save(server, BookFile, good, await HashOf(server, BookFile));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("back in the den", saved.GetProperty("effects")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal(0, (await Errors(server)).GetProperty("count").GetInt32());
        await server.GetJsonAsync("/api/entity?entity=Mr.%20Swales", HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-91")]
    public async Task AFinalBookStaysFrozenHere()
    {
        using var den = TempDen.Dracula();
        var plain = den.Read(BookFile).ReplaceLineEndings("\n");
        den.Write(Broken, OneError);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        // Marking it final takes Freeze.
        var (marked, json) = await Save(server, BookFile, plain.Replace("status: draft", "status: final", StringComparison.Ordinal), await HashOf(server, BookFile));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, marked);
        Assert.Contains("Freeze", json.GetProperty("message").GetString(), StringComparison.Ordinal);

        den.Write(BookFile, plain.Replace("status: draft", "status: final", StringComparison.Ordinal));
        const string InBook = "universes/dracula/books/dracula/entities/notes/ember-vault.md";
        den.Write(InBook, OneError.Replace(Id, "01M4TF5F0EKGGWD5FNVBAEZ281", StringComparison.Ordinal));

        var source = await server.GetJsonAsync($"/api/file/source?path={Encoded(InBook)}", HttpStatusCode.OK);
        Assert.Contains("final", source.GetProperty("frozen").GetString(), StringComparison.Ordinal);
        var (frozen, _) = await Save(server, InBook, Healthy.Replace(Id, "01M4TF5F0EKGGWD5FNVBAEZ281", StringComparison.Ordinal), source.GetProperty("hash").GetString()!);
        Assert.Equal(HttpStatusCode.Conflict, frozen);

        // A final book's file that's broken can't be unfrozen, so it's fixed outside Wolfden.
        var broken = plain.Replace("status: draft", "status: final\nbroken: [", StringComparison.Ordinal);
        den.Write(BookFile, broken);
        var (still, words) = await Save(server, BookFile, plain, await HashOf(server, BookFile));
        Assert.Equal(HttpStatusCode.Conflict, still);
        Assert.Contains("your own editor", words.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-92")]
    [InlineData("../outside.md")]
    [InlineData("..\\outside.md")]
    [InlineData("/etc/hosts")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData(".wolfden/index.db")]
    [InlineData(".git/config")]
    [InlineData("universes/dracula/.hidden/x.md")]
    [InlineData("universes/dracula/entities/notes/../../../../outside.md")]
    [InlineData("universes/dracula/entities/notes/no-such-note.md")]
    [InlineData("universes/dracula/image.png")]
    [InlineData("universes/dracula/readme.md")]
    [InlineData("UNIVERSES/dracula/universe.md")]
    [InlineData("universes/dracula/books/dracula/entities/characters/MR-SWALES.md")]
    [InlineData("")]
    public async Task APathThatIsntAnEditableDenFileIsRefusedTheSameWay(string path)
    {
        using var den = TempDen.Dracula();
        den.Write("universes/dracula/readme.md", "Not a note.\n");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(den.Root)!, "outside.md"), "secret");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var read = await server.GetJsonAsync($"/api/file/source?path={Encoded(path)}", HttpStatusCode.NotFound);
        var (status, write) = await Save(server, path, "x", new string('0', 64));

        Assert.Equal("unknown_file", read.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("unknown_file", write.GetProperty("code").GetString());
        Assert.Equal("secret", File.ReadAllText(Path.Combine(Path.GetDirectoryName(den.Root)!, "outside.md")));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-92")]
    public async Task AFileOrFolderLinkIsRefused()
    {
        using var den = TempDen.Dracula();
        var outside = Path.Combine(Path.GetTempPath(), "wolfden-api-tests", "outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "note.md"), OneError);
        var entities = Path.Combine(den.Root, "universes", "dracula", "entities");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(entities, "linked"), outside);
            File.CreateSymbolicLink(Path.Combine(entities, "linked-note.md"), Path.Combine(outside, "note.md"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This machine can't make symbolic links.");
        }

        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        await server.GetJsonAsync($"/api/file/source?path={Encoded("universes/dracula/entities/linked/note.md")}", HttpStatusCode.NotFound);
        await server.GetJsonAsync($"/api/file/source?path={Encoded("universes/dracula/entities/linked-note.md")}", HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-92")]
    public async Task APinnedServerListsAndOpensOnlyItsUniverseAndTheDenLevelOnes()
    {
        const string Here = "universes/drift/entities/notes/ember-vault.md";
        const string There = "universes/starfall/entities/notes/ember-vault.md";
        using var den = TempDen.Starfall();
        den.Write(Here, OneError);
        den.Write(There, OneError.Replace(Id, "01M4TF5F0EKGGWD5FNVBAEZ282", StringComparison.Ordinal));
        den.Write("packs/starfall/pack.yaml", "not: [valid\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root) { PinnedUniverse = "drift" });

        var paths = (await Errors(server)).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("path").GetString()!).ToList();

        Assert.Contains(Here, paths);
        Assert.Contains(paths, p => p.StartsWith("packs/", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.StartsWith("universes/starfall", StringComparison.Ordinal));

        await server.GetJsonAsync($"/api/file/source?path={Encoded(Here)}", HttpStatusCode.OK);
        var other = await server.GetJsonAsync($"/api/file/source?path={Encoded(There)}", HttpStatusCode.NotFound);
        var missing = await server.GetJsonAsync($"/api/file/source?path={Encoded("universes/starfall/entities/notes/nothing.md")}", HttpStatusCode.NotFound);
        await server.GetJsonAsync("/api/file/source?path=den.yaml", HttpStatusCode.NotFound);

        // The answer doesn't say whether it's there.
        Assert.Equal(
            other.GetProperty("message").GetString()!.Replace(There, "", StringComparison.Ordinal),
            missing.GetProperty("message").GetString()!.Replace("universes/starfall/entities/notes/nothing.md", "", StringComparison.Ordinal));

        var (status, _) = await Save(server, There, Healthy, new string('0', 64));
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-94")]
    [InlineData(Broken)]
    [InlineData(Id)]
    [InlineData("Ember Vault")]
    [InlineData("ember-vault")]
    public async Task ThePageOfANoteLeftOutSaysSoAndNamesTheLine(string name)
    {
        using var den = TempDen.Dracula();
        den.Write(Broken, OneError);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var error = await server.GetJsonAsync($"/api/entity?entity={Encoded(name)}", HttpStatusCode.NotFound);

        Assert.Equal("unknown_entity", error.GetProperty("code").GetString());
        Assert.Contains("left out of the den", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("line 5", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/api/", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        var leftOut = error.GetProperty("leftOut");
        Assert.Equal(Broken, leftOut.GetProperty("path").GetString());
        Assert.Equal(5, leftOut.GetProperty("line").GetInt32());
        Assert.Equal("F-ENT-5", leftOut.GetProperty("rule").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-94")]
    public async Task ThePageOfANoteInABookLeftOutNamesTheBooksLine()
    {
        using var den = TempDen.Dracula();
        den.Write(BookFile, den.Read(BookFile).Replace("status: draft", "status: nope", StringComparison.Ordinal));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var error = await server.GetJsonAsync($"/api/entity?universe=dracula&entity={Encoded(Swales)}", HttpStatusCode.NotFound);

        Assert.Equal(BookFile, error.GetProperty("leftOut").GetProperty("path").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-94")]
    public async Task NoMessageSendsAWriterToTheApi()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var unknown = await server.GetJsonAsync("/api/entity?entity=Nobody", HttpStatusCode.NotFound);

        Assert.Equal("unknown_entity", unknown.GetProperty("code").GetString());
        Assert.Contains("Use the search box to find it.", unknown.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/api/", unknown.GetProperty("message").GetString(), StringComparison.Ordinal);
        var words = ReadEndpoints.Words.ToString();
        Assert.DoesNotContain("/api/", words, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-95")]
    public async Task NothingAtAPositionLeaksWhatsLeftOut()
    {
        using var den = TempDen.Dracula();
        den.Write(Broken, OneError);
        den.Write(Swales, den.Read(Swales) + "\n## The secret twin\n<!-- wolfden: status=maybe -->\nWhispers.\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        // The list is no view at a position: it refuses one.
        var refused = await server.GetJsonAsync("/api/den/errors?asOf=dracula:1", HttpStatusCode.BadRequest);
        Assert.Equal("bad_argument", refused.GetProperty("code").GetString());
        await server.GetJsonAsync("/api/file/source?asOf=dracula:1&path=" + Encoded(Broken), HttpStatusCode.BadRequest);

        // By path or id, a position says it's left out and nothing about the file or the error.
        foreach (var name in new[] { Broken, Id })
        {
            var at = await server.GetJsonAsync($"/api/entity?asOf=dracula:1&entity={Encoded(name)}", HttpStatusCode.NotFound);
            var text = at.GetRawText();
            Assert.Contains("every story position", at.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Equal(JsonValueKind.Null, at.GetProperty("leftOut").GetProperty("line").ValueKind);
            Assert.DoesNotContain("maybe", text, StringComparison.Ordinal);
            Assert.DoesNotContain("F-ENT-5", text, StringComparison.Ordinal);
            if (name == Id)
            {
                Assert.DoesNotContain("ember-vault", text, StringComparison.Ordinal);
            }
        }

        // By name, it's as if there's no such note: a position never confirms a name.
        var byName = await server.GetJsonAsync($"/api/entity?asOf=dracula:1&entity={Encoded("Ember Vault")}", HttpStatusCode.NotFound);
        Assert.False(byName.TryGetProperty("leftOut", out _));
        Assert.Contains("No entity", byName.GetProperty("message").GetString(), StringComparison.Ordinal);
        var unknown = await server.GetJsonAsync($"/api/entity?asOf=dracula:1&entity={Encoded("Nobody at all")}", HttpStatusCode.NotFound);
        Assert.Equal(
            unknown.GetProperty("message").GetString()!.Replace("Nobody at all", "", StringComparison.Ordinal),
            byName.GetProperty("message").GetString()!.Replace("Ember Vault", "", StringComparison.Ordinal));

        // A section's name stays out of every view at a position.
        foreach (var path in new[] { "/api/entity?asOf=dracula:9&entity=Mr.%20Swales", "/api/canon?asOf=dracula:9", "/api/search?asOf=dracula:9&query=whispers" })
        {
            var text = (await server.GetJsonAsync(path, HttpStatusCode.OK)).GetRawText();
            Assert.DoesNotContain("secret twin", text, StringComparison.Ordinal);
        }
    }
}

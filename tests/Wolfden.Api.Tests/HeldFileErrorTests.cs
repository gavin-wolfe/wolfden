using System.Net;
using System.Text;
using System.Text.Json;
using Wolfden.Core.Checks;
using Wolfden.Core.Documents;
using Wolfden.Mcp.Tests;

namespace Wolfden.Api.Tests;

/// <summary>
/// A file or folder that can't be read (#284), in the den's list of errors and
/// in Edit file (#280). The list says what <c>wolfden check</c> says, the file
/// can't be opened while that lasts, and nothing tells the writer to add a file
/// that's there.
/// </summary>
public sealed class HeldFileErrorTests
{
    private const string BookFolder = "universes/dracula/books/dracula";
    private const string BookFile = BookFolder + "/book.md";
    private const string Swales = BookFolder + "/entities/characters/mr-swales.md";
    private const string AftermathFile = "universes/dracula/books/aftermath/book.md";
    private const string Broken = "universes/dracula/entities/notes/ember-vault.md";
    private const string Id = "01M4TF5F0EKGGWD5FNVBAEZ280";

    private const string OneError = "---\nid: " + Id + "\ntype: note\nname: Ember Vault\nstatus: maybe\n---\n\n# Ember Vault\n\nA vault under the abbey.\n";
    private const string Healthy = "---\nid: " + Id + "\ntype: note\nname: Ember Vault\nstatus: canon\n---\n\n# Ember Vault\n\nA vault under the abbey.\n";
    private const string HealthyAtAPosition = "---\nid: " + Id + "\ntype: note\nname: Ember Vault\nstatus: canon\nfrom: aftermath:1\n---\n\n# Ember Vault\n\nA vault under the abbey.\n";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Encoded(string text) => Uri.EscapeDataString(text);

    private static void WriteAftermath(TempDen den) =>
        den.Write(AftermathFile, "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z2\nname: Aftermath\nstatus: draft\nchapters: [1, 2]\n---\n");

    private static string Full(TempDen den, string path) => Path.Combine(den.Root, path.Replace('/', Path.DirectorySeparatorChar));

    private static async Task<List<JsonElement>> Errors(Server server) =>
        (await server.GetJsonAsync("/api/den/errors", HttpStatusCode.OK)).GetProperty("errors").EnumerateArray().ToList();

    private static async Task<(HttpStatusCode Status, JsonElement Json)> Save(Server server, string path, string text, string hash)
    {
        using var body = new StringContent(JsonSerializer.Serialize(new { path, text, expectedHash = hash }), Encoding.UTF8, "application/json");
        using var response = await server.Client.PostAsync("/api/file/edit", body, Token);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone());
    }

    private static async Task<string> HashOf(Server server, string path) =>
        (await server.GetJsonAsync($"/api/file/source?path={Encoded(path)}", HttpStatusCode.OK)).GetProperty("hash").GetString()!;

    private static string Text(JsonElement error, string property) => error.GetProperty(property).GetString()!;

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task AHeldNoteIsListedAsCheckListsItAndCantBeOpened()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        using (LockedFile.Hold(Full(den, Swales)))
        {
            var errors = await Errors(server);

            var held = Assert.Single(errors, e => Text(e, "rule") == "F-GEN-5");
            Assert.Equal(Swales, Text(held, "path"));
            Assert.Equal("note", Text(held, "kind"));
            Assert.Equal("file", Text(held, "reach"));
            Assert.Equal("dracula", Text(held, "universe"));
            Assert.Equal(JsonValueKind.Null, held.GetProperty("line").ValueKind);
            Assert.False(held.GetProperty("canEdit").GetBoolean());

            var check = DenCheck.Run(den.Root).Diagnostics.Single(d => d.Diagnostic.RuleId == "F-GEN-5");
            Assert.Equal(check.Path, Text(held, "path"));
            Assert.Equal(check.Diagnostic.Message, Text(held, "message"));
            Assert.Equal(DiagnosticSeverity.Error, check.Diagnostic.Severity);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task AHeldDenYamlSaysItCouldntBeReadAndNotThatItsMissing()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        using (LockedFile.Hold(Full(den, "den.yaml")))
        {
            var answer = await server.GetJsonAsync("/api/den/errors", HttpStatusCode.OK);

            Assert.False(answer.GetProperty("denLoaded").GetBoolean());
            var errors = answer.GetProperty("errors").EnumerateArray().ToList();
            Assert.DoesNotContain(errors, e => Text(e, "rule") == "F-DEN-1");
            var held = Assert.Single(errors);
            Assert.Equal("F-GEN-5", Text(held, "rule"));
            Assert.Equal("den.yaml", Text(held, "path"));
            Assert.Equal("den", Text(held, "reach"));
            Assert.False(held.GetProperty("canEdit").GetBoolean());
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task ADenYamlThatsGoneStillSaysItsMissing()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        File.Delete(Full(den, "den.yaml"));

        var error = Assert.Single(await Errors(server));

        Assert.Equal("F-DEN-1", Text(error, "rule"));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task AHeldBookFileKeepsItsFoldersErrorWhichNamesTheFileThatsThere()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        using (LockedFile.Hold(Full(den, BookFile)))
        {
            var errors = await Errors(server);

            var file = Assert.Single(errors, e => Text(e, "rule") == "F-GEN-5");
            Assert.Equal(BookFile, Text(file, "path"));
            Assert.Equal("book", Text(file, "kind"));
            Assert.Equal("container", Text(file, "reach"));
            Assert.False(file.GetProperty("canEdit").GetBoolean());
            Assert.Equal(JsonValueKind.Null, file.GetProperty("heldFile").ValueKind);

            // It's still listed, as check lists it (rule 7), but it knows the file is there.
            var folder = Assert.Single(errors, e => Text(e, "rule") == "F-LAY-4");
            Assert.Equal(BookFolder, Text(folder, "path"));
            Assert.Equal("folder", Text(folder, "kind"));
            Assert.Equal(BookFile, Text(folder, "heldFile"));
            Assert.False(folder.GetProperty("canEdit").GetBoolean());
            Assert.Contains(DenCheck.Run(den.Root).Diagnostics, d => d.Diagnostic.RuleId == "F-LAY-4" && d.Path == BookFolder);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task AFolderReallyMissingItsFileNamesNoFile()
    {
        using var den = TempDen.Dracula();
        Directory.CreateDirectory(Full(den, "universes/dracula/books/empty"));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var folder = Assert.Single(await Errors(server), e => Text(e, "rule") == "F-LAY-4");

        Assert.Equal(JsonValueKind.Null, folder.GetProperty("heldFile").ValueKind);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task AFolderThatCantBeListedIsListedAsAFolderWhateverItsName()
    {
        const string Folder = "universes/dracula/vol.2";
        using var den = TempDen.Dracula();
        Directory.CreateDirectory(Full(den, Folder));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        using (LockedFile.Unlistable(Full(den, Folder)))
        {
            var error = Assert.Single(await Errors(server), e => Text(e, "rule") == "F-GEN-5");

            Assert.Equal(Folder, Text(error, "path"));
            Assert.Equal("container", Text(error, "reach"));
            Assert.False(error.GetProperty("canEdit").GetBoolean());
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task APinnedServerListsWhatsHeldInItsUniverseAndNowhereElse()
    {
        const string Here = "universes/drift/universe.md";
        const string There = "universes/starfall/universe.md";
        using var den = TempDen.Starfall();
        await using var pinned = await Server.StartAsync(new WolfdenApiOptions(den.Root) { PinnedUniverse = "drift" });
        await using var elsewhere = await Server.StartAsync(new WolfdenApiOptions(den.Root) { PinnedUniverse = "starfall" });
        using (LockedFile.Hold(Full(den, There)))
        {
            // A pinned check reads the other universes' container files, so this one was seen.
            Assert.Empty(await Errors(pinned));
            Assert.Contains(await Errors(elsewhere), e => Text(e, "rule") == "F-GEN-5" && Text(e, "path") == There);

            // Nor does Edit file say it's there.
            var refused = await pinned.GetJsonAsync($"/api/file/source?path={Encoded(There)}", HttpStatusCode.NotFound);
            Assert.Equal("unknown_file", Text(refused, "code"));
        }

        using (LockedFile.Hold(Full(den, Here)))
        {
            // A different server's answer, as this one has its first answer now and it stands for a minute.
            await using var again = await Server.StartAsync(new WolfdenApiOptions(den.Root) { PinnedUniverse = "drift" });
            Assert.Contains(await Errors(again), e => Text(e, "rule") == "F-GEN-5" && Text(e, "path") == Here);
        }
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-96")]
    [InlineData(Swales)]
    [InlineData(BookFile)]
    [InlineData("den.yaml")]
    public async Task ADirectLinkToAHeldFileSaysItCantBeReadNotThatItsGone(string path)
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        using (LockedFile.Hold(Full(den, path)))
        {
            var error = await server.GetJsonAsync($"/api/file/source?path={Encoded(path)}", HttpStatusCode.ServiceUnavailable);

            Assert.Equal("den_unreadable", Text(error, "code"));
            Assert.Contains(path, Text(error, "message"), StringComparison.Ordinal);
            Assert.DoesNotContain("moved or deleted", Text(error, "message"), StringComparison.Ordinal);
        }

        var gone = await server.GetJsonAsync($"/api/file/source?path={Encoded(Swales.Replace("mr-swales", "nobody", StringComparison.Ordinal))}", HttpStatusCode.NotFound);
        Assert.Equal("unknown_file", Text(gone, "code"));
        Assert.Contains("moved or deleted", Text(gone, "message"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task SavingAHeldFileSaysItCantBeRead()
    {
        using var den = TempDen.Dracula();
        den.Write(Broken, OneError);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = await HashOf(server, Broken);
        using (LockedFile.Hold(Full(den, Broken)))
        {
            var (status, json) = await Save(server, Broken, Healthy, hash);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal("den_unreadable", Text(json, "code"));
        }

        Assert.Equal(OneError, den.Read(Broken));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task AFixIsRefusedWhileAFileItsSaveReadIsHeld()
    {
        using var den = TempDen.Dracula();
        WriteAftermath(den);
        den.Write(Broken, OneError);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = await HashOf(server, Broken);
        using (LockedFile.Hold(Full(den, AftermathFile)))
        {
            var (status, json) = await Save(server, Broken, Healthy, hash);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal("den_unreadable", Text(json, "code"));
            Assert.Contains(AftermathFile, Text(json, "message"), StringComparison.Ordinal);
            Assert.Equal(OneError, den.Read(Broken));
        }

        var (after, _) = await Save(server, Broken, Healthy, hash);
        Assert.Equal(HttpStatusCode.OK, after);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task AFixThatLeavesItLeftOutIsRefusedTooWhileAFileItsSaveReadIsHeld()
    {
        const string TwoErrors = "---\nid: " + Id + "\ntype: note\nstatus: maybe\n---\n\n# Ember Vault\n\nA vault under the abbey.\n";
        using var den = TempDen.Dracula();
        WriteAftermath(den);
        den.Write(Broken, TwoErrors);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = await HashOf(server, Broken);
        using (LockedFile.Hold(Full(den, AftermathFile)))
        {
            var (status, json) = await Save(server, Broken, OneError, hash);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal("den_unreadable", Text(json, "code"));
            Assert.Equal(TwoErrors, den.Read(Broken));
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task ASaveOfABookFileIsRefusedWhileAFileItsSaveReadIsHeld()
    {
        using var den = TempDen.Dracula();
        WriteAftermath(den);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var before = den.Read(BookFile);
        var hash = await HashOf(server, BookFile);
        using (LockedFile.Hold(Full(den, AftermathFile)))
        {
            var (status, json) = await Save(server, BookFile, before + "\nA line more.\n", hash);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal("den_unreadable", Text(json, "code"));
            Assert.Equal(before, den.Read(BookFile));
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task AFixGoesAheadWhileOnlyAnotherNoteIsHeld()
    {
        using var den = TempDen.Dracula();
        den.Write(Broken, OneError);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = await HashOf(server, Broken);
        using (LockedFile.Hold(Full(den, Swales)))
        {
            var (status, _) = await Save(server, Broken, Healthy, hash);

            Assert.Equal(HttpStatusCode.OK, status);
        }

        Assert.Equal(Healthy, den.Read(Broken));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task ASaveThatBringsANoteBackSaysAHeldBookMayBeWhyItsPositionDoesntResolve()
    {
        using var den = TempDen.Dracula();
        WriteAftermath(den);
        den.Write(Broken, OneError);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = await HashOf(server, Broken);
        using (LockedFile.Hold(Full(den, AftermathFile)))
        {
            var (status, json) = await Save(server, Broken, HealthyAtAPosition, hash);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Equal("bad_position", Text(json, "code"));
            Assert.Contains($"Couldn't read {AftermathFile} right now, which may be why.", Text(json, "message"), StringComparison.Ordinal);
            Assert.Equal(OneError, den.Read(Broken));
        }

        // Without the hold, a position that really doesn't resolve says nothing about reading.
        var (_, real) = await Save(server, Broken, HealthyAtAPosition.Replace("aftermath:1", "aftermath:no-such-chapter", StringComparison.Ordinal), hash);
        Assert.Equal("bad_position", Text(real, "code"));
        Assert.DoesNotContain("may be why", Text(real, "message"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-96")]
    public async Task ANoteLeftOutKeepsWhyItsLeftOutWhileAnotherFileIsHeld()
    {
        using var den = TempDen.Dracula();
        den.Write(Broken, OneError);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        using (LockedFile.Hold(Full(den, Swales)))
        {
            var error = await server.GetJsonAsync($"/api/entity/source?entity={Encoded(Broken)}", HttpStatusCode.NotFound);

            Assert.Equal("unknown_entity", Text(error, "code"));
            Assert.Contains("which may be why", Text(error, "message"), StringComparison.Ordinal);
            Assert.Equal(Broken, Text(error.GetProperty("leftOut"), "path"));
            Assert.Equal("F-ENT-5", Text(error.GetProperty("leftOut"), "rule"));
        }
    }
}

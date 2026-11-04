using System.Net;
using System.Text;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// Pages read a file another program has open to write; a save doesn't, and
/// names the file that stopped it, not the one on the page (#298).
/// </summary>
public sealed class SharedReadEndpointTests
{
    private const string BookFile = "universes/dracula/books/dracula/book.md";
    private const string Swales = "universes/dracula/books/dracula/entities/characters/mr-swales.md";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FileStream OpenToWrite(TempDen den, string path) =>
        new(Path.Combine(den.Root, path.Replace('/', Path.DirectorySeparatorChar)), FileMode.Open, FileAccess.Write, FileShare.Read);

    private static async Task<(HttpStatusCode Status, JsonElement Json)> PostAsync(Server server, string path, object body)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await server.Client.PostAsync(path, content, Token);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone());
    }

    private static async Task<string> SourceHash(Server server, string entity) =>
        (await server.GetJsonAsync($"/api/entity/source?entity={Uri.EscapeDataString(entity)}", HttpStatusCode.OK)).GetProperty("hash").GetString()!;

    private static void SkipUnlessWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows can refuse a program that's writing a file: on Linux and macOS it doesn't normally say so.");
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-98")]
    public async Task APageOpensANoteOpenElsewhereAndTheListOfProblemsDoesntCountIt()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        using (OpenToWrite(den, Swales))
        {
            await server.GetJsonAsync($"/api/entity/source?entity={Uri.EscapeDataString(Swales)}", HttpStatusCode.OK);
            await server.GetJsonAsync($"/api/file/source?path={Uri.EscapeDataString(Swales)}", HttpStatusCode.OK);
            var errors = await server.GetJsonAsync("/api/den/errors", HttpStatusCode.OK);
            Assert.DoesNotContain(errors.GetProperty("errors").EnumerateArray(), e => e.GetProperty("rule").GetString() == "F-GEN-5");
        }
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-98")]
    public async Task SavingANoteOpenElsewhereNamesItAndHasNoNewerCopyToOffer()
    {
        SkipUnlessWindows();
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = await SourceHash(server, Swales);
        var before = den.Read(Swales);

        using (OpenToWrite(den, Swales))
        {
            var (status, json) = await PostAsync(server, "/api/edit/lead", new { entity = Swales, expectedHash = hash, text = "A new opening." });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal("den_unreadable", json.GetProperty("code").GetString());
            Assert.Contains(Swales, json.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.False(json.TryGetProperty("current", out var current) && current.ValueKind != JsonValueKind.Null);
        }

        Assert.Equal(before, den.Read(Swales));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-98")]
    public async Task EditingANoteNamesTheBookFileOpenElsewhereAndDoesntBlameTheNote()
    {
        SkipUnlessWindows();
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = await SourceHash(server, Swales);
        var before = den.Read(Swales);

        using (OpenToWrite(den, BookFile))
        {
            var (status, json) = await PostAsync(server, "/api/edit/lead", new { entity = Swales, expectedHash = hash, text = "A new opening." });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal("den_unreadable", json.GetProperty("code").GetString());
            var message = json.GetProperty("message").GetString();
            Assert.Contains(BookFile, message, StringComparison.Ordinal);
            Assert.DoesNotContain(Swales, message, StringComparison.Ordinal);
            Assert.DoesNotContain("has errors", message, StringComparison.Ordinal);
        }

        Assert.Equal(before, den.Read(Swales));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-98")]
    public async Task SavingANoteFromEditFileNamesTheBookFileOpenElsewhere()
    {
        SkipUnlessWindows();
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var hash = (await server.GetJsonAsync($"/api/file/source?path={Uri.EscapeDataString(Swales)}", HttpStatusCode.OK)).GetProperty("hash").GetString();
        var before = den.Read(Swales);

        using (OpenToWrite(den, BookFile))
        {
            var (status, json) = await PostAsync(server, "/api/file/edit", new { path = Swales, text = before.ReplaceLineEndings("\n") + "\nMore.\n", expectedHash = hash });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal("den_unreadable", json.GetProperty("code").GetString());
            Assert.Contains(BookFile, json.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        Assert.Equal(before, den.Read(Swales));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-98")]
    public void ARefusalAboutAnotherFileOffersNoCopyOfThisOne()
    {
        using var den = TempDen.Dracula();
        var tools = new DenTools(den.Root);
        var about = new DenToolException(ToolErrorCodes.StaleFile, "changed") { StalePath = "universes/dracula/entities/notes/other.md" };
        var own = new DenToolException(ToolErrorCodes.StaleFile, "changed") { StalePath = Swales };
        var hashOnly = new DenToolException(ToolErrorCodes.StaleFile, "changed");

        Assert.Null(WriteEndpoints.CurrentIfItsThisFile(tools, about, Swales, null));
        Assert.NotNull(WriteEndpoints.CurrentIfItsThisFile(tools, own, Swales, null));
        Assert.NotNull(WriteEndpoints.CurrentIfItsThisFile(tools, hashOnly, Swales, null));
    }

    [Theory]
    [InlineData("/api/chapter-defaults?book=no-such-book")]
    [InlineData("/api/works?universe=no-such-universe")]
    [Trait("Scenario", "SC-WEB-98")]
    public async Task AWrongNameOnAReadDoesntBlameANoteOpenElsewhere(string path)
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        using (OpenToWrite(den, Swales))
        {
            using var response = await server.Client.GetAsync(path, Token);
            var text = await response.Content.ReadAsStringAsync(Token);

            Assert.NotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain("den_unreadable", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Swales, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(BookFile)]
    [InlineData("universes/dracula/universe.md")]
    [InlineData("den.yaml")]
    [Trait("Scenario", "SC-WEB-98")]
    public async Task AnImageCantReachAFrozenBookWhileWhatsAboveTheNoteIsOpenElsewhere(string held)
    {
        SkipUnlessWindows();
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var freeze = await server.GetJsonAsync("/api/book/freeze?book=dracula", HttpStatusCode.OK);
        var (frozen, _) = await PostAsync(server, "/api/book/freeze", new { book = "dracula", expectedHash = freeze.GetProperty("hash").GetString() });
        Assert.Equal(HttpStatusCode.OK, frozen);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2];
        var images = Path.Combine(den.Root, Swales.Replace('/', Path.DirectorySeparatorChar));
        images = Path.Combine(Path.GetDirectoryName(images)!, "images");

        using (OpenToWrite(den, held))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/entity/image?entity={Uri.EscapeDataString(Swales)}&name=map.png") { Content = new ByteArrayContent(png) };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            using var response = await server.Client.SendAsync(request, Token);
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("den_unreadable", json.GetProperty("code").GetString());
            Assert.Contains(held, json.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        Assert.False(Directory.Exists(images));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-98")]
    public async Task AddingAChapterNamesTheBookFileOpenElsewhere()
    {
        SkipUnlessWindows();
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var before = den.Read(BookFile);
        var hash = (await server.GetJsonAsync("/api/book/chapters?book=dracula", HttpStatusCode.OK)).GetProperty("hash").GetString();

        using (OpenToWrite(den, BookFile))
        {
            var (status, json) = await PostAsync(server, "/api/chapter/create", new { book = "dracula", title = "A new chapter", expectedHash = hash });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal("den_unreadable", json.GetProperty("code").GetString());
            Assert.Contains(BookFile, json.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        Assert.Equal(before, den.Read(BookFile));
    }
}

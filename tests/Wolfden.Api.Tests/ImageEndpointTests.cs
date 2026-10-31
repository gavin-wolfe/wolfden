using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wolfden.Core.History;

namespace Wolfden.Api.Tests;

/// <summary>
/// Adding an image to a note from the editor (#187): <c>POST /api/entity/image</c>
/// saves the file in an <c>images/</c> folder beside the note, under a name
/// the server makes, through the write tools. Over a copy of the Dracula
/// sample, and the invented Starfall den for pinning.
/// </summary>
public class ImageEndpointTests
{
    private const string Castle = "universes/dracula/entities/locations/castle-dracula.md";
    private const string Images = "universes/dracula/entities/locations/images";
    private const string SwalesPath = "universes/dracula/books/dracula/entities/characters/mr-swales.md";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(HttpStatusCode Status, JsonElement Json)> UploadAsync(
        Server server, string query, byte[] bytes, string? contentType = "application/octet-stream", Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/entity/image?" + query) { Content = new ByteArrayContent(bytes) };
        if (contentType is not null)
        {
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        configure?.Invoke(request);
        using var response = await server.Client.SendAsync(request, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string Query(string entity, string name, string? universe = null) =>
        $"entity={Uri.EscapeDataString(entity)}&name={Uri.EscapeDataString(name)}" + (universe is null ? "" : $"&universe={universe}");

    private static string Full(TempDen den, string path) => Path.Combine(den.Root, path.Replace('/', Path.DirectorySeparatorChar));

    private static string[] ImagesIn(TempDen den, string folder = Images) =>
        Directory.Exists(Full(den, folder)) ? Directory.GetFiles(Full(den, folder)).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray() : [];

    [Fact]
    [Trait("Scenario", "SC-WEB-80")]
    public async Task AnImageIsSavedBesideTheNoteUnderASlugAndTheNoteIsUnchanged()
    {
        using var den = TempDen.Dracula();
        var note = den.Read(Castle);
        var options = new WolfdenApiOptions(den.Root);
        await using var server = await Server.StartAsync(options);

        var (status, json) = await UploadAsync(server, Query(Castle, "Castle Map (1832).PNG"), Png, "image/png");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal($"{Images}/castle-map-1832.png", json.GetProperty("path").GetString());
        Assert.Equal("images/castle-map-1832.png", json.GetProperty("link").GetString());
        Assert.Equal("Castle Map (1832)", json.GetProperty("alt").GetString());
        Assert.Equal(Png.Length, json.GetProperty("bytes").GetInt32());
        Assert.Empty(json.GetProperty("effects").EnumerateArray());
        Assert.Equal(Png, File.ReadAllBytes(Full(den, $"{Images}/castle-map-1832.png")));
        Assert.Equal(note, den.Read(Castle));

        // It's the server's own write, so the checkpoint dialog marks it "saved in Wolfden" (#30).
        Assert.True(options.Ledger.IsOwn(new HistoryFileChange(HistoryChangeKind.Added, $"{Images}/castle-map-1832.png")));

        // And serve shows it at the path the note will hold.
        using var image = await server.Client.GetAsync($"/den-files/{Images}/castle-map-1832.png", Token);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-80")]
    public async Task AFileAlreadyThereIsNeverReplacedTheNewOneGetsANumber()
    {
        using var den = TempDen.Dracula();
        Directory.CreateDirectory(Full(den, Images));
        File.WriteAllBytes(Full(den, $"{Images}/castle-map.png"), [1, 2, 3]);
        File.WriteAllBytes(Full(den, $"{Images}/Castle-Map-2.png"), [4, 5, 6]);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await UploadAsync(server, Query(Castle, "castle_map.png"), Png);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("images/castle-map-3.png", json.GetProperty("link").GetString());
        Assert.Contains("already has castle-map.png, so the image was saved as castle-map-3.png", Assert.Single(json.GetProperty("effects").EnumerateArray()).GetString(), StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Full(den, $"{Images}/castle-map.png")));
        Assert.Equal([4, 5, 6], File.ReadAllBytes(Full(den, $"{Images}/Castle-Map-2.png")));
        Assert.Equal(Png, File.ReadAllBytes(Full(den, $"{Images}/castle-map-3.png")));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-80")]
    public async Task AnImagesFolderInAnotherCaseIsUsedAsItsSpelled()
    {
        using var den = TempDen.Dracula();
        const string Folder = "universes/dracula/entities/locations/Images";
        Directory.CreateDirectory(Full(den, Folder));
        File.WriteAllBytes(Full(den, $"{Folder}/map.png"), [1, 2, 3]);
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await UploadAsync(server, Query(Castle, "map.png"), Png);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal($"{Folder}/map-2.png", json.GetProperty("path").GetString());
        Assert.Equal("Images/map-2.png", json.GetProperty("link").GetString());
        Assert.Equal(["Images"], Directory.GetDirectories(Full(den, "universes/dracula/entities/locations")).Select(d => Path.GetFileName(d)).ToArray());
        Assert.Equal(Png, File.ReadAllBytes(Full(den, $"{Folder}/map-2.png")));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-81")]
    public async Task AFileNamedImagesBesideTheNoteIsAConflictThatSaysWhatToRename()
    {
        using var den = TempDen.Dracula();
        File.WriteAllText(Full(den, "universes/dracula/entities/locations/images"), "not a folder");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await UploadAsync(server, Query(Castle, "map.png"), Png);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("already_exists", json.GetProperty("code").GetString());
        Assert.Contains("There's a file named 'images' beside the note", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Rename or move the file", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("not a folder", den.Read("universes/dracula/entities/locations/images"));
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-81")]
    [InlineData("../../../../../escape.png", "escape.png")]
    [InlineData("..\\..\\..\\escape.png", "escape.png")]
    [InlineData("/etc/escape.png", "escape.png")]
    [InlineData(".hidden.png", "hidden.png")]
    [InlineData("CON.png", "con-image.png")]
    [InlineData("%2e%2e%2fmap.png", "2e-2e-2fmap.png")]
    [InlineData("Ærøskøbing harbour.jpg", "aeroskobing-harbour.jpg")]
    [InlineData("★.png", "image.png")]
    [InlineData(".png", "image.png")]
    public async Task TheServerPicksTheNameSoItStaysInTheNotesImagesFolder(string given, string saved)
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var bytes = given.EndsWith(".jpg", StringComparison.Ordinal) ? Jpeg : Png;

        var (status, json) = await UploadAsync(server, Query(Castle, given), bytes);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal($"{Images}/{saved}", json.GetProperty("path").GetString());
        Assert.Equal([saved], ImagesIn(den));
        Assert.False(File.Exists(Path.Combine(den.Root, "escape.png")));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-81")]
    public async Task ANoteInAFinalBookRefusesTheImageWithTheUnfreezeHint()
    {
        using var den = TempDen.Dracula();
        var book = "universes/dracula/books/dracula/book.md";
        den.Write(book, den.Read(book).Replace("status: draft", "status: final", StringComparison.Ordinal));
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await UploadAsync(server, Query(SwalesPath, "whitby.png"), Png);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("frozen", json.GetProperty("code").GetString());
        Assert.Contains("final (frozen)", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("Unfreeze… on the page of book 'dracula' opens it for editing again.", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(ImagesIn(den, "universes/dracula/books/dracula/entities/characters/images"));
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-81")]
    [InlineData("map.png", "jpeg", "it's a JPEG. Rename it to end in .jpg")]
    [InlineData("map.png", "text", "isn't one")]
    [InlineData("map.gif", "png", "it's a PNG. Rename it to end in .png")]
    [InlineData("map.svg", "html", "isn't one")]
    [InlineData("notes.txt", "text", "Wolfden adds .png, .jpg, .jpeg, .gif, .webp, .avif, .svg files, not .txt")]
    [InlineData("map", "png", "no extension")]
    [InlineData("map.png", "empty", "the file is empty")]
    public async Task TheContentsMustBeTheImageTypeTheNameSays(string name, string contents, string said)
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        byte[] bytes = contents switch
        {
            "jpeg" => Jpeg,
            "png" => Png,
            "html" => Encoding.UTF8.GetBytes("<html><body>not a picture</body></html>"),
            "empty" => [],
            _ => Encoding.UTF8.GetBytes("just some words"),
        };

        var (status, json) = await UploadAsync(server, Query(Castle, name), bytes);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("bad_argument", json.GetProperty("code").GetString());
        Assert.Contains(said, json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(ImagesIn(den));
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-81")]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData(null)]
    public async Task OnlyTheFileItselfIsTakenNotAFormOrText(string? contentType)
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await UploadAsync(server, Query(Castle, "map.png"), Png, contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, status);
        Assert.Equal("unsupported_media_type", json.GetProperty("code").GetString());
        Assert.Empty(ImagesIn(den));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-81")]
    public async Task AnImageOverTenMegabytesIsTooLarge()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var big = new byte[DenTools.MaxImageBytes + 1];
        Png.CopyTo(big, 0);

        var (status, json) = await UploadAsync(server, Query(Castle, "map.png"), big);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, status);
        Assert.Equal("too_large", json.GetProperty("code").GetString());
        Assert.Empty(ImagesIn(den));

        // Exactly the limit is fine.
        var (fits, _) = await UploadAsync(server, Query(Castle, "map.png"), big[..DenTools.MaxImageBytes]);
        Assert.Equal(HttpStatusCode.Created, fits);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-81")]
    [InlineData("entity=castle-dracula", "Give the image's file name")]
    [InlineData("name=map.png", "entity")]
    [InlineData("entity=castle-dracula&name=map.png&path=../x.png", "doesn't take 'path'")]
    [InlineData("entity=castle-dracula&name=a.png&name=b.png", "once")]
    public async Task ItTakesOnlyTheNoteTheUniverseAndTheName(string query, string said)
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var (status, json) = await UploadAsync(server, query, Png);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(said, json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(ImagesIn(den));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-81")]
    public async Task ItAnswersPostOnlyAndRefusesAnotherSite()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        using var get = await server.Client.GetAsync("/api/entity/image", Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Equal("POST", get.Content.Headers.Allow.Single());

        var (status, json) = await UploadAsync(server, Query(Castle, "map.png"), Png, configure: r => r.Headers.Add("Origin", "https://attacker.example"));
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("cross_site", json.GetProperty("code").GetString());
        Assert.Empty(ImagesIn(den));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-81")]
    public async Task APinnedServerAddsImagesOnlyInItsUniverse()
    {
        using var den = TempDen.Starfall();
        const string Reach = "universes/starfall/entities/locations/kessan-reach.md";
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root) { PinnedUniverse = "drift" });

        var (status, json) = await UploadAsync(server, Query(Reach, "map.png", "starfall"), Png);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("pinned", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(ImagesIn(den, "universes/starfall/entities/locations/images"));
    }

    [Fact]
    public async Task AnImageWhileAnotherWolfdenKeepsWritingIsDenBusyAndWritesNothing()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root) { WriteLockWait = TimeSpan.Zero });
        await server.GetJsonAsync($"/api/entity/source?entity={Uri.EscapeDataString(Castle)}", HttpStatusCode.OK);

        using (new FileStream(DenWriteLock.PathFor(den.Root), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var (status, json) = await UploadAsync(server, Query(Castle, "map.png"), Png);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal("den_busy", json.GetProperty("code").GetString());
            Assert.Empty(ImagesIn(den));
        }

        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(server, Query(Castle, "map.png"), Png)).Status);
    }
}

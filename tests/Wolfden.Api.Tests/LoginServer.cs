using System.Net;
using System.Net.Http.Json;
using Wolfden.Accounts;

namespace Wolfden.Api.Tests;

/// <summary>
/// A server for a copy of Dracula with login on (#315), over an installation
/// folder of its own under the system's temporary folder, never the real
/// one. It has three accounts, one for each role: <c>wren</c> (owner),
/// <c>ash</c> (writer) and <c>rook</c> (viewer), all with <see cref="Password"/>.
/// </summary>
internal sealed class LoginServer : IAsyncDisposable
{
    public const string Password = "correct horse battery";

    private readonly List<HttpClient> _clients = [];

    private readonly Dictionary<HttpClient, CookieContainer> _cookies = [];

    private LoginServer(TempDen den, string home, InstallationFolder installation, LoginOptions login, Server server)
    {
        Den = den;
        HomeRoot = home;
        Installation = installation;
        Login = login;
        Server = server;
    }

    public TempDen Den { get; }

    /// <summary>The temporary folder that holds the installation.</summary>
    public string HomeRoot { get; }

    public InstallationFolder Installation { get; }

    public LoginOptions Login { get; }

    public Server Server { get; }

    /// <summary>This server's origin, as its own page sends it.</summary>
    public string Origin => $"http://127.0.0.1:{Server.Port}";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A new installation with the three accounts, and a server for it.</summary>
    public static async Task<LoginServer> StartAsync(
        Func<InstallationFolder, string, LoginOptions>? options = null, string? webRoot = null, Action<Microsoft.AspNetCore.Builder.WebApplication>? configure = null)
    {
        var home = Path.Combine(Path.GetTempPath(), "wolfden-api-tests", "homes", Guid.NewGuid().ToString("N"));
        var installation = new InstallationFolder(Path.Combine(home, "home"), InstallationSource.Option);
        await using (var store = await AccountStore.OpenAsync(installation, Token))
        {
            Assert.True((await store.AddAsync("wren", Password, AccountRole.Owner)).Ok);
            Assert.True((await store.AddAsync("ash", Password, AccountRole.Writer)).Ok);
            Assert.True((await store.AddAsync("rook", Password, AccountRole.Viewer)).Ok);
        }

        var id = InstallationSettings.EnsureInstallationId(installation);
        var login = options?.Invoke(installation, id) ?? new LoginOptions(installation, id);
        var den = TempDen.Dracula();
        var server = await Server.StartAsync(new WolfdenApiOptions(den.Root) { Login = login, WebRoot = webRoot }, configure);
        return new LoginServer(den, home, installation, login, server);
    }

    /// <summary>A client that keeps cookies, sends this server's Origin on every request as a browser would, and isn't logged in.</summary>
    public HttpClient Client()
    {
        var cookies = new CookieContainer();
        var client = new HttpClient(new HttpClientHandler { CookieContainer = cookies, UseCookies = true, AllowAutoRedirect = false })
        {
            BaseAddress = Server.Client.BaseAddress,
        };
        client.DefaultRequestHeaders.Add("Origin", Origin);
        _clients.Add(client);
        _cookies[client] = cookies;
        return client;
    }

    /// <summary>The Cookie header a client from <see cref="Client"/> sends to this server.</summary>
    public string CookieHeader(HttpClient client) => _cookies[client].GetCookieHeader(Server.Client.BaseAddress!);

    /// <summary>A client logged in as <paramref name="user"/>.</summary>
    public async Task<HttpClient> LoggedInAsync(string user)
    {
        var client = Client();
        using var response = await LogInAsync(client, user, Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    /// <summary>Posts a login for <paramref name="user"/> with <paramref name="password"/>.</summary>
    public static Task<HttpResponseMessage> LogInAsync(HttpClient client, string user, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { username = user, password }, Token);

    /// <summary>The accounts, opened for a change made the way the CLI makes it.</summary>
    public Task<AccountStore> AccountsAsync() => AccountStore.OpenAsync(Installation, Token);

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        await Server.DisposeAsync();
        Den.Dispose();
        try
        {
            Directory.Delete(HomeRoot, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort; the OS cleans its temp folder.
        }
    }
}

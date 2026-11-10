using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wolfden.Accounts;

namespace Wolfden.Api.Tests;

/// <summary>Logging in to the web pages (#315 PR 4b): the cookie, the limits on guessing, roles, and the hardening around them.</summary>
public class LoginTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone();

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task WithLoginOffEveryoneIsTheOwnerAsBefore()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var status = await server.GetJsonAsync("/api/auth/status", HttpStatusCode.OK);
        Assert.False(status.GetProperty("login").GetBoolean());
        Assert.Equal("owner", status.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("user").ValueKind);
        await server.GetJsonAsync("/api/den", HttpStatusCode.OK);

        using var login = await server.Client.PostAsJsonAsync("/api/auth/login", new { username = "wren", password = LoginServer.Password }, Token);
        Assert.Equal(HttpStatusCode.Conflict, login.StatusCode);
        Assert.Equal("login_off", (await Json(login)).GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task ALoginGivesAStrictHttpOnlyCookieNamedForTheInstallation()
    {
        await using var server = await LoginServer.StartAsync();
        using var client = server.Client();

        var before = await client.GetFromJsonAsync<JsonElement>("/api/auth/status", Token);
        Assert.True(before.GetProperty("login").GetBoolean());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("role").ValueKind);

        using var response = await LoginServer.LogInAsync(client, "WREN", LoginServer.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("wren", body.GetProperty("user").GetString());
        Assert.Equal("owner", body.GetProperty("role").GetString());

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"wolfden-{server.Login.InstallationId}=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        // Sessions last 14 days on this computer, and slide.
        var expires = DateTimeOffset.Parse(cookie.Split(';').Single(p => p.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase)).Trim()[8..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddDays(13.9), DateTimeOffset.UtcNow.AddDays(14.1));

        var after = await client.GetFromJsonAsync<JsonElement>("/api/auth/status", Token);
        Assert.Equal("wren", after.GetProperty("user").GetString());
        Assert.Equal("owner", after.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/den", Token)).StatusCode);

        // The key that signs it is kept in the installation's keys folder, never a den.
        Assert.NotEmpty(Directory.GetFiles(server.Installation.KeysFolder));
        Assert.Empty(Directory.GetFiles(server.Den.Root, "key-*.xml", SearchOption.AllDirectories));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task UnderASecureOriginTheCookieIsSecureWithTheHostPrefixAndLastsTwelveHours()
    {
        await using var server = await LoginServer.StartAsync((home, id) => new LoginOptions(home, id) { SecureOrigin = true });
        using var client = server.Client();

        using var response = await LoginServer.LogInAsync(client, "wren", LoginServer.Password);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"__Host-wolfden-{server.Login.InstallationId}=", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TimeSpan.FromHours(12), server.Login.Session);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task TwoInstallationsHaveCookiesOfTheirOwn()
    {
        await using var first = await LoginServer.StartAsync();
        await using var second = await LoginServer.StartAsync();

        Assert.NotEqual(first.Login.CookieName, second.Login.CookieName);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task ACookieStillWorksAfterARestart()
    {
        await using var server = await LoginServer.StartAsync();
        var client = await server.LoggedInAsync("ash");
        var cookie = server.CookieHeader(client);
        Assert.StartsWith(server.Login.CookieName + "=", cookie, StringComparison.Ordinal);

        // A second server over the same installation reads the cookie, with the keys kept in keys/.
        using var den = TempDen.Dracula();
        await using var restarted = await Server.StartAsync(new WolfdenApiOptions(den.Root) { Login = server.Login });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/status");
        request.Headers.Add("Cookie", cookie);
        using var response = await restarted.Client.SendAsync(request, Token);

        var status = await Json(response);
        Assert.Equal("ash", status.GetProperty("user").GetString());
        Assert.Equal("writer", status.GetProperty("role").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-14")]
    public async Task AWrongPasswordAndAnUnknownNameGetTheSameAnswer()
    {
        await using var server = await LoginServer.StartAsync();
        using var client = server.Client();

        using var wrong = await LoginServer.LogInAsync(client, "wren", "not the right password at all");
        using var unknown = await LoginServer.LogInAsync(client, "nobody-here", "not the right password at all");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(Token), await unknown.Content.ReadAsStringAsync(Token));
        Assert.Equal("login_failed", (await Json(wrong)).GetProperty("code").GetString());
        Assert.False(wrong.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-14")]
    public async Task FiveWrongPasswordsLockTheAccountAndALockedAccountLooksLikeAWrongPassword()
    {
        await using var server = await LoginServer.StartAsync();
        using var client = server.Client();

        string? failed = null;
        for (var i = 0; i < AccountsServices.MaxFailedAttempts; i++)
        {
            using var response = await LoginServer.LogInAsync(client, "ash", "not the right password at all");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            failed = await response.Content.ReadAsStringAsync(Token);
        }

        // The right password, now that it's locked: refused, in the same words.
        using var locked = await LoginServer.LogInAsync(client, "ash", LoginServer.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(failed, await locked.Content.ReadAsStringAsync(Token));
        Assert.Contains("wolfden user unlock", failed, StringComparison.Ordinal);

        await using (var accounts = await server.AccountsAsync())
        {
            var ash = (await accounts.ListAsync(Token)).Single(a => a.UserName == "ash");
            Assert.NotNull(ash.LockedUntil);
            Assert.InRange(ash.LockedUntil!.Value, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
            Assert.True((await accounts.UnlockAsync("ash")).Ok);
        }

        using var unlocked = await LoginServer.LogInAsync(client, "ash", LoginServer.Password);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-14")]
    public async Task TooManyAttemptsFromOneAddressAreRefusedForAMinute()
    {
        await using var server = await LoginServer.StartAsync((home, id) => new LoginOptions(home, id) { AttemptsPerAddress = 3, AttemptsPerUser = 100, LimitAddressesOverride = true });
        using var client = server.Client();

        for (var i = 0; i < 3; i++)
        {
            using var response = await LoginServer.LogInAsync(client, $"guess{i}", "not the right password at all");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var refused = await LoginServer.LogInAsync(client, "wren", LoginServer.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("too_many_attempts", (await Json(refused)).GetProperty("code").GetString());
        Assert.Equal(TimeSpan.FromSeconds(60), refused.Headers.RetryAfter?.Delta);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-14")]
    public async Task OnThisComputerOneAddressIsntOneBucketForEveryone()
    {
        // Everyone on 127.0.0.1 is one address: a per-address limit there would let anyone keep the owner out.
        await using var server = await LoginServer.StartAsync((home, id) => new LoginOptions(home, id) { AttemptsPerAddress = 3, AttemptsPerUser = 100 });
        using var client = server.Client();
        Assert.False(server.Login.LimitsAddresses);

        for (var i = 0; i < 5; i++)
        {
            using var response = await LoginServer.LogInAsync(client, $"guess{i}", "not the right password at all");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var owner = await LoginServer.LogInAsync(client, "wren", LoginServer.Password);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);

        // Behind a proxy (a secure origin, a later change), addresses count.
        Assert.True(new LoginOptions(server.Installation, server.Login.InstallationId) { SecureOrigin = true }.LimitsAddresses);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-14")]
    public async Task ANameSpelledWithLookalikeLettersIsntAnotherBucket()
    {
        await using var server = await LoginServer.StartAsync((home, id) => new LoginOptions(home, id) { AttemptsPerUser = 2 });
        using var client = server.Client();

        (await LoginServer.LogInAsync(client, "rook", "not the right password at all")).Dispose();
        (await LoginServer.LogInAsync(client, "ROOK", "not the right password at all")).Dispose();

        // U+212A KELVIN SIGN: Identity's lookup would take it for K. It's no account's name, so it's refused without a lookup.
        using var kelvin = await LoginServer.LogInAsync(client, "roo\u212A", LoginServer.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, kelvin.StatusCode);
        Assert.Equal("login_failed", (await Json(kelvin)).GetProperty("code").GetString());
        Assert.False(kelvin.Headers.Contains("Set-Cookie"));

        using var limited = await LoginServer.LogInAsync(client, "rook", LoginServer.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-14")]
    public async Task ALoginBodyLongerThanAnyLoginIsRefusedUnread()
    {
        await using var server = await LoginServer.StartAsync();
        using var client = server.Client();

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "wren", password = new string('x', 9000) }, Token);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("too_large", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-14")]
    public async Task AnAccountWithARoleWolfdenDoesntKnowGetsNoCookie()
    {
        await using var server = await LoginServer.StartAsync();
        await using (var db = new Microsoft.Data.Sqlite.SqliteConnection(AccountsDbContext.ConnectionString(server.Installation.AccountsDatabase)))
        {
            await db.OpenAsync(Token);
            await using var update = db.CreateCommand();
            update.CommandText = "UPDATE AspNetUsers SET Role = 'editor' WHERE UserName = 'ash'";
            Assert.Equal(1, await update.ExecuteNonQueryAsync(Token));
        }

        using var client = server.Client();
        using var response = await LoginServer.LogInAsync(client, "ash", LoginServer.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("login_failed", (await Json(response)).GetProperty("code").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-19")]
    public async Task LoginTurnedOnWhileServingStopsItUntilARestart()
    {
        using var den = TempDen.Dracula();
        var on = false;
        var log = new StringWriter();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root)
        {
            LoginSetting = () => on, LoginSettingEvery = TimeSpan.Zero, Log = TextWriter.Synchronized(log),
        });
        await server.GetJsonAsync("/api/den", HttpStatusCode.OK);

        on = true;

        var refused = await server.GetJsonAsync("/api/den", HttpStatusCode.ServiceUnavailable);
        Assert.Equal("restart_needed", refused.GetProperty("code").GetString());
        Assert.Contains("Restart wolfden serve", refused.GetProperty("message").GetString(), StringComparison.Ordinal);
        using var page = await server.Client.GetAsync("/", Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, page.StatusCode);
        await server.GetJsonAsync("/api/health", HttpStatusCode.OK);

        // Turning it off again doesn't undo that: only a restart does.
        on = false;
        await server.GetJsonAsync("/api/den", HttpStatusCode.ServiceUnavailable);
        Assert.Contains("wolfden serve: Login was turned on. Restart wolfden serve", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-19")]
    public async Task LoginTurnedOffWhileServingStillAsksForItUntilARestart()
    {
        var on = true;
        var log = new StringWriter();
        await using var server = await LoginServer.StartAsync(log: TextWriter.Synchronized(log), loginSetting: () => on);
        using var stranger = server.Client();

        on = false;

        using var response = await stranger.GetAsync("/api/den", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("login was turned off, but this server keeps asking for it until it's restarted", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-14")]
    public async Task TooManyAttemptsForOneNameAreRefusedWhateverTheCase()
    {
        await using var server = await LoginServer.StartAsync((home, id) => new LoginOptions(home, id) { AttemptsPerAddress = 100, AttemptsPerUser = 2 });
        using var client = server.Client();

        (await LoginServer.LogInAsync(client, "wren", "not the right password at all")).Dispose();
        (await LoginServer.LogInAsync(client, "Wren", "not the right password at all")).Dispose();
        using var refused = await LoginServer.LogInAsync(client, "WREN", LoginServer.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

        // Another name is still counted on its own.
        using var other = await LoginServer.LogInAsync(client, "ash", LoginServer.Password);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task LoggingOutEndsTheSession()
    {
        await using var server = await LoginServer.StartAsync();
        var client = await server.LoggedInAsync("wren");

        using var logout = await client.PostAsync("/api/auth/logout", null, Token);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", Assert.Single(logout.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/den", Token)).StatusCode);
    }

    [Theory]
    [Trait("Scenario", "SC-ACCT-15")]
    [InlineData("reset-password")]
    [InlineData("set-role")]
    [InlineData("remove")]
    public async Task ANewPasswordANewRoleOrRemovingTheAccountEndsItsSessions(string change)
    {
        await using var server = await LoginServer.StartAsync();
        var client = await server.LoggedInAsync("ash");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/den", Token)).StatusCode);

        await using (var accounts = await server.AccountsAsync())
        {
            var result = change switch
            {
                "reset-password" => await accounts.ResetPasswordAsync("ash", "another long passphrase here"),
                "set-role" => await accounts.SetRoleAsync("ash", AccountRole.Viewer),
                _ => await accounts.RemoveAsync("ash"),
            };
            Assert.True(result.Ok, result.Message);
        }

        using var after = await client.GetAsync("/api/den", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-12")]
    public async Task AViewerReadsEverythingAndChangesNothing()
    {
        await using var server = await LoginServer.StartAsync();
        var viewer = await server.LoggedInAsync("rook");

        var status = await viewer.GetFromJsonAsync<JsonElement>("/api/auth/status", Token);
        Assert.Equal("viewer", status.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/entity?entity=count-dracula&universe=dracula", Token)).StatusCode);

        using var edit = await viewer.PostAsJsonAsync("/api/edit/lead", new { entity = "count-dracula", universe = "dracula", text = "Changed.", expectedHash = "x" }, Token);
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        var error = await Json(edit);
        Assert.Equal("forbidden", error.GetProperty("code").GetString());
        Assert.Contains("viewer", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-16")]
    public async Task AWriterFreezesButOnlyTheOwnerUnfreezesTurnsHistoryOnOrFixesDenYaml()
    {
        await using var server = await LoginServer.StartAsync();
        var writer = await server.LoggedInAsync("ash");
        var owner = await server.LoggedInAsync("wren");
        var book = new { book = "dracula", universe = "dracula", expectedHash = "x" };

        Assert.NotEqual(HttpStatusCode.Forbidden, (await writer.PostAsJsonAsync("/api/book/freeze", book, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.PostAsJsonAsync("/api/book/unfreeze", book, Token)).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/book/unfreeze", book, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.PostAsJsonAsync("/api/history/turn-on", new { }, Token)).StatusCode);

        // Edit file is a writer's, except for den.yaml, which decides how every file reads.
        var denYaml = new { path = "den.yaml", text = "name: Changed\n", expectedHash = "x" };
        using var refused = await writer.PostAsJsonAsync("/api/file/edit", denYaml, Token);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("Only an owner", (await Json(refused)).GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/file/edit", denYaml, Token)).StatusCode);
        Assert.NotEqual(
            HttpStatusCode.Forbidden,
            (await writer.PostAsJsonAsync("/api/file/edit", new { path = "universes/dracula/universe.md", text = "x", expectedHash = "x" }, Token)).StatusCode);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-17")]
    public async Task WithLoginOnAChangeMustCarryThisServersOrigin()
    {
        await using var server = await LoginServer.StartAsync();
        var owner = await server.LoggedInAsync("wren");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkpoint") { Content = JsonContent.Create(new { message = "x" }) };
        request.Headers.Remove("Origin");
        owner.DefaultRequestHeaders.Remove("Origin");
        using var response = await owner.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("origin_required", (await Json(response)).GetProperty("code").GetString());

        // A read doesn't need one.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/den", Token)).StatusCode);
    }

    [Theory]
    [Trait("Scenario", "SC-ACCT-17")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APageOnAnotherPortOfThisComputerIsRefused(bool loginOn)
    {
        await using var login = loginOn ? await LoginServer.StartAsync() : null;
        using var den = TempDen.Dracula();
        await using var plain = loginOn ? null : await Server.StartAsync(new WolfdenApiOptions(den.Root));
        var client = loginOn ? await login!.LoggedInAsync("wren") : plain!.Client;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/den");
        request.Headers.Add("Sec-Fetch-Site", "same-site");
        using var response = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("cross_site", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-17")]
    public async Task TheApisAnswersAreForItsOwnPagesOnly()
    {
        using var den = TempDen.Dracula();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        foreach (var path in new[] { "/api/den", "/api/health", "/api/no-such-thing" })
        {
            using var response = await server.Client.GetAsync(path, Token);
            Assert.Equal(["same-origin"], response.Headers.GetValues("Cross-Origin-Resource-Policy"));
        }
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-14")]
    public async Task ALoginTakesOnlyAUsernameAndAPasswordAsJson()
    {
        await using var server = await LoginServer.StartAsync();
        using var client = server.Client();

        using var form = await client.PostAsync("/api/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "wren", ["password"] = LoginServer.Password }), Token);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, form.StatusCode);

        using var extra = await client.PostAsJsonAsync("/api/auth/login", new { username = "wren", password = LoginServer.Password, role = "owner" }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);

        using var empty = await client.PostAsJsonAsync("/api/auth/login", new { username = "wren", password = "" }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }
}

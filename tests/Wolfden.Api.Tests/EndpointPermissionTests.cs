using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Wolfden.Api.Tests;

/// <summary>
/// A permission on every endpoint (#315 §4.5): Test 1 reads the endpoint
/// list and fails on any endpoint that declares neither a permission nor a
/// place on the short list that needs no login; Test 2 calls every endpoint
/// with login on, with no cookie and then as a viewer and a writer, and
/// checks it's refused before its handler runs.
/// </summary>
public class EndpointPermissionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// The only endpoints that need no login. None holds den content: the
    /// server's health, who is logged in, logging in, and the built UI's
    /// own page (which shows the login page), or the page that says how to
    /// build the UI.
    /// </summary>
    private static readonly string[] AllowAnonymous =
        ["/api/health", "/api/auth/status", "/api/auth/login", "/", "{*path:nonfile}", "/u/{**path}"];

    /// <summary>Endpoints that answer a method other than GET or HEAD but change nothing in the den, so Read is enough.</summary>
    private static readonly string[] ReadOnlyPosts = ["/api/auth/login", "/api/auth/logout", "/api/{**rest}"];

    /// <summary>What the owner alone may do (§4.6): unfreeze, and turn history on.</summary>
    private static readonly string[] OwnerOnly = ["POST /api/book/unfreeze", "POST /api/history/turn-on"];

    /// <summary>Every route endpoint of a server built with a built UI, and of one without (whose <c>/</c> says how to build it).</summary>
    private static List<RouteEndpoint> Endpoints(WolfdenApiOptions? login = null)
    {
        using var den = TempDen.Dracula();
        var webRoot = WebRoot.Built();
        try
        {
            var endpoints = new List<RouteEndpoint>();
            foreach (var root in new[] { webRoot, Path.Combine(den.Root, "no-ui-here") })
            {
                var app = WolfdenApi.Build(new WolfdenApiOptions(den.Root) { Port = 0, WebRoot = root, Login = login?.Login });
                endpoints.AddRange(((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>());
                ((IAsyncDisposable)app).DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return endpoints;
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    private static string Methods(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is { Count: > 0 } methods ? string.Join(",", methods) : "*";

    private static string Describe(RouteEndpoint endpoint) => $"{Methods(endpoint)} {endpoint.RoutePattern.RawText}";

    [Fact]
    [Trait("Scenario", "SC-ACCT-13")]
    public void Test1EveryEndpointDeclaresAPermissionOrIsOnTheShortList()
    {
        var endpoints = Endpoints();
        Assert.NotEmpty(endpoints);
        var problems = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var pattern = endpoint.RoutePattern.RawText ?? "";
            var permission = endpoint.Metadata.GetMetadata<WolfdenPermission>();
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            if (permission is null && !anonymous)
            {
                problems.Add($"{Describe(endpoint)} declares no permission: add .RequireWolfden(Permission.Read|Write|Owner).");
            }

            if (anonymous && !AllowAnonymous.Contains(pattern))
            {
                problems.Add($"{Describe(endpoint)} needs no login, but isn't on the short list that may.");
            }

            if (anonymous && permission is not null)
            {
                problems.Add($"{Describe(endpoint)} declares a permission and AllowAnonymous; AllowAnonymous wins, so pick one.");
            }

            // A request that can change something needs Write or Owner, unless it's one of the few that change nothing in the den.
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
            var changes = methods.Count == 0 || methods.Any(m => !HttpMethods.IsGet(m) && !HttpMethods.IsHead(m));
            if (changes && permission?.Permission == Permission.Read && !ReadOnlyPosts.Contains(pattern))
            {
                problems.Add($"{Describe(endpoint)} can change the den but declares only Read.");
            }

            if (OwnerOnly.Contains(Describe(endpoint)) && permission?.Permission != Permission.Owner)
            {
                problems.Add($"{Describe(endpoint)} is the owner's alone (#315 §4.6), but declares {permission?.Permission.ToString() ?? "nothing"}.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        // Every endpoint on the short list is still there, so the list doesn't keep a stale entry.
        Assert.Equal(AllowAnonymous.Order(), endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null).Select(e => e.RoutePattern.RawText!).Distinct().Order());
        Assert.All(OwnerOnly, owner => Assert.Contains(endpoints, e => Describe(e) == owner));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-13")]
    public async Task AnEndpointSomeoneForgetsNeedsALogin()
    {
        // The fallback policy: an endpoint with no authorization metadata at all still needs a Read permission.
        using var den = TempDen.Dracula();
        await using var app = WolfdenApi.Build(new WolfdenApiOptions(den.Root) { Port = 0 });
        var policy = (IAuthorizationPolicyProvider)app.Services.GetService(typeof(IAuthorizationPolicyProvider))!;
        var fallback = await policy.GetFallbackPolicyAsync();
        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, r => r is PermissionRequirement { Permission: Permission.Read });
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-13")]
    public async Task AForgottenEndpointIsRefusedWithLoginOn()
    {
        await using var server = await LoginServer.StartAsync(configure: app => app.MapGet("/api/forgotten", () => "Dracula's secrets"));
        using var anonymous = server.Client();

        using var response = await anonymous.GetAsync("/api/forgotten", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("Dracula", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);

        using var owner = await server.LoggedInAsync("wren");
        Assert.Equal("Dracula's secrets", await owner.GetStringAsync("/api/forgotten", Token));
    }

    /// <summary>A request for <paramref name="endpoint"/> with its own method, and a path that fills its parameters.</summary>
    private static HttpRequestMessage RequestFor(RouteEndpoint endpoint)
    {
        var path = (endpoint.RoutePattern.RawText ?? "/")
            .Replace("{**rest}", "no/such/endpoint", StringComparison.Ordinal)
            .Replace("{**path}", "universes/dracula/x.png", StringComparison.Ordinal);
        var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Post;
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != HttpMethods.Get && method != HttpMethods.Head)
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        return request;
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-12")]
    [Trait("Scenario", "SC-ACCT-13")]
    public async Task Test2EveryEndpointRefusesAStrangerAndEachRoleWhatItMayntDo()
    {
        await using var server = await LoginServer.StartAsync(webRoot: WebRoot.Built());
        var endpoints = ((IEndpointRouteBuilder)server.Server.App).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<WolfdenPermission>() is not null)
            .ToList();
        Assert.True(endpoints.Count > 40, $"only {endpoints.Count} endpoints declare a permission");

        using var stranger = server.Client();
        var viewer = await server.LoggedInAsync("rook");
        var writer = await server.LoggedInAsync("ash");
        var problems = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var permission = endpoint.Metadata.GetMetadata<WolfdenPermission>()!.Permission;
            await Expect(stranger, endpoint, HttpStatusCode.Unauthorized, "with no cookie");
            if (endpoint.RoutePattern.RawText == "/api/auth/logout")
            {
                // It would end the session the rest of the loop uses; LoginTests covers it.
                continue;
            }

            if (permission is Permission.Write or Permission.Owner)
            {
                await Expect(viewer, endpoint, HttpStatusCode.Forbidden, "as a viewer");
            }
            else
            {
                await ExpectNot(viewer, endpoint, "as a viewer");
            }

            if (permission is Permission.Owner)
            {
                await Expect(writer, endpoint, HttpStatusCode.Forbidden, "as a writer");
            }
            else
            {
                await ExpectNot(writer, endpoint, "as a writer");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        async Task Expect(HttpClient client, RouteEndpoint endpoint, HttpStatusCode expected, string who)
        {
            using var request = RequestFor(endpoint);
            using var response = await client.SendAsync(request, Token);
            var body = await response.Content.ReadAsStringAsync(Token);
            if (response.StatusCode != expected)
            {
                problems.Add($"{Describe(endpoint)} {who}: expected {(int)expected}, got {(int)response.StatusCode}: {body}");
            }
            else if (!body.Contains(expected == HttpStatusCode.Unauthorized ? "\"code\":\"unauthenticated\"" : "\"code\":\"forbidden\"", StringComparison.Ordinal))
            {
                problems.Add($"{Describe(endpoint)} {who}: the refusal isn't the API's error: {body}");
            }
        }

        async Task ExpectNot(HttpClient client, RouteEndpoint endpoint, string who)
        {
            using var request = RequestFor(endpoint);
            using var response = await client.SendAsync(request, Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                problems.Add($"{Describe(endpoint)} {who}: refused ({(int)response.StatusCode}) but the role allows it: {await response.Content.ReadAsStringAsync(Token)}");
            }
        }
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-12")]
    public async Task TheUiAndTheHealthCheckNeedNoLoginButTheDensImagesDo()
    {
        var webRoot = WebRoot.Built();
        await using var server = await LoginServer.StartAsync(webRoot: webRoot);
        using var stranger = server.Client();

        foreach (var path in new[] { "/", "/index.html", "/u/dracula", "/assets/app.js", "/api/health", "/api/auth/status" })
        {
            using var response = await stranger.GetAsync(path, Token);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {response.StatusCode}");
        }

        foreach (var path in new[] { "/den-files/universes/dracula/anything.png", "/api/den", "/api/no-such-thing" })
        {
            using var response = await stranger.GetAsync(path, Token);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{path}: {response.StatusCode}");
        }
    }
}

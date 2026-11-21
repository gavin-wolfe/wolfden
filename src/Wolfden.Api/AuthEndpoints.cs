using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wolfden.Accounts;

namespace Wolfden.Api;

/// <summary>Who is logged in, from <c>/api/auth/status</c>, <c>/api/auth/login</c> and <c>/api/auth/logout</c> (#315).</summary>
/// <param name="Version">The shape of this result.</param>
/// <param name="Login">True if this installation asks for a login.</param>
/// <param name="User">The logged-in account's username, or null (always null with login off).</param>
/// <param name="Role">
/// What the request may do: <c>owner</c>, <c>writer</c> or <c>viewer</c>.
/// With login off it's <c>owner</c>; with login on and nobody logged in, null.
/// The page hides edit controls from a viewer; the server's 403 is what enforces it.
/// </param>
public sealed record AuthStatus(int Version, bool Login, string? User, string? Role);

/// <summary>
/// Login (#315 §4.5): the cookie, Identity's sign-in over the installation's
/// <c>accounts.db</c>, the rate limits on logging in, and the
/// <c>/api/auth</c> endpoints.
/// </summary>
/// <remarks>
/// <para>
/// The cookie is <c>HttpOnly</c> and <c>SameSite=Strict</c>, named for the
/// installation (<see cref="LoginOptions.CookieName"/>), and signed and
/// encrypted with Data Protection keys kept in the installation's
/// <c>keys/</c> folder (protected with DPAPI on Windows). A session slides:
/// each use extends it (<see cref="LoginOptions.Session"/>).
/// </para>
/// <para>
/// Each request checks the cookie's account against <c>accounts.db</c>: an
/// account that was removed, or whose password or role changed (a new
/// security stamp), is logged out at once, and the role is the one in the
/// database now.
/// </para>
/// <para>
/// A failed login says the same thing, with the same status, whether the
/// username exists or not, and whether the account is locked; an unknown
/// name still costs a password hash, so the time taken doesn't say either.
/// </para>
/// </remarks>
internal static class AuthEndpoints
{
    /// <summary>The shape of <see cref="AuthStatus"/>.</summary>
    public const int StatusVersion = 1;

    /// <summary>The rate limiter policy on <c>POST /api/auth/login</c>, per client address.</summary>
    internal const string LoginPolicy = "wolfden-login";

    /// <summary>The paths under <c>/api</c> that answer GET here.</summary>
    public static readonly IReadOnlyList<string> GetPaths = ["auth/status"];

    /// <summary>The paths under <c>/api</c> that answer POST here.</summary>
    public static readonly IReadOnlyList<string> PostPaths = ["auth/login", "auth/logout"];

    /// <summary>What a failed login says, whatever the reason.</summary>
    internal const string LoginFailed =
        "That username and password don't match an account that can log in now. After 5 failed tries an account is locked for 15 minutes; " +
        "an owner can end that sooner with 'wolfden user unlock <name>' on this computer.";

    /// <summary>What a refused attempt says when there have been too many.</summary>
    internal const string TooManyAttempts = "Too many login attempts. Wait a minute, then try again.";

    /// <summary>
    /// Adds the login services: Data Protection, the cookie, authorization
    /// with a permission on every endpoint (and a fallback that needs a
    /// login), the login rate limit, and, with login on, the accounts.
    /// </summary>
    public static void AddWolfdenLogin(this IServiceCollection services, LoginOptions? login)
    {
        var state = new LoginState(login);
        services.AddSingleton(state);

        var protection = services.AddDataProtection().SetApplicationName("Wolfden");
        if (login is null)
        {
            // No cookie is ever issued with login off: keep no keys, anywhere.
            protection.UseEphemeralDataProtectionProvider();
        }
        else
        {
            var keys = KeysFolder(login.Installation);
            protection.PersistKeysToFileSystem(keys);
            if (OperatingSystem.IsWindows())
            {
                protection.ProtectKeysWithDpapi();
            }

            services.AddWolfdenAccounts(login.Installation).AddSignInManager();
            services.AddSingleton(new UserAttempts(login));
        }

        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, cookie =>
            {
                cookie.Cookie.Name = login?.CookieName ?? "wolfden-login-off";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                cookie.Cookie.Path = "/";
                cookie.Cookie.SecurePolicy = login?.SecureOrigin == true ? CookieSecurePolicy.Always : CookieSecurePolicy.None;
                cookie.ExpireTimeSpan = login?.Session ?? TimeSpan.FromDays(14);
                cookie.SlidingExpiration = true;
                cookie.Events.OnValidatePrincipal = Validate;

                // Never a redirect: JsonAuthorizationResults answers refusals, and these are a backstop.
                cookie.Events.OnRedirectToLogin = context => Refuse(context.Response, StatusCodes.Status401Unauthorized, Access.Unauthenticated);
                cookie.Events.OnRedirectToAccessDenied = context => Refuse(context.Response, StatusCodes.Status403Forbidden, Access.ForbiddenError(null, Permission.Read));
            });

        services.AddAuthorization(authorization =>
        {
            foreach (var permission in Enum.GetValues<Permission>())
            {
                authorization.AddPolicy(Access.PolicyName(permission), policy => policy.AddRequirements(new PermissionRequirement(permission)));
            }

            // An endpoint that declares nothing needs a login, so one someone forgets is refused, not open.
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder().AddRequirements(new PermissionRequirement(Permission.Read)).Build();
        });
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, JsonAuthorizationResults>();

        services.AddRateLimiter(limiter =>
        {
            // Per client address only when addresses mean something: behind a proxy
            // (a public origin, #315 D8). On 127.0.0.1 everyone is one address, so one
            // bucket would let anyone on this computer keep the owner from logging in;
            // the per-username limit and the lockout guard a local login instead.
            limiter.AddPolicy(LoginPolicy, context => login?.LimitsAddresses == true
                ? RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = login.AttemptsPerAddress,
                        Window = login.AttemptWindow,
                        QueueLimit = 0,
                    })
                : RateLimitPartition.GetNoLimiter("loopback"));
            limiter.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = ((int)(login?.AttemptWindow ?? TimeSpan.FromMinutes(1)).TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new ValueTask(Refuse(context.HttpContext.Response, StatusCodes.Status429TooManyRequests, new ApiError("too_many_attempts", TooManyAttempts)));
            };
        });
    }

    /// <summary>
    /// The installation's <c>keys/</c> folder, created if it isn't there:
    /// outside Windows, readable only by this computer account (0700).
    /// </summary>
    internal static DirectoryInfo KeysFolder(InstallationFolder installation)
    {
        installation.Create();
        var path = installation.KeysFolder;
        if (OperatingSystem.IsWindows())
        {
            return Directory.CreateDirectory(path);
        }

        // Wolfden's own folder, so it's made private even if it was there already, keys and all.
        var folder = Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var key in Directory.EnumerateFiles(path))
        {
            File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return folder;
    }

    /// <summary>Maps <c>/api/auth/status</c>, <c>/api/auth/login</c> and <c>/api/auth/logout</c>.</summary>
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/auth/status", (HttpContext c) => Results.Json(Status(c))).AllowAnonymous();
        api.MapPost("/auth/login", (Func<HttpContext, Task<IResult>>)Login).AllowAnonymous().RequireRateLimiting(LoginPolicy);
        api.MapPost("/auth/logout", (Func<HttpContext, Task<IResult>>)Logout).RequireWolfden(Permission.Read);
    }

    /// <summary>Who the request is: with login off, nobody, as the owner.</summary>
    internal static AuthStatus Status(HttpContext context)
    {
        if (!LoginState.Of(context).On)
        {
            return new AuthStatus(StatusVersion, false, null, AccountRole.Owner.Name());
        }

        var role = Access.RoleOf(context.User);
        return role is null
            ? new AuthStatus(StatusVersion, true, null, null)
            : new AuthStatus(StatusVersion, true, context.User.Identity?.Name, role.Value.Name());
    }

    private static async Task<IResult> Login(HttpContext context)
    {
        var state = LoginState.Of(context);
        if (!state.On)
        {
            return Error(StatusCodes.Status409Conflict, "login_off", "Login is off for this installation, so there's nothing to log in to: the pages work without it.");
        }

        if (!context.Request.HasJsonContentType())
        {
            return Error(StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type", "'/api/auth/login' takes a JSON body: send it with 'Content-Type: application/json'.");
        }

        // Read no more than a login can need, before any limit or hash.
        if (context.Request.ContentLength > MaxLoginBody)
        {
            return TooLarge();
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
        {
            size.MaxRequestBodySize = MaxLoginBody;
        }

        string userName;
        string password;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
            (userName, password) = Credentials(document.RootElement);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }
        catch (JsonException)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_argument", "The body should be JSON: { \"username\": \"…\", \"password\": \"…\" }.");
        }
        catch (FormatException e)
        {
            return Error(StatusCodes.Status400BadRequest, "bad_argument", e.Message);
        }

        var users = context.RequestServices.GetRequiredService<UserManager<WolfdenUser>>();
        var signIn = context.RequestServices.GetRequiredService<SignInManager<WolfdenUser>>();

        // A name no account can have takes no one's slot, and costs a hash like any other failure.
        if (UserNameRules.Problem(userName) is not null)
        {
            SpendAHash(users, password);
            return Failed();
        }

        // Per username, from any address, counted under the name Identity looks it up by.
        using var lease = context.RequestServices.GetRequiredService<UserAttempts>().Acquire(users.NormalizeName(userName));
        if (!lease.IsAcquired)
        {
            context.Response.Headers.RetryAfter = ((int)state.Options!.AttemptWindow.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Error(StatusCodes.Status429TooManyRequests, "too_many_attempts", TooManyAttempts);
        }

        var user = await users.FindByNameAsync(userName).ConfigureAwait(false);
        if (user is null)
        {
            // As long as a real check takes, so the time doesn't say the name is unknown.
            SpendAHash(users, password);
            return Failed();
        }

        // The password (and lockout) first, with no cookie; then a cookie only for an account with a role Wolfden knows.
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut || result.IsNotAllowed)
            {
                // A locked account isn't checked; spend the same time anyway.
                SpendAHash(users, password);
            }

            return Failed();
        }

        if (!AccountRoles.TryParse(user.Role, out var role))
        {
            return Failed();
        }

        await signIn.SignInAsync(user, isPersistent: true).ConfigureAwait(false);
        return Results.Json(new AuthStatus(StatusVersion, true, user.UserName, role.Name()));
    }

    private static async Task<IResult> Logout(HttpContext context)
    {
        if (LoginState.Of(context).On)
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false);
        }

        return Results.Json(LoginState.Of(context).On
            ? new AuthStatus(StatusVersion, true, null, null)
            : new AuthStatus(StatusVersion, false, null, AccountRole.Owner.Name()));
    }

    /// <summary>The body's username and password: one JSON object with exactly those two strings.</summary>
    private static (string UserName, string Password) Credentials(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The body should be one JSON object: { \"username\": \"…\", \"password\": \"…\" }.");
        }

        string? userName = null;
        string? password = null;
        foreach (var member in body.EnumerateObject())
        {
            switch (member.Name)
            {
                case "username" when userName is null && member.Value.ValueKind == JsonValueKind.String:
                    userName = member.Value.GetString();
                    break;
                case "password" when password is null && member.Value.ValueKind == JsonValueKind.String:
                    password = member.Value.GetString();
                    break;
                default:
                    throw new FormatException($"'/api/auth/login' takes 'username' and 'password', each once, as text, not '{member.Name}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            throw new FormatException("Give a username and a password.");
        }

        // Longer than any username or password an account can have: no use hashing it.
        if (userName.Length > UserNameRules.MaximumLength * 4 || password.Length > PasswordRules.MaximumLength * 4)
        {
            throw new FormatException("That username or password is longer than any account's.");
        }

        return (userName.Trim(), password);
    }

    private static IResult Failed() => Error(StatusCodes.Status401Unauthorized, "login_failed", LoginFailed);

    /// <summary>The most a login's body may hold: a username and a password, with room to spare.</summary>
    internal const int MaxLoginBody = 8 * 1024;

    private static IResult TooLarge() =>
        Error(StatusCodes.Status413PayloadTooLarge, "too_large", "A login is a username and a password: that body is far longer than one.");

    /// <summary>Hashes <paramref name="password"/> against a made-up account, for the time it takes.</summary>
    private static void SpendAHash(UserManager<WolfdenUser> users, string password)
    {
        var nobody = new WolfdenUser { Id = Guid.Empty, UserName = "" };
        var hash = Decoy ??= users.PasswordHasher.HashPassword(nobody, "not a password: only here to take as long as one");
        users.PasswordHasher.VerifyHashedPassword(nobody, hash, password);
    }

    private static string? Decoy;

    /// <summary>
    /// Checks a cookie's account on each request (#315): by its id, parsed
    /// as a Guid, it must still exist with the same security stamp, so a
    /// removed account, a new password or a new role ends the session. The
    /// role in the principal is the database's.
    /// </summary>
    private static async Task Validate(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (!LoginState.Of(context.HttpContext).On || principal is null)
        {
            await Reject(context).ConfigureAwait(false);
            return;
        }

        var services = context.HttpContext.RequestServices;
        var options = services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity;
        var users = services.GetRequiredService<UserManager<WolfdenUser>>();
        var id = principal.FindFirstValue(options.UserIdClaimType);
        var stamp = principal.FindFirstValue(options.SecurityStampClaimType);
        WolfdenUser? user = null;
        if (Guid.TryParse(id, out var key))
        {
            user = await users.FindByIdAsync(key.ToString()).ConfigureAwait(false);
        }

        if (user is null || user.Id != key || stamp is null || !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal))
        {
            await Reject(context).ConfigureAwait(false);
            return;
        }

        var role = AccountRoles.TryParse(user.Role, out var parsed) ? parsed.Name() : null;
        if (principal.FindFirstValue(options.RoleClaimType) != role)
        {
            var factory = services.GetRequiredService<IUserClaimsPrincipalFactory<WolfdenUser>>();
            context.ReplacePrincipal(await factory.CreateAsync(user).ConfigureAwait(false));
        }

        static async Task Reject(CookieValidatePrincipalContext context)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false);
        }
    }

    private static Task Refuse(HttpResponse response, int status, ApiError error)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(error);
    }

    private static IResult Error(int status, string code, string message) => Results.Json(new ApiError(code, message), statusCode: status);

    /// <summary>The per-username login limit (#315 §7: 10 a minute), with the built-in limiter, kept in memory only.</summary>
    internal sealed class UserAttempts(LoginOptions login) : IDisposable
    {
        private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(name => RateLimitPartition.GetFixedWindowLimiter(
            name,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = login.AttemptsPerUser, Window = login.AttemptWindow, QueueLimit = 0 }));

        /// <summary>One attempt for the account named <paramref name="normalizedName"/>, as Identity's lookup normalizer gives it.</summary>
        public RateLimitLease Acquire(string normalizedName) => _limiter.AttemptAcquire(normalizedName);

        public void Dispose() => _limiter.Dispose();
    }
}

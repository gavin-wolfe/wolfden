using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Wolfden.Accounts;

namespace Wolfden.Api;

/// <summary>
/// What an endpoint lets an account do (#315 §4.5, §4.6). Every endpoint
/// declares one with <see cref="Access.RequireWolfden{TBuilder}"/>, or is
/// on the short list that needs none (<c>AllowAnonymous</c>).
/// </summary>
public enum Permission
{
    /// <summary>Read the den: pages, search, history, diffs, images. Every role has it.</summary>
    Read,

    /// <summary>Change the den: edit and create notes, series, books and chapters, add images, checkpoint, freeze. Owners and writers.</summary>
    Write,

    /// <summary>Owner only: unfreeze a book, turn history on, fix a <c>den.yaml</c> that won't load.</summary>
    Owner,
}

/// <summary>The permission an endpoint declared, as endpoint metadata, so a test can list every endpoint's.</summary>
/// <param name="Permission">The permission.</param>
public sealed record WolfdenPermission(Permission Permission);

/// <summary>
/// Who may do what (#315). With login off, everyone is the owner, as before
/// accounts. With login on, an account's role decides: an owner may do
/// everything, a writer reads and writes, and a viewer only reads (D6).
/// </summary>
public static class Access
{
    /// <summary>
    /// Declares the permission an endpoint needs. It adds the
    /// <see cref="WolfdenPermission"/> metadata and the matching policy.
    /// </summary>
    public static TBuilder RequireWolfden<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new WolfdenPermission(permission)).RequireAuthorization(PolicyName(permission));
    }

    /// <summary>The policy's name for <paramref name="permission"/>.</summary>
    internal static string PolicyName(Permission permission) => "wolfden-" + permission.ToString().ToLowerInvariant();

    /// <summary>True if an account with <paramref name="role"/> has <paramref name="permission"/>.</summary>
    public static bool Allows(AccountRole role, Permission permission) => permission switch
    {
        Permission.Read => true,
        Permission.Write => role is AccountRole.Owner or AccountRole.Writer,
        Permission.Owner => role is AccountRole.Owner,
        _ => false,
    };

    /// <summary>
    /// The role of the request's account: <see cref="AccountRole.Owner"/>
    /// with login off, the logged-in account's role with it on, or null
    /// when nobody is logged in (or the account's role isn't one Wolfden knows).
    /// </summary>
    public static AccountRole? RoleOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!LoginState.Of(context).On)
        {
            return AccountRole.Owner;
        }

        return RoleOf(context.User);
    }

    /// <summary>The role in a logged-in principal's role claim, or null.</summary>
    internal static AccountRole? RoleOf(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && AccountRoles.TryParse(user.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;

    /// <summary>True if the request's account has <paramref name="permission"/>; always, with login off.</summary>
    public static bool Allows(HttpContext context, Permission permission) =>
        RoleOf(context) is { } role && Allows(role, permission);

    /// <summary>
    /// The 403 for an account that lacks <paramref name="permission"/>, in
    /// words that say what its role allows.
    /// </summary>
    internal static IResult Forbidden(HttpContext context, Permission permission) =>
        Results.Json(ForbiddenError(RoleOf(context), permission), statusCode: StatusCodes.Status403Forbidden);

    internal static ApiError ForbiddenError(AccountRole? role, Permission permission) => new(
        "forbidden",
        (role, permission) switch
        {
            (AccountRole.Viewer, _) => "Your account is a viewer: it can read this den but not change it. An owner can change its role with 'wolfden user set-role'.",
            (_, Permission.Owner) => "Only an owner can do this. An owner can do it, or change your account's role with 'wolfden user set-role'.",
            _ => "Your account can't do this.",
        });

    /// <summary>The 401 for a request with no login, when login is on.</summary>
    internal static readonly ApiError Unauthenticated = new(
        "unauthenticated", "Log in to use Wolfden: this installation asks for a login. If your session ended, log in again.");
}

/// <summary>An endpoint's permission, as an authorization requirement.</summary>
internal sealed class PermissionRequirement(Permission permission) : IAuthorizationRequirement
{
    public Permission Permission { get; } = permission;
}

/// <summary>
/// Meets a <see cref="PermissionRequirement"/>: always with login off (the
/// implicit owner), and with login on when the logged-in account's role
/// allows it.
/// </summary>
internal sealed class PermissionHandler(LoginState login) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!login.On || (Access.RoleOf(context.User) is { } role && Access.Allows(role, requirement.Permission)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Answers a refused request with the API's JSON error rather than a
/// redirect: 401 <c>unauthenticated</c> when nobody is logged in, and 403
/// <c>forbidden</c> when the account's role doesn't allow it.
/// </summary>
internal sealed class JsonAuthorizationResults : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            return _default.HandleAsync(next, context, policy, authorizeResult);
        }

        var role = Access.RoleOf(context.User);
        if (role is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsJsonAsync(Access.Unauthenticated);
        }

        var permission = context.GetEndpoint()?.Metadata.GetMetadata<WolfdenPermission>()?.Permission ?? Permission.Read;
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(Access.ForbiddenError(role, permission));
    }
}

/// <summary>Whether this server asks for a login: the server's <see cref="LoginOptions"/>, or null for off.</summary>
internal sealed class LoginState(LoginOptions? options)
{
    public LoginOptions? Options { get; } = options;

    /// <summary>True if login is on.</summary>
    public bool On => Options is not null;

    /// <summary>The state of the server answering <paramref name="context"/>.</summary>
    public static LoginState Of(HttpContext context) =>
        context.RequestServices.GetService(typeof(LoginState)) as LoginState ?? Off;

    /// <summary>Login off.</summary>
    public static readonly LoginState Off = new(null);
}

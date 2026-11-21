using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Wolfden.Accounts;

/// <summary>
/// Identity's claims for a logged-in account, plus its role (#315): the
/// <c>Role</c> column becomes one role claim, in the role's word
/// (<c>owner</c>, <c>writer</c> or <c>viewer</c>). An unknown word in the
/// column gives no role claim, so the account may do nothing.
/// </summary>
/// <remarks>
/// The id claim (<see cref="ClaimTypes.NameIdentifier"/>) is the account's
/// Guid in lower-case "D" form. Code that reads it parses it with
/// <see cref="Guid.Parse(string)"/> and never compares the text.
/// </remarks>
public sealed class WolfdenClaimsPrincipalFactory(UserManager<WolfdenUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<WolfdenUser>(users, options)
{
    /// <inheritdoc/>
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(WolfdenUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = await base.GenerateClaimsAsync(user).ConfigureAwait(false);
        if (AccountRoles.TryParse(user.Role, out var role))
        {
            identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, role.Name()));
        }

        return identity;
    }
}

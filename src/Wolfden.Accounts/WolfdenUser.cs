using Microsoft.AspNetCore.Identity;

namespace Wolfden.Accounts;

/// <summary>
/// What an account may do (#315 D6). A role says what you may change; the
/// Reader's view is a filter on what you see, and isn't a role.
/// </summary>
public enum AccountRole
{
    /// <summary>Everything, including unfreezing, turning history on, and (later) account settings.</summary>
    Owner,

    /// <summary>Reads and edits notes, checkpoints, freezes.</summary>
    Writer,

    /// <summary>Reads everything a writer reads, spoilers included, and changes nothing.</summary>
    Viewer,
}

/// <summary>Reading and writing <see cref="AccountRole"/> as the words writers type.</summary>
public static class AccountRoles
{
    /// <summary>The roles, as typed: owner, writer, viewer.</summary>
    public static IReadOnlyList<string> Names { get; } = ["owner", "writer", "viewer"];

    /// <summary>The role's word: <c>owner</c>, <c>writer</c> or <c>viewer</c>.</summary>
    public static string Name(this AccountRole role) => role switch
    {
        AccountRole.Owner => "owner",
        AccountRole.Writer => "writer",
        AccountRole.Viewer => "viewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    /// <summary>Reads a role's word, ignoring case. False for anything else.</summary>
    public static bool TryParse(string? text, out AccountRole role)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "owner":
                role = AccountRole.Owner;
                return true;
            case "writer":
                role = AccountRole.Writer;
                return true;
            case "viewer":
                role = AccountRole.Viewer;
                return true;
            default:
                role = default;
                return false;
        }
    }
}

/// <summary>
/// An account in <c>accounts.db</c>. Identity's user, with a role and
/// without email or phone: <see cref="AccountsDbContext"/> leaves those
/// columns out of the database, so no address is ever stored (#315 §4.3).
/// </summary>
public sealed class WolfdenUser : IdentityUser<Guid>
{
    /// <summary>The role's word, as <see cref="AccountRoles.Name"/> writes it. One role per account.</summary>
    public string Role { get; set; } = AccountRole.Viewer.Name();
}

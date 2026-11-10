using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Wolfden.Accounts;

/// <summary>One account as the CLI lists it.</summary>
/// <param name="UserName">The username, as it was given.</param>
/// <param name="Role">What the account may do.</param>
/// <param name="LockedUntil">When a lockout after failed logins ends, or null if it isn't locked.</param>
public sealed record Account(string UserName, AccountRole Role, DateTimeOffset? LockedUntil);

/// <summary>The outcome of a change to the accounts.</summary>
/// <param name="Ok">True if the change was made (or there was nothing to change).</param>
/// <param name="Code">Why it wasn't, as a stable code: <c>unknown_user</c>, <c>user_exists</c>, <c>bad_username</c>, <c>bad_password</c>, <c>last_owner</c>; null when it was.</param>
/// <param name="Message">What happened, in a sentence, without the command's name.</param>
/// <param name="Account">The account as it is now, or null if there's none.</param>
public sealed record AccountResult(bool Ok, string? Code, string Message, Account? Account)
{
    internal static AccountResult Done(string message, Account? account) => new(true, null, message, account);

    internal static AccountResult Refused(string code, string message) => new(false, code, message, null);
}

/// <summary>
/// The accounts of one installation, in its <c>accounts.db</c>, through
/// Identity's stores and Wolfden's rules (<see cref="AccountsServices"/>).
/// Every change that could leave no owner is refused, so nobody can lock
/// themselves out of an installation with login on (#315 §4.4).
/// </summary>
public sealed class AccountStore : IAsyncDisposable, IDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;

    private AccountStore(InstallationFolder folder)
    {
        Folder = folder;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWolfdenAccounts(folder);
        _services = services.BuildServiceProvider();
        _scope = _services.CreateAsyncScope();
    }

    /// <summary>The installation these accounts belong to.</summary>
    public InstallationFolder Folder { get; }

    /// <summary>Identity's user manager, for tests that act as the web's login will (failed attempts, checking a password).</summary>
    internal UserManager<WolfdenUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<WolfdenUser>>();

    private AccountsDbContext Db => _scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

    /// <summary>
    /// Opens the installation's accounts, creating the folder and
    /// <c>accounts.db</c> if they aren't there, and bringing an older
    /// database's schema up to date.
    /// </summary>
    public static async Task<AccountStore> OpenAsync(InstallationFolder folder, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        folder.Create();
        var store = new AccountStore(folder);
        try
        {
            await store.Db.Database.MigrateAsync(cancel).ConfigureAwait(false);
            return store;
        }
        catch
        {
            await store.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Opens the accounts if <c>accounts.db</c> exists, or returns null without creating anything.</summary>
    public static async Task<AccountStore?> OpenExistingAsync(InstallationFolder folder, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return File.Exists(folder.AccountsDatabase) ? await OpenAsync(folder, cancel).ConfigureAwait(false) : null;
    }

    /// <summary>Every account, by username.</summary>
    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancel = default)
    {
        var users = await Db.Users.AsNoTracking().ToListAsync(cancel).ConfigureAwait(false);
        return users.Select(ToAccount).OrderBy(a => a.UserName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>How many accounts are owners.</summary>
    public Task<int> OwnerCountAsync(CancellationToken cancel = default)
    {
        var owner = AccountRole.Owner.Name();
        return Db.Users.CountAsync(u => u.Role == owner, cancel);
    }

    /// <summary>Adds an account. Refused for a username that's taken (in any case) or breaks the rules, or a password that does.</summary>
    public async Task<AccountResult> AddAsync(string userName, string password, AccountRole role)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);
        if (UserNameRules.Problem(userName) is { } nameProblem)
        {
            return AccountResult.Refused("bad_username", nameProblem);
        }

        if (await Users.FindByNameAsync(userName).ConfigureAwait(false) is { } existing)
        {
            return AccountResult.Refused("user_exists", $"there's already an account named '{existing.UserName}'.");
        }

        if (PasswordRules.Problem(userName, password) is { } passwordProblem)
        {
            return AccountResult.Refused(PasswordRules.ErrorCode, passwordProblem);
        }

        var user = new WolfdenUser { UserName = userName, Role = role.Name() };
        var created = await Users.CreateAsync(user, password).ConfigureAwait(false);
        return created.Succeeded
            ? AccountResult.Done($"added '{userName}' as {Article(role)} {role.Name()}.", ToAccount(user))
            : Failed(created);
    }

    /// <summary>Removes an account. Refused for the last owner.</summary>
    public async Task<AccountResult> RemoveAsync(string userName)
    {
        await using var transaction = await Db.Database.BeginTransactionAsync().ConfigureAwait(false);
        if (await FindAsync(userName).ConfigureAwait(false) is not { } user)
        {
            return Unknown(userName);
        }

        if (await IsLastOwnerAsync(user).ConfigureAwait(false))
        {
            return AccountResult.Refused("last_owner", $"'{user.UserName}' is the only owner, and an installation needs one. Make another account an owner first ('wolfden user set-role <name> owner').");
        }

        var deleted = await Users.DeleteAsync(user).ConfigureAwait(false);
        if (!deleted.Succeeded)
        {
            return Failed(deleted);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        return AccountResult.Done($"removed '{user.UserName}'.", null);
    }

    /// <summary>Changes an account's role. Refused when it would leave no owner. A new role ends the account's sessions.</summary>
    public async Task<AccountResult> SetRoleAsync(string userName, AccountRole role)
    {
        await using var transaction = await Db.Database.BeginTransactionAsync().ConfigureAwait(false);
        if (await FindAsync(userName).ConfigureAwait(false) is not { } user)
        {
            return Unknown(userName);
        }

        if (user.Role == role.Name())
        {
            return AccountResult.Done($"'{user.UserName}' is already {Article(role)} {role.Name()}.", ToAccount(user));
        }

        if (role != AccountRole.Owner && await IsLastOwnerAsync(user).ConfigureAwait(false))
        {
            return AccountResult.Refused("last_owner", $"'{user.UserName}' is the only owner, and an installation needs one. Make another account an owner first.");
        }

        user.Role = role.Name();

        // A new security stamp ends sessions made under the old role.
        var updated = await Users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
        if (!updated.Succeeded)
        {
            return Failed(updated);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        return AccountResult.Done($"'{user.UserName}' is now {Article(role)} {role.Name()}.", ToAccount(user));
    }

    /// <summary>
    /// Gives an account a new password. It ends every session the account
    /// has (a new security stamp), and ends a lockout, since whoever can run
    /// this can unlock it anyway.
    /// </summary>
    public async Task<AccountResult> ResetPasswordAsync(string userName, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (await FindAsync(userName).ConfigureAwait(false) is not { } user)
        {
            return Unknown(userName);
        }

        if (PasswordRules.Problem(user.UserName, password) is { } problem)
        {
            return AccountResult.Refused(PasswordRules.ErrorCode, problem);
        }

        await using var transaction = await Db.Database.BeginTransactionAsync().ConfigureAwait(false);
        user.PasswordHash = Users.PasswordHasher.HashPassword(user, password);
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        var updated = await Users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
        if (!updated.Succeeded)
        {
            return Failed(updated);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        return AccountResult.Done($"set a new password for '{user.UserName}'. Anywhere it was logged in, it's logged out.", ToAccount(user));
    }

    /// <summary>Ends a lockout after failed logins, and clears the count of failures.</summary>
    public async Task<AccountResult> UnlockAsync(string userName)
    {
        if (await FindAsync(userName).ConfigureAwait(false) is not { } user)
        {
            return Unknown(userName);
        }

        var wasLocked = LockedUntil(user) is not null;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        var updated = await Users.UpdateAsync(user).ConfigureAwait(false);
        if (!updated.Succeeded)
        {
            return Failed(updated);
        }

        return AccountResult.Done(
            wasLocked ? $"unlocked '{user.UserName}'; it can log in again." : $"'{user.UserName}' wasn't locked; its count of failed logins is back to 0.",
            ToAccount(user));
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync().ConfigureAwait(false);
        await _services.DisposeAsync().ConfigureAwait(false);
    }

    private Task<WolfdenUser?> FindAsync(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);
        return Users.FindByNameAsync(userName);
    }

    private async Task<bool> IsLastOwnerAsync(WolfdenUser user) =>
        user.Role == AccountRole.Owner.Name() && await OwnerCountAsync().ConfigureAwait(false) <= 1;

    private static Account ToAccount(WolfdenUser user) =>
        new(user.UserName ?? string.Empty, AccountRoles.TryParse(user.Role, out var role) ? role : AccountRole.Viewer, LockedUntil(user));

    private static DateTimeOffset? LockedUntil(WolfdenUser user) =>
        user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow ? end : null;

    private static AccountResult Unknown(string userName) =>
        AccountResult.Refused("unknown_user", $"there's no account named '{userName}'. 'wolfden user list' lists them.");

    private static AccountResult Failed(IdentityResult result)
    {
        var error = result.Errors.FirstOrDefault();
        return error?.Code switch
        {
            PasswordRules.ErrorCode => AccountResult.Refused(PasswordRules.ErrorCode, error.Description),
            nameof(IdentityErrorDescriber.DuplicateUserName) => AccountResult.Refused("user_exists", "there's already an account with that name."),
            nameof(IdentityErrorDescriber.InvalidUserName) => AccountResult.Refused("bad_username", "use letters, digits, '.', '-' and '_' in a username, with no spaces."),
            _ => throw new InvalidOperationException($"The accounts database refused the change: {string.Join(" ", result.Errors.Select(e => e.Description))}"),
        };
    }

    private static string Article(AccountRole role) => role == AccountRole.Owner ? "an" : "a";
}

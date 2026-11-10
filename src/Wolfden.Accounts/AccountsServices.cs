using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Wolfden.Accounts;

/// <summary>
/// Registers an installation's accounts: Identity over
/// <c>accounts.db</c>, with Wolfden's password, username and lockout rules.
/// The CLI's <see cref="AccountStore"/> uses it; the web's login is to add
/// <c>AddSignInManager</c> and a cookie on top of the same call.
/// </summary>
public static class AccountsServices
{
    /// <summary>Failed logins before an account is locked (#315 §7).</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>How long a lockout lasts (#315 §7). <c>wolfden user unlock</c> ends it sooner.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Adds the accounts of <paramref name="folder"/>, and returns Identity's builder for more.</summary>
    public static IdentityBuilder AddWolfdenAccounts(this IServiceCollection services, InstallationFolder folder)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(folder);
        services.AddDbContext<AccountsDbContext>(o => o.UseSqlite(AccountsDbContext.ConnectionString(folder.AccountsDatabase)));
        var builder = services
            .AddIdentityCore<WolfdenUser>(Configure)
            .AddEntityFrameworkStores<AccountsDbContext>();

        // Only Wolfden's password rules: Identity's own validator would add
        // composition rules and count UTF-16 units, not characters.
        services.RemoveAll<IPasswordValidator<WolfdenUser>>();
        services.AddScoped<IPasswordValidator<WolfdenUser>, PasswordRules>();
        services.Replace(ServiceDescriptor.Scoped<IPasswordHasher<WolfdenUser>, NormalizingPasswordHasher>());
        return builder;
    }

    /// <summary>Wolfden's Identity options: no composition rules, lockout after 5 failures for 15 minutes, no email.</summary>
    public static void Configure(IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // PasswordRules does the length, in code points; these are Identity's own checks, off.
        options.Password.RequiredLength = PasswordRules.MinimumLength;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = MaxFailedAttempts;
        options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;

        options.User.RequireUniqueEmail = false;
        options.User.AllowedUserNameCharacters = UserNameRules.AllowedCharacters;
        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedPhoneNumber = false;
        options.SignIn.RequireConfirmedAccount = false;
    }
}

using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Wolfden.Accounts.Tests;

/// <summary>Accounts in an installation's accounts.db (#315 §4.3, §4.4, §4.6, §7).</summary>
public class AccountStoreTests
{
    private const string Password = "correct horse battery";
    private const string Other = "a quiet moon rises";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Scenario", "SC-ACCT-2")]
    public async Task AnAccountIsAddedAndListedInTheInstallationNotADen()
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);

        var added = await store.AddAsync("wren", Password, AccountRole.Owner);
        var viewer = await store.AddAsync("Rook", Password, AccountRole.Viewer);

        Assert.True(added.Ok, added.Message);
        Assert.Equal("added 'wren' as an owner.", added.Message);
        Assert.Equal("added 'Rook' as a viewer.", viewer.Message);
        Assert.Equal(
            [new Account("Rook", AccountRole.Viewer, null), new Account("wren", AccountRole.Owner, null)],
            await store.ListAsync(Token));
        Assert.True(File.Exists(Path.Combine(temp.Folder.Path, "accounts.db")));
        Assert.True(await store.Users.CheckPasswordAsync((await store.Users.FindByNameAsync("WREN"))!, Password));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-2")]
    public async Task NoEmailOrPhoneColumnIsStored()
    {
        using var temp = new TempHome();
        await using (var store = await AccountStore.OpenAsync(temp.Folder, Token))
        {
            await store.AddAsync("wren", Password, AccountRole.Owner);
        }

        using var connection = new SqliteConnection(AccountsDbContext.ConnectionString(temp.Folder.AccountsDatabase));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('AspNetUsers')";
        var columns = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }
        }

        Assert.Contains("Role", columns);
        Assert.Contains("PasswordHash", columns);
        Assert.DoesNotContain(columns, c => c.Contains("Email", StringComparison.OrdinalIgnoreCase) || c.Contains("Phone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheSchemaMatchesItsMigrations()
    {
        // accounts.db isn't rebuildable, so every model change needs a migration.
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);
        using var scope = BuildScope(temp.Folder);
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Token));
        Assert.False(db.Database.HasPendingModelChanges(), "The accounts model changed without a migration: run 'dotnet ef migrations add <Name> --project src/Wolfden.Accounts'.");
    }

    [Fact]
    public async Task ANameIsTakenWhateverItsCase()
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);
        await store.AddAsync("wren", Password, AccountRole.Owner);

        var again = await store.AddAsync("Wren", Password, AccountRole.Writer);

        Assert.False(again.Ok);
        Assert.Equal("user_exists", again.Code);
        Assert.Equal("there's already an account named 'wren'.", again.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wren calder")]
    [InlineData("wren@example.com")]
    [InlineData("zoë")]
    public async Task ANameOutsideTheRulesIsRefused(string name)
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);

        var result = await store.AddAsync(name, Password, AccountRole.Owner);

        Assert.Equal("bad_username", result.Code);
        Assert.Empty(await store.ListAsync(Token));
    }

    [Theory]
    [Trait("Scenario", "SC-ACCT-3")]
    [InlineData("elevenchars", "at least 12 characters, and this one has 11")]
    [InlineData("aaaaaaaaaaaaaa", "one character repeated")]
    [InlineData("WrenCalder99", "can't be the username")]
    public async Task APasswordOutsideTheRulesIsRefused(string password, string expected)
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);

        var result = await store.AddAsync("wrencalder99", password, AccountRole.Owner);

        Assert.Equal("bad_password", result.Code);
        Assert.Contains(expected, result.Message, StringComparison.Ordinal);
        Assert.Empty(await store.ListAsync(Token));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-3")]
    public void LengthCountsCharactersFrom12To256WithNoCompositionRules()
    {
        Assert.Null(PasswordRules.Problem("wren", "twelve chars"));
        Assert.Null(PasswordRules.Problem("wren", "alllowercase"));
        Assert.Null(PasswordRules.Problem("wren", new string('a', 255) + "b"));
        Assert.Contains("at most 256", PasswordRules.Problem("wren", new string('a', 256) + "b"), StringComparison.Ordinal);

        // Eleven emoji are 22 UTF-16 units but 11 characters (NIST: each code point counts once).
        Assert.Contains("has 11", PasswordRules.Problem("wren", string.Concat(Enumerable.Repeat("🐺", 10)) + "🌙"), StringComparison.Ordinal);
        Assert.Null(PasswordRules.Problem("wren", string.Concat(Enumerable.Repeat("🐺", 11)) + "🌙"));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-3")]
    public async Task APasswordMatchesWhicheverWayItsAccentsWereTyped()
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);
        var composed = "café au lait du matin";
        var decomposed = "café au lait du matin";
        Assert.NotEqual(composed, decomposed);

        await store.AddAsync("wren", composed, AccountRole.Owner);
        var user = (await store.Users.FindByNameAsync("wren"))!;

        Assert.True(await store.Users.CheckPasswordAsync(user, decomposed));
        Assert.True(await store.Users.CheckPasswordAsync(user, composed));
        Assert.False(await store.Users.CheckPasswordAsync(user, "cafe au lait du matin"));
        Assert.Equal(composed.Normalize(NormalizationForm.FormC), PasswordRules.Normalize(decomposed));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-4")]
    public async Task TheLastOwnerCantBeRemovedOrDemoted()
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);
        await store.AddAsync("wren", Password, AccountRole.Owner);
        await store.AddAsync("ash", Password, AccountRole.Writer);

        var remove = await store.RemoveAsync("wren");
        var demote = await store.SetRoleAsync("WREN", AccountRole.Writer);

        Assert.Equal("last_owner", remove.Code);
        Assert.Equal("last_owner", demote.Code);
        Assert.Contains("only owner", remove.Message, StringComparison.Ordinal);
        Assert.Equal(1, await store.OwnerCountAsync(Token));

        // With a second owner, either can go.
        Assert.True((await store.SetRoleAsync("ash", AccountRole.Owner)).Ok);
        Assert.True((await store.SetRoleAsync("wren", AccountRole.Viewer)).Ok);
        Assert.True((await store.RemoveAsync("wren")).Ok);
        Assert.Equal([new Account("ash", AccountRole.Owner, null)], await store.ListAsync(Token));
    }

    [Fact]
    public async Task AnUnknownAccountIsSaidPlainly()
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);

        foreach (var result in new[]
        {
            await store.RemoveAsync("nobody"),
            await store.SetRoleAsync("nobody", AccountRole.Writer),
            await store.ResetPasswordAsync("nobody", Password),
            await store.UnlockAsync("nobody"),
        })
        {
            Assert.Equal("unknown_user", result.Code);
            Assert.StartsWith("there's no account named 'nobody'.", result.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-5")]
    public async Task FiveFailuresLockAnAccountForFifteenMinutesAndUnlockEndsIt()
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);
        await store.AddAsync("wren", Password, AccountRole.Owner);
        var user = (await store.Users.FindByNameAsync("wren"))!;

        for (var i = 0; i < 4; i++)
        {
            await store.Users.AccessFailedAsync(user);
        }

        Assert.False(await store.Users.IsLockedOutAsync(user));
        await store.Users.AccessFailedAsync(user);
        Assert.True(await store.Users.IsLockedOutAsync(user));
        var locked = Assert.Single(await store.ListAsync(Token)).LockedUntil;
        Assert.NotNull(locked);
        Assert.InRange(locked.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));

        var unlocked = await store.UnlockAsync("wren");

        Assert.Equal("unlocked 'wren'; it can log in again.", unlocked.Message);
        Assert.Null(Assert.Single(await store.ListAsync(Token)).LockedUntil);
        Assert.Equal(0, (await store.Users.FindByNameAsync("wren"))!.AccessFailedCount);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-5")]
    public async Task ANewPasswordEndsSessionsAndALockout()
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);
        await store.AddAsync("wren", Password, AccountRole.Owner);
        var user = (await store.Users.FindByNameAsync("wren"))!;
        var stamp = user.SecurityStamp;
        await store.Users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.Equal("bad_password", (await store.ResetPasswordAsync("wren", "short")).Code);
        var reset = await store.ResetPasswordAsync("wren", Other);

        Assert.True(reset.Ok, reset.Message);
        user = (await store.Users.FindByNameAsync("wren"))!;
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.Null(reset.Account!.LockedUntil);
        Assert.True(await store.Users.CheckPasswordAsync(user, Other));
        Assert.False(await store.Users.CheckPasswordAsync(user, Password));
    }

    [Fact]
    public async Task ANewRoleEndsSessions()
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);
        await store.AddAsync("wren", Password, AccountRole.Owner);
        await store.AddAsync("ash", Password, AccountRole.Writer);
        var stamp = (await store.Users.FindByNameAsync("ash"))!.SecurityStamp;

        var result = await store.SetRoleAsync("ash", AccountRole.Viewer);

        Assert.Equal("'ash' is now a viewer.", result.Message);
        Assert.NotEqual(stamp, (await store.Users.FindByNameAsync("ash"))!.SecurityStamp);
        Assert.Equal("'ash' is already a viewer.", (await store.SetRoleAsync("ash", AccountRole.Viewer)).Message);
    }

    [Fact]
    public async Task OpeningExistingAccountsCreatesNothing()
    {
        using var temp = new TempHome();

        Assert.Null(await AccountStore.OpenExistingAsync(temp.Folder, Token));
        Assert.False(Directory.Exists(temp.Folder.Path));
    }

    [Fact]
    public void RolesReadAsTyped()
    {
        Assert.Equal(["owner", "writer", "viewer"], AccountRoles.Names);
        Assert.True(AccountRoles.TryParse("Viewer", out var viewer));
        Assert.Equal(AccountRole.Viewer, viewer);
        Assert.False(AccountRoles.TryParse("reader", out _));
        Assert.Equal("writer", AccountRole.Writer.Name());
    }

    private static AsyncServiceScope BuildScope(InstallationFolder folder)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWolfdenAccounts(folder);
        return services.BuildServiceProvider().CreateAsyncScope();
    }
}

using System.Text.Json;
using Wolfden.Accounts;

namespace Wolfden.Cli.Tests;

/// <summary>
/// <c>wolfden user</c> and <c>wolfden login</c> (#315 PR 4a), each against
/// an installation folder of its own under the system's temporary folder.
/// </summary>
public sealed class AccountCommandTests : IDisposable
{
    private const string Password = "correct horse battery";

    private readonly string _root = Path.Combine(TempDen.TempRoot, "homes", Guid.NewGuid().ToString("N"));

    /// <summary>A test that forgets --home lands here, never in the real installation.</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void IsolateInstallation() =>
        Installation.TestHome = Path.Combine(TempDen.TempRoot, "default-home");

    private string Home => Path.Combine(_root, "home");

    private InstallationFolder Folder => new(Home, InstallationSource.Option);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Runs with this test's --home, answering prompts from <paramref name="typed"/> in order.</summary>
    private (int ExitCode, string Output, string Error) Run(string[] typed, params string[] args)
    {
        var answers = new Queue<string>(typed);
        var prompts = new List<string>();
        PasswordInput.Prompter = prompt =>
        {
            prompts.Add(prompt);
            return answers.Count > 0 ? answers.Dequeue() : null;
        };
        try
        {
            return Cli.Run([.. args, "--home", Home]);
        }
        finally
        {
            PasswordInput.Prompter = null;
        }
    }

    private (int ExitCode, string Output, string Error) Run(params string[] args) =>
        Run(NoTyping, args);

    private static readonly string[] NoTyping = [];

    private void AddOwner(string name = "wren") =>
        Assert.Equal(CliApp.ExitOk, Run([Password, Password], "user", "add", name, "--owner").ExitCode);

    [Fact]
    [Trait("Scenario", "SC-ACCT-2")]
    public void TheFirstOwnerIsAddedWithAPasswordTypedTwice()
    {
        var (exitCode, output, error) = Run([Password, Password], "user", "add", "wren", "--owner");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Equal("", error);
        Assert.Equal(
            "Added 'wren' as an owner.\n" +
            "Login is off. Turn it on with 'wolfden login on'.\n" +
            $"Installation folder: {Path.GetFullPath(Home)} (set by --home)\n",
            output);
        Assert.True(File.Exists(Path.Combine(Home, "accounts.db")));

        var list = Run("user", "list");
        Assert.Equal(CliApp.ExitOk, list.ExitCode);
        Assert.Equal(
            $"Installation folder: {Path.GetFullPath(Home)} (set by --home)\n" +
            "Login: off\n\n" +
            "  wren  owner\n",
            list.Output);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-2")]
    public void APasswordThatDoesntMatchOrBreaksTheRulesIsAskedForAgain()
    {
        var (exitCode, _, error) = Run(["short", Password, "something else entirely", Password, Password], "user", "add", "ash", "--role", "writer");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Contains("That won't do: a password needs at least 15 characters, and this one has 5.", error, StringComparison.Ordinal);
        Assert.Contains("Those didn't match.", error, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, error, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeMissesAddNothing()
    {
        var (exitCode, output, error) = Run(["one password here", "another one here", "one password here", "x", "y"], "user", "add", "ash", "--role", "writer");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Equal("", output);
        Assert.EndsWith("wolfden user add: no password was set.\n", error, StringComparison.Ordinal);
        Assert.Contains("No accounts yet.", Run("user", "list").Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-2")]
    public async Task AScriptPipesThePasswordInAndNeverPutsItOnTheCommandLine()
    {
        PasswordInput.Input = new StringReader(Password + "\r\n");
        var (exitCode, _, error) = Run("user", "add", "wren", "--owner", "--password-stdin");
        PasswordInput.Input = new StringReader("");
        var (empty, _, emptyError) = Run("user", "add", "ash", "--role", "writer", "--password-stdin");
        var (onArgv, _, argvError) = Run("user", "add", "ash", "--role", "writer", "--password", Password);

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Equal("", error);
        Assert.Equal(CliApp.ExitUsage, empty);
        Assert.Equal("wolfden user add: --password-stdin found no password on standard input.\n", emptyError);
        Assert.Equal(CliApp.ExitUsage, onArgv);
        Assert.StartsWith("wolfden user add: unknown option '--password'.", argvError, StringComparison.Ordinal);

        await using var store = await AccountStore.OpenAsync(Folder, TestContext.Current.CancellationToken);
        var user = (await store.Users.FindByNameAsync("wren"))!;
        Assert.True(await store.Users.CheckPasswordAsync(user, Password));
    }

    [Theory]
    [InlineData(new[] { "user", "add", "ash" }, "say what the account may do: --role writer, --role viewer, or --owner.")]
    [InlineData(new[] { "user", "add", "ash", "--role", "reader" }, "'reader' isn't a role; use owner, writer, or viewer.")]
    [InlineData(new[] { "user", "add", "ash", "--owner", "--role", "viewer" }, "give --owner or --role viewer, not both.")]
    [InlineData(new[] { "user", "add", "ash calder", "--owner" }, "'ash calder' isn't a username: use letters, digits, '.', '-' and '_', with no spaces.")]
    [InlineData(new[] { "user", "add", "--owner" }, "give one username.")]
    public void AnAddWithoutAClearRoleOrNameAsksNothingAndAddsNothing(string[] args, string expected)
    {
        var (exitCode, _, error) = Run(args);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.StartsWith($"wolfden user add: {expected}\n", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Home));
    }

    [Fact]
    public void ATakenNameIsSaidBeforeAPasswordIsAskedFor()
    {
        AddOwner();

        var (exitCode, _, error) = Run(["should not be asked", "should not be asked"], "user", "add", "WREN", "--role", "writer");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Equal("wolfden user add: there's already an account named 'wren'.\n", error);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-7")]
    public void AViewerIsToldTheySeeEverythingAWriterSees()
    {
        AddOwner();

        var (exitCode, output, _) = Run([Password, Password], "user", "add", "rook", "--role", "viewer");
        var (_, setOutput, _) = Run([Password, Password], "user", "add", "ash", "--role", "writer");
        var (_, demoted, _) = Run("user", "set-role", "ash", "viewer");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Contains("Added 'rook' as a viewer.\nA viewer sees everything a writer sees, spoilers included, and can't change anything.", output, StringComparison.Ordinal);
        Assert.DoesNotContain("viewer sees", setOutput, StringComparison.Ordinal);
        Assert.Contains("'ash' is now a viewer.\nA viewer sees everything a writer sees", demoted, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-4")]
    public void TheLastOwnerStays()
    {
        AddOwner();

        var remove = Run("user", "remove", "wren");
        var demote = Run("user", "set-role", "wren", "writer");

        Assert.Equal(CliApp.ExitUsage, remove.ExitCode);
        Assert.StartsWith("wolfden user remove: 'wren' is the only owner, and an installation needs one.", remove.Error, StringComparison.Ordinal);
        Assert.Equal(CliApp.ExitUsage, demote.ExitCode);
        Assert.StartsWith("wolfden user set-role: 'wren' is the only owner", demote.Error, StringComparison.Ordinal);

        AddOwner("juno");
        Assert.Equal("Removed 'wren'.\n" + $"Installation folder: {Path.GetFullPath(Home)} (set by --home)\n", Run("user", "remove", "wren").Output);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-5")]
    public async Task AForgottenPasswordIsResetAndALockoutUndoneLocally()
    {
        AddOwner();
        await using (var store = await AccountStore.OpenAsync(Folder, TestContext.Current.CancellationToken))
        {
            var user = (await store.Users.FindByNameAsync("wren"))!;
            for (var i = 0; i < AccountsServices.MaxFailedAttempts; i++)
            {
                await store.Users.AccessFailedAsync(user);
            }
        }

        var list = Run("user", "list");
        Assert.Contains("  wren  owner  locked until ", list.Output, StringComparison.Ordinal);
        Assert.Contains("('wolfden user unlock wren')", list.Output, StringComparison.Ordinal);

        var unlock = Run("user", "unlock", "wren");
        Assert.Equal(CliApp.ExitOk, unlock.ExitCode);
        Assert.StartsWith("Unlocked 'wren'; it can log in again.\n", unlock.Output, StringComparison.Ordinal);
        Assert.Contains("  wren  owner\n", Run("user", "list").Output, StringComparison.Ordinal);

        const string newPassword = "a quiet moon rises";
        var reset = Run([newPassword, newPassword], "user", "reset-password", "wren");
        Assert.Equal(CliApp.ExitOk, reset.ExitCode);
        Assert.StartsWith("Set a new password for 'wren'. Anywhere it was logged in, it's logged out.\n", reset.Output, StringComparison.Ordinal);
        await using (var store = await AccountStore.OpenAsync(Folder, TestContext.Current.CancellationToken))
        {
            var user = (await store.Users.FindByNameAsync("wren"))!;
            Assert.True(await store.Users.CheckPasswordAsync(user, newPassword));
        }

        var unknown = Run(["should not be asked"], "user", "reset-password", "nobody");
        Assert.Equal(CliApp.ExitUsage, unknown.ExitCode);
        Assert.StartsWith("wolfden user reset-password: there's no account named 'nobody'.", unknown.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("unlock")]
    [InlineData("reset-password")]
    public void ChangesBeforeAnyAccountSayHowToStart(string subcommand)
    {
        var (exitCode, _, error) = Run("user", subcommand, "wren");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Equal($"wolfden user {subcommand}: there are no accounts in the installation at '{Path.GetFullPath(Home)}' yet. Add one with 'wolfden user add <name> --owner'.\n", error);
        Assert.False(Directory.Exists(Home));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-8")]
    public void ListingAnEmptyInstallationCreatesNothingAndNamesTheFolder()
    {
        var (exitCode, output, _) = Run("user", "list");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Equal(
            $"Installation folder: {Path.GetFullPath(Home)} (set by --home)\n" +
            "Login: off\n" +
            "No accounts yet. Add the first owner with 'wolfden user add <name> --owner'.\n",
            output);
        Assert.False(Directory.Exists(Home));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-8")]
    public void WithoutHomeItUsesAnotherInstallation()
    {
        var (_, output, _) = Cli.Run("user", "list");

        Assert.Contains($"Installation folder: {Path.GetFullPath(Installation.TestHome!)}", output, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFullPath(Home), output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonListsTheAccounts()
    {
        AddOwner();

        var (_, output, _) = Run("user", "list", "--json");

        using var json = JsonDocument.Parse(output);
        Assert.Equal(["version", "home", "login", "accounts"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(Path.GetFullPath(Home), json.RootElement.GetProperty("home").GetString());
        var account = Assert.Single(json.RootElement.GetProperty("accounts").EnumerateArray());
        Assert.Equal("wren", account.GetProperty("userName").GetString());
        Assert.Equal("owner", account.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, account.GetProperty("lockedUntil").ValueKind);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-6")]
    public void LoginOnNeedsAnOwnerFirst()
    {
        var (exitCode, output, error) = Run("login", "on");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Equal("", output);
        Assert.StartsWith("wolfden login: there's no owner account yet, so nobody could log in. Add one first, then turn login on:\n  wolfden user add <name> --owner\n  wolfden login on\n", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Home, "settings.json")));

        Run([Password, Password], "user", "add", "ash", "--role", "writer");
        Assert.Equal(CliApp.ExitUsage, Run("login", "on").ExitCode);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-6")]
    public void LoginOnSaysWhatItDoesntProtectAndToRestartServe()
    {
        AddOwner();

        var (exitCode, output, error) = Run("login", "on");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Equal("", error);
        Assert.StartsWith("Login is on.\n\nWhat logging in protects:", output, StringComparison.Ordinal);
        Assert.Contains("What it doesn't protect: your notes are ordinary files in a folder.", output, StringComparison.Ordinal);
        Assert.Contains("an AI assistant you started\nwith 'wolfden mcp', the 'wolfden' command itself, a backup or sync program,", output, StringComparison.Ordinal);
        Assert.Contains("restart it for the change to apply", output, StringComparison.Ordinal);
        Assert.EndsWith($"Installation folder: {Path.GetFullPath(Home)} (set by --home)\n", output, StringComparison.Ordinal);
        Assert.True(InstallationSettings.Load(Folder).Login);

        var status = Run("login");
        Assert.StartsWith("Login is on.\n", status.Output, StringComparison.Ordinal);
        Assert.Contains("Installation folder:", status.Output, StringComparison.Ordinal);
        Assert.Contains("Login: on", Run("user", "list").Output, StringComparison.Ordinal);

        var off = Run("login", "off");
        Assert.Equal(CliApp.ExitOk, off.ExitCode);
        Assert.StartsWith("Login is off: the web pages no longer ask for a login.\n", off.Output, StringComparison.Ordinal);
        Assert.False(InstallationSettings.Load(Folder).Login);
    }

    [Fact]
    public void LoginJsonSaysItIsEnforced()
    {
        AddOwner();
        Run("login", "on");

        var (_, output, _) = Run("login", "--json");

        using var json = JsonDocument.Parse(output);
        Assert.Equal(["version", "home", "login", "owners", "enforced"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.True(json.RootElement.GetProperty("login").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("owners").GetInt32());
        Assert.True(json.RootElement.GetProperty("enforced").GetBoolean());
    }

    [Fact]
    public void LoginTakesOnlyOnOrOff()
    {
        var (exitCode, _, error) = Run("login", "yes");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.StartsWith("wolfden login: 'yes' isn't 'on' or 'off'.\n", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrokenSettingsFileIsReportedNotIgnored()
    {
        Folder.Create();
        File.WriteAllText(Path.Combine(Home, "settings.json"), "{ \"login\": maybe }");

        var (exitCode, _, error) = Run("login");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.StartsWith($"wolfden login: couldn't use the installation at '{Path.GetFullPath(Home)}':", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-6")]
    public void LoginOffAlwaysWorksEvenWithASettingsFileThatDoesntRead()
    {
        Folder.Create();
        File.WriteAllText(Path.Combine(Home, "settings.json"), "{ \"login\": true, \"login\": false }");

        var list = Run("user", "list");
        var (exitCode, output, error) = Run("login", "off");

        Assert.Equal(CliApp.ExitUsage, list.ExitCode);
        Assert.Contains("names a setting twice", list.Error, StringComparison.Ordinal);
        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.StartsWith("Login is off: the web pages no longer ask for a login.\n", output, StringComparison.Ordinal);
        Assert.Contains("so it's replaced with one that turns login off; the old file is kept as", error, StringComparison.Ordinal);
        Assert.False(InstallationSettings.Load(Folder).Login);
        Assert.Equal(CliApp.ExitOk, Run("user", "list").ExitCode);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-6")]
    public void LoginOffKeepsTheOtherSettingsWhenOnlyLoginIsWrong()
    {
        Folder.Create();
        var original = "{ \"login\": \"yes\", \"public_origin\": \"https://den.example.net\" }";
        File.WriteAllText(Path.Combine(Home, "settings.json"), original);

        var (exitCode, _, error) = Run("login", "off");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Contains("Its \"login\" is now false, and its other settings are kept; the old file is kept as", error, StringComparison.Ordinal);
        Assert.Contains("https://den.example.net", File.ReadAllText(Path.Combine(Home, "settings.json")), StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(Path.Combine(Home, "settings.json.broken")));
        Assert.False(InstallationSettings.Load(Folder).Login);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-9")]
    public void AnInstallationInADenIsRefused()
    {
        using var den = TempDen.FromFiles(new Dictionary<string, string> { ["den.yaml"] = "format: 1\nname: Test\n" });
        var inside = Path.Combine(den.Root, "wolfden-home");
        PasswordInput.Input = new StringReader(Password + "\n");

        var add = Cli.Run("user", "add", "wren", "--owner", "--password-stdin", "--home", inside);
        var here = Cli.Run("login", "--home", den.Root);

        Assert.Equal(CliApp.ExitUsage, add.ExitCode);
        Assert.Equal($"wolfden user add: the installation folder '{Path.GetFullPath(inside)}' is inside the den at '{Path.GetFullPath(den.Root)}', whose checkpoints would record the accounts. Use a folder outside every den, with --home or WOLFDEN_HOME.\n", add.Error);
        Assert.False(Directory.Exists(inside));
        Assert.Equal(CliApp.ExitUsage, here.ExitCode);
        Assert.Contains("is a den, whose checkpoints would record the accounts", here.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(den.Root, "accounts.db")));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-9")]
    public void AnInstallationInARepositoryIsWarnedAbout()
    {
        var repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, ".git"));
        var home = Path.Combine(repository, "wolfden-home");

        var (exitCode, _, error) = Cli.Run("user", "list", "--home", home);

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Equal($"wolfden user list: warning: the installation folder '{Path.GetFullPath(home)}' is inside the git repository at '{Path.GetFullPath(repository)}'. Keep it out of that repository's commits (add it to .gitignore), or use a folder outside it with --home or WOLFDEN_HOME.\n", error);
    }

    [Fact]
    public void UserWithoutASubcommandListsThem()
    {
        var (exitCode, _, error) = Cli.Run("user");
        var (unknown, _, unknownError) = Cli.Run("user", "delete", "wren");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("set-role <name> <role>", error, StringComparison.Ordinal);
        Assert.Equal(CliApp.ExitUsage, unknown);
        Assert.StartsWith("wolfden user: unknown subcommand 'delete'.", unknownError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("list")]
    [InlineData("remove")]
    [InlineData("set-role")]
    [InlineData("reset-password")]
    [InlineData("unlock")]
    public void EverySubcommandHasHelpThatNamesHome(string subcommand)
    {
        var (exitCode, output, error) = Cli.Run("user", subcommand, "--help");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Empty(error);
        Assert.StartsWith($"usage: wolfden user {subcommand}", output, StringComparison.Ordinal);
        Assert.Contains("--home <folder>", output, StringComparison.Ordinal);
    }
}

/// <summary><c>serve</c> and the login setting (#315); in the serve tests' collection, since they share <see cref="ServeCommand.Started"/>.</summary>
[Collection(nameof(ServeCommandTests))]
public sealed class ServeLoginTests : IDisposable
{
    private readonly string _home = Path.Combine(TempDen.TempRoot, "homes", Guid.NewGuid().ToString("N"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void TurnLoginOn()
    {
        PasswordInput.Input = new StringReader("correct horse battery\n");
        Assert.Equal(CliApp.ExitOk, Cli.Run("user", "add", "wren", "--owner", "--password-stdin", "--home", _home).ExitCode);
        Assert.Equal(CliApp.ExitOk, Cli.Run("login", "on", "--home", _home).ExitCode);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-6")]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task WithLoginOnServeAsksForALogin()
    {
        TurnLoginOn();

        var (exitCode, output, error) = await Serve(_home, async url =>
        {
            using var client = new HttpClient { BaseAddress = new Uri(url) };
            using var den = await client.GetAsync("/api/den", Token);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, den.StatusCode);
            var status = await client.GetStringAsync("/api/auth/status", Token);
            Assert.Contains("\"login\":true", status, StringComparison.Ordinal);
        });

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Contains($"Login is on: the page asks for a username and password (installation folder '{Path.GetFullPath(_home)}').", output, StringComparison.Ordinal);
        Assert.DoesNotContain("doesn't ask for", error, StringComparison.Ordinal);

        // The cookie's name comes from a random id, kept in settings.json so it stays the same.
        var id = InstallationSettings.Load(new InstallationFolder(_home, InstallationSource.Option)).InstallationId;
        Assert.True(InstallationSettings.IsInstallationId(id));
        Assert.True(Directory.Exists(Path.Combine(_home, "keys")));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task WithLoginOffServeAnswersAsBefore()
    {
        var (exitCode, output, _) = await Serve(_home, async url =>
        {
            using var client = new HttpClient { BaseAddress = new Uri(url) };
            using var den = await client.GetAsync("/api/den", Token);
            Assert.Equal(System.Net.HttpStatusCode.OK, den.StatusCode);
        });

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.DoesNotContain("Login is on", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_home, "keys")));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task ASettingsFileThatDoesntReadStopsServe()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_home, "settings.json"), "{ \"login\": true, \"login\": false }");

        var (exitCode, _, error) = await Serve(_home, null);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("wolfden serve: stopped, because it couldn't use the installation at", error, StringComparison.Ordinal);
        Assert.Contains("names a setting twice", error, StringComparison.Ordinal);
        Assert.Contains("'wolfden login off'", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task AKeysFolderItCantMakeStopsServeWithAReason()
    {
        TurnLoginOn();
        File.WriteAllText(Path.Combine(_home, "keys"), "a file where the keys folder should be");

        var (exitCode, _, error) = await Serve(_home, null);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.StartsWith("wolfden serve: stopped, because it couldn't use the installation at", error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task LoginOnWithNoOwnerStopsServe()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_home, "settings.json"), "{ \"login\": true }");

        var (exitCode, _, error) = await Serve(_home, null);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("but it has no owner account, so nobody could log in", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-11")]
    public async Task AnInstallationFolderItCantUseStopsServe()
    {
        using var den = TempDen.DraculaCopy();

        var (exitCode, _, error) = await Serve(Path.Combine(den.Root, "accounts"), null);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("whose checkpoints would record the accounts", error, StringComparison.Ordinal);
        Assert.Contains("wolfden serve: stopped, because without the installation folder it can't tell whether login is on.", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Serves a copy of Dracula with <paramref name="home"/>; once it has
    /// started, runs <paramref name="whileServing"/> with its address, then
    /// stops it. With a null <paramref name="whileServing"/>, serve is
    /// expected not to start.
    /// </summary>
    private static async Task<(int ExitCode, string Output, string Error)> Serve(string home, Func<string, Task>? whileServing)
    {
        using var den = TempDen.DraculaCopy();
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        ServeCommand.Started = url => started.TrySetResult(url);
        using var stop = new CancellationTokenSource();
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            var serving = Task.Run(() => CliApp.RunUntil(["serve", den.Root, "--port", "0", "--no-open", "--no-watch", "--home", home], output, error, stop.Token), Token);
            if (whileServing is not null)
            {
                await whileServing(await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Token));
                await stop.CancelAsync();
            }

            var exitCode = await serving.WaitAsync(TimeSpan.FromSeconds(30), Token);
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            await stop.CancelAsync();
            ServeCommand.Started = null;
        }
    }
}

namespace Wolfden.Accounts.Tests;

/// <summary>The installation folder and its settings (#315 §3.2).</summary>
public class InstallationTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    [Trait("Scenario", "SC-ACCT-1")]
    public void HomeBeatsTheEnvironmentWhichBeatsTheDefault()
    {
        var given = Path.Combine(Path.GetTempPath(), "given-home");
        var fromEnv = Path.Combine(Path.GetTempPath(), "env-home");
        var env = Env(("WOLFDEN_HOME", fromEnv), ("HOME", Path.GetTempPath()));

        var a = InstallationFolder.Resolve(given, env, out _)!;
        Assert.Equal(Path.GetFullPath(given), a.Path);
        Assert.Equal(InstallationSource.Option, a.Source);

        var b = InstallationFolder.Resolve(null, env, out _)!;
        Assert.Equal(Path.GetFullPath(fromEnv), b.Path);
        Assert.Equal(InstallationSource.Environment, b.Source);
        Assert.Equal("set by WOLFDEN_HOME", b.SourceText);

        var c = InstallationFolder.Resolve(null, Env(("HOME", Path.GetTempPath())), out _)!;
        Assert.Equal(InstallationSource.Default, c.Source);
        Assert.Equal(Path.Combine(c.Path, "accounts.db"), c.AccountsDatabase);
        Assert.Equal(Path.Combine(c.Path, "settings.json"), c.SettingsFile);
        Assert.Equal(Path.Combine(c.Path, "keys"), c.KeysFolder);
    }

    [Fact]
    public void AnEmptyHomeIsRefused()
    {
        Assert.Null(InstallationFolder.Resolve(" ", Env(), out var error));
        Assert.Equal("--home needs a folder.", error);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-1")]
    public void TheDefaultIsTheAccountsOwnDataFolder()
    {
        var home = Path.Combine(Path.GetTempPath(), "someone");
        var path = InstallationFolder.DefaultPath(Env(("HOME", home)));
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wolfden"), path);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(Path.Combine(home, "Library", "Application Support", "Wolfden"), path);
        }
        else
        {
            Assert.Equal(Path.Combine(home, ".local", "share", "wolfden"), path);
            Assert.Equal("/xdg/data/wolfden", InstallationFolder.DefaultPath(Env(("HOME", home), ("XDG_DATA_HOME", "/xdg/data"))));
            // A relative XDG_DATA_HOME is invalid, and ignored.
            Assert.Equal(Path.Combine(home, ".local", "share", "wolfden"), InstallationFolder.DefaultPath(Env(("HOME", home), ("XDG_DATA_HOME", "relative"))));
        }
    }

    [Fact]
    public void ANewFolderIsOnlyForThisAccountOutsideWindows()
    {
        using var temp = new TempHome();
        Assert.False(temp.Folder.Exists);

        temp.Folder.Create();

        Assert.True(temp.Folder.Exists);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(temp.Folder.Path));
        }
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-6")]
    public void SettingsDefaultToLoginOffAndKeepKeysTheyDontKnow()
    {
        using var temp = new TempHome();
        Assert.False(InstallationSettings.Load(temp.Folder).Login);
        Assert.False(temp.Folder.Exists);

        temp.Folder.Create();
        File.WriteAllText(temp.Folder.SettingsFile, "{ \"login\": false, \"later\": { \"x\": 1 } }");
        var settings = InstallationSettings.Load(temp.Folder);
        settings.Login = true;
        settings.Save(temp.Folder);

        Assert.True(InstallationSettings.Load(temp.Folder).Login);
        var text = File.ReadAllText(temp.Folder.SettingsFile);
        Assert.Contains("\"later\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.False(File.Exists(temp.Folder.SettingsFile + ".tmp"));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-1")]
    public void ARelativeWolfdenHomeIsRefused()
    {
        Assert.Null(InstallationFolder.Resolve(null, Env(("WOLFDEN_HOME", "."), ("HOME", Path.GetTempPath())), out var error));
        Assert.Equal("WOLFDEN_HOME must be a full path, not '.'.", error);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-9")]
    public void AFolderInADenIsRefused()
    {
        using var temp = new TempHome();
        var den = Path.Combine(temp.Root, "den");
        Directory.CreateDirectory(Path.Combine(den, "notes"));
        File.WriteAllText(Path.Combine(den, "den.yaml"), "format: 1\nname: Test\n");

        Assert.Null(InstallationFolder.Resolve(den, Env(), out var asDen));
        Assert.Equal($"the installation folder '{den}' is a den, whose checkpoints would record the accounts. Use a folder outside every den, with --home or WOLFDEN_HOME.", asDen);
        Assert.Null(InstallationFolder.Resolve(null, Env(("WOLFDEN_HOME", Path.Combine(den, "notes", "wolfden"))), out var inside));
        Assert.Contains($"is inside the den at '{den}'", inside, StringComparison.Ordinal);

        // Built directly, it still won't be created there.
        var direct = new InstallationFolder(Path.Combine(den, "home"), InstallationSource.Option);
        Assert.Throws<AccountsException>(direct.Create);
        Assert.False(Directory.Exists(direct.Path));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-9")]
    public void ALinkIntoADenIsInTheDen()
    {
        using var temp = new TempHome();
        var den = Path.Combine(temp.Root, "den");
        Directory.CreateDirectory(Path.Combine(den, "private"));
        File.WriteAllText(Path.Combine(den, "den.yaml"), "format: 1\nname: Test\n");
        var link = Path.Combine(temp.Root, "link");
        FolderLink.Create(link, Path.Combine(den, "private"));

        Assert.Null(InstallationFolder.Resolve(link, Env(), out var direct));
        Assert.Contains($"is inside the den at '{InstallationFolder.RealPath(den)}'", direct, StringComparison.Ordinal);
        Assert.Null(InstallationFolder.Resolve(Path.Combine(link, "wolfden"), Env(), out var below));
        Assert.Contains($"is inside the den at '{InstallationFolder.RealPath(den)}'", below, StringComparison.Ordinal);
        Assert.Throws<AccountsException>(new InstallationFolder(link, InstallationSource.Option).Create);
        Assert.False(File.Exists(Path.Combine(den, "private", "accounts.db")));

        // A link to the den itself is the den.
        var toDen = Path.Combine(temp.Root, "to-den");
        FolderLink.Create(toDen, den);
        Assert.Null(InstallationFolder.Resolve(toDen, Env(), out var isDen));
        Assert.Contains("is a den,", isDen, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkOutsideAnyDenIsFine()
    {
        using var temp = new TempHome();
        var real = Path.Combine(temp.Root, "real");
        Directory.CreateDirectory(real);
        var link = Path.Combine(temp.Root, "link");
        FolderLink.Create(link, real);

        Assert.Equal(InstallationFolder.RealPath(real), InstallationFolder.RealPath(link));
        Assert.Equal(Path.Combine(InstallationFolder.RealPath(real), "not-yet"), InstallationFolder.RealPath(Path.Combine(link, "not-yet")));
        Assert.NotNull(InstallationFolder.Resolve(link, Env(), out _));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-6")]
    public void RepairingLoginKeepsTheOtherSettingsWhenTheFileStillReads()
    {
        using var temp = new TempHome();
        temp.Folder.Create();
        File.WriteAllText(temp.Folder.SettingsFile + ".broken", "an older copy");
        var original = "{ \"login\": \"yes\", \"public_origin\": \"https://den.example.net\" }";
        File.WriteAllText(temp.Folder.SettingsFile, original);
        Assert.Throws<InvalidDataException>(() => InstallationSettings.Load(temp.Folder));

        var said = InstallationSettings.TurnLoginOffRepairing(temp.Folder);

        Assert.StartsWith("Its \"login\" is now false, and its other settings are kept;", said, StringComparison.Ordinal);
        Assert.False(InstallationSettings.Load(temp.Folder).Login);
        Assert.Contains("\"public_origin\": \"https://den.example.net\"", File.ReadAllText(temp.Folder.SettingsFile), StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(temp.Folder.SettingsFile + ".broken"));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-6")]
    public void RepairingLoginReplacesAFileThatDoesntRead()
    {
        using var temp = new TempHome();
        temp.Folder.Create();
        File.WriteAllText(temp.Folder.SettingsFile, "{ \"login\": true, \"login\": false }");

        var said = InstallationSettings.TurnLoginOffRepairing(temp.Folder);

        Assert.StartsWith("It couldn't be read, so it's replaced with one that turns login off;", said, StringComparison.Ordinal);
        Assert.False(InstallationSettings.Load(temp.Folder).Login);
        Assert.Equal("{ \"login\": true, \"login\": false }", File.ReadAllText(temp.Folder.SettingsFile + ".broken"));
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-9")]
    public void AFolderInARepositoryIsFoundForAWarning()
    {
        using var temp = new TempHome();
        var repo = Path.Combine(temp.Root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../repo/.git/worktrees/x\n");

        Assert.Equal(repo, new InstallationFolder(Path.Combine(repo, "a", "home"), InstallationSource.Option).RepositoryAbove());
        Assert.Equal(worktree, new InstallationFolder(Path.Combine(worktree, "home"), InstallationSource.Option).RepositoryAbove());
        Assert.Null(temp.Folder.RepositoryAbove());

        // Allowed, unlike a den: a home folder kept in git is common.
        Assert.NotNull(InstallationFolder.Resolve(Path.Combine(repo, "home"), Env(), out _));
    }

    [Fact]
    public void AFolderOthersCanOpenIsWarnedAbout()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows folders have no Unix mode bits.");
            return;
        }

        using var temp = new TempHome();
        Assert.Null(temp.Folder.PermissionProblem());
        temp.Folder.Create();
        Assert.Null(temp.Folder.PermissionProblem());

        File.SetUnixFileMode(temp.Folder.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

        Assert.Contains("other accounts on this computer may open the installation folder", temp.Folder.PermissionProblem(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ \"login\": true, \"login\": false }")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("{ \"login\": \"yes\" }")]
    public void SettingsThatDontReadAreAnError(string text)
    {
        using var temp = new TempHome();
        temp.Folder.Create();
        File.WriteAllText(temp.Folder.SettingsFile, text);

        var e = Assert.Throws<InvalidDataException>(() => InstallationSettings.Load(temp.Folder));
        Assert.Contains(temp.Folder.SettingsFile, e.Message, StringComparison.Ordinal);
    }
}

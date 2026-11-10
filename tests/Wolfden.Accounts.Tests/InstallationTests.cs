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

    [Theory]
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

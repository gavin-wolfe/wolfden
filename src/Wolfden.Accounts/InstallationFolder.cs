using IOPath = System.IO.Path;

namespace Wolfden.Accounts;

/// <summary>Where an <see cref="InstallationFolder"/>'s path came from.</summary>
public enum InstallationSource
{
    /// <summary>The command line's <c>--home &lt;folder&gt;</c>.</summary>
    Option,

    /// <summary>The <c>WOLFDEN_HOME</c> environment variable.</summary>
    Environment,

    /// <summary>The computer account's own data folder.</summary>
    Default,
}

/// <summary>
/// An installation: one computer account's Wolfden data folder (#315). It
/// holds <c>accounts.db</c>, the cookie keys (<c>keys/</c>) and
/// <c>settings.json</c>. None of it is den content: it's never inside a
/// den and never pushed. Deleting it loses accounts, never notes or history.
/// </summary>
/// <remarks>
/// <para>
/// By default it's <c>%LOCALAPPDATA%\Wolfden</c> on Windows,
/// <c>~/Library/Application Support/Wolfden</c> on macOS, and
/// <c>$XDG_DATA_HOME/wolfden</c> (or <c>~/.local/share/wolfden</c>) elsewhere.
/// <c>--home &lt;folder&gt;</c> beats <c>WOLFDEN_HOME</c>, which beats the
/// default. A writer who keeps two names apart on one computer account
/// gives each its own folder.
/// </para>
/// </remarks>
public sealed class InstallationFolder
{
    /// <summary>The environment variable that moves the installation folder.</summary>
    public const string EnvironmentVariable = "WOLFDEN_HOME";

    /// <summary>An installation folder at <paramref name="path"/>, made absolute.</summary>
    public InstallationFolder(string path, InstallationSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = IOPath.GetFullPath(path);
        Source = source;
    }

    /// <summary>The folder, as an absolute path.</summary>
    public string Path { get; }

    /// <summary>Where the path came from.</summary>
    public InstallationSource Source { get; }

    /// <summary>The accounts database.</summary>
    public string AccountsDatabase => IOPath.Combine(Path, "accounts.db");

    /// <summary>The settings file: whether login is on, and later the public origin.</summary>
    public string SettingsFile => IOPath.Combine(Path, "settings.json");

    /// <summary>ASP.NET Core Data Protection keys, which sign and encrypt login cookies.</summary>
    public string KeysFolder => IOPath.Combine(Path, "keys");

    /// <summary>How the source reads in a sentence: "set by --home", and so on.</summary>
    public string SourceText => Source switch
    {
        InstallationSource.Option => "set by --home",
        InstallationSource.Environment => $"set by {EnvironmentVariable}",
        _ => "the default for this computer account",
    };

    /// <summary>
    /// The installation to use: <paramref name="given"/> (<c>--home</c>) if
    /// set, then <c>WOLFDEN_HOME</c>, then the default for this OS. Returns
    /// null with a reason when none can be worked out.
    /// </summary>
    /// <param name="given">The <c>--home</c> value, or null.</param>
    /// <param name="environment">Reads an environment variable; by default, the process's.</param>
    /// <param name="error">Why there's no folder, when null is returned.</param>
    public static InstallationFolder? Resolve(string? given, Func<string, string?>? environment, out string? error)
    {
        environment ??= System.Environment.GetEnvironmentVariable;
        error = null;
        if (given is not null)
        {
            if (string.IsNullOrWhiteSpace(given))
            {
                error = "--home needs a folder.";
                return null;
            }

            return new InstallationFolder(given, InstallationSource.Option);
        }

        if (environment(EnvironmentVariable) is { Length: > 0 } home && !string.IsNullOrWhiteSpace(home))
        {
            return new InstallationFolder(home, InstallationSource.Environment);
        }

        if (DefaultPath(environment) is { } path)
        {
            return new InstallationFolder(path, InstallationSource.Default);
        }

        error = $"couldn't find this computer account's data folder. Set {EnvironmentVariable}, or give --home <folder>.";
        return null;
    }

    /// <summary>The default folder for this OS, or null if the account's home can't be found.</summary>
    internal static string? DefaultPath(Func<string, string?> environment)
    {
        if (OperatingSystem.IsWindows())
        {
            var local = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(local) ? null : IOPath.Combine(local, "Wolfden");
        }

        var home = environment("HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        }

        if (OperatingSystem.IsMacOS())
        {
            return string.IsNullOrEmpty(home) ? null : IOPath.Combine(home, "Library", "Application Support", "Wolfden");
        }

        // XDG: a relative XDG_DATA_HOME is invalid and is ignored.
        if (environment("XDG_DATA_HOME") is { Length: > 0 } xdg && IOPath.IsPathRooted(xdg))
        {
            return IOPath.Combine(xdg, "wolfden");
        }

        return string.IsNullOrEmpty(home) ? null : IOPath.Combine(home, ".local", "share", "wolfden");
    }

    /// <summary>True if the folder exists.</summary>
    public bool Exists => Directory.Exists(Path);

    /// <summary>
    /// Creates the folder if it isn't there. Outside Windows, a new folder
    /// is readable only by this computer account (0700). On Windows, a
    /// folder under <c>%LOCALAPPDATA%</c> already is.
    /// </summary>
    public void Create()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(Path);
        }
        else
        {
            Directory.CreateDirectory(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}

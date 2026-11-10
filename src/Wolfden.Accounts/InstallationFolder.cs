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
        Path = IOPath.TrimEndingDirectorySeparator(IOPath.GetFullPath(path));
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

            return Checked(new InstallationFolder(given, InstallationSource.Option), out error);
        }

        InstallationFolder? folder = null;
        if (environment(EnvironmentVariable) is { Length: > 0 } home && !string.IsNullOrWhiteSpace(home))
        {
            // Like a relative XDG_DATA_HOME, a relative WOLFDEN_HOME would
            // follow the current folder, which may be a den.
            if (!IOPath.IsPathFullyQualified(home))
            {
                error = $"{EnvironmentVariable} must be a full path, not '{home}'.";
                return null;
            }

            folder = new InstallationFolder(home, InstallationSource.Environment);
        }
        else if (DefaultPath(environment) is { } path)
        {
            folder = new InstallationFolder(path, InstallationSource.Default);
        }
        else
        {
            error = $"couldn't find this computer account's data folder. Set {EnvironmentVariable}, or give --home <folder>.";
            return null;
        }

        return Checked(folder, out error);
    }

    /// <summary><paramref name="folder"/>, or null with a reason when it's in a den (<see cref="DenProblem"/>).</summary>
    private static InstallationFolder? Checked(InstallationFolder folder, out string? error)
    {
        error = folder.DenProblem();
        return error is null ? folder : null;
    }

    /// <summary>
    /// Why this folder can't be an installation, or null: it's a den, or
    /// inside one (a <c>den.yaml</c> in it or a folder above). A den's
    /// checkpoints would then record the password hashes and cookie keys.
    /// </summary>
    public string? DenProblem()
    {
        // By name, and by where links (symbolic links, and junctions on
        // Windows) really lead: a link to a den's subfolder is in the den.
        var real = RealPath(Path);
        var den = Above(Path, IsDen) ?? Above(real, IsDen);
        if (den is null)
        {
            return null;
        }

        var what = den == Path || den == real ? "a den" : $"inside the den at '{den}'";
        return $"the installation folder '{Path}' is {what}, whose checkpoints would record the accounts. Use a folder outside every den, with --home or {EnvironmentVariable}.";

        static bool IsDen(string dir) => File.Exists(IOPath.Combine(dir, "den.yaml"));
    }

    /// <summary>
    /// <paramref name="path"/> with every link along it followed to its final
    /// target, folder by folder from the root. Parts that don't exist yet are
    /// kept as written.
    /// </summary>
    internal static string RealPath(string path) => RealPath(path, 0);

    private static string RealPath(string path, int depth)
    {
        var full = IOPath.GetFullPath(path);
        var root = IOPath.GetPathRoot(full) ?? string.Empty;
        var current = root;
        var parts = full[root.Length..].Split([IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        // A loop of links has no end; give up following after this many.
        var hops = 0;
        foreach (var part in parts)
        {
            current = IOPath.Combine(current, part);
            var info = new DirectoryInfo(current);
            if (depth + hops < 64 && info.Exists && info.LinkTarget is not null)
            {
                hops++;
                try
                {
                    if (info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                    {
                        current = RealPath(target.FullName, depth + hops);
                    }
                }
                catch (IOException)
                {
                    // A broken or looping link: keep the name.
                }
            }
        }

        return IOPath.TrimEndingDirectorySeparator(current);
    }

    /// <summary>
    /// The git working tree this folder is inside, or null. Not refused (a
    /// home folder kept in git is common), but worth a warning: a plain
    /// <c>git add</c> there could take <c>accounts.db</c>.
    /// </summary>
    public string? RepositoryAbove()
    {
        return Above(Path, IsRepository) ?? Above(RealPath(Path), IsRepository);

        static bool IsRepository(string dir) => Directory.Exists(IOPath.Combine(dir, ".git")) || File.Exists(IOPath.Combine(dir, ".git"));
    }

    /// <summary>
    /// Outside Windows: a warning when the folder exists and other
    /// accounts on this computer may open it (group or world bits set), or
    /// null. Wolfden doesn't change an existing folder's permissions.
    /// </summary>
    public string? PermissionProblem()
    {
        if (OperatingSystem.IsWindows() || !Exists)
        {
            return null;
        }

        const UnixFileMode others = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(Path) & others) != 0
            ? $"other accounts on this computer may open the installation folder '{Path}'. To keep them out: chmod 700 '{Path}'"
            : null;
    }

    /// <summary>The nearest of <paramref name="start"/> and the folders above it that <paramref name="matches"/>, or null.</summary>
    private static string? Above(string start, Func<string, bool> matches)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (matches(dir.FullName))
            {
                return IOPath.TrimEndingDirectorySeparator(dir.FullName);
            }
        }

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
    /// <exception cref="AccountsException">The folder is a den or inside one (<see cref="DenProblem"/>).</exception>
    public void Create()
    {
        if (DenProblem() is { } problem)
        {
            throw new AccountsException(problem);
        }

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

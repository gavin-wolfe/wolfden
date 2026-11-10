using Microsoft.Data.Sqlite;
using Wolfden.Accounts;

namespace Wolfden.Cli;

/// <summary>
/// The installation folder for <c>user</c>, <c>login</c> and <c>serve</c>
/// (#315): <c>--home &lt;folder&gt;</c>, then <c>WOLFDEN_HOME</c>, then the
/// computer account's data folder (<see cref="InstallationFolder"/>).
/// </summary>
internal static class Installation
{
    /// <summary>The option every command that uses the installation takes.</summary>
    internal const string HomeOption = "--home";

    /// <summary>
    /// For tests: used in place of <c>WOLFDEN_HOME</c> and the default, so a
    /// test that forgets <c>--home</c> still never touches the real
    /// installation. <c>--home</c> still wins.
    /// </summary>
    internal static string? TestHome { get; set; }

    /// <summary>What <c>--help</c> says about <c>--home</c>.</summary>
    internal const string HomeHelp =
        """
          --home <folder>     the installation folder, which holds the accounts and
                              login settings; by default WOLFDEN_HOME, or this
                              computer account's data folder (on Windows,
                              %LOCALAPPDATA%\Wolfden)
        """;

    /// <summary>The installation to use, or null after saying why on <paramref name="error"/>.</summary>
    internal static InstallationFolder? Resolve(string command, CommandLine line, TextWriter error)
    {
        var given = line.Value(HomeOption);
        string? problem = null;
        var folder = given is null && TestHome is { } test
            ? new InstallationFolder(test, InstallationSource.Environment)
            : InstallationFolder.Resolve(given, null, out problem);
        problem = folder is null ? problem : folder.DenProblem();
        if (folder is null || problem is not null)
        {
            return Fail(problem ?? "couldn't find the installation folder.");
        }

        if (folder.RepositoryAbove() is { } repository)
        {
            error.WriteLine($"wolfden {command}: warning: the installation folder '{folder.Path}' is inside the git repository at '{repository}'. Keep it out of that repository's commits (add it to .gitignore), or use a folder outside it with --home or {InstallationFolder.EnvironmentVariable}.");
        }

        if (folder.PermissionProblem() is { } permissions)
        {
            error.WriteLine($"wolfden {command}: warning: {permissions}");
        }

        return folder;

        InstallationFolder? Fail(string message)
        {
            error.WriteLine($"wolfden {command}: {message}");
            return null;
        }
    }

    /// <summary>The line that says which installation is in use, so a writer can see two names aren't sharing one.</summary>
    internal static string Describe(InstallationFolder folder) => $"Installation folder: {folder.Path} ({folder.SourceText})";

    /// <summary>True for the failures reading or writing the installation can have, which a command reports and exits 2 for.</summary>
    internal static bool IsFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException
            or Microsoft.EntityFrameworkCore.DbUpdateException or AccountsException;
}

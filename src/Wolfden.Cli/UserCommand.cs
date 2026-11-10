using System.Globalization;
using Wolfden.Accounts;

namespace Wolfden.Cli;

/// <summary>
/// <c>wolfden user add|list|remove|set-role|reset-password|unlock</c>: the
/// installation's accounts (#315), in <c>accounts.db</c> in the
/// installation folder, never in a den. These are local commands: whoever
/// can run <c>wolfden</c> as this computer account can use them, the same
/// trust as opening the files.
/// </summary>
internal static class UserCommand
{
    /// <summary>What a viewer is told when the account is made (#315 §7, D6).</summary>
    internal const string ViewerNote =
        "A viewer sees everything a writer sees, spoilers included, and can't change anything. " +
        "The Reader's view is a filter anyone can turn on, not a limit on an account.";

    private const string RoleList = "owner, writer, or viewer";

    internal static readonly string[] Subcommands = ["add", "list", "remove", "set-role", "reset-password", "unlock"];

    internal static string Usage =>
        """
        usage: wolfden user <subcommand> [<args>]

        Manages the accounts of this installation (its accounts.db, outside every den):

          add <name> --role <role>     add an account (--owner for --role owner);
                                       asks for its password twice
          list                         list the accounts, and the installation folder
          remove <name>                remove an account
          set-role <name> <role>       change what an account may do
          reset-password <name>        give an account a new password
          unlock <name>                end a lockout after failed logins

        Roles: owner (everything), writer (reads and edits), viewer (reads only).
        There's always at least one owner. Run 'wolfden user <subcommand> --help'
        for its options. Login itself is turned on with 'wolfden login on'.
        """;

    internal static readonly CommandSpec AddSpec = new(
        "user add",
        "usage: wolfden user add <name> (--role <role> | --owner) [--password-stdin] [--home <folder>] [--json]",
        $"""
        Adds an account to this installation. It asks for the password twice,
        without showing it. A password needs {PasswordRules.MinimumLength} to {PasswordRules.MaximumLength} characters, with no other rules:
        a few words in a row make a good one. No email is asked for or stored.

          <name>              the username: letters, digits, '.', '-' and '_', up to
                              {UserNameRules.MaximumLength} characters; case doesn't matter when logging in.
                              It isn't written into any den or its history, but this
                              installation's owners can see it
          --role <role>       {RoleList}
          --owner             the same as --role owner
        {PasswordInput.Help}
        {Installation.HomeHelp}
          --json              print one JSON document instead of text

        Exits 0 when the account is added, and 2 when it isn't: a name that's taken
        or not allowed, a password that's too short or long, or no role.
        """,
        ["--owner", PasswordInput.StdinFlag, "--json"],
        ["--role", Installation.HomeOption],
        [],
        1,
        "give one username.");

    internal static readonly CommandSpec ListSpec = new(
        "user list",
        "usage: wolfden user list [--home <folder>] [--json]",
        $"""
        Lists this installation's accounts, with their roles and any lockout, and
        says which installation folder is in use and whether login is on.

        {Installation.HomeHelp}
          --json              print one JSON document instead of text

        Exits 0, or 2 if the installation can't be read.
        """,
        ["--json"],
        [Installation.HomeOption],
        [],
        0,
        "'user list' takes no arguments.");

    internal static readonly CommandSpec RemoveSpec = new(
        "user remove",
        "usage: wolfden user remove <name> [--home <folder>] [--json]",
        $"""
        Removes an account. The last owner can't be removed.

        {Installation.HomeHelp}
          --json              print one JSON document instead of text

        Exits 0 when it's removed, and 2 when there's no such account or it's the
        last owner.
        """,
        ["--json"],
        [Installation.HomeOption],
        [],
        1,
        "give one username.");

    internal static readonly CommandSpec SetRoleSpec = new(
        "user set-role",
        "usage: wolfden user set-role <name> <role> [--home <folder>] [--json]",
        $"""
        Changes what an account may do: <role> is {RoleList}. The last owner can't
        be given another role. The account is logged out everywhere.

        {Installation.HomeHelp}
          --json              print one JSON document instead of text

        Exits 0 when the role is set, and 2 when there's no such account, the role
        isn't one of the three, or it would leave no owner.
        """,
        ["--json"],
        [Installation.HomeOption],
        [],
        2,
        "give a username and a role.");

    internal static readonly CommandSpec ResetPasswordSpec = new(
        "user reset-password",
        "usage: wolfden user reset-password <name> [--password-stdin] [--home <folder>] [--json]",
        $"""
        Gives an account a new password, asked for twice. The account is logged out
        everywhere, and a lockout ends. This is how a forgotten password is
        recovered: there's no email reset.

        {PasswordInput.Help}
        {Installation.HomeHelp}
          --json              print one JSON document instead of text

        Exits 0 when the password is set, and 2 when it isn't.
        """,
        [PasswordInput.StdinFlag, "--json"],
        [Installation.HomeOption],
        [],
        1,
        "give one username.");

    internal static readonly CommandSpec UnlockSpec = new(
        "user unlock",
        "usage: wolfden user unlock <name> [--home <folder>] [--json]",
        $"""
        Ends a lockout. After {AccountsServices.MaxFailedAttempts} failed logins in a row, an account is locked for
        {AccountsServices.LockoutDuration.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes; anyone can cause that by guessing, so this undoes it at once.

        {Installation.HomeHelp}
          --json              print one JSON document instead of text

        Exits 0, or 2 when there's no such account.
        """,
        ["--json"],
        [Installation.HomeOption],
        [],
        1,
        "give one username.");

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        switch (args)
        {
            case ["--help"] or ["-h"]:
                output.WriteLine(Usage);
                return CliApp.ExitOk;
            case []:
                error.WriteLine("wolfden user: give a subcommand.");
                error.WriteLine(Usage);
                return CliApp.ExitUsage;
            case ["add", .. var rest]:
                return Add(rest, output, error);
            case ["list", .. var rest]:
                return List(rest, output, error);
            case ["remove", .. var rest]:
                return Change(RemoveSpec, rest, output, error, (store, line) => store.RemoveAsync(line.Arguments[0]));
            case ["set-role", .. var rest]:
                return SetRole(rest, output, error);
            case ["reset-password", .. var rest]:
                return ResetPassword(rest, output, error);
            case ["unlock", .. var rest]:
                return Change(UnlockSpec, rest, output, error, (store, line) => store.UnlockAsync(line.Arguments[0]));
            default:
                error.WriteLine($"wolfden user: unknown subcommand '{args[0]}'.");
                error.WriteLine(Usage);
                return CliApp.ExitUsage;
        }
    }

    private static int Add(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!Parse(AddSpec, args, output, error, out var line, out var exit))
        {
            return exit;
        }

        AccountRole role;
        if (line.Has("--owner"))
        {
            if (line.Value("--role") is { } other && !(AccountRoles.TryParse(other, out var parsed) && parsed == AccountRole.Owner))
            {
                return Refuse(AddSpec, error, $"give --owner or --role {other}, not both.");
            }

            role = AccountRole.Owner;
        }
        else if (line.Value("--role") is not { } roleText)
        {
            return Refuse(AddSpec, error, $"say what the account may do: --role writer, --role viewer, or --owner.");
        }
        else if (!AccountRoles.TryParse(roleText, out role))
        {
            return Refuse(AddSpec, error, $"'{roleText}' isn't a role; use {RoleList}.");
        }

        var userName = line.Arguments[0];
        if (UserNameRules.Problem(userName) is { } nameProblem)
        {
            return Refuse(AddSpec, error, nameProblem);
        }

        if (Installation.Resolve(AddSpec.Name, line, error) is not { } folder)
        {
            return CliApp.ExitUsage;
        }

        return WithStore(AddSpec, folder, error, store =>
        {
            // Before asking for a password: say a taken name at once.
            if (store.ListAsync().GetAwaiter().GetResult().FirstOrDefault(a => string.Equals(a.UserName, userName, StringComparison.OrdinalIgnoreCase)) is { } taken)
            {
                error.WriteLine($"wolfden {AddSpec.Name}: there's already an account named '{taken.UserName}'.");
                return CliApp.ExitUsage;
            }

            if (PasswordInput.ReadNew(AddSpec.Name, userName, line.Has(PasswordInput.StdinFlag), error) is not { } password)
            {
                return CliApp.ExitUsage;
            }

            var result = store.AddAsync(userName, password, role).GetAwaiter().GetResult();
            var notes = new List<string>();
            if (result.Ok)
            {
                if (role == AccountRole.Viewer)
                {
                    notes.Add(ViewerNote);
                }

                var settings = InstallationSettings.Load(folder);
                if (!settings.Login && role == AccountRole.Owner)
                {
                    notes.Add("Login is off. Turn it on with 'wolfden login on'.");
                }
            }

            return Report(AddSpec, line, folder, result, notes, output, error);
        });
    }

    private static int List(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!Parse(ListSpec, args, output, error, out var line, out var exit, arguments: 0))
        {
            return exit;
        }

        if (Installation.Resolve(ListSpec.Name, line, error) is not { } folder)
        {
            return CliApp.ExitUsage;
        }

        try
        {
            var settings = InstallationSettings.Load(folder);
            IReadOnlyList<Account> accounts = [];
            using (var store = AccountStore.OpenExistingAsync(folder).GetAwaiter().GetResult())
            {
                if (store is not null)
                {
                    accounts = store.ListAsync().GetAwaiter().GetResult();
                }
            }

            if (line.Has("--json"))
            {
                CliJson.Write(output, new UserListReport(1, folder.Path, settings.Login, accounts.Select(AccountJson.From).ToList()));
                return CliApp.ExitOk;
            }

            output.WriteLine(Installation.Describe(folder));
            output.WriteLine($"Login: {(settings.Login ? "on" : "off")}");
            if (accounts.Count == 0)
            {
                output.WriteLine("No accounts yet. Add the first owner with 'wolfden user add <name> --owner'.");
                return CliApp.ExitOk;
            }

            output.WriteLine();
            var width = accounts.Max(a => a.UserName.Length);
            foreach (var account in accounts)
            {
                var locked = account.LockedUntil is { } until ? $"  locked until {Local(until)} ('wolfden user unlock {account.UserName}')" : "";
                output.WriteLine($"  {account.UserName.PadRight(width)}  {account.Role.Name()}{locked}");
            }

            return CliApp.ExitOk;
        }
        catch (Exception e) when (Installation.IsFailure(e))
        {
            error.WriteLine($"wolfden {ListSpec.Name}: couldn't read the installation at '{folder.Path}': {e.Message}");
            return CliApp.ExitUsage;
        }
    }

    private static int SetRole(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!Parse(SetRoleSpec, args, output, error, out var line, out var exit, arguments: 2))
        {
            return exit;
        }

        if (!AccountRoles.TryParse(line.Arguments[1], out var role))
        {
            return Refuse(SetRoleSpec, error, $"'{line.Arguments[1]}' isn't a role; use {RoleList}.");
        }

        return Change(SetRoleSpec, line, output, error, store =>
        {
            var result = store.SetRoleAsync(line.Arguments[0], role).GetAwaiter().GetResult();
            return (result, result.Ok && role == AccountRole.Viewer ? [ViewerNote] : []);
        });
    }

    private static int ResetPassword(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!Parse(ResetPasswordSpec, args, output, error, out var line, out var exit))
        {
            return exit;
        }

        return Change(ResetPasswordSpec, line, output, error, store =>
        {
            var userName = line.Arguments[0];
            if (store.ListAsync().GetAwaiter().GetResult().FirstOrDefault(a => string.Equals(a.UserName, userName, StringComparison.OrdinalIgnoreCase)) is not { } account)
            {
                return (store.ResetPasswordAsync(userName, string.Empty).GetAwaiter().GetResult(), []);
            }

            if (PasswordInput.ReadNew(ResetPasswordSpec.Name, account.UserName, line.Has(PasswordInput.StdinFlag), error) is not { } password)
            {
                return (null, []);
            }

            return (store.ResetPasswordAsync(userName, password).GetAwaiter().GetResult(), []);
        });
    }

    private static int Change(CommandSpec spec, IReadOnlyList<string> args, TextWriter output, TextWriter error, Func<AccountStore, CommandLine, Task<AccountResult>> change)
    {
        if (!Parse(spec, args, output, error, out var line, out var exit))
        {
            return exit;
        }

        return Change(spec, line, output, error, store => (change(store, line).GetAwaiter().GetResult(), []));
    }

    /// <summary>Opens the accounts and makes a change; a null result means the change already said why it stopped.</summary>
    private static int Change(CommandSpec spec, CommandLine line, TextWriter output, TextWriter error, Func<AccountStore, (AccountResult? Result, List<string> Notes)> change)
    {
        if (Installation.Resolve(spec.Name, line, error) is not { } folder)
        {
            return CliApp.ExitUsage;
        }

        if (!File.Exists(folder.AccountsDatabase))
        {
            error.WriteLine($"wolfden {spec.Name}: there are no accounts in the installation at '{folder.Path}' yet. Add one with 'wolfden user add <name> --owner'.");
            return CliApp.ExitUsage;
        }

        return WithStore(spec, folder, error, store =>
        {
            var (result, notes) = change(store);
            return result is null ? CliApp.ExitUsage : Report(spec, line, folder, result, notes, output, error);
        });
    }

    private static int WithStore(CommandSpec spec, InstallationFolder folder, TextWriter error, Func<AccountStore, int> work)
    {
        try
        {
            using var store = AccountStore.OpenAsync(folder).GetAwaiter().GetResult();
            return work(store);
        }
        catch (Exception e) when (Installation.IsFailure(e))
        {
            error.WriteLine($"wolfden {spec.Name}: couldn't use the accounts in '{folder.Path}': {e.Message}");
            return CliApp.ExitUsage;
        }
    }

    private static int Report(CommandSpec spec, CommandLine line, InstallationFolder folder, AccountResult result, List<string> notes, TextWriter output, TextWriter error)
    {
        if (!result.Ok)
        {
            error.WriteLine($"wolfden {spec.Name}: {result.Message}");
            return CliApp.ExitUsage;
        }

        if (line.Has("--json"))
        {
            CliJson.Write(output, new UserChangeReport(1, folder.Path, Capitalize(result.Message), result.Account is { } a ? AccountJson.From(a) : null, notes));
            return CliApp.ExitOk;
        }

        output.WriteLine(Capitalize(result.Message));
        foreach (var note in notes)
        {
            output.WriteLine(note);
        }

        output.WriteLine(Installation.Describe(folder));
        return CliApp.ExitOk;
    }

    private static bool Parse(CommandSpec spec, IReadOnlyList<string> args, TextWriter output, TextWriter error, out CommandLine line, out int exit, int arguments = 1)
    {
        if (CommandLine.Parse(spec, args, output, error, out line) is { } code)
        {
            exit = code;
            return false;
        }

        if (line.Arguments.Count < arguments)
        {
            exit = Refuse(spec, error, spec.TooManyArguments);
            return false;
        }

        exit = CliApp.ExitOk;
        return true;
    }

    private static int Refuse(CommandSpec spec, TextWriter error, string message)
    {
        error.WriteLine($"wolfden {spec.Name}: {message}");
        error.WriteLine(spec.Usage);
        return CliApp.ExitUsage;
    }

    private static string Capitalize(string message) =>
        message.Length == 0 || message[0] == '\'' ? message : char.ToUpperInvariant(message[0]) + message[1..];

    private static string Local(DateTimeOffset time) =>
        time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>One account in <c>--json</c> output.</summary>
    /// <param name="UserName">The username.</param>
    /// <param name="Role">owner, writer, or viewer.</param>
    /// <param name="LockedUntil">When a lockout ends (ISO 8601, UTC), or null.</param>
    internal sealed record AccountJson(string UserName, string Role, DateTimeOffset? LockedUntil)
    {
        internal static AccountJson From(Account account) => new(account.UserName, account.Role.Name(), account.LockedUntil?.ToUniversalTime());
    }

    /// <summary>The JSON shape of <c>wolfden user list --json</c>.</summary>
    internal sealed record UserListReport(int Version, string Home, bool Login, IReadOnlyList<AccountJson> Accounts);

    /// <summary>The JSON shape of the other <c>wolfden user</c> subcommands with <c>--json</c>.</summary>
    internal sealed record UserChangeReport(int Version, string Home, string Message, AccountJson? Account, IReadOnlyList<string> Notes);
}

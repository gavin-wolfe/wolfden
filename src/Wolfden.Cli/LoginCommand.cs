using Wolfden.Accounts;

namespace Wolfden.Cli;

/// <summary>
/// <c>wolfden login [on|off]</c>: whether this installation's web pages ask
/// for a login (#315). It records the setting in the installation's
/// <c>settings.json</c>, and says what login protects and what it doesn't.
/// </summary>
/// <remarks>
/// <c>wolfden serve</c> reads the setting when it starts, so a running
/// server keeps the setting it started with until it's restarted.
/// </remarks>
internal static class LoginCommand
{
    /// <summary>What login protects and what it doesn't (#315 §4.1), printed by <c>login on</c>.</summary>
    internal const string WhatItProtects =
        """
        What logging in protects: with login on, the web pages ask for a username
        and password before they show or change anything. That keeps out other
        people who can reach the page: someone using this computer under another
        account, or another device, once network access is set up.

        What it doesn't protect: your notes are ordinary files in a folder. Anyone,
        or any program, that can open that folder can read and change them without
        logging in. That includes you in your editor, an AI assistant you started
        with 'wolfden mcp', the 'wolfden' command itself, a backup or sync program,
        and anyone who uses your computer account. Login doesn't encrypt anything.
        To keep notes from other people on this computer, give each person their
        own computer account, and turn on disk encryption.
        """;

    /// <summary>Said when the setting changes: a running server reads it only when it starts.</summary>
    internal const string RestartServe =
        """
        A 'wolfden serve' that's already running keeps the setting it started with:
        restart it for the change to apply.
        """;

    internal static readonly CommandSpec Spec = new(
        "login",
        "usage: wolfden login [on | off] [--home <folder>] [--json]",
        $"""
        Turns login on or off for this installation's web pages, or with neither,
        says whether it's on. It always says which installation folder is in use.
        'login on' needs an owner account first ('wolfden user add <name> --owner'),
        so nobody can turn it on and lock themselves out. 'login off' always works:
        it's the way back in if every owner's password is lost.

        Login protects the web pages only, not the den's files; 'login on' says
        what that means.

        {RestartServe}

        'login off' also repairs a settings.json it can't read: it keeps a copy as
        settings.json.broken, then sets "login" to false, keeping other settings
        when the file is still one JSON object.

        {Installation.HomeHelp}
          --json              print one JSON document instead of text

        Exits 0, or 2 when 'login on' finds no owner or the installation can't be
        read or written.
        """,
        ["--json"],
        [Installation.HomeOption],
        [],
        1,
        "give 'on', 'off', or nothing.");

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (CommandLine.Parse(Spec, args, output, error, out var line) is { } exit)
        {
            return exit;
        }

        if (line.Arguments is [var given] && given is not ("on" or "off"))
        {
            error.WriteLine($"wolfden login: '{given}' isn't 'on' or 'off'.");
            error.WriteLine(Spec.Usage);
            return CliApp.ExitUsage;
        }

        bool? turnOn = line.Arguments is [var word] ? word == "on" : null;

        if (Installation.Resolve(Spec.Name, line, error) is not { } folder)
        {
            return CliApp.ExitUsage;
        }

        try
        {
            InstallationSettings settings;
            string? replaced = null;
            try
            {
                settings = InstallationSettings.Load(folder);
            }
            catch (InvalidDataException e) when (turnOn is false)
            {
                // 'login off' is the way back in, so it always works: a
                // settings file it can't read is replaced, with login off.
                replaced = $"{e.Message} {InstallationSettings.TurnLoginOffRepairing(folder)}";
                settings = InstallationSettings.Load(folder);
            }

            // Only 'login on' needs the owners; 'login off' never depends on the accounts reading.
            int? owners;
            try
            {
                using var store = AccountStore.OpenExistingAsync(folder).GetAwaiter().GetResult();
                owners = store is null ? 0 : store.OwnerCountAsync().GetAwaiter().GetResult();
            }
            catch (Exception e) when (turnOn is not true && Installation.IsFailure(e))
            {
                owners = null;
            }

            if (replaced is not null)
            {
                error.WriteLine($"wolfden login: {replaced}");
            }

            string message;
            if (turnOn is true)
            {
                if (owners == 0)
                {
                    error.WriteLine("wolfden login: there's no owner account yet, so nobody could log in. Add one first, then turn login on:");
                    error.WriteLine("  wolfden user add <name> --owner");
                    error.WriteLine("  wolfden login on");
                    error.WriteLine(Installation.Describe(folder));
                    return CliApp.ExitUsage;
                }

                message = settings.Login ? "Login was already on." : "Login is on.";
                if (!settings.Login)
                {
                    settings.Login = true;
                    settings.Save(folder);
                }
            }
            else if (turnOn is false)
            {
                message = settings.Login || replaced is not null ? "Login is off: the web pages no longer ask for a login." : "Login was already off.";
                if (settings.Login)
                {
                    settings.Login = false;
                    settings.Save(folder);
                }
            }
            else
            {
                message = settings.Login ? "Login is on." : "Login is off.";
            }

            if (line.Has("--json"))
            {
                CliJson.Write(output, new LoginReport(1, folder.Path, settings.Login, owners, Enforced: true));
                return CliApp.ExitOk;
            }

            output.WriteLine(message);
            if (turnOn is true)
            {
                output.WriteLine();
                output.Write(WhatItProtects.ReplaceLineEndings(output.NewLine));
                output.WriteLine();
                output.WriteLine();
            }

            if (turnOn is not null)
            {
                output.WriteLine(RestartServe.ReplaceLineEndings(output.NewLine));
            }

            if (turnOn is null && !settings.Login && owners == 0)
            {
                output.WriteLine("To turn it on, add an owner first ('wolfden user add <name> --owner'), then run 'wolfden login on'.");
            }

            output.WriteLine(Installation.Describe(folder));
            return CliApp.ExitOk;
        }
        catch (Exception e) when (Installation.IsFailure(e))
        {
            error.WriteLine($"wolfden login: couldn't use the installation at '{folder.Path}': {e.Message}");
            return CliApp.ExitUsage;
        }
    }

    /// <summary>The JSON shape of <c>wolfden login --json</c>.</summary>
    /// <param name="Version">The shape's version, 1.</param>
    /// <param name="Home">The installation folder.</param>
    /// <param name="Login">Whether login is on.</param>
    /// <param name="Owners">How many owner accounts there are, or null if the accounts couldn't be read (only for <c>login</c> and <c>login off</c>).</param>
    /// <param name="Enforced">Whether this version's server asks for the login when it's on: true since #315's web login (it was false while only the setting existed).</param>
    internal sealed record LoginReport(int Version, string Home, bool Login, int? Owners, bool Enforced);
}

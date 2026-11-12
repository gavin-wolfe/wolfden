using System.Text;
using Wolfden.Accounts;

namespace Wolfden.Cli;

/// <summary>
/// Reads a new password for <c>user add</c> and <c>user reset-password</c>:
/// typed twice at a terminal without echo, or one line from standard input
/// with <c>--password-stdin</c>, for scripts. Never from the command line,
/// where it would land in the shell's history and the process list.
/// </summary>
internal static class PasswordInput
{
    /// <summary>The flag that reads the password from standard input.</summary>
    internal const string StdinFlag = "--password-stdin";

    /// <summary>How many times a terminal asks before giving up.</summary>
    internal const int Tries = 3;

    /// <summary>What <c>--help</c> says about the password.</summary>
    internal const string Help =
        """
          --password-stdin    read the password from standard input (its first line),
                              for scripts; otherwise it's asked for twice, without
                              showing what you type
        """;

    private static readonly AsyncLocal<TextReader?> InputOverride = new();
    private static readonly AsyncLocal<Func<string, string?>?> PrompterOverride = new();

    /// <summary>
    /// Standard input as UTF-8, whatever the console's code page, so a
    /// password piped in from a file or a script reads the same everywhere.
    /// </summary>
    private static readonly Lazy<TextReader> StandardInput = new(() =>
        new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true));

    /// <summary>Standard input, for <c>--password-stdin</c>. Tests give their own, for their own flow only.</summary>
    internal static TextReader Input
    {
        get => InputOverride.Value ?? StandardInput.Value;
        set => InputOverride.Value = value;
    }

    /// <summary>True when standard input is a terminal rather than a pipe or file (a test's own input never is).</summary>
    private static bool InputIsTerminal => InputOverride.Value is null && !Console.IsInputRedirected;

    /// <summary>
    /// For tests, for their own flow only: reads one answer to a prompt as
    /// if typed at a terminal, or returns null for end of input. When it's
    /// null, a terminal is asked if there is one.
    /// </summary>
    internal static Func<string, string?>? Prompter
    {
        get => PrompterOverride.Value;
        set => PrompterOverride.Value = value;
    }

    /// <summary>
    /// Reads a new password for <paramref name="userName"/> that meets the
    /// rules, or returns null after saying why on <paramref name="error"/>.
    /// Prompts go to standard error, so standard output keeps only the result.
    /// </summary>
    internal static string? ReadNew(string command, string userName, bool fromStdin, TextWriter error)
    {
        // --password-stdin typed at a terminal: there's nothing piped in,
        // so ask as without it, hiding what's typed, rather than reading
        // a visible line.
        if (fromStdin && !InputIsTerminal)
        {
            var line = Input.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                error.WriteLine($"wolfden {command}: {StdinFlag} found no password on standard input.");
                return null;
            }

            if (PasswordRules.Problem(userName, line) is { } problem)
            {
                error.WriteLine($"wolfden {command}: {problem}");
                return null;
            }

            return line;
        }

        var prompter = Prompter ?? (InputIsTerminal ? ReadHidden(error) : null);
        if (prompter is null)
        {
            error.WriteLine($"wolfden {command}: there's no terminal to ask for a password in. Pipe it in with '{StdinFlag}'.");
            return null;
        }

        for (var attempt = 1; attempt <= Tries; attempt++)
        {
            var first = prompter($"New password for '{userName}' ({PasswordRules.MinimumLength} characters or more; a few random words in a row are fine): ");
            if (first is null)
            {
                break;
            }

            if (PasswordRules.Problem(userName, first) is { } problem)
            {
                error.WriteLine($"That won't do: {problem}");
                continue;
            }

            var second = prompter("Type it again: ");
            if (second is null)
            {
                break;
            }

            if (second == first)
            {
                return first;
            }

            error.WriteLine("Those didn't match. Let's start again.");
        }

        error.WriteLine($"wolfden {command}: no password was set.");
        return null;
    }

    /// <summary>Asks at the terminal, showing nothing of what's typed.</summary>
    private static Func<string, string?> ReadHidden(TextWriter error) => prompt =>
    {
        error.Write(prompt);
        error.Flush();
        var typed = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                error.WriteLine();
                return typed.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    // A character outside the BMP is two chars; take both.
                    var remove = typed.Length > 1 && char.IsLowSurrogate(typed[^1]) && char.IsHighSurrogate(typed[^2]) ? 2 : 1;
                    typed.Length -= remove;
                }
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }
    };
}

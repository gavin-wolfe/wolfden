using Microsoft.Data.Sqlite;
using Wolfden.Mcp;

namespace Wolfden.Cli;

/// <summary>
/// <c>wolfden new &lt;type&gt; &lt;name&gt; [--in &lt;scope&gt;]</c>: creates a
/// note from its type's template (#29; types.md F-TPL-6), with a new id. It
/// does what the MCP tool <c>create_entity</c> does
/// (<see cref="DenTools.CreateEntity"/>), and takes its den, universe, and
/// scope the way <c>wolfden copy</c> does: the note goes to the den's files
/// only, and nothing is committed. <c>wolfden new series &lt;name&gt;</c> and
/// <c>wolfden new book &lt;name&gt;</c> create a series or book instead (#213),
/// as the web UI's New series and New book do.
/// </summary>
/// <remarks>
/// Exit codes: 0 when the note, series, or book is made, and 2 when the
/// command line is wrong, there's no den, or the create is refused (an
/// unknown type or scope, a final book, a file name or key that's taken, an
/// <c>--after</c> that isn't there, or a den with several universes and
/// none named).
/// </remarks>
internal static class NewCommand
{
    internal static readonly CommandSpec Spec = new(
        "new",
        """
        usage: wolfden new <type> <name> [--in <scope>] [--from <position>] [--slug <slug>] [--allow-relinks] [--den <den>] [--json]
               wolfden new series <name> [--in <universe>] [--after <key> | --first] [--den <den>] [--json]
               wolfden new book <name> [--in <universe or series>] [--after <key> | --first] [--chapters <n>] [--den <den>] [--json]
        """,
        """
        Creates a note from its type's template, such as a new character. It
        gets a new id that no file in the den uses, the name, and a file name
        made from it (with -2, -3 if it's taken), in the type's folder under
        entities/. Its status is the template's, or draft, and its sections
        are the template's, empty, for you to fill in. It's written to the
        den's files only; nothing is committed.

          <type>              the type's key, such as 'character', 'location', or
                              a type your den's own packs define
          <name>              the note's name (quote a name with spaces)
          --in <scope>        where it goes: '<universe>' (or '<universe>/') for
                              universe scope, or '<universe>/<key>' for a series
                              or book of it ('<universe>/series:<key>' or
                              '<universe>/book:<key>' if a series and a book share
                              the key); in a den with one universe, a bare series
                              or book key will do, unless it's the universe's key
                              too. By default, the den's universe, when it has
                              one. Never a final book.
          --from <position>   where the reader first meets it, such as
                              'dracula:5'; by default, the start of where it goes
          --slug <slug>       the file name without .md; by default, made from the
                              name
          --allow-relinks     make the note even if links that reach another note
                              would reach this one or become ambiguous (they're
                              listed)
          --den <den>         the den's folder; by default, the current folder or
                              the nearest folder above it with a den.yaml
          --json              print one JSON document instead of text

        If the name would take links from the note they reach now (a note
        nearer them wins), nothing is made unless --allow-relinks. It prints
        the new file, where the note enters canon, and the links that reach it
        now. Exits 0 when the note is made, and 2 if it isn't.

        'wolfden new series' and 'wolfden new book' create a series, or a book
        in a series or standalone in the universe: its folder, named from the
        name (a name that gives a key that's taken is refused), and its
        series.md or book.md with a new id and the name. It's added to the
        story order: the series' books list, or the universe's order.

          --in <place>        for a series, its universe; for a book, its
                              universe (a standalone book), or a series:
                              '<universe>/<series>', or in a den with one
                              universe, the series' key. By default, the
                              den's universe, when it has one.
          --after <key>       the series or book it comes after in story
                              order; by default, at the end
          --first             put it at the start of the story order instead,
                              before every work (not with --after)
          --chapters <n>      a book's chapters to start with, 1 to 1000, keyed
                              1 to n; by default, none

        Exits 0 when it's made, and 2 if it isn't.
        """,
        ["--json", "--allow-relinks", "--first"],
        ["--den", "--in", "--from", "--slug", "--after", "--chapters"],
        [],
        2,
        "give the type and the name, and nothing else (quote a name with spaces).");

    /// <summary>The command's options and commands, in place of the MCP tool's parameters and tools, in its messages.</summary>
    internal static readonly ToolWords Words = new(
        "pass --allow-relinks", "as <name>", "--slug", "with --in", "use 'wolfden query --brief'", "run 'wolfden check'", "run 'wolfden check'", "run 'wolfden check'")
    {
        ChapterKeys = "use a chapter key the book's book.md lists",
    };

    /// <summary><c>--in</c>, where the note goes.</summary>
    private static readonly ScopeOption In = new("--in", "create it in", "created");

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (CommandLine.Parse(Spec, args, output, error, out var line) is { } exit)
        {
            return exit;
        }

        if (line.Arguments.Count < 2)
        {
            error.WriteLine(line.Arguments is ["series" or "book"]
                ? $"wolfden new: give the {line.Arguments[0]}'s name, such as: wolfden new {line.Arguments[0]} \"{(line.Arguments[0] == "series" ? "The Harker Papers" : "Dracula's Guest")}\"."
                : "wolfden new: give the note's type and its name, such as: wolfden new character \"Mina Harker\".");
            error.WriteLine(Spec.Usage);
            return CliApp.ExitUsage;
        }

        // 'series' and 'book' are reserved type keys (types.md F-TYPE-2), so they never name a note's type.
        if (line.Arguments[0] is "series" or "book")
        {
            return RunContainer(line, line.Arguments[0] == "series", output, error);
        }

        foreach (var option in new[] { "--after", "--chapters" })
        {
            if (line.Value(option) is not null)
            {
                error.WriteLine($"wolfden new: {option} is for a new series or book ('wolfden new series' or 'wolfden new book'), not a note.");
                return CliApp.ExitUsage;
            }
        }

        if (line.Has("--first"))
        {
            error.WriteLine("wolfden new: --first is for a new series or book ('wolfden new series' or 'wolfden new book'), not a note.");
            return CliApp.ExitUsage;
        }

        var name = line.Arguments[1];
        if (Wolfden.Core.Editing.NewEntity.NameProblem(name) is { } nameProblem)
        {
            error.WriteLine($"wolfden new: give the note a name of one line, at most {Wolfden.Core.Editing.NewEntity.MaxNameLength} characters. That one can't be used: {nameProblem}.");
            return CliApp.ExitUsage;
        }

        if (DenLocation.Resolve(Spec.Name, line.Value("--den"), error) is not { } root)
        {
            return CliApp.ExitUsage;
        }

        string? universe = null, series = null, book = null;
        if (line.Value("--in") is { } scope && !In.TryParse(root, scope, out universe, out series, out book, out var problem))
        {
            error.WriteLine($"wolfden new: {problem}");
            return CliApp.ExitUsage;
        }

        WriteResult result;
        try
        {
            result = new DenTools(root) { Words = Words }.CreateEntity(
                line.Arguments[0], name, universe, series, book, line.Value("--slug"), from: line.Value("--from"), allowRelinks: line.Has("--allow-relinks"));
        }
        catch (DenToolException e)
        {
            error.WriteLine($"wolfden new: {e.Message}");
            return CliApp.ExitUsage;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException)
        {
            error.WriteLine($"wolfden new: couldn't read the den at '{root}' or its index: {e.Message}");
            return CliApp.ExitUsage;
        }

        if (line.Has("--json"))
        {
            CliJson.Write(output, result);
            return CliApp.ExitOk;
        }

        output.WriteLine($"Created {result.Path}");
        foreach (var effect in result.Effects)
        {
            output.WriteLine($"  {effect}");
        }

        foreach (var w in result.Warnings)
        {
            output.WriteLine($"{w.Path}:{w.Line?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}: {w.Severity} {w.Rule}: {w.Message}");
        }

        return CliApp.ExitOk;
    }

    /// <summary>
    /// <c>wolfden new series &lt;name&gt;</c> and <c>wolfden new book &lt;name&gt;</c>
    /// (#213): what the web UI's New series and New book do
    /// (<see cref="DenTools.CreateSeries"/>, <see cref="DenTools.CreateBook"/>).
    /// It reads the universe's or series' file and writes it in one go, so it
    /// passes no hash, as <c>wolfden freeze</c> does.
    /// </summary>
    private static int RunContainer(CommandLine line, bool series, TextWriter output, TextWriter error)
    {
        var what = series ? "series" : "book";
        foreach (var option in new[] { "--from", "--slug" })
        {
            if (line.Value(option) is not null)
            {
                error.WriteLine($"wolfden new: {option} is for a note, not a {what}.");
                return CliApp.ExitUsage;
            }
        }

        if (line.Has("--allow-relinks"))
        {
            // Containers aren't link targets (format.md F-LINK-5), so a new one never moves a link.
            error.WriteLine($"wolfden new: --allow-relinks is for a note: a new {what} never changes which note a link reaches.");
            return CliApp.ExitUsage;
        }

        if (line.Has("--first") && line.Value("--after") is not null)
        {
            error.WriteLine($"wolfden new: give --first or --after, not both: the {what} goes at the start, or after one work.");
            return CliApp.ExitUsage;
        }

        int? chapters = null;
        if (line.Value("--chapters") is { } count)
        {
            if (series)
            {
                error.WriteLine("wolfden new: --chapters is for a new book, not a series.");
                return CliApp.ExitUsage;
            }

            if (!int.TryParse(count, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
                || n is < 1 or > DenTools.MaxNewChapters)
            {
                error.WriteLine($"wolfden new: --chapters takes a whole number from 1 to {DenTools.MaxNewChapters}, not '{count}'. Leave it out for a book with no chapters yet.");
                return CliApp.ExitUsage;
            }

            chapters = n;
        }

        var name = line.Arguments[1];
        if (Wolfden.Core.Editing.NewEntity.NameProblem(name) is { } nameProblem)
        {
            error.WriteLine($"wolfden new: give the {what} a name of one line, at most {Wolfden.Core.Editing.NewEntity.MaxNameLength} characters. That one can't be used: {nameProblem}.");
            return CliApp.ExitUsage;
        }

        if (DenLocation.Resolve(Spec.Name, line.Value("--den"), error) is not { } root)
        {
            return CliApp.ExitUsage;
        }

        string? universe = null, inSeries = null;
        if (line.Value("--in") is { } place && !TryPlace(root, place.Trim(), series, out universe, out inSeries, out var problem))
        {
            error.WriteLine($"wolfden new: {problem}");
            return CliApp.ExitUsage;
        }

        ContainerCreateResult result;
        try
        {
            var tools = new DenTools(root) { Words = Words };
            result = series
                ? tools.CreateSeries(name, universe, line.Value("--after"), first: line.Has("--first"))
                : tools.CreateBook(name, universe, inSeries, line.Value("--after"), chapters, first: line.Has("--first"));
        }
        catch (DenToolException e)
        {
            error.WriteLine($"wolfden new: {e.Message}");
            return CliApp.ExitUsage;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException)
        {
            error.WriteLine($"wolfden new: couldn't read the den at '{root}' or its index: {e.Message}");
            return CliApp.ExitUsage;
        }

        if (line.Has("--json"))
        {
            CliJson.Write(output, result);
            return CliApp.ExitOk;
        }

        output.WriteLine($"Created {result.Path}");
        foreach (var effect in result.Effects)
        {
            output.WriteLine($"  {effect}");
        }

        foreach (var w in result.Warnings)
        {
            output.WriteLine($"{w.Path}:{w.Line?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}: {w.Severity} {w.Rule}: {w.Message}");
        }

        return CliApp.ExitOk;
    }

    /// <summary>
    /// Reads <c>--in</c> for a new series (a universe) or book (a universe, or
    /// one of its series). A universe's key alone always names the universe
    /// here, since a book holds no series or books: <c>--in dracula</c> in
    /// the Dracula sample is the universe, though it has a book 'dracula' too.
    /// Otherwise it's read as for a note (<see cref="ScopeOption"/>), and a
    /// book is refused.
    /// </summary>
    private static bool TryPlace(string root, string value, bool series, out string? universe, out string? inSeries, out string problem)
    {
        universe = inSeries = null;
        problem = "";
        var structure = Wolfden.Core.Structure.DenStructure.Load(root);
        if (structure.IsLoaded && !value.Contains('/', StringComparison.Ordinal) && structure.FindUniverse(value) is { } named)
        {
            universe = named.Key;
            return true;
        }

        // A bare key that's no universe, in a den with several: say where it could go, naming universes and series only.
        if (structure.IsLoaded && structure.Universes.Count > 1 && !value.Contains('/', StringComparison.Ordinal) && !value.Contains(':', StringComparison.Ordinal))
        {
            var holders = structure.Universes.Where(x => x.Series.Any(s => s.Key == value)).Select(x => $"'{x.Key}/{value}'").ToList();
            var asBook = structure.Universes.Any(x => x.FindBook(value) is not null);
            problem = $"the den has no universe '{value}', so --in '{value}' doesn't name one." +
                (series
                    ? " A series goes in a universe: give --in '<universe>'."
                    : (holders.Count > 0
                        ? $" To put the book in the series '{value}', give --in {string.Join(" or ", holders)}."
                        : asBook ? $" '{value}' is a book, and a book goes in a universe or a series." : "") +
                      " Give --in '<universe>' for a standalone book, or '<universe>/<series>'.");
            return false;
        }

        if (!In.TryParse(root, value, out universe, out inSeries, out var book, out problem))
        {
            return false;
        }

        if (series && (inSeries is not null || book is not null))
        {
            problem = $"a series goes in a universe, so give --in '<universe>', not '{value}'.";
            return false;
        }

        if (book is not null)
        {
            var u = structure.FindUniverse(universe!);
            problem = u?.FindBook(book) is not null
                ? $"'{book}' is a book, and a book goes in a universe or a series: give --in '{universe}' for a standalone book, or '{universe}/<series>'."
                : $"universe '{universe}' has no series '{book}'." +
                  (u is null || !u.Series.Any() ? "" : $" Its series: {string.Join(", ", u.Series.Select(s => s.Key))}.") +
                  $" Give --in '{universe}' for a standalone book, or '{universe}/<series>'.";
            return false;
        }

        return true;
    }
}

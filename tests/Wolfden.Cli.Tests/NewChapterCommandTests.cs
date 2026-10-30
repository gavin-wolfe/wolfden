using System.Text.Json;

namespace Wolfden.Cli.Tests;

/// <summary>
/// <c>wolfden new chapter</c> (#215): the command line over the write
/// tools' <c>AddChapter</c>, whose behaviour is tested in Wolfden.Mcp
/// (<c>AddChapterTests</c>). Each test works on its own copy of
/// <c>samples/dracula</c>.
/// </summary>
public sealed class NewChapterCommandTests : IDisposable
{
    private const string Book = "universes/dracula/books/dracula/book.md";

    private readonly TempDen _den = TempDen.DraculaCopy();

    public void Dispose() => _den.Dispose();

    private (int ExitCode, string Output, string Error) New(params string[] args) =>
        Cli.Run(["new", "--den", _den.Root, .. args]);

    [Fact]
    [Trait("Scenario", "SC-CLI-17")]
    public void ANewChapterGoesAtTheEndOrAfterTheChapterNamed()
    {
        // --in dracula is the book here, though it's the universe's key too: only a book holds chapters.
        var (exitCode, output, error) = New("chapter", "Jonathan's Letter", "--in", "dracula", "--after", "3");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Empty(error);
        Assert.StartsWith($"Added chapter dracula:3b to {Book}\n  Added chapter '3b' to book 'dracula', after '3', with number 4 and the title 'Jonathan's Letter'.", output, StringComparison.Ordinal);
        Assert.Contains("Renumbered the chapters after it, keys unchanged", output, StringComparison.Ordinal);
        Assert.Contains("'4' 4 → 5, '5' 5 → 6, … '27' 27 → 28 (24 chapters)", output, StringComparison.Ordinal);
        Assert.Contains("\n  - key: \"3b\"\n    number: 4\n    title: Jonathan's Letter\n  - key: 4\n    number: 5\n", _den.Read(Book).ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // No title, at the end; 'book:' and '<universe>/<book>' name it too.
        Assert.Equal(CliApp.ExitOk, New("chapter", "--in", "book:dracula").ExitCode);
        Assert.Equal(CliApp.ExitOk, New("chapter", "--in", "dracula/dracula").ExitCode);
        Assert.Contains("\n  - key: \"28\"\n    number: 29\n  - key: \"29\"\n    number: 30\n---\n", _den.Read(Book).ReplaceLineEndings("\n"), StringComparison.Ordinal);

        var (checkExit, check, _) = Cli.Run("check", _den.Root);
        Assert.Equal(CliApp.ExitOk, checkExit);
        Assert.DoesNotContain("book.md", check, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-17")]
    public void JsonPrintsWhatWasAdded()
    {
        var (exitCode, output, _) = New("chapter", "--in", "dracula", "--after", "26", "--json");

        Assert.Equal(CliApp.ExitOk, exitCode);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.Equal("26b", json.GetProperty("key").GetString());
        Assert.Equal("dracula:26b", json.GetProperty("position").GetString());
        Assert.Equal(27, json.GetProperty("number").GetInt64());
        Assert.Equal("27", json.GetProperty("renumbered")[0].GetProperty("key").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-17")]
    public void ARefusedAddExitsTwoAndWritesNothing()
    {
        var before = _den.Read(Book);

        void Refused(string text, params string[] args)
        {
            var (exitCode, output, error) = New(args);
            Assert.Equal(CliApp.ExitUsage, exitCode);
            Assert.Empty(output);
            Assert.Contains(text, error, StringComparison.Ordinal);
            Assert.Equal(before, _den.Read(Book));
        }

        Refused("give the book with --in", "chapter", "Title");
        Refused("the den has no book 'nope'", "chapter", "--in", "nope");
        Refused("the den has no universe 'nope'", "chapter", "--in", "nope/dracula");
        Refused("has no chapter '99'", "chapter", "--in", "dracula", "--after", "99");
        Refused("title of one line", "chapter", "Two\nlines", "--in", "dracula");
        Refused("at most 200", "chapter", new string('x', 201), "--in", "dracula");
        Refused("--first isn't for a chapter", "chapter", "--in", "dracula", "--first");
        Refused("--chapters isn't for a chapter", "chapter", "--in", "dracula", "--chapters", "2");
        Refused("--slug isn't for a chapter", "chapter", "--in", "dracula", "--slug", "x");
        Refused("--from isn't for a chapter", "chapter", "--in", "dracula", "--from", "dracula:1");
        Refused("--allow-relinks is for a note", "chapter", "--in", "dracula", "--allow-relinks");

        _den.Write(Book, before.Replace("status: draft", "status: final", StringComparison.Ordinal));
        before = _den.Read(Book);
        Refused("Book 'dracula' is final (frozen), so no chapter can be added to it until it's unfrozen (F-BOOK-1). To open the book for editing again, run: wolfden unfreeze dracula --universe dracula --den ", "chapter", "--in", "dracula");
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-17")]
    public async Task ACommandWaitsWhileAnotherWolfdenProcessIsWritingThenWrites()
    {
        // This process holds the den's write lock (#221), as 'wolfden serve' does while it saves.
        var cancel = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.Combine(_den.Root, ".wolfden"));
        var held = new FileStream(Path.Combine(_den.Root, ".wolfden", "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var start = new System.Diagnostics.ProcessStartInfo(Dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { Wolfden, "new", "chapter", "--in", "dracula", "--den", _den.Root })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancel);
        try
        {
            // The command says it's waiting, and hasn't written anything.
            using (var bound = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                bound.CancelAfter(TimeSpan.FromSeconds(120));
                var line = await process.StandardError.ReadLineAsync(bound.Token);
                Assert.Equal("wolfden new: another Wolfden is writing to the den; waiting up to 30 seconds for it to finish...", line);
            }

            Assert.DoesNotContain("key: \"28\"", _den.Read(Book), StringComparison.Ordinal);
        }
        finally
        {
            // The other process finishes its write (or ends, however it ends): the OS lets go of the file.
            held.Dispose();
        }

        var rest = await process.StandardError.ReadToEndAsync(cancel);
        using (var exit = CancellationTokenSource.CreateLinkedTokenSource(cancel))
        {
            exit.CancelAfter(TimeSpan.FromSeconds(120));
            await process.WaitForExitAsync(exit.Token);
        }

        Assert.Equal("", rest);
        Assert.Equal(CliApp.ExitOk, process.ExitCode);
        Assert.StartsWith($"Added chapter dracula:28 to {Book}", await output, StringComparison.Ordinal);
        Assert.Contains("\n  - key: \"28\"\n    number: 28\n---\n", _den.Read(Book).ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    /// <summary>The built <c>wolfden</c>, next to the test binaries.</summary>
    private static string Wolfden => Path.Combine(AppContext.BaseDirectory, "wolfden.dll");

    /// <summary>The <c>dotnet</c> host running the tests, or the one on the PATH.</summary>
    private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
}

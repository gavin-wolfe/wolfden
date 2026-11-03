using System.Runtime.Versioning;
using System.Text.Json;

namespace Wolfden.Cli.Tests;

/// <summary>
/// <c>check</c>, <c>index</c>, and <c>query</c> don't fail on a note they
/// can't read: it's left out, as if it weren't there (#112). The index
/// tests cover broken links and the watcher. <c>check</c> also lists it, as an
/// error (F-GEN-5, #284).
/// </summary>
public sealed class UnreadableFileTests
{
    private const string Jonathan = "universes/dracula/books/dracula/entities/characters/jonathan-harker.md";

    [Fact]
    [Trait("Scenario", "SC-CLI-8")]
    public void ANoteThatCantBeReadIsLeftOutAndTheCommandsStillWork()
    {
        using var den = TempDen.DraculaCopy();
        using var deleted = TempDen.DraculaCopy();
        File.Delete(Full(deleted.Root, Jonathan));
        var (_, expected, _) = Cli.Run("check", deleted.Root);

        using (Unreadable(Full(den.Root, Jonathan)))
        {
            var (checkExit, check, checkError) = Cli.Run("check", den.Root);
            Assert.Equal(CliApp.ExitErrors, checkExit);
            Assert.Contains($"{Jonathan}: error F-GEN-5: This file couldn't be read", check, StringComparison.Ordinal);
            Assert.Equal(WithoutUnreadable(expected), WithoutUnreadable(check));
            Assert.Empty(checkError);

            var (jsonExit, json, _) = Cli.Run("check", den.Root, "--json");
            Assert.Equal(CliApp.ExitErrors, jsonExit);
            using var report = JsonDocument.Parse(json);
            var found = Assert.Single(report.RootElement.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("rule").GetString() == "F-GEN-5");
            Assert.Equal(Jonathan, found.GetProperty("path").GetString());
            Assert.Equal("error", found.GetProperty("severity").GetString());

            var (indexExit, _, indexError) = Cli.Run("index", "--den", den.Root);
            Assert.Equal(CliApp.ExitOk, indexExit);
            Assert.Empty(indexError);

            var (queryExit, query, queryError) = Cli.Run("query", "--den", den.Root, "--at", "dracula:12", "--brief", "--type", "character");
            Assert.Equal(CliApp.ExitOk, queryExit);
            Assert.Empty(queryError);
            Assert.DoesNotContain(Jonathan, query, StringComparison.Ordinal);
        }

        // Once it can be read again, the next query sees it.
        var (_, after, _) = Cli.Run("query", "--den", den.Root, "--at", "dracula:12", "--brief", "--type", "character");
        Assert.Contains(Jonathan, after, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void CheckingAUniverseWhoseFileCantBeReadReportsThat()
    {
        using var den = TempDen.DraculaCopy();
        const string universe = "universes/dracula/universe.md";

        using (Unreadable(Full(den.Root, universe)))
        {
            var (exit, output, error) = Cli.Run("check", den.Root, "--universe", "dracula", "--no-index");

            Assert.Equal(CliApp.ExitErrors, exit);
            Assert.Contains($"{universe}: error F-GEN-5:", output, StringComparison.Ordinal);
            Assert.Empty(error);
        }
    }

    /// <summary>The check's output without the F-GEN-5 line and the summary, whose counts it changes.</summary>
    private static IEnumerable<string> WithoutUnreadable(string output) =>
        output.ReplaceLineEndings("\n").Split('\n').Where(l => !l.Contains("F-GEN-5", StringComparison.Ordinal) && !l.Contains("Checked ", StringComparison.Ordinal));

    private static string Full(string root, string path) => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Makes the file at <paramref name="full"/> unreadable until disposed:
    /// held open with no sharing on Windows, or with no permissions
    /// elsewhere. Skips the test where that doesn't stop reads, as for root.
    /// </summary>
    private static IDisposable Unreadable(string full)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        var cleanup = WithoutPermissions(full);
        try
        {
            File.ReadAllBytes(full);
        }
        catch (UnauthorizedAccessException)
        {
            return cleanup;
        }

        cleanup.Dispose();
        Assert.Skip("This user can read a file without read permission.");
        return null!;
    }

    [UnsupportedOSPlatform("windows")]
    private static Cleanup WithoutPermissions(string full)
    {
        var mode = File.GetUnixFileMode(full);
        File.SetUnixFileMode(full, UnixFileMode.None);
        return new Cleanup(() => File.SetUnixFileMode(full, mode));
    }

    private sealed class Cleanup(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

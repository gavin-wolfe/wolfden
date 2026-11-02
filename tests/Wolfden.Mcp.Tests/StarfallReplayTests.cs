using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Wolfden.Core.Checks;
using Wolfden.Core.History;
using Wolfden.Git;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// <c>scripts/starfall-history/replay.mjs</c> (#34): it replays the Starfall
/// sample's history into a new repository that ends exactly as
/// <c>samples/starfall</c> is, and it refuses to overwrite anything. The
/// replay's own git settings are the script's. These tests read the result
/// with theirs, never the developer's.
/// </summary>
/// <remarks>
/// The script needs Node. Where it's missing these tests skip, except in CI,
/// where they fail, so that CI can't stop running them without anyone seeing.
/// </remarks>
public sealed class StarfallReplayTests : IClassFixture<StarfallReplayTests.Replayed>
{
    private const string PlagueFile = "universes/starfall/entities/notes/velari-plague.md";
    private const string EmberFolder = "universes/starfall/series/long-dark/books/ember";
    private const string Tag = "book/starfall/ember/final";

    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "wolfden-mcp-tests");
    private static readonly string Script = Path.Combine(TempDen.RepoRoot, "scripts", "starfall-history", "replay.mjs");
    private static readonly string Sample = Path.Combine(TempDen.RepoRoot, "samples", "starfall");

    private readonly Replayed _replay;

    public StarfallReplayTests(Replayed replay)
    {
        _replay = replay;
    }

    private string Root => _replay.Require();

    private string[] Commits() => Git(Root, "log", "--reverse", "--format=%H").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private string Show(string commit, string path) => Git(Root, "show", $"{commit}:{path}");

    [Fact]
    [Trait("Scenario", "SC-HIST-18")]
    public void TheEndStateMatchesTheSampleExactly()
    {
        var replayed = FilesUnder(Root);
        var sample = FilesUnder(Sample);

        Assert.Equal(sample, replayed);
        Assert.All(sample, file => Assert.True(
            File.ReadAllBytes(Path.Combine(Sample, file)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Root, file))),
            $"{file} differs from the sample."));
        Assert.Empty(Git(Root, "status", "--porcelain"));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-18")]
    public void ItHasAboutEightCheckpointsByTheSamplesInventedAuthor()
    {
        Assert.Equal(8, Commits().Length);
        Assert.Equal(["Wren Calder <wren.calder@example.invalid>"], Git(Root, "log", "--format=%an <%ae>").Split('\n', StringSplitOptions.RemoveEmptyEntries).Distinct());
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-18")]
    public void EmberIsFrozenPartWayThroughAndNotChangedAfter()
    {
        var commits = Commits();
        var tagged = Git(Root, "rev-list", "-n", "1", Tag).Trim();
        var book = $"{EmberFolder}/book.md";

        Assert.Equal(commits[4], tagged);
        Assert.Contains("status: draft\n", Show(commits[3], book), StringComparison.Ordinal);
        Assert.Contains("status: final\n", Show(tagged, book), StringComparison.Ordinal);
        Assert.Empty(Git(Root, "diff", "--name-only", Tag, "HEAD", "--", EmberFolder));
        Assert.Equal("Freeze book 'ember' of universe 'starfall'", Git(Root, "log", "-1", "--format=%s", Tag).Trim());

        // Wolfden itself sees the tag as this book's freeze.
        var frozen = Assert.Single(FreezeTag.Existing(GitHistory.ForDen(Root, GitOptions, TestContext.Current.CancellationToken), "starfall", "ember", TestContext.Current.CancellationToken));
        Assert.Equal(Tag, frozen.Name);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-18")]
    public void TheRetconIsItsOwnCheckpointAndTheOldVersionIsInHistory()
    {
        var tools = new DenTools(Root) { History = () => GitHistory.ForDen(Root, GitOptions) };

        var history = tools.GetHistory("The Velari plague", diff: false);

        Assert.True(history.Available);
        Assert.Equal(PlagueFile, history.Entity.Path);
        Assert.Equal(["Retcon: there was no plague", "Start the Starfall notes: the Reach, the relays, the Velari"], history.Entries!.Select(e => e.Message));

        var retcon = history.Entries![0];
        Assert.Equal(
            ["universes/starfall/entities/notes/velari-plague.md", "universes/starfall/entities/species/velari.md", "universes/starfall/entities/technologies/relay-network.md"],
            Git(Root, "show", "--name-only", "--format=", retcon.Id).Split('\n', StringSplitOptions.RemoveEmptyEntries));

        var before = history.Entries[1].Id;
        Assert.Contains("A plague killed the Velari sixty years ago", Show(before, PlagueFile), StringComparison.Ordinal);
        Assert.DoesNotContain("cover story", Show(before, PlagueFile), StringComparison.Ordinal);
        Assert.Contains("the plague was the cover story", Show(retcon.Id, PlagueFile), StringComparison.Ordinal);

        var since = tools.GetHistory("The Velari plague", diffFrom: before);
        var patch = Assert.Single(since.Diff!.Files).Patch.ReplaceLineEndings("\n");
        Assert.Contains("\n-A plague killed the Velari", patch, StringComparison.Ordinal);
        Assert.Contains("\n+There was no plague.", patch, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-18")]
    public void EveryCheckpointPassesCheckWithNoErrorsOrWarnings()
    {
        var clone = NewFolder();
        try
        {
            Git(TempRoot, "clone", "--quiet", "--no-hardlinks", Root, clone);
            foreach (var commit in Commits())
            {
                Git(clone, "checkout", "--quiet", commit);
                var found = DenCheck.Run(clone).Diagnostics.Select(d => $"{d.Path}: {d.Diagnostic}").ToList();
                Assert.True(found.Count == 0, $"{commit}:\n{string.Join('\n', found)}");
            }
        }
        finally
        {
            Delete(clone);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-19")]
    public void ASecondReplayIntoTheSameFolderIsRefused()
    {
        var before = Git(Root, "rev-parse", "HEAD");

        var result = Replayed.RunNode(Script, [Root], null);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(before, Git(Root, "rev-parse", "HEAD"));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-19")]
    public void ItRefusesAFolderThatIsntEmptyAndChangesNothing()
    {
        _ = Root;
        var target = NewFolder();
        try
        {
            Directory.CreateDirectory(target);
            var existing = Path.Combine(target, "my-notes.md");
            File.WriteAllText(existing, "Mine.\n");

            var result = Replayed.RunNode(Script, [target], null);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("isn't an empty folder", result.Error, StringComparison.Ordinal);
            Assert.Equal("Mine.\n", File.ReadAllText(existing));
            Assert.Equal(["my-notes.md"], Directory.EnumerateFileSystemEntries(target).Select(Path.GetFileName));
        }
        finally
        {
            Delete(target);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-19")]
    public void ItRefusesAFileAtThatPathAndChangesNothing()
    {
        _ = Root;
        var target = NewFolder();
        try
        {
            File.WriteAllText(target, "Mine.\n");

            var result = Replayed.RunNode(Script, [target], null);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal("Mine.\n", File.ReadAllText(target));
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-19")]
    [InlineData("scripts", "starfall-history", "replay-here")]
    [InlineData("samples", "starfall", "replay-here")]
    public void ItRefusesAFolderInsideThisRepository(string first, string second, string third)
    {
        _ = Root;
        var target = Path.Combine(TempDen.RepoRoot, first, second, third);

        var result = Replayed.RunNode(Script, [target], null);

        Assert.Equal(1, result.ExitCode);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-19")]
    public void ALinkedParentCantLeadItIntoThisRepository()
    {
        _ = Root;
        var outside = NewFolder();
        var escaped = Path.Combine(TempDen.RepoRoot, "samples", $"replay-{Guid.NewGuid():N}");
        var link = Path.Combine(outside, "link");
        var goodLink = Path.Combine(outside, "good");
        try
        {
            Directory.CreateDirectory(outside);
            if (!MakeFolderLink(link, Path.Combine(TempDen.RepoRoot, "samples")))
            {
                Assert.Skip("This system doesn't allow folder links to be created.");
            }

            var result = Replayed.RunNode(Script, [Path.Combine(link, Path.GetFileName(escaped))], null);

            Assert.Equal(1, result.ExitCode);
            Assert.False(Directory.Exists(escaped), "The replay wrote inside the repository through the link.");

            // A link to a folder elsewhere is fine, and the replay lands where the link leads.
            var elsewhere = Path.Combine(outside, "elsewhere");
            Directory.CreateDirectory(elsewhere);
            Assert.True(MakeFolderLink(goodLink, elsewhere));
            var through = Replayed.RunNode(Script, [Path.Combine(goodLink, "den")], null);
            Assert.True(through.ExitCode == 0, through.Error);
            Assert.True(File.Exists(Path.Combine(elsewhere, "den", "den.yaml")));
        }
        finally
        {
            // The links first, so that deleting the folder can't reach through them.
            foreach (var made in new[] { link, goodLink })
            {
                if (Directory.Exists(made))
                {
                    Directory.Delete(made);
                }
            }

            Delete(escaped);
            Delete(outside);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-19")]
    public void ItRefusesAFolderInsideAnotherRepository()
    {
        _ = Root;
        var outer = NewFolder();
        try
        {
            Directory.CreateDirectory(outer);
            Git(outer, "init", "--quiet");
            var target = Path.Combine(outer, "den");

            var result = Replayed.RunNode(Script, [target], null);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("inside the git repository", result.Error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(target));
        }
        finally
        {
            Delete(outer);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-19")]
    public void WithoutAFolderItPrintsHowToUseIt()
    {
        _ = Root;
        var result = Replayed.RunNode(Script, [], null);

        Assert.Equal(2, result.ExitCode);
        Assert.StartsWith("Usage: node scripts/starfall-history/replay.mjs", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-19")]
    public void GitSettingsInTheEnvironmentCantPointItAtAnotherRepository()
    {
        _ = Root;
        var other = NewFolder();
        var target = NewFolder();
        try
        {
            Directory.CreateDirectory(other);
            Git(other, "init", "--quiet");
            var otherGit = Path.Combine(other, ".git");

            var result = Replayed.RunNode(Script, [target], new Dictionary<string, string?>
            {
                ["GIT_DIR"] = otherGit,
                ["GIT_WORK_TREE"] = other,
                ["GIT_AUTHOR_NAME"] = "Somebody Else",
            });

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal(Commits(), Git(target, "log", "--reverse", "--format=%H").Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.Throws<InvalidOperationException>(() => Git(other, "rev-parse", "--verify", "--quiet", "HEAD"));
        }
        finally
        {
            Delete(other);
            Delete(target);
        }
    }

    [Fact]
    public void TheScriptRunsNoGitCommandThatCouldReachTheNetwork()
    {
        var script = File.ReadAllText(Script);

        foreach (var word in new[] { "'push'", "'fetch'", "'pull'", "'clone'", "'remote'", "'submodule'", "http://", "https://" })
        {
            Assert.DoesNotContain(word, script.Replace("https://nodejs.org", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }
    }

    /// <summary>A folder link: a symbolic link, or on Windows without that right, a junction. False if neither can be made.</summary>
    private static bool MakeFolderLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }
        }

        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        mklink.WaitForExit();
        return mklink.ExitCode == 0 && Directory.Exists(link);
    }

    private static List<string> FilesUnder(string root) =>
    [
        .. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith(".git/", StringComparison.Ordinal) && !f.StartsWith(".wolfden/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal),
    ];

    private static string NewFolder()
    {
        Directory.CreateDirectory(TempRoot);
        return Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
    }

    private static void Delete(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(folder, recursive: true);
    }

    private static GitOptions GitOptions => new()
    {
        Environment = new Dictionary<string, string?>
        {
            ["GIT_CONFIG_GLOBAL"] = ConfigFile.Value,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CEILING_DIRECTORIES"] = TempRoot,
            ["GIT_DIR"] = null,
            ["GIT_WORK_TREE"] = null,
            ["GIT_INDEX_FILE"] = null,
        },
    };

    private static readonly Lazy<string> ConfigFile = new(() =>
    {
        Directory.CreateDirectory(TempRoot);
        var file = Path.Combine(TempRoot, $"gitconfig-{Guid.NewGuid():N}");
        File.WriteAllText(
            file,
            "[user]\n\tname = Test Writer\n\temail = writer@example.com\n[commit]\n\tgpgsign = false\n[core]\n\tautocrlf = false\n",
            new UTF8Encoding(false));
        return file;
    });

    private static string Git(string folder, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        foreach (var (name, value) in GitOptions.Environment)
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return process.ExitCode == 0
            ? output.Result.ReplaceLineEndings("\n")
            : throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {error.Result}");
    }

    /// <summary>One replay into a temporary folder, shared by the tests that only read it.</summary>
    public sealed class Replayed : IDisposable
    {
        private readonly string _target = NewFolder();
        private readonly NodeResult? _result;

        public Replayed()
        {
            try
            {
                _result = RunNode(Script, [_target], null);
            }
            catch (Win32Exception)
            {
                // No node on this machine: Require says what that means.
            }
        }

        /// <summary>The replayed den; skips or fails the test if Node is missing, and fails it if the replay did.</summary>
        public string Require()
        {
            if (_result is null)
            {
                if (Environment.GetEnvironmentVariable("CI") is { Length: > 0 })
                {
                    Assert.Fail("Node isn't on this CI runner, so the Starfall replay can't be tested.");
                }

                Assert.Skip("Node isn't installed, so the Starfall replay can't run here.");
            }

            Assert.True(_result!.ExitCode == 0, _result.Error);
            return _target;
        }

        public void Dispose() => Delete(_target);

        internal static NodeResult RunNode(string script, string[] args, IReadOnlyDictionary<string, string?>? environment)
        {
            var start = new ProcessStartInfo("node")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            start.ArgumentList.Add(script);
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
            {
                start.Environment[name] = value;
            }

            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return new NodeResult(process.ExitCode, output.Result, error.Result);
        }
    }

    internal sealed record NodeResult(int ExitCode, string Output, string Error);
}

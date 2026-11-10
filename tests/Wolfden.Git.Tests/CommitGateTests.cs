using System.Text;
using System.Text.RegularExpressions;
using Wolfden.Core.History;

namespace Wolfden.Git.Tests;

/// <summary>
/// The one gate every commit and tag passes (#311, #315 design §3.4): the
/// name and email from git's settings, handed to that one git process in its
/// environment, and signing pinned to the den's own <c>.git/config</c>. Real
/// repositories in temporary folders, each with a global config of the
/// test's own, and invented names only.
/// </summary>
public sealed class CommitGateTests : IDisposable
{
    private const string PenName = "Test Pen Name|pen@example.invalid|Test Pen Name|pen@example.invalid";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly List<string> _folders = [];

    public void Dispose()
    {
        foreach (var folder in _folders)
        {
            TestRepo.DeleteFolder(folder);
        }
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-26")]
    [InlineData("commit", "-m", "x")]
    [InlineData("tag", "v1")]
    [InlineData("-c", "user.name=Somebody", "commit", "-m", "x")]
    [InlineData("-C", ".", "tag", "v1")]
    [InlineData("--literal-pathspecs", "commit-tree", "HEAD^{tree}")]
    [InlineData("merge", "main")]
    [InlineData("cherry-pick", "HEAD")]
    [InlineData("revert", "HEAD")]
    [InlineData("rebase", "main")]
    [InlineData("am")]
    [InlineData("notes", "add")]
    [InlineData("stash")]
    [InlineData("mktag")]
    [InlineData("replay", "--onto", "main", "HEAD")]
    [InlineData("subtree", "add")]
    [InlineData("-c", "alias.save=commit", "save", "-m", "x")]
    [InlineData("-c", "ALIAS.save=commit", "status")]
    [InlineData("-calias.save=commit", "save")]
    [InlineData("--config-env", "alias.save=SOME_VARIABLE", "save")]
    [InlineData("-c", "include.path=/elsewhere", "status")]
    [InlineData("-c", "includeIf.gitdir:/.path=/elsewhere", "status")]
    [InlineData("--config-env=include.path=SOME_VARIABLE", "status")]
    public void AGitCommandThatMakesCommitsOrTagsRunsOnlyThroughTheGate(params string[] args)
    {
        // Refused before git starts, so no folder or repository is needed: a new write path
        // that skipped the gate would fail like this in its first test.
        var refused = Assert.Throws<InvalidOperationException>(() => GitProcess.Run(TestRepo.Options, Path.GetTempPath(), args, cancellationToken: Token));

        Assert.Contains("CommitGate", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-26")]
    [InlineData("ls-files", "--", "-calias.md")]
    [InlineData("ls-files", "--", "-cinclude.md")]
    [InlineData("ls-files", "-calias.md")]
    [InlineData("-c", "core.quotepath=false", "log", "--oneline", "--", "-cincludeIf.md")]
    public void AnArgumentAfterTheCommandThatLooksLikeASettingIsntRefused(params string[] args)
    {
        // Only git's own options, before the command, can set config; a file named so is the command's.
        using var repo = TestRepo.Create();

        var run = GitProcess.Run(TestRepo.Options, repo.Root, args, cancellationToken: Token);

        Assert.True(run.ExitCode is 0 or 128 or 129, run.Said);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-26")]
    public void ADenFileNamedLikeASettingIsCheckpointed()
    {
        using var repo = TestRepo.Create();
        repo.WriteDen("-calias.md", "# Odd name\n");
        repo.WriteDen("-cinclude.md", "# Odd name\n");

        var made = repo.History.Checkpoint(new CheckpointRequest("Odd names", ["-calias.md", "-cinclude.md"]), Token);

        Assert.True(made.IsAvailable, made.Reason);
        Assert.Equal(["-calias.md", "-cinclude.md"], made.Value!.Changes.Select(c => c.Path).Order(StringComparer.Ordinal));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-24")]
    [InlineData("parameters")]
    [InlineData("count")]
    public void ANameAndEmailPassedAsConfigInTheEnvironmentArentUsed(string how)
    {
        using var repo = TestRepo.Create();
        repo.Git("config", "--local", "user.name", "Test Pen Name");
        repo.Git("config", "--local", "user.email", "pen@example.invalid");
        var options = With(TestRepo.Options, how == "parameters"
            ? new() { ["GIT_CONFIG_PARAMETERS"] = "'user.name=Env Config Name' 'user.email=envcfg@example.invalid'" }
            : new()
            {
                ["GIT_CONFIG_COUNT"] = "2",
                ["GIT_CONFIG_KEY_0"] = "user.name",
                ["GIT_CONFIG_VALUE_0"] = "Env Config Name",
                ["GIT_CONFIG_KEY_1"] = "author.email",
                ["GIT_CONFIG_VALUE_1"] = "envcfg@example.invalid",
            });

        var made = GitHistory.ForDen(repo.DenRoot, options, Token).Checkpoint(new CheckpointRequest("First"), Token);

        Assert.True(made.IsAvailable, made.Reason);
        Assert.Equal(PenName, repo.Git("log", "-1", "--format=%an|%ae|%cn|%ce").Trim());
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-26")]
    public void NothingInWolfdenRunsGitExceptThroughGitProcess()
    {
        // GitProcess is where the gate is enforced, so a process started anywhere else could skip it.
        // The one other process Wolfden starts is the browser, for 'serve --open'.
        var src = Path.Combine(RepoRoot(), "src");
        // Any way of making or starting a process: ProcessStartInfo, Process.Start, new Process(), the full type name.
        var startsAProcess = new Regex(
            @"ProcessStartInfo|\bStartInfo\b|\bProcess\s*\.\s*Start\b|\bnew\s+(System\.Diagnostics\.)?Process\b|System\.Diagnostics\.Process\b|using\s+static\s+System\.Diagnostics\.Process");
        var starting = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => startsAProcess.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(src, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["Wolfden.Cli/ServeCommand.cs", "Wolfden.Git/GitProcess.cs"], starting);
        var serve = File.ReadAllText(Path.Combine(src, "Wolfden.Cli", "ServeCommand.cs"));
        Assert.DoesNotContain("\"git\"", serve, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-24")]
    public void AnInheritedNameAndEmailDontWinAtACheckpointOrATag()
    {
        using var repo = TestRepo.Create();
        repo.Git("config", "--local", "user.name", "Test Pen Name");
        repo.Git("config", "--local", "user.email", "pen@example.invalid");
        repo.WriteDen("notes/one.md", "# One\n");
        var options = With(TestRepo.Options, new()
        {
            ["GIT_AUTHOR_NAME"] = "Everyday Name",
            ["GIT_AUTHOR_EMAIL"] = "everyday@example.invalid",
            ["GIT_COMMITTER_NAME"] = "Everyday Name",
            ["GIT_COMMITTER_EMAIL"] = "everyday@example.invalid",
            ["EMAIL"] = "everyday@example.invalid",
        });
        var history = GitHistory.ForDen(repo.DenRoot, options, Token);

        var made = history.Checkpoint(new CheckpointRequest("First"), Token);
        var tagged = history.Tag(new TagRequest("book/kestrel/first/final", Message: "Freeze"), Token);

        Assert.True(made.IsAvailable, made.Reason);
        Assert.Empty(made.Warnings);
        Assert.Equal(PenName, repo.Git("log", "-1", "--format=%an|%ae|%cn|%ce").Trim());
        Assert.True(tagged.IsAvailable, tagged.Reason);
        Assert.Equal("Test Pen Name|<pen@example.invalid>", repo.Git("for-each-ref", "--format=%(taggername)|%(taggeremail)", "refs/tags/book/kestrel/first/final").Trim());
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-24")]
    public void ARepositoryTrustedOnlyThroughConfigInTheEnvironmentIsRefused()
    {
        // git var, without that config, would answer from outside the repository with the global name,
        // while the commit trusts it: refused before anything is marked.
        using var repo = TestRepo.Create();
        var options = Global("[user]\n\tname = Everyday Name\n\temail = everyday@example.invalid\n");
        RunGit(repo.Root, options, "config", "--local", "user.name", "Test Pen Name");
        RunGit(repo.Root, options, "config", "--local", "user.email", "pen@example.invalid");
        repo.WriteDen("notes/one.md", "# One\n");
        options = With(options, new()
        {
            ["GIT_TEST_ASSUME_DIFFERENT_OWNER"] = "1",
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "safe.directory",
            ["GIT_CONFIG_VALUE_0"] = "*",
        });
        var history = GitHistory.ForDen(repo.DenRoot, options, Token);

        var refused = Assert.Throws<HistoryException>(() => history.Checkpoint(new CheckpointRequest("First"), Token));

        Assert.StartsWith("Git trusts this den's repository only through settings passed in the environment", refused.Message, StringComparison.Ordinal);
        Assert.Contains("safe.directory", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Everyday", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pen Name", refused.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => repo.Git("rev-parse", "--verify", "--quiet", "HEAD"));
        Assert.Equal("?? den.yaml\n?? notes/one.md\n", repo.Git("status", "--porcelain", "--untracked-files=all"));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-24")]
    public void TheCommitterIsTheAuthorAsGitsSettingsGiveIt()
    {
        using var repo = TestRepo.Create();
        repo.Git("config", "--local", "author.name", "Test Pen Name");
        repo.Git("config", "--local", "author.email", "pen@example.invalid");
        repo.Git("config", "--local", "committer.name", "Someone Else");
        repo.Git("config", "--local", "committer.email", "else@example.invalid");

        Assert.True(repo.History.Checkpoint(new CheckpointRequest("First"), Token).IsAvailable);

        Assert.Equal(PenName, repo.Git("log", "-1", "--format=%an|%ae|%cn|%ce").Trim());
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-24")]
    public void WithNoNameInGitsSettingsNothingIsGuessedAndNothingChanges()
    {
        using var repo = TestRepo.Create();

        // Not the environment's, and not one git would make from this computer's account.
        var options = With(Global(""), new() { ["GIT_AUTHOR_NAME"] = "Everyday Name", ["GIT_AUTHOR_EMAIL"] = "everyday@example.invalid", ["EMAIL"] = "everyday@example.invalid" });
        var refused = Assert.Throws<HistoryException>(() => GitHistory.ForDen(repo.DenRoot, options, Token).Checkpoint(new CheckpointRequest("First"), Token));

        Assert.StartsWith("Git has no name and email in its settings for this den, so nothing was recorded.", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("everyday", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("git config --local user.name", refused.Message, StringComparison.Ordinal);

        // The new file wasn't even marked for the commit.
        Assert.Equal("?? den.yaml\n", repo.Git("status", "--porcelain", "--untracked-files=all"));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-25")]
    [InlineData("include")]
    [InlineData("worktree")]
    [InlineData("count")]
    [InlineData("parameters")]
    public void SigningTurnedOnOutsideTheDensOwnFileDoesntSignAndTheResultSaysSo(string how)
    {
        using var repo = TestRepo.Create();
        var (signer, attempts) = Signer();
        var options = Global($"[user]\n\tname = Test Pen Name\n\temail = pen@example.invalid\n\tsigningkey = KEY-OF-SOMEONE\n{signer}");
        repo.WriteDen("notes/one.md", "# One\n");
        RunGit(repo.Root, options, "config", "--local", "commit.gpgsign", "false");
        RunGit(repo.Root, options, "config", "--local", "tag.gpgsign", "false");
        options = TurnSigningOn(repo, options, how);
        var history = GitHistory.ForDen(repo.DenRoot, options, Token);

        var made = history.Checkpoint(new CheckpointRequest("First"), Token);
        var tagged = history.Tag(new TagRequest("book/kestrel/first/final", Message: "Freeze"), Token);

        Assert.True(made.IsAvailable, made.Reason);
        Assert.True(tagged.IsAvailable, tagged.Reason);
        Assert.False(File.Exists(attempts), "git tried to sign");
        var warning = Assert.Single(made.Warnings);
        Assert.StartsWith("Git's settings would have signed this checkpoint, but this den's own .git/config says not to, so Wolfden didn't sign it.", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("KEY-OF-SOMEONE", warning, StringComparison.Ordinal);
        Assert.StartsWith("Git's settings would have signed this tag", Assert.Single(tagged.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-25")]
    public void TheDensOwnFileSayingSignWinsOverAnEnvironmentThatSaysNot()
    {
        using var repo = TestRepo.Create();
        var (signer, attempts) = Signer();
        var options = Global($"[user]\n\tname = Test Pen Name\n\temail = pen@example.invalid\n\tsigningkey = KEY-OF-THE-PEN-NAME\n{signer}");
        repo.WriteDen("notes/one.md", "# One\n");
        RunGit(repo.Root, options, "config", "--local", "commit.gpgsign", "true");
        options = With(options, new() { ["GIT_CONFIG_COUNT"] = "1", ["GIT_CONFIG_KEY_0"] = "commit.gpgsign", ["GIT_CONFIG_VALUE_0"] = "false" });

        var made = GitHistory.ForDen(repo.DenRoot, options, Token).Checkpoint(new CheckpointRequest("First"), Token);

        Assert.True(made.IsAvailable, made.Reason);
        Assert.True(File.Exists(attempts), "git didn't sign");
        Assert.StartsWith("This den's own .git/config says to sign, so Wolfden signed this checkpoint", Assert.Single(made.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-25")]
    public void WithNothingInTheDensOwnFileGitDecidesAsBeforeAndNothingIsSaid()
    {
        using var repo = TestRepo.Create();
        var (signer, attempts) = Signer();
        var options = Global($"[user]\n\tname = Test Pen Name\n\temail = pen@example.invalid\n\tsigningkey = KEY-OF-THE-PEN-NAME\n[commit]\n\tgpgsign = true\n[tag]\n\tgpgsign = true\n{signer}");
        repo.WriteDen("notes/one.md", "# One\n");
        var history = GitHistory.ForDen(repo.DenRoot, options, Token);

        var made = history.Checkpoint(new CheckpointRequest("First"), Token);
        var light = history.Tag(new TagRequest("light"), Token);

        Assert.True(made.IsAvailable, made.Reason);
        Assert.Empty(made.Warnings);
        Assert.True(File.Exists(attempts), "git didn't sign");

        // A lightweight tag is still never signed, whatever tag.gpgsign says, and records no one.
        Assert.True(light.IsAvailable, light.Reason);
        Assert.Empty(light.Warnings);
        Assert.Equal("commit", RunGit(repo.Root, options, "cat-file", "-t", "refs/tags/light").Trim());
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-25")]
    public void AValueWithoutAnEqualsSignInTheDensOwnFileIsPinnedAsOn()
    {
        using var repo = TestRepo.Create();
        var (signer, attempts) = Signer();
        var options = Global($"[user]\n\tname = Test Pen Name\n\temail = pen@example.invalid\n\tsigningkey = KEY-OF-THE-PEN-NAME\n{signer}");
        repo.WriteDen("notes/one.md", "# One\n");
        File.AppendAllText(Path.Combine(repo.Root, ".git", "config"), "[commit]\n\tgpgsign\n", new UTF8Encoding(false));
        options = With(options, new() { ["GIT_CONFIG_PARAMETERS"] = "'commit.gpgsign=false'" });

        var made = GitHistory.ForDen(repo.DenRoot, options, Token).Checkpoint(new CheckpointRequest("First"), Token);

        Assert.True(made.IsAvailable, made.Reason);
        Assert.True(File.Exists(attempts), "git didn't sign");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData("true", true)]
    [InlineData("Yes", true)]
    [InlineData(" on ", true)]
    [InlineData("false", false)]
    [InlineData("NO", false)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    [InlineData("2", true)]
    [InlineData("-1", true)]
    [InlineData("maybe", null)]
    public void BooleansAreReadAsGitReadsThem(string? value, bool? expected) => Assert.Equal(expected, CommitGate.Bool(value));

    [Theory]
    [Trait("Scenario", "SC-HIST-27")]
    [InlineData("Everyday Name <agent@example.invalid>")]
    [InlineData("everyday   NAME <agent@example.invalid>")]
    [InlineData("Claude <Everyday@Example.invalid>")]
    [InlineData("Other Everyday <other-everyday@example.invalid>")]
    public void ACoAuthorWithGitsGlobalNameOrEmailIsRefusedWhenTheDenRecordsAnother(string coAuthor)
    {
        using var repo = TestRepo.Create();
        var options = Global(
            "[user]\n\tname = Everyday Name\n\temail = everyday@example.invalid\n[author]\n\tname = Other Everyday\n\temail = other-everyday@example.invalid\n");
        RunGit(repo.Root, options, "config", "--local", "user.name", "Test Pen Name");
        RunGit(repo.Root, options, "config", "--local", "user.email", "pen@example.invalid");
        RunGit(repo.Root, options, "config", "--local", "author.name", "Test Pen Name");
        RunGit(repo.Root, options, "config", "--local", "author.email", "pen@example.invalid");
        var history = GitHistory.ForDen(repo.DenRoot, options, Token);

        var refused = Assert.Throws<ArgumentException>(() => history.Checkpoint(new CheckpointRequest("First", CoAuthors: [coAuthor]), Token));

        Assert.StartsWith("The co-author given has a name or email from git's global settings", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("everyday", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("?? den.yaml\n", RunGit(repo.Root, options, "status", "--porcelain", "--untracked-files=all"));

        // The agent's own name is fine.
        var made = history.Checkpoint(new CheckpointRequest("First", CoAuthors: ["Claude <noreply@anthropic.com>"]), Token);
        Assert.True(made.IsAvailable, made.Reason);
        Assert.Equal(PenName, RunGit(repo.Root, options, "log", "-1", "--format=%an|%ae|%cn|%ce").Trim());
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-27")]
    public void ACoAuthorWithGitsGlobalNameIsntRefusedWhenTheDenRecordsThatNameToo()
    {
        using var repo = TestRepo.Create();
        var options = Global("[user]\n\tname = Test Pen Name\n\temail = pen@example.invalid\n");

        var made = GitHistory.ForDen(repo.DenRoot, options, Token).Checkpoint(new CheckpointRequest("First", CoAuthors: ["Test Pen Name <pen@example.invalid>"]), Token);

        Assert.True(made.IsAvailable, made.Reason);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-27")]
    public void ACoAuthorWithAGlobalNameFromAPlainIncludeIsRefused()
    {
        using var repo = TestRepo.Create();
        var extra = Path.Combine(Folder(), "extra");
        File.WriteAllText(extra, "[user]\n\tname = Everyday Name\n\temail = everyday@example.invalid\n", new UTF8Encoding(false));
        var options = Global($"[include]\n\tpath = {extra.Replace('\\', '/')}\n");
        RunGit(repo.Root, options, "config", "--local", "user.name", "Test Pen Name");
        RunGit(repo.Root, options, "config", "--local", "user.email", "pen@example.invalid");
        var history = GitHistory.ForDen(repo.DenRoot, options, Token);

        Assert.Throws<ArgumentException>(() => history.Checkpoint(new CheckpointRequest("First", CoAuthors: ["Everyday Name <agent@example.invalid>"]), Token));
        var refused = Assert.Throws<ArgumentException>(() => history.Checkpoint(new CheckpointRequest("First", CoAuthors: ["Claude <everyday@example.invalid>"]), Token));

        Assert.DoesNotContain("everyday", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Parameter", refused.Message, StringComparison.Ordinal);
        Assert.Equal("?? den.yaml\n", RunGit(repo.Root, options, "status", "--porcelain", "--untracked-files=all"));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-27")]
    public void APenNameFromIncludeIfIsTheDensNameNotAGlobalOne()
    {
        using var repo = TestRepo.Create();
        var pen = Path.Combine(Folder(), "pen");
        File.WriteAllText(pen, "[user]\n\tname = Test Pen Name\n\temail = pen@example.invalid\n", new UTF8Encoding(false));
        var root = Path.GetFullPath(repo.Root).Replace('\\', '/').TrimEnd('/');
        var options = Global(
            "[user]\n\tname = Everyday Name\n\temail = everyday@example.invalid\n" +
            $"[includeIf \"gitdir/i:{root}/\"]\n\tpath = {pen.Replace('\\', '/')}\n");
        var history = GitHistory.ForDen(repo.DenRoot, options, Token);

        Assert.Throws<ArgumentException>(() => history.Checkpoint(new CheckpointRequest("First", CoAuthors: ["Everyday Name <agent@example.invalid>"]), Token));
        var made = history.Checkpoint(new CheckpointRequest("First", CoAuthors: ["Claude <noreply@anthropic.com>"]), Token);

        Assert.True(made.IsAvailable, made.Reason);
        Assert.Equal(PenName, RunGit(repo.Root, options, "log", "-1", "--format=%an|%ae|%cn|%ce").Trim());
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-27")]
    public void WithNoGlobalConfigAtAllACoAuthorIsntRefused()
    {
        using var repo = TestRepo.Create();
        repo.Git("config", "--local", "user.name", "Test Pen Name");
        repo.Git("config", "--local", "user.email", "pen@example.invalid");
        var options = With(TestRepo.Options, new() { ["GIT_CONFIG_GLOBAL"] = Path.Combine(Folder(), "no-such-file") });

        var made = GitHistory.ForDen(repo.DenRoot, options, Token).Checkpoint(new CheckpointRequest("First", CoAuthors: ["Claude <noreply@anthropic.com>"]), Token);

        Assert.True(made.IsAvailable, made.Reason);
    }

    /// <summary>
    /// Something git reads after the den's own <c>.git/config</c> that turns signing on for
    /// commits and tags: <paramref name="how"/> is <c>include</c>, <c>worktree</c>,
    /// <c>count</c> (<c>GIT_CONFIG_COUNT</c>) or <c>parameters</c> (<c>GIT_CONFIG_PARAMETERS</c>).
    /// </summary>
    private GitOptions TurnSigningOn(TestRepo repo, GitOptions options, string how)
    {
        switch (how)
        {
            case "include":
                var included = Path.Combine(Folder(), "signs");
                File.WriteAllText(included, "[commit]\n\tgpgsign = true\n[tag]\n\tgpgsign = true\n", new UTF8Encoding(false));
                File.AppendAllText(Path.Combine(repo.Root, ".git", "config"), $"[include]\n\tpath = {included.Replace('\\', '/')}\n", new UTF8Encoding(false));
                return options;
            case "worktree":
                RunGit(repo.Root, options, "config", "--local", "extensions.worktreeConfig", "true");
                RunGit(repo.Root, options, "config", "--worktree", "commit.gpgsign", "true");
                RunGit(repo.Root, options, "config", "--worktree", "tag.gpgsign", "true");
                return options;
            case "count":
                return With(options, new()
                {
                    ["GIT_CONFIG_COUNT"] = "2",
                    ["GIT_CONFIG_KEY_0"] = "commit.gpgsign",
                    ["GIT_CONFIG_VALUE_0"] = "true",
                    ["GIT_CONFIG_KEY_1"] = "tag.gpgsign",
                    ["GIT_CONFIG_VALUE_1"] = "true",
                });
            default:
                return With(options, new() { ["GIT_CONFIG_PARAMETERS"] = "'commit.gpgsign=true' 'tag.gpgsign=true'" });
        }
    }

    /// <summary>
    /// Config for a signing program of the test's own that works, as gpg does, and notes each
    /// time git asks it to sign in the file it returns, so "attempts" says whether git signed.
    /// </summary>
    private (string Config, string Attempts) Signer()
    {
        var folder = Folder();
        var attempts = Path.Combine(folder, "attempts").Replace('\\', '/');
        var program = Path.Combine(folder, "signer.sh");
        File.WriteAllText(
            program,
            "#!/bin/sh\n" +
            "case \"$*\" in\n" +
            "*--verify*) echo '[GNUPG:] NEWSIG'; echo '[GNUPG:] GOODSIG 0000 Test Pen Name <pen@example.invalid>' ;;\n" +
            $"*) echo \"$*\" >> \"{attempts}\"; cat > /dev/null; echo '[GNUPG:] SIG_CREATED D 1 8 00 0 0000' >&2\n" +
            "   echo '-----BEGIN PGP SIGNATURE-----'; echo; echo 'dGVzdA=='; echo '-----END PGP SIGNATURE-----' ;;\n" +
            "esac\n",
            new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return ($"[gpg]\n\tprogram = {program.Replace('\\', '/')}\n", attempts);
    }

    /// <summary>The tests' git, with a global config of the test's own holding <paramref name="text"/>, and no name or email from the environment.</summary>
    private GitOptions Global(string text)
    {
        var file = Path.Combine(Folder(), "gitconfig");
        File.WriteAllText(file, "[init]\n\tdefaultBranch = main\n[core]\n\tautocrlf = false\n" + text, new UTF8Encoding(false));
        return With(TestRepo.Options, new() { ["GIT_CONFIG_GLOBAL"] = file, ["EMAIL"] = null });
    }

    private static GitOptions With(GitOptions options, Dictionary<string, string?> more)
    {
        var environment = new Dictionary<string, string?>(options.Environment);
        foreach (var (name, value) in more)
        {
            environment[name] = value;
        }

        return options with { Environment = environment };
    }

    /// <summary>Runs git with <paramref name="options"/>' configuration; a failure fails the test.</summary>
    private static string RunGit(string folder, GitOptions options, params string[] args)
    {
        var run = GitProcess.Run(options, folder, args, cancellationToken: Token);
        Assert.True(run.Succeeded, run.Said);
        return run.Output.ReplaceLineEndings("\n");
    }

    private string Folder()
    {
        var folder = TestRepo.NewFolder();
        _folders.Add(folder);
        return folder;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Wolfden.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Couldn't find the repo root (Wolfden.slnx) above the test binaries.");
    }
}

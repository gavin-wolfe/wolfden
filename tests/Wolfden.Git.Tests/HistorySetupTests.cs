using System.Text;
using Wolfden.Core.History;

namespace Wolfden.Git.Tests;

/// <summary>
/// Turning history on for a den that has none (#195), with real
/// repositories in temporary folders. Git runs with a configuration of the
/// tests' own: the tests' usual one, or, for a missing name or email, a
/// global config file of their own with none, so the developer's own
/// settings are never read or written.
/// </summary>
public sealed class HistorySetupTests : IDisposable
{
    private const string Ilse = "universes/kestrel/entities/characters/ilse-tarrow.md";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly List<string> _folders = [];

    public void Dispose()
    {
        foreach (var folder in _folders)
        {
            TestRepo.DeleteFolder(folder);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-14")]
    public void ADenWithoutHistoryGetsARepositoryAGitignoreAndAFirstCheckpoint()
    {
        var den = Den();
        var setup = new GitHistorySetup(den, TestRepo.Options);
        Assert.Same(NoHistory.Instance, GitHistory.ForDen(den, TestRepo.Options, Token));

        var check = setup.Check(Token);
        Assert.True(check.Possible);
        Assert.False(check.NeedsName);
        Assert.False(check.NeedsEmail);
        Assert.False(check.Finishing);

        var result = setup.TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Null(result.Code);
        Assert.True(result.RepositoryMade);
        Assert.Equal("created", result.GitIgnore);
        Assert.Empty(result.IdentitySaved);
        Assert.Equal(
            [
                "Made the den's folder a git repository.",
                "Created .gitignore, with .wolfden/, so Wolfden's index stays out of the history.",
                "Made the first checkpoint, \"Start of history\", of 2 files.",
            ],
            result.Steps);
        Assert.Equal("# Wolfden's index, rebuilt from the den's files\n.wolfden/\n", File.ReadAllText(Path.Combine(den, ".gitignore")));

        var history = GitHistory.ForDen(den, TestRepo.Options, Token);
        Assert.Equal(HistoryCapabilities.All, history.Capabilities);
        var log = history.Log(new HistoryLogQuery(), Token).Value;
        var first = Assert.Single(log);
        Assert.Equal(result.Checkpoint!.Id, first.Id);
        Assert.Equal("Start of history", first.Message.TrimEnd('\n'));
        Assert.Equal("Test Writer", first.Author);
        Assert.Equal(["den.yaml", Ilse], first.Changes.Select(c => c.Path).Order(StringComparer.Ordinal));

        // Nothing was saved in the repository's own config: git had a name and email.
        Assert.DoesNotContain("[user]", File.ReadAllText(Path.Combine(den, ".git", "config")), StringComparison.Ordinal);

        // Now it has history, turning it on again is refused, and nothing changes.
        var again = setup.TurnOn(new HistorySetupRequest(), Token);
        Assert.Equal(HistorySetupStatus.Refused, again.Status);
        Assert.Equal(HistorySetupCodes.HasRepository, again.Code);
        Assert.Single(history.Log(new HistoryLogQuery(), Token).Value);
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-14")]
    [InlineData("*.tmp\n", "*.tmp\n.wolfden/\n", "added")]
    [InlineData("*.tmp", "*.tmp\n.wolfden/\n", "added")]
    [InlineData("*.tmp\r\n", "*.tmp\r\n.wolfden/\r\n", "added")]
    [InlineData("", ".wolfden/\n", "added")]
    [InlineData("*.tmp\n.wolfden\n", "*.tmp\n.wolfden\n", "present")]
    [InlineData("/.wolfden/  \r\n", "/.wolfden/  \r\n", "present")]

    // Git's own answer: a leading space is part of the pattern, and a later negation wins.
    [InlineData(" .wolfden/\n", " .wolfden/\n.wolfden/\n", "added")]
    [InlineData(".wolfden/\n!.wolfden/\n", ".wolfden/\n!.wolfden/\n.wolfden/\n", "added")]
    public void AnExistingGitignoreKeepsWhatItHasAndGetsTheLineOnlyIfItsMissing(string before, string after, string outcome)
    {
        var den = Den();
        File.WriteAllText(Path.Combine(den, ".gitignore"), before, new UTF8Encoding(false));

        var result = new GitHistorySetup(den, TestRepo.Options).TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Equal(outcome, result.GitIgnore);
        Assert.Equal(after, File.ReadAllText(Path.Combine(den, ".gitignore")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-14")]
    public void WithoutTheFirstCheckpointTheRepositoryHasNone()
    {
        var den = Den();

        var result = new GitHistorySetup(den, TestRepo.Options).TurnOn(new HistorySetupRequest(FirstCheckpoint: false), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Null(result.Checkpoint);
        Assert.Equal("Made no first checkpoint, as asked.", result.Steps[^1]);
        var history = GitHistory.ForDen(den, TestRepo.Options, Token);
        Assert.Empty(history.Log(new HistoryLogQuery(), Token).Value);
        Assert.Equal(2, history.Pending(Token).Value.Count);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-15")]
    public void AMissingNameAndEmailAreAskedForAndSavedForTheDensRepositoryOnly()
    {
        var den = Den();
        var global = Config("");
        var options = Without(global);
        var setup = new GitHistorySetup(den, options);

        var check = setup.Check(Token);
        Assert.True(check.Possible);
        Assert.True(check.NeedsName);
        Assert.True(check.NeedsEmail);
        Assert.False(check.IdentityCertain);

        // Not given: refused, and the repository made to ask git is taken away again.
        var refused = setup.TurnOn(new HistorySetupRequest(), Token);
        Assert.Equal(HistorySetupStatus.Refused, refused.Status);
        Assert.Equal(HistorySetupCodes.IdentityNeeded, refused.Code);
        Assert.True(refused.NeedsName);
        Assert.True(refused.NeedsEmail);
        Assert.Contains("no name or email", refused.Problem, StringComparison.Ordinal);
        Assert.Contains("placeholder", refused.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
        Assert.False(File.Exists(Path.Combine(den, ".gitignore")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(den, ".git*")); // the folder moved aside was deleted too

        // Given: a pen name and a placeholder email, saved in the repository's own config.
        var result = setup.TurnOn(new HistorySetupRequest("  Ash Calloway ", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Equal(["user.name", "user.email"], result.IdentitySaved);
        Assert.Contains("Saved your name and email for this den's repository only.", result.Steps);
        Assert.Equal("Ash Calloway\n", TestRepo.RunGit(den, "config", "--local", "--get", "user.name"));
        Assert.Equal("ash@example.invalid\n", TestRepo.RunGit(den, "config", "--local", "--get", "user.email"));
        Assert.Equal("", File.ReadAllText(global));
        Assert.Equal("Ash Calloway", GitHistory.ForDen(den, options, Token).Log(new HistoryLogQuery(), Token).Value[0].Author);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-15")]
    public void WhenGitHasOnlyOneBothAreAskedForAndOneAloneIsRefused()
    {
        var den = Den();
        var global = Config("[user]\n\tname = Marek Oswin\n");
        var options = Without(global);
        var setup = new GitHistorySetup(den, options);

        var check = setup.Check(Token);
        Assert.True(check.NeedsName);
        Assert.True(check.NeedsEmail);

        var nothing = setup.TurnOn(new HistorySetupRequest(), Token);
        Assert.Equal(HistorySetupCodes.IdentityNeeded, nothing.Code);
        Assert.True(nothing.NeedsName);
        Assert.True(nothing.NeedsEmail);
        Assert.Contains("Give a name and an email", nothing.Problem, StringComparison.Ordinal);

        var nameAlone = setup.TurnOn(new HistorySetupRequest("Ash Calloway"), Token);
        Assert.Equal(HistorySetupCodes.IdentityNeeded, nameAlone.Code);
        Assert.False(nameAlone.NeedsName);
        Assert.True(nameAlone.NeedsEmail);

        // The email Wolfden would once have taken alone, beside the name git has.
        var emailAlone = setup.TurnOn(new HistorySetupRequest(null, "ash@example.invalid"), Token);
        Assert.Equal(HistorySetupStatus.Refused, emailAlone.Status);
        Assert.Equal(HistorySetupCodes.IdentityHalfGiven, emailAlone.Code);
        Assert.True(emailAlone.NeedsName);
        Assert.False(emailAlone.NeedsEmail);
        Assert.DoesNotContain("Marek", emailAlone.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
        Assert.False(File.Exists(Path.Combine(den, ".gitignore")));

        var result = setup.TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Equal(["user.name", "user.email"], result.IdentitySaved);
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
        Assert.Equal("[user]\n\tname = Marek Oswin\n", File.ReadAllText(global));
    }

    [Theory]
    [InlineData("Ash\nCalloway", null, "The name must be one line")]
    [InlineData("Ash <Calloway>", null, "can't hold '<' or '>'")]
    [InlineData("Ash Calloway", "ash@example.invalid\t", null)]
    [InlineData(null, "a\u0007b", "The email must be one line")]
    public void ANameOrEmailThatGitCantRecordIsRefused(string? name, string? email, string? problem)
    {
        var den = Den();

        var result = new GitHistorySetup(den, TestRepo.Options).TurnOn(new HistorySetupRequest(name, email), Token);

        if (problem is null)
        {
            // Trimmed first, so a trailing tab is fine.
            Assert.Equal(HistorySetupStatus.Done, result.Status);
            return;
        }

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.BadArgument, result.Code);
        Assert.Contains(problem, result.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
    }

    [Fact]
    public void ATooLongNameIsRefused()
    {
        Assert.Equal("The name is at most 200 characters, not 201.", HistorySetup.IdentityProblem("name", new string('a', 201)));
        Assert.Null(HistorySetup.IdentityProblem("name", new string('a', 200)));
        Assert.Equal("The email can't be empty.", HistorySetup.IdentityProblem("email", "  "));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void ADenInsideALargerRepositoryIsRefusedWithNothingChanged()
    {
        using var repo = TestRepo.Create("notes/kestrel");

        var result = new GitHistorySetup(repo.DenRoot, TestRepo.Options).TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.InRepository, result.Code);
        Assert.Contains("which keeps its history already", result.Problem, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repo.DenRoot, ".gitignore")));
        Assert.False(Directory.Exists(Path.Combine(repo.DenRoot, ".git")));
        Assert.False(new GitHistorySetup(repo.DenRoot, TestRepo.Options).Check(Token).Possible);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void ADenWhoseOwnRepositoryHasCheckpointsIsRefused()
    {
        using var repo = TestRepo.Create();
        var head = repo.CommitAll("Start");

        var result = new GitHistorySetup(repo.DenRoot, TestRepo.Options).TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.HasRepository, result.Code);
        Assert.Equal(head, repo.Git("rev-parse", "HEAD").Trim());
        Assert.False(File.Exists(Path.Combine(repo.DenRoot, ".gitignore")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-16")]
    public void ADenWhoseOwnRepositoryHasNoCheckpointsIsFinished()
    {
        using var repo = TestRepo.Create();
        var setup = new GitHistorySetup(repo.DenRoot, TestRepo.Options);
        Assert.True(setup.Check(Token).Finishing);

        var result = setup.TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.False(result.RepositoryMade);
        Assert.Equal("Used the git repository already in the den's folder, which had no checkpoints yet.", result.Steps[0]);
        Assert.Equal("Start of history", repo.Git("log", "-1", "--format=%s").Trim());
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-16")]
    public void AFailedFirstCheckpointSaysWhatWasDoneAndTryingAgainFinishes()
    {
        var den = Den();
        var hooks = Folder();
        var hook = Path.Combine(hooks, "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\necho 'notes must be proofread first' >&2\nexit 1\n", new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var global = Config(
            $"[user]\n\tname = Test Writer\n\temail = writer@example.com\n[commit]\n\tgpgsign = false\n[core]\n\tautocrlf = false\n\thooksPath = {hooks.Replace('\\', '/')}\n");
        var setup = new GitHistorySetup(den, Without(global));

        var failed = setup.TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Incomplete, failed.Status);
        Assert.Equal(HistorySetupCodes.HistoryFailed, failed.Code);
        Assert.True(failed.RepositoryMade);
        Assert.Equal("created", failed.GitIgnore);
        Assert.Null(failed.Checkpoint);
        Assert.Equal(2, failed.Steps.Count);
        Assert.Contains("Couldn't make the first checkpoint", failed.Problem, StringComparison.Ordinal);
        Assert.Contains("notes must be proofread first", failed.Problem, StringComparison.Ordinal);
        Assert.Contains("history is on", failed.Problem, StringComparison.Ordinal);
        Assert.Contains("run 'wolfden init --git' in the den again", failed.Problem, StringComparison.Ordinal);

        // The hook is fixed: trying again finishes, with the same repository and .gitignore.
        File.Delete(hook);
        var finished = setup.TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Done, finished.Status);
        Assert.False(finished.RepositoryMade);
        Assert.Equal("present", finished.GitIgnore);
        Assert.NotNull(finished.Checkpoint);
        Assert.Equal("# Wolfden's index, rebuilt from the den's files\n.wolfden/\n", File.ReadAllText(Path.Combine(den, ".gitignore")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void WithoutGitItSaysSoAndChangesNothing()
    {
        var den = Den();
        var setup = new GitHistorySetup(den, TestRepo.Options with { Executable = "wolfden-test-no-such-git" });

        var check = setup.Check(Token);
        var result = setup.TurnOn(new HistorySetupRequest(), Token);

        Assert.False(check.Possible);
        Assert.Equal(HistorySetupCodes.GitNotInstalled, check.Code);
        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.GitNotInstalled, result.Code);
        Assert.StartsWith("Git isn't installed", result.Problem, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(den, ".gitignore")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-15")]
    public void ANameAndEmailGitGivesOnlyInsideTheRepositoryAreUsedAndNothingIsAsked()
    {
        // includeIf "gitdir:…" applies only once the den's folder is a repository.
        var den = Den();
        var included = Config("[user]\n\tname = Ash Calloway\n\temail = ash@example.invalid\n");
        var global = Config($"[includeIf \"gitdir/i:**/{Path.GetFileName(den)}/\"]\n\tpath = {included.Replace('\\', '/')}\n");
        var options = Without(global);
        var setup = new GitHistorySetup(den, options);
        var check = setup.Check(Token);
        Assert.True(check.NeedsName);
        Assert.False(check.IdentityCertain);

        var result = setup.TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Empty(result.IdentitySaved);
        Assert.Equal("Ash Calloway", GitHistory.ForDen(den, options, Token).Log(new HistoryLogQuery(), Token).Value[0].Author);
        Assert.DoesNotContain("[user]", File.ReadAllText(Path.Combine(den, ".git", "config")), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-15")]
    [InlineData(";")]
    [InlineData(" ,;: ")]
    public void ANameGitWontRecordIsRefusedBeforeAnythingIsSaved(string name)
    {
        var den = Den();
        var setup = new GitHistorySetup(den, Without(Config("")));

        var result = setup.TurnOn(new HistorySetupRequest(name, "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.BadArgument, result.Code);
        Assert.StartsWith("Git won't record that name and email on a checkpoint", result.Problem, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was changed.", result.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
        Assert.False(File.Exists(Path.Combine(den, ".gitignore")));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-15")]
    [InlineData(";")]
    [InlineData(",;:")]
    [InlineData("\"'")]
    public void AnEmailGitWouldRecordAsEmptyIsRefusedLikeAName(string email)
    {
        // git var takes these, but records "Ash Calloway <>": the same test as the name's (#207).
        var den = Den();
        var setup = new GitHistorySetup(den, Without(Config("")));

        var result = setup.TurnOn(new HistorySetupRequest("Ash Calloway", email), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.BadArgument, result.Code);
        Assert.StartsWith("Git won't record that email on a checkpoint", result.Problem, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was changed.", result.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-15")]
    public void AnOddEmailGitAlreadyHasIsLeftToGit()
    {
        // Only a given email is checked: one git already has is git's business.
        var den = Den();
        var setup = new GitHistorySetup(den, Without(Config("[user]\n\tname = Ash Calloway\n\temail = \";\"\n")));

        var result = setup.TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Empty(result.IdentitySaved);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void AnEmptyDotGitInsideALargerRepositoryDoesntMakeTheDenItsOwnRepository()
    {
        // A half-synced or half-deleted .git: git doesn't take it for a repository, so the den is in the larger one.
        using var repo = TestRepo.Create("notes/kestrel");
        Directory.CreateDirectory(Path.Combine(repo.DenRoot, ".git"));
        var configBefore = File.ReadAllText(Path.Combine(repo.Root, ".git", "config"));

        var result = new GitHistorySetup(repo.DenRoot, TestRepo.Options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.InRepository, result.Code);
        Assert.Equal(configBefore, File.ReadAllText(Path.Combine(repo.Root, ".git", "config")));
        Assert.Throws<InvalidOperationException>(() => repo.Git("rev-parse", "--verify", "HEAD"));
        Assert.False(File.Exists(Path.Combine(repo.DenRoot, ".gitignore")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void AnOrphanLinkedWorktreeIsRefusedAndTheMainRepositorysConfigIsLeftAlone()
    {
        // git worktree add --orphan: the den's folder is the worktree's top level, with no commits,
        // but its config is the main repository's, shared.
        using var repo = TestRepo.Create();
        repo.CommitAll("Start");
        var den = Path.Combine(Folder(), "kestrel");
        repo.Git("worktree", "add", "--quiet", "--orphan", "-b", "kestrel-notes", den);
        File.WriteAllText(Path.Combine(den, "den.yaml"), "format: 1\n", new UTF8Encoding(false));
        var configBefore = File.ReadAllText(Path.Combine(repo.Root, ".git", "config"));
        var setup = new GitHistorySetup(den, Without(Config("")));

        var result = setup.TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.InRepository, result.Code);
        Assert.Contains("linked worktree", result.Problem, StringComparison.Ordinal);
        Assert.False(setup.Check(Token).Possible);
        Assert.Equal(configBefore, File.ReadAllText(Path.Combine(repo.Root, ".git", "config")));
        Assert.False(File.Exists(Path.Combine(den, ".gitignore")));
        repo.Git("worktree", "remove", "--force", den);
    }

    [Fact]
    public void AGitignoreThatCantBeWrittenSaysSoWithOneFullStop()
    {
        var den = Den();
        Directory.CreateDirectory(Path.Combine(den, ".gitignore"));

        var result = new GitHistorySetup(den, TestRepo.Options).TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Incomplete, result.Status);
        Assert.Equal(HistorySetupCodes.WriteFailed, result.Code);
        Assert.StartsWith("Couldn't update .gitignore: ", result.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("..", result.Problem, StringComparison.Ordinal);
    }

    /// <summary>A den without history: a den.yaml and one note, in a folder outside any repository.</summary>
    [Fact]
    [Trait("Scenario", "SC-HIST-21")]
    public void AGivenNameAndEmailWinOverGitsForTheAuthorAndTheCommitter()
    {
        var den = Den();
        var text = "[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n";
        var global = Config(text);
        var options = Without(global);

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Equal(["user.name", "user.email"], result.IdentitySaved);
        Assert.Contains("Saved your name and email for this den's repository only.", result.Steps);
        Assert.DoesNotContain(result.Steps, s => s.Contains("signing", StringComparison.OrdinalIgnoreCase) || s.Contains("Marek", StringComparison.Ordinal));
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
        Assert.Equal("Ash Calloway\n", RunGit(den, options, "config", "--local", "--get", "user.name"));
        Assert.Equal("ash@example.invalid\n", RunGit(den, options, "config", "--local", "--get", "user.email"));
        Assert.Equal(text, File.ReadAllText(global));
        Assert.Equal(AshOnly, LaterCommitIdentity(den, options));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-21")]
    public void AGivenNameAndEmailWinOverOnesGitGivesOnlyInsideTheRepository()
    {
        var den = Den();
        var included = Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n");
        var global = Config($"[includeIf \"gitdir/i:**/{Path.GetFileName(den)}/\"]\n\tpath = {included.Replace('\\', '/')}\n");
        var options = Without(global);

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-21")]
    public void AGivenNameAndEmailWinOverAuthorAndCommitterSettings()
    {
        // Git prefers these to user.name and user.email, wherever they're set.
        var den = Den();
        var options = Without(Config(
            "[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n" +
            "[author]\n\tname = Marek A\n\temail = a@example.invalid\n[committer]\n\tname = Marek C\n\temail = c@example.invalid\n"));

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Equal(["user.name", "user.email"], result.IdentitySaved);
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-21")]
    public void AGivenNameAndEmailReplaceOnesInARepositoryWithNoCheckpointsYet()
    {
        var den = Den();
        var options = Without(Config(""));
        RunGit(den, options, "init", "--quiet");
        RunGit(den, options, "config", "--local", "user.name", "Marek Oswin");
        RunGit(den, options, "config", "--local", "user.email", "marek@example.invalid");

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.False(result.RepositoryMade);
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-21")]
    [InlineData("GIT_AUTHOR_NAME")]
    [InlineData("GIT_COMMITTER_NAME")]
    [InlineData("GIT_AUTHOR_EMAIL")]
    [InlineData("GIT_COMMITTER_EMAIL")]
    public void AnEnvironmentVariableThatWouldBeatWhatsGivenRefusesWithNothingChanged(string variable)
    {
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n"));
        options = options with { Environment = new Dictionary<string, string?>(options.Environment) { [variable] = "marek" } };

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.BadArgument, result.Code);
        Assert.Contains(variable, result.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("marek", result.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Nothing was changed.", result.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
        Assert.False(File.Exists(Path.Combine(den, ".gitignore")));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-22")]
    [InlineData("Ash Calloway", null, false, true)]
    [InlineData(null, "ash@example.invalid", true, false)]
    [InlineData("Marek Oswin", null, false, true)] // what git has already: it would still sit beside an email not given
    [InlineData(null, "marek@example.invalid", true, false)]
    public void OneGivenAloneIsRefusedWithNothingChangedEvenIfItsWhatGitHas(string? name, string? email, bool needsName, bool needsEmail)
    {
        var den = Den();
        var setup = new GitHistorySetup(den, Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n")));

        var result = setup.TurnOn(new HistorySetupRequest(name, email), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.IdentityHalfGiven, result.Code);
        Assert.Equal(needsName, result.NeedsName);
        Assert.Equal(needsEmail, result.NeedsEmail);
        Assert.DoesNotContain("Marek", result.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Nothing was changed.", result.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
        Assert.False(File.Exists(Path.Combine(den, ".gitignore")));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-23")]
    [InlineData("gpg")]
    [InlineData("ssh")]
    public void GitsKeyIsNotUsedForANameGivenAndSigningStaysOffForTheDen(string format)
    {
        var den = Den();
        var (text, attempts) = SigningConfig(format);
        var global = Config(text);
        var options = Without(global);

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.False(File.Exists(attempts));
        var step = Assert.Single(result.Steps, s => s.StartsWith("Turned signing off for this den's repository only", StringComparison.Ordinal));
        Assert.Contains("user.signingkey", step, StringComparison.Ordinal);
        Assert.Contains("commit.gpgsign true", step, StringComparison.Ordinal);
        Assert.Contains("tag.gpgsign true", step, StringComparison.Ordinal);
        Assert.Contains("local-user", step, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Steps, s => s.Contains("KEY-OF-MAREK", StringComparison.Ordinal));
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
        Assert.Equal("N\n", RunGit(den, options, "log", "-1", "--format=%G?"));
        foreach (var setting in new[] { "commit.gpgsign", "tag.gpgsign", "tag.forcesignannotated", "push.gpgsign" })
        {
            Assert.Equal("false\n", RunGit(den, options, "config", "--local", "--get", setting));
        }

        Assert.Equal(text, File.ReadAllText(global));
        Assert.Equal(AshOnly, LaterCommitIdentity(den, options)); // a later checkpoint, and it doesn't sign either
        Assert.False(File.Exists(attempts));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-23")]
    [InlineData("gpg")]
    [InlineData("ssh")]
    public void ANameGivenWithNoSigningSetUpSaysNothingOfItButStaysOffIfSigningIsTurnedOnLater(string format)
    {
        var den = Den();
        var global = Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n");
        var options = Without(global);

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.DoesNotContain(result.Steps, s => s.Contains("signing", StringComparison.OrdinalIgnoreCase));

        var (text, attempts) = SigningConfig(format);
        File.WriteAllText(global, text, new UTF8Encoding(false));
        Assert.Equal(AshOnly, LaterCommitIdentity(den, options));
        Assert.False(File.Exists(attempts));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-23")]
    [InlineData("gpg", "global")]
    [InlineData("ssh", "global")]
    [InlineData("gpg", "local")]
    [InlineData("ssh", "local")]
    [InlineData("gpg", "includeIf")]
    [InlineData("ssh", "includeIf")]
    public void WhatGitAlreadyHasGivenAgainStillTurnsSigningOffAndSaysSo(string format, string where)
    {
        // Wherever the name comes from: with gpg.ssh.defaultKeyCommand or gpg.conf's local-user,
        // the key git would use has nothing to do with the name, and Wolfden can't see which it is.
        var den = Den();
        var (signing, attempts) = SigningConfig(format, identity: false);
        const string Ash = "[user]\n\tname = Ash Calloway\n\temail = ash@example.invalid\n";
        var options = Without(Config(where switch
        {
            "global" => Ash + signing,
            "includeIf" => signing + $"[includeIf \"gitdir/i:**/{Path.GetFileName(den)}/\"]\n\tpath = {Config(Ash).Replace('\\', '/')}\n",
            _ => signing,
        }));
        if (where == "local")
        {
            RunGit(den, options, "init", "--quiet");
            RunGit(den, options, "config", "--local", "user.name", "Ash Calloway");
            RunGit(den, options, "config", "--local", "user.email", "ash@example.invalid");
        }

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.False(File.Exists(attempts));
        Assert.Contains(result.Steps, s => s.StartsWith("Turned signing off", StringComparison.Ordinal));
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
        Assert.Equal("N\n", RunGit(den, options, "log", "-1", "--format=%G?"));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-23")]
    [InlineData("gpg")]
    [InlineData("ssh")]
    public void NothingGivenLeavesSigningAloneAndItsFailureIsntRepeated(string format)
    {
        var den = Den();
        var (signing, attempts) = SigningConfig(format);
        var options = Without(Config(signing));

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest(), Token);

        Assert.True(File.Exists(attempts));
        Assert.Equal(HistorySetupStatus.Incomplete, result.Status);
        Assert.DoesNotContain("gpgsign", File.ReadAllText(Path.Combine(den, ".git", "config")), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("KEY-OF-MAREK", result.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("marek", result.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.False(RepositoryHasCommits(den, options));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-23")]
    [InlineData("gpg")]
    [InlineData("ssh")]
    public void AKeySetInTheDensOwnRepositoryDoesntSignEitherButIsKeptForTurningItBackOn(string format)
    {
        // A name given always turns signing off (maintainer decision, #286): gpg.conf's local-user could add another key.
        var den = Den();
        var (signing, attempts) = SigningConfig(format);
        var options = Without(Config(signing));
        RunGit(den, options, "init", "--quiet");
        var key = format == "ssh" ? "key::ssh-ed25519 KEY-OF-ASH" : "KEY-OF-ASH";
        RunGit(den, options, "config", "--local", "user.signingkey", key);

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.False(File.Exists(attempts));
        var step = Assert.Single(result.Steps, s => s.StartsWith("Turned signing off", StringComparison.Ordinal));
        Assert.Contains("local-user", step, StringComparison.Ordinal);
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
        Assert.Equal("N\n", RunGit(den, options, "log", "-1", "--format=%G?"));
        Assert.Equal($"{key}\n", RunGit(den, options, "config", "--local", "--get", "user.signingkey"));

        // What the step says to do turns it back on, with that key.
        RunGit(den, options, "config", "--local", "commit.gpgsign", "true");
        File.WriteAllText(Path.Combine(den, "later.md"), "# Later\n", new UTF8Encoding(false));
        Assert.Throws<HistoryException>(() => GitHistory.ForDen(den, options, Token).Checkpoint(new CheckpointRequest("Later"), Token));
        Assert.Contains("KEY-OF-ASH", File.ReadAllText(attempts), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-16")]
    public void AFailedSaveInARepositoryThisRunMadeTakesTheRepositoryAway()
    {
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n"));
        var setup = new GitHistorySetup(den, options) { BeforeStep = FailAt("user.email") };

        var result = setup.TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.HistoryFailed, result.Code);
        Assert.StartsWith("Couldn't save the name and email for the den's repository", result.Problem, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was changed.", result.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
        Assert.False(File.Exists(Path.Combine(den, ".gitignore")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-16")]
    public void AFailedSaveInARepositoryThatWasThereTakesBackWhatWasSaved()
    {
        var den = Den();
        var (text, attempts) = SigningConfig();
        var options = Without(Config(text));
        RunGit(den, options, "init", "--quiet");
        RunGit(den, options, "config", "--local", "user.name", "Marek Local");
        var setup = new GitHistorySetup(den, options) { BeforeStep = FailAt("user.email") };

        var failed = setup.TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Incomplete, failed.Status);
        Assert.Empty(failed.IdentitySaved);
        Assert.DoesNotContain(failed.Steps, s => s.Contains("signing", StringComparison.OrdinalIgnoreCase) || s.StartsWith("Saved", StringComparison.Ordinal));
        Assert.Contains("What this try saved was put back as it was.", failed.Problem, StringComparison.Ordinal);
        Assert.Contains("--author-name", failed.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("checkpoint dialog", failed.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("Ash", failed.Problem, StringComparison.Ordinal);
        Assert.Equal("Marek Local\n", RunGit(den, options, "config", "--local", "--get", "user.name"));
        Assert.DoesNotContain("gpgsign", File.ReadAllText(Path.Combine(den, ".git", "config")), StringComparison.OrdinalIgnoreCase);
        Assert.False(RepositoryHasCommits(den, options));
        Assert.False(File.Exists(attempts));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-16")]
    public void TakingBackKeepsASettingWithNoValueAndAnEmptyOne()
    {
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n"));
        RunGit(den, options, "init", "--quiet");
        File.AppendAllText(Path.Combine(den, ".git", "config"), "[commit]\n\tgpgsign\n[push]\n\tgpgsign =\n", new UTF8Encoding(false));
        var setup = new GitHistorySetup(den, options) { BeforeStep = FailAt("user.email") };

        var failed = setup.TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Contains("What this try saved was put back as it was.", failed.Problem, StringComparison.Ordinal);

        // No value means true, and git can't write that, so it comes back as what it means.
        Assert.Equal("true\n", RunGit(den, options, "config", "--local", "--type=bool", "--get", "commit.gpgsign"));
        Assert.Equal("\n", RunGit(den, options, "config", "--local", "--get", "push.gpgsign"));
        Assert.Equal(1, GitProcess.Run(options, den, ["config", "--local", "--get", "tag.gpgsign"], cancellationToken: Token).ExitCode);
        Assert.Equal(1, GitProcess.Run(options, den, ["config", "--local", "--get", "user.name"], cancellationToken: Token).ExitCode);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void ARepositoryThisRunMadeInAFolderNamedReinitializedIsStillTakenAway()
    {
        // git init names the folder, so only the start of what it says tells a new repository from one found.
        var den = Path.Combine(Folder(), "Reinitialized");
        Directory.CreateDirectory(den);
        File.WriteAllText(Path.Combine(den, "den.yaml"), "format: 1\n", new UTF8Encoding(false));

        var result = new GitHistorySetup(den, Without(Config(""))).TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupCodes.IdentityNeeded, result.Code);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-16")]
    public void AHeldConfigThatStopsTheTakingBackSaysWhatsLeftAndAskedAgainItFinishesUnderTheNameGiven()
    {
        // The lock appears after the name is saved, so neither the email nor the taking back can write.
        var den = Den();
        var (text, attempts) = SigningConfig();
        var options = Without(Config(text));
        RunGit(den, options, "init", "--quiet");
        var held = Path.Combine(den, ".git", "config.lock");
        var setup = new GitHistorySetup(den, options)
        {
            BeforeStep = key =>
            {
                if (key == "user.email")
                {
                    File.WriteAllText(held, "");
                }
            },
        };

        var failed = setup.TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Incomplete, failed.Status);
        Assert.Equal(["user.name"], failed.IdentitySaved);
        Assert.Contains("Saved your name for this den's repository only.", failed.Steps);
        Assert.Contains(failed.Steps, s => s.StartsWith("Turned signing off", StringComparison.Ordinal));
        Assert.Contains("still saved for this den: commit.gpgsign, tag.gpgsign, tag.forcesignannotated, push.gpgsign, user.name.", failed.Problem, StringComparison.Ordinal);
        Assert.Contains("--author-name", failed.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("checkpoint dialog", failed.Problem, StringComparison.Ordinal);
        Assert.False(RepositoryHasCommits(den, options));

        File.Delete(held);
        var done = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, done.Status);
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
        Assert.False(File.Exists(attempts));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-21")]
    public void SeveralValuesOfASettingDontStopTheSave()
    {
        var den = Den();
        var options = Without(Config(""));
        RunGit(den, options, "init", "--quiet");
        RunGit(den, options, "config", "--local", "--add", "user.email", "one@example.invalid");
        RunGit(den, options, "config", "--local", "--add", "user.email", "two@example.invalid");

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
        Assert.Equal("ash@example.invalid\n", RunGit(den, options, "config", "--local", "--get-all", "user.email"));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void ARepositoryMadeMeanwhileIsntTakenAwayWhenItsRefused()
    {
        // Between the check and git init, something else makes the repository: this run didn't, so it stays.
        var den = Den();
        var options = Without(Config(""));
        var setup = new GitHistorySetup(den, options) { BeforeStep = key => { if (key == "init") { RunGit(den, options, "init", "--quiet"); } } };

        var result = setup.TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.IdentityNeeded, result.Code);
        Assert.False(result.RepositoryMade);
        Assert.True(Directory.Exists(Path.Combine(den, ".git")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void ARepositoryWithCheckpointsMadeMeanwhileIsRefusedAndKept()
    {
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n"));
        var setup = new GitHistorySetup(den, options)
        {
            BeforeStep = key =>
            {
                if (key == "init")
                {
                    RunGit(den, options, "init", "--quiet");
                    RunGit(den, options, "commit", "--quiet", "--allow-empty", "-m", "Theirs");
                }
            },
        };

        var result = setup.TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupCodes.HasRepository, result.Code);
        Assert.Equal("Theirs\n", RunGit(den, options, "log", "-1", "--format=%s"));
        Assert.Equal(1, GitProcess.Run(options, den, ["config", "--local", "--get", "user.name"], cancellationToken: Token).ExitCode);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-16")]
    public void AGitignoreThatFailsLeavesTheNameSavedSoTryingAgainWithoutItIsStillTheNameGiven()
    {
        var den = Den();
        var ignore = Path.Combine(den, ".gitignore");
        Directory.CreateDirectory(ignore);
        var (text, attempts) = SigningConfig();
        var options = Without(Config(text));
        var setup = new GitHistorySetup(den, options);

        var failed = setup.TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Incomplete, failed.Status);
        Assert.Equal(HistorySetupCodes.WriteFailed, failed.Code);
        Assert.Equal(["user.name", "user.email"], failed.IdentitySaved);
        Assert.Contains(failed.Steps, s => s.StartsWith("Turned signing off", StringComparison.Ordinal));
        Assert.Contains("--author-name \"<name>\" --author-email <email>", failed.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("checkpoint dialog", failed.Problem, StringComparison.Ordinal);

        // Following the old advice to the letter, with no options, can't reach git's identity now.
        Directory.Delete(ignore);
        var done = setup.TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Done, done.Status);
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
        Assert.False(File.Exists(attempts));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-16")]
    public void AFailedFirstCheckpointWithANameGivenAsksForItAgainAndNotTheDialog()
    {
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n"));
        RunGit(den, options, "init", "--quiet");
        File.WriteAllText(Path.Combine(den, ".git", "hooks", "pre-commit"), "#!/bin/sh\nexit 1\n", new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(den, ".git", "hooks", "pre-commit"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var failed = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Incomplete, failed.Status);
        Assert.Contains("--author-name", failed.Problem, StringComparison.Ordinal);
        Assert.Contains("stay saved for this den", failed.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("can be recorded under what git has", failed.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("checkpoint dialog", failed.Problem, StringComparison.Ordinal);
        Assert.False(RepositoryHasCommits(den, options));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-23")]
    [InlineData("gpg")]
    [InlineData("ssh")]
    public void ASigningFailureAtACheckpointOrTagDoesntRepeatWhoTheKeyBelongsTo(string format)
    {
        var den = Den();
        var (signing, attempts) = SigningConfig(format);
        var options = Without(Config(signing));
        RunGit(den, options, "init", "--quiet");
        var history = GitHistory.ForDen(den, options, Token);

        var refused = Assert.Throws<HistoryException>(() => history.Checkpoint(new CheckpointRequest("Later"), Token));

        Assert.True(File.Exists(attempts));
        Assert.StartsWith("Git refused the checkpoint, so nothing was recorded (", refused.Message, StringComparison.Ordinal);
        Assert.Contains("run 'git add -A' then 'git commit -m <message>' in the den's folder, which makes the commit if it succeeds", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("KEY-OF-MAREK", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("marek", refused.Message, StringComparison.OrdinalIgnoreCase);

        // A tag signs by tag.gpgsign, whatever commit.gpgsign says.
        RunGit(den, options, "config", "--local", "commit.gpgsign", "false");
        Assert.True(history.Checkpoint(new CheckpointRequest("Start"), Token).IsAvailable);
        var tag = Assert.Throws<HistoryException>(() => history.Tag(new TagRequest("book/final", Message: "Final"), Token));

        Assert.Contains("run 'git tag -m <message> <name>' in the den's folder, which makes the tag if it succeeds", tag.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("marek", tag.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-13")]
    public void WithSigningOffWhatGitSaidIsRepeatedEvenIfItMentionsSigning()
    {
        // A folder and a hook that happen to use the word aren't a signing failure.
        var den = Path.Combine(Folder(), "gpg-notes");
        Directory.CreateDirectory(den);
        File.WriteAllText(Path.Combine(den, "den.yaml"), "format: 1\n", new UTF8Encoding(false));
        var options = TestRepo.Options;
        RunGit(den, options, "init", "--quiet");
        var hook = Path.Combine(den, ".git", "hooks", "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\necho 'hook says: gpg review needed' >&2\nexit 1\n", new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var refused = Assert.Throws<HistoryException>(() => GitHistory.ForDen(den, options, Token).Checkpoint(new CheckpointRequest("Later"), Token));

        Assert.Contains("hook says: gpg review needed", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConfigFilesPathIsLeftOutOfWhatGitSaidAndAFileAHookNamesIsnt()
    {
        foreach (var said in new[]
        {
            "fatal: bad config line 1 in file C:/somewhere/private/gitconfig",
            "fatal: bad numeric config value 'x' for 'core.abbrev' in file C:/somewhere/private/gitconfig: invalid unit",
            "fatal: bad boolean config value 'x' for 'commit.gpgsign' in file C:/somewhere/private/gitconfig",
        })
        {
            Assert.DoesNotContain("private", new GitRun(128, "", said + "\n").Said, StringComparison.Ordinal);
        }

        Assert.Equal("hook: a TODO in file notes.md", new GitRun(1, "", "hook: a TODO in file notes.md\n").Said);
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-23")]
    [InlineData("gpg")]
    [InlineData("ssh")]
    public void AKeyFromATemplateInARepositoryThisRunMadeIsNotChosenForIt(string format)
    {
        var den = Den();
        var template = Folder();
        File.WriteAllText(Path.Combine(template, "config"), "[user]\n\tsigningkey = KEY-OF-TEMPLATE\n", new UTF8Encoding(false));
        var (signing, attempts) = SigningConfig(format);
        var options = Without(Config(signing + $"[init]\n\ttemplateDir = {template.Replace('\\', '/')}\n"));

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.False(File.Exists(attempts));
        Assert.Equal(AshOnly, FirstCommitIdentity(den, options));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-21")]
    [InlineData("count")]
    [InlineData("parameters")]
    public void ASettingFromTheEnvironmentThatBeatsWhatsSavedRefusesBeforeTheFirstCheckpoint(string how)
    {
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n"));
        var environment = new Dictionary<string, string?>(options.Environment);
        if (how == "count")
        {
            environment["GIT_CONFIG_COUNT"] = "1";
            environment["GIT_CONFIG_KEY_0"] = "user.name";
            environment["GIT_CONFIG_VALUE_0"] = "Marek Env";
        }
        else
        {
            environment["GIT_CONFIG_PARAMETERS"] = "'user.name=Marek Env'";
        }

        var result = new GitHistorySetup(den, options with { Environment = environment }).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.BadArgument, result.Code);
        Assert.DoesNotContain("Marek", result.Problem, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was changed.", result.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-23")]
    [InlineData("gpg")]
    [InlineData("ssh")]
    public void ASigningSettingFromTheEnvironmentThatStaysOnRefusesBeforeTheFirstCheckpoint(string format)
    {
        var den = Den();
        var (signing, attempts) = SigningConfig(format);
        var options = Without(Config(signing));
        var environment = new Dictionary<string, string?>(options.Environment)
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "commit.gpgsign",
            ["GIT_CONFIG_VALUE_0"] = "true",
        };

        var result = new GitHistorySetup(den, options with { Environment = environment }).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.False(File.Exists(attempts));
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-21")]
    [InlineData("worktree")]
    [InlineData("include")]
    public void ALaterSettingInTheRepositoryThatBeatsWhatsSavedStopsBeforeTheFirstCheckpoint(string how)
    {
        var den = Den();
        var options = Without(Config(""));
        RunGit(den, options, "init", "--quiet");
        if (how == "worktree")
        {
            RunGit(den, options, "config", "--local", "extensions.worktreeConfig", "true");
            RunGit(den, options, "config", "--worktree", "user.name", "Marek Worktree");
        }
        else
        {
            var included = Config("[user]\n\tname = Marek Included\n");
            RunGit(den, options, "config", "--local", "user.name", "Marek Local");
            File.AppendAllText(Path.Combine(den, ".git", "config"), $"[include]\n\tpath = {included.Replace('\\', '/')}\n", new UTF8Encoding(false));
        }

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Incomplete, result.Status);
        Assert.DoesNotContain("Marek", result.Problem, StringComparison.Ordinal);
        Assert.False(RepositoryHasCommits(den, options));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-21")]
    [Trait("Scenario", "SC-HIST-23")]
    [InlineData("include")]
    [InlineData("worktree")]
    [InlineData("count")]
    [InlineData("parameters")]
    public void WhatBeatsSigningBeingOffGetsItAllTakenBackSoNoLaterCommitSignsTheNameGiven(string how)
    {
        // The signer works here, and something git reads after the den's own settings keeps signing on.
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n\tsigningkey = KEY-OF-MAREK\n" + WorkingSigner()));
        RunGit(den, options, "init", "--quiet");
        var environment = new Dictionary<string, string?>(options.Environment);
        switch (how)
        {
            case "include":
                RunGit(den, options, "config", "--local", "commit.gpgsign", "false");
                File.AppendAllText(Path.Combine(den, ".git", "config"), $"[include]\n\tpath = {Config("[commit]\n\tgpgsign = true\n").Replace('\\', '/')}\n", new UTF8Encoding(false));
                break;
            case "worktree":
                RunGit(den, options, "config", "--local", "extensions.worktreeConfig", "true");
                RunGit(den, options, "config", "--worktree", "commit.gpgsign", "true");
                break;
            case "count":
                environment["GIT_CONFIG_COUNT"] = "1";
                environment["GIT_CONFIG_KEY_0"] = "commit.gpgsign";
                environment["GIT_CONFIG_VALUE_0"] = "true";
                break;
            default:
                environment["GIT_CONFIG_PARAMETERS"] = "'commit.gpgsign=true'";
                break;
        }

        options = options with { Environment = environment };
        var setup = new GitHistorySetup(den, options);

        var failed = setup.TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Incomplete, failed.Status);
        Assert.Equal(HistorySetupCodes.BadArgument, failed.Code);
        Assert.Contains("Git would still sign", failed.Problem, StringComparison.Ordinal);
        Assert.Contains("What this try saved was put back as it was.", failed.Problem, StringComparison.Ordinal);
        Assert.Empty(failed.IdentitySaved);
        Assert.DoesNotContain(failed.Steps, s => s.StartsWith("Saved", StringComparison.Ordinal) || s.StartsWith("Turned signing off", StringComparison.Ordinal));
        Assert.False(RepositoryHasCommits(den, options));
        Assert.Equal(1, GitProcess.Run(options, den, ["config", "--local", "--get", "user.name"], cancellationToken: Token).ExitCode);

        // Trying again with no options: under git's own name, not the pen name, and signed as git is
        // set to. The include's is the exception: its "false" comes back after the include, so it wins.
        var signature = how == "include" ? "N\n" : "G\n";
        var retried = setup.TurnOn(new HistorySetupRequest(), Token);

        Assert.Equal(HistorySetupStatus.Done, retried.Status);
        const string Marek = "Marek Oswin|marek@example.invalid|Marek Oswin|marek@example.invalid\n";
        Assert.Equal(Marek, FirstCommitIdentity(den, options));
        Assert.Equal(signature, RunGit(den, options, "log", "-1", "--format=%G?"));

        // A checkpoint by any other route, as POST /api/checkpoint and auto_checkpoint make.
        Assert.Equal(Marek, LaterCommitIdentity(den, options));
        Assert.Equal(signature, RunGit(den, options, "log", "-1", "--format=%G?"));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-21")]
    public void CharactersGitStripsFromWhatsGivenArentTakenForSomethingBeatingIt()
    {
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n"));

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("A. Calloway Jr.;", ",ash@example.invalid:"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
        Assert.Equal("A. Calloway Jr.|ash@example.invalid|A. Calloway Jr.|ash@example.invalid\n", FirstCommitIdentity(den, options));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-21")]
    public void GitDirInTheEnvironmentRefusesRatherThanSaveInAnotherRepository()
    {
        var den = Den();
        var other = Folder();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n"));
        RunGit(other, options, "init", "--quiet");
        var before = File.ReadAllText(Path.Combine(other, ".git", "config"));
        var environment = new Dictionary<string, string?>(options.Environment) { ["GIT_DIR"] = Path.Combine(other, ".git") };

        var result = new GitHistorySetup(den, options with { Environment = environment }).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Contains("GIT_DIR", result.Problem, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(Path.Combine(other, ".git", "config")));
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-21")]
    [InlineData("Ash\u00A0;")]
    [InlineData("Ash\u3000;")]
    [InlineData("\uFEFFAsh")]
    public void UnusualSpacesAndMarksArentTakenForSomethingBeatingWhatsGiven(string name)
    {
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n"));

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest(name, "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Done, result.Status);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-21")]
    public void ABadDateInTheEnvironmentIsntBlamedOnTheNameBeingBeaten()
    {
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n"));
        options = options with { Environment = new Dictionary<string, string?>(options.Environment) { ["GIT_AUTHOR_DATE"] = "not a date" } };

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Contains("GIT_AUTHOR_DATE", result.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-22")]
    public void AnAuthorEmailAloneIsntAnEmailGitHasSoANameAloneIsAskedForOne()
    {
        var den = Den();
        var options = Without(Config("[author]\n\temail = real@example.invalid\n"));

        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway"), Token);

        Assert.Equal(HistorySetupStatus.Refused, result.Status);
        Assert.Equal(HistorySetupCodes.IdentityNeeded, result.Code);
        Assert.True(result.NeedsEmail);
        Assert.False(Directory.Exists(Path.Combine(den, ".git")));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-17")]
    [InlineData("gpg")]
    [InlineData("ssh")]
    public void ANameGivenToADenWithCheckpointsSaysHowToChangeItAndFollowingThatWorks(string format)
    {
        var den = Den();
        var options = Without(Config("[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n[author]\n\tname = Marek Author\n[committer]\n\temail = committer@example.invalid\n"));
        var setup = new GitHistorySetup(den, options);
        Assert.Equal(HistorySetupStatus.Done, setup.TurnOn(new HistorySetupRequest(), Token).Status);

        // Signing goes on after the first checkpoint, which could otherwise not be made here.
        var (signing, attempts) = SigningConfig(format, identity: false);
        options = Without(Config(signing + "[author]\n\tname = Marek Author\n[committer]\n\temail = committer@example.invalid\n"));
        var result = new GitHistorySetup(den, options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupCodes.HasRepository, result.Code);
        Assert.Contains("weren't applied", result.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("Marek", result.Problem, StringComparison.Ordinal);

        // Every command it names, exactly as it names it, with the writer's values filled in.
        var commands = System.Text.RegularExpressions.Regex.Matches(result.Problem!, "git config --local \\S+ (\"<name>\"|<email>|false)");
        Assert.Equal(10, commands.Count);
        foreach (System.Text.RegularExpressions.Match command in commands)
        {
            // As typed, with the two-word name in the quotes the command shows: the shell's words, without "git".
            var typed = command.Value.Replace("<name>", "Ash Calloway", StringComparison.Ordinal).Replace("<email>", "ash@example.invalid", StringComparison.Ordinal);
            var words = System.Text.RegularExpressions.Regex.Matches(typed, "\"[^\"]*\"|\\S+").Select(word => word.Value.Trim('"')).Skip(1);
            RunGit(den, options, [.. words]);
        }

        Assert.Equal(AshOnly, LaterCommitIdentity(den, options));
        Assert.False(File.Exists(attempts));
        Assert.Equal("N\n", RunGit(den, options, "log", "-1", "--format=%G?"));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void ANameGivenToADenInALargerRepositorySaysItWasntAppliedAndChangesThatRepositorysNothing()
    {
        using var repo = TestRepo.Create("notes/kestrel");
        var before = File.ReadAllText(Path.Combine(repo.Root, ".git", "config"));

        var result = new GitHistorySetup(repo.DenRoot, TestRepo.Options).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.Equal(HistorySetupCodes.InRepository, result.Code);
        Assert.Contains("weren't applied", result.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("git config --local", result.Problem, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(Path.Combine(repo.Root, ".git", "config")));
    }

    [Fact]
    public void ABadConfigLineDoesntNameTheFolderItsIn()
    {
        var den = Den();
        var global = Config("this isn't a config line\n");
        var folder = Path.GetDirectoryName(global)!;

        var result = new GitHistorySetup(den, Without(global)).TurnOn(new HistorySetupRequest("Ash Calloway", "ash@example.invalid"), Token);

        Assert.NotEqual(HistorySetupStatus.Done, result.Status);
        Assert.Contains("bad config line", result.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFileName(folder), result.Problem, StringComparison.Ordinal);
    }

    private const string AshOnly = "Ash Calloway|ash@example.invalid|Ash Calloway|ash@example.invalid\n";

    /// <summary>
    /// A global config that signs by programs of the test's own, which note each attempt in the
    /// file returned, say whose key it is, and fail. No file: nothing tried to sign. For gpg, a
    /// <c>user.signingkey</c> of another name's. For ssh, no key at all, but a
    /// <c>gpg.ssh.defaultKeyCommand</c> that prints one, which git uses whatever the name is.
    /// </summary>
    private (string Text, string Attempts) SigningConfig(string format = "gpg", bool identity = true)
    {
        var folder = Folder();
        var attempts = Path.Combine(folder, "attempts").Replace('\\', '/');
        string Script(string name, string body)
        {
            var program = Path.Combine(folder, name);
            File.WriteAllText(program, $"#!/bin/sh\n{body}\n", new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return program.Replace('\\', '/');
        }

        var who = identity ? "[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n" : "";
        const string Signs = "[commit]\n\tgpgsign = true\n[tag]\n\tgpgsign = true\n";
        if (format == "ssh")
        {
            var keyCommand = Script("key-command.sh", $"echo key-command >> \"{attempts}\"\necho 'key::ssh-ed25519 KEY-OF-MAREK'");
            var ssh = Script("ssh-signer.sh", $"echo \"ssh $*\" >> \"{attempts}\"\ncat \"$6\" >> \"{attempts}\" 2>/dev/null\necho 'agent refused key marek@example.invalid (KEY-OF-MAREK)' >&2\nexit 1");
            return ($"{who}{Signs}[gpg]\n\tformat = ssh\n[gpg \"ssh\"]\n\tprogram = {ssh}\n\tdefaultKeyCommand = {keyCommand}\n", attempts);
        }

        var gpg = Script("signer.sh", $"echo \"$*\" >> \"{attempts}\"\necho 'gpg: skipped \"KEY-OF-MAREK\": Marek Oswin <marek@example.invalid> has no secret key' >&2\nexit 1");
        return ($"{who}[user]\n\tsigningkey = KEY-OF-MAREK\n{Signs}[gpg]\n\tprogram = {gpg}\n", attempts);
    }

    /// <summary>
    /// Config for a signing program of the test's own that succeeds, as gpg does: it answers a
    /// signing with a signature and a verifying with a good one, so <c>%G?</c> reads <c>G</c>.
    /// </summary>
    private string WorkingSigner()
    {
        var program = Path.Combine(Folder(), "working-signer.sh");
        File.WriteAllText(
            program,
            "#!/bin/sh\n" +
            "case \"$*\" in\n" +
            "*--verify*) echo '[GNUPG:] NEWSIG'; echo '[GNUPG:] GOODSIG 0000 Marek Oswin <marek@example.invalid>'; echo '[GNUPG:] TRUST_FULLY 0 pgp' ;;\n" +
            "*) cat > /dev/null; echo '[GNUPG:] BEGIN_SIGNING' >&2; echo '[GNUPG:] SIG_CREATED D 1 8 00 0 0000' >&2\n" +
            "   echo '-----BEGIN PGP SIGNATURE-----'; echo; echo 'dGVzdA=='; echo '-----END PGP SIGNATURE-----' ;;\n" +
            "esac\n",
            new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return $"[gpg]\n\tprogram = {program.Replace('\\', '/')}\n";
    }

    /// <summary>A save that fails at <paramref name="failing"/> and nowhere else.</summary>
    private static Action<string> FailAt(string failing) => key =>
    {
        if (key == failing)
        {
            throw new HistoryException("git config failed");
        }
    };

    private static bool RepositoryHasCommits(string den, GitOptions options) =>
        GitProcess.Run(options, den, ["rev-parse", "--verify", "--quiet", "HEAD^{commit}"], cancellationToken: Token).Succeeded;

    /// <summary>The first commit's author and committer, name then email, with bars between.</summary>
    private static string FirstCommitIdentity(string den, GitOptions options) =>
        RunGit(den, options, "log", "--max-parents=0", "-1", "--format=%an|%ae|%cn|%ce");

    /// <summary>The author and committer of another commit, made as Wolfden makes a checkpoint.</summary>
    private static string LaterCommitIdentity(string den, GitOptions options)
    {
        File.WriteAllText(Path.Combine(den, "later.md"), "# Later\n", new UTF8Encoding(false));
        var made = GitHistory.ForDen(den, options, Token).Checkpoint(new CheckpointRequest("Later"), Token);
        Assert.True(made.IsAvailable, made.Reason);
        return RunGit(den, options, "log", "-1", "--format=%an|%ae|%cn|%ce");
    }

    /// <summary>Runs git with <paramref name="options"/>' configuration; a failure fails the test.</summary>
    private static string RunGit(string folder, GitOptions options, params string[] args)
    {
        var run = GitProcess.Run(options, folder, args, cancellationToken: Token);
        Assert.True(run.Succeeded, run.Said);
        return run.Output.ReplaceLineEndings("\n");
    }

    private string Den()
    {
        var den = Folder();
        File.WriteAllText(Path.Combine(den, "den.yaml"), "format: 1\n", new UTF8Encoding(false));
        var note = Path.Combine(den, Ilse.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(note)!);
        File.WriteAllText(note, "# Ilse Tarrow\n", new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(den, ".wolfden"));
        File.WriteAllText(Path.Combine(den, ".wolfden", "index.db"), "not recorded", new UTF8Encoding(false));
        return den;
    }

    private string Folder()
    {
        var folder = TestRepo.NewFolder();
        _folders.Add(folder);
        return folder;
    }

    /// <summary>A global git config file of the test's own, holding <paramref name="text"/>.</summary>
    private string Config(string text)
    {
        var folder = Folder();
        var file = Path.Combine(folder, "gitconfig");
        File.WriteAllText(file, text, new UTF8Encoding(false));
        return file;
    }

    /// <summary>The tests' git, with <paramref name="global"/> as its global config in place of the usual one.</summary>
    private static GitOptions Without(string global)
    {
        var environment = new Dictionary<string, string?>(TestRepo.Options.Environment)
        {
            ["GIT_CONFIG_GLOBAL"] = global,
            ["EMAIL"] = null,
        };
        return TestRepo.Options with { Environment = environment };
    }
}

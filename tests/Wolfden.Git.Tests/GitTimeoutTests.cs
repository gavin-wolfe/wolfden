using Wolfden.Core.History;

namespace Wolfden.Git.Tests;

/// <summary>
/// Time limits and cancelling (#143, #145): a git that only reads is
/// stopped, but one that writes the user's index or refs is never stopped
/// part way, which would leave <c>.git/index.lock</c> behind. Hooks and
/// filters here wait for a file the test writes, never a fixed time, and
/// give up on their own after <see cref="GiveUpSeconds"/>. Where a test shows
/// Wolfden didn't wait for one, it checks that the script hadn't finished
/// when Wolfden returned, not how long Wolfden took: a busy Windows CI
/// machine can take over half a minute for a checkpoint (#219). The call
/// runs on a thread of its own, and the test stops waiting for it after
/// <see cref="Bound"/>, well before the script would give up, so a
/// regression fails here, named, not by timing out the CI job.
/// </summary>
public sealed class GitTimeoutTests
{
    private const string Mara = "universes/vael/entities/characters/mara-voss.md";

    /// <summary>
    /// How long, by the clock, a waiting script runs if the test never
    /// releases it: longer than <see cref="Bound"/>, so a script still
    /// running when Wolfden returns shows it wasn't waited for.
    /// </summary>
    private const int GiveUpSeconds = 180;

    /// <summary>
    /// How long a test waits for a git call that should return while a
    /// script it started is still running. Far longer than any such call
    /// takes on a busy machine, and shorter than <see cref="GiveUpSeconds"/>.
    /// </summary>
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(2);

    /// <summary>A shell loop that waits until <c>.git/wolfden-&lt;name&gt;-release</c> exists, or <see cref="GiveUpSeconds"/> pass.</summary>
    private static string WaitForRelease(string name) =>
        $"end=$(($(date +%s)+{GiveUpSeconds})); while [ ! -f .git/wolfden-{name}-release ] && [ $(date +%s) -lt $end ]; do sleep 0.1; done";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A shell script that says it has started, waits until it's released, then exits with <paramref name="exitCode"/>.</summary>
    private static string Waiting(string name, int exitCode) =>
        $"""
        #!/bin/sh
        : > .git/wolfden-{name}-started
        {WaitForRelease(name)}
        : > .git/wolfden-{name}-done
        exit {exitCode}

        """;

    [Fact]
    [Trait("Scenario", "SC-HIST-3")]
    public async Task ACheckpointWhoseHookHangsLeavesGitRunningAndPutsTheIndexBack()
    {
        using var repo = TestRepo.Create();
        repo.CommitAll("start");
        var head = repo.Git("rev-parse", "HEAD");
        repo.WriteHook("pre-commit", Waiting("hook", exitCode: 1));
        repo.WriteDen(Mara, "# Mara Voss\n");
        var history = GitHistory.ForDen(repo.DenRoot, TestRepo.Options with { WriteTimeout = TimeSpan.FromSeconds(5) }, Token);
        try
        {
            var stuck = Assert.Throws<HistoryException>(() => history.Checkpoint(new CheckpointRequest("Add Mara"), Token));

            Assert.StartsWith("Git hasn't finished the checkpoint after 5 seconds. Wolfden stopped waiting but left git running", stuck.Message, StringComparison.Ordinal);
            Assert.Contains("signing (a passphrase prompt)", stuck.Message, StringComparison.Ordinal);
            Assert.Contains("hook", stuck.Message, StringComparison.Ordinal);
            await WaitFor(() => File.Exists(GitFile(repo, "wolfden-hook-started")), "the hook to start");

            // Git still holds its lock: it wasn't stopped, and the lock wasn't deleted.
            Assert.True(File.Exists(GitFile(repo, "index.lock")), "Git's index.lock is gone while git is still committing.");
        }
        finally
        {
            Release(repo, "hook");
        }

        // The hook refuses; once git has exited, the new file is untracked again.
        await WaitFor(
            () => File.Exists(GitFile(repo, "wolfden-hook-done")) && !File.Exists(GitFile(repo, "index.lock")) && Status(repo) == $"?? {Mara}\n",
            "git to finish and the new file to be untracked again");
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-3")]
    public async Task ACheckpointGitFinishesAfterTheTimeLimitIsKept()
    {
        using var repo = TestRepo.Create();
        repo.CommitAll("start");
        repo.WriteHook("pre-commit", Waiting("hook", exitCode: 0));
        repo.WriteDen(Mara, "# Mara Voss\n");
        var history = GitHistory.ForDen(repo.DenRoot, TestRepo.Options with { WriteTimeout = TimeSpan.FromSeconds(5) }, Token);
        try
        {
            Assert.Throws<HistoryException>(() => history.Checkpoint(new CheckpointRequest("Add Mara"), Token));
            await WaitFor(() => File.Exists(GitFile(repo, "wolfden-hook-started")), "the hook to start");
        }
        finally
        {
            Release(repo, "hook");
        }

        await WaitFor(
            () => !File.Exists(GitFile(repo, "index.lock")) && repo.Git("log", "-1", "--format=%s") == "Add Mara\n",
            "git to record the checkpoint");
        Assert.Equal($"{Mara}\n", repo.Git("show", "--name-only", "--format=", "HEAD"));
        Assert.Equal("", Status(repo));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-3")]
    public async Task ACheckpointCancelledWhileGitCommitsIsStillRecorded()
    {
        using var repo = TestRepo.Create();
        repo.CommitAll("start");
        repo.WriteHook("pre-commit", Waiting("hook", exitCode: 0));
        repo.WriteDen(Mara, "# Mara Voss\n");

        // Cancel while the pre-commit hook runs, then let the hook finish.
        // Stopping git there would leave index.lock behind (#145).
        using var cancel = new CancellationTokenSource();
        var watch = Task.Run(
            async () =>
            {
                try
                {
                    await WaitFor(() => File.Exists(GitFile(repo, "wolfden-hook-started")), "the hook to start");
                    await cancel.CancelAsync();
                }
                finally
                {
                    Release(repo, "hook");
                }
            },
            Token);

        var entry = repo.History.Checkpoint(new CheckpointRequest("Add Mara"), cancel.Token).Value;
        await watch;

        Assert.True(cancel.IsCancellationRequested);
        Assert.NotNull(entry);
        Assert.Equal("Add Mara", entry.Message);
        Assert.Equal(repo.Git("rev-parse", "HEAD").Trim(), entry.Id);
        Assert.False(File.Exists(GitFile(repo, "index.lock")));
        Assert.Equal("", Status(repo));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-3")]
    public async Task AHooksBackgroundChildDoesntHoldTheCheckpointUp()
    {
        using var repo = TestRepo.Create();
        repo.CommitAll("start");

        // Like a post-commit 'git push &': the child keeps git's stderr open
        // after git has exited, until the test releases it.
        repo.WriteHook("post-commit", $"#!/bin/sh\n{BackgroundChild}\nexit 0\n");
        repo.WriteDen(Mara, "# Mara Voss\n");
        try
        {
            // The default limits. A checkpoint that waited for the child's
            // output to close would return only once the child had ended.
            var entry = (await WithinBound(() => repo.History.Checkpoint(new CheckpointRequest("Add Mara"), Token), "The checkpoint waited on the hook's child.")).Value;

            Assert.NotNull(entry);
            Assert.Equal("Add Mara", entry.Message);
            Assert.False(File.Exists(GitFile(repo, "wolfden-child-done")), "The checkpoint waited on the hook's child.");
        }
        finally
        {
            await EndBackgroundChild(repo);
        }
    }

    [Fact]
    public async Task OutputAChildHoldsOpenIsReportedIncompleteAndNotUsed()
    {
        using var repo = TestRepo.Create();
        var options = TestRepo.Options with { ReadTimeout = TimeSpan.FromSeconds(5) };
        try
        {
            // An alias runs in the shell: it prints, then leaves a child
            // holding git's standard output open after git has exited.
            var run = await WithinBound(
                () => GitProcess.Run(options, repo.Root, ["-c", $"alias.wolfden-partial=!printf partial; {BackgroundChild}", "wolfden-partial"], cancellationToken: Token),
                "Git's output was waited for until the child ended.");

            Assert.True(run.Succeeded, run.Said);
            Assert.False(run.OutputComplete);
            Assert.Equal("partial", run.Output);
            Assert.False(File.Exists(GitFile(repo, "wolfden-child-done")), "Git's output was waited for until the child ended.");
            var refused = Assert.Throws<HistoryException>(() => GitHistory.OutputOf(run, "wolfden-partial"));
            Assert.StartsWith("git wolfden-partial finished, but its output was still open when Wolfden stopped waiting", refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            await EndBackgroundChild(repo);
        }
    }

    /// <summary>A shell line that starts a child holding the shell's output open until the test releases it, or <see cref="GiveUpSeconds"/> pass.</summary>
    private static readonly string BackgroundChild =
        $"({WaitForRelease("child")}; : > .git/wolfden-child-done) &";

    /// <summary>
    /// Releases the <see cref="BackgroundChild"/> and waits until it has
    /// gone, so no process outlives the test or holds its folder. A pid
    /// wouldn't do: under Git for Windows, the shell's pids aren't Windows's.
    /// </summary>
    private static async Task EndBackgroundChild(TestRepo repo)
    {
        Release(repo, "child");
        await WaitFor(() => File.Exists(GitFile(repo, "wolfden-child-done")), "the background child to end");
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-3")]
    public void ACancelledCheckpointLeavesTheIndexAsItWas()
    {
        using var repo = TestRepo.Create();
        repo.CommitAll("start");
        repo.WriteDen(Mara, "# Mara Voss\n");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => repo.History.Checkpoint(new CheckpointRequest("Add Mara"), cancelled.Token));

        Assert.Equal($"?? {Mara}\n", Status(repo));
        Assert.Equal("start\n", repo.Git("log", "-1", "--format=%s"));
    }

    [Fact]
    public async Task AReadThatHangsIsStopped()
    {
        using var repo = TestRepo.Create();
        repo.CommitAll("start");

        // A clean filter that hangs: comparing with the files as they are
        // now runs it, through Wolfden's temporary index.
        repo.Git(
            "config",
            "filter.wolfden-slow.clean",
            $": > .git/wolfden-filter-started; {WaitForRelease("filter")}; : > .git/wolfden-filter-done; cat");
        repo.WriteRepo(".gitattributes", "*.md filter=wolfden-slow\n");
        repo.WriteDen(Mara, "# Mara Voss\n");
        var history = GitHistory.ForDen(repo.DenRoot, TestRepo.Options with { ReadTimeout = TimeSpan.FromSeconds(10) }, Token);
        try
        {
            var stopped = Assert.Throws<HistoryException>(() => history.Diff(new HistoryDiffQuery(), Token));

            Assert.Equal("git diff didn't finish within 10 seconds, so Wolfden stopped it, and nothing was changed. " +
                "Git may be waiting on a credential helper or a filter (such as Git LFS), or the repository may be very large.", stopped.Message);
            Assert.True(File.Exists(GitFile(repo, "wolfden-filter-started")), "The filter never ran.");

            // The filter would have waited minutes; Wolfden didn't wait for it.
            Assert.False(File.Exists(GitFile(repo, "wolfden-filter-done")), "Wolfden waited for a stopped git's filter.");
            Assert.False(File.Exists(GitFile(repo, "index.lock")));

            // And git was stopped, filter and all: released now, a filter
            // still running would finish within a moment. (A slow machine
            // can only make a filter that's still running look stopped,
            // never the other way round.)
            Release(repo, "filter");
            await Task.Delay(TimeSpan.FromSeconds(3), Token);
            Assert.False(File.Exists(GitFile(repo, "wolfden-filter-done")), "Git's filter was still running after Wolfden gave up on git: it wasn't stopped.");
        }
        finally
        {
            Release(repo, "filter");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ATimeLimitMustBePositive(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GitOptions.Default with { ReadTimeout = TimeSpan.FromSeconds(seconds) });
        Assert.Throws<ArgumentOutOfRangeException>(() => GitOptions.Default with { WriteTimeout = TimeSpan.FromSeconds(seconds) });
        Assert.Equal(Timeout.InfiniteTimeSpan, (GitOptions.Default with { WriteTimeout = Timeout.InfiniteTimeSpan }).WriteTimeout);
    }

    /// <summary>
    /// Runs <paramref name="call"/> on a thread of its own and waits up to
    /// <see cref="Bound"/> for it, failing with <paramref name="failure"/>
    /// if it hasn't returned by then.
    /// </summary>
    private static async Task<T> WithinBound<T>(Func<T> call, string failure)
    {
        var run = Task.Factory.StartNew(call, Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            return await run.WaitAsync(Bound, Token);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"{failure} It hadn't returned after {Bound.TotalMinutes} minutes.");
            throw;
        }
    }

    private static string GitFile(TestRepo repo, string name) => Path.Combine(repo.Root, ".git", name);

    private static void Release(TestRepo repo, string name) => File.WriteAllText(GitFile(repo, $"wolfden-{name}-release"), "");

    /// <summary>The repository's status, read without taking any of git's locks, so it can't get in the way of a git that's still running.</summary>
    private static string Status(TestRepo repo) =>
        TestRepo.RunGit(repo.Root, new Dictionary<string, string?> { ["GIT_OPTIONAL_LOCKS"] = "0" }, "status", "--porcelain", "--untracked-files=all");

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Gave up waiting for {what}.");
            await Task.Delay(50, Token);
        }
    }
}

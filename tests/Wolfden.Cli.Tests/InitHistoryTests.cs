using System.Text;
using System.Text.Json;
using Wolfden.Core.History;
using Wolfden.Git;

namespace Wolfden.Cli.Tests;

/// <summary>
/// <c>wolfden init --git</c> on a folder that's already a den: turning on
/// its history (#195). Git runs with the tests' own configuration, or, for
/// a missing name or email, with a global config file of the test's own
/// that has none, never the developer's.
/// </summary>
public class InitHistoryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Scenario", "SC-HIST-14")]
    public void AnExistingDenGetsHistoryWithAFirstCheckpoint()
    {
        using var den = TempDen.DraculaCopy();
        File.WriteAllText(Path.Combine(den.Root, ".gitignore"), "*.bak\n", new UTF8Encoding(false));

        var (exitCode, output, error) = Cli.Run("init", den.Root, "--git");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Empty(error);
        var history = GitHistory.ForDen(den.Root, TestGit.Options, Token);
        var first = Assert.Single(history.Log(new HistoryLogQuery(), Token).Value);
        Assert.Equal(
            $"Turned on history for the den at '{den.Root}'.\n" +
            "Made the den's folder a git repository.\n" +
            "Added .wolfden/ to .gitignore, so Wolfden's index stays out of the history.\n" +
            $"Made the first checkpoint, \"Start of history\", of {first.Changes.Count} files.\n" +
            "Checkpoints work from now on: in the web UI ('wolfden serve', then Checkpoint…), or through an AI assistant.\n",
            output);
        Assert.Equal("*.bak\n.wolfden/\n", den.Read(".gitignore"));
        Assert.Equal("Start of history", first.Message.TrimEnd('\n'));
        Assert.Contains(first.Changes, c => c.Path == "den.yaml");
        Assert.Empty(history.Pending(Token).Value);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-14")]
    public void FromInsideTheDenWithNoCheckpointItSaysWhatItDidAsJson()
    {
        using var den = TempDen.DraculaCopy();

        var (exitCode, output, error) = Cli.RunIn(Path.Combine(den.Root, "universes"), "init", "..", "--git", "--no-checkpoint", "--json");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Empty(error);
        using var json = JsonDocument.Parse(output);
        Assert.Equal([".gitignore"], json.RootElement.GetProperty("created").EnumerateArray().Select(e => e.GetString()));
        var git = json.RootElement.GetProperty("git");
        Assert.Equal("created", git.GetProperty("outcome").GetString());
        Assert.Equal("created", git.GetProperty("gitIgnore").GetString());
        Assert.Equal("Made no first checkpoint, as asked.", git.GetProperty("steps").EnumerateArray().Last().GetString());
        Assert.False(git.TryGetProperty("checkpoint", out _));
        Assert.False(git.TryGetProperty("code", out _));
        Assert.Empty(GitHistory.ForDen(den.Root, TestGit.Options, Token).Log(new HistoryLogQuery(), Token).Value);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-15")]
    public void AMissingNameAndEmailAreAskedForThenSavedForTheDenOnly()
    {
        using var den = TempDen.DraculaCopy();
        using var config = TempDen.Empty();
        var global = Path.Combine(config.Root, "gitconfig");
        File.WriteAllText(global, "", new UTF8Encoding(false));
        var git = NoIdentity(global);

        var output = new StringWriter();
        var error = new StringWriter();
        var refused = InitCommand.Run([den.Root, "--git"], output, error, git);

        Assert.Equal(CliApp.ExitUsage, refused);
        Assert.Empty(output.ToString());
        Assert.Equal(
            "wolfden init: git records who made each checkpoint, and needs a name and an email for this den. " +
            "Run 'wolfden init --git' again with --author-name \"<name>\" --author-email <email>: they're saved for this den's repository only, never globally. " +
            "Any name will do, a pen name included, and the email can be a placeholder, such as you@example.com. Nothing was changed in git.\n",
            error.ToString().ReplaceLineEndings("\n"));
        Assert.False(Directory.Exists(Path.Combine(den.Root, ".git")));

        output = new StringWriter();
        error = new StringWriter();
        var done = InitCommand.Run([den.Root, "--git", "--author-name", "Ash Calloway", "--author-email", "ash@example.invalid"], output, error, git);

        Assert.Equal(CliApp.ExitOk, done);
        Assert.Contains("Saved your name and email for this den's repository only.\n", output.ToString().ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Equal("Ash Calloway\n", TestGit.Run(den.Root, "config", "--local", "--get", "user.name"));
        Assert.Equal("ash@example.invalid\n", TestGit.Run(den.Root, "config", "--local", "--get", "user.email"));
        Assert.Equal("", File.ReadAllText(global));
        Assert.Equal("Ash Calloway", GitHistory.ForDen(den.Root, git, Token).Log(new HistoryLogQuery(), Token).Value[0].Author);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-15")]
    public void ANameAndEmailGitGivesOnlyInsideTheRepositoryAreUsedWhenNoneIsGiven()
    {
        using var den = TempDen.DraculaCopy();
        using var config = TempDen.Empty();
        var included = Path.Combine(config.Root, "included");
        File.WriteAllText(included, "[user]\n\tname = Ash Calloway\n\temail = ash@example.invalid\n", new UTF8Encoding(false));
        var global = Path.Combine(config.Root, "gitconfig");
        File.WriteAllText(
            global, $"[includeIf \"gitdir/i:**/{Path.GetFileName(den.Root)}/\"]\n\tpath = {included.Replace('\\', '/')}\n", new UTF8Encoding(false));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InitCommand.Run([den.Root, "--git"], output, error, NoIdentity(global));

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Empty(error.ToString());
        Assert.Equal("Ash Calloway", GitHistory.ForDen(den.Root, NoIdentity(global), Token).Log(new HistoryLogQuery(), Token).Value[0].Author);
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-21")]
    [Trait("Scenario", "SC-CLI-12")]
    [InlineData(true)]
    [InlineData(false)]
    public void AGivenNameAndEmailWinOverGitsInAnExistingOrNewDen(bool existing)
    {
        using var den = existing ? TempDen.DraculaCopy() : TempDen.Empty();
        using var config = TempDen.Empty();
        var text = "[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n";
        var global = Path.Combine(config.Root, "gitconfig");
        File.WriteAllText(global, text, new UTF8Encoding(false));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InitCommand.Run([den.Root, "--git", "--author-name", "Ash Calloway", "--author-email", "ash@example.invalid"], output, error, NoIdentity(global));

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Empty(error.ToString());
        Assert.Contains("Saved your name and email for this den's repository only.\n", output.ToString().ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("Marek", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("Ash Calloway|ash@example.invalid|Ash Calloway|ash@example.invalid\n", TestGit.Run(den.Root, "log", "-1", "--format=%an|%ae|%cn|%ce"));
        Assert.Equal(text, File.ReadAllText(global));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-17")]
    [Trait("Scenario", "SC-CLI-12")]
    [InlineData(true)]
    [InlineData(false)]
    public void ANewDenInALargerRepositoryIsRefusedWithANameGivenAndNothingIsMade(bool folderExists)
    {
        using var writing = TempDen.Empty();
        using var config = TempDen.Empty();
        var global = Path.Combine(config.Root, "gitconfig");
        File.WriteAllText(global, "[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n", new UTF8Encoding(false));
        var git = NoIdentity(global);
        TestGit.Run(writing.Root, "init", "--quiet");
        var before = File.ReadAllText(Path.Combine(writing.Root, ".git", "config"));
        var folder = Path.Combine(writing.Root, "outer", "vael-notes");
        if (folderExists)
        {
            Directory.CreateDirectory(folder);
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = InitCommand.Run([folder, "--git", "--author-name", "Ash Calloway", "--author-email", "ash@example.invalid"], output, error, git);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("weren't applied", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("marek", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(folderExists, Directory.Exists(folder));
        Assert.False(File.Exists(Path.Combine(folder, "den.yaml")));
        Assert.Equal(before, File.ReadAllText(Path.Combine(writing.Root, ".git", "config")));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void ANewDenInAFolderThatIsARepositoryIsRefusedWithANameGivenAndSaysHowToGiveIt()
    {
        using var folder = TempDen.Empty();
        using var config = TempDen.Empty();
        var global = Path.Combine(config.Root, "gitconfig");
        File.WriteAllText(global, "[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n", new UTF8Encoding(false));
        TestGit.Run(folder.Root, "init", "--quiet");
        var output = new StringWriter();
        var error = new StringWriter();

        var refused = InitCommand.Run([folder.Root, "--git", "--author-name", "Ash Calloway", "--author-email", "ash@example.invalid"], output, error, NoIdentity(global));

        Assert.Equal(CliApp.ExitUsage, refused);
        Assert.Contains("--author-name \"<name>\" --author-email <email>", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder.Root, "den.yaml")));

        // What it says works: the den first, then its history, under the name given.
        Assert.Equal(CliApp.ExitOk, InitCommand.Run([folder.Root], new StringWriter(), new StringWriter(), NoIdentity(global)));
        Assert.Equal(CliApp.ExitOk, InitCommand.Run([folder.Root, "--git", "--author-name", "Ash Calloway", "--author-email", "ash@example.invalid"], new StringWriter(), new StringWriter(), NoIdentity(global)));
        Assert.Equal("Ash Calloway|ash@example.invalid|Ash Calloway|ash@example.invalid\n", TestGit.Run(folder.Root, "log", "-1", "--format=%an|%ae|%cn|%ce"));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-21")]
    public void GitDirInTheEnvironmentIsNamedForANewDenRatherThanBlamingTheFolderAbove()
    {
        using var parent = TempDen.Empty();
        using var config = TempDen.Empty();
        using var other = TempDen.Empty();
        var global = Path.Combine(config.Root, "gitconfig");
        File.WriteAllText(global, "[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n", new UTF8Encoding(false));
        TestGit.Run(other.Root, "init", "--quiet");
        var git = NoIdentity(global);
        git = git with { Environment = new Dictionary<string, string?>(git.Environment) { ["GIT_DIR"] = Path.Combine(other.Root, ".git") } };
        var folder = Path.Combine(parent.Root, "vael-notes");
        var error = new StringWriter();

        var exitCode = InitCommand.Run([folder, "--git", "--author-name", "Ash Calloway", "--author-email", "ash@example.invalid"], new StringWriter(), error, git);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains("GIT_DIR", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("inside the git repository", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void ANewDenInARepositoryWithCheckpointsIsRefusedWithAdviceThatWorks()
    {
        using var folder = TempDen.Empty();
        using var config = TempDen.Empty();
        var global = Path.Combine(config.Root, "gitconfig");
        File.WriteAllText(global, "[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n[author]\n\tname = Marek Author\n", new UTF8Encoding(false));
        var git = NoIdentity(global);
        GitRun(folder.Root, git, "init", "--quiet");
        File.WriteAllText(Path.Combine(folder.Root, "first.txt"), "first\n", new UTF8Encoding(false));
        GitRun(folder.Root, git, "add", "--all");
        GitRun(folder.Root, git, "commit", "--quiet", "-m", "First");
        var error = new StringWriter();

        var refused = InitCommand.Run([folder.Root, "--git", "--author-name", "Ash Calloway", "--author-email", "ash@example.invalid"], new StringWriter(), error, git);

        // It doesn't send the writer to a second refusal.
        Assert.Equal(CliApp.ExitUsage, refused);
        Assert.False(File.Exists(Path.Combine(folder.Root, "den.yaml")));
        Assert.DoesNotContain("wolfden init --git --author-name", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("weren't applied", error.ToString(), StringComparison.Ordinal);

        // The den, then each command it names.
        Assert.Equal(CliApp.ExitOk, InitCommand.Run([folder.Root], new StringWriter(), new StringWriter(), git));
        var commands = System.Text.RegularExpressions.Regex.Matches(error.ToString(), "git config --local \\S+ (\"<name>\"|<email>|false)");
        Assert.Equal(10, commands.Count);
        foreach (System.Text.RegularExpressions.Match command in commands)
        {
            // As typed, with the two-word name in the quotes the command shows: the shell's words, without "git".
            var typed = command.Value.Replace("<name>", "Ash Calloway", StringComparison.Ordinal).Replace("<email>", "ash@example.invalid", StringComparison.Ordinal);
            var words = System.Text.RegularExpressions.Regex.Matches(typed, "\"[^\"]*\"|\\S+").Select(word => word.Value.Trim('"')).Skip(1);
            GitRun(folder.Root, git, [.. words]);
        }

        File.WriteAllText(Path.Combine(folder.Root, "second.txt"), "second\n", new UTF8Encoding(false));
        GitRun(folder.Root, git, "add", "--all");
        GitRun(folder.Root, git, "commit", "--quiet", "-m", "Second");
        Assert.Equal("Ash Calloway|ash@example.invalid|Ash Calloway|ash@example.invalid\n", GitRun(folder.Root, git, "log", "-1", "--format=%an|%ae|%cn|%ce"));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-22")]
    [InlineData(true, "--author-name", "Ash Calloway")]
    [InlineData(true, "--author-email", "ash@example.invalid")]
    [InlineData(true, "--author-name", "Marek Oswin")]
    [InlineData(false, "--author-name", "Ash Calloway")]
    public void OneGivenAloneIsRefusedAndNothingIsMade(bool existing, string option, string value)
    {
        using var parent = TempDen.Empty();
        using var dracula = TempDen.DraculaCopy();
        using var config = TempDen.Empty();
        var global = Path.Combine(config.Root, "gitconfig");
        File.WriteAllText(global, "[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n", new UTF8Encoding(false));
        var folder = existing ? dracula.Root : Path.Combine(parent.Root, "kestrel-notes");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InitCommand.Run([folder, "--git", option, value], output, error, NoIdentity(global));

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Empty(output.ToString());
        Assert.StartsWith("wolfden init: --author-name and --author-email go together", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("marek", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(folder, ".git")));
        Assert.Equal(existing, Directory.Exists(folder));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-23")]
    [InlineData("gpg")]
    [InlineData("ssh")]
    public void AGivenNameTurnsSigningOffForTheDenAndSaysSo(string format)
    {
        using var den = TempDen.DraculaCopy();
        using var config = TempDen.Empty();
        var attempts = Path.Combine(config.Root, "attempts").Replace('\\', '/');
        var program = Path.Combine(config.Root, "signer.sh").Replace('\\', '/');
        File.WriteAllText(program, $"#!/bin/sh\necho \"$*\" >> \"{attempts}\"\nexit 1\n", new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var global = Path.Combine(config.Root, "gitconfig");
        File.WriteAllText(
            global,
            "[user]\n\tname = Marek Oswin\n\temail = marek@example.invalid\n[commit]\n\tgpgsign = true\n" +
            (format == "ssh"
                ? $"[gpg]\n\tformat = ssh\n[gpg \"ssh\"]\n\tprogram = {program}\n\tdefaultKeyCommand = {program}\n"
                : $"[user]\n\tsigningkey = KEY-OF-MAREK\n[gpg]\n\tprogram = {program}\n"),
            new UTF8Encoding(false));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InitCommand.Run([den.Root, "--git", "--author-name", "Ash Calloway", "--author-email", "ash@example.invalid"], output, error, NoIdentity(global));

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.False(File.Exists(attempts));
        Assert.Contains("Turned signing off for this den's repository only", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("KEY-OF-MAREK", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("Ash Calloway|ash@example.invalid|Ash Calloway|ash@example.invalid\n", TestGit.Run(den.Root, "log", "-1", "--format=%an|%ae|%cn|%ce"));
        Assert.Equal("N\n", TestGit.Run(den.Root, "log", "-1", "--format=%G?"));
    }

    [Fact]
    [Trait("Scenario", "SC-CLI-12")]
    [Trait("Scenario", "SC-HIST-15")]
    public void ANewDenWithoutANameOrEmailIsMadeButItsHistoryIsntTurnedOn()
    {
        using var parent = TempDen.Empty();
        var global = Path.Combine(parent.Root, "gitconfig");
        File.WriteAllText(global, "[user]\n\tname = Ash Calloway\n", new UTF8Encoding(false));
        var folder = Path.Combine(parent.Root, "kestrel-notes");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InitCommand.Run([folder, "--git"], output, error, NoIdentity(global));

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.True(File.Exists(Path.Combine(folder, "den.yaml")));
        Assert.False(Directory.Exists(Path.Combine(folder, ".git")));
        Assert.StartsWith(
            "wolfden init: the den was created, but its history wasn't turned on: git records who made each checkpoint, and needs a name and an email for this den. " +
            "Run 'wolfden init --git' in the den with --author-name \"<name>\" --author-email <email>:",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-14")]
    [InlineData(new[] { "--name", "Kestrel" }, "--name and --universe don't apply")]
    [InlineData(new[] { "--universe", "kestrel" }, "--name and --universe don't apply")]
    public void NewDenOptionsAreRefusedForAnExistingDen(string[] extra, string says)
    {
        using var den = TempDen.DraculaCopy();

        var (exitCode, _, error) = Cli.Run(["init", den.Root, "--git", .. extra]);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Contains(says, error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(den.Root, ".git")));
    }

    [Theory]
    [InlineData(new[] { "--no-checkpoint" }, "wolfden init: --no-checkpoint goes with --git, which turns on the den's history.\n")]
    [InlineData(new[] { "--author-name", "Ash" }, "wolfden init: --author-name goes with --git, which turns on the den's history.\n")]
    [InlineData(new[] { "--git", "--author-email", "a\tb" }, "wolfden init: --author-email: The email must be one line, with no control characters.\n")]
    [InlineData(new[] { "--git", "--author-name", "Ash <Calloway>" }, "wolfden init: --author-name: The name can't hold '<' or '>', which git can't record in one.\n")]
    public void HistoryOptionsAreChecked(string[] extra, string expected)
    {
        using var den = TempDen.DraculaCopy();

        var (exitCode, _, error) = Cli.Run(["init", den.Root, .. extra]);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Equal(expected, error);
        Assert.False(Directory.Exists(Path.Combine(den.Root, ".git")));
    }

    [Fact]
    public void WithoutGitAnExistingDenIsStillRefused()
    {
        using var den = TempDen.DraculaCopy();

        var (exitCode, _, error) = Cli.Run("init", den.Root);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Equal(
            $"wolfden init: '{den.Root}' is already a den: it has a den.yaml. Nothing was changed. To turn on its history, run 'wolfden init --git' in it.\n",
            error);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void ADenInsideALargerRepositoryIsRefused()
    {
        using var writing = TempDen.Empty();
        TestGit.Run(writing.Root, "init", "--quiet");
        writing.Write("notes/den.yaml", "format: 1\n");

        var (exitCode, output, error) = Cli.Run("init", Path.Combine(writing.Root, "notes"), "--git", "--json");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        using var json = JsonDocument.Parse(output);
        var git = json.RootElement.GetProperty("git");
        Assert.Equal("refused", git.GetProperty("outcome").GetString());
        Assert.Equal("in_repository", git.GetProperty("code").GetString());
        Assert.Contains("which keeps its history already", error, StringComparison.Ordinal);
        Assert.False(writing.Exists("notes/.gitignore"));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void ADenWithHistoryAlreadyIsRefused()
    {
        using var den = TempDen.DraculaCopy();
        Assert.Equal(CliApp.ExitOk, Cli.Run("init", den.Root, "--git").ExitCode);

        var (exitCode, output, error) = Cli.Run("init", den.Root, "--git");

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Empty(output);
        Assert.Equal("wolfden init: The den's folder is already a git repository with checkpoints, so its history is on. Nothing was changed.\n", error);
        Assert.Single(GitHistory.ForDen(den.Root, TestGit.Options, Token).Log(new HistoryLogQuery(), Token).Value);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-16")]
    public void ADenWhoseOwnRepositoryHasNoCheckpointsIsFinished()
    {
        using var den = TempDen.DraculaCopy();
        TestGit.Run(den.Root, "init", "--quiet");

        var (exitCode, output, error) = Cli.Run("init", den.Root, "--git", "--json");

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Empty(error);
        using var json = JsonDocument.Parse(output);
        Assert.Equal("finished", json.RootElement.GetProperty("git").GetProperty("outcome").GetString());
        Assert.Equal("Start of history", json.RootElement.GetProperty("git").GetProperty("checkpoint").GetProperty("message").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-17")]
    public void WithoutGitInstalledAnExistingDenIsRefused()
    {
        using var den = TempDen.DraculaCopy();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = InitCommand.Run([den.Root, "--git"], output, error, TestGit.Options with { Executable = "wolfden-test-no-such-git" });

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.StartsWith("wolfden init: Git isn't installed, or Wolfden can't find it on the PATH", error.ToString(), StringComparison.Ordinal);
        Assert.False(den.Exists(".gitignore"));
    }

    /// <summary>Runs git with <paramref name="options"/>' configuration; a failure fails the test.</summary>
    private static string GitRun(string folder, GitOptions options, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        foreach (var (name, value) in options.Environment)
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

        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(Token);
        var said = process.StandardError.ReadToEndAsync(Token);
        process.WaitForExitAsync(Token).GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, said.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult().ReplaceLineEndings("\n");
    }

    private static GitOptions NoIdentity(string global)
    {
        var environment = new Dictionary<string, string?>(TestGit.Options.Environment)
        {
            ["GIT_CONFIG_GLOBAL"] = global,
            ["EMAIL"] = null,
        };
        return TestGit.Options with { Environment = environment };
    }
}

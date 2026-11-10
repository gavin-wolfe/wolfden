using System.Text.Json;

namespace Wolfden.Cli.Tests;

/// <summary>
/// <c>wolfden identity audit</c> (#315, design §3.6 item 2): the command
/// line over <c>IdentityAudit</c>, whose reading of a history is tested in
/// Wolfden.Git (<c>IdentityAuditTests</c>). Every name and email is invented.
/// </summary>
public sealed class IdentityCommandTests : IDisposable
{
    private const string DenYaml = """
        format: 1
        authors:
          wren-calder:
            name: Wren Calder
            email: wren@example.com
          juno-pike:
            name: Juno Pike

        """;

    private readonly TempDen _den = TempDen.FromFiles(new Dictionary<string, string> { ["den.yaml"] = DenYaml });

    public void Dispose()
    {
        TestGit.MakeDeletable(_den.Root);
        _den.Dispose();
    }

    private void CommitAs(string name, string email, string message)
    {
        TestGit.Run(_den.Root, "add", "--all");
        TestGit.Run(_den.Root, "-c", $"user.name={name}", "-c", $"user.email={email}", "commit", "--quiet", "--allow-empty", "-m", message);
    }

    private void WithHistory()
    {
        TestGit.Run(_den.Root, "init", "--quiet");
        CommitAs("Wren Calder", "wren@example.com", "Start");
        CommitAs("Juno Pike", "juno@example.com", "Notes\n\nCo-Authored-By: Story Helper <helper@example.com>");
        TestGit.Run(_den.Root, "remote", "add", "origin", "https://wren:hunter22@example.com/notes.git");
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-24")]
    public void TheAuditListsEveryNameTheHistoryAndDenYamlHoldAndChangesNothing()
    {
        WithHistory();
        var before = Cli.Snapshot(_den.Root);

        var (exitCode, output, error) = Cli.Run("identity", "audit", "--den", _den.Root);

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Empty(error);
        Assert.StartsWith("The den's history: 2 checkpoints and 0 tags, on every branch and tag.\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("larger repository", output, StringComparison.Ordinal);
        Assert.Contains("\n  Wren Calder <wren@example.com>\n    author of 1 checkpoint, committer of 1 checkpoint\n", output, StringComparison.Ordinal);
        Assert.Contains("\n  Juno Pike <juno@example.com>\n", output, StringComparison.Ordinal);
        Assert.Contains("\n  Story Helper <helper@example.com>\n    in Co-Authored-By of 1 checkpoint\n", output, StringComparison.Ordinal);
        Assert.Contains("\nSignatures: none of its checkpoints or tags are signed.\n", output, StringComparison.Ordinal);
        Assert.Contains("\n  origin  https://wren:***@example.com/notes.git  (fetch, push)\n    account name in the address: wren\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter22", output, StringComparison.Ordinal);
        Assert.Contains("\nAuthors in den.yaml:\n  wren-calder: name: Wren Calder, email: wren@example.com\n  juno-pike: name: Juno Pike\n", output, StringComparison.Ordinal);
        Assert.EndsWith("\nAll of this travels with any copy or push of this den.\n", output, StringComparison.Ordinal);
        Assert.Equal(before, Cli.Snapshot(_den.Root));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-24")]
    public void JsonGivesTheSameAuditWithAVersion()
    {
        WithHistory();

        var (exitCode, output, _) = Cli.Run("identity", "audit", "--den", _den.Root, "--json");

        Assert.Equal(CliApp.ExitOk, exitCode);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.Equal(1, json.GetProperty("version").GetInt32());
        Assert.Equal("audited", json.GetProperty("history").GetString());
        Assert.True(json.GetProperty("denIsRepositoryRoot").GetBoolean());
        Assert.Equal(2, json.GetProperty("commits").GetInt32());
        var names = json.GetProperty("identities").EnumerateArray().Select(i => $"{i.GetProperty("name").GetString()} <{i.GetProperty("email").GetString()}>").ToList();
        Assert.Equal(["Juno Pike <juno@example.com>", "Wren Calder <wren@example.com>", "Story Helper <helper@example.com>"], names);
        Assert.Equal(0, json.GetProperty("signing").GetProperty("signedCommits").GetInt32());
        Assert.Equal("https://wren:***@example.com/notes.git", json.GetProperty("remotes")[0].GetProperty("url").GetString());
        Assert.Equal("wren", json.GetProperty("remotes")[0].GetProperty("account").GetString());
        var author = json.GetProperty("authors")[0];
        Assert.Equal("wren-calder", author.GetProperty("key").GetString());
        Assert.Equal("email", author.GetProperty("fields")[1].GetProperty("key").GetString());
        Assert.DoesNotContain("hunter22", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-24")]
    public void ADenWithNoHistoryStillListsItsAuthors()
    {
        var (exitCode, output, error) = Cli.Run("identity", "audit", "--den", _den.Root);
        var json = JsonDocument.Parse(Cli.Run("identity", "audit", "--den", _den.Root, "--json").Output).RootElement;

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Empty(error);
        Assert.StartsWith("This den has no history: its folder isn't in a git repository", output, StringComparison.Ordinal);
        Assert.Contains("\n  wren-calder: name: Wren Calder, email: wren@example.com\n", output, StringComparison.Ordinal);
        Assert.EndsWith("\nden.yaml travels with any copy of this den.\n", output, StringComparison.Ordinal);
        Assert.Equal("none", json.GetProperty("history").GetString());
        Assert.Equal(0, json.GetProperty("identities").GetArrayLength());
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-24")]
    public void ADenYamlThatDoesntParseStillGetsItsHistoryAudited()
    {
        WithHistory();
        _den.Write("den.yaml", "format: 1\nauthors: [unclosed\n");

        var (exitCode, output, _) = Cli.Run("identity", "audit", "--den", _den.Root);

        Assert.Equal(CliApp.ExitOk, exitCode);
        Assert.Contains("\n  Wren Calder <wren@example.com>\n", output, StringComparison.Ordinal);
        Assert.Contains("\nAuthors in den.yaml:\n  den.yaml doesn't parse, so its authors aren't listed; run 'wolfden check'.\n", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-25")]
    public void AKeyThatBelongsToAnotherNameIsCalledOut()
    {
        var audit = new Wolfden.Git.IdentityAudit(
            Wolfden.Git.IdentityAuditState.Audited, null, _den.Root, true, 3, 1, 1, [], [],
            new Wolfden.Git.SigningSummary(
                2,
                1,
                [
                    new Wolfden.Git.SigningKeyUse(Wolfden.Git.SignatureKind.OpenPgp, "0123456789ABCDEF0123456789ABCDEF01234567", 2, 0,
                        ["Wren Calder <wren@example.com>", "Juno Pike <juno@example.com>"], "Ash Everyday <ash@example.com>"),
                    new Wolfden.Git.SigningKeyUse(Wolfden.Git.SignatureKind.X509, null, 0, 1, ["Wren Calder <wren@example.com>"], null),
                ],
                0),
            []);
        var output = new StringWriter { NewLine = "\n" };

        IdentityCommand.WriteText(output, audit, [], null);

        Assert.Contains(
            "\nSignatures: 2 of 3 checkpoints and 1 of 1 annotated tag signed, with 2 keys. A key ties together every name it signs under, for good.\n" +
            "  OpenPGP key 0123456789ABCDEF0123456789ABCDEF01234567: 2 checkpoints\n" +
            "    signed as: Wren Calder <wren@example.com>; Juno Pike <juno@example.com>\n" +
            "    belongs to: Ash Everyday <ash@example.com> (as this computer reports it)\n" +
            "    That isn't a name it signed as, so anyone who checks the signature can link the two.\n" +
            "  X.509 certificate (which one isn't recorded in the signature): 1 tag\n" +
            "    signed as: Wren Calder <wren@example.com>\n",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-24")]
    [InlineData(new string[] { "identity" }, "wolfden identity: give a subcommand: audit.")]
    [InlineData(new string[] { "identity", "show" }, "wolfden identity: unknown subcommand 'show'; the subcommands are: audit.")]
    [InlineData(new string[] { "identity", "--json" }, "wolfden identity: unknown option '--json'.")]
    [InlineData(new string[] { "identity", "audit", "extra" }, "wolfden identity audit: it takes no arguments; name the den with '--den'.")]
    public void AMistakeOnTheCommandLineExitsTwo(string[] args, string message)
    {
        var (exitCode, output, error) = Cli.Run(args);

        Assert.Equal(CliApp.ExitUsage, exitCode);
        Assert.Empty(output);
        Assert.StartsWith(message + "\nusage: wolfden identity", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-24")]
    public void EachLevelHasItsOwnHelp()
    {
        var (groupCode, group, _) = Cli.Run("identity", "--help");
        var (auditCode, audit, _) = Cli.Run("identity", "audit", "--help");

        Assert.Equal((CliApp.ExitOk, CliApp.ExitOk), (groupCode, auditCode));
        Assert.StartsWith("usage: wolfden identity <subcommand>", group, StringComparison.Ordinal);
        Assert.Contains("\n  audit ", group, StringComparison.Ordinal);
        Assert.StartsWith("usage: wolfden identity audit [--den <den>] [--json]", audit, StringComparison.Ordinal);
    }
}

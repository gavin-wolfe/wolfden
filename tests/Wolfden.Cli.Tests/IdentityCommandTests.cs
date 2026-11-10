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
        Assert.StartsWith("The den's history: 2 checkpoints, 0 tags, on every branch, tag, and other ref.\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("larger repository", output, StringComparison.Ordinal);
        Assert.Contains("\n  Wren Calder <wren@example.com>\n    author of 1 checkpoint, committer of 1 checkpoint\n", output, StringComparison.Ordinal);
        Assert.Contains("\n  Juno Pike <juno@example.com>\n", output, StringComparison.Ordinal);
        Assert.Contains("\n  Story Helper <helper@example.com>\n    in Co-Authored-By of 1 message\n", output, StringComparison.Ordinal);
        Assert.Contains("\nSignatures: none of its checkpoints or tags are signed.\n", output, StringComparison.Ordinal);
        Assert.Contains("\nAuthors in den.yaml:\n  wren-calder: name: Wren Calder, email: wren@example.com\n  juno-pike: name: Juno Pike\n", output, StringComparison.Ordinal);
        Assert.Contains(
            "\nOnly in this folder (not pushed or cloned):\n  Remotes:\n    origin  https://wren:***@example.com/notes.git  (fetch, push)\n      account name in the address: wren\n",
            output,
            StringComparison.Ordinal);
        Assert.Contains("\n  Name settings in this copy's git config:\n    none\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter22", output, StringComparison.Ordinal);
        Assert.EndsWith(
            "\nA push or a clone carries the history and den.yaml above. A copy of the folder itself\n(a zip, a synced folder, a disk backup) carries all of it, the last part included.\n",
            output,
            StringComparison.Ordinal);
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
        var remote = json.GetProperty("local").GetProperty("remotes")[0];
        Assert.Equal("https://wren:***@example.com/notes.git", remote.GetProperty("url").GetString());
        Assert.Equal("wren", remote.GetProperty("account").GetString());
        Assert.Equal(0, json.GetProperty("mentions").GetArrayLength());
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
        var audit = Audit(
            commits: 3,
            annotatedTags: 1,
            mergedTags: 1,
            new Wolfden.Git.SigningSummary(
                2,
                1,
                1,
                [
                    new Wolfden.Git.SigningKeyUse(Wolfden.Git.SignatureKind.OpenPgp, "0123456789ABCDEF0123456789ABCDEF01234567", 2, 0, 0,
                        ["Wren Calder <wren@example.com>", "Juno Pike <juno@example.com>"], "Ash Everyday <ash@example.com>", true),
                    new Wolfden.Git.SigningKeyUse(Wolfden.Git.SignatureKind.Ssh, "SHA256:abc", 0, 0, 1, ["Ash Everyday <ash@example.com>"], null, false),
                    new Wolfden.Git.SigningKeyUse(Wolfden.Git.SignatureKind.Ssh, "SHA256:def", 0, 1, 0, ["Wren Calder <wren@example.com>"], null, true),
                ],
                [new Wolfden.Git.UnidentifiedSignatures(Wolfden.Git.SignatureKind.X509, "Wren Calder <wren@example.com>", 2, 0, 0)],
                0));

        var output = Text(audit);

        Assert.Contains(
            "\nSignatures: 2 of 3 checkpoints, 1 of 1 annotated tag, 1 of 1 merged tag signed, with 3 keys. A key ties together every name it signs under, for good.\n" +
            "  OpenPGP key 0123456789ABCDEF0123456789ABCDEF01234567: 2 checkpoints\n" +
            "    signed as: Wren Calder <wren@example.com>; Juno Pike <juno@example.com>\n" +
            "    belongs to: Ash Everyday <ash@example.com> (as this computer reports it)\n" +
            "    That isn't a name it signed as, so anyone who checks the signature can link the two.\n" +
            "  SSH key SHA256:abc: 1 merged tag\n" +
            "    signed as: Ash Everyday <ash@example.com>\n" +
            "    owner not checked\n" +
            "  SSH key SHA256:def: 1 tag\n" +
            "    signed as: Wren Calder <wren@example.com>\n" +
            "    owner: this computer can't say whose key it is\n" +
            "  2 signatures whose key isn't recorded (X.509), signed as Wren Calder <wren@example.com>: 2 checkpoints\n",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-25")]
    public void SignaturesOnlyOnMergedTagsArentReportedAsNone()
    {
        var audit = Audit(
            commits: 2,
            annotatedTags: 0,
            mergedTags: 1,
            new Wolfden.Git.SigningSummary(0, 0, 1, [new Wolfden.Git.SigningKeyUse(Wolfden.Git.SignatureKind.Ssh, "SHA256:abc", 0, 0, 1, [], null, false)], [], 0));

        var output = Text(audit);

        Assert.DoesNotContain("none of its checkpoints", output, StringComparison.Ordinal);
        Assert.Contains("\nSignatures: 0 of 2 checkpoints, 1 of 1 merged tag signed, with 1 key.", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-24")]
    public void MentionsTheReflogsAndThisCopysSettingsAreShown()
    {
        var audit = Audit(commits: 1, annotatedTags: 0, mergedTags: 0, new Wolfden.Git.SigningSummary(0, 0, 0, [], [], 0)) with
        {
            Shallow = true,
            Mentions = [new Wolfden.Git.MessageMention("Signed-off-by", "Ash Everyday", 1), new Wolfden.Git.MessageMention(null, "Thanks to ash@example.com", 2)],
            Local = new Wolfden.Git.LocalOnly(
                [],
                [new Wolfden.Git.LocalName("Ash Everyday", "ash@example.com", 2, 1)],
                [new Wolfden.Git.LocalSetting("user.name", "Ash Everyday")],
                null),
        };

        var output = Text(audit);

        Assert.Contains("\nThis copy is shallow:", output, StringComparison.Ordinal);
        Assert.Contains(
            "\nElsewhere in checkpoint and tag messages, lines that may name someone:\n  Signed-off-by: Ash Everyday  (1 message)\n  Thanks to ash@example.com  (2 messages)\n",
            output,
            StringComparison.Ordinal);
        Assert.Contains("\n    Ash Everyday <ash@example.com>: 2 reflog entries, 1 checkpoint only a reflog keeps\n", output, StringComparison.Ordinal);
        Assert.Contains("\n  Name settings in this copy's git config:\n    user.name = Ash Everyday\n", output, StringComparison.Ordinal);
    }

    private Wolfden.Git.IdentityAudit Audit(int commits, int annotatedTags, int mergedTags, Wolfden.Git.SigningSummary signing) => new(
        Wolfden.Git.IdentityAuditState.Audited, null, _den.Root, true, false, commits, annotatedTags, annotatedTags, mergedTags, [], [], [], signing,
        new Wolfden.Git.LocalOnly([], [], [], null));

    private static string Text(Wolfden.Git.IdentityAudit audit)
    {
        var output = new StringWriter { NewLine = "\n" };
        IdentityCommand.WriteText(output, audit, [], null);
        return output.ToString();
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

using System.Diagnostics;
using System.ComponentModel;
using System.Text;

namespace Wolfden.Git.Tests;

/// <summary>
/// <c>wolfden identity audit</c>'s reading of a history (#315, design §3.6
/// item 2): every name and email, offset, signature, and remote that a
/// copy or push would carry. Every name, email, and key is invented.
/// </summary>
public sealed class IdentityAuditTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A key fingerprint for crafted OpenPGP signatures.</summary>
    private const string Fingerprint = "0123456789ABCDEF0123456789ABCDEF01234567";

    /// <summary>An SSH signature made by a throwaway ed25519 key, and that key's fingerprint as <c>ssh-keygen -l</c> printed it.</summary>
    private const string SshSignature = """
        -----BEGIN SSH SIGNATURE-----
        U1NIU0lHAAAAAQAAADMAAAALc3NoLWVkMjU1MTkAAAAgrp9PRQB10pxLS/Ns7eu2BlaCbI
        EA0Bv1WOppI3WIVpgAAAADZ2l0AAAAAAAAAAZzaGE1MTIAAABTAAAAC3NzaC1lZDI1NTE5
        AAAAQIuCKqwSAPF6kwfoL9TeLZw515JaJxmBE/xYH/m7+j0uFcXeCEgxEwdPpnX6/pcOg0
        4PVP3KBm55Zerm8FaMDAk=
        -----END SSH SIGNATURE-----
        """;

    private const string SshFingerprint = "SHA256:QMhN5KrTO8DtDSNiSP4a9qUre5+2TYBgnlgaELKBEPo";

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void EveryNameAndEmailIsListedExactlyAsRecordedOnEveryBranchAndTag()
    {
        using var repo = TestRepo.Create();
        Commit(repo, "First notes", "Wren Calder", "wren@example.com", "2026-01-05T09:00:00+01:00");
        repo.WriteDen("a.md", "a\n");
        Commit(
            repo,
            "More notes\n\nCo-Authored-By: Story Helper <helper@example.com>\nco-authored-by: Story Helper <helper@example.com>\nSigned-off-by: Wren Calder <wren@example.com>\nCloses: #12",
            "Wren Calder",
            "wren@example.com",
            "2026-01-06T22:30:00+01:00");
        TestRepo.RunGit(repo.Root, Who("Wren Calder", "wren@example.com", "2026-01-07T10:00:00+01:00"), "tag", "-a", "-m", "Book one", "book-one");
        repo.Git("tag", "light");

        // Another branch, not checked out, and a name that differs only in case.
        repo.Git("checkout", "--quiet", "-b", "side");
        repo.WriteDen("b.md", "b\n");
        Commit(repo, "Side notes", "juno pike", "juno@example.com", "2026-02-01T08:00:00-05:00");
        repo.WriteDen("c.md", "c\n");
        Commit(repo, "More side notes", "Juno Pike", "juno@example.com", "2026-02-02T08:00:00-05:00");
        repo.Git("checkout", "--quiet", "main");

        // A mailmap changes only how git shows names; the audit shows what's recorded.
        repo.WriteDen(".mailmap", "Wren Calder <wren@example.com> Juno Pike <juno@example.com>\n");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal(IdentityAuditState.Audited, audit.State);
        Assert.True(audit.DenIsRepositoryRoot);
        Assert.Equal(4, audit.Commits);
        Assert.Equal(2, audit.Tags);
        Assert.Equal(1, audit.AnnotatedTags);

        var wren = Single(audit, "Wren Calder", "wren@example.com");
        Assert.Equal((2, 2, 1), (wren.AsAuthor, wren.AsCommitter, wren.AsTagger));
        Assert.Equal([new TrailerUse("Signed-off-by", 1)], wren.InTrailers);
        Assert.Equal(["+0100"], wren.Offsets);
        Assert.Equal(DateTimeOffset.Parse("2026-01-05T09:00:00+01:00", System.Globalization.CultureInfo.InvariantCulture), wren.First);
        Assert.Equal(DateTimeOffset.Parse("2026-01-07T10:00:00+01:00", System.Globalization.CultureInfo.InvariantCulture), wren.Last);
        Assert.Equal(TimeSpan.FromHours(1), wren.First!.Value.Offset);

        // A trailer is counted once per commit, whatever its key's case.
        var helper = Single(audit, "Story Helper", "helper@example.com");
        Assert.Equal((0, 0, 0), (helper.AsAuthor, helper.AsCommitter, helper.AsTagger));
        Assert.Equal([new TrailerUse("Co-Authored-By", 1)], helper.InTrailers);
        Assert.Null(helper.First);

        Assert.Equal(1, Single(audit, "juno pike", "juno@example.com").AsAuthor);
        Assert.Equal(1, Single(audit, "Juno Pike", "juno@example.com").AsAuthor);
        Assert.Equal(["-0500"], Single(audit, "Juno Pike", "juno@example.com").Offsets);
        Assert.Equal(4, audit.Identities.Count);

        Assert.Equal([new RecordedOffset("+0100", 5), new RecordedOffset("-0500", 4)], audit.Offsets);
        Assert.Equal(0, audit.Signing.SignedCommits);
        Assert.Empty(audit.Signing.Keys);
        Assert.Empty(audit.Local.Remotes);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void ADenInALargerRepositoryIsAuditedForTheWholeRepository()
    {
        using var repo = TestRepo.Create("notes/den");
        repo.WriteRepo("other/readme.txt", "not the den's\n");
        Commit(repo, "Outside the den too", "Juno Pike", "juno@example.com", "2026-03-01T12:00:00+00:00");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.False(audit.DenIsRepositoryRoot);
        Assert.Equal(IdentityAudit.Tilde(repo.Root, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), audit.RepositoryRoot, ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal("Juno Pike", Assert.Single(audit.Identities).Name);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void ADenWithNoRepositoryOrNoCommitsYetHasNothingRecorded()
    {
        var folder = TestRepo.NewFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, "den.yaml"), "format: 1\n");
            var none = IdentityAudit.Run(folder, TestRepo.Options, Token);
            Assert.Equal(IdentityAuditState.NoHistory, none.State);
            Assert.Empty(none.Identities);
        }
        finally
        {
            TestRepo.DeleteFolder(folder);
        }

        using var repo = TestRepo.Create();
        var empty = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal(IdentityAuditState.Audited, empty.State);
        Assert.Equal(0, empty.Commits);
        Assert.Empty(empty.Identities);
        Assert.Empty(empty.Offsets);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void RemotesAreListedWithTheirAccountNameAndNeverAPassword()
    {
        using var repo = TestRepo.Create();
        repo.Git("remote", "add", "origin", "https://wren-calder:s3cret-token@example.com/wren/notes.git");
        repo.Git("remote", "set-url", "--push", "origin", "git@example.com:wren/notes.git");
        repo.Git("remote", "add", "backup", "ssh://juno@backup.example.com/notes.git");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal(3, audit.Local.Remotes.Count);
        var fetch = Assert.Single(audit.Local.Remotes, r => r.Name == "origin" && r.Uses.Contains("fetch"));
        Assert.Equal("https://wren-calder:***@example.com/wren/notes.git", fetch.Url);
        Assert.Equal("wren-calder", fetch.Account);
        var push = Assert.Single(audit.Local.Remotes, r => r.Name == "origin" && r.Uses.Contains("push"));
        Assert.Equal("git@example.com:wren/notes.git", push.Url);
        Assert.Null(push.Account);
        var backup = Assert.Single(audit.Local.Remotes, r => r.Name == "backup");
        Assert.Equal(["fetch", "push"], backup.Uses);
        Assert.Equal("juno", backup.Account);
        Assert.DoesNotContain(audit.Local.Remotes, r => r.Url.Contains("s3cret", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-29")]
    public void SignaturesAreCountedByKeyWithTheNamesEachKeySignedUnder()
    {
        using var repo = TestRepo.Create();
        var log = UseFakeGpg(repo, "Ash Everyday <ash@example.com>");

        // One OpenPGP key signs under two names, once naming itself by
        // fingerprint and once by key ID alone; an SSH key signs a tag.
        var first = CraftCommit(repo, "main", "Wren Calder", "wren@example.com", Pgp(Packet(fingerprint: true, keyId: true)), parent: null);
        var second = CraftCommit(repo, "main", "Juno Pike", "juno@example.com", Pgp(Packet(fingerprint: false, keyId: true), oldFormat: true), parent: first);
        CraftCommit(repo, "main", "Wren Calder", "wren@example.com", signature: null, parent: second);
        CraftTag(repo, "signed-tag", second, "Wren Calder", "wren@example.com", SshSignature);

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal(3, audit.Commits);
        Assert.Equal(2, audit.Signing.SignedCommits);
        Assert.Equal(1, audit.Signing.SignedTags);
        Assert.Equal(2, audit.Signing.Keys.Count);

        var pgp = audit.Signing.Keys[0];
        Assert.Equal(SignatureKind.OpenPgp, pgp.Kind);
        Assert.Equal(Fingerprint, pgp.KeyId);
        Assert.Equal((2, 0), (pgp.Commits, pgp.Tags));
        Assert.Equal(["Wren Calder <wren@example.com>", "Juno Pike <juno@example.com>"], pgp.SignedAs.Order(StringComparer.Ordinal).Reverse());
        Assert.Equal("Ash Everyday <ash@example.com>", pgp.Owner);

        var ssh = audit.Signing.Keys[1];
        Assert.Equal(SignatureKind.Ssh, ssh.Kind);
        Assert.Equal(SshFingerprint, ssh.KeyId);
        Assert.Equal((0, 1), (ssh.Commits, ssh.Tags));
        Assert.Equal(["Wren Calder <wren@example.com>"], ssh.SignedAs);
        Assert.Null(ssh.Owner);
        Assert.True(ssh.OwnerChecked);

        // One check of one commit for the one key with a commit; never one per commit.
        Assert.Single(File.ReadAllLines(log));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-29")]
    public void OwnersAreLookedUpForTenKeysAtMost()
    {
        using var repo = TestRepo.Create();
        var log = UseFakeGpg(repo, "Ash Everyday <ash@example.com>");
        string? parent = null;
        for (var i = 0; i < IdentityAudit.MaxOwnerLookups + 2; i++)
        {
            parent = CraftCommit(repo, "main", "Wren Calder", "wren@example.com", Pgp(Packet(fingerprint: true, keyId: false, fingerprintHex: $"{i:X2}{Fingerprint[2..]}")), parent);
        }

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal(IdentityAudit.MaxOwnerLookups + 2, audit.Signing.Keys.Count);
        Assert.Equal(2, audit.Signing.OwnersNotLookedUp);
        Assert.Equal(IdentityAudit.MaxOwnerLookups, audit.Signing.Keys.Count(k => k.Owner is not null));
        Assert.Equal(IdentityAudit.MaxOwnerLookups, File.ReadAllLines(log).Length);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-29")]
    public void ARealSshSignedCheckpointIsReadWithItsKeyAndTheAllowedSignersName()
    {
        using var repo = TestRepo.Create();
        var keys = TestRepo.NewFolder();
        try
        {
            var key = Path.Combine(keys, "key");
            if (!TryRun(keys, "ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-C", "", "-f", key))
            {
                Assert.Skip("ssh-keygen isn't available here.");
            }

            var publicKey = File.ReadAllText(key + ".pub").Trim();
            var allowed = Path.Combine(keys, "allowed_signers");
            File.WriteAllText(allowed, $"juno@example.com {publicKey}\n");
            repo.Git("config", "gpg.format", "ssh");
            repo.Git("config", "user.signingkey", key.Replace('\\', '/'));
            repo.Git("config", "gpg.ssh.allowedSignersFile", allowed.Replace('\\', '/'));
            repo.Git("add", "--all");
            try
            {
                TestRepo.RunGit(repo.Root, Who("Juno Pike", "juno@example.com", "2026-04-01T10:00:00+02:00"), "commit", "--quiet", "-S", "-m", "Signed notes");
            }
            catch (InvalidOperationException e)
            {
                Assert.Skip($"git can't sign with ssh-keygen here: {e.Message}");
            }

            var expected = SshFingerprintOf(keys, key);
            var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

            Assert.Equal(1, audit.Signing.SignedCommits);
            var used = Assert.Single(audit.Signing.Keys);
            Assert.Equal(SignatureKind.Ssh, used.Kind);
            Assert.Equal(expected, used.KeyId);
            Assert.Equal(["Juno Pike <juno@example.com>"], used.SignedAs);
            Assert.Equal("juno@example.com", used.Owner);
        }
        finally
        {
            TestRepo.DeleteFolder(keys);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-29")]
    public void ASignaturesKeyIsReadFromItsOwnBytes()
    {
        Assert.Equal([new SignatureKey(SignatureKind.Ssh, SshFingerprint)], SignatureKey.Read(SshSignature));
        Assert.Equal([new SignatureKey(SignatureKind.OpenPgp, Fingerprint)], SignatureKey.Read(Pgp(Packet(fingerprint: true, keyId: true))));
        Assert.Equal([new SignatureKey(SignatureKind.OpenPgp, Fingerprint[^16..], KeyIdIsShort: true)], SignatureKey.Read(Pgp(Packet(fingerprint: false, keyId: true))));
        Assert.Equal([new SignatureKey(SignatureKind.OpenPgp, Fingerprint, false)], SignatureKey.Read(Pgp(Packet(fingerprint: true, keyId: false), armorHeader: true)));
        Assert.Equal([new SignatureKey(SignatureKind.OpenPgp, null)], SignatureKey.Read(Pgp(Packet(fingerprint: false, keyId: false))));
        Assert.Equal([new SignatureKey(SignatureKind.X509, null)], SignatureKey.Read("-----BEGIN SIGNED MESSAGE-----\nMIIB\n-----END SIGNED MESSAGE-----"));
        Assert.Equal([new SignatureKey(SignatureKind.OpenPgp, null)], SignatureKey.Read("-----BEGIN PGP SIGNATURE-----\n\nnot base64!\n-----END PGP SIGNATURE-----"));
        Assert.Equal([new SignatureKey(SignatureKind.OpenPgp, null)], SignatureKey.Read("-----BEGIN PGP SIGNATURE-----\n\niQ==\n-----END PGP SIGNATURE-----"));
        Assert.Equal([new SignatureKey(SignatureKind.Unknown, null)], SignatureKey.Read("no signature here"));
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-28")]
    [InlineData("https://example.com/notes.git", "https://example.com/notes.git", null)]
    [InlineData("https://wren@example.com/notes.git", "https://wren@example.com/notes.git", "wren")]
    [InlineData("https://wren:pa55@example.com/notes.git", "https://wren:***@example.com/notes.git", "wren")]
    [InlineData("https://:token@example.com/notes.git", "https://:***@example.com/notes.git", null)]
    [InlineData("git@example.com:wren/notes.git", "git@example.com:wren/notes.git", null)]
    [InlineData("juno@example.com:notes.git", "juno@example.com:notes.git", "juno")]
    [InlineData("C:/notes/backup.git", "C:/notes/backup.git", null)]
    [InlineData("https://pen:p@ss@w0rd@example.com/den.git", "https://pen:***@example.com/den.git", "pen")]
    [InlineData("https://ghp_abcdefghijklmnopqrstuvwxyz0123456789@github.com/pen/den.git", "https://***@github.com/pen/den.git", null)]
    [InlineData("https://github_pat_11ABC@github.com/pen/den.git", "https://***@github.com/pen/den.git", null)]
    [InlineData("https://glpat-xyz@gitlab.example.com/den.git", "https://***@gitlab.example.com/den.git", null)]
    [InlineData("https://x-access-token:ghs_secret@github.com/pen/den.git", "https://***@github.com/pen/den.git", null)]
    [InlineData("https://oauth2:glpat-secret@gitlab.example.com/den.git", "https://***@gitlab.example.com/den.git", null)]
    [InlineData("https://a1b2c3d4e5f6a7b8c9d0e1f2@example.com/den.git", "https://***@example.com/den.git", null)]
    [InlineData("https://example.com/den.git?access_token=SECRET123", "https://example.com/den.git?***", null)]
    [InlineData("https://example.com/den.git#SECRET", "https://example.com/den.git#***", null)]
    [InlineData("https://example.com?token=SECRET", "https://example.com?***", null)]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789@github.com:pen/den.git", "***@github.com:pen/den.git", null)]
    public void ARemoteAddressKeepsItsAccountNameAndLosesItsPassword(string url, string shown, string? account)
    {
        Assert.Equal((shown, account), IdentityAudit.Redact(url));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void AReplaceRefDoesntHideTheNameACommitRecords()
    {
        using var repo = TestRepo.Create();
        Commit(repo, "Leaked", "Ash Everyday", "ash@example.com", "2026-01-05T09:00:00+01:00");
        var original = repo.Git("rev-parse", "HEAD").Trim();
        var edited = repo.Git("cat-file", "commit", original).Replace("Ash Everyday <ash@example.com>", "Wren Calder <wren@example.com>", StringComparison.Ordinal);
        repo.Git("replace", original, WriteObject(repo, "commit", edited));
        Assert.Contains("Wren Calder", repo.Git("log", "--format=%an"), StringComparison.Ordinal);

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal(1, Single(audit, "Ash Everyday", "ash@example.com").AsAuthor);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void ColourAndSignatureSettingsDontChangeWhatsRead()
    {
        using var repo = TestRepo.Create();
        Commit(repo, "Notes", "Ash Everyday", "ash@example.com", "2026-01-05T09:00:00+01:00");
        repo.Git("config", "color.ui", "always");
        repo.Git("config", "color.diff", "always");
        repo.Git("config", "log.showSignature", "true");
        repo.Git("config", "log.decorate", "full");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal(1, audit.Commits);
        Assert.Equal(1, Single(audit, "Ash Everyday", "ash@example.com").AsAuthor);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void ATagOutsideRefsTagsAndATagOfATagAreRead()
    {
        using var repo = TestRepo.Create();
        Commit(repo, "Notes", "Wren Calder", "wren@example.com", "2026-01-05T09:00:00+01:00");
        TestRepo.RunGit(repo.Root, Who("Ash Everyday", "ash@example.com", "2026-01-06T09:00:00+01:00"), "tag", "-a", "-m", "inner", "inner");
        TestRepo.RunGit(repo.Root, Who("Juno Pike", "juno@example.com", "2026-01-07T09:00:00+01:00"), "tag", "-a", "-m", "outer", "outer", "inner");
        repo.Git("tag", "-d", "inner");
        TestRepo.RunGit(repo.Root, Who("Birch Lane", "birch@example.com", "2026-01-08T09:00:00+01:00"), "tag", "-a", "-m", "kept", "kept");
        repo.Git("update-ref", "refs/custom/keep", repo.Git("rev-parse", "kept").Trim());
        repo.Git("tag", "-d", "kept");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal(1, audit.Tags);
        Assert.Equal(3, audit.AnnotatedTags);
        Assert.Equal(1, Single(audit, "Ash Everyday", "ash@example.com").AsTagger);
        Assert.Equal(1, Single(audit, "Juno Pike", "juno@example.com").AsTagger);
        Assert.Equal(1, Single(audit, "Birch Lane", "birch@example.com").AsTagger);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-29")]
    public void AMergedTagsSignatureAndTaggerAreReadPastAnIndentedLine()
    {
        using var repo = TestRepo.Create();
        var first = CraftCommit(repo, "main", "Wren Calder", "wren@example.com", signature: null, parent: null);
        var side = CraftCommit(repo, "side", "Wren Calder", "wren@example.com", signature: null, parent: first);
        var signature = string.Join("\n ", Pgp(Packet(fingerprint: true, keyId: false)).Split('\n'));
        var mergetag =
            $"mergetag object {side}\n type commit\n tag rel\n tagger Ash Everyday <ash@example.com> 1767600000 +0100\n \n Release notes\n \n    - indented item\n Thanks Birch Lane <birch@example.com>\n done\n {signature}\n";
        var tree = WriteObject(repo, "tree", "");
        var merge = WriteObject(
            repo,
            "commit",
            $"tree {tree}\nparent {first}\nparent {side}\nauthor Wren Calder <wren@example.com> 1767600000 +0100\ncommitter Wren Calder <wren@example.com> 1767600000 +0100\n{mergetag}\nMerge tag 'rel'\n");
        repo.Git("update-ref", "refs/heads/main", merge);
        repo.Git("branch", "-D", "side");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal(1, audit.MergedTags);
        Assert.Equal(1, audit.Signing.SignedMergedTags);
        Assert.Equal(1, Single(audit, "Ash Everyday", "ash@example.com").AsTagger);
        var key = Assert.Single(audit.Signing.Keys);
        Assert.Equal(Fingerprint, key.KeyId);
        Assert.Equal((0, 0, 1), (key.Commits, key.Tags, key.MergedTags));
        Assert.Equal(["Ash Everyday <ash@example.com>"], key.SignedAs);
        Assert.False(key.OwnerChecked);
        Assert.Equal([new MessageMention(null, "Thanks Birch Lane <birch@example.com>", 1)], audit.Mentions);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void NamesInTagMessagesAreFound()
    {
        using var repo = TestRepo.Create();
        Commit(repo, "Notes", "Wren Calder", "wren@example.com", "2026-01-05T09:00:00+01:00");
        TestRepo.RunGit(
            repo.Root,
            Who("Wren Calder", "wren@example.com", "2026-01-06T09:00:00+01:00"),
            "tag", "-a", "-m", "Release. Thanks Birch Lane <birch@example.com>", "-m", "Co-Authored-By: Story Helper <helper@example.com>", "v1");
        TestRepo.RunGit(
            repo.Root,
            Who("Wren Calder", "wren@example.com", "2026-01-07T09:00:00+01:00"),
            "tag", "-a", "-m", "Outer", "-m", "Reviewed-by: reviewer@example.com", "outer", "v1");
        repo.Git("tag", "-d", "v1");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        // v1 is read through outer, with git cat-file; outer through git for-each-ref.
        Assert.Equal([new TrailerUse("Co-Authored-By", 1)], Single(audit, "Story Helper", "helper@example.com").InTrailers);
        Assert.Equal(
            [
                new MessageMention(null, "Release. Thanks Birch Lane <birch@example.com>", 1),
                new MessageMention("Reviewed-by", "reviewer@example.com", 1),
            ],
            audit.Mentions);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void ACommitMadeWhileTheAuditReadsIsReadOnASecondTry()
    {
        using var repo = TestRepo.Create();
        Commit(repo, "Notes", "Wren Calder", "wren@example.com", "2026-01-05T09:00:00+01:00");
        var moves = 0;
        IdentityAudit.BetweenReadAndCount = () =>
        {
            if (moves++ == 0)
            {
                Commit(repo, "Automatic checkpoint", "Juno Pike", "juno@example.com", "2026-01-05T09:30:00+01:00");
            }
        };
        try
        {
            var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

            Assert.Equal(2, moves);
            Assert.Equal(2, audit.Commits);
            Assert.Equal(1, Single(audit, "Juno Pike", "juno@example.com").AsAuthor);
        }
        finally
        {
            IdentityAudit.BetweenReadAndCount = null;
        }
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void ARepositoryThatKeepsChangingWhileItsReadFailsRatherThanReportTooLittle()
    {
        using var repo = TestRepo.Create();
        Commit(repo, "Notes", "Wren Calder", "wren@example.com", "2026-01-05T09:00:00+01:00");
        var moves = 0;
        IdentityAudit.BetweenReadAndCount = () => Commit(repo, $"Checkpoint {++moves}", "Juno Pike", "juno@example.com", "2026-01-05T09:30:00+01:00");
        try
        {
            var e = Assert.Throws<Wolfden.Core.History.HistoryException>(() => IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token));

            Assert.Equal(2, moves);
            Assert.Contains("twice", e.Message, StringComparison.Ordinal);
        }
        finally
        {
            IdentityAudit.BetweenReadAndCount = null;
        }
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void ASigningKeyFileInTheHomeFolderIsShownWithATilde()
    {
        using var repo = TestRepo.Create();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        repo.Git("config", "user.signingkey", Path.Combine(home, ".ssh", "pen.pub"));

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        var expected = string.Join(Path.DirectorySeparatorChar, "~", ".ssh", "pen.pub");
        Assert.Contains(new LocalSetting("user.signingkey", expected), audit.Local.Settings);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void NamesElsewhereInMessagesAreFound()
    {
        using var repo = TestRepo.Create();
        Commit(repo, "one\n\nThanks to the team\nCo-Authored-By: Story Helper <helper@example.com>", "Wren Calder", "wren@example.com", "2026-01-05T09:00:00+01:00");
        Commit(repo, "two\n\nSigned-off-by: Ash Everyday", "Wren Calder", "wren@example.com", "2026-01-05T09:01:00+01:00");
        Commit(repo, "three\n\nReviewed-by: reviewer@example.com", "Wren Calder", "wren@example.com", "2026-01-05T09:02:00+01:00");
        Commit(repo, "four\n\nCo-Authored-By: Juno Pike <juno@example.com> (copyedits)", "Wren Calder", "wren@example.com", "2026-01-05T09:03:00+01:00");
        Commit(repo, "five\n\nMention Birch Lane <birch@example.com> in the body.\n\nSigned-off-by: Wren Calder <wren@example.com>", "Wren Calder", "wren@example.com", "2026-01-05T09:04:00+01:00");
        Commit(repo, "six\n\nco-authored-by:Story Helper <helper@example.com>\nCloses: #12\nSee https://example.com/x", "Wren Calder", "wren@example.com", "2026-01-05T09:05:00+01:00");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal([new TrailerUse("Co-Authored-By", 2)], Single(audit, "Story Helper", "helper@example.com").InTrailers);
        Assert.Equal([new TrailerUse("Co-Authored-By", 1)], Single(audit, "Juno Pike", "juno@example.com").InTrailers);
        Assert.Equal([new TrailerUse("Signed-off-by", 1)], Single(audit, "Wren Calder", "wren@example.com").InTrailers);
        Assert.Equal(
            [
                new MessageMention(null, "Mention Birch Lane <birch@example.com> in the body.", 1),
                new MessageMention("Reviewed-by", "reviewer@example.com", 1),
                new MessageMention("Signed-off-by", "Ash Everyday", 1),
            ],
            audit.Mentions);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void NamesOnlyTheReflogsKeepAndThisCopysSettingsAreListedApart()
    {
        using var repo = TestRepo.Create();
        Commit(repo, "Start", "Wren Calder", "wren@example.com", "2026-01-05T09:00:00+01:00");
        TestRepo.RunGit(repo.Root, Who("Ash Everyday", "ash@example.com", "2026-01-05T10:00:00+01:00"), "commit", "--quiet", "--allow-empty", "-m", "oops");
        TestRepo.RunGit(repo.Root, Who("Wren Calder", "wren@example.com", "2026-01-05T10:01:00+01:00"), "commit", "--quiet", "--allow-empty", "--amend", "--reset-author", "-m", "fixed");
        repo.Git("config", "user.name", "Juno Pike");
        repo.Git("config", "user.signingkey", "0123456789ABCDEF");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.DoesNotContain(audit.Identities, i => i.Name == "Ash Everyday");
        Assert.Null(audit.Local.Problem);
        var ash = Assert.Single(audit.Local.Names, n => n.Name == "Ash Everyday");
        // One entry in HEAD's reflog and one in main's.
        Assert.Equal(("ash@example.com", 2, 1), (ash.Email, ash.ReflogEntries, ash.UnreachableCommits));
        Assert.DoesNotContain(audit.Local.Names, n => n.Name == "Wren Calder");
        Assert.Contains(new LocalSetting("user.name", "Juno Pike"), audit.Local.Settings);
        Assert.Contains(new LocalSetting("user.signingkey", "0123456789ABCDEF"), audit.Local.Settings);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-28")]
    public void AShallowCloneSaysSo()
    {
        using var repo = TestRepo.Create();
        Commit(repo, "One", "Wren Calder", "wren@example.com", "2026-01-05T09:00:00+01:00");
        Commit(repo, "Two", "Wren Calder", "wren@example.com", "2026-01-05T09:01:00+01:00");
        var clone = TestRepo.NewFolder();
        try
        {
            TestRepo.RunGit(clone, "clone", "--quiet", "--depth", "1", new Uri(repo.Root).AbsoluteUri, "copy");
            var audit = IdentityAudit.Run(Path.Combine(clone, "copy"), TestRepo.Options, Token);

            Assert.True(audit.Shallow);
            Assert.Equal(1, audit.Commits);
            Assert.False(IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token).Shallow);
        }
        finally
        {
            TestRepo.DeleteFolder(clone);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-29")]
    public void AKeyThatSignedOnlyTagsIsCheckedThroughTheTag()
    {
        using var repo = TestRepo.Create();
        var log = UseFakeGpg(repo, "Ash Everyday <ash@example.com>");
        var commit = CraftCommit(repo, "main", "Wren Calder", "wren@example.com", signature: null, parent: null);
        CraftTag(repo, "v1", commit, "Wren Calder", "wren@example.com", Pgp(Packet(fingerprint: true, keyId: false)));
        repo.Git("config", "log.showSignature", "true");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        var key = Assert.Single(audit.Signing.Keys);
        Assert.Equal((0, 1), (key.Commits, key.Tags));
        Assert.True(key.OwnerChecked);
        Assert.Equal("Ash Everyday <ash@example.com>", key.Owner);
        Assert.Single(File.ReadAllLines(log));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-29")]
    public void AnOwnerIsReadExactlyWhenGitIsSetToShowSignatures()
    {
        using var repo = TestRepo.Create();
        UseFakeGpg(repo, "Ash Everyday <ash@example.com>");
        CraftCommit(repo, "main", "Wren Calder", "wren@example.com", Pgp(Packet(fingerprint: true, keyId: false)), parent: null);
        repo.Git("config", "log.showSignature", "true");
        repo.Git("config", "color.ui", "always");

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Equal("Ash Everyday <ash@example.com>", Assert.Single(audit.Signing.Keys).Owner);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-29")]
    public void SignaturesThatDontNameAKeyAreGroupedBySignerNotMergedIntoOneKey()
    {
        using var repo = TestRepo.Create();
        var x509 = "-----BEGIN SIGNED MESSAGE-----\nMIIBcrafted\n-----END SIGNED MESSAGE-----";
        var first = CraftCommit(repo, "main", "Wren Calder", "wren@example.com", x509, parent: null);
        var second = CraftCommit(repo, "main", "Ash Everyday", "ash@example.com", x509.Replace("crafted", "other", StringComparison.Ordinal), parent: first);
        CraftCommit(repo, "main", "Ash Everyday", "ash@example.com", x509, parent: second);

        var audit = IdentityAudit.Run(repo.DenRoot, TestRepo.Options, Token);

        Assert.Empty(audit.Signing.Keys);
        Assert.Equal(
            [
                new UnidentifiedSignatures(SignatureKind.X509, "Ash Everyday <ash@example.com>", 2, 0, 0),
                new UnidentifiedSignatures(SignatureKind.X509, "Wren Calder <wren@example.com>", 1, 0, 0),
            ],
            audit.Signing.Unidentified);
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-29")]
    public void EverySignatureInABlockIsRead()
    {
        var other = "FEDCBA9876543210FEDCBA9876543210FEDCBA98";
        var one = Packet(fingerprint: true, keyId: false);
        var two = Packet(fingerprint: true, keyId: false, fingerprintHex: other);
        byte[] packets = [0xC2, (byte)one.Length, .. one, 0x88, (byte)two.Length, .. two];

        Assert.Equal(
            [new SignatureKey(SignatureKind.OpenPgp, Fingerprint), new SignatureKey(SignatureKind.OpenPgp, other)],
            SignatureKey.Read(Armor(packets)));
    }

    [Fact]
    [Trait("Scenario", "SC-HIST-29")]
    public void MalformedSignatureBytesEndQuicklyWithNoKey()
    {
        // A length far past the end, as in the review's repro.
        Assert.Equal([new SignatureKey(SignatureKind.OpenPgp, null)], SignatureKey.Read(Armor([0x86, 0xFF, 0xFF, 0xFF, 0xFB])));
        Assert.Equal([new SignatureKey(SignatureKind.OpenPgp, null)], SignatureKey.Read(Armor([0xC2, 0xFF, 0xFF, 0xFF, 0xFF, 0xFB, 4])));
        Assert.Equal([new SignatureKey(SignatureKind.OpenPgp, null)], SignatureKey.Read(Armor([0xC2, 8, 4, 0, 22, 10, 0xFF, 0xFF, 0, 0])));
        Assert.Equal([new SignatureKey(SignatureKind.Ssh, null)], SignatureKey.Read(
            "-----BEGIN SSH SIGNATURE-----\n" + Convert.ToBase64String([.. "SSHSIG"u8, 0, 0, 0, 1, 0xFF, 0xFF, 0xFF, 0xF0]) + "\n-----END SSH SIGNATURE-----"));

        var random = new Random(315);
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 20_000; i++)
        {
            var bytes = new byte[random.Next(0, 80)];
            random.NextBytes(bytes);
            if (bytes.Length > 0 && i % 2 == 0)
            {
                bytes[0] = (byte)(i % 4 == 0 ? 0xC2 : 0x88);
            }

            Assert.NotEmpty(SignatureKey.Read(Armor(bytes)));
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"took {clock.Elapsed}");
    }

    [Theory]
    [Trait("Scenario", "SC-HIST-28")]
    [InlineData(@"D:\Homes\wren\dens\notes", @"D:\Homes\wren", @"~\dens\notes")]
    [InlineData("file:///D:/Homes/wren/backup.git", @"D:\Homes\wren", "file:///~/backup.git")]
    [InlineData("/home/wren/dens/notes", "/home/wren", "~/dens/notes")]
    [InlineData("/home/wrenna/dens", "/home/wren", "/home/wrenna/dens")]
    [InlineData("/srv/dens", "/home/wren", "/srv/dens")]
    public void TheHomeFolderIsShownAsATilde(string text, string home, string shown)
    {
        Assert.Equal(shown, IdentityAudit.Tilde(text, home));
    }

    private static RecordedIdentity Single(IdentityAudit audit, string name, string email) =>
        Assert.Single(audit.Identities, i => i.Name == name && i.Email == email);

    private static Dictionary<string, string?> Who(string name, string email, string date) => new()
    {
        ["GIT_AUTHOR_NAME"] = name,
        ["GIT_AUTHOR_EMAIL"] = email,
        ["GIT_COMMITTER_NAME"] = name,
        ["GIT_COMMITTER_EMAIL"] = email,
        ["GIT_AUTHOR_DATE"] = date,
        ["GIT_COMMITTER_DATE"] = date,
    };

    private static void Commit(TestRepo repo, string message, string name, string email, string date)
    {
        repo.Git("add", "--all");
        TestRepo.RunGit(repo.Root, Who(name, email, date), "commit", "--quiet", "--allow-empty", "-m", message);
    }

    /// <summary>
    /// Points the repository's <c>gpg.program</c> at a script that logs each
    /// call and reports every signature as good, by <paramref name="owner"/>,
    /// so no real gpg or keyring is touched.
    /// </summary>
    private static string UseFakeGpg(TestRepo repo, string owner)
    {
        var folder = Path.Combine(repo.Root, ".git", "fake-gpg");
        Directory.CreateDirectory(folder);
        var log = Path.Combine(folder, "calls.log");
        File.WriteAllText(log, "");
        var script = Path.Combine(folder, "gpg.sh");
        File.WriteAllText(
            script,
            $"""
            #!/bin/sh
            echo "called" >> '{log.Replace('\\', '/')}'
            cat > /dev/null
            echo "[GNUPG:] NEWSIG"
            echo "[GNUPG:] GOODSIG 89ABCDEF01234567 {owner}"
            echo "[GNUPG:] VALIDSIG {Fingerprint} 2026-01-01 0 4 0 22 10 00 {Fingerprint}"
            echo "[GNUPG:] TRUST_ULTIMATE 0 pgp"
            exit 0

            """.ReplaceLineEndings("\n"),
            new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        repo.Git("config", "gpg.program", script.Replace('\\', '/'));
        return log;
    }

    /// <summary>Writes a commit object by hand, with <paramref name="signature"/> in its <c>gpgsig</c> header, and points <paramref name="branch"/> at it.</summary>
    private static string CraftCommit(TestRepo repo, string branch, string name, string email, string? signature, string? parent)
    {
        var tree = WriteObject(repo, "tree", "");
        var text = new StringBuilder();
        text.Append($"tree {tree}\n");
        if (parent is not null)
        {
            text.Append($"parent {parent}\n");
        }

        text.Append($"author {name} <{email}> 1767600000 +0100\n");
        text.Append($"committer {name} <{email}> 1767600000 +0100\n");
        if (signature is not null)
        {
            text.Append("gpgsig ").Append(string.Join("\n ", signature.ReplaceLineEndings("\n").Split('\n'))).Append('\n');
        }

        text.Append("\nCrafted notes\n");
        var commit = WriteObject(repo, "commit", text.ToString());
        repo.Git("update-ref", $"refs/heads/{branch}", commit);
        return commit;
    }

    private static void CraftTag(TestRepo repo, string tag, string commit, string name, string email, string signature)
    {
        var text = $"object {commit}\ntype commit\ntag {tag}\ntagger {name} <{email}> 1767600000 +0100\n\nA tag\n{signature.ReplaceLineEndings("\n")}\n";
        repo.Git("update-ref", $"refs/tags/{tag}", WriteObject(repo, "tag", text));
    }

    private static string WriteObject(TestRepo repo, string type, string text)
    {
        var file = Path.Combine(repo.Root, ".git", $"crafted-{Guid.NewGuid():N}");
        File.WriteAllText(file, text, new UTF8Encoding(false));
        return repo.Git("hash-object", "-t", type, "-w", file).Trim();
    }

    /// <summary>A version 4 OpenPGP signature packet's body, naming its key by fingerprint (subpacket 33), key ID (16), both, or neither.</summary>
    private static byte[] Packet(bool fingerprint, bool keyId, string fingerprintHex = Fingerprint)
    {
        var hashed = new List<byte> { 5, 2, 0x69, 0x5B, 0x00, 0x00 };
        var fpr = Convert.FromHexString(fingerprintHex);
        if (fingerprint)
        {
            hashed.AddRange([(byte)(fpr.Length + 2), 33, 4, .. fpr]);
        }

        var unhashed = new List<byte>();
        if (keyId)
        {
            unhashed.AddRange([9, 16, .. fpr[^8..]]);
        }

        return
        [
            4, 0, 22, 10,
            (byte)(hashed.Count >> 8), (byte)hashed.Count, .. hashed,
            (byte)(unhashed.Count >> 8), (byte)unhashed.Count, .. unhashed,
            0xAB, 0xCD, 0x00, 0x08, 0xFF,
        ];
    }

    /// <summary>An armored OpenPGP signature holding <paramref name="body"/> as one signature packet.</summary>
    private static string Pgp(byte[] body, bool oldFormat = false, bool armorHeader = false) =>
        Armor(oldFormat ? [0x88, (byte)body.Length, .. body] : [0xC2, (byte)body.Length, .. body], armorHeader);

    /// <summary>Armored OpenPGP signature text around <paramref name="packets"/>.</summary>
    private static string Armor(byte[] packets, bool armorHeader = false)
    {
        var base64 = Convert.ToBase64String(packets);
        var lines = Enumerable.Range(0, (base64.Length + 63) / 64).Select(i => base64.Substring(i * 64, Math.Min(64, base64.Length - (i * 64))));
        return "-----BEGIN PGP SIGNATURE-----\n" + (armorHeader ? "Comment: crafted\n" : "") + "\n" + string.Join("\n", lines) + "\n=AbCd\n-----END PGP SIGNATURE-----";
    }

    private static string SshFingerprintOf(string folder, string key)
    {
        var start = new ProcessStartInfo("ssh-keygen") { WorkingDirectory = folder, RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-l");
        start.ArgumentList.Add("-E");
        start.ArgumentList.Add("sha256");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add(key + ".pub");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Split(' ')[1];
    }

    private static bool TryRun(string folder, string program, params string[] args)
    {
        var start = new ProcessStartInfo(program) { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            return process.WaitForExit(30_000) && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}

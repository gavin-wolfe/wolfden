using System.Globalization;
using System.Text.RegularExpressions;
using Wolfden.Core.History;

namespace Wolfden.Git;

/// <summary>Whether a den's history could be audited.</summary>
public enum IdentityAuditState
{
    /// <summary>The den is in a git repository, and its history was read.</summary>
    Audited,

    /// <summary>The den isn't in a git repository: there's no history to audit.</summary>
    NoHistory,

    /// <summary>Git isn't installed, or refused the repository the den is in.</summary>
    Unavailable,
}

/// <summary>
/// A name and email the history records, exactly as git recorded them
/// (case and spaces included), and where it records them.
/// </summary>
/// <param name="Name">The name, as recorded.</param>
/// <param name="Email">The email, as recorded, without its angle brackets.</param>
/// <param name="AsAuthor">How many commits record it as the author.</param>
/// <param name="AsCommitter">How many commits record it as the committer.</param>
/// <param name="AsTagger">How many annotated tags record it as the tagger, a merge commit's embedded tag included.</param>
/// <param name="InTrailers">
/// The <c>Key: Name &lt;email&gt;</c> lines that name it in commit messages,
/// such as <c>Co-Authored-By</c>, wherever they are in the message, with how
/// many commits have one.
/// </param>
/// <param name="Offsets">The UTC offsets of its author, committer, and tagger dates, such as <c>+0100</c>, most used first.</param>
/// <param name="First">Its earliest author, committer, or tagger date, or null if it's only in messages.</param>
/// <param name="Last">Its latest such date, or null if it's only in messages.</param>
public sealed record RecordedIdentity(
    string Name,
    string Email,
    int AsAuthor,
    int AsCommitter,
    int AsTagger,
    IReadOnlyList<TrailerUse> InTrailers,
    IReadOnlyList<string> Offsets,
    DateTimeOffset? First,
    DateTimeOffset? Last);

/// <summary>A key, such as <c>Co-Authored-By</c>, and how many commits use it for one name.</summary>
/// <param name="Key">The key, in the spelling that sorts first (keys are compared ignoring case).</param>
/// <param name="Commits">How many commits.</param>
public sealed record TrailerUse(string Key, int Commits);

/// <summary>
/// Something else in commit messages that may name a person: a line such as
/// <c>Signed-off-by: Wren Calder</c> without an email, a <c>Key:</c> line with
/// a bare email, or a line of the message that holds an email.
/// </summary>
/// <param name="Key">The line's key, such as <c>Reviewed-by</c>, or null for a line of the message.</param>
/// <param name="Text">The value after the key, or the whole line, trimmed (at most 200 characters).</param>
/// <param name="Commits">How many commits have it.</param>
public sealed record MessageMention(string? Key, string Text, int Commits);

/// <summary>A UTC offset recorded in the history, and how many dates carry it.</summary>
/// <param name="Offset">The offset, as git records it, such as <c>-0500</c>.</param>
/// <param name="Dates">How many author, committer, and tagger dates carry it.</param>
public sealed record RecordedOffset(string Offset, int Dates);

/// <summary>One key that signed commits or tags in the history.</summary>
/// <param name="Kind">OpenPGP or SSH (a signature that doesn't say which key is in <see cref="SigningSummary.Unidentified"/>).</param>
/// <param name="KeyId">Its fingerprint, or an OpenPGP key ID.</param>
/// <param name="Commits">How many commits it signed.</param>
/// <param name="Tags">How many annotated tags it signed.</param>
/// <param name="MergedTags">How many signed tags embedded in merge commits (<c>mergetag</c>) it signed.</param>
/// <param name="SignedAs">The names it signed under: the committer of each commit and the tagger of each tag, as <c>Name &lt;email&gt;</c>.</param>
/// <param name="Owner">
/// Who the key belongs to, as this computer's git and gpg (or the SSH
/// allowed signers file) report it for one of its commits or tags; null
/// when they can't say, or it wasn't checked. Once a key is on a keyserver,
/// its names are public.
/// </param>
/// <param name="OwnerChecked">
/// False when the owner wasn't looked up: past the limit of ten keys, or
/// for a key that signed only tags embedded in merge commits, which git
/// can't verify on their own.
/// </param>
public sealed record SigningKeyUse(
    SignatureKind Kind, string? KeyId, int Commits, int Tags, int MergedTags, IReadOnlyList<string> SignedAs, string? Owner, bool OwnerChecked);

/// <summary>Signatures that don't record which key made them (an X.509 one, or one Wolfden can't read), by one signer.</summary>
/// <param name="Kind">The kind of signature.</param>
/// <param name="SignedAs">The committer or tagger, as <c>Name &lt;email&gt;</c>, or empty when the history doesn't record one.</param>
/// <param name="Commits">How many commits.</param>
/// <param name="Tags">How many annotated tags.</param>
/// <param name="MergedTags">How many tags embedded in merge commits.</param>
public sealed record UnidentifiedSignatures(SignatureKind Kind, string SignedAs, int Commits, int Tags, int MergedTags);

/// <summary>What the history's signatures show.</summary>
/// <param name="SignedCommits">How many commits are signed.</param>
/// <param name="SignedTags">How many annotated tags are signed.</param>
/// <param name="SignedMergedTags">How many tags embedded in merge commits are signed.</param>
/// <param name="Keys">The keys that signed them, most used first.</param>
/// <param name="Unidentified">Signatures that don't say which key made them, by signer.</param>
/// <param name="OwnersNotLookedUp">How many keys weren't looked up for an owner, past the limit.</param>
public sealed record SigningSummary(
    int SignedCommits, int SignedTags, int SignedMergedTags, IReadOnlyList<SigningKeyUse> Keys, IReadOnlyList<UnidentifiedSignatures> Unidentified, int OwnersNotLookedUp);

/// <summary>One remote the repository names.</summary>
/// <param name="Name">The remote's name, such as <c>origin</c>.</param>
/// <param name="Url">
/// Its address as git uses it (after <c>insteadOf</c>), with any password,
/// any user name that looks like a token, and any query or fragment replaced
/// by <c>***</c>, and the home folder shown as <c>~</c>.
/// </param>
/// <param name="Uses"><c>fetch</c>, <c>push</c>, or both.</param>
/// <param name="Account">The account name in the address (<c>https://wren@host/…</c>), if it has one other than a generic one such as ssh's <c>git</c>.</param>
public sealed record RemoteAddress(string Name, string Url, IReadOnlyList<string> Uses, string? Account);

/// <summary>A name only this copy of the repository holds: in a reflog, or on a commit only a reflog keeps.</summary>
/// <param name="Name">The name.</param>
/// <param name="Email">The email.</param>
/// <param name="ReflogEntries">How many reflog entries record it as the one who moved a branch or HEAD.</param>
/// <param name="UnreachableCommits">How many commits that only a reflog keeps (amended, rebased away, reset) record it as author or committer.</param>
public sealed record LocalName(string Name, string Email, int ReflogEntries, int UnreachableCommits);

/// <summary>A name or key setting in this copy's own git config, such as <c>user.name</c>.</summary>
/// <param name="Key">The setting, as git lists it (lowercase).</param>
/// <param name="Value">Its value.</param>
public sealed record LocalSetting(string Key, string Value);

/// <summary>
/// What's only in this folder: never pushed or cloned, but carried by a
/// copy of the folder itself (a zip, a synced folder, a disk backup).
/// </summary>
/// <param name="Remotes">The repository's remotes.</param>
/// <param name="Names">Names in the reflogs that the history above doesn't hold.</param>
/// <param name="Settings">Name, email, and signing key settings in this copy's own git config.</param>
/// <param name="Problem">Why the reflogs or settings couldn't be read, or null.</param>
public sealed record LocalOnly(IReadOnlyList<RemoteAddress> Remotes, IReadOnlyList<LocalName> Names, IReadOnlyList<LocalSetting> Settings, string? Problem);

/// <summary>
/// What a den's history would reveal to anyone with a copy of it
/// (#315, design §3.6 item 2): every name and email it records as author,
/// committer, tagger, or in a commit message, the UTC offsets of its dates,
/// its signatures and the keys that made them, and, only in this folder,
/// its remotes, reflogs, and name settings.
/// </summary>
/// <remarks>
/// <para>
/// It reads the whole repository the den is in, every ref (<c>git log
/// --all</c>: branches, tags, remote-tracking branches, notes, and the
/// stash), not just the den's folder, since a push or a clone carries the
/// whole repository. Annotated tags are found under every ref, not only
/// <c>refs/tags</c>, and a tag of a tag is followed to the end. Names are
/// as recorded: replace refs (<c>git replace</c>) and a <c>.mailmap</c>
/// change only how git shows a commit, so neither is applied. The number of
/// commits read is checked against <c>git rev-list --all --count</c>, so a
/// setting that changes git's output can't make it report nothing.
/// Unreachable objects that no reflog names aren't read.
/// </para>
/// <para>
/// It's read-only: every git command is a <see cref="GitCallKind.Stoppable"/>
/// read, stopped at <see cref="GitOptions.ReadTimeout"/> (#143, #146).
/// </para>
/// <para>
/// Declared commit identities (PR 2 of #311) can be compared with
/// <see cref="Identities"/> by name and email, exactly, as git records them.
/// </para>
/// </remarks>
/// <param name="State">Whether the history was read.</param>
/// <param name="Problem">For <see cref="IdentityAuditState.Unavailable"/>, what's wrong.</param>
/// <param name="RepositoryRoot">The repository's top folder, when audited, with the home folder shown as <c>~</c>.</param>
/// <param name="DenIsRepositoryRoot">True if the den's folder is the repository's top folder; false when the den is part of a larger repository.</param>
/// <param name="Shallow">True if this is a shallow clone, holding only part of the history.</param>
/// <param name="Commits">How many commits the repository holds, across every ref.</param>
/// <param name="Tags">How many tags are under <c>refs/tags</c>.</param>
/// <param name="AnnotatedTags">How many annotated tag objects any ref reaches, each recording a tagger.</param>
/// <param name="MergedTags">How many tags are embedded in merge commits (<c>mergetag</c>).</param>
/// <param name="Identities">Every name and email recorded, most used first.</param>
/// <param name="Mentions">Other possible names in commit messages.</param>
/// <param name="Offsets">Every UTC offset recorded, most used first.</param>
/// <param name="Signing">The signatures, and the keys that made them.</param>
/// <param name="Local">What's only in this folder.</param>
public sealed partial record IdentityAudit(
    IdentityAuditState State,
    string? Problem,
    string? RepositoryRoot,
    bool DenIsRepositoryRoot,
    bool Shallow,
    int Commits,
    int Tags,
    int AnnotatedTags,
    int MergedTags,
    IReadOnlyList<RecordedIdentity> Identities,
    IReadOnlyList<MessageMention> Mentions,
    IReadOnlyList<RecordedOffset> Offsets,
    SigningSummary Signing,
    LocalOnly Local)
{
    /// <summary>The most keys whose owner is looked up, each with one gpg or ssh-keygen run.</summary>
    internal const int MaxOwnerLookups = 10;

    private const int MaxMentionLength = 200;

    private static readonly SigningSummary NoSigning = new(0, 0, 0, [], [], 0);

    /// <summary>
    /// For every git command the audit runs: replace refs aren't applied, so
    /// a commit is read as recorded, not as a replacement shows it.
    /// </summary>
    private static readonly Dictionary<string, string?> AuditEnvironment = new() { ["GIT_NO_REPLACE_OBJECTS"] = "1" };

    // Options that keep the user's log settings out of what Wolfden parses:
    // no colour, no signature checks (each runs gpg), no notes, no decorations, no mailmap.
    private static readonly string[] LogOptions =
        ["--no-color", "--no-show-signature", "--no-notes", "--no-decorate", "--no-use-mailmap", "--no-abbrev-commit"];

    /// <summary>Audits the history of the den at <paramref name="denRoot"/>.</summary>
    /// <param name="denRoot">The den's folder.</param>
    /// <param name="options">Which git to run, or null for the user's own.</param>
    /// <param name="cancellationToken">Stops git.</param>
    /// <exception cref="HistoryException">A git command failed or didn't finish in time, or its output didn't add up.</exception>
    public static IdentityAudit Run(string denRoot, GitOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(denRoot);
        options ??= GitOptions.Default;
        var root = Path.GetFullPath(denRoot);
        var location = GitRepository.Locate(root, options, cancellationToken);
        switch (location.Kind)
        {
            case GitLocationKind.NotARepository:
                return Empty(IdentityAuditState.NoHistory, null);
            case GitLocationKind.GitNotInstalled:
                return Empty(IdentityAuditState.Unavailable, "Git isn't installed, or Wolfden can't find it on the PATH");
            case GitLocationKind.Unusable:
                return Empty(IdentityAuditState.Unavailable, $"Git can't use the repository the den is in ({location.Problem})");
        }

        var collector = new Collector();
        ReadCommits(collector, Git(options, root, ["log", "--all", .. LogOptions, "--format=raw"], cancellationToken));
        var counted = Git(options, root, ["rev-list", "--all", "--count"], cancellationToken).Trim();
        if (!int.TryParse(counted, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected != collector.Commits)
        {
            throw new HistoryException(
                $"Wolfden read {collector.Commits} commits from git log, but git rev-list counts {counted}, so the audit stopped rather than miss any. " +
                "A git setting may be changing git log's output.");
        }

        var tags = ReadTags(collector, Git(
            options,
            root,
            ["for-each-ref", "--format=%01%(refname)%02%(objectname)%02%(objecttype)%02%(taggername)%02%(taggeremail)%02%(taggerdate:raw)%02%(type)%02%(object)%02%(contents:signature)"],
            cancellationToken));
        while (collector.NextNestedTag() is { } nested)
        {
            ReadTagObject(collector, nested, Git(options, root, ["cat-file", "tag", nested], cancellationToken));
        }

        var shallow = Git(options, root, ["rev-parse", "--is-shallow-repository"], cancellationToken).Trim() == "true";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var remotes = ReadRemotes(Git(options, root, ["remote", "-v"], cancellationToken), home);
        var signing = collector.Signing(options, root, cancellationToken);
        var local = ReadLocal(collector, options, root, remotes, cancellationToken);

        var repositoryRoot = location.Root!;
        return new IdentityAudit(
            IdentityAuditState.Audited,
            null,
            Tilde(repositoryRoot, home),
            SameFolder(repositoryRoot, root),
            shallow,
            collector.Commits,
            tags,
            collector.TagObjects,
            collector.MergedTags,
            collector.Identities(),
            collector.MentionList(),
            collector.OffsetList(),
            signing,
            local);
    }

    private static IdentityAudit Empty(IdentityAuditState state, string? problem) =>
        new(state, problem, null, false, false, 0, 0, 0, 0, [], [], [], NoSigning, new LocalOnly([], [], [], null));

    private static bool SameFolder(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// <paramref name="text"/> with the home folder, wherever it appears (with
    /// either slash), shown as <c>~</c>, since its name is often the
    /// computer account's, which can be a real name.
    /// </summary>
    internal static string Tilde(string text, string? home)
    {
        if (string.IsNullOrEmpty(home) || home.Length < 3)
        {
            return text;
        }

        home = Path.TrimEndingDirectorySeparator(home);
        foreach (var form in new[] { home, home.Replace('\\', '/'), home.Replace('/', '\\') }.Distinct())
        {
            var at = text.IndexOf(form, PathComparison);
            while (at >= 0)
            {
                var after = at + form.Length;
                if (after == text.Length || text[after] is '/' or '\\')
                {
                    text = text[..at] + "~" + text[after..];
                    after = at + 1;
                }

                at = after < text.Length ? text.IndexOf(form, after, PathComparison) : -1;
            }
        }

        return text;
    }

    /// <summary>Runs a read-only git command and returns its whole output, or throws a <see cref="HistoryException"/>.</summary>
    private static string Git(GitOptions options, string folder, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var command = GitProcess.CommandName(args);
        var run = Start(options, folder, args, cancellationToken);
        return run.Succeeded ? GitHistory.OutputOf(run, command) : throw new HistoryException($"git {command} failed: {run.Said}");
    }

    private static GitRun Start(GitOptions options, string folder, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        try
        {
            return GitProcess.Run(options, folder, args, environment: AuditEnvironment, cancellationToken: cancellationToken);
        }
        catch (GitTimeoutException e)
        {
            throw new HistoryException(
                $"{e.Message}, so Wolfden stopped it. Git may be waiting on a credential helper, or the repository may be very large.", e);
        }
        catch (GitNotFoundException e)
        {
            throw new HistoryException($"Git couldn't be run ({e.Message}).", e);
        }
    }

    /// <summary>
    /// Reads <c>git log --format=raw</c>: each commit's author, committer,
    /// signatures, any merged tag's tagger and signature, and the names in
    /// its message (indented by four spaces, after the headers).
    /// </summary>
    internal static void ReadCommits(Collector collector, string output)
    {
        string? commit = null;
        Ident? committer = null;
        var inHeaders = false;
        var signed = false;
        string? header = null;
        var value = new List<string>();
        var message = new List<string>();

        foreach (var line in output.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("commit ", StringComparison.Ordinal))
            {
                EndCommit();
                commit = line["commit ".Length..].Trim();
                committer = null;
                signed = false;
                inHeaders = true;
                collector.Commits++;
                collector.Reachable.Add(commit);
                continue;
            }

            if (commit is null)
            {
                continue;
            }

            if (!inHeaders)
            {
                if (line.StartsWith("    ", StringComparison.Ordinal))
                {
                    message.Add(line[4..]);
                }

                continue;
            }

            if (line.Length == 0)
            {
                // The blank line between the headers and the message.
                EndHeader();
                inHeaders = false;
                continue;
            }

            if (line[0] == ' ' && header is not null)
            {
                // A header's next line: a merged tag's message can start with spaces of its own.
                value.Add(line[1..]);
                continue;
            }

            EndHeader();
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            var name = space < 0 ? line : line[..space];
            var rest = space < 0 ? "" : line[(space + 1)..];
            switch (name)
            {
                case "author":
                    collector.Add(Ident.Parse(rest), Role.Author);
                    break;
                case "committer":
                    committer = Ident.Parse(rest);
                    collector.Add(committer, Role.Committer);
                    break;
                case "gpgsig" or "gpgsig-sha256" or "mergetag":
                    header = name;
                    value.Add(rest);
                    break;
            }
        }

        EndCommit();

        void EndCommit()
        {
            EndHeader();
            if (commit is not null)
            {
                collector.ScanMessage(message);
            }

            message.Clear();
        }

        void EndHeader()
        {
            if (header is null)
            {
                return;
            }

            if (header == "mergetag")
            {
                // A merged signed tag, kept whole in the merge commit.
                collector.MergedTags++;
                var tagger = value.FirstOrDefault(l => l.StartsWith("tagger ", StringComparison.Ordinal));
                var taggerIdent = tagger is null ? null : Ident.Parse(tagger["tagger ".Length..]);
                if (taggerIdent is not null)
                {
                    collector.Add(taggerIdent, Role.Tagger);
                }

                var begin = value.FindIndex(l => l.StartsWith("-----BEGIN ", StringComparison.Ordinal));
                if (begin >= 0)
                {
                    collector.Signed(SignatureKey.Read(string.Join('\n', value.Skip(begin))), taggerIdent, SignedOn.MergedTag, null);
                    collector.SignedMergedTags++;
                }
            }
            else
            {
                // gpgsig, or gpgsig-sha256: a commit can carry both, one for each hash, and counts once.
                collector.Signed(SignatureKey.Read(string.Join('\n', value)), committer, SignedOn.Commit, commit, count: !signed);
                if (!signed)
                {
                    collector.SignedCommits++;
                    signed = true;
                }
            }

            header = null;
            value.Clear();
        }
    }

    /// <summary>
    /// Reads <c>git for-each-ref</c> over every ref: each annotated tag
    /// object's tagger and signature, once however many refs name it, and
    /// any tag it points at, to read next. Returns how many refs are under
    /// <c>refs/tags</c>.
    /// </summary>
    internal static int ReadTags(Collector collector, string output)
    {
        var tags = 0;
        foreach (var record in output.Replace("\r", "", StringComparison.Ordinal).Split('\x01', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('\x02');
            if (fields.Length < 9)
            {
                continue;
            }

            if (fields[0].StartsWith("refs/tags/", StringComparison.Ordinal))
            {
                tags++;
            }

            if (fields[2] != "tag" || !collector.FirstSightOfTag(fields[1]))
            {
                continue;
            }

            Ident? tagger = null;
            if (fields[3].Length > 0 || fields[4].Length > 0)
            {
                tagger = Ident.Parse($"{fields[3]} {fields[4]} {fields[5]}".Trim());
                collector.Add(tagger, Role.Tagger);
            }

            if (fields[8].Contains("-----BEGIN ", StringComparison.Ordinal))
            {
                collector.Signed(SignatureKey.Read(fields[8]), tagger, SignedOn.Tag, fields[1]);
                collector.SignedTags++;
            }

            if (fields[6] == "tag")
            {
                collector.QueueNestedTag(fields[7]);
            }
        }

        return tags;
    }

    /// <summary>Reads a tag object from <c>git cat-file tag</c>: its tagger, its signature, and the tag it points at, if it does.</summary>
    internal static void ReadTagObject(Collector collector, string id, string raw)
    {
        var lines = raw.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        var blank = Array.IndexOf(lines, "");
        var headers = blank < 0 ? lines : lines[..blank];
        var body = blank < 0 ? [] : lines[(blank + 1)..];
        Ident? tagger = null;
        string? type = null, target = null;
        foreach (var header in headers)
        {
            if (header.StartsWith("tagger ", StringComparison.Ordinal))
            {
                tagger = Ident.Parse(header["tagger ".Length..]);
                collector.Add(tagger, Role.Tagger);
            }
            else if (header.StartsWith("type ", StringComparison.Ordinal))
            {
                type = header["type ".Length..].Trim();
            }
            else if (header.StartsWith("object ", StringComparison.Ordinal))
            {
                target = header["object ".Length..].Trim();
            }
        }

        var begin = Array.FindIndex(body, l => l.StartsWith("-----BEGIN ", StringComparison.Ordinal));
        if (begin >= 0)
        {
            collector.Signed(SignatureKey.Read(string.Join('\n', body[begin..])), tagger, SignedOn.Tag, id);
            collector.SignedTags++;
        }

        if (type == "tag" && target is not null)
        {
            collector.QueueNestedTag(target);
        }
    }

    /// <summary>Reads <c>git remote -v</c>, one line per address and use.</summary>
    internal static IReadOnlyList<RemoteAddress> ReadRemotes(string output, string? home)
    {
        var remotes = new List<(string Name, string Url, List<string> Uses)>();
        foreach (var line in output.Replace("\r", "", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = RemoteLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            var url = match.Groups["url"].Value;
            var existing = remotes.FindIndex(r => r.Name == name && r.Url == url);
            if (existing < 0)
            {
                remotes.Add((name, url, [match.Groups["use"].Value]));
            }
            else
            {
                remotes[existing].Uses.Add(match.Groups["use"].Value);
            }
        }

        return remotes.Select(r =>
        {
            var (url, account) = Redact(r.Url);
            return new RemoteAddress(r.Name, Tilde(url, home), r.Uses, account);
        }).ToList();
    }

    /// <summary>
    /// The address with secrets replaced by <c>***</c>, and the account name
    /// in it, if any. Secrets are a password (everything after the first
    /// <c>:</c> of the user information, which runs to the last <c>@</c>
    /// before the path), a user name that looks like a token, and a query or
    /// fragment, which can carry an access token.
    /// </summary>
    internal static (string Url, string? Account) Redact(string url)
    {
        var scheme = UrlScheme().Match(url);
        if (scheme.Success)
        {
            var rest = url[scheme.Length..];
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            var authority = slash < 0 ? rest : rest[..slash];
            var path = slash < 0 ? "" : rest[slash..];
            var at = authority.LastIndexOf('@');
            string? account = null;
            if (at >= 0)
            {
                var info = authority[..at];
                var colon = info.IndexOf(':', StringComparison.Ordinal);
                var user = colon < 0 ? info : info[..colon];
                string shown;
                if (LooksLikeToken(user))
                {
                    shown = "***";
                }
                else
                {
                    shown = colon < 0 ? user : user + ":***";
                    account = Account(Uri.UnescapeDataString(user));
                }

                authority = shown + authority[at..];
            }

            return (scheme.Value + Unquery(authority) + Unquery(path), account);
        }

        var scp = ScpAddress().Match(url);
        if (scp.Success)
        {
            var user = scp.Groups["user"].Value;
            return LooksLikeToken(user)
                ? ("***@" + scp.Groups["rest"].Value, null)
                : (url, Account(user));
        }

        return (url, null);

        static string Unquery(string text)
        {
            var cut = text.IndexOfAny(['?', '#']);
            return cut < 0 ? text : text[..cut] + text[cut] + "***";
        }
    }

    /// <summary>True if a user name in an address looks like an access token rather than an account name.</summary>
    internal static bool LooksLikeToken(string user)
    {
        string[] prefixes = ["ghp_", "gho_", "ghu_", "ghs_", "ghr_", "github_pat_", "glpat-", "glptt-", "x-access-token", "x-token-auth", "oauth2", "oauth"];
        if (prefixes.Any(p => user.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var letters = user.Any(char.IsAsciiLetter);
        var digits = user.Any(char.IsAsciiDigit);
        return user.Length >= 32 || (user.Length >= 20 && letters && digits);
    }

    private static string? Account(string user) =>
        user.Length == 0 || user is "git" or "token" or "anonymous" ? null : user;

    /// <summary>The reflogs' names that the history doesn't hold, and this copy's own name and key settings.</summary>
    private static LocalOnly ReadLocal(Collector collector, GitOptions options, string root, IReadOnlyList<RemoteAddress> remotes, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var names = new List<LocalName>();
        try
        {
            names = ReadReflogs(collector, Git(
                options,
                root,
                ["log", "--walk-reflogs", "--all", .. LogOptions, "--format=%x01%H%x02%gn%x02%ge%x02%an%x02%ae%x02%cn%x02%ce"],
                cancellationToken));
        }
        catch (HistoryException e)
        {
            problems.Add($"the reflogs couldn't be read ({e.Message})");
        }

        var settings = new List<LocalSetting>();
        var run = Start(
            options,
            root,
            ["config", "--local", "--includes", "--get-regexp", @"^(user|author|committer)\.(name|email)$|^user\.signingkey$"],
            cancellationToken);
        if (run.Succeeded && run.OutputComplete)
        {
            foreach (var line in run.Output.Replace("\r", "", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var space = line.IndexOf(' ', StringComparison.Ordinal);
                settings.Add(space < 0 ? new LocalSetting(line, "") : new LocalSetting(line[..space], line[(space + 1)..]));
            }
        }
        else if (run.ExitCode != 1)
        {
            // 1 means none is set.
            problems.Add($"this copy's git config couldn't be read ({run.Said})");
        }

        return new LocalOnly(remotes, names, settings, problems.Count == 0 ? null : string.Join("; ", problems));
    }

    /// <summary>Reads <c>git log --walk-reflogs --all</c>: who moved each ref, and the authors of commits only a reflog keeps.</summary>
    internal static List<LocalName> ReadReflogs(Collector collector, string output)
    {
        var counts = new Dictionary<(string Name, string Email), (int Entries, int Commits)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in output.Replace("\r", "", StringComparison.Ordinal).Split('\x01', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimEnd('\n').Split('\x02');
            if (fields.Length < 7)
            {
                continue;
            }

            Count(fields[1], fields[2], entry: true);
            if (!collector.Reachable.Contains(fields[0]) && seen.Add(fields[0]))
            {
                Count(fields[3], fields[4], entry: false);
                if (fields[5] != fields[3] || fields[6] != fields[4])
                {
                    Count(fields[5], fields[6], entry: false);
                }
            }
        }

        return counts
            .Select(p => new LocalName(p.Key.Name, p.Key.Email, p.Value.Entries, p.Value.Commits))
            .OrderByDescending(n => n.ReflogEntries + n.UnreachableCommits)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();

        void Count(string name, string email, bool entry)
        {
            if ((name.Length == 0 && email.Length == 0) || collector.Holds(name, email))
            {
                return;
            }

            var current = counts.GetValueOrDefault((name, email));
            counts[(name, email)] = entry ? (current.Entries + 1, current.Commits) : (current.Entries, current.Commits + 1);
        }
    }

    [GeneratedRegex(@"^\s*(?<key>[A-Za-z][A-Za-z0-9-]*)\s*:\s*(?<value>.*?)\s*$")]
    private static partial Regex KeyLine();

    // A name and an email in angle brackets; anything after them (a comment) is ignored.
    [GeneratedRegex(@"^(?<name>[^<>]*?)\s*<(?<email>[^<>\s]+)>")]
    private static partial Regex NameAndEmail();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+")]
    private static partial Regex Email();

    [GeneratedRegex(@"^(?<name>\S+)\t(?<url>.*) \((?<use>fetch|push)\)$")]
    private static partial Regex RemoteLine();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*://")]
    private static partial Regex UrlScheme();

    // scp-like: user@host:path, with no scheme; the user runs to the last '@' before the host.
    [GeneratedRegex(@"^(?<user>[^/:]+)@(?<rest>[^@/:]+:.*)$")]
    private static partial Regex ScpAddress();

    internal enum Role
    {
        Author,
        Committer,
        Tagger,
    }

    /// <summary>What a signature is on.</summary>
    internal enum SignedOn
    {
        Commit,
        Tag,
        MergedTag,
    }

    /// <summary>A name, email, and date as git records them: <c>Name &lt;email&gt; 1700000000 +0100</c>.</summary>
    internal sealed partial record Ident(string Name, string Email, DateTimeOffset? Date, string? Offset)
    {
        public string Display => $"{Name} <{Email}>";

        public static Ident Parse(string text)
        {
            var match = IdentLine().Match(text);
            if (!match.Success)
            {
                var open = text.IndexOf('<', StringComparison.Ordinal);
                var close = text.IndexOf('>', StringComparison.Ordinal);
                return open >= 0 && close > open
                    ? new Ident(text[..open].Trim(), text[(open + 1)..close], null, null)
                    : new Ident(text.Trim(), "", null, null);
            }

            var offset = match.Groups["zone"].Value;
            DateTimeOffset? date = null;
            if (long.TryParse(match.Groups["time"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            {
                var sign = offset[0] == '-' ? -1 : 1;
                var hours = int.Parse(offset.AsSpan(1, 2), CultureInfo.InvariantCulture);
                var minutes = int.Parse(offset.AsSpan(3, 2), CultureInfo.InvariantCulture);
                var span = new TimeSpan(hours, minutes, 0) * sign;
                if (span.Duration() <= TimeSpan.FromHours(14))
                {
                    date = DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(span);
                }
            }

            return new Ident(match.Groups["name"].Value, match.Groups["email"].Value, date, offset);
        }

        [GeneratedRegex(@"^(?<name>.*?) ?<(?<email>[^<>]*)> (?<time>\d+) (?<zone>[+-]\d{4})\s*$")]
        private static partial Regex IdentLine();
    }

    /// <summary>Gathers what the history records, as it's read.</summary>
    internal sealed class Collector
    {
        private readonly Dictionary<(string Name, string Email), Entry> _identities = [];
        private readonly Dictionary<(string? Key, string Text), int> _mentions = [];
        private readonly Dictionary<string, string> _mentionKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal);
        private readonly List<KeyEntry> _keys = [];
        private readonly Dictionary<(SignatureKind Kind, string SignedAs), int[]> _unidentified = [];
        private readonly HashSet<string> _tagObjects = new(StringComparer.Ordinal);
        private readonly Queue<string> _nestedTags = new();

        public int Commits { get; set; }

        public int SignedCommits { get; set; }

        public int SignedTags { get; set; }

        public int MergedTags { get; set; }

        public int SignedMergedTags { get; set; }

        public int TagObjects => _tagObjects.Count;

        /// <summary>Every commit any ref reaches.</summary>
        public HashSet<string> Reachable { get; } = new(StringComparer.Ordinal);

        /// <summary>True the first time a tag object is seen, so one named by several refs is read once.</summary>
        public bool FirstSightOfTag(string id) => _tagObjects.Add(id);

        public void QueueNestedTag(string id)
        {
            if (!_tagObjects.Contains(id))
            {
                _nestedTags.Enqueue(id);
            }
        }

        /// <summary>The next tag object a tag points at that hasn't been read, or null.</summary>
        public string? NextNestedTag()
        {
            while (_nestedTags.TryDequeue(out var id))
            {
                if (_tagObjects.Add(id))
                {
                    return id;
                }
            }

            return null;
        }

        /// <summary>True if the history holds this name and email as author, committer, tagger, or in a message.</summary>
        public bool Holds(string name, string email) => _identities.ContainsKey((name, email));

        public void Add(Ident ident, Role role)
        {
            var entry = Get(ident.Name, ident.Email);
            switch (role)
            {
                case Role.Author:
                    entry.Author++;
                    break;
                case Role.Committer:
                    entry.Committer++;
                    break;
                default:
                    entry.Tagger++;
                    break;
            }

            if (ident.Offset is { } offset)
            {
                entry.Offsets[offset] = entry.Offsets.GetValueOrDefault(offset) + 1;
                _offsets[offset] = _offsets.GetValueOrDefault(offset) + 1;
            }

            if (ident.Date is { } date)
            {
                entry.First = entry.First is null || date < entry.First ? date : entry.First;
                entry.Last = entry.Last is null || date > entry.Last ? date : entry.Last;
            }
        }

        /// <summary>
        /// Looks through one commit's message for names: every <c>Key: Name
        /// &lt;email&gt;</c> line, anywhere in it, is a trailer use; a
        /// <c>Key:</c> line with a bare email, or a <c>…-by:</c> line without
        /// one, and any other line holding an email, is a mention. Each counts
        /// once per commit.
        /// </summary>
        public void ScanMessage(IReadOnlyList<string> lines)
        {
            var trailers = new HashSet<(string, string, string)>();
            var mentions = new HashSet<(string?, string)>();
            foreach (var line in lines)
            {
                var keyLine = KeyLine().Match(line);
                if (keyLine.Success)
                {
                    var key = keyLine.Groups["key"].Value;
                    var value = keyLine.Groups["value"].Value;
                    var named = NameAndEmail().Match(value);
                    if (named.Success && named.Groups["name"].Value.Trim().Length > 0 && named.Groups["email"].Value.Contains('@', StringComparison.Ordinal))
                    {
                        var name = named.Groups["name"].Value.Trim();
                        var email = named.Groups["email"].Value;
                        if (trailers.Add((key.ToUpperInvariant(), name, email)))
                        {
                            AddTrailer(name, email, key);
                        }

                        continue;
                    }

                    if (value.Length > 0 && (Email().IsMatch(value) || key.EndsWith("-by", StringComparison.OrdinalIgnoreCase)))
                    {
                        var shown = _mentionKeys.TryGetValue(key, out var first) ? first : _mentionKeys[key] = key;
                        if (mentions.Add((shown, Shorten(value))))
                        {
                            AddMention(shown, Shorten(value));
                        }

                        continue;
                    }
                }

                if (Email().IsMatch(line) && mentions.Add((null, Shorten(line.Trim()))))
                {
                    AddMention(null, Shorten(line.Trim()));
                }
            }

            static string Shorten(string text) => text.Length <= MaxMentionLength ? text : text[..MaxMentionLength] + "…";
        }

        private void AddTrailer(string name, string email, string key)
        {
            // Keys are compared ignoring case, and shown in the spelling that sorts first (Co-Authored-By before co-authored-by).
            var entry = Get(name, email);
            var existing = entry.Trailers.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            var count = existing is null ? 0 : entry.Trailers[existing];
            if (existing is not null && string.CompareOrdinal(key, existing) < 0)
            {
                entry.Trailers.Remove(existing);
                existing = key;
            }

            existing ??= key;
            entry.Trailers[existing] = count + 1;
        }

        private void AddMention(string? key, string text) => _mentions[(key, text)] = _mentions.GetValueOrDefault((key, text)) + 1;

        /// <summary>
        /// Records the signatures in one signature block, by
        /// <paramref name="signer"/>. <paramref name="target"/> is the commit
        /// or tag object to verify for the key's owner, or null.
        /// </summary>
        public void Signed(IReadOnlyList<SignatureKey> keys, Ident? signer, SignedOn on, string? target, bool count = true)
        {
            foreach (var key in keys.Distinct())
            {
                if (key.KeyId is null)
                {
                    var group = (key.Kind, signer?.Display ?? "");
                    if (!_unidentified.TryGetValue(group, out var counts))
                    {
                        counts = new int[3];
                        _unidentified[group] = counts;
                    }

                    if (count)
                    {
                        counts[(int)on]++;
                    }

                    continue;
                }

                var entry = _keys.FirstOrDefault(k => k.Kind == key.Kind && k.KeyId == key.KeyId && k.KeyIdIsShort == key.KeyIdIsShort);
                if (entry is null)
                {
                    entry = new KeyEntry(key.Kind, key.KeyId, key.KeyIdIsShort);
                    _keys.Add(entry);
                }

                if (count)
                {
                    entry.Counts[(int)on]++;
                }

                if (on == SignedOn.Commit)
                {
                    entry.Commit ??= target;
                }
                else if (on == SignedOn.Tag)
                {
                    entry.Tag ??= target;
                }

                if (signer is not null && !entry.SignedAs.Contains(signer.Display))
                {
                    entry.SignedAs.Add(signer.Display);
                }
            }
        }

        public IReadOnlyList<RecordedIdentity> Identities() => _identities
            .Select(p => new RecordedIdentity(
                p.Key.Name,
                p.Key.Email,
                p.Value.Author,
                p.Value.Committer,
                p.Value.Tagger,
                p.Value.Trailers.Select(t => new TrailerUse(t.Key, t.Value)).ToList(),
                MostUsed(p.Value.Offsets).Select(o => o.Key).ToList(),
                p.Value.First,
                p.Value.Last))
            .OrderByDescending(i => i.AsAuthor + i.AsCommitter + i.AsTagger + i.InTrailers.Sum(t => t.Commits))
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .ThenBy(i => i.Email, StringComparer.Ordinal)
            .ToList();

        public IReadOnlyList<MessageMention> MentionList() => _mentions
            .Select(p => new MessageMention(p.Key.Key, p.Key.Text, p.Value))
            .OrderByDescending(m => m.Commits)
            .ThenBy(m => m.Key ?? "", StringComparer.Ordinal)
            .ThenBy(m => m.Text, StringComparer.Ordinal)
            .ToList();

        public IReadOnlyList<RecordedOffset> OffsetList() => MostUsed(_offsets).Select(o => new RecordedOffset(o.Key, o.Value)).ToList();

        /// <summary>The keys, with an OpenPGP key ID merged into the key whose fingerprint ends with it, and owners looked up.</summary>
        public SigningSummary Signing(GitOptions options, string folder, CancellationToken cancellationToken)
        {
            foreach (var shortKey in _keys.Where(k => k.KeyIdIsShort).ToList())
            {
                var full = _keys.FirstOrDefault(k => !k.KeyIdIsShort && k.Kind == SignatureKind.OpenPgp && k.KeyId is { Length: 40 } id
                    && id.EndsWith(shortKey.KeyId!, StringComparison.OrdinalIgnoreCase));
                if (full is not null)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        full.Counts[i] += shortKey.Counts[i];
                    }

                    full.Commit ??= shortKey.Commit;
                    full.Tag ??= shortKey.Tag;
                    full.SignedAs.AddRange(shortKey.SignedAs.Where(s => !full.SignedAs.Contains(s)));
                    _keys.Remove(shortKey);
                }
            }

            var ordered = _keys.OrderByDescending(k => k.Counts.Sum()).ThenBy(k => k.KeyId, StringComparer.Ordinal).ToList();
            var lookups = 0;
            var notLookedUp = 0;
            var keys = new List<SigningKeyUse>();
            foreach (var key in ordered)
            {
                string? owner = null;
                var checkedOwner = false;
                if (key.Commit is not null || key.Tag is not null)
                {
                    if (lookups < MaxOwnerLookups)
                    {
                        lookups++;
                        checkedOwner = true;
                        owner = key.Commit is not null
                            ? CommitOwner(options, folder, key.Commit, cancellationToken)
                            : TagOwner(options, folder, key.Tag!, cancellationToken);
                    }
                    else
                    {
                        notLookedUp++;
                    }
                }

                keys.Add(new SigningKeyUse(key.Kind, key.KeyId, key.Counts[0], key.Counts[1], key.Counts[2], key.SignedAs, owner, checkedOwner));
            }

            var unidentified = _unidentified
                .Select(p => new UnidentifiedSignatures(p.Key.Kind, p.Key.SignedAs, p.Value[0], p.Value[1], p.Value[2]))
                .OrderByDescending(u => u.Commits + u.Tags + u.MergedTags)
                .ThenBy(u => u.SignedAs, StringComparer.Ordinal)
                .ToList();
            return new SigningSummary(SignedCommits, SignedTags, SignedMergedTags, keys, unidentified, notLookedUp);
        }

        /// <summary>
        /// Who signed <paramref name="commit"/>, as git's verifier reports it
        /// (<c>%GS</c>), or null. This one check runs gpg or ssh-keygen; a
        /// failure or a timeout only means the owner isn't known.
        /// </summary>
        private static string? CommitOwner(GitOptions options, string folder, string commit, CancellationToken cancellationToken)
        {
            var output = Lookup(options, folder, ["log", "-1", "--no-walk", .. LogOptions, "--format=%GS", commit, "--"], cancellationToken);
            var owner = output?.Output.Trim() ?? "";
            return owner.Length == 0 ? null : owner;
        }

        /// <summary>
        /// Who signed the tag object <paramref name="tag"/>, from <c>git
        /// verify-tag --raw</c>: gpg's <c>GOODSIG</c> (or another status
        /// naming the signer) line, or ssh-keygen's "Good signature for".
        /// </summary>
        private static string? TagOwner(GitOptions options, string folder, string tag, CancellationToken cancellationToken)
        {
            var run = Lookup(options, folder, ["verify-tag", "--raw", tag], cancellationToken);
            if (run is null)
            {
                return null;
            }

            var text = run.Error + "\n" + run.Output;
            var gpg = GpgSigner().Match(text);
            if (gpg.Success)
            {
                return gpg.Groups["who"].Value.Trim();
            }

            var ssh = SshSigner().Match(text);
            return ssh.Success ? ssh.Groups["who"].Value.Trim() : null;
        }

        private static GitRun? Lookup(GitOptions options, string folder, IReadOnlyList<string> args, CancellationToken cancellationToken)
        {
            try
            {
                var run = GitProcess.Run(options, folder, args, environment: AuditEnvironment, cancellationToken: cancellationToken);
                return run.OutputComplete ? run : null;
            }
            catch (Exception e) when (e is GitTimeoutException or GitNotFoundException)
            {
                return null;
            }
        }

        private static IEnumerable<KeyValuePair<string, int>> MostUsed(Dictionary<string, int> counts) =>
            counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal);

        private Entry Get(string name, string email)
        {
            if (!_identities.TryGetValue((name, email), out var entry))
            {
                entry = new Entry();
                _identities[(name, email)] = entry;
            }

            return entry;
        }

        private sealed class Entry
        {
            public int Author { get; set; }

            public int Committer { get; set; }

            public int Tagger { get; set; }

            public Dictionary<string, int> Trailers { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, int> Offsets { get; } = new(StringComparer.Ordinal);

            public DateTimeOffset? First { get; set; }

            public DateTimeOffset? Last { get; set; }
        }

        private sealed class KeyEntry(SignatureKind kind, string? keyId, bool keyIdIsShort)
        {
            public SignatureKind Kind { get; } = kind;

            public string? KeyId { get; } = keyId;

            public bool KeyIdIsShort { get; } = keyIdIsShort;

            /// <summary>Commits, tags, and merged tags signed, indexed by <see cref="SignedOn"/>.</summary>
            public int[] Counts { get; } = new int[3];

            public string? Commit { get; set; }

            public string? Tag { get; set; }

            public List<string> SignedAs { get; } = [];
        }
    }

    // gpg's status lines that name the signer: [GNUPG:] GOODSIG <key id> <user id>, and the like.
    [GeneratedRegex(@"\[GNUPG:\] (?:GOODSIG|EXPSIG|EXPKEYSIG|REVKEYSIG|BADSIG) \S+ (?<who>[^\r\n]+)")]
    private static partial Regex GpgSigner();

    [GeneratedRegex(@"Good ""git"" signature for (?<who>.+?) with ")]
    private static partial Regex SshSigner();
}

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
/// <param name="AsTagger">How many annotated tags record it as the tagger (a merge commit's embedded tag included).</param>
/// <param name="InTrailers">The trailers that name it in commit messages, such as <c>Co-Authored-By</c>, with how many commits do.</param>
/// <param name="Offsets">The UTC offsets of its author, committer, and tagger dates, such as <c>+0100</c>, most used first.</param>
/// <param name="First">Its earliest author, committer, or tagger date, or null if it's only in trailers.</param>
/// <param name="Last">Its latest such date, or null if it's only in trailers.</param>
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

/// <summary>A trailer key, such as <c>Co-Authored-By</c>, and how many commits use it for one name.</summary>
/// <param name="Key">The key, as first seen (keys are compared ignoring case).</param>
/// <param name="Commits">How many commits.</param>
public sealed record TrailerUse(string Key, int Commits);

/// <summary>A UTC offset recorded in the history, and how many dates carry it.</summary>
/// <param name="Offset">The offset, as git records it, such as <c>-0500</c>.</param>
/// <param name="Dates">How many author, committer, and tagger dates carry it.</param>
public sealed record RecordedOffset(string Offset, int Dates);

/// <summary>One key that signed commits or tags in the history.</summary>
/// <param name="Kind">OpenPGP, SSH, or X.509.</param>
/// <param name="KeyId">Its fingerprint (or OpenPGP key ID), or null when the signature doesn't say which key, as with X.509.</param>
/// <param name="Commits">How many commits it signed.</param>
/// <param name="Tags">How many annotated tags it signed.</param>
/// <param name="SignedAs">The names it signed under: the committer of each commit and the tagger of each tag, as <c>Name &lt;email&gt;</c>.</param>
/// <param name="Owner">
/// Who the key belongs to, as this computer's git and gpg (or the SSH
/// allowed signers file) report it for one of its commits; null when they
/// can't say, for example because this computer doesn't have the key.
/// Once a key is on a keyserver, its names are public.
/// </param>
public sealed record SigningKeyUse(SignatureKind Kind, string? KeyId, int Commits, int Tags, IReadOnlyList<string> SignedAs, string? Owner);

/// <summary>What the history's signatures show.</summary>
/// <param name="SignedCommits">How many commits are signed.</param>
/// <param name="SignedTags">How many annotated tags are signed.</param>
/// <param name="Keys">The keys that signed them, most used first.</param>
/// <param name="OwnersNotLookedUp">How many keys weren't looked up for an owner, past the limit.</param>
public sealed record SigningSummary(int SignedCommits, int SignedTags, IReadOnlyList<SigningKeyUse> Keys, int OwnersNotLookedUp);

/// <summary>One remote the repository names.</summary>
/// <param name="Name">The remote's name, such as <c>origin</c>.</param>
/// <param name="Url">Its address, with any password in it replaced by <c>***</c>.</param>
/// <param name="Uses"><c>fetch</c>, <c>push</c>, or both.</param>
/// <param name="Account">The account name in the address (<c>https://wren@host/…</c>), if it has one other than ssh's usual <c>git</c>.</param>
public sealed record RemoteAddress(string Name, string Url, IReadOnlyList<string> Uses, string? Account);

/// <summary>
/// What a den's history would reveal to anyone with a copy of it
/// (#315, design §3.6 item 2): every name and email it records as author,
/// committer, tagger, or in a trailer, the UTC offsets of its dates, its
/// signatures and the keys that made them, and its remotes. A push or a
/// copy carries all of it.
/// </summary>
/// <remarks>
/// <para>
/// It reads the whole repository the den is in, every ref (<c>git log
/// --all</c>: branches, tags, remote-tracking branches, notes, and the
/// stash), not just the den's folder, since a push or a clone carries the
/// whole repository. Names are as recorded: a <c>.mailmap</c> isn't applied,
/// since it changes only how git shows them.
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
/// <param name="RepositoryRoot">The repository's top folder, when audited.</param>
/// <param name="DenIsRepositoryRoot">True if the den's folder is the repository's top folder; false when the den is part of a larger repository.</param>
/// <param name="Commits">How many commits the repository holds, across every ref.</param>
/// <param name="Tags">How many tags it holds.</param>
/// <param name="AnnotatedTags">How many of them are annotated, recording a tagger.</param>
/// <param name="Identities">Every name and email recorded, most used first.</param>
/// <param name="Offsets">Every UTC offset recorded, most used first.</param>
/// <param name="Signing">The signatures, and the keys that made them.</param>
/// <param name="Remotes">The repository's remotes.</param>
public sealed partial record IdentityAudit(
    IdentityAuditState State,
    string? Problem,
    string? RepositoryRoot,
    bool DenIsRepositoryRoot,
    int Commits,
    int Tags,
    int AnnotatedTags,
    IReadOnlyList<RecordedIdentity> Identities,
    IReadOnlyList<RecordedOffset> Offsets,
    SigningSummary Signing,
    IReadOnlyList<RemoteAddress> Remotes)
{
    /// <summary>The most keys whose owner is looked up, each with one gpg or ssh-keygen run.</summary>
    internal const int MaxOwnerLookups = 10;

    private static readonly SigningSummary NoSigning = new(0, 0, [], 0);

    // Options that keep the user's log settings out of what Wolfden parses:
    // no signature checks (each runs gpg), no notes, no decorations, no mailmap.
    private static readonly string[] LogOptions = ["--all", "--no-show-signature", "--no-notes", "--no-decorate", "--no-use-mailmap", "--no-abbrev-commit"];

    /// <summary>Audits the history of the den at <paramref name="denRoot"/>.</summary>
    /// <param name="denRoot">The den's folder.</param>
    /// <param name="options">Which git to run, or null for the user's own.</param>
    /// <param name="cancellationToken">Stops git.</param>
    /// <exception cref="HistoryException">A git command failed or didn't finish in time.</exception>
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
        ReadCommits(collector, Git(options, root, ["log", .. LogOptions, "--format=raw"], cancellationToken));
        ReadTrailers(collector, Git(options, root, ["log", .. LogOptions, "--format=%x01%(trailers:only,unfold)"], cancellationToken));
        var (tags, annotated) = ReadTags(collector, Git(
            options,
            root,
            ["for-each-ref", "--format=%01%(objecttype)%02%(taggername)%02%(taggeremail)%02%(taggerdate:raw)%02%(contents:signature)", "refs/tags"],
            cancellationToken));
        var remotes = ReadRemotes(Git(options, root, ["remote", "-v"], cancellationToken));
        var signing = collector.Signing(options, root, cancellationToken);

        var repositoryRoot = location.Root!;
        return new IdentityAudit(
            IdentityAuditState.Audited,
            null,
            repositoryRoot,
            SameFolder(repositoryRoot, root),
            collector.Commits,
            tags,
            annotated,
            collector.Identities(),
            collector.OffsetList(),
            signing,
            remotes);
    }

    private static IdentityAudit Empty(IdentityAuditState state, string? problem) =>
        new(state, problem, null, false, 0, 0, 0, [], [], NoSigning, []);

    private static bool SameFolder(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Runs a read-only git command and returns its whole output, or throws a <see cref="HistoryException"/>.</summary>
    private static string Git(GitOptions options, string folder, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var command = GitProcess.CommandName(args);
        GitRun run;
        try
        {
            run = GitProcess.Run(options, folder, args, cancellationToken: cancellationToken);
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

        return run.Succeeded ? GitHistory.OutputOf(run, command) : throw new HistoryException($"git {command} failed: {run.Said}");
    }

    /// <summary>Reads <c>git log --format=raw</c>: each commit's author, committer, signature, and any merged tag's tagger.</summary>
    internal static void ReadCommits(Collector collector, string output)
    {
        string? commit = null;
        Ident? committer = null;
        var signed = false;
        string? header = null;
        var value = new List<string>();

        foreach (var line in output.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("commit ", StringComparison.Ordinal))
            {
                EndHeader();
                commit = line["commit ".Length..].Trim();
                committer = null;
                signed = false;
                collector.Commits++;
                continue;
            }

            if (commit is null || line.StartsWith("    ", StringComparison.Ordinal))
            {
                // The message, indented by four spaces.
                EndHeader();
                continue;
            }

            if (line.StartsWith(' ') && header is not null)
            {
                // A header's next line.
                value.Add(line[1..]);
                continue;
            }

            EndHeader();
            if (line.Length == 0)
            {
                continue;
            }

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

        EndHeader();

        void EndHeader()
        {
            if (header is null)
            {
                return;
            }

            if (header == "mergetag")
            {
                // A merged signed tag, kept whole in the merge commit.
                var tagger = value.FirstOrDefault(l => l.StartsWith("tagger ", StringComparison.Ordinal));
                var taggerIdent = tagger is null ? null : Ident.Parse(tagger["tagger ".Length..]);
                if (taggerIdent is not null)
                {
                    collector.Add(taggerIdent, Role.Tagger);
                }

                var begin = value.FindIndex(l => l.StartsWith("-----BEGIN ", StringComparison.Ordinal));
                if (begin >= 0)
                {
                    collector.Signed(SignatureKey.Read(string.Join('\n', value.Skip(begin))), taggerIdent, null);
                }
            }
            else
            {
                // gpgsig, or gpgsig-sha256: a commit can carry both, one for each hash, and counts once.
                collector.Signed(SignatureKey.Read(string.Join('\n', value)), committer, commit, countSigned: !signed);
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

    /// <summary>Reads each commit's trailers that name someone (<c>Key: Name &lt;email&gt;</c>), as git finds them.</summary>
    internal static void ReadTrailers(Collector collector, string output)
    {
        foreach (var record in output.Replace("\r", "", StringComparison.Ordinal).Split('\x01', StringSplitOptions.RemoveEmptyEntries))
        {
            var seen = new HashSet<(string, string, string)>();
            foreach (var line in record.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                {
                    continue;
                }

                var key = line[..colon].Trim();
                var match = NameAndEmail().Match(line[(colon + 1)..].Trim());
                if (key.Length == 0 || !match.Success)
                {
                    continue;
                }

                var name = match.Groups["name"].Value.Trim();
                var email = match.Groups["email"].Value;
                if (seen.Add((key.ToUpperInvariant(), name, email)))
                {
                    collector.AddTrailer(name, email, key);
                }
            }
        }
    }

    /// <summary>Reads <c>git for-each-ref refs/tags</c>: each annotated tag's tagger and signature.</summary>
    internal static (int Tags, int Annotated) ReadTags(Collector collector, string output)
    {
        int tags = 0, annotated = 0;
        foreach (var record in output.Replace("\r", "", StringComparison.Ordinal).Split('\x01', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('\x02');
            if (fields.Length < 5)
            {
                continue;
            }

            tags++;
            if (fields[0] != "tag")
            {
                continue;
            }

            annotated++;
            Ident? tagger = null;
            if (fields[1].Length > 0 || fields[2].Length > 0)
            {
                tagger = Ident.Parse($"{fields[1]} {fields[2]} {fields[3]}".Trim());
                collector.Add(tagger, Role.Tagger);
            }

            if (fields[4].Contains("-----BEGIN ", StringComparison.Ordinal))
            {
                collector.Signed(SignatureKey.Read(fields[4]), tagger, null);
                collector.SignedTags++;
            }
        }

        return (tags, annotated);
    }

    /// <summary>Reads <c>git remote -v</c>, one line per address and use.</summary>
    internal static IReadOnlyList<RemoteAddress> ReadRemotes(string output)
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
            return new RemoteAddress(r.Name, url, r.Uses, account);
        }).ToList();
    }

    /// <summary>The address with any password replaced, and the account name in it, if any.</summary>
    internal static (string Url, string? Account) Redact(string url)
    {
        var scheme = UrlUserInfo().Match(url);
        if (scheme.Success)
        {
            var info = scheme.Groups["info"].Value;
            var colon = info.IndexOf(':', StringComparison.Ordinal);
            var user = colon < 0 ? info : info[..colon];
            var redacted = colon < 0 ? url : url[..scheme.Groups["info"].Index] + user + ":***" + url[(scheme.Groups["info"].Index + info.Length)..];
            return (redacted, Account(Uri.UnescapeDataString(user)));
        }

        var scp = ScpUser().Match(url);
        return scp.Success ? (url, Account(scp.Groups["user"].Value)) : (url, null);

        static string? Account(string user) => user.Length == 0 || user == "git" ? null : user;
    }

    [GeneratedRegex(@"^(?<name>.*?)\s*<(?<email>[^<>]*)>$")]
    private static partial Regex NameAndEmail();

    [GeneratedRegex(@"^(?<name>\S+)\t(?<url>.*) \((?<use>fetch|push)\)$")]
    private static partial Regex RemoteLine();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*://(?<info>[^/@]*)@")]
    private static partial Regex UrlUserInfo();

    // scp-like: user@host:path, with no scheme.
    [GeneratedRegex(@"^(?<user>[^@/:]+)@[^/:]+:")]
    private static partial Regex ScpUser();

    internal enum Role
    {
        Author,
        Committer,
        Tagger,
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
        private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal);
        private readonly List<KeyEntry> _keys = [];

        public int Commits { get; set; }

        public int SignedCommits { get; set; }

        public int SignedTags { get; set; }

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

        public void AddTrailer(string name, string email, string key)
        {
            var entry = Get(name, email);
            var existing = entry.Trailers.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) ?? key;
            entry.Trailers[existing] = entry.Trailers.GetValueOrDefault(existing) + 1;
        }

        /// <summary>Records a signature by <paramref name="signer"/>, on <paramref name="commit"/> or (when it's null) a tag.</summary>
        public void Signed(SignatureKey key, Ident? signer, string? commit, bool countSigned = true)
        {
            var entry = _keys.FirstOrDefault(k => k.Kind == key.Kind && k.KeyId == key.KeyId && k.KeyIdIsShort == key.KeyIdIsShort);
            if (entry is null)
            {
                entry = new KeyEntry(key.Kind, key.KeyId, key.KeyIdIsShort);
                _keys.Add(entry);
            }

            if (countSigned)
            {
                if (commit is null)
                {
                    entry.Tags++;
                }
                else
                {
                    entry.Commits++;
                }
            }

            entry.Commit ??= commit;
            if (signer is not null && !entry.SignedAs.Contains(signer.Display))
            {
                entry.SignedAs.Add(signer.Display);
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
                    full.Commits += shortKey.Commits;
                    full.Tags += shortKey.Tags;
                    full.Commit ??= shortKey.Commit;
                    full.SignedAs.AddRange(shortKey.SignedAs.Where(s => !full.SignedAs.Contains(s)));
                    _keys.Remove(shortKey);
                }
            }

            var ordered = _keys.OrderByDescending(k => k.Commits + k.Tags).ThenBy(k => k.KeyId, StringComparer.Ordinal).ToList();
            var lookups = 0;
            var notLookedUp = 0;
            var keys = new List<SigningKeyUse>();
            foreach (var key in ordered)
            {
                string? owner = null;
                if (key.Commit is not null && key.Kind is SignatureKind.OpenPgp or SignatureKind.Ssh or SignatureKind.X509)
                {
                    if (lookups < MaxOwnerLookups)
                    {
                        lookups++;
                        owner = Owner(options, folder, key.Commit, cancellationToken);
                    }
                    else
                    {
                        notLookedUp++;
                    }
                }

                keys.Add(new SigningKeyUse(key.Kind, key.KeyId, key.Commits, key.Tags, key.SignedAs, owner));
            }

            return new SigningSummary(SignedCommits, SignedTags, keys, notLookedUp);
        }

        /// <summary>
        /// Who signed <paramref name="commit"/>, as git's verifier reports it
        /// (<c>%GS</c>), or null. This one check runs gpg or ssh-keygen; a
        /// failure or a timeout only means the owner isn't known.
        /// </summary>
        private static string? Owner(GitOptions options, string folder, string commit, CancellationToken cancellationToken)
        {
            try
            {
                var run = GitProcess.Run(options, folder, ["log", "-1", "--no-walk", "--no-notes", "--format=%GS", commit, "--"], cancellationToken: cancellationToken);
                var owner = run.Succeeded && run.OutputComplete ? run.Output.Trim() : "";
                return owner.Length == 0 ? null : owner;
            }
            catch (GitTimeoutException)
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

            public int Commits { get; set; }

            public int Tags { get; set; }

            public string? Commit { get; set; }

            public List<string> SignedAs { get; } = [];
        }
    }
}

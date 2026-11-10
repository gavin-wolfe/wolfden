using System.Globalization;
using Wolfden.Core.Documents;
using Wolfden.Core.History;
using Wolfden.Core.Yaml;
using Wolfden.Git;

namespace Wolfden.Cli;

/// <summary>
/// <c>wolfden identity &lt;subcommand&gt;</c>: the names a den's history
/// records (#311, #315). For now it has one subcommand, <c>audit</c>
/// (design §3.6 item 2). Showing and choosing the name the next checkpoint
/// records (<c>identity</c> on its own, <c>use</c>, <c>add</c>) come with
/// declared commit identities, and go in <see cref="Subcommands"/> beside it.
/// </summary>
internal static class IdentityCommand
{
    /// <summary>The subcommands, in the order the help lists them.</summary>
    internal static readonly string[] Subcommands = ["audit"];

    internal static readonly CommandSpec Spec = new(
        "identity",
        "usage: wolfden identity <subcommand> [<args>]",
        """
        The names and emails a den's history records.

          audit    list every name, email, signing key, UTC offset, and remote the
                   den's history holds: what any copy or push of it would show

        Run 'wolfden identity <subcommand> --help' for its options.
        """,
        [],
        [],
        [],
        0,
        "give a subcommand.");

    internal static readonly CommandSpec AuditSpec = new(
        "identity audit",
        "usage: wolfden identity audit [--den <den>] [--json]",
        """
        Lists what the den's history would reveal to anyone with a copy of it,
        so you can check before you push it anywhere or share it:

        - every name and email it records, as the author or committer of a
          checkpoint, as the tagger of a tag, or in a commit message line such
          as Co-Authored-By, with how often, the UTC offsets of their dates, and
          when each was first and last recorded
        - other lines of commit messages that may name someone: a
          'Signed-off-by:' without an email, or any line holding an email
        - every UTC offset the dates carry, since the same offset on two names
          hints that they're one person
        - how many checkpoints and tags are signed, by which keys, under which
          names, and who each key belongs to as this computer's gpg or ssh
          setup reports it
        - the authors in den.yaml, which travels with every copy too
        - only in this folder: the remotes and any account name in their
          addresses (a password or token is never printed), names that only
          the reflogs keep (an amended checkpoint's, say), and the name settings
          in this copy's own git config

        It reads every branch, tag, and other ref of the repository the den is
        in, the whole repository when the den is part of a larger one, since a
        push or a clone carries all of it. A copy of the folder itself (a zip,
        a synced folder, a disk backup) carries the last part too. Names are
        shown as git recorded them: a .mailmap or a replace ref doesn't hide
        one. It changes nothing.

          --den <den>   the den's folder; by default, the current folder or the
                        nearest folder above it with a den.yaml
          --json        print one JSON document instead of text

        What this prints can include a name you keep apart from this den, so
        don't paste it anywhere public. Exits 0 when the audit is done (a den
        with no history included), and 2 if the command line is wrong, there's
        no den, or git can't read the history.
        """,
        ["--json"],
        ["--den"],
        [],
        0,
        "it takes no arguments; name the den with '--den'.");

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        switch (args.ToArray())
        {
            case ["audit", .. var rest]:
                return Audit(rest, output, error);
            case []:
                return Fail(error);
            case [var first, ..] when !first.StartsWith('-'):
                error.WriteLine($"wolfden identity: unknown subcommand '{first}'; the subcommands are: {string.Join(", ", Subcommands)}.");
                error.WriteLine(Spec.Usage);
                return CliApp.ExitUsage;
            default:
                // Options before a subcommand: only --help means anything.
                return CommandLine.Parse(Spec, args, output, error, out _) ?? Fail(error);
        }

        static int Fail(TextWriter error)
        {
            error.WriteLine($"wolfden identity: give a subcommand: {string.Join(", ", Subcommands)}.");
            error.WriteLine(Spec.Usage);
            return CliApp.ExitUsage;
        }
    }

    private static int Audit(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (CommandLine.Parse(AuditSpec, args, output, error, out var line) is { } exit)
        {
            return exit;
        }

        if (DenLocation.Resolve(AuditSpec.Name, line.Value("--den"), error) is not { } root)
        {
            return CliApp.ExitUsage;
        }

        IdentityAudit audit;
        try
        {
            audit = IdentityAudit.Run(root, CliApp.Git);
        }
        catch (HistoryException e)
        {
            error.WriteLine($"wolfden {AuditSpec.Name}: couldn't read the den's history: {e.Message}");
            return CliApp.ExitUsage;
        }

        if (audit.State == IdentityAuditState.Unavailable)
        {
            error.WriteLine($"wolfden {AuditSpec.Name}: {audit.Problem}.");
            return CliApp.ExitUsage;
        }

        var authors = ReadAuthors(root, out var authorsProblem);
        if (line.Has("--json"))
        {
            CliJson.Write(output, AuditReport.From(audit, authors, authorsProblem));
        }
        else
        {
            WriteText(output, audit, authors, authorsProblem);
        }

        return CliApp.ExitOk;
    }

    /// <summary>
    /// The authors in <c>den.yaml</c> (F-DEN-3), each with every plain value
    /// its identity sets (its <c>name</c>, and anything else, such as an
    /// email), whether or not the den would load. Null, with
    /// <paramref name="problem"/> set, if the file can't be read or parsed.
    /// </summary>
    internal static IReadOnlyList<DenAuthor>? ReadAuthors(string root, out string? problem)
    {
        problem = null;
        YamlFile file;
        try
        {
            file = YamlFile.Parse(File.ReadAllBytes(Path.Combine(root, DenLocation.DenFile)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"den.yaml couldn't be read ({e.Message})";
            return null;
        }

        if (file.HasErrors || file.Root is null)
        {
            problem = "den.yaml doesn't parse, so its authors aren't listed; run 'wolfden check'";
            return null;
        }

        if (file.GetValue("authors") is not YamlMapping map)
        {
            return [];
        }

        return map.Entries.Select(entry => new DenAuthor(
            entry.KeyText ?? "",
            entry.Value is YamlMapping identity
                ? identity.Entries
                    .Where(e => e.KeyText is not null && e.Value is YamlScalar { IsNull: false })
                    .Select(e => new DenAuthorField(e.KeyText!, ((YamlScalar)e.Value).Text))
                    .ToList()
                : entry.Value is YamlScalar { IsNull: false } scalar ? [new DenAuthorField("", scalar.Text)] : []))
            .ToList();
    }


    internal static void WriteText(TextWriter output, IdentityAudit audit, IReadOnlyList<DenAuthor>? authors, string? authorsProblem)
    {
        if (audit.State == IdentityAuditState.NoHistory)
        {
            output.WriteLine("This den has no history: its folder isn't in a git repository, so no checkpoint records a name.");
            output.WriteLine();
            WriteAuthors(output, authors, authorsProblem);
            output.WriteLine();
            output.WriteLine("den.yaml travels with any copy of this den.");
            return;
        }

        var tagParts = new List<string> { Count(audit.Tags, "tag") };
        if (audit.AnnotatedTags > 0)
        {
            tagParts.Add($"{Count(audit.AnnotatedTags, "annotated tag object")} under any ref");
        }

        if (audit.MergedTags > 0)
        {
            tagParts.Add($"{Count(audit.MergedTags, "tag")} merged into checkpoints");
        }

        output.WriteLine($"The den's history: {Count(audit.Commits, "checkpoint")}, {string.Join(", ", tagParts)}, on every branch, tag, and other ref.");
        if (!audit.DenIsRepositoryRoot)
        {
            output.WriteLine(
                $"The den is part of a larger repository, at {audit.RepositoryRoot}. A push or a clone carries that " +
                "repository's whole history, so this covers all of it, not only the den's checkpoints.");
        }

        if (audit.Shallow)
        {
            output.WriteLine(
                "This copy is shallow: it holds only part of the history, so the full history (where it was cloned from) may record more names.");
        }

        output.WriteLine();
        output.WriteLine("Names and emails it records:");
        if (audit.Identities.Count == 0)
        {
            output.WriteLine("  none");
        }

        foreach (var identity in audit.Identities)
        {
            output.WriteLine($"  {identity.Name} <{identity.Email}>");
            var uses = new List<string>();
            if (identity.AsAuthor > 0)
            {
                uses.Add($"author of {Count(identity.AsAuthor, "checkpoint")}");
            }

            if (identity.AsCommitter > 0)
            {
                uses.Add($"committer of {Count(identity.AsCommitter, "checkpoint")}");
            }

            if (identity.AsTagger > 0)
            {
                uses.Add($"tagger of {Count(identity.AsTagger, "tag")}");
            }

            uses.AddRange(identity.InTrailers.Select(t => $"in {t.Key} of {Count(t.Commits, "checkpoint")}"));
            output.WriteLine($"    {string.Join(", ", uses)}");
            if (identity.First is { } first && identity.Last is { } last)
            {
                var span = Day(first) == Day(last) ? $"on {Day(first)}" : $"from {Day(first)} to {Day(last)}";
                output.WriteLine($"    {span}, UTC offset{(identity.Offsets.Count == 1 ? "" : "s")} {string.Join(", ", identity.Offsets)}");
            }
        }

        if (audit.Mentions.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Elsewhere in checkpoint messages, lines that may name someone:");
            foreach (var mention in audit.Mentions)
            {
                var text = mention.Key is null ? mention.Text : $"{mention.Key}: {mention.Text}";
                output.WriteLine($"  {text}  ({Count(mention.Commits, "checkpoint")})");
            }
        }

        output.WriteLine();
        output.WriteLine(audit.Offsets.Count == 0
            ? "UTC offsets: none"
            : $"UTC offsets: {string.Join(", ", audit.Offsets.Select(o => $"{o.Offset} ({Count(o.Dates, "date")})"))}");

        output.WriteLine();
        WriteSigning(output, audit);

        output.WriteLine();
        WriteAuthors(output, authors, authorsProblem);

        output.WriteLine();
        WriteLocal(output, audit.Local);

        output.WriteLine();
        output.WriteLine("A push or a clone carries the history and den.yaml above. A copy of the folder itself");
        output.WriteLine("(a zip, a synced folder, a disk backup) carries all of it, the last part included.");
    }

    private static void WriteSigning(TextWriter output, IdentityAudit audit)
    {
        var signing = audit.Signing;
        if (signing.SignedCommits == 0 && signing.SignedTags == 0 && signing.SignedMergedTags == 0)
        {
            output.WriteLine("Signatures: none of its checkpoints or tags are signed.");
            return;
        }

        var parts = new List<string> { $"{signing.SignedCommits} of {Count(audit.Commits, "checkpoint")}" };
        if (audit.AnnotatedTags > 0)
        {
            parts.Add($"{signing.SignedTags} of {Count(audit.AnnotatedTags, "annotated tag")}");
        }

        if (audit.MergedTags > 0)
        {
            parts.Add($"{signing.SignedMergedTags} of {Count(audit.MergedTags, "merged tag")}");
        }

        var keys = signing.Keys.Count;
        output.WriteLine(
            $"Signatures: {string.Join(", ", parts)} signed" +
            (keys > 0 ? $", with {Count(keys, "key")}. A key ties together every name it signs under, for good." : "."));
        foreach (var key in signing.Keys)
        {
            output.WriteLine($"  {Kind(key.Kind)} {key.KeyId}: {What(key.Commits, key.Tags, key.MergedTags)}");
            if (key.SignedAs.Count > 0)
            {
                output.WriteLine($"    signed as: {string.Join("; ", key.SignedAs)}");
            }

            if (!key.OwnerChecked)
            {
                output.WriteLine("    owner not checked");
            }
            else if (key.Owner is null)
            {
                output.WriteLine("    owner: this computer can't say whose key it is");
            }
            else
            {
                output.WriteLine($"    belongs to: {key.Owner} (as this computer reports it)");
                if (!key.SignedAs.Contains(key.Owner, StringComparer.Ordinal) && key.SignedAs.Count > 0)
                {
                    output.WriteLine("    That isn't a name it signed as, so anyone who checks the signature can link the two.");
                }
            }
        }

        foreach (var group in signing.Unidentified)
        {
            var total = group.Commits + group.Tags + group.MergedTags;
            var signer = group.SignedAs.Length == 0 ? "" : $", signed as {group.SignedAs}";
            output.WriteLine(
                $"  {Count(total, "signature")} whose key isn't recorded ({Kind(group.Kind)}){signer}: {What(group.Commits, group.Tags, group.MergedTags)}");
        }

        if (signing.OwnersNotLookedUp > 0)
        {
            output.WriteLine($"  Owners weren't looked up for {Count(signing.OwnersNotLookedUp, "more key")}.");
        }

        static string Kind(SignatureKind kind) => kind switch
        {
            SignatureKind.OpenPgp => "OpenPGP key",
            SignatureKind.Ssh => "SSH key",
            SignatureKind.X509 => "X.509 certificate",
            _ => "unrecognized signature",
        };

        static string What(int commits, int tags, int merged)
        {
            var what = new List<string>();
            if (commits > 0)
            {
                what.Add(Count(commits, "checkpoint"));
            }

            if (tags > 0)
            {
                what.Add(Count(tags, "tag"));
            }

            if (merged > 0)
            {
                what.Add(Count(merged, "merged tag"));
            }

            return string.Join(", ", what);
        }
    }

    private static void WriteLocal(TextWriter output, LocalOnly local)
    {
        output.WriteLine("Only in this folder (not pushed or cloned):");
        output.WriteLine("  Remotes:");
        if (local.Remotes.Count == 0)
        {
            output.WriteLine("    none");
        }

        foreach (var remote in local.Remotes)
        {
            output.WriteLine($"    {remote.Name}  {remote.Url}  ({string.Join(", ", remote.Uses)})");
            if (remote.Account is not null)
            {
                output.WriteLine($"      account name in the address: {remote.Account}");
            }
        }

        output.WriteLine("  Names only the reflogs keep (who moved a branch, or a checkpoint since amended or reset away):");
        if (local.Names.Count == 0)
        {
            output.WriteLine("    none");
        }

        foreach (var name in local.Names)
        {
            var what = new List<string>();
            if (name.ReflogEntries > 0)
            {
                what.Add(Count(name.ReflogEntries, "reflog entry", "reflog entries"));
            }

            if (name.UnreachableCommits > 0)
            {
                what.Add(Count(name.UnreachableCommits, "checkpoint only a reflog keeps", "checkpoints only a reflog keeps"));
            }

            output.WriteLine($"    {name.Name} <{name.Email}>: {string.Join(", ", what)}");
        }

        output.WriteLine("  Name settings in this copy's git config:");
        if (local.Settings.Count == 0)
        {
            output.WriteLine("    none");
        }

        foreach (var setting in local.Settings)
        {
            output.WriteLine($"    {setting.Key} = {setting.Value}");
        }

        if (local.Problem is not null)
        {
            output.WriteLine($"  Not all of it could be read: {local.Problem}.");
        }

        output.WriteLine("  Objects nothing points to any more (git collects them in time) aren't read.");
    }

    private static void WriteAuthors(TextWriter output, IReadOnlyList<DenAuthor>? authors, string? problem)
    {
        output.WriteLine("Authors in den.yaml:");
        if (authors is null)
        {
            output.WriteLine($"  {problem}.");
            return;
        }

        if (authors.Count == 0)
        {
            output.WriteLine("  none");
        }

        foreach (var author in authors)
        {
            var fields = author.Fields.Select(f => f.Key.Length == 0 ? f.Value : $"{f.Key}: {f.Value}");
            output.WriteLine($"  {author.Key}: {string.Join(", ", fields)}");
        }
    }

    private static string Day(DateTimeOffset date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Count(int count, string noun, string? plural = null) =>
        count == 1 ? $"1 {noun}" : $"{count.ToString(CultureInfo.InvariantCulture)} {plural ?? noun + "s"}";

    /// <summary>An author in <c>den.yaml</c>, with the plain values its identity sets.</summary>
    internal sealed record DenAuthor(string Key, IReadOnlyList<DenAuthorField> Fields);

    /// <summary>One plain value in an author's identity, such as its <c>name</c>.</summary>
    internal sealed record DenAuthorField(string Key, string Value);

    /// <summary>
    /// The JSON shape of <c>wolfden identity audit --json</c>. <see cref="History"/>
    /// is <c>audited</c> or <c>none</c> (no repository, and every list empty).
    /// Paths and addresses show the home folder as <c>~</c>.
    /// </summary>
    /// <param name="Version">The shape's version, 1.</param>
    /// <param name="History"><c>audited</c>, or <c>none</c> when the den isn't in a git repository.</param>
    /// <param name="RepositoryRoot">The repository's top folder, or null.</param>
    /// <param name="DenIsRepositoryRoot">False when the den is part of a larger repository, whose whole history is listed.</param>
    /// <param name="Shallow">True for a shallow clone, which holds only part of the history.</param>
    /// <param name="Commits">How many commits (checkpoints) the repository holds, on every ref.</param>
    /// <param name="Tags">How many tags are under <c>refs/tags</c>.</param>
    /// <param name="AnnotatedTags">How many annotated tag objects any ref reaches.</param>
    /// <param name="MergedTags">How many tags are embedded in merge commits.</param>
    /// <param name="Identities">Every name and email recorded.</param>
    /// <param name="Mentions">Other lines of commit messages that may name someone.</param>
    /// <param name="Offsets">Every UTC offset recorded.</param>
    /// <param name="Signing">The signatures and keys.</param>
    /// <param name="Authors">The authors in <c>den.yaml</c>, or null when it can't be read.</param>
    /// <param name="AuthorsProblem">Why <paramref name="Authors"/> is null.</param>
    /// <param name="Local">Only in this folder: remotes, names only the reflogs keep, and this copy's name settings.</param>
    internal sealed record AuditReport(
        int Version,
        string History,
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
        SigningReport Signing,
        IReadOnlyList<DenAuthor>? Authors,
        string? AuthorsProblem,
        LocalOnly Local)
    {
        public static AuditReport From(IdentityAudit audit, IReadOnlyList<DenAuthor>? authors, string? authorsProblem) => new(
            1,
            audit.State == IdentityAuditState.Audited ? "audited" : "none",
            audit.RepositoryRoot,
            audit.DenIsRepositoryRoot,
            audit.Shallow,
            audit.Commits,
            audit.Tags,
            audit.AnnotatedTags,
            audit.MergedTags,
            audit.Identities,
            audit.Mentions,
            audit.Offsets,
            new SigningReport(
                audit.Signing.SignedCommits,
                audit.Signing.SignedTags,
                audit.Signing.SignedMergedTags,
                audit.Signing.Keys.Select(k => new KeyReport(KindName(k.Kind), k.KeyId, k.Commits, k.Tags, k.MergedTags, k.SignedAs, k.Owner, k.OwnerChecked)).ToList(),
                audit.Signing.Unidentified.Select(u => new UnidentifiedReport(KindName(u.Kind), u.SignedAs, u.Commits, u.Tags, u.MergedTags)).ToList(),
                audit.Signing.OwnersNotLookedUp),
            authors,
            authorsProblem,
            audit.Local);

        private static string KindName(SignatureKind kind) =>
            kind switch { SignatureKind.OpenPgp => "openpgp", SignatureKind.Ssh => "ssh", SignatureKind.X509 => "x509", _ => "unknown" };
    }

    /// <summary>The signatures: how many commits, annotated tags, and merged tags are signed, and by which keys.</summary>
    internal sealed record SigningReport(
        int SignedCommits, int SignedTags, int SignedMergedTags, IReadOnlyList<KeyReport> Keys, IReadOnlyList<UnidentifiedReport> Unidentified, int OwnersNotLookedUp);

    /// <summary>One signing key: <c>openpgp</c> or <c>ssh</c>, its fingerprint, what it signed, and its owner as this computer reports it, if that was checked.</summary>
    internal sealed record KeyReport(string Kind, string? KeyId, int Commits, int Tags, int MergedTags, IReadOnlyList<string> SignedAs, string? Owner, bool OwnerChecked);

    /// <summary>Signatures that don't record their key (<c>x509</c> or <c>unknown</c>), by one signer.</summary>
    internal sealed record UnidentifiedReport(string Kind, string SignedAs, int Commits, int Tags, int MergedTags);
}

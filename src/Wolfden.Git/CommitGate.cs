using System.Collections.Frozen;
using System.Globalization;
using Wolfden.Core.History;

namespace Wolfden.Git;

/// <summary>What a git write that passes the gate makes.</summary>
internal enum GatedWrite
{
    /// <summary>A commit: a checkpoint.</summary>
    Commit,

    /// <summary>An annotated tag, which records a tagger and may be signed.</summary>
    AnnotatedTag,

    /// <summary>A lightweight tag, which records no one and is never signed.</summary>
    LightweightTag,
}

/// <summary>
/// The one check before every commit and tag Wolfden makes (#311, #315
/// design §3.4, steps 3 and 4). It works out the name and email the write
/// records, from git's settings only, and hands them to that one git
/// process in its environment, which beats every other source, an inherited
/// <c>GIT_AUTHOR_NAME</c> included. And it pins signing with <c>-c</c> to
/// what the den's own <c>.git/config</c> says, which beats an
/// <c>[include]</c>, a <c>config.worktree</c> and <c>GIT_CONFIG_COUNT</c>,
/// with a warning, never a refusal, when git's own answer differs (D4).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GitProcess.Run"/> refuses to run a command that makes commits
/// or tags (<see cref="IsGated"/>) without a <see cref="CommitClearance"/>,
/// and only <see cref="Clear"/> makes one, so a new write path can't skip
/// the gate without failing its first test.
/// </para>
/// <para>
/// <b>The name and email</b> are what <c>git var GIT_AUTHOR_IDENT</c> says
/// with <c>user.useConfigOnly</c> on and the identity variables taken out of
/// its environment (<c>GIT_AUTHOR_*</c>, <c>GIT_COMMITTER_*</c> and
/// <c>EMAIL</c>): git's settings, as they apply to the den's repository
/// (its <c>author.*</c> before <c>user.*</c>, and an <c>includeIf</c>), and
/// never a name git guesses from the computer's account. Git has none: the
/// write is refused, and nothing is changed. The committer is the author.
/// </para>
/// <para>
/// <b>Signing</b>: <c>commit.gpgsign</c>, <c>tag.gpgsign</c> and
/// <c>tag.forceSignAnnotated</c>, each one the den's own file sets, read
/// with <c>git config --local</c>, which doesn't follow includes, is passed
/// again with <c>-c</c>. One it doesn't set is left to git, as before. The
/// pin can only keep signing to what the den's file says: it doesn't choose
/// a key when that turns signing on.
/// </para>
/// </remarks>
internal static class CommitGate
{
    /// <summary>The signing settings pinned, as git lists them (lower case).</summary>
    internal static readonly string[] SigningKeys = ["commit.gpgsign", "tag.gpgsign", "tag.forcesignannotated"];

    /// <summary>
    /// Git commands that make commits or tags, which only run with a
    /// <see cref="CommitClearance"/>. Wolfden runs only <c>commit</c> and
    /// <c>tag</c> today; the rest are here so one added later can't skip the gate.
    /// </summary>
    internal static readonly FrozenSet<string> GatedCommands = new[]
    {
        "commit", "commit-tree", "tag", "mktag", "merge", "pull", "cherry-pick", "revert", "rebase", "am", "notes", "stash", "replace",
        "fast-import", "filter-branch",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The variables a gated git gets, which decide what it records.</summary>
    internal static readonly string[] IdentityVariables = ["GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL"];

    /// <summary>True if the git command in <paramref name="args"/> makes commits or tags, so it must pass the gate.</summary>
    internal static bool IsGated(IReadOnlyList<string> args) => GatedCommands.Contains(GitProcess.CommandName(args));

    /// <summary>
    /// Works out what a <paramref name="write"/> in the den's repository
    /// records and how it's signed.
    /// </summary>
    /// <param name="write">What the write makes.</param>
    /// <param name="run">Runs a git command that only reads, in the den's folder, with more environment for it; it throws a <see cref="HistoryException"/> if git can't be run.</param>
    /// <exception cref="HistoryException">Git has no name and email in its settings for the den, or can't read its settings.</exception>
    public static CommitClearance Clear(GatedWrite write, Func<IReadOnlyList<string>, IReadOnlyDictionary<string, string?>?, GitRun> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var (name, email) = Identity(run);
        // GIT_CONFIG would make 'git config' read only that file; a commit never reads it.
        var noConfigFile = new Dictionary<string, string?> { ["GIT_CONFIG"] = null };
        var local = Settings(run(["config", "--local", "--null", "--list"], noConfigFile), "the den's own settings");
        var effective = Settings(run(["config", "--null", "--list"], noConfigFile), "its settings");

        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in SigningKeys)
        {
            if (local.TryGetValue(key, out var value))
            {
                // A key with no value means true, and -c needs one.
                pins[key] = value ?? "true";
            }
        }

        var warnings = new List<string>();
        if (write != GatedWrite.LightweightTag)
        {
            string[] deciding = write == GatedWrite.Commit ? ["commit.gpgsign"] : ["tag.gpgsign", "tag.forcesignannotated"];
            var signs = deciding.Any(key => On(pins.TryGetValue(key, out var pinned) ? (true, pinned) : Find(effective, key)));
            var gitWouldSign = deciding.Any(key => On(Find(effective, key)));
            if (signs != gitWouldSign)
            {
                warnings.Add(SigningWarning(write, signs));
            }
        }

        return new CommitClearance(name, email, pins, effective, warnings);
    }

    /// <summary>
    /// The warning when git's own answer about signing differs from the den's
    /// file. It doesn't say which setting it was or what it holds.
    /// </summary>
    private static string SigningWarning(GatedWrite write, bool signed)
    {
        var what = write == GatedWrite.Commit ? "checkpoint" : "tag";
        const string Where = "Something git reads after the den's own .git/config disagrees with it: an [include] or a config.worktree in the den's repository, " +
            "or GIT_CONFIG_COUNT or GIT_CONFIG_PARAMETERS in the environment. Wolfden follows the den's own settings for its checkpoints and tags (#311).";
        return signed
            ? $"This den's own .git/config says to sign, so Wolfden signed this {what}, but git's settings as a whole wouldn't have. {Where}"
            : $"Git's settings would have signed this {what}, but this den's own .git/config says not to, so Wolfden didn't sign it. {Where}";
    }

    /// <summary>The name and email git's settings give for the den's repository.</summary>
    private static (string Name, string Email) Identity(Func<IReadOnlyList<string>, IReadOnlyDictionary<string, string?>?, GitRun> run)
    {
        var environment = IdentityVariables.Append("EMAIL").ToDictionary(v => v, _ => (string?)null, StringComparer.Ordinal);
        var said = run(["-c", "user.useConfigOnly=true", "var", "GIT_AUTHOR_IDENT"], environment);

        // "Name <email> 1793462796 -0700": git takes '<' and '>' out of both.
        var text = said.OutputComplete ? said.Output.Split('\n')[0] : "";
        var open = text.IndexOf('<', StringComparison.Ordinal);
        var close = open < 0 ? -1 : text.IndexOf('>', open);
        if (!said.Succeeded || close < 0)
        {
            // Not what git said: with no settings, it can name the computer's account.
            throw new HistoryException(
                "Git has no name and email in its settings for this den, so nothing was recorded. Wolfden records checkpoints and tags only under a name and email " +
                "from git's settings, never one git guesses from this computer's account or one from the environment. In the den's folder, run " +
                "'git config --local user.name \"<name>\"' and 'git config --local user.email <email>' (a pen name and a placeholder email are fine), then try again. " +
                "A GIT_AUTHOR_DATE in the environment that git can't read stops it too.");
        }

        return (text[..open].TrimEnd(' '), text[(open + 1)..close]);
    }

    /// <summary>
    /// The settings in a <c>git config --null --list</c>, each key's last
    /// value (the one git uses), null for a key with no value.
    /// </summary>
    private static Dictionary<string, string?> Settings(GitRun listed, string what)
    {
        if (!listed.Succeeded || !listed.OutputComplete)
        {
            throw new HistoryException($"Git couldn't read {what}, so nothing was recorded: {listed.Said}");
        }

        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in listed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('\n', 2);
            settings[parts[0]] = parts.Length == 2 ? parts[1] : null;
        }

        return settings;
    }

    private static (bool Set, string? Value) Find(Dictionary<string, string?> settings, string key) =>
        settings.TryGetValue(key, out var value) ? (true, value) : (false, null);

    /// <summary>True if a setting that's set means on, as git reads a boolean; one git can't read counts as on.</summary>
    internal static bool On((bool Set, string? Value) setting) => setting.Set && (Bool(setting.Value) ?? true);

    /// <summary>A boolean setting as git reads it, or null if git can't: no value is true, and an empty one false.</summary>
    internal static bool? Bool(string? value)
    {
        if (value is null)
        {
            return true;
        }

        var v = value.Trim();
        if (v.Length == 0)
        {
            return false;
        }

        if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("no", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n != 0 : null;
    }
}

/// <summary>
/// What <see cref="CommitGate.Clear"/> decided for one write: the name and
/// email it records, and the signing settings pinned. Only the gate makes
/// one, and <see cref="GitProcess.Run"/> runs a commit or tag only with one.
/// </summary>
internal sealed class CommitClearance
{
    private readonly IReadOnlyDictionary<string, string> _pins;
    private readonly Dictionary<string, string?> _effective;

    internal CommitClearance(string name, string email, IReadOnlyDictionary<string, string> pins, Dictionary<string, string?> effective, IReadOnlyList<string>? warnings = null)
    {
        Name = name;
        Email = email;
        _pins = pins;
        _effective = effective;
        Warnings = warnings ?? [];
    }

    /// <summary>The name the write records, as author and committer (or tagger).</summary>
    public string Name { get; }

    /// <summary>The email the write records.</summary>
    public string Email { get; }

    /// <summary>What the writer should know: that git's own settings would have signed it differently.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>The variables for git's environment: the name and email as author and committer, over anything inherited.</summary>
    public IReadOnlyDictionary<string, string?> Environment => new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["GIT_AUTHOR_NAME"] = Name,
        ["GIT_AUTHOR_EMAIL"] = Email,
        ["GIT_COMMITTER_NAME"] = Name,
        ["GIT_COMMITTER_EMAIL"] = Email,
    };

    /// <summary>The <c>-c</c> options that pin signing to the den's own file, to go before the command.</summary>
    public IReadOnlyList<string> Settings => _pins.SelectMany(pin => new[] { "-c", $"{pin.Key}={pin.Value}" }).ToList();

    /// <summary>True if the write would sign by any of <paramref name="keys"/>, pinned or not. One git can't read counts as on.</summary>
    public bool WouldSign(params string[] keys) => keys.Any(key =>
        _pins.TryGetValue(key.ToLowerInvariant(), out var pinned)
            ? CommitGate.On((true, pinned))
            : CommitGate.On(_effective.TryGetValue(key, out var value) ? (true, value) : (false, null)));
}

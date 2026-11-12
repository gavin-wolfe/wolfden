using System.Text;
using Wolfden.Core.History;

namespace Wolfden.Git;

/// <summary>
/// Turns git history on for a den that has none (#195): <c>git init</c> in
/// the den's folder, <c>.wolfden/</c> in its <c>.gitignore</c>, a name and
/// email saved in the repository's own config if they're given or git has
/// none, and a first checkpoint. See <see cref="IHistorySetup"/> for the rules.
/// </summary>
/// <remarks>
/// <para>
/// <b>The name and email</b> are what git itself would use for the den's
/// repository: <c>git config user.name</c> and <c>user.email</c> run in the
/// den's folder, so the user's global and system settings count. They're
/// asked for only when one is missing. One given always wins for this den
/// (#286): it's saved with <c>git config --local</c>, in that repository
/// only, never globally, before the first checkpoint, even over what git
/// already has, and over an <c>author.name</c> or <c>committer.email</c> of
/// the kind that would otherwise beat it. They're checked once the
/// repository exists, right after <c>git init</c>, since a setting can
/// depend on where the repository is (git's <c>includeIf "gitdir:…"</c>),
/// which git can't apply before. A missing one that wasn't given, or a pair
/// git won't record (<c>git var GIT_COMMITTER_IDENT</c>, which also says
/// what git makes of them: a given email it would record as empty, such as
/// <c>;</c>, is refused as a name git strips to nothing is), refuses: the repository
/// this run just made, with no commit yet, is taken away again, so nothing
/// is changed. So does one given without the other, always (maintainer
/// decision, #286): the two go together, so a pen name is never recorded
/// beside an email git has, even one that's what git has already. Also any
/// given while <c>GIT_DIR</c> or <c>GIT_COMMON_DIR</c> points git at
/// another repository. A <c>GIT_AUTHOR_NAME</c>-style environment variable
/// doesn't refuse: Wolfden's commits don't use it (<see cref="CommitGate"/>),
/// and the steps say it's set, since git commands run by hand still do.
/// </para>
/// <para>
/// <b>Saved first, and checked.</b> What's given is saved before anything
/// else is done, so nothing that fails later can leave a retry under git's
/// own name. A save that fails in a repository this run made takes the
/// repository away again; in one that was there, what was saved is taken back
/// if it can be, and the result names what couldn't be. Once saved, git is asked what it would record
/// (<c>git var</c>) and whether it would sign, and a setting that still
/// beats what was saved (<c>extensions.worktreeConfig</c>, a later
/// <c>[include]</c>, <c>GIT_CONFIG_COUNT</c>) refuses before any commit.
/// </para>
/// <para>
/// <b>Signing.</b> When a name and email are given, <see cref="SigningSettings"/>
/// are saved as off in the den's repository, always (maintainer decision,
/// #286), and stay off if signing is turned on globally later; the steps say
/// so if signing was on. The key git would use can have nothing to do with the
/// name (<c>user.signingkey</c>, <c>gpg.ssh.defaultKeyCommand</c>,
/// <c>local-user</c> in <c>gpg.conf</c>), and Wolfden can't see them all.
/// </para>
/// <para>
/// <b>Its own repository</b> is one whose top level, as git says
/// (<c>git rev-parse --show-prefix</c> is empty), is the den's folder; a
/// <c>.git</c> there that git doesn't take for a repository, inside a larger
/// one, leaves the den in the larger one, and that's refused. So is a linked
/// worktree of another repository (<c>git worktree add --orphan</c>):
/// <c>--git-dir</c> and <c>--git-common-dir</c> differ, and its config is
/// the other repository's, shared.
/// </para>
/// <para>
/// <b>Taking a new repository away</b> is one rename first: <c>.git</c> is
/// moved to a hidden folder in the den, then deleted. If the rename fails,
/// nothing is removed and the result says the den is an empty repository;
/// if the delete fails, the den has no repository and the result names the
/// folder left behind.
/// </para>
/// <para>
/// <b>.gitignore</b> is created if there's none, and otherwise
/// <c>.wolfden/</c> is added at its end unless git says the file already
/// leaves it out (<c>git check-ignore</c>); the rest of the file is never
/// changed.
/// </para>
/// </remarks>
public sealed class GitHistorySetup : IHistorySetup
{
    /// <summary>The line the den's <c>.gitignore</c> needs.</summary>
    public const string IgnoreLine = ".wolfden/";

    private static readonly UTF8Encoding Utf8 = new(false);

    private static readonly string[] IgnoreSpellings = [".wolfden", ".wolfden/", "/.wolfden", "/.wolfden/", ".wolfden/*", "/.wolfden/*", ".wolfden/**", "/.wolfden/**"];

    /// <summary>What's saved as off in the den's repository when a name and email are given.</summary>
    private static readonly string[] SigningSettings = ["commit.gpgsign", "tag.gpgsign", "tag.forcesignannotated", "push.gpgsign"];

    /// <summary>The settings that make a commit or tag signed, so that signing is on if any is.</summary>
    private static readonly string[] SignsCheckpoints = ["commit.gpgsign", "tag.gpgsign", "tag.forcesignannotated"];

    private readonly string _denRoot;
    private readonly GitOptions _options;

    /// <summary>For tests: called with <c>init</c>, or the setting's key, just before git is asked to make the repository or save it.</summary>
    internal Action<string>? BeforeStep { get; init; }

    /// <summary>Turning history on for the den at <paramref name="denRoot"/>, with <paramref name="options"/>' git.</summary>
    public GitHistorySetup(string denRoot, GitOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(denRoot);
        _denRoot = Path.GetFullPath(denRoot);
        _options = options ?? GitOptions.Default;
    }

    /// <inheritdoc />
    public HistorySetupCheck Check(CancellationToken cancellationToken = default)
    {
        var location = GitRepository.Locate(_denRoot, _options, cancellationToken);
        var finishing = false;
        switch (location.Kind)
        {
            case GitLocationKind.GitNotInstalled:
                return Refuse(
                    HistorySetupCodes.GitNotInstalled,
                    "Git isn't installed, or Wolfden can't find it on the PATH, so history can't be turned on. Install git, then try again.");
            case GitLocationKind.Unusable:
                return Refuse(HistorySetupCodes.HistoryFailed, $"Git can't use the den's folder ({Sentence(location.Problem)} So history can't be turned on: fix what git says, then try again.");
            case GitLocationKind.InRepository:
                bool own;
                string? sharedWith;
                bool hasCommits;
                try
                {
                    // The den's own repository only if git's top level is the den's folder, as git
                    // itself says: a .git there that git doesn't take for one doesn't count (#205 review).
                    own = IsTopLevel(cancellationToken);

                    // And only if it's a repository of its own, not a linked worktree of another
                    // (git worktree add --orphan): its config is that repository's, shared (#205 review).
                    sharedWith = own ? SharedRepository(cancellationToken) : null;
                    hasCommits = own && sharedWith is null && HasCommits(cancellationToken);
                }
                catch (HistoryException e)
                {
                    return Refuse(HistorySetupCodes.HistoryFailed, e.Message);
                }

                if (!own)
                {
                    return Refuse(
                        HistorySetupCodes.InRepository,
                        $"The den is inside the git repository at '{location.Root}', which keeps its history already, for the den's files only. There's nothing to turn on.");
                }

                if (sharedWith is not null)
                {
                    return Refuse(
                        HistorySetupCodes.InRepository,
                        $"The den's folder is a linked worktree of the git repository at '{sharedWith}', which keeps history, and settings, for more than this den. " +
                        "Wolfden won't turn on history or save a name and email there. Nothing was changed.");
                }

                if (hasCommits)
                {
                    return Refuse(
                        HistorySetupCodes.HasRepository,
                        "The den's folder is already a git repository with checkpoints, so its history is on. Nothing was changed.");
                }

                finishing = true;
                break;
            case GitLocationKind.NotARepository when HasOwnGit():
                return Refuse(
                    HistorySetupCodes.HistoryFailed,
                    "The den's folder has a .git that git doesn't take for a repository, so Wolfden won't make another. Move it out of the way, then try again.");
        }

        try
        {
            var (name, email) = Identity(cancellationToken);

            // Outside a repository, git can't apply a setting that depends on where the
            // repository is (includeIf "gitdir:…"), so its answer isn't final.
            // The two go together: if either is missing, both are asked for (#286).
            var needed = name is null || email is null;
            return new HistorySetupCheck(true, null, null, needed, needed, finishing, IdentityCertain: finishing);
        }
        catch (HistoryException e)
        {
            return Refuse(HistorySetupCodes.HistoryFailed, e.Message);
        }

        static HistorySetupCheck Refuse(string code, string reason) => new(false, code, reason, false, false);
    }

    /// <inheritdoc />
    public HistorySetupResult TurnOn(HistorySetupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = request.AuthorName?.Trim();
        var email = request.AuthorEmail?.Trim();
        if ((name is null ? null : HistorySetup.IdentityProblem("name", name)) is { } nameProblem)
        {
            return Refused(HistorySetupCodes.BadArgument, nameProblem);
        }

        if ((email is null ? null : HistorySetup.IdentityProblem("email", email)) is { } emailProblem)
        {
            return Refused(HistorySetupCodes.BadArgument, emailProblem);
        }

        var given = name is not null || email is not null;
        if (given && RedirectedByEnvironment() is { } redirected)
        {
            return Refused(HistorySetupCodes.BadArgument, redirected);
        }

        var check = Check(cancellationToken);
        if (!check.Possible)
        {
            return Refused(check.Code!, given ? $"{check.Reason} {NotApplied(check.Code!)}".TrimEnd() : check.Reason!);
        }

        var steps = new List<string>();
        var made = false;
        string? gitIgnore = null;
        var saved = new List<string>();
        var tookBack = false;

        // 1. The repository. The name and email are checked once it's there, since what
        // git has for it can depend on where it is (includeIf "gitdir:…").
        if (!check.Finishing)
        {
            BeforeStep?.Invoke("init");
            var created = GitRepository.Create(_denRoot, _options, cancellationToken);
            if (created.Kind != GitLocationKind.InRepository)
            {
                return Stop(
                    created.Kind == GitLocationKind.GitNotInstalled ? HistorySetupCodes.GitNotInstalled : HistorySetupCodes.HistoryFailed,
                    $"Git couldn't make the den's folder a repository: {Sentence(created.Problem)}",
                    "Nothing else was done.");
            }

            // One made meanwhile isn't this run's to take away, and may have checkpoints by now.
            made = !created.AlreadyThere;
            if (created.AlreadyThere)
            {
                var again = Check(cancellationToken);
                if (!again.Possible)
                {
                    return Refused(again.Code!, given ? $"{again.Reason} {NotApplied(again.Code!)}".TrimEnd() : again.Reason!);
                }
            }
        }

        // 2. The name and email git will use: refused, with the repository just made taken
        // away again, if one is missing and not given, if one given would be paired with a
        // value of git's the writer didn't choose or lose to an environment variable, or if
        // git won't record them.
        string? hasName;
        string? hasEmail;
        try
        {
            (hasName, hasEmail) = Identity(cancellationToken);
            if (Missing(hasName is null, hasEmail is null, name, email) is { } missing)
            {
                return Undo(HistorySetupCodes.IdentityNeeded, missing, name is null, email is null);
            }

            if (HalfGiven(name, email) is { } half)
            {
                return Undo(HistorySetupCodes.IdentityHalfGiven, half, needsName: name is null, needsEmail: email is null);
            }

            if (given && Unrecordable(name!, email!, cancellationToken) is { } unrecordable)
            {
                return Undo(HistorySetupCodes.BadArgument, unrecordable);
            }
        }
        catch (HistoryException e)
        {
            return Stop(HistorySetupCodes.HistoryFailed, $"Couldn't read the name and email git has for the den's repository: {e.Message}", NotDone(identity: true));
        }

        steps.Add(made ? "Made the den's folder a git repository." : "Used the git repository already in the den's folder, which had no checkpoints yet.");

        // 3. Save what was given, which wins for this den (#286), before anything can fail and
        // send the writer to try again without it. Signing goes off first, so a save that stops
        // part way, and a retry that then sees nothing to change, can't sign with git's key.
        var signingOff = false;
        var written = new List<(string Key, string?[] Before)>();
        if (given)
        {
            string? beatenAfterSaving;
            try
            {
                var stronger = StrongerSettings(name, email, cancellationToken);
                var signsNow = SignsNow(cancellationToken);
                foreach (var setting in SigningSettings)
                {
                    Save(setting, "false");
                }

                signingOff = signsNow;
                Save("user.name", name!);
                saved.Add("user.name");
                Save("user.email", email!);
                saved.Add("user.email");
                foreach (var (key, value) in stronger)
                {
                    Save(key, value);
                }

                beatenAfterSaving = BeatenAfterSaving(name, email, cancellationToken);
            }
            catch (HistoryException e)
            {
                var problem = $"Couldn't save the name and email for the den's repository: {e.Message}";
                if (made)
                {
                    return Undo(HistorySetupCodes.HistoryFailed, problem);
                }

                return Stop(HistorySetupCodes.HistoryFailed, problem, TakeBackWhatWasSaved());
            }

            if (beatenAfterSaving is not null)
            {
                if (made)
                {
                    return Undo(HistorySetupCodes.BadArgument, beatenAfterSaving);
                }

                // Left saved, the next commit from anywhere would record the name given, and sign it
                // with whatever beat signing's being off. Taken back, it's under git's own name.
                return Stop(HistorySetupCodes.BadArgument, beatenAfterSaving, TakeBackWhatWasSaved());
            }

            AddSavedSteps();
            if (SetInEnvironment(name, email) is { } inEnvironment)
            {
                steps.Add(inEnvironment);
            }
        }

        // 4. .gitignore.
        try
        {
            gitIgnore = EnsureIgnored(cancellationToken);
            steps.Add(gitIgnore switch
            {
                "created" => $"Created .gitignore, with {IgnoreLine}, so Wolfden's index stays out of the history.",
                "added" => $"Added {IgnoreLine} to .gitignore, so Wolfden's index stays out of the history.",
                _ => $".gitignore already leaves out {IgnoreLine}.",
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Stop(HistorySetupCodes.WriteFailed, $"Couldn't update .gitignore: {Sentence(e.Message)}", request.FirstCheckpoint ? "No first checkpoint was made." : "Nothing else was done.");
        }

        // 5. The first checkpoint.
        HistoryEntry? first = null;
        if (!request.FirstCheckpoint)
        {
            steps.Add("Made no first checkpoint, as asked.");
        }
        else
        {
            try
            {
                var history = GitHistory.ForDen(_denRoot, _options, cancellationToken);
                var checkpoint = new CheckpointRequest(HistorySetup.FirstCheckpointMessage);
                var result = request.Checkpoint is { } make ? make(history, checkpoint) : history.Checkpoint(checkpoint, cancellationToken);
                if (!result.IsAvailable)
                {
                    return Stop(HistorySetupCodes.HistoryFailed, $"Couldn't make the first checkpoint: {result.Reason}", FirstCheckpointNotDone);
                }

                first = result.Value;
                steps.Add(first is not null
                    ? $"Made the first checkpoint, \"{HistorySetup.FirstCheckpointMessage}\", of {Count(first.Changes.Count)}."
                    : HasCommits(cancellationToken)
                        ? "Made no first checkpoint: the den's files were already recorded, by another checkpoint made meanwhile."
                        : "Made no first checkpoint: the den has no files to record yet.");
                steps.AddRange(result.Warnings);
            }
            catch (HistoryException e)
            {
                return Stop(HistorySetupCodes.HistoryFailed, $"Couldn't make the first checkpoint: {e.Message}", FirstCheckpointNotDone);
            }
        }

        return new HistorySetupResult(HistorySetupStatus.Done, null, null, steps, _denRoot, made, gitIgnore, saved, first);

        HistorySetupResult Refused(string code, string problem) => new(HistorySetupStatus.Refused, code, problem, [], null, false, null, [], null);

        // Refused after git init: the repository this run made, still without a commit,
        // is taken away again, so nothing is changed. A finished one was there before.
        HistorySetupResult Undo(string code, string problem, bool needsName = false, bool needsEmail = false)
        {
            var removal = made ? RemoveNewRepository(cancellationToken) : null;
            if (removal is { Leftover: { } leftover })
            {
                // The den is clean, and a retry works; only the moved-aside folder is left.
                return new HistorySetupResult(
                    HistorySetupStatus.Refused,
                    code,
                    $"{problem} The empty git repository Wolfden made first was moved out of the way, so the den has no repository, " +
                    $"but Wolfden couldn't delete it: delete the folder '{leftover}' in the den's folder.",
                    [], null, false, null, [], null, needsName, needsEmail);
            }

            if (removal is { Removed: false } failed)
            {
                return new HistorySetupResult(
                    HistorySetupStatus.Incomplete,
                    code,
                    $"{problem} The den's folder was made a git repository first, and Wolfden couldn't take it away again ({failed.Why}), so it's an empty repository now. " +
                    (given ? "Try again with the name and email: it picks up where this stopped." : "Try again with what's missing: it picks up where this stopped."),
                    ["Made the den's folder a git repository."],
                    _denRoot,
                    true,
                    null,
                    [],
                    null,
                    needsName,
                    needsEmail);
            }

            return new HistorySetupResult(HistorySetupStatus.Refused, code, $"{problem} Nothing was changed.", [], null, false, null, [], null, needsName, needsEmail);
        }

        HistorySetupResult Stop(string code, string problem, string notDone, bool needsName = false, bool needsEmail = false)
        {
            var status = steps.Count == 0 && !made ? HistorySetupStatus.Refused : HistorySetupStatus.Incomplete;
            if (made && steps.Count == 0)
            {
                steps.Add("Made the den's folder a git repository.");
            }

            var howToFinish = !given ? HowToFinish : saved.Count == 2 && !tookBack ? HowToFinishWithANameSaved : HowToFinishWithAName;
            var text = status == HistorySetupStatus.Refused ? $"{problem} {notDone}" : $"{problem} {notDone} {howToFinish}";
            return new HistorySetupResult(status, code, text, steps, status == HistorySetupStatus.Refused ? null : _denRoot, made, gitIgnore, saved, null, needsName, needsEmail);
        }

        string NotDone(bool identity) => request.FirstCheckpoint
            ? identity ? "No name or email was saved, and no first checkpoint was made." : "No first checkpoint was made."
            : "No name or email was saved.";

        void AddSavedSteps()
        {
            if (saved.Count > 0)
            {
                var what = saved.Count == 2 ? "your name and email" : saved[0] == "user.name" ? "your name" : "your email";
                steps.Add($"Saved {what} for this den's repository only.");
            }

            if (signingOff)
            {
                steps.Add(
                    "Turned signing off for this den's repository only: git is set to sign, and Wolfden can't tell whose key that would use. " +
                    "To sign here with a key of this name's, run 'git config --local user.signingkey <key>' and 'git config --local commit.gpgsign true' in the den's folder, and 'git config --local tag.gpgsign true' to sign tags too. " +
                    "If your gpg.conf has a 'local-user' line, gpg adds that key's signature too, so take that into account first.");
            }
        }

        // In a repository that was already there: as it was before this try, if git lets it be, which
        // keeps a name an earlier try saved. So it says "put back", not that nothing's left. Says what is.
        string TakeBackWhatWasSaved()
        {
            tookBack = true;
            var leftover = TakeBack(written);
            saved.RemoveAll(key => !leftover.Contains(key));
            signingOff = signingOff && SigningSettings.All(leftover.Contains);
            AddSavedSteps();
            var checkpointNotDone = request.FirstCheckpoint ? " No first checkpoint was made." : "";
            return leftover.Count == 0
                ? $"What this try saved was put back as it was.{checkpointNotDone}"
                : $"These couldn't be put back as they were, and are still saved for this den: {string.Join(", ", leftover)}.{checkpointNotDone}";
        }

        void Save(string key, string value)
        {
            var before = LocalValues(key, cancellationToken);
            BeforeStep?.Invoke(key);
            SetLocal(key, value, cancellationToken);
            written.Add((key, before));
        }
    }

    private const string FirstCheckpointNotDone = "No first checkpoint was made, but history is on.";

    /// <summary>What a turn-on that stopped part way tells the writer to do.</summary>
    private const string HowToFinish =
        "The den's folder is a git repository now, so to finish, fix the problem and run 'wolfden init --git' in the den again: it picks up where this stopped. " +
        "Or make the first checkpoint from the web UI's checkpoint dialog.";

    /// <summary>
    /// The same when a name or email was given. It's asked for again, since what was saved may
    /// not be all of it, and the dialog isn't offered, since it would use what git has for the rest.
    /// </summary>
    private const string HowToFinishWithAName =
        "The den's folder is a git repository now, so to finish, fix the problem and run 'wolfden init --git --author-name \"<name>\" --author-email <email>' in the den again, " +
        "with the same name and email: it picks up where this stopped. Without them, its checkpoints can be recorded under what git has.";

    /// <summary>
    /// The same when the name and email were all saved, and something later stopped it. They
    /// stay, so going on without them records them, not what git has, and only others replace them.
    /// </summary>
    private const string HowToFinishWithANameSaved =
        "The den's folder is a git repository now, so to finish, fix the problem and run 'wolfden init --git' in the den again: it picks up where this stopped. " +
        "The name and email given stay saved for this den, so its checkpoints are recorded under them, not what git has, " +
        "whether or not they're given again. To use others, add --author-name \"<name>\" --author-email <email> to that command before the first checkpoint.";

    /// <summary>
    /// Why a name or email given can't be used alone, in words, or null if it
    /// can. The two go together (maintainer decision, #286): alone, either
    /// would be recorded beside what git has for the other, a pen name with the
    /// email it was meant to keep apart. What git has isn't repeated, so the
    /// message is safe to copy anywhere. One that git lacks the other half of
    /// is <see cref="Missing"/>'s to say, first.
    /// </summary>
    private static string? HalfGiven(string? name, string? email)
    {
        if (name is not null && email is null)
        {
            return "A name was given without an email. Git has an email for this den, and would record it beside the name given. " +
                "Give an email too: it's saved with the name for this den only, and a placeholder such as you@example.com will do.";
        }

        if (email is not null && name is null)
        {
            return "An email was given without a name. Git has a name for this den, and would record it beside the email given. " +
                "Give a name too: it's saved with the email for this den only, and any name will do, a pen name included.";
        }

        return null;
    }

    /// <summary>
    /// The git commands that put a repository with checkpoints under another name from now on:
    /// the name and email, the <c>author.*</c> and <c>committer.*</c> settings git prefers to
    /// them, and signing off, as turning history on with a name does.
    /// </summary>
    public const string ChangeNameByHand =
        "run these in the den's folder: " +
        "git config --local user.name \"<name>\" ; git config --local user.email <email> ; " +
        "git config --local author.name \"<name>\" ; git config --local author.email <email> ; " +
        "git config --local committer.name \"<name>\" ; git config --local committer.email <email> " +
        "(git prefers those two kinds to the first, if it has them), and, so git's own key doesn't sign under that name: " +
        "git config --local commit.gpgsign false ; git config --local tag.gpgsign false ; " +
        "git config --local tag.forcesignannotated false ; git config --local push.gpgsign false . " +
        "Earlier checkpoints keep the name they have.";

    /// <summary>What a refusal adds when a name or email was given: that it wasn't applied, and, where there's one, the git commands that would.</summary>
    public static string NotApplied(string code) => code switch
    {
        HistorySetupCodes.HasRepository =>
            $"The name and email given weren't applied, so later checkpoints stay under what git has for this den. To change that from now on, {ChangeNameByHand}",
        HistorySetupCodes.InRepository =>
            "The name and email given weren't applied, so later checkpoints stay under what git has for that repository, whose settings this den shares.",
        _ => "",
    };

    /// <summary>Why what's given would be saved in another repository, or null: <c>GIT_DIR</c> and <c>GIT_COMMON_DIR</c> point git away from the den's folder.</summary>
    public string? RedirectedByEnvironment()
    {
        var set = new[] { "GIT_DIR", "GIT_COMMON_DIR" }.Where(v => EnvironmentValue(v) is not null).ToList();
        return set.Count == 0
            ? null
            : $"{string.Join(" and ", set)} {(set.Count == 1 ? "is" : "are")} set in the environment, which points git at another repository, so the name and email given would be saved there. " +
                "Unset that, then try again. Nothing was changed.";
    }

    /// <summary>
    /// Why git wouldn't record what was given, or would still sign, now that it's saved, in
    /// words, or null if all is as it should be. Git's own answer counts, since settings it reads
    /// after the repository's own can beat them. The value that beat them isn't repeated.
    /// </summary>
    private string? BeatenAfterSaving(string? name, string? email, CancellationToken cancellationToken)
    {
        const string Causes = "Something git reads after the den's own settings beats them: GIT_CONFIG_COUNT or GIT_CONFIG_PARAMETERS in the environment, " +
            "extensions.worktreeConfig, or an [include] in the repository's config. Remove that, then try again.";
        // As the gate asks it: from git's settings, not the environment's name and email (CommitGate),
        // and only once git finds the repository on its own settings too.
        if (CommitGate.Untrusted((args, environment) => Run([.. args], cancellationToken, environment: environment)) is { } untrusted)
        {
            return untrusted;
        }

        var settingsOnly = CommitGate.SettingsOnly();
        foreach (var ident in new[] { "GIT_AUTHOR_IDENT", "GIT_COMMITTER_IDENT" })
        {
            var run = Run(["var", ident], cancellationToken, environment: settingsOnly);
            if (!run.Succeeded)
            {
                // Not what git's output said, which can name what git has.
                return "Git couldn't say what it would record with the name and email given. Check its settings, and a GIT_AUTHOR_DATE or GIT_COMMITTER_DATE in the environment, then try again.";
            }

            var text = run.Output;
            var open = text.IndexOf('<', StringComparison.Ordinal);
            var close = open < 0 ? -1 : text.IndexOf('>', open);
            if (close < 0
                || (name is not null && WithoutMark(text[..open].TrimEnd(' ')) != WithoutCrud(name))
                || (email is not null && WithoutMark(text[(open + 1)..close]) != WithoutCrud(email)))
            {
                return $"Git wouldn't record the name and email given, even with them saved for this den. {Causes}";
            }
        }

        if (SigningSettings.Any(key => Run(["config", "--type=bool", "--get", key], cancellationToken).Output.Trim() != "false"))
        {
            return $"Git would still sign, with a key that isn't tied to the name and email given, even with signing saved as off for this den. {Causes}";
        }

        return null;
    }

    /// <summary>A name or email as git records it: without the characters it strips from the ends, or the angle brackets and line breaks it strips anywhere.</summary>
    private static string WithoutCrud(string value) =>
        WithoutMark(value).Replace("<", "", StringComparison.Ordinal).Replace(">", "", StringComparison.Ordinal)
            .Trim(' ', '\t', ',', ':', ';', '"', '\\', '\'');

    /// <summary>Without a byte order mark, which reading git's output can drop from the start.</summary>
    private static string WithoutMark(string value) => value.Replace("\uFEFF", "", StringComparison.Ordinal);

    /// <summary>
    /// A step that says an environment variable holds another name or email,
    /// or null. Wolfden's own checkpoints don't use it (<see cref="CommitGate"/>
    /// gives git the name and email in its environment), but git commands run by
    /// hand do, since it beats every setting. Its value isn't repeated.
    /// </summary>
    private string? SetInEnvironment(string? name, string? email)
    {
        var set = new[] { ("GIT_AUTHOR_NAME", name), ("GIT_COMMITTER_NAME", name), ("GIT_AUTHOR_EMAIL", email), ("GIT_COMMITTER_EMAIL", email) }
            .Where(v => v.Item2 is not null && EnvironmentValue(v.Item1) is { } value && value != v.Item2)
            .Select(v => v.Item1)
            .ToList();
        return set.Count == 0
            ? null
            : $"{string.Join(", ", set)} {(set.Count == 1 ? "is" : "are")} set in the environment. Wolfden's checkpoints for this den are recorded under the name and email given anyway, " +
                "but git commands you run yourself use the environment's, as in any repository.";
    }

    /// <summary>A variable as git will see it: <see cref="GitOptions.Environment"/> first, then this process's own.</summary>
    private string? EnvironmentValue(string variable) =>
        _options.Environment.TryGetValue(variable, out var value) ? value : Environment.GetEnvironmentVariable(variable);

    /// <summary>
    /// Settings git prefers to <c>user.name</c> and <c>user.email</c>
    /// (<c>author.name</c>, <c>committer.email</c>, …) that hold something
    /// other than what was given, each with the value to save over it.
    /// </summary>
    private List<(string Key, string Value)> StrongerSettings(string? name, string? email, CancellationToken cancellationToken)
    {
        var found = new List<(string, string)>();
        foreach (var (field, value) in new[] { ("name", name), ("email", email) })
        {
            if (value is null)
            {
                continue;
            }

            foreach (var role in new[] { "author", "committer" })
            {
                if (Setting($"{role}.{field}", cancellationToken) is { } other && other != value)
                {
                    found.Add(($"{role}.{field}", value));
                }
            }
        }

        return found;
    }

    /// <summary>The repository's own values of <paramref name="key"/>, none if it isn't set there.</summary>
    private string?[] LocalValues(string key, CancellationToken cancellationToken)
    {
        // --list, since only it tells "gpgsign" with no value (null here), which means true, from
        // an empty "gpgsign =". With --null, each is "key\nvalue\0", or "key\0" with no value.
        var run = Run(["config", "--local", "--null", "--list"], cancellationToken);
        if (!run.Succeeded)
        {
            throw new HistoryException($"Git couldn't read its '{key}' setting: {run.Said}");
        }

        return run.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('\n', 2))
            .Where(parts => string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts.Length == 2 ? parts[1] : null)
            .ToArray();
    }

    /// <summary>
    /// Puts <paramref name="written"/> settings back as they were, last first, and returns the
    /// keys it couldn't, in the order they were saved. Whatever stopped the save may stop this too.
    /// It stops at the first it can't, so signing, saved first, is the last to come back on.
    /// </summary>
    private List<string> TakeBack(List<(string Key, string?[] Before)> written)
    {
        for (var i = written.Count - 1; i >= 0; i--)
        {
            var (key, before) = written[i];
            try
            {
                var unset = Run(["config", "--local", "--unset-all", key], CancellationToken.None, GitCallKind.MustFinish);

                // Git can't write a setting with no value, so one comes back as "true", which it means.
                var restored = unset.Succeeded && before.All(value => Run(["config", "--local", "--add", key, value ?? "true"], CancellationToken.None, GitCallKind.MustFinish).Succeeded);
                if (!restored)
                {
                    return written.Take(i + 1).Select(w => w.Key).ToList();
                }
            }
            catch (HistoryException)
            {
                return written.Take(i + 1).Select(w => w.Key).ToList();
            }
        }

        return [];
    }

    /// <summary>True if git would sign a commit or tag made now. A value git can't read as true or false counts as on.</summary>
    private bool SignsNow(CancellationToken cancellationToken) => SignsCheckpoints.Any(key =>
    {
        var run = Run(["config", "--type=bool", "--get", key], cancellationToken);
        return run.ExitCode != 1 && run.Output.Trim() != "false";
    });

    private static string Count(int files) => files == 1 ? "1 file" : $"{files} files";

    /// <summary>What's missing that wasn't given, in words, or null if git will have both.</summary>
    private static string? Missing(bool needsName, bool needsEmail, string? name, string? email)
    {
        var noName = needsName && name is null;
        var noEmail = needsEmail && email is null;
        if (!noName && !noEmail)
        {
            return null;
        }

        // Both, unless one was given: the two go together (#286).
        var what = name is null && email is null ? "a name and an email" : noName ? "a name" : "an email";
        return $"Git records who made each checkpoint, and it has no {(noName && noEmail ? "name or email" : noName ? "name" : "email")} for this den. " +
            $"Give {what}: they're saved for this den's repository only. Any name will do, a pen name included, and the email can be a placeholder, " +
            "such as you@example.com.";
    }

    /// <summary>Ends <paramref name="text"/> with one full stop (or other closing mark), never two.</summary>
    private static string Sentence(string? text)
    {
        var t = (text ?? "").TrimEnd();
        return t.Length > 0 && t[^1] is '.' or '!' or '?' ? t : t + ".";
    }

    /// <summary>True if git's top level for the den's folder is the folder itself (<c>git rev-parse --show-prefix</c> is empty).</summary>
    private bool IsTopLevel(CancellationToken cancellationToken)
    {
        var run = Run(["rev-parse", "--show-prefix"], cancellationToken);
        if (!run.Succeeded || !run.OutputComplete)
        {
            throw new HistoryException($"Git couldn't tell where the den's repository starts: {run.Said}");
        }

        return run.Output.Trim().Length == 0;
    }

    /// <summary>
    /// Why git won't record <paramref name="name"/> and <paramref name="email"/>
    /// on a checkpoint, in git's words, or null if it will: git's own rules,
    /// through <c>git var GIT_COMMITTER_IDENT</c>, which refuses, say, a name
    /// made only of characters git strips (<c>;</c>, <c>,</c>, …). Git takes
    /// such an email but records it as empty (<c>Ann &lt;&gt;</c>), so that's refused too.
    /// What's given is also passed as <c>committer.name</c> and
    /// <c>committer.email</c>, which beat <c>user.name</c> and would otherwise be what git checks.
    /// </summary>
    private string? Unrecordable(string name, string email, CancellationToken cancellationToken)
    {
        var run = Run(["-c", $"user.name={name}", "-c", $"user.email={email}", "-c", $"committer.name={name}", "-c", $"committer.email={email}", "var", "GIT_COMMITTER_IDENT"], cancellationToken);
        if (!run.Succeeded)
        {
            return $"Git won't record that name and email on a checkpoint ({Sentence(run.Said)[..^1]}). Choose another; any ordinary name will do, a pen name included.";
        }

        // "Name <email> 1793462796 -0700": the name can't hold '<' or '>' (HistorySetup.IdentityProblem).
        var ident = run.Output.Trim();
        var open = ident.IndexOf('<', StringComparison.Ordinal);
        var close = open < 0 ? -1 : ident.IndexOf('>', open);
        if (run.OutputComplete && close > open && ident[(open + 1)..close].Trim().Length == 0)
        {
            return $"Git won't record that email on a checkpoint: it takes out characters such as ';' and ',' at the ends, which leaves nothing of '{email}'. " +
                "Give another; a placeholder such as you@example.com will do.";
        }

        return null;
    }

    /// <summary>
    /// Takes away the repository this run just made, before anything else was
    /// done in it: only if it still has no commits. True if it's gone.
    /// </summary>
    private Removal RemoveNewRepository(CancellationToken cancellationToken)
    {
        var git = Path.Combine(_denRoot, ".git");
        try
        {
            if (!Directory.Exists(git) || HasCommits(cancellationToken))
            {
                return new Removal(false, "it has a commit now, or isn't a folder", null);
            }
        }
        catch (HistoryException e)
        {
            return new Removal(false, e.Message, null);
        }

        // One step first: the .git is moved aside in one rename, so the den is either still a
        // repository or not one at all, never a half-deleted one (#205 review).
        var aside = Path.Combine(_denRoot, $".git-removed-by-wolfden-{Guid.NewGuid():N}");
        try
        {
            Directory.Move(git, aside);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Removal(false, Sentence(e.Message)[..^1], null);
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(aside, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(aside, recursive: true);
            return new Removal(true, null, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Removal(true, null, Path.GetFileName(aside));
        }
    }

    /// <summary>What taking away a repository this run made did.</summary>
    /// <param name="Removed">True if the den's folder has no <c>.git</c> any more.</param>
    /// <param name="Why">Why it's still there, when it is.</param>
    /// <param name="Leftover">The hidden folder in the den it was moved to and couldn't be deleted from, or null.</param>
    private sealed record Removal(bool Removed, string? Why, string? Leftover);

    /// <summary>
    /// The repository the den's folder is a linked worktree of (its common
    /// git folder's parent), or null if the den's repository is its own:
    /// <c>git rev-parse --git-dir</c> and <c>--git-common-dir</c> are the same folder.
    /// </summary>
    private string? SharedRepository(CancellationToken cancellationToken)
    {
        var run = Run(["rev-parse", "--path-format=absolute", "--git-dir", "--git-common-dir"], cancellationToken);
        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!run.Succeeded || !run.OutputComplete || lines.Length != 2)
        {
            throw new HistoryException($"Git couldn't tell whether the den's repository is its own: {run.Said}");
        }

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var gitDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(lines[0]));
        var common = Path.TrimEndingDirectorySeparator(Path.GetFullPath(lines[1]));
        return string.Equals(gitDir, common, comparison) ? null : Path.GetDirectoryName(common) ?? common;
    }

    private bool HasOwnGit()
    {
        var git = Path.Combine(_denRoot, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    private bool HasCommits(CancellationToken cancellationToken)
    {
        var run = Run(["rev-parse", "--verify", "--quiet", "HEAD^{commit}"], cancellationToken);
        return run.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new HistoryException($"Git couldn't tell whether the den's repository has checkpoints: {run.Said}"),
        };
    }

    /// <summary>True if the den's own <c>.gitignore</c> leaves out <c>.wolfden/</c>, as git decides; false if not; null if git can't say.</summary>
    private bool? IgnoredByGitignore(CancellationToken cancellationToken)
    {
        GitRun run;
        try
        {
            run = Run(["check-ignore", "--verbose", "--no-index", "--", ".wolfden/"], cancellationToken);
        }
        catch (HistoryException)
        {
            return null;
        }

        return run.ExitCode switch
        {
            // "<source>:<line>:<pattern>\t<path>": ignored, but say so only when the source is the den's .gitignore.
            0 => run.Output.StartsWith(".gitignore:", StringComparison.Ordinal),
            1 => false,
            _ => null,
        };
    }

    /// <summary>The name and email git would use for the den's repository, each null if it has none.</summary>
    private (string? Name, string? Email) Identity(CancellationToken cancellationToken) => (Setting("user.name", cancellationToken), Setting("user.email", cancellationToken));

    private string? Setting(string key, CancellationToken cancellationToken)
    {
        var run = Run(["config", "--get", key], cancellationToken);
        if (run.ExitCode == 1)
        {
            return null;
        }

        if (!run.Succeeded)
        {
            throw new HistoryException($"Git couldn't read its '{key}' setting: {run.Said}");
        }

        var value = run.Output.Trim();
        return value.Length == 0 ? null : value;
    }

    private void SetLocal(string key, string value, CancellationToken cancellationToken)
    {
        // All of a key's values: more than one would otherwise refuse the save.
        var run = Run(["config", "--local", "--replace-all", key, value], cancellationToken, GitCallKind.MustFinish);
        if (!run.Succeeded)
        {
            throw new HistoryException($"git config {key} failed: {run.Said}");
        }
    }

    private GitRun Run(string[] args, CancellationToken cancellationToken, GitCallKind kind = GitCallKind.Stoppable, IReadOnlyDictionary<string, string?>? environment = null)
    {
        try
        {
            return GitProcess.Run(_options, _denRoot, args, environment: environment, cancellationToken: cancellationToken, kind: kind);
        }
        catch (GitNotFoundException e)
        {
            throw new HistoryException($"Git isn't installed, or Wolfden can't find it on the PATH: {e.Message}", e);
        }
        catch (GitTimeoutException e)
        {
            throw new HistoryException($"{e.Message}, so Wolfden stopped waiting for it", e);
        }
    }

    /// <summary>
    /// Makes sure the den's <c>.gitignore</c> leaves out <c>.wolfden/</c>:
    /// <c>created</c>, <c>added</c>, or <c>present</c>. Whether it does is
    /// git's own answer (<c>git check-ignore</c>, from the den's
    /// <c>.gitignore</c> itself, so a later <c>!.wolfden/</c> counts, and a
    /// global excludes file doesn't); if git can't say, a line naming it does.
    /// </summary>
    private string EnsureIgnored(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_denRoot, ".gitignore");
        if (!File.Exists(path))
        {
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                stream.Write(Utf8.GetBytes($"# Wolfden's index, rebuilt from the den's files\n{IgnoreLine}\n"));
                return "created";
            }
            catch (IOException) when (File.Exists(path))
            {
                // Made meanwhile: fall through and read it.
            }
        }

        var text = File.ReadAllText(path);
        if (IgnoredByGitignore(cancellationToken) ?? text.Split('\n').Select(l => l.Trim()).Any(l => IgnoreSpellings.Contains(l, StringComparer.Ordinal)))
        {
            return "present";
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var start = text.Length > 0 && !text.EndsWith('\n') ? newline : "";
        File.AppendAllText(path, $"{start}{IgnoreLine}{newline}", Utf8);
        return "added";
    }
}

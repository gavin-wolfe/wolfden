using Wolfden.Core.History;

namespace Wolfden.Git.Tests;

/// <summary>
/// <see cref="GitHistory.LaterOf"/> (#201): which revisions come after a
/// checkpoint in the history HEAD is on, by ancestry, which freezing uses
/// to tell whether a freeze tag is later than the checkpoint that last
/// changed a book.
/// </summary>
public sealed class LaterOfTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void OnAStraightHistoryOnlyCheckpointsMadeAfterItAreLaterAndInTheOrderGiven()
    {
        using var repo = TestRepo.Create();
        repo.WriteDen("a.md", "# A\n");
        var first = repo.CommitAll("first");
        repo.WriteDen("b.md", "# B\n");
        var second = repo.CommitAll("second");
        repo.WriteDen("c.md", "# C\n");
        var head = repo.CommitAll("third");
        repo.Git("tag", "on-head");

        var later = repo.History.LaterOf(second, [head, first, second, "on-head"], Token);

        // The same checkpoint isn't later, nor is one before it; a tag on HEAD is, by its name.
        Assert.True(later.IsAvailable);
        Assert.Equal([head, "on-head"], later.Value);
        Assert.Empty(repo.History.LaterOf(head, [head, "on-head"], Token).Value);
        Assert.Empty(repo.History.LaterOf(first, [], Token).Value);
    }

    [Fact]
    public void ASideBranchCountsOnceItsMergedIntoHead()
    {
        using var repo = TestRepo.Create();
        repo.WriteDen("a.md", "# A\n");
        var start = repo.CommitAll("start");
        var main = repo.Git("rev-parse", "--abbrev-ref", "HEAD").Trim();
        repo.Git("checkout", "--quiet", "-b", "side");
        repo.WriteDen("side.md", "# Side\n");
        var side = repo.CommitAll("on the side");
        repo.Git("checkout", "--quiet", main);
        repo.WriteDen("main.md", "# Main\n");
        repo.CommitAll("on main");

        // Not merged: HEAD wasn't made from it.
        Assert.Empty(repo.History.LaterOf(start, [side], Token).Value);

        repo.Git("merge", "--quiet", "--no-ff", "--no-edit", "side");
        Assert.Equal([side], repo.History.LaterOf(start, [side], Token).Value);
    }

    [Fact]
    public void ARevisionThatIsntACheckpointIsntLater()
    {
        using var repo = TestRepo.Create();
        repo.WriteDen("a.md", "# A\n");
        var start = repo.CommitAll("start");
        repo.WriteDen("b.md", "# B\n");
        var head = repo.CommitAll("later");

        // A tag on a tree, and one on a tag on a tree, as a hand-made freeze tag might be (#254 review, S1).
        var tree = repo.Git("rev-parse", "HEAD^{tree}").Trim();
        repo.Git("tag", "on-tree", tree);
        repo.Git("tag", "-a", "-m", "tag of a tag", "on-tag-of-tree", "on-tree");
        var peeled = repo.Git("rev-parse", "on-tag-of-tree^{}").Trim();

        var later = repo.History.LaterOf(start, [tree, "on-tree", peeled, "no-such-revision", "-x", head], Token);

        Assert.Equal([head], later.Value);
    }

    [Fact]
    public void BeforeTheFirstCommitNothingIsLaterButTheCheckpointMustExist()
    {
        using var repo = TestRepo.Create();
        repo.WriteDen("a.md", "# A\n");
        var start = repo.CommitAll("start");

        // An unborn branch: HEAD names no commit yet.
        repo.Git("checkout", "--quiet", "--orphan", "fresh");
        Assert.Empty(repo.History.LaterOf(start, [start], Token).Value);

        Assert.Throws<HistoryRevisionException>(() => repo.History.LaterOf("no-such-checkpoint", [start], Token));
    }

    [Fact]
    public void AGitFailureIsAHistoryException()
    {
        using var repo = TestRepo.Create();
        repo.WriteDen("a.md", "# A\n");
        var start = repo.CommitAll("start");
        repo.WriteDen("b.md", "# B\n");
        var middle = repo.CommitAll("middle");
        repo.WriteDen("c.md", "# C\n");
        var head = repo.CommitAll("head");

        // A commit between the two lost from the repository: git can't walk from one to the other.
        var lost = Path.Combine(repo.Root, ".git", "objects", middle[..2], middle[2..]);
        File.SetAttributes(lost, FileAttributes.Normal);
        File.Delete(lost);

        var failed = Assert.ThrowsAny<HistoryException>(() => repo.History.LaterOf(start, [head], Token));
        Assert.IsNotType<HistoryRevisionException>(failed);
        Assert.Contains("merge-base", failed.Message, StringComparison.Ordinal);
    }
}

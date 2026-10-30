namespace Wolfden.Mcp.Tests;

/// <summary>
/// The den's write lock (#221): two sessions on one den, each with its own
/// in-process lock as two Wolfden processes would have, take turns through
/// <c>.wolfden/write.lock</c>, so neither saves over the other's change.
/// </summary>
public sealed class WriteLockTests : IDisposable
{
    private const string Swales = "universes/dracula/books/dracula/entities/characters/mr-swales.md";

    /// <summary>How long a signal may take before the test fails; never a pause of its own.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private readonly TempDen _den = TempDen.Dracula();

    public void Dispose() => _den.Dispose();

    private string LockPath => Path.Combine(_den.Root, ".wolfden", "write.lock");

    [Fact]
    public void TwoSessionsTakeTurnsSoTheSecondSeesTheFirstsChange()
    {
        var hash = new DenTools(_den.Root).GetEntity("Mr. Swales").Entity!.Hash!;

        var cancel = TestContext.Current.CancellationToken;

        // The first session stops after its last comparison with the disk, just before it writes.
        using var paused = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = new DenTools(_den.Root)
        {
            BeforeWrite = () =>
            {
                paused.Set();
                release.Wait(Bound, cancel);
            },
        };
        WriteResult? firstResult = null;
        Exception? firstError = null;
        var firstThread = new Thread(() =>
        {
            try
            {
                firstResult = first.AppendSection("Mr. Swales", "First pass", "From the first session.", hash, newSection: true);
            }
            catch (Exception e)
            {
                firstError = e;
                paused.Set();
            }
        });
        firstThread.Start();
        Assert.True(paused.Wait(Bound, cancel));
        Assert.Null(firstError);

        // A second session, with a lock of its own, edits the same file from the same hash.
        using var waiting = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var second = new DenTools(_den.Root) { WaitingForOtherWriter = _ => waiting.Set() };
        Exception? secondError = null;
        WriteResult? secondResult = null;
        var secondThread = new Thread(() =>
        {
            try
            {
                secondResult = second.AppendSection("Mr. Swales", "Second pass", "From the second session.", hash, newSection: true);
            }
            catch (Exception e)
            {
                secondError = e;
            }
            finally
            {
                done.Set();
            }
        });
        secondThread.Start();

        // It waits for the first (without the lock file it wouldn't, and would finish first).
        WaitHandle.WaitAny([waiting.WaitHandle, done.WaitHandle], Bound);
        release.Set();
        Assert.True(firstThread.Join(Bound));
        Assert.True(secondThread.Join(Bound));

        Assert.Null(firstError);
        Assert.True(waiting.IsSet, "the second session should have waited for the first one's write");
        Assert.True(firstResult!.Changed);
        Assert.Null(secondResult);
        Assert.Equal(ToolErrorCodes.StaleFile, Assert.IsType<DenToolException>(secondError).Code);
        var text = _den.Read(Swales);
        Assert.Contains("From the first session.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("From the second session.", text, StringComparison.Ordinal);

        // Read again, the second session's edit goes in after the first's.
        var again = second.AppendSection("Mr. Swales", "Second pass", "From the second session.", firstResult.Hash, newSection: true);
        Assert.True(again.Changed);
        Assert.Contains("From the first session.", _den.Read(Swales), StringComparison.Ordinal);
        Assert.Contains("From the second session.", _den.Read(Swales), StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteGivesUpWithDenBusyWhileAnotherHoldsTheLockAndGoesAheadOnceItsLetGo()
    {
        var hash = new DenTools(_den.Root).GetEntity("Mr. Swales").Entity!.Hash!;
        var before = _den.Read(Swales);
        var tools = new DenTools(_den.Root) { WriteLockWait = TimeSpan.Zero };

        // Another process holding the lock, as an open file with no sharing.
        var other = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        DenToolException busy;
        try
        {
            busy = Assert.Throws<DenToolException>(() => tools.AppendSection("Mr. Swales", "Later", "He is buried at Whitby.", hash, newSection: true));
        }
        finally
        {
            // A process that ends, however it ends, lets go of the file: the OS closes it.
            other.Dispose();
        }

        Assert.Equal(ToolErrorCodes.DenBusy, busy.Code);
        Assert.Contains("Other writes to the den kept it busy for 0 seconds", busy.Message, StringComparison.Ordinal);
        Assert.Equal(before, _den.Read(Swales));

        // The file stays behind, and means nothing: the next write goes ahead.
        Assert.True(File.Exists(LockPath));
        Assert.True(tools.AppendSection("Mr. Swales", "Later", "He is buried at Whitby.", hash, newSection: true).Changed);
    }

    [Fact]
    public void AWriteQueuedBehindThisSessionsOwnWriteHasTheSameWaitNotOneOfItsOwn()
    {
        var hash = new DenTools(_den.Root).GetEntity("Mr. Swales").Entity!.Hash!;
        var cancel = TestContext.Current.CancellationToken;

        // One session (as 'serve' is), with no time to wait: its first write holds its turn, paused before writing.
        using var paused = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var tools = new DenTools(_den.Root)
        {
            WriteLockWait = TimeSpan.Zero,
            BeforeWrite = () =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    paused.Set();
                    release.Wait(Bound, cancel);
                }
            },
        };
        Exception? firstError = null;
        var firstThread = new Thread(() =>
        {
            try
            {
                tools.AppendSection("Mr. Swales", "First pass", "From the first request.", hash, newSection: true);
            }
            catch (Exception e)
            {
                firstError = e;
                paused.Set();
            }
        });
        firstThread.Start();
        Assert.True(paused.Wait(Bound, cancel));
        Assert.Null(firstError);

        // A second request in the same session gives up within its wait, while the first still holds the turn.
        using var done = new ManualResetEventSlim();
        Exception? secondError = null;
        var secondThread = new Thread(() =>
        {
            try
            {
                tools.AppendSection("Mr. Swales", "Second pass", "From the second request.", hash, newSection: true);
            }
            catch (Exception e)
            {
                secondError = e;
            }
            finally
            {
                done.Set();
            }
        });
        secondThread.Start();
        var gaveUp = done.Wait(Bound, cancel);
        release.Set();
        Assert.True(firstThread.Join(Bound));
        Assert.True(secondThread.Join(Bound));

        Assert.True(gaveUp, "the queued write should have given up while the first still held the session's turn");
        Assert.Null(firstError);
        Assert.Equal(ToolErrorCodes.DenBusy, Assert.IsType<DenToolException>(secondError).Code);
        Assert.DoesNotContain("From the second request.", _den.Read(Swales), StringComparison.Ordinal);
    }

    [Fact]
    public void ALockFileThatCantBeOpenedFailsAtOnceWithWriteFailed()
    {
        var hash = new DenTools(_den.Root).GetEntity("Mr. Swales").Entity!.Hash!;

        // A folder where the lock file should be: no other Wolfden holds it, so waiting won't help.
        File.Delete(LockPath);
        Directory.CreateDirectory(LockPath);
        var failed = Assert.Throws<DenToolException>(() => new DenTools(_den.Root).AppendSection("Mr. Swales", "Later", "x", hash, newSection: true));

        Assert.Equal(ToolErrorCodes.WriteFailed, failed.Code);
        Assert.Contains("write lock", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedOrFinishedWriteLetsGoOfTheLock()
    {
        var hash = new DenTools(_den.Root).GetEntity("Mr. Swales").Entity!.Hash!;
        var tools = new DenTools(_den.Root);
        var other = new DenTools(_den.Root) { WriteLockWait = TimeSpan.Zero };

        Assert.Equal(ToolErrorCodes.StaleFile, Assert.Throws<DenToolException>(() => tools.AppendSection("Mr. Swales", "Later", "x", "0000", newSection: true)).Code);
        var written = other.AppendSection("Mr. Swales", "Later", "He is buried at Whitby.", hash, newSection: true);
        Assert.True(tools.AppendSection("Mr. Swales", "Voice", "He calls the gravestones lies.", written.Hash, newSection: true).Changed);

        // Nothing holds it now: it opens with no sharing.
        using var probe = new FileStream(LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}

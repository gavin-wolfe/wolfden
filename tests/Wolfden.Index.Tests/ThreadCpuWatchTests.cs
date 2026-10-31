namespace Wolfden.Index.Tests;

/// <summary>
/// <see cref="ThreadCpuWatch"/> measures something (#248): a watch that
/// counted too little, say from a wrong field or unit, would let every
/// performance bound pass.
/// </summary>
public sealed class ThreadCpuWatchTests
{
    private static readonly TimeSpan Target = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(10);

    [Fact]
    public void SpinningOnTheThreadCountsAsItsProcessorTime()
    {
        // Spin until the watch reports 100 ms of processor time, giving up
        // after 10 s by the clock. A starved thread on a busy runner just
        // takes longer to get there; a watch that counts nothing, or reads
        // the wrong field or unit, never does.
        var watch = ThreadCpuWatch.StartNew();
        var spins = 0L;
        while (watch.Cpu < Target && watch.Clock < GiveUp)
        {
            spins++;
        }

        var cpu = watch.Cpu;
        var clock = watch.Clock;

        // The thread can't use more processor time than has passed, plus a
        // tick of the OS's coarser count (10 ms on Linux, about 16 ms on
        // Windows).
        Assert.True(spins > 0);
        Assert.True(cpu >= Target, $"Only {cpu} of processor time in {clock}.");
        Assert.True(cpu <= clock + TimeSpan.FromMilliseconds(20), $"{cpu} of processor time in {clock}.");
    }
}

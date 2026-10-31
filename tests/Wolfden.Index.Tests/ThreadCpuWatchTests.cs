namespace Wolfden.Index.Tests;

/// <summary>
/// <see cref="ThreadCpuWatch"/> measures something (#248): a watch that
/// counted too little, say from a wrong field or unit, would let every
/// performance bound pass.
/// </summary>
public sealed class ThreadCpuWatchTests
{
    [Fact]
    public void SpinningOnTheThreadCountsAsItsProcessorTime()
    {
        var watch = ThreadCpuWatch.StartNew();
        var spins = 0L;
        while (watch.Clock < TimeSpan.FromMilliseconds(300))
        {
            spins++;
        }

        var cpu = watch.Cpu;
        var clock = watch.Clock;

        // The thread can't use more processor time than has passed, plus a
        // tick of the OS's coarser count (10 ms on Linux, about 16 ms on
        // Windows). The lower bound is a tenth, not a half: on a machine
        // busy enough to give the spinning thread a quarter of a processor,
        // a half would fail, and a wrong field or unit is off by far more.
        Assert.True(spins > 0);
        Assert.True(cpu <= clock + TimeSpan.FromMilliseconds(20), $"{cpu} of processor time in {clock}.");
        Assert.True(cpu >= clock / 10, $"Only {cpu} of processor time in {clock}.");
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Wolfden.Index.Tests;

/// <summary>
/// Measures the processor time the current thread uses (user and kernel),
/// alongside the time by the clock. On a busy machine the clock time of
/// work grows with everything else running there, and processor time hardly
/// does, so a bound on it catches work that got slower, not a machine that
/// was busy (#248). Use it only for work that runs on the calling thread,
/// as the check and index rebuild do.
/// </summary>
internal sealed class ThreadCpuWatch
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _start = Now();

    /// <summary>The current thread's processor time since the watch started or restarted.</summary>
    public TimeSpan Cpu => Now() - _start;

    /// <summary>The time by the clock since the watch started or restarted.</summary>
    public TimeSpan Clock => _clock.Elapsed;

    public static ThreadCpuWatch StartNew() => new();

    public void Restart()
    {
        _start = Now();
        _clock.Restart();
    }

    public override string ToString() => $"{Cpu.TotalSeconds:F1} s of processor time, {Clock.TotalSeconds:F1} s by the clock";

    private static TimeSpan Now()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user))
            {
                throw new InvalidOperationException($"GetThreadTimes failed ({Marshal.GetLastPInvokeError()}).");
            }

            // Both in 100-nanosecond units, the same as TimeSpan ticks.
            return TimeSpan.FromTicks(kernel + user);
        }

        if (OperatingSystem.IsLinux())
        {
            // Fields 14 and 15 of the thread's stat, after the name in
            // parentheses: user and kernel time, in clock ticks of 1/100 s.
            var stat = File.ReadAllText("/proc/thread-self/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            var ticks = long.Parse(fields[11], CultureInfo.InvariantCulture) + long.Parse(fields[12], CultureInfo.InvariantCulture);
            return TimeSpan.FromTicks(ticks * (TimeSpan.TicksPerSecond / 100));
        }

        // macOS: CLOCK_THREAD_CPUTIME_ID is 16.
        if (clock_gettime(16, out var time) != 0)
        {
            throw new InvalidOperationException($"clock_gettime failed ({Marshal.GetLastPInvokeError()}).");
        }

        return TimeSpan.FromTicks(time.Seconds * TimeSpan.TicksPerSecond + time.Nanoseconds / 100);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("libc", SetLastError = true)]
    private static extern int clock_gettime(int clock, out TimeSpec time);
}

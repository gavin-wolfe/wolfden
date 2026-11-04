using System.Diagnostics;
using System.Text;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// A real separate process saving a file in place, as a program does: it opens
/// the file to write, letting others read, write, and delete, cuts the old text,
/// writes the first part, and stalls. It writes the rest when told to, or after
/// a time. Windows only, since that's the only system a write can refuse it on;
/// it runs in Windows PowerShell, which every Windows machine has.
/// </summary>
internal sealed class StalledWriter : IAsyncDisposable
{
    private const string Script = """
        $utf8 = [Text.Encoding]::UTF8
        $file = [IO.File]::Open($env:WOLFDEN_TEST_PATH, 'Open', 'Write', 'ReadWrite,Delete')
        $file.SetLength(0)
        $first = [Convert]::FromBase64String($env:WOLFDEN_TEST_FIRST)
        $file.Write($first, 0, $first.Length)
        $file.Flush()
        [Console]::Out.WriteLine('stalled')
        [Console]::Out.Flush()
        if ($env:WOLFDEN_TEST_STALL_MS) { Start-Sleep -Milliseconds ([int]$env:WOLFDEN_TEST_STALL_MS) } else { [void][Console]::In.ReadLine() }
        $rest = [Convert]::FromBase64String($env:WOLFDEN_TEST_REST)
        $file.Write($rest, 0, $rest.Length)
        $file.Dispose()
        """;

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private readonly Process _process;

    private StalledWriter(Process process) => _process = process;

    /// <summary>Starts a save of <paramref name="whole"/> over the file at <paramref name="full"/>, and returns once it has stalled after <paramref name="firstPart"/> characters. With <paramref name="stallMs"/>, it goes on by itself after that long.</summary>
    public static async Task<StalledWriter> StartAsync(string full, string whole, int firstPart, int? stallMs = null)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", Script })
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["WOLFDEN_TEST_PATH"] = full;
        start.Environment["WOLFDEN_TEST_FIRST"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(whole[..firstPart]));
        start.Environment["WOLFDEN_TEST_REST"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(whole[firstPart..]));
        if (stallMs is { } ms)
        {
            start.Environment["WOLFDEN_TEST_STALL_MS"] = ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var process = Process.Start(start)!;
        var writer = new StalledWriter(process);
        using var bound = new CancellationTokenSource(Bound);
        string? line;
        try
        {
            line = await process.StandardOutput.ReadLineAsync(bound.Token);
        }
        catch (OperationCanceledException)
        {
            line = null;
        }

        if (line != "stalled")
        {
            var why = process.HasExited ? await process.StandardError.ReadToEndAsync(CancellationToken.None) : "it didn't answer in time";
            await writer.DisposeAsync();
            Assert.Fail($"The writer process didn't get as far as stalling: {why}");
        }

        return writer;
    }

    /// <summary>Lets it write the rest, if it's waiting to be told, and waits until it has finished and closed the file.</summary>
    public async Task FinishAsync()
    {
        try
        {
            await _process.StandardInput.WriteLineAsync();
            await _process.StandardInput.FlushAsync();
        }
        catch (IOException)
        {
            // A timed stall that has finished already isn't listening.
        }

        using var bound = new CancellationTokenSource(Bound);
        await _process.WaitForExitAsync(bound.Token);
        Assert.Equal(0, _process.ExitCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            await _process.WaitForExitAsync(CancellationToken.None);
        }

        _process.Dispose();
    }
}

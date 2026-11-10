using System.Diagnostics;

namespace Wolfden.Accounts.Tests;

/// <summary>Folder links for tests: a junction on Windows (no special rights needed), a symbolic link elsewhere.</summary>
internal static class FolderLink
{
    /// <summary>Makes <paramref name="link"/> lead to the folder <paramref name="target"/>, or skips the test if this system won't.</summary>
    public static void Create(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "/c", "mklink", "/J", link, target })
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || !Directory.Exists(link))
            {
                Assert.Skip("This system didn't make a junction.");
            }

            return;
        }

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"This system didn't make a folder link: {e.Message}");
        }
    }
}

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Wolfden.Core.Structure;

namespace Wolfden.Mcp.Tests;

/// <summary>Real files and folders that can't be read while a test runs.</summary>
internal static class LockedFile
{
    /// <summary>
    /// Holds the file open with no sharing until disposed. Where that
    /// doesn't stop reads (a file system with no locks), it takes away
    /// the file's permissions instead, and skips the test where that
    /// doesn't either, as for root.
    /// </summary>
    public static IDisposable Hold(string full)
    {
        var stream = new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (!CanRead(full))
        {
            return stream;
        }

        stream.Dispose();
        if (!OperatingSystem.IsWindows())
        {
            var cleanup = WithoutPermissions(full);
            if (!CanRead(full))
            {
                return cleanup;
            }

            cleanup.Dispose();
        }

        Assert.Skip("This user can read a file held open with no sharing and with no permissions.");
        return null!;
    }

    /// <summary>
    /// Makes the folder one the user may not list until disposed, or skips
    /// the test where that doesn't stop listing, as for root, but not on
    /// Windows CI, where a skip would hide lost coverage (#248).
    /// </summary>
    public static IDisposable Unlistable(string full)
    {
        var cleanup = OperatingSystem.IsWindows() ? DenyListing(full) : WithoutPermissions(full);
        if (!FileSystemDenSource.CanList(full))
        {
            return cleanup;
        }

        cleanup.Dispose();
        Assert.False(
            OperatingSystem.IsWindows() && string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase),
            "On Windows CI, a folder with a deny rule for listing could still be listed, so this test would have been skipped.");
        Assert.Skip("This user can list a folder without permission to list it.");
        return null!;
    }

    private static bool CanRead(string full)
    {
        try
        {
            File.ReadAllBytes(full);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Cleanup DenyListing(string full)
    {
        var folder = new DirectoryInfo(full);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = folder.GetAccessControl();
        security.AddAccessRule(rule);
        folder.SetAccessControl(security);
        return new Cleanup(() =>
        {
            var restored = folder.GetAccessControl();
            restored.RemoveAccessRuleSpecific(rule);
            folder.SetAccessControl(restored);
        });
    }

    [UnsupportedOSPlatform("windows")]
    private static Cleanup WithoutPermissions(string full)
    {
        var mode = File.GetUnixFileMode(full);
        File.SetUnixFileMode(full, UnixFileMode.None);
        return new Cleanup(() => File.SetUnixFileMode(full, mode));
    }

    private sealed class Cleanup(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

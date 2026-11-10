namespace Wolfden.Accounts.Tests;

/// <summary>An installation folder under the system's temporary folder, deleted on dispose. Tests never use the real one.</summary>
public sealed class TempHome : IDisposable
{
    public TempHome()
    {
        Root = Path.Combine(Path.GetTempPath(), "wolfden-accounts-tests", Guid.NewGuid().ToString("N"));
        Folder = new InstallationFolder(Path.Combine(Root, "home"), InstallationSource.Option);
    }

    /// <summary>The temporary folder that holds the installation (which isn't created until something writes).</summary>
    public string Root { get; }

    /// <summary>The installation.</summary>
    public InstallationFolder Folder { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort; the OS cleans its temp folder.
        }
    }
}

using Wolfden.Core.Checks;
using Wolfden.Core.Structure;

namespace Wolfden.Core.Tests;

/// <summary>
/// <see cref="TrackingDenSource"/> tells "not there" from "there but can't be
/// read" (#284). Reads through it still count as absent (#112, #116).
/// </summary>
public sealed class TrackingDenSourceTests
{
    private const string Ilse = "universes/starfall/series/long-dark/entities/characters/ilse-tarrow.md";
    private const string Characters = "universes/starfall/series/long-dark/entities/characters";

    private static MemoryDenSource Den() => MemoryDenSource.FromDirectory(MemoryDenSource.StarfallRoot);

    [Theory]
    [InlineData("locked")]
    [InlineData("denied")]
    public void AFileThatsThereButCantBeReadIsAbsentAndRemembered(string failure)
    {
        IDenSource source = new TrackingDenSource(new FailingSource(Den(), Ilse, failure));

        Assert.Null(source.TryReadFile(Ilse));
        Assert.Null(source.TryReadListedFile(Ilse));

        Assert.Equal([new UnreadablePath(Ilse, IsFolder: false)], ((TrackingDenSource)source).Unreadable);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("folder-deleted")]
    [InlineData("locked-then-gone")]
    public void AFileDeletedBetweenListingAndReadingIsntAFailure(string failure)
    {
        IDenSource source = new TrackingDenSource(new FailingSource(Den(), Ilse, failure));

        Assert.Null(source.TryReadListedFile(Ilse));

        Assert.Empty(((TrackingDenSource)source).Unreadable);
    }

    [Fact]
    public void AFileThatIsntThereIsntAFailure()
    {
        IDenSource source = new TrackingDenSource(Den());

        Assert.Null(source.TryReadFile("universes/starfall/entities/characters/nobody.md"));

        Assert.Empty(((TrackingDenSource)source).Unreadable);
    }

    [Fact]
    public void AFolderThatCantBeListedIsEmptyAndRemembered()
    {
        var source = new TrackingDenSource(new UnlistableSource(Den(), Characters));

        Assert.Empty(source.GetFiles(Characters));
        Assert.Empty(source.GetDirectories(Characters));
        Assert.False(source.FolderExists(Characters));

        Assert.Equal([new UnreadablePath(Characters, IsFolder: true)], source.Unreadable);
    }

    [Fact]
    public void AFileLookedUpInAFolderThatCantBeListedRemembersTheFolder()
    {
        var source = new TrackingDenSource(new UnlistableSource(Den(), Characters));

        Assert.False(source.FileExists(Ilse));

        Assert.Equal([new UnreadablePath(Characters, IsFolder: true)], source.Unreadable);
    }

    [Fact]
    public void AnEmptyFolderThatCanBeListedIsntAFailure()
    {
        var source = new TrackingDenSource(Den());

        Assert.Empty(source.GetFiles("universes/starfall/no-such-folder"));

        Assert.Empty(source.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void CheckReportsAFolderThatCantBeListed()
    {
        var check = DenCheck.Run(new UnlistableSource(Den(), Characters));

        var found = Assert.Single(check.Diagnostics, d => d.Diagnostic.RuleId == "F-GEN-5");
        Assert.Equal(Characters, found.Path);
        Assert.Equal(Documents.DiagnosticSeverity.Error, found.Diagnostic.Severity);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void ACheckOfOneUniverseReportsOnlyItsOwnUnreadableFiles()
    {
        const string inDrift = "universes/drift/universe.md";
        var den = new FailingSource(Den(), inDrift, "locked");

        Assert.Contains(DenCheck.Run(den).Diagnostics, d => d.Path == inDrift && d.Diagnostic.RuleId == "F-GEN-5");
        Assert.DoesNotContain(DenCheck.Run(den, "starfall").Diagnostics, d => d.Diagnostic.RuleId == "F-GEN-5");
    }

    /// <summary>A den source that lists a file but fails to read it, as each failure does on disk.</summary>
    private sealed class FailingSource(IDenSource inner, string path, string failure) : IDenSource
    {
        public bool FileExists(string p) => (p != path || failure != "locked-then-gone") && inner.FileExists(p);

        public byte[] ReadFile(string p)
        {
            if (p != path)
            {
                return inner.ReadFile(p);
            }

            throw failure switch
            {
                "deleted" => new FileNotFoundException("Could not find file.", p),
                "folder-deleted" => new DirectoryNotFoundException("Could not find a part of the path."),
                "denied" => new UnauthorizedAccessException("Access to the path is denied."),
                _ => new IOException("The process cannot access the file because it is being used by another process."),
            };
        }

        public IEnumerable<string> GetDirectories(string p) => inner.GetDirectories(p);

        public IEnumerable<string> GetFiles(string p) => inner.GetFiles(p);
    }

    /// <summary>A den source with a folder that's in its parent's listing, but that can't be listed itself, as on disk.</summary>
    private sealed class UnlistableSource(IDenSource inner, string folder) : IDenSource
    {
        private bool In(string path) => path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal);

        public bool FileExists(string p) => !In(p) && inner.FileExists(p);

        public byte[] ReadFile(string p) => In(p) ? throw new UnauthorizedAccessException("Access to the path is denied.") : inner.ReadFile(p);

        public IEnumerable<string> GetDirectories(string p) => In(p) ? [] : inner.GetDirectories(p);

        public IEnumerable<string> GetFiles(string p) => In(p) ? [] : inner.GetFiles(p);

        public bool FolderExists(string p) => !In(p) && inner.FolderExists(p);

        public bool CannotList(string p) => p == folder;
    }
}

using System.Text;
using Wolfden.Core.Checks;
using Wolfden.Core.Structure;

namespace Wolfden.Core.Tests;

/// <summary>
/// Views read a file shared with programs writing it, so only a lock that keeps
/// others from reading hides a note. Reads a write builds on don't share writing,
/// so they can't build on part of a save (#298). The holders here are in this
/// process, opened as another program would open the file, which makes no
/// difference to how the OS or .NET treats them.
/// </summary>
public sealed class SharedFileTests : IDisposable
{
    private const string Name = "note.md";
    private const string Whole = "---\nname: A note\n---\n\nThe whole of it.\n";

    private readonly string _root = Directory.CreateTempSubdirectory("wolfden-shared-").FullName;

    public SharedFileTests() => File.WriteAllText(Full, Whole);

    private string Full => Path.Combine(_root, Name);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Theory]
    [InlineData(FileAccess.Write, FileShare.Read)]
    [InlineData(FileAccess.ReadWrite, FileShare.Read)]
    [InlineData(FileAccess.Write, FileShare.ReadWrite)]
    [InlineData(FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)]
    [Trait("Scenario", "SC-CHECK-64")]
    public void AViewReadsAFileAProgramIsWritingAndLettingOthersRead(FileAccess access, FileShare share)
    {
        using var holder = new FileStream(Full, FileMode.Open, access, share);
        var tracked = new TrackingDenSource(FileSystemDenSource.ForViews(_root));

        Assert.Equal(Whole, Text(((IDenSource)tracked).TryReadFile(Name)!));
        Assert.Empty(tracked.Unreadable);
    }

    [Theory]
    [InlineData(FileAccess.Write, FileShare.Read)]
    [InlineData(FileAccess.ReadWrite, FileShare.Read)]
    [InlineData(FileAccess.Write, FileShare.ReadWrite)]
    [InlineData(FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)]
    [Trait("Scenario", "SC-MCP-43")]
    public void AReadForAWriteIsRefusedWhileAProgramHasTheFileOpenToWriteOnWindows(FileAccess access, FileShare share)
    {
        SkipUnlessWindows("Linux and macOS can't tell: any sharing is the same shared lock there.");
        using var holder = new FileStream(Full, FileMode.Open, access, share);
        var tracked = new TrackingDenSource(new FileSystemDenSource(_root));

        Assert.Null(((IDenSource)tracked).TryReadFile(Name));
        Assert.Equal([new UnreadablePath(Name, IsFolder: false)], tracked.Unreadable);
    }

    [Theory]
    [InlineData(FileShare.Read)]
    [InlineData(FileShare.ReadWrite | FileShare.Delete)]
    [Trait("Scenario", "SC-MCP-43")]
    public void AProgramOnlyReadingAFileDoesntStopAReadForAWrite(FileShare share)
    {
        using var holder = new FileStream(Full, FileMode.Open, FileAccess.Read, share);

        Assert.Equal(Whole, Text(new FileSystemDenSource(_root).ReadFile(Name)));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void AProgramHoldingAFileWithNoSharingStopsBothKindsOfRead()
    {
        using var holder = new FileStream(Full, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        foreach (var source in new[] { FileSystemDenSource.ForViews(_root), new FileSystemDenSource(_root) })
        {
            var tracked = new TrackingDenSource(source);
            Assert.Null(((IDenSource)tracked).TryReadFile(Name));
            Assert.Equal([new UnreadablePath(Name, IsFolder: false)], tracked.Unreadable);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void AProgramLettingOthersWriteButNotReadStopsAViewOnWindows()
    {
        SkipUnlessWindows("Linux and macOS have no lock that lets writers in but not readers.");
        using var holder = new FileStream(Full, FileMode.Open, FileAccess.ReadWrite, FileShare.Write);
        var tracked = new TrackingDenSource(FileSystemDenSource.ForViews(_root));

        Assert.Null(((IDenSource)tracked).TryReadFile(Name));
        Assert.Equal([new UnreadablePath(Name, IsFolder: false)], tracked.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void AnExclusiveLockOnTheFilesBytesStopsAViewOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Locks on bytes are advisory on Linux and macOS, so a read isn't stopped by one.");
            return;
        }

        using var holder = new FileStream(Full, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        holder.Lock(0, Whole.Length);
        var tracked = new TrackingDenSource(FileSystemDenSource.ForViews(_root));

        // This one opens fine: it's the read that fails.
        Assert.Null(((IDenSource)tracked).TryReadFile(Name));
        Assert.Equal([new UnreadablePath(Name, IsFolder: false)], tracked.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-43")]
    public void AViewDuringASaveSeesPartOfItAndTheNextReadSeesItAll()
    {
        var view = FileSystemDenSource.ForViews(_root);
        byte[] during;
        using (var saving = new FileStream(Full, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            saving.SetLength(0);
            saving.Write(Encoding.UTF8.GetBytes(Whole[..10]));
            saving.Flush();

            during = view.ReadFile(Name);
            saving.Write(Encoding.UTF8.GetBytes(Whole[10..]));
        }

        Assert.Equal(Whole[..10], Text(during));
        Assert.Equal(Whole, Text(view.ReadFile(Name)));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-64")]
    public void CheckDoesntListAHeldNoteWhenItsSharedAndDoesWhenItIsnt()
    {
        var den = Path.Combine(_root, "den");
        CopyStarfall(den);
        var ilse = Path.Combine(den, "universes", "starfall", "series", "long-dark", "entities", "characters", "ilse-tarrow.md");
        using (new FileStream(ilse, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            Assert.DoesNotContain(DenCheck.Run(den).Diagnostics, d => d.Diagnostic.RuleId == "F-GEN-5");
        }

        using (new FileStream(ilse, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Contains(DenCheck.Run(den).Diagnostics, d => d.Diagnostic.RuleId == "F-GEN-5");
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-43")]
    public void ChecksFixDoesntAddAnIdToANoteThatsOpenToWriteOnWindows()
    {
        SkipUnlessWindows("Linux and macOS can't tell that a file is open to write.");
        var den = Path.Combine(_root, "den");
        CopyStarfall(den);
        var ilse = Path.Combine(den, "universes", "starfall", "series", "long-dark", "entities", "characters", "ilse-tarrow.md");
        var text = File.ReadAllText(ilse);
        var withoutId = string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').Where(l => !l.StartsWith("id:", StringComparison.Ordinal)));
        File.WriteAllText(ilse, withoutId);

        IReadOnlyList<IdFixResult> fixes;
        using (new FileStream(ilse, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            fixes = IdFix.Run(den);
        }

        Assert.DoesNotContain(fixes, f => f.Outcome == IdFixOutcome.Added && f.Path.EndsWith("ilse-tarrow.md", StringComparison.Ordinal));
        Assert.Equal(withoutId, File.ReadAllText(ilse));
    }

    private static void CopyStarfall(string to)
    {
        foreach (var file in Directory.EnumerateFiles(MemoryDenSource.StarfallRoot, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(MemoryDenSource.StarfallRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void SkipUnlessWindows(string why)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(why);
        }
    }
}

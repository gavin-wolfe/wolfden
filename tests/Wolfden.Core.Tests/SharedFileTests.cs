using System.Text;
using Wolfden.Core.Checks;
using Wolfden.Core.Structure;

namespace Wolfden.Core.Tests;

/// <summary>
/// Reads share a file with programs writing to it, so only a truly exclusive
/// lock refuses one (#298). The holders here are real, opened as another
/// program would open the file; they're in this process, which makes no
/// difference to how the OS or .NET treats them.
/// </summary>
public sealed class SharedFileTests : IDisposable
{
    private const string Name = "note.md";
    private const string Whole = "---\nname: A note\n---\n\nThe whole of it.\n";

    private readonly string _root = Directory.CreateTempSubdirectory("wolfden-shared-").FullName;

    public SharedFileTests() => File.WriteAllText(Full, Whole);

    private string Full => Path.Combine(_root, Name);

    private FileSystemDenSource Source => new(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Theory]
    [InlineData(FileAccess.Write, FileShare.Read)]
    [InlineData(FileAccess.ReadWrite, FileShare.Read)]
    [InlineData(FileAccess.Write, FileShare.ReadWrite)]
    [InlineData(FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)]
    [Trait("Scenario", "SC-CHECK-64")]
    public void AProgramWritingAFileAndLettingOthersReadItDoesntStopARead(FileAccess access, FileShare share)
    {
        using var holder = new FileStream(Full, FileMode.Open, access, share);
        var tracked = new TrackingDenSource(Source);

        Assert.Equal(Whole, Text(((IDenSource)tracked).TryReadFile(Name)!));
        Assert.Empty(tracked.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void AProgramHoldingAFileWithNoSharingStillStopsARead()
    {
        using var holder = new FileStream(Full, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var tracked = new TrackingDenSource(Source);

        Assert.Null(((IDenSource)tracked).TryReadFile(Name));
        Assert.Equal([new UnreadablePath(Name, IsFolder: false)], tracked.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void AProgramLettingOthersWriteButNotReadStillStopsAReadOnWindows()
    {
        SkipUnlessWindows("Linux and macOS have no lock that lets writers in but not readers.");
        using var holder = new FileStream(Full, FileMode.Open, FileAccess.ReadWrite, FileShare.Write);
        var tracked = new TrackingDenSource(Source);

        Assert.Null(((IDenSource)tracked).TryReadFile(Name));
        Assert.Equal([new UnreadablePath(Name, IsFolder: false)], tracked.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void AnExclusiveLockOnTheFilesBytesStillStopsAReadOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Locks on bytes are advisory on Linux and macOS, so a read isn't stopped by one.");
            return;
        }

        using var holder = new FileStream(Full, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        holder.Lock(0, Whole.Length);
        var tracked = new TrackingDenSource(Source);

        // This one opens fine: it's the read that fails.
        Assert.Null(((IDenSource)tracked).TryReadFile(Name));
        Assert.Equal([new UnreadablePath(Name, IsFolder: false)], tracked.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AReadDuringASaveSeesPartOfItAndTheNextReadSeesItAll()
    {
        byte[] during;
        using (var saving = new FileStream(Full, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            saving.SetLength(0);
            saving.Write(Encoding.UTF8.GetBytes(Whole[..10]));
            saving.Flush();

            during = Source.ReadFile(Name);
            saving.Write(Encoding.UTF8.GetBytes(Whole[10..]));
        }

        Assert.Equal(Whole[..10], Text(during));
        Assert.Equal(Whole, Text(Source.ReadFile(Name)));
    }

    [Theory]
    [InlineData(FileAccess.Write, FileShare.Read, true)]
    [InlineData(FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, true)]
    [InlineData(FileAccess.Read, FileShare.Read, false)]
    [InlineData(FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, false)]
    [Trait("Scenario", "SC-MCP-41")]
    public void OnlyWindowsCanTellThatAFileIsOpenForWriting(FileAccess access, FileShare share, bool writing)
    {
        using var holder = new FileStream(Full, FileMode.Open, access, share);

        Assert.Equal(writing && OperatingSystem.IsWindows(), Source.IsOpenForWriting(Name));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AFileNoOneHasOpenOrThatIsntThereIsntOpenForWriting()
    {
        Assert.False(Source.IsOpenForWriting(Name));
        Assert.False(Source.IsOpenForWriting("nobody.md"));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AWriteTakesAFileOpenForWritingOnceItStaysTheSame()
    {
        var den = new ScriptedSource(Whole, Whole);
        var tracked = new TrackingDenSource(den, forWrite: true);

        Assert.Equal(Whole, Text(((IDenSource)tracked).TryReadFile(Name)!));
        Assert.Empty(tracked.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AWriteTakesWhatASaveEndsUpAs()
    {
        var den = new ScriptedSource("---\nna", Whole, Whole);
        var tracked = new TrackingDenSource(den, forWrite: true);

        Assert.Equal(Whole, Text(((IDenSource)tracked).TryReadFile(Name)!));
        Assert.Empty(tracked.Unreadable);
        Assert.Empty(tracked.ChangedSinceRead());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AWriteCountsAFileThatKeepsChangingAsUnreadable()
    {
        var den = new ScriptedSource(Enumerable.Range(0, TrackingDenSource.SettleLooks + 1).Select(i => new string('x', i + 1)).ToArray());
        var tracked = new TrackingDenSource(den, forWrite: true);

        Assert.Null(((IDenSource)tracked).TryReadFile(Name));
        Assert.Equal([new UnreadablePath(Name, IsFolder: false)], tracked.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AWriteCountsAnEmptyFileOpenForWritingAsUnreadable()
    {
        var tracked = new TrackingDenSource(new ScriptedSource("", ""), forWrite: true);

        Assert.Null(((IDenSource)tracked).TryReadFile(Name));
        Assert.Equal([new UnreadablePath(Name, IsFolder: false)], tracked.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AFileAlreadyTakenIsntWaitedForAgain()
    {
        var den = new ScriptedSource(Whole, Whole, Whole, Whole);
        IDenSource tracked = new TrackingDenSource(den, forWrite: true);

        tracked.TryReadFile(Name);
        tracked.TryReadFile(Name);
        tracked.TryReadFile(Name);

        Assert.Equal(1, den.Asked);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void ACheckDoesntWaitOnAFileOpenForWriting()
    {
        var den = new ScriptedSource("---\nna", Whole);
        IDenSource tracked = new TrackingDenSource(den);

        Assert.Equal("---\nna", Text(tracked.TryReadFile(Name)!));
        Assert.Equal(0, den.Asked);
        Assert.Empty(((TrackingDenSource)tracked).ChangedSinceRead());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AWriteCanTellThatAFileItReadHasChangedOrGone()
    {
        const string other = "other.md";
        File.WriteAllText(Path.Combine(_root, other), "other");
        var tracked = new TrackingDenSource(Source, forWrite: true);
        IDenSource source = tracked;
        source.TryReadFile(Name);
        source.TryReadFile(other);
        Assert.Empty(tracked.ChangedSinceRead());

        File.AppendAllText(Full, "And the rest.\n");
        File.Delete(Path.Combine(_root, other));

        Assert.Equal([Name, other], tracked.ChangedSinceRead());
        Assert.Empty(tracked.Unreadable);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-64")]
    public void ACheckOfADenWithAHeldNoteDoesntListItWhenItsShared()
    {
        var den = Path.Combine(_root, "den");
        CopyStarfall(den);
        var ilse = Path.Combine(den, "universes", "starfall", "series", "long-dark", "entities", "characters", "ilse-tarrow.md");
        using (new FileStream(ilse, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            Assert.DoesNotContain(DenCheck.Run(new FileSystemDenSource(den)).Diagnostics, d => d.Diagnostic.RuleId == "F-GEN-5");
        }

        using (new FileStream(ilse, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Contains(DenCheck.Run(new FileSystemDenSource(den)).Diagnostics, d => d.Diagnostic.RuleId == "F-GEN-5");
        }
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

    /// <summary>A den of one file, open for writing, whose reads give each text in turn, then the last again.</summary>
    private sealed class ScriptedSource(params string[] reads) : IDenSource
    {
        private int _next;

        public int Asked { get; private set; }

        public bool IsOpenForWriting(string path)
        {
            Asked++;
            return true;
        }

        public bool FileExists(string path) => path == Name;

        public byte[] ReadFile(string path) => Encoding.UTF8.GetBytes(reads[Math.Min(_next++, reads.Length - 1)]);

        public IEnumerable<string> GetDirectories(string path) => [];

        public IEnumerable<string> GetFiles(string path) => [Name];
    }
}

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Wolfden.Core.Structure;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// A write that has to have seen every file is refused as a whole while a
/// den file is there but can't be read, or a folder can't be listed (#284).
/// Treating it as absent (#112) would let links in it break, and let a change
/// past a final book. The file is held open with no sharing, as another
/// program would hold it. That stops reads on Linux too: .NET takes an
/// advisory lock there, which its own reads honour.
/// </summary>
public sealed class LockedFileWriteTests : IDisposable
{
    private const string Book = "universes/dracula/books/dracula";
    private const string Swales = Book + "/entities/characters/mr-swales.md";
    private const string Outline = Book + "/outline.md";
    private const string Notes = "universes/dracula/entities/notes";
    private const string Note = Notes + "/whitby-dialect.md";
    private const string Aftermath = "universes/dracula/books/aftermath";
    private const string AftermathFile = Aftermath + "/book.md";
    private const string Memories = Aftermath + "/entities/notes/memories.md";

    private readonly TempDen _den = TempDen.Dracula();

    public LockedFileWriteTests()
    {
        _den.Write(Note,
            "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z1\ntype: note\nname: Whitby dialect\nstatus: draft\n---\n\n" +
            "See [[Mr. Swales]], [[Mr. Swales#Voice]], and [[the Count]].\n");
    }

    private DenTools Tools => new(_den.Root);

    public void Dispose() => _den.Dispose();

    public static TheoryData<string> DenWideWrites() =>
        ["rename_entity", "change_alias", "rename_section", "move_entity", "copy_entity", "create_entity"];

    private WriteResult Call(string tool, string swalesHash, string countHash) => Call(Tools, tool, swalesHash, countHash);

    private static WriteResult Call(DenTools tools, string tool, string swalesHash, string countHash) => tool switch
    {
        "rename_entity" => tools.RenameEntity("Mr. Swales", "Old Swales", swalesHash),
        "change_alias" => tools.ChangeAlias("Count Dracula", "the Count", countHash, newAlias: "the Nobleman"),
        "rename_section" => tools.RenameSection("Mr. Swales", "Voice", "Dialect", swalesHash),
        "move_entity" => tools.MoveEntity("Mr. Swales", swalesHash, keepEntry: true),
        "copy_entity" => tools.CopyEntity("Mr. Swales", "Young Swales"),
        "create_entity" => tools.CreateEntity("note", "Whitby weather"),
        _ => throw new ArgumentException(tool),
    };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [MemberData(nameof(DenWideWrites))]
    [Trait("Scenario", "SC-MCP-38")]
    public void ALockedNoteRefusesADenWideWriteAndNothingIsWritten(string tool)
    {
        var swales = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var count = Tools.GetEntity("Count Dracula").Entity!.Hash!;
        var before = Files();

        DenToolException refused;
        using (LockedFile.Hold(Full(Note)))
        {
            refused = Assert.Throws<DenToolException>(() => Call(tool, swales, count));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(Note, refused.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", refused.Message, StringComparison.Ordinal);
        Assert.Contains("close it there, then try again", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());

        // The same call goes through once the file is closed.
        Assert.True(Call(tool, swales, count).Changed);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void TheLinksInALockedNoteFollowOnceItsClosedAndTheWriteIsRepeated()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        using (LockedFile.Hold(Full(Note)))
        {
            Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", hash));
        }

        var result = Tools.RenameEntity("Mr. Swales", "Old Swales", hash);

        Assert.Contains(result.Effects, e => e.Contains("rewrote 2 link(s)", StringComparison.Ordinal));
        Assert.Contains("See [[Old Swales]], [[Old Swales#Voice]]", _den.Read(Note), StringComparison.Ordinal);
        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule == "F-LINK-8");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void AnOutlineIsNamedWhenItsTheOneLocked()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var before = Files();

        DenToolException refused;
        using (LockedFile.Hold(Full(Outline)))
        {
            refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", hash));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(Outline, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void ANoteLockedItselfKeepsUnknownEntityAndIsNamed()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var before = Files();

        DenToolException refused;
        using (LockedFile.Hold(Full(Swales)))
        {
            refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", hash));
        }

        Assert.Equal(ToolErrorCodes.UnknownEntity, refused.Code);
        Assert.Contains(Swales, refused.Message, StringComparison.Ordinal);
        Assert.Contains("which may be why", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void AWrongNameKeepsItsErrorAndAddsWhatCouldntBeRead()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var changes = Json("""[{"op": "set", "path": "status", "value": "canon"}]""");
        Assert.DoesNotContain("couldn't read", Assert.Throws<DenToolException>(() => Tools.UpdateFields("No Such Note", hash, changes)).Message, StringComparison.OrdinalIgnoreCase);

        using (LockedFile.Hold(Full(Note)))
        {
            var refused = Assert.Throws<DenToolException>(() => Tools.UpdateFields("No Such Note", hash, changes));

            Assert.Equal(ToolErrorCodes.UnknownEntity, refused.Code);
            Assert.Contains("No Such Note", refused.Message, StringComparison.Ordinal);
            Assert.Contains($"Couldn't read {Note} right now, which may be why", refused.Message, StringComparison.Ordinal);

            var book = Assert.Throws<DenToolException>(() => Tools.GetChapter("no-such-book"));
            Assert.Equal(ToolErrorCodes.UnknownScope, book.Code);
            Assert.Contains("which may be why", book.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(DenWideWrites))]
    [Trait("Scenario", "SC-MCP-38")]
    public void AFileThatBecomesUnreadableBeforeTheSaveRefusesTheWrite(string tool)
    {
        var swales = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var count = Tools.GetEntity("Count Dracula").Entity!.Hash!;
        var before = Files();
        var tools = new DenTools(_den.Root)
        {
            Store = new FailingReadStore(
                new FileSystemDenStore(_den.Root), Outline, read => read >= 2 ? new IOException("The process cannot access the file because it is being used by another process.") : null),
        };

        var refused = Assert.Throws<DenToolException>(() => Call(tools, tool, swales, count));

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(Outline, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());
    }

    [Theory]
    [InlineData("book")]
    [InlineData("series")]
    [Trait("Scenario", "SC-MCP-38")]
    public void ALockedSiblingRefusesANewBookOrSeries(string what)
    {
        WriteAftermath(withMemories: false);
        var before = Files();
        ContainerCreateResult Create() => what == "book" ? Tools.CreateBook("Fresh Book") : Tools.CreateSeries("Fresh Series");

        DenToolException refused;
        using (LockedFile.Hold(Full(AftermathFile)))
        {
            refused = Assert.Throws<DenToolException>(Create);
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(AftermathFile, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());

        Assert.NotEmpty(Create().Hash);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void AnotherBookNamedAfterALockedSiblingIsRefusedForTheLockNotAsMissing()
    {
        WriteAftermath(withMemories: false);

        using (LockedFile.Hold(Full(AftermathFile)))
        {
            var refused = Assert.Throws<DenToolException>(() => Tools.CreateBook("Fresh Book", after: "aftermath"));

            Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void APinnedSessionDoesntNameAFileInAnotherUniverse()
    {
        using var den = TempDen.Starfall();
        const string drift = "universes/drift/universe.md";
        var pinned = new DenTools(den.Root) { PinnedUniverse = "starfall" };
        var hash = pinned.GetEntity("Ilse Tarrow").Entity!.Hash!;

        using (LockedFile.Hold(Path.Combine(den.Root, drift.Replace('/', Path.DirectorySeparatorChar))))
        {
            var refused = Assert.Throws<DenToolException>(() => pinned.RenameEntity("Ilse Tarrow", "Ilse Marrow", hash));

            Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
            Assert.Contains("a file outside this universe", refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("drift", refused.Message, StringComparison.OrdinalIgnoreCase);

            var unpinned = Assert.Throws<DenToolException>(() => new DenTools(den.Root).RenameEntity("Ilse Tarrow", "Ilse Marrow", hash, universe: "starfall"));
            Assert.Contains(drift, unpinned.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void APositionInABookWhoseFileIsLockedSaysItCouldntBeRead()
    {
        WriteAftermath(withMemories: false);
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var before = Files();

        using (LockedFile.Hold(Full(AftermathFile)))
        {
            var refused = Assert.Throws<DenToolException>(() =>
                Tools.UpdateFields("Mr. Swales", hash, Json("""[{"op": "set", "path": "from", "value": "aftermath:1"}]""")));

            Assert.Equal(ToolErrorCodes.BadPosition, refused.Code);
            Assert.Contains($"Couldn't read {AftermathFile} right now, which may be why", refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void RemovingASectionSaysItsLinksInALockedNoteWerentCounted()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;

        using (LockedFile.Hold(Full(Note)))
        {
            var result = Tools.RemoveSection("Mr. Swales", 1, "Voice", hash);

            Assert.True(result.Changed);
            Assert.Contains(result.Effects, e => e.StartsWith($"Couldn't read {Note}, so any links to this section", StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void TheCheckToolReportsAUniverseWhoseFileIsLockedRatherThanUnknown()
    {
        const string universe = "universes/dracula/universe.md";

        using (LockedFile.Hold(Full(universe)))
        {
            var check = Tools.Check(universe: "dracula");

            Assert.Contains(check.Diagnostics, d => d.Rule == "F-GEN-5" && d.Path == universe);
            Assert.Equal(ToolErrorCodes.UnknownUniverse, Assert.Throws<DenToolException>(() => Tools.Check(universe: "no-such-universe")).Code);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-39")]
    public void ALockedNoteInAFrozenBookCantSkipTheFrozenRefusal()
    {
        WriteAftermath(withMemories: true);
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var before = Files();

        DenToolException refused;
        using (LockedFile.Hold(Full(Memories)))
        {
            refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", hash));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(Memories, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());

        // Once it's closed, the book's refusal comes as it always did.
        Assert.Equal(ToolErrorCodes.Frozen, Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", hash)).Code);
        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-39")]
    public void AFrozenBooksLockedBookFileCantSkipTheFrozenRefusal()
    {
        WriteAftermath(withMemories: true);
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var before = Files();

        DenToolException refused;
        using (LockedFile.Hold(Full(AftermathFile)))
        {
            refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", hash));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(AftermathFile, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-39")]
    public void ANoteInAFrozenBookWhoseBookFileIsLockedSaysSoAsWellAsNotFound()
    {
        WriteAftermath(withMemories: true);
        var hash = Tools.GetEntity("Memories").Entity!.Hash!;
        var before = Files();

        DenToolException refused;
        using (LockedFile.Hold(Full(AftermathFile)))
        {
            refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Memories", "Recollections", hash));
        }

        Assert.Equal(ToolErrorCodes.UnknownEntity, refused.Code);
        Assert.Contains(AftermathFile, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-39")]
    public void AChapterDefaultCantReachAFrozenBookWhoseBookFileIsLocked()
    {
        WriteAftermath(withMemories: false);
        var read = Tools.GetChapterDefaults();
        var changes = JsonDocument.Parse("""[{"op": "add", "field": "pov", "value": "[[mr-swales]]"}]""").RootElement.Clone();
        var before = Files();

        DenToolException refused;
        using (LockedFile.Hold(Full(AftermathFile)))
        {
            refused = Assert.Throws<DenToolException>(() => Tools.UpdateChapterDefaults(read.Hash, changes));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(AftermathFile, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());

        Assert.Equal(ToolErrorCodes.Frozen, Assert.Throws<DenToolException>(() => Tools.UpdateChapterDefaults(read.Hash, changes)).Code);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void AFolderThatCantBeListedRefusesADenWideWriteAndIsNamed()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var before = Files();

        DenToolException refused;
        using (LockedFile.Unlistable(Full(Notes)))
        {
            refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", hash));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(Notes, refused.Message, StringComparison.Ordinal);
        Assert.Contains("permissions", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void AWriteToOneFileIsntStoppedByALockedNoteElsewhere()
    {
        using (LockedFile.Hold(Full(Note)))
        {
            Assert.NotEmpty(Tools.AddChapter("dracula", title: "A new chapter").Hash);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void AFileDeletedBetweenListingAndReadingIsntAFailure()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var tools = new DenTools(_den.Root)
        {
            Store = new FailingReadStore(new FileSystemDenStore(_den.Root), Note, _ => new FileNotFoundException("Could not find file.", Note)),
        };

        var result = tools.RenameEntity("Mr. Swales", "Old Swales", hash);

        Assert.True(result.Changed);
        Assert.Contains("\nname: Old Swales\n", _den.Read(Swales), StringComparison.Ordinal);
        Assert.Contains("See [[Mr. Swales]]", _den.Read(Note), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void TheCheckToolListsALockedNoteAsAnError()
    {
        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule == "F-GEN-5");

        using (LockedFile.Hold(Full(Note)))
        {
            foreach (var check in new[] { Tools.Check(), Tools.Check(universe: "dracula"), Tools.Check(path: Note) })
            {
                var found = Assert.Single(check.Diagnostics, d => d.Rule == "F-GEN-5");
                Assert.Equal(Note, found.Path);
                Assert.Equal("error", found.Severity);
                Assert.Null(found.Line);
                Assert.Contains("couldn't be read", found.Message, StringComparison.Ordinal);
                Assert.True(check.Errors >= 1);
            }
        }

        Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule == "F-GEN-5");
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-63")]
    public void TheCheckToolListsAFolderThatCantBeListed()
    {
        using (LockedFile.Unlistable(Full(Notes)))
        {
            var found = Assert.Single(Tools.Check().Diagnostics, d => d.Rule == "F-GEN-5");
            Assert.Equal(Notes, found.Path);
            Assert.Equal("error", found.Severity);
            Assert.Contains("couldn't be listed", found.Message, StringComparison.Ordinal);
        }
    }

    private void WriteAftermath(bool withMemories)
    {
        _den.Write(AftermathFile, "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z2\nname: Aftermath\nstatus: final\nchapters: [1, 2]\n---\n");
        if (withMemories)
        {
            _den.Write(Memories, "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z3\ntype: note\nname: Memories\nstatus: draft\n---\n\nRecall [[Mr. Swales]].\n");
        }
    }

    private string Full(string path) => Path.Combine(_den.Root, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Every file of the den but the index, with its bytes.</summary>
    private Dictionary<string, string> Files() =>
        Directory.EnumerateFiles(_den.Root, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(_den.Root, f).StartsWith(".wolfden", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetRelativePath(_den.Root, f), File.ReadAllText);

    /// <summary>A store whose file <paramref name="failing"/> is still listed, but whose reads fail as <paramref name="failure"/> says, given which read it is, from 1.</summary>
    private sealed class FailingReadStore(IDenStore inner, string failing, Func<int, Exception?> failure) : IDenStore
    {
        private int _reads;

        private IDenSource Reads => inner;

        public bool IsReadOnly => inner.IsReadOnly;

        public bool FileExists(string path) => inner.FileExists(path);

        public byte[] ReadFile(string path)
        {
            if (path == failing && failure(Interlocked.Increment(ref _reads)) is { } e)
            {
                throw e;
            }

            return inner.ReadFile(path);
        }

        public IEnumerable<string> GetDirectories(string path) => inner.GetDirectories(path);

        public IEnumerable<string> GetFiles(string path) => inner.GetFiles(path);

        public bool FolderExists(string path) => Reads.FolderExists(path);

        public bool CannotList(string path) => Reads.CannotList(path);

        public IEnumerable<string> GetFolderLinks(string path) => Reads.GetFolderLinks(path);

        public void WriteFile(string path, byte[] bytes, DenWriteMode mode = DenWriteMode.CreateOrReplace) => inner.WriteFile(path, bytes, mode);

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public void MoveFile(string from, string to) => inner.MoveFile(from, to);

        public void MoveFolder(string from, string to) => inner.MoveFolder(from, to);

        public void DeleteFolder(string path) => inner.DeleteFolder(path);
    }
}

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

using System.Text;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// Reads share a file with a program writing to it (#298), so a read can land
/// in the middle of its save. A view corrects itself on the next read. These
/// show what a write does: it doesn't build on what it can tell is part of a
/// save, and it's refused if what it read has changed by the time it saves.
/// The save is a real one, a program's handle writing in place, up to the
/// tests that say otherwise.
/// </summary>
public sealed class SharedFileWriteTests : IDisposable
{
    private const string Swales = "universes/dracula/books/dracula/entities/characters/mr-swales.md";
    private const string Notes = "universes/dracula/entities/notes";

    /// <summary>A note that needs rewriting by every write below, but only in the part still to come.</summary>
    private const string Note = Notes + "/whitby-dialect.md";

    /// <summary>A note that links to nothing the writes below change.</summary>
    private const string Elsewhere = Notes + "/whitby-weather-almanac.md";

    private const string Head = "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z1\ntype: note\nname: Whitby dialect\nstatus: draft\n---\n\nAll about the dialect. ";
    private const string Tail = "See [[Mr. Swales]], [[Mr. Swales#Voice]], and [[the Count]].\n";

    private readonly TempDen _den = TempDen.Dracula();

    public SharedFileWriteTests()
    {
        _den.Write(Note, Head + Tail);
        _den.Write(Elsewhere, "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z2\ntype: note\nname: Weather almanac\nstatus: draft\n---\n\nFog, mostly.\n");
    }

    private DenTools Tools => new(_den.Root);

    public void Dispose() => _den.Dispose();

    public static TheoryData<string> DenWideWrites() =>
        ["rename_entity", "change_alias", "rename_section", "move_entity", "copy_entity", "create_entity"];

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

    private (string Swales, string Count) Hashes() => (Tools.GetEntity("Mr. Swales").Entity!.Hash!, Tools.GetEntity("Count Dracula").Entity!.Hash!);

    [Theory]
    [MemberData(nameof(DenWideWrites))]
    [Trait("Scenario", "SC-MCP-38")]
    public void ADenWideWriteGoesAheadWhileAnotherProgramIsHoldingANoteItCanRead(string tool)
    {
        var (swales, count) = Hashes();

        WriteResult done;
        using (new FileStream(Full(Elsewhere), FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            done = Call(Tools, tool, swales, count);
        }

        Assert.True(done.Changed);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void ANoteHeldForWritingIsSeenSoAPageAndACheckAreRight()
    {
        using (new FileStream(Full(Note), FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            Assert.Contains("Mr. Swales", Tools.GetEntity("Whitby dialect").Entity!.Lead!, StringComparison.Ordinal);
            Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule == "F-GEN-5");
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void ALockWithNoSharingStillRefusesAndIsStillListed()
    {
        var (swales, _) = Hashes();
        var before = Files();

        using (LockedFile.Hold(Full(Note)))
        {
            var refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", swales));
            Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
            Assert.Contains(Note, refused.Message, StringComparison.Ordinal);
            Assert.Contains(Tools.Check().Diagnostics, d => d.Rule == "F-GEN-5" && d.Path == Note);
        }

        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void ANoteARenameMustRewriteIsntOverwrittenWhileAnotherProgramHasItOpenOnWindows()
    {
        SkipUnlessWindows("Only Windows keeps an open file from being replaced.");
        var (swales, _) = Hashes();
        var before = Files();

        using (new FileStream(Full(Note), FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            var refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", swales));
            Assert.Equal(ToolErrorCodes.WriteFailed, refused.Code);
            Assert.Contains("another program has it open", refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AnEditBasedOnTheWholeFileIsRefusedInTheMiddleOfASave()
    {
        var hash = Tools.GetEntity("Whitby dialect").Entity!.Hash!;

        using (var saving = BeginSave(Note))
        {
            var refused = Assert.Throws<DenToolException>(() => Tools.ReplaceLead("Whitby dialect", "New text.\n", hash));
            Assert.Equal(ToolErrorCodes.StaleFile, refused.Code);

            // Nothing was written over the save, which goes on to finish.
            Assert.Equal(Head, Read(Note));
            saving.Write(Encoding.UTF8.GetBytes(Tail));
        }

        Assert.Equal(Head + Tail, Read(Note));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void AnEditBasedOnAPageShownMidSaveIsRefusedOnceTheSaveGoesOn()
    {
        string hash;
        using (var saving = BeginSave(Note))
        {
            var shown = Tools.GetEntity("Whitby dialect").Entity!;
            Assert.DoesNotContain("Mr. Swales", shown.Lead!, StringComparison.Ordinal);
            hash = shown.Hash!;
            saving.Write(Encoding.UTF8.GetBytes(Tail));
        }

        // The page corrects itself on the next read.
        Assert.Contains("Mr. Swales", Tools.GetEntity("Whitby dialect").Entity!.Lead!, StringComparison.Ordinal);

        var refused = Assert.Throws<DenToolException>(() => Tools.ReplaceLead("Whitby dialect", "New text.\n", hash));
        Assert.Equal(ToolErrorCodes.StaleFile, refused.Code);
        Assert.Equal(Head + Tail, Read(Note));
    }

    [Theory]
    [MemberData(nameof(DenWideWrites))]
    [Trait("Scenario", "SC-MCP-41")]
    public void AWriteIsRefusedIfWhatItReadWasPartOfASaveThatHasGoneOnSince(string tool)
    {
        // As on Linux and macOS, nothing says a save is under way: only that the file changes.
        var (swales, count) = Hashes();
        _den.Write(Note, Head);
        var before = Files();
        var tools = new DenTools(_den.Root) { BeforeSaveCheck = () => _den.Write(Note, Head + Tail) };

        var refused = Assert.Throws<DenToolException>(() => Call(tools, tool, swales, count));

        Assert.Equal(ToolErrorCodes.StaleFile, refused.Code);
        Assert.Contains(Note, refused.Message, StringComparison.Ordinal);
        before[Rel(Note)] = Head + Tail;
        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void TheSameWriteGoesThroughOnceTheSaveHasFinishedAndTheLinksFollow()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        _den.Write(Note, Head);
        var midSave = new DenTools(_den.Root) { BeforeSaveCheck = () => _den.Write(Note, Head + Tail) };
        Assert.Throws<DenToolException>(() => midSave.RenameEntity("Mr. Swales", "Old Swales", hash));

        Tools.RenameEntity("Mr. Swales", "Old Swales", hash);

        Assert.Contains("[[Old Swales]]", Read(Note), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public async Task ANoteStillBeingWrittenToRefusesADenWideWriteOnWindows()
    {
        SkipUnlessWindows("Only Windows can tell that a file is open for writing.");
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        using var stop = new CancellationTokenSource();
        var saving = BeginSave(Elsewhere);
        var writer = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                saving.Write("more "u8);
                saving.Flush();
                await Task.Delay(20, CancellationToken.None);
            }
        }, CancellationToken.None);

        try
        {
            var refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", hash));

            Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
            Assert.Contains(Elsewhere, refused.Message, StringComparison.Ordinal);
            Assert.Contains("Mr. Swales", Tools.GetEntity("Mr. Swales").Entity!.Name, StringComparison.Ordinal);
        }
        finally
        {
            await stop.CancelAsync();
            await writer;
            await saving.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void ANoteJustCutForASaveRefusesADenWideWriteOnWindows()
    {
        SkipUnlessWindows("Only Windows can tell that a file is open for writing.");
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;

        using var saving = new FileStream(Full(Elsewhere), FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        saving.SetLength(0);
        saving.Flush();

        var refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", hash));
        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(Elsewhere, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public async Task ASaveThatFinishesWhileAWriteLooksAtItIsReadWholeOnWindows()
    {
        SkipUnlessWindows("Only Windows can tell that a file is open for writing.");
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var saving = BeginSave(Note);
        var finishing = Task.Run(async () =>
        {
            await Task.Delay(50, CancellationToken.None);
            saving.Write(Encoding.UTF8.GetBytes(Tail));
            await saving.DisposeAsync();
        }, CancellationToken.None);

        var done = Tools.RenameEntity("Mr. Swales", "Old Swales", hash);
        await finishing;

        Assert.True(done.Changed);
        Assert.Contains("[[Old Swales]]", Read(Note), StringComparison.Ordinal);
    }

    /// <summary>
    /// The limit of what can be told. A program paused mid-save for longer
    /// than a write takes, in a note the write doesn't otherwise touch, looks
    /// just like one holding a whole file. The link still to come isn't seen,
    /// and shows as broken in <c>check</c> afterwards. (On Linux and macOS, a
    /// note the write does rewrite can be replaced with the part read, too; see
    /// design.md.) This pins that down so it stays stated; it isn't what we'd want.
    /// </summary>
    [Fact]
    [Trait("Scenario", "SC-MCP-41")]
    public void KnownLimitASavePausedForLongerThanAWriteTakesCanStillHideALink()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;

        using (var saving = BeginSave(Note))
        {
            Assert.True(Tools.RenameEntity("Mr. Swales", "Old Swales", hash).Changed);
            saving.Write(Encoding.UTF8.GetBytes(Tail));
        }

        Assert.Contains(Tools.Check().Diagnostics, d => d.Rule == "F-LINK-8" && d.Path == Note);
    }

    /// <summary>A program's save of <see cref="Head"/> so far: the old text cut, the first part written, and still open.</summary>
    private FileStream BeginSave(string path)
    {
        var saving = new FileStream(Full(path), FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        saving.SetLength(0);
        saving.Write(Encoding.UTF8.GetBytes(path == Note ? Head : "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z2\ntype: note\nname: Weather almanac\nstatus: draft\n---\n\n"));
        saving.Flush();
        return saving;
    }

    private static void SkipUnlessWindows(string why)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(why);
        }
    }

    private string Full(string path) => Path.Combine(_den.Root, path.Replace('/', Path.DirectorySeparatorChar));

    private static string Rel(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private string Read(string path)
    {
        using var stream = new FileStream(Full(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Every file of the den but the index, with its text.</summary>
    private Dictionary<string, string> Files() =>
        Directory.EnumerateFiles(_den.Root, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(_den.Root, f).StartsWith(".wolfden", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetRelativePath(_den.Root, f), File.ReadAllText);
}

using Wolfden.Core.Structure;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// Another program saves a file after a write's comparison, just before the store
/// writes it. The store compares again before each try, so the save stays, and the
/// write says what it left behind (#323). The save is made by hand at that moment,
/// so these don't depend on timing, and they hold on every OS.
/// </summary>
public sealed class SaveDuringWriteTests : IDisposable
{
    private const string Book = "universes/dracula/books/dracula";
    private const string BookFile = Book + "/book.md";
    private const string Swales = Book + "/entities/characters/mr-swales.md";
    private const string Note = "universes/dracula/entities/notes/whitby-dialect.md";
    private const string Theirs = "\nSaved in an editor.\n";

    private readonly TempDen _den = TempDen.Dracula();

    public SaveDuringWriteTests() =>
        _den.Write(Note, "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z1\ntype: note\nname: Whitby dialect\nstatus: draft\n---\n\nSee [[Mr. Swales]].\n");

    private DenTools Tools => new(_den.Root);

    public void Dispose() => _den.Dispose();

    /// <summary>Tools whose store runs <paramref name="hook"/> just before each store write, with the path and how many writes of it came before.</summary>
    private DenTools WithHook(Action<string, int> hook) => new(_den.Root) { Store = new HookStore(_den.Root, hook) };

    private void SaveElsewhere(string path) => _den.Write(path, _den.Read(path) + Theirs);

    private string Full(string path) => Path.Combine(_den.Root, path.Replace('/', Path.DirectorySeparatorChar));

    private static void SkipUnlessWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only a real process on Windows is run for this.");
        }
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AnEditLeavesASaveMadeJustBeforeItsWrite()
    {
        var hash = Tools.GetEntity("Whitby dialect").Entity!.Hash!;
        var before = _den.Read(Note);
        var tools = WithHook((path, _) => SaveElsewhere(path));

        var refused = Assert.Throws<DenToolException>(() => tools.ReplaceLead("Whitby dialect", "New text.\n", hash));

        Assert.Equal(ToolErrorCodes.StaleFile, refused.Code);
        Assert.Equal(Note, refused.StalePath);
        Assert.Equal($"{Note} changed while the edit was being saved. Nothing was written. Read it again and retry.", refused.Message);
        Assert.Equal(before + Theirs, _den.Read(Note));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AddingAChapterLeavesASaveOfBookMdMadeJustBeforeItsWrite()
    {
        var hash = Tools.GetBookFreeze("dracula").Hash;
        var before = _den.Read(BookFile);
        var tools = WithHook((path, _) => SaveElsewhere(path));

        var refused = Assert.Throws<DenToolException>(() => tools.AddChapter("dracula", "A new chapter", expectedHash: hash));

        Assert.Equal(ToolErrorCodes.StaleFile, refused.Code);
        Assert.Equal(BookFile, refused.StalePath);
        Assert.Equal(before + Theirs, _den.Read(BookFile));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void ARenameLeftByASavePartWayPutsBackWhatItWroteAndKeepsTheSave()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var before = (Swales: _den.Read(Swales), Note: _den.Read(Note));

        // Swales's file sorts first, so it's written before the note is reached.
        var tools = WithHook((path, _) =>
        {
            if (path == Note)
            {
                SaveElsewhere(Note);
            }
        });

        var refused = Assert.Throws<DenToolException>(() => tools.RenameEntity("Mr. Swales", "Old Swales", hash));

        Assert.Equal(ToolErrorCodes.StaleFile, refused.Code);
        Assert.Equal(Note, refused.StalePath);
        Assert.Equal(
            $"{Note} changed while the edit was being saved. The files already written ({Swales}) were put back as they were, so nothing changed. Read it again and retry.",
            refused.Message);
        Assert.Equal(before.Swales, _den.Read(Swales));
        Assert.Equal(before.Note + Theirs, _den.Read(Note));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void ARenameLeftPartWaySaysAFileSavedDuringThePuttingBackIsntToBeRestored()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var note = _den.Read(Note);
        string? theirs = null;

        // The note's save stops the rename. Then, just as Swales is put back, it's saved too, so that's left alone.
        var tools = WithHook((path, count) =>
        {
            if (path == Note)
            {
                SaveElsewhere(Note);
            }
            else if (path == Swales && count == 1)
            {
                theirs = _den.Read(Swales) + Theirs;
                _den.Write(Swales, theirs);
            }
        });

        var refused = Assert.Throws<DenToolException>(() => tools.RenameEntity("Mr. Swales", "Old Swales", hash));

        // Part of it stands, so it isn't a stale_file to read again and retry, and a page shows this message.
        Assert.Equal(ToolErrorCodes.WriteFailed, refused.Code);
        Assert.Null(refused.StalePath);
        Assert.StartsWith($"{Note} changed while the edit was being saved.", refused.Message, StringComparison.Ordinal);
        AssertSavedElsewhere(refused, Swales);
        Assert.Equal(theirs, _den.Read(Swales));
        Assert.Equal(note + Theirs, _den.Read(Note));
    }

    private static void AssertSavedElsewhere(DenToolException refused, string path)
    {
        Assert.Contains($"may have old links: {path}.", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Don't restore them from history", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hold the edit", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Read it again and retry", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void ARealSaveFromAnotherProcessDuringThePuttingBackIsKeptAndNotToBeRestored()
    {
        SkipUnlessWindows();
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        string? theirs = null;
        var tools = WithHook((path, count) =>
        {
            if (path == Note)
            {
                SaveElsewhere(Note);
            }
            else if (path == Swales && count == 1)
            {
                // A whole save by a real separate process, finished before the store looks.
                theirs = _den.Read(Swales) + Theirs;
                var saving = StalledWriter.StartAsync(Full(Swales), theirs, 40).GetAwaiter().GetResult();
                saving.FinishAsync().GetAwaiter().GetResult();
                saving.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });

        var refused = Assert.Throws<DenToolException>(() => tools.RenameEntity("Mr. Swales", "Old Swales", hash));

        Assert.Equal(ToolErrorCodes.WriteFailed, refused.Code);
        AssertSavedElsewhere(refused, Swales);
        Assert.Equal(theirs, _den.Read(Swales));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public async Task AFileStillBeingSavedDuringThePuttingBackIsNamedAsOneToCheckOnceClosed()
    {
        SkipUnlessWindows();
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        StalledWriter? saving = null;
        string? theirs = null;
        var tools = WithHook((path, count) =>
        {
            if (path == Note)
            {
                SaveElsewhere(Note);
            }
            else if (path == Swales && count == 1)
            {
                theirs = _den.Read(Swales) + Theirs;
                saving = StalledWriter.StartAsync(Full(Swales), theirs, 40).GetAwaiter().GetResult();
            }
        });

        try
        {
            var refused = Assert.Throws<DenToolException>(() => tools.RenameEntity("Mr. Swales", "Old Swales", hash));

            Assert.Equal(ToolErrorCodes.WriteFailed, refused.Code);
            Assert.Contains($"Wolfden can't tell what's in them: {Swales}. Check them once they're closed.", refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("from history", refused.Message, StringComparison.Ordinal);
            await saving!.FinishAsync();
        }
        finally
        {
            if (saving is not null)
            {
                await saving.DisposeAsync();
            }
        }

        Assert.Equal(theirs, _den.Read(Swales));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AFileThatStillHoldsTheEditCanBeRestoredFromHistory()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;

        // Nothing else saved Swales: it just can't be written back.
        var tools = WithHook((path, count) =>
        {
            if (path == Note)
            {
                SaveElsewhere(Note);
            }
            else if (path == Swales && count == 1)
            {
                throw new DenStoreException(DenStoreError.AccessDenied, path, $"'{path}' is read-only.");
            }
        });

        var refused = Assert.Throws<DenToolException>(() => tools.RenameEntity("Mr. Swales", "Old Swales", hash));

        Assert.Equal(ToolErrorCodes.WriteFailed, refused.Code);
        Assert.Contains($"couldn't be put back, and hold the edit: {Swales}. Check them, or restore them from history.", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Don't restore", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AMoveLeftAtItsNewPathBySaveSaysWhereTheNoteIs()
    {
        const string Series = "universes/starfall/series/long-dark";
        const string Ilse = Series + "/entities/characters/ilse-tarrow.md";
        const string Moved = Series + "/books/ash/entities/characters/ilse-tarrow.md";
        using var den = TempDen.Starfall();
        var hash = new DenTools(den.Root).GetEntity(Ilse, "starfall").Entity!.Hash!;
        var moves = 0;

        // The last rewrite, at the new path, fails. The note is saved there just as it's to move back.
        var tools = new DenTools(den.Root)
        {
            Store = new HookStore(
                den.Root,
                (path, _) =>
                {
                    if (path == Moved)
                    {
                        throw new DenStoreException(DenStoreError.AccessDenied, path, $"'{path}' is read-only.");
                    }
                },
                moving: () =>
                {
                    if (++moves == 2)
                    {
                        den.Write(Moved, den.Read(Moved) + Theirs);
                    }
                }),
        };

        var refused = Assert.Throws<DenToolException>(() => tools.MoveEntity(Ilse, hash, toBook: "ash", enterAtNewScope: true, universe: "starfall"));

        Assert.Equal(ToolErrorCodes.WriteFailed, refused.Code);
        Assert.Contains($"may have old links: {Moved}.", refused.Message, StringComparison.Ordinal);
        Assert.Contains($"The note is at {Moved} now, not {Ilse}.", refused.Message, StringComparison.Ordinal);
        Assert.EndsWith(Theirs, den.Read(Moved), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AWholeDenWriteStoppedLateListsOnlyTheFirstFewItPutBack()
    {
        for (var i = 0; i < 12; i++)
        {
            _den.Write($"universes/dracula/entities/notes/link-{i:D2}.md", $"---\nid: 01M4TF5F0EKGGWD5FNVBAEY{i:D3}\ntype: note\nname: Link {i}\nstatus: draft\n---\n\nSee [[Mr. Swales]].\n");
        }

        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var tools = WithHook((path, _) =>
        {
            if (path == Note)
            {
                SaveElsewhere(Note);
            }
        });

        var refused = Assert.Throws<DenToolException>(() => tools.RenameEntity("Mr. Swales", "Old Swales", hash));

        Assert.Equal(ToolErrorCodes.StaleFile, refused.Code);
        Assert.Contains(", and 3 more) were put back as they were", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("link-11.md", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AMoveLeftByASavePartWayPutsBackWhatItWroteAndKeepsTheSave()
    {
        const string Series = "universes/starfall/series/long-dark";
        const string Tessaly = Series + "/entities/ships/tessaly.md";
        const string Ilse = Series + "/entities/characters/ilse-tarrow.md";
        const string Outline = Series + "/books/ember/outline.md";
        using var den = TempDen.Starfall();

        // A second Tessaly elsewhere, so the links to this one have to say which they mean once it moves.
        den.Write("universes/starfall/books/a-quiet-moon/entities/ships/tessaly.md", "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z4\ntype: ship\nname: Tessaly\nstatus: canon\nfrom: a-quiet-moon\n---\n");
        var hash = new DenTools(den.Root).GetEntity(Tessaly, "starfall").Entity!.Hash!;
        var before = (Tessaly: den.Read(Tessaly), Ilse: den.Read(Ilse), Outline: den.Read(Outline));

        // The file is rewritten in place and the links to it follow, before it's moved. Ilse's note holds one.
        var tools = new DenTools(den.Root)
        {
            Store = new HookStore(den.Root, (path, _) =>
            {
                if (path == Ilse)
                {
                    den.Write(Ilse, den.Read(Ilse) + Theirs);
                }
            }),
        };

        var refused = Assert.Throws<DenToolException>(() => tools.MoveEntity(Tessaly, hash, toBook: "ash", keepEntry: true, universe: "starfall"));

        Assert.Equal(ToolErrorCodes.StaleFile, refused.Code);
        Assert.Equal(Ilse, refused.StalePath);
        Assert.StartsWith($"{Ilse} changed while the move was being saved.", refused.Message, StringComparison.Ordinal);
        Assert.Contains($"{Tessaly} is where it was, so nothing changed. Read it again and retry.", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before.Tessaly, den.Read(Tessaly));
        Assert.Equal(before.Outline, den.Read(Outline));
        Assert.Equal(before.Ilse + Theirs, den.Read(Ilse));
        Assert.False(File.Exists(Path.Combine(den.Root, Series + "/books/ash/entities/ships/tessaly.md")));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AMoveDoesntReplaceASaveOfTheNoteMadeJustBeforeItIsMoved()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var moved = "universes/dracula/entities/characters/mr-swales.md";

        // The note is moved whole by the store's MoveFile, so the save must stop that too.
        var tools = new DenTools(_den.Root) { Store = new HookStore(_den.Root, (_, _) => { }, moving: () => SaveElsewhere(Swales)) };

        var refused = Assert.Throws<DenToolException>(() => tools.MoveEntity("Mr. Swales", hash));

        Assert.Equal(ToolErrorCodes.StaleFile, refused.Code);
        Assert.Equal(Swales, refused.StalePath);
        Assert.EndsWith(Theirs, _den.Read(Swales), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_den.Root, moved)));
    }

    /// <summary>The real store, with a hook just before each write of a file, and one before each move of a file.</summary>
    private sealed class HookStore(string root, Action<string, int> hook, Action? moving = null) : IDenStore
    {
        private readonly FileSystemDenStore _disk = new(root);
        private readonly Dictionary<string, int> _writes = new(StringComparer.Ordinal);

        public bool IsReadOnly => false;

        public bool FileExists(string path) => _disk.FileExists(path);

        public byte[] ReadFile(string path) => _disk.ReadFile(path);

        public byte[]? TryReadFile(string path) => _disk.TryReadFile(path);

        public byte[]? TryReadListedFile(string path) => _disk.TryReadListedFile(path);

        public IEnumerable<string> GetDirectories(string path) => _disk.GetDirectories(path);

        public IEnumerable<string> GetFiles(string path) => _disk.GetFiles(path);

        public bool FolderExists(string path) => _disk.FolderExists(path);

        public void WriteFile(string path, byte[] bytes, DenWriteMode mode, byte[]? builtOn)
        {
            _writes.TryGetValue(path, out var count);
            _writes[path] = count + 1;
            hook(path, count);
            _disk.WriteFile(path, bytes, mode, builtOn);
        }

        public void DeleteFile(string path, byte[]? builtOn) => _disk.DeleteFile(path, builtOn);

        public void MoveFile(string from, string to, byte[]? builtOn)
        {
            moving?.Invoke();
            _disk.MoveFile(from, to, builtOn);
        }

        public void MoveFolder(string from, string to) => _disk.MoveFolder(from, to);

        public void DeleteFolder(string path) => _disk.DeleteFolder(path);
    }
}

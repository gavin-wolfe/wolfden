using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Wolfden.Core.Structure;
using Wolfden.Core.Types;

namespace Wolfden.Core.Tests;

/// <summary>
/// The den store's write half on disk (<see cref="FileSystemDenStore"/>,
/// #121): atomic writes that keep bytes exactly, paths that can't leave
/// the den, names that are one file on every OS, and read-only stores that
/// refuse writes. Each test works in a fresh copy of the Starfall den.
/// </summary>
public sealed class DenStoreTests : IDisposable
{
    private const string Ilse = "universes/starfall/series/long-dark/entities/characters/ilse-tarrow.md";
    private const string Characters = "universes/starfall/series/long-dark/entities/characters";

    private readonly string _root;
    private readonly string _outside;

    public DenStoreTests()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wolfden-core-tests", "store-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(folder, "den");
        _outside = Path.Combine(folder, "outside");
        Directory.CreateDirectory(_outside);
        foreach (var file in Directory.EnumerateFiles(MemoryDenSource.StarfallRoot, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(_root, Path.GetRelativePath(MemoryDenSource.StarfallRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temporary folder left behind is harmless.
        }
    }

    private FileSystemDenStore Store() => new(_root);

    private string Full(string path) => Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    // ----- writing -----

    public static TheoryData<string> ExactBytes() => new()
    {
        "bom-crlf-no-final-newline.md",
        "crlf-entity.md",
        "invalid-utf8.md",
        "lone-cr.md",
        "mixed-endings.md",
        "no-final-newline.md",
    };

    [Theory]
    [MemberData(nameof(ExactBytes))]
    [Trait("Scenario", "SC-STORE-1")]
    public void AWriteKeepsTheBytesExactlyAsGiven(string fixture)
    {
        var bytes = File.ReadAllBytes(Path.Combine(TestFiles.RepoRoot, "tests", "Wolfden.Core.Tests", "Fixtures", fixture));
        var store = Store();

        store.WriteFile(Ilse, bytes);

        Assert.Equal(bytes, File.ReadAllBytes(Full(Ilse)));
        Assert.Equal(bytes, store.ReadFile(Ilse));
    }

    [Fact]
    public void AnEmptyFileCanBeWritten()
    {
        var store = Store();

        store.WriteFile(Ilse, []);

        Assert.Empty(File.ReadAllBytes(Full(Ilse)));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-1")]
    public void AWriteLeavesNoTemporaryFileBehind()
    {
        var store = Store();
        var before = Directory.GetFileSystemEntries(Full(Characters)).Order(StringComparer.Ordinal).ToList();

        store.WriteFile(Ilse, Bytes("---\nname: Ilse Tarrow\n---\n"));
        store.WriteFile($"{Characters}/wren-ashby.md", Bytes("---\nname: Wren Ashby\n---\n"), DenWriteMode.Create);

        var after = Directory.GetFileSystemEntries(Full(Characters)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal([.. before, Full($"{Characters}/wren-ashby.md")], after.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-1")]
    public void AWriteThatFailsPartWayLeavesTheOldFileAndNoTemporaryFile()
    {
        var old = File.ReadAllBytes(Full(Ilse));
        string? temp = null;
        var store = new FileSystemDenStore(_root)
        {
            BeforeReplace = t =>
            {
                temp = t;
                throw new IOException("The power went out.");
            },
        };

        var e = Assert.Throws<IOException>(() => store.WriteFile(Ilse, Bytes("half a note")));

        Assert.Equal("The power went out.", e.Message);
        Assert.Equal(old, File.ReadAllBytes(Full(Ilse)));
        Assert.NotNull(temp);
        Assert.StartsWith(FileSystemDenStore.TempPrefix, Path.GetFileName(temp), StringComparison.Ordinal);
        Assert.False(File.Exists(temp));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-1")]
    public void AReaderSeesTheOldFileOrTheNewOneNeverPartOfEither()
    {
        // Two versions of different lengths, each all one letter, so a torn
        // read (part of one, part of the other, or cut short) shows.
        var a = Bytes(new string('a', 300_000));
        var b = Bytes(new string('b', 200_000));
        var store = Store();
        store.WriteFile(Ilse, a);
        var torn = new ConcurrentBag<string>();
        var reads = 0;
        var readerErrors = 0;
        string? lastReaderError = null;
        var stop = 0;

        var reader = new Thread(() =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                byte[] seen;
                try
                {
                    // Shared for delete too, as an editor's reader would be. Windows
                    // still won't replace the file while this is open (see below).
                    using var stream = new FileStream(Full(Ilse), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var buffer = new byte[stream.Length];
                    seen = buffer[..stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false)];
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Windows: caught mid-replace. Not a torn read.
                    Interlocked.Increment(ref readerErrors);
                    Volatile.Write(ref lastReaderError, $"{e.GetType().Name} (0x{e.HResult:X8})");
                    Thread.Sleep(1);
                    continue;
                }

                Interlocked.Increment(ref reads);
                if (!seen.AsSpan().SequenceEqual(a) && !seen.AsSpan().SequenceEqual(b))
                {
                    torn.Add(DescribeRead(seen, a, b));
                }

                Thread.Sleep(1);
            }
        });
        reader.Start();

        // Write until the reader has read alongside the writes for a while,
        // not for a fixed time: on a busy machine the reader's thread can
        // start late, or get little time.
        const int Writes = 60;
        const int ReadsDuringWrites = 20;
        var watch = Stopwatch.StartNew();
        var readsBefore = Volatile.Read(ref reads);
        var written = 0;
        var refused = 0;
        DenStoreException? lastRefusal = null;
        var current = a;
        string Progress() =>
            $"{written} writes, {refused} refused (last: {lastRefusal?.Message ?? "none"}); "
            + $"{Volatile.Read(ref reads) - readsBefore} reads, {Volatile.Read(ref readerErrors)} failed (last: {Volatile.Read(ref lastReaderError) ?? "none"}), {torn.Count} torn; "
            + $"{watch.Elapsed.TotalSeconds:F1} s.";
        byte[] ReadNote()
        {
            // Whatever refused the save may still hold the note, even with no
            // sharing, so wait for it: that isn't what's being checked.
            var waited = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    using var stream = new FileStream(Full(Ilse), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var buffer = new byte[stream.Length];
                    stream.ReadExactly(buffer);
                    return buffer;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException && waited.Elapsed < TimeSpan.FromSeconds(30))
                {
                    Thread.Sleep(20);
                }
            }
        }

        try
        {
            // Ends on b, not the a it started with, so saves that change
            // nothing can't pass for the last one saved.
            while (written < Writes || Volatile.Read(ref reads) - readsBefore < ReadsDuringWrites || ReferenceEquals(current, a))
            {
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"Too few writes and reads in time: {Progress()}");
                var next = written % 2 == 0 ? b : a;
                try
                {
                    store.WriteFile(Ilse, next, DenWriteMode.Replace);
                    current = next;
                    written++;
                }
                catch (DenStoreException e) when (e.Error == DenStoreError.InUse)
                {
                    // Windows can't replace a file that's open. The store tries
                    // again for a moment, then says so: a save can be refused
                    // when the reader's handle outlasts that (#173). That's a
                    // failed save, not a torn read, and it doesn't count as a
                    // write. It must leave the note as it was: the next save
                    // would hide any change, so that's checked now.
                    refused++;
                    lastRefusal = e;
                    Assert.True(ReadNote().AsSpan().SequenceEqual(current), $"A refused save changed the note. {Progress()}");
                }
            }
        }
        finally
        {
            Volatile.Write(ref stop, 1);
            reader.Join();
        }

        Assert.True(torn.IsEmpty, $"A reader saw part of a note {torn.Count} times, such as: {string.Join("; ", torn.Distinct().Take(5))}. {Progress()}");

        Assert.True(ReadNote().AsSpan().SequenceEqual(current), $"The note isn't the last one saved. {Progress()}");

        // Refused saves clean up too. A scanner can keep a deleted file listed for a moment.
        bool NoTemporaryFile() => !Directory.EnumerateFiles(Full(Characters), FileSystemDenStore.TempPrefix + "*").Any();
        Assert.True(SpinWait.SpinUntil(NoTemporaryFile, TimeSpan.FromSeconds(5)), $"A temporary file was left among the notes. {Progress()}");
    }

    /// <summary>What a reader got that was neither version: enough to tell a cut-short read from a mix of the two.</summary>
    private static string DescribeRead(byte[] seen, byte[] a, byte[] b)
    {
        var letters = string.Join(", ", seen.GroupBy(x => x).OrderBy(g => g.Key).Select(g => $"{g.Count()} x 0x{g.Key:X2}"));
        return $"{seen.Length} bytes of {a.Length} or {b.Length} ({(letters.Length == 0 ? "empty" : letters)})";
    }

    [Fact]
    public void MissingFoldersOnTheWayAreMade()
    {
        var store = Store();
        const string ship = "universes/starfall/entities/ships/kestrel.md";

        store.WriteFile(ship, Bytes("---\ntype: ship\nname: Kestrel\n---\n"), DenWriteMode.Create);

        Assert.True(store.FileExists(ship));
        Assert.Equal(["kestrel.md"], store.GetFiles("universes/starfall/entities/ships"));
    }

    [Fact]
    public void CreateRefusesAFileThatsThereAndLeavesItAlone()
    {
        var old = File.ReadAllBytes(Full(Ilse));

        var e = Assert.Throws<DenStoreException>(() => Store().WriteFile(Ilse, Bytes("new"), DenWriteMode.Create));

        Assert.Equal(DenStoreError.AlreadyExists, e.Error);
        Assert.Equal(old, File.ReadAllBytes(Full(Ilse)));
    }

    [Fact]
    public void ReplaceRefusesAFileThatIsntThereAndMakesNothing()
    {
        const string wren = $"{Characters}/wren-ashby.md";

        var e = Assert.Throws<DenStoreException>(() => Store().WriteFile(wren, Bytes("new"), DenWriteMode.Replace));

        Assert.Equal(DenStoreError.NotFound, e.Error);
        Assert.False(File.Exists(Full(wren)));
    }

    [Fact]
    public void AFolderIsntWrittenAsAFileAndAFileIsntAFolder()
    {
        var store = Store();

        Assert.Equal(DenStoreError.NotAFile, Assert.Throws<DenStoreException>(() => store.WriteFile(Characters, Bytes("x"))).Error);
        Assert.Equal(DenStoreError.NotAFile, Assert.Throws<DenStoreException>(() => store.WriteFile("den.yaml/x.md", Bytes("x"))).Error);
        Assert.Equal(DenStoreError.NotAFile, Assert.Throws<DenStoreException>(() => store.DeleteFile(Characters)).Error);
        Assert.True(Directory.Exists(Full(Characters)));
    }

    // ----- paths that would leave the den -----

    [Theory]
    [Trait("Scenario", "SC-STORE-2")]
    [InlineData("../outside/escaped.md")]
    [InlineData("universes/../../outside/escaped.md")]
    [InlineData("..\\outside\\escaped.md")]
    [InlineData(".git/hooks/pre-commit")]
    [InlineData(".wolfden/index.db")]
    public void AWriteOutsideTheDenOrIntoAHiddenFolderIsRefusedAndTouchesNothing(string path)
    {
        var store = Store();

        var e = Assert.Throws<DenStoreException>(() => store.WriteFile(path, Bytes("escaped")));

        Assert.Equal(DenStoreError.InvalidPath, e.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_outside));
        Assert.False(Directory.Exists(Path.Combine(_root, ".git")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".wolfden")));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-2")]
    public void AnAbsolutePathIsRefused()
    {
        var absolute = Path.Combine(_outside, "escaped.md").Replace('\\', '/');

        var e = Assert.Throws<DenStoreException>(() => Store().WriteFile(absolute, Bytes("escaped")));

        Assert.Equal(DenStoreError.InvalidPath, e.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_outside));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-2")]
    public void EveryWriteChecksItsPaths()
    {
        var store = Store();
        const string escape = "../outside/escaped.md";
        File.WriteAllText(Path.Combine(_outside, "keep.md"), "keep");

        Assert.Equal(DenStoreError.InvalidPath, Assert.Throws<DenStoreException>(() => store.DeleteFile("../outside/keep.md")).Error);
        Assert.Equal(DenStoreError.InvalidPath, Assert.Throws<DenStoreException>(() => store.MoveFile(Ilse, escape)).Error);
        Assert.Equal(DenStoreError.InvalidPath, Assert.Throws<DenStoreException>(() => store.MoveFile("../outside/keep.md", Ilse)).Error);
        Assert.Equal(DenStoreError.InvalidPath, Assert.Throws<DenStoreException>(() => store.MoveFolder(Characters, "../outside/characters")).Error);
        Assert.Equal(DenStoreError.InvalidPath, Assert.Throws<DenStoreException>(() => store.DeleteFolder("../outside")).Error);
        Assert.Equal(DenStoreError.InvalidPath, Assert.Throws<DenStoreException>(() => store.DeleteFolder("")).Error);

        Assert.Equal(["keep.md"], Directory.EnumerateFileSystemEntries(_outside).Select(Path.GetFileName));
        Assert.True(File.Exists(Full(Ilse)));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-2")]
    public void AWriteNeverGoesThroughAFolderLink()
    {
        File.WriteAllText(Path.Combine(_outside, "keep.md"), "keep");
        using var link = LinkFolder(Full("universes/starfall/shared"), _outside);
        var store = Store();

        var write = Assert.Throws<DenStoreException>(() => store.WriteFile("universes/starfall/shared/escaped.md", Bytes("escaped")));
        var delete = Assert.Throws<DenStoreException>(() => store.DeleteFile("universes/starfall/shared/keep.md"));
        var moveOut = Assert.Throws<DenStoreException>(() => store.MoveFile("universes/starfall/shared/keep.md", $"{Characters}/keep.md"));
        var deleteLink = Assert.Throws<DenStoreException>(() => store.DeleteFolder("universes/starfall/shared"));

        Assert.All([write, delete, moveOut, deleteLink], e => Assert.Equal(DenStoreError.InvalidPath, e.Error));
        Assert.Contains("folder link", write.Message, StringComparison.Ordinal);
        Assert.Equal(["keep.md"], Directory.EnumerateFileSystemEntries(_outside).Select(Path.GetFileName));
    }

    // ----- names that differ only in case -----

    [Fact]
    [Trait("Scenario", "SC-STORE-3")]
    public void ANameThatDiffersOnlyInCaseIsRefusedOnEveryOs()
    {
        var store = Store();
        var old = File.ReadAllBytes(Full(Ilse));

        var file = Assert.Throws<DenStoreException>(() => store.WriteFile($"{Characters}/Ilse-Tarrow.md", Bytes("another Ilse")));
        var folder = Assert.Throws<DenStoreException>(() => store.WriteFile("universes/Starfall/entities/notes/x.md", Bytes("x")));
        var moved = Assert.Throws<DenStoreException>(() => store.MoveFile("universes/starfall/entities/locations/kessan-reach.md", $"{Characters}/ILSE-TARROW.md"));

        Assert.All([file, folder, moved], e => Assert.Equal(DenStoreError.CaseConflict, e.Error));
        Assert.Contains($"'{Ilse}' is already there", file.Message, StringComparison.Ordinal);
        Assert.Contains("'universes/starfall' is already there", folder.Message, StringComparison.Ordinal);
        Assert.Equal(old, File.ReadAllBytes(Full(Ilse)));
        Assert.Equal(["ilse-tarrow.md"], store.GetFiles(Characters));
        Assert.Equal(["starfall"], store.GetDirectories("universes").Where(d => d.Equals("starfall", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-3")]
    public void AWindowsShortNameIsAnotherNameAndIsRefused()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Short names are a Windows file system feature.");
        }

        var full = Full(Characters);
        var shortFull = ShortPath(full);
        var shortName = Path.GetFileName(shortFull);
        if (string.Equals(shortName, Path.GetFileName(full), StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("This volume doesn't make short names.");
        }

        var e = Assert.Throws<DenStoreException>(() =>
            Store().WriteFile($"universes/starfall/series/long-dark/entities/{shortName}/ilse-tarrow.md", Bytes("through a short name")));

        Assert.Equal(DenStoreError.CaseConflict, e.Error);
        Assert.Contains("another name", e.Message, StringComparison.Ordinal);
    }

    // ----- moving, renaming, deleting -----

    [Fact]
    [Trait("Scenario", "SC-STORE-5")]
    public void AFileIsMovedOrRenamedWithItsBytes()
    {
        var store = Store();
        var bytes = File.ReadAllBytes(Full(Ilse));
        const string to = "universes/starfall/entities/characters/ilse-tarrow.md";

        store.MoveFile(Ilse, to);

        Assert.False(store.FileExists(Ilse));
        Assert.Equal(bytes, store.ReadFile(to));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-5")]
    public void ARenameThatChangesOnlyTheCaseIsAllowed()
    {
        var store = Store();
        var bytes = File.ReadAllBytes(Full(Ilse));

        store.MoveFile(Ilse, $"{Characters}/Ilse-Tarrow.md");
        store.MoveFolder("universes/starfall/series/long-dark/books/ember", "universes/starfall/series/long-dark/books/Ember");

        Assert.Equal(["Ilse-Tarrow.md"], store.GetFiles(Characters));
        Assert.Equal(bytes, store.ReadFile($"{Characters}/Ilse-Tarrow.md"));
        Assert.Contains("Ember", store.GetDirectories("universes/starfall/series/long-dark/books"));
        Assert.DoesNotContain("ember", store.GetDirectories("universes/starfall/series/long-dark/books"));
        Assert.True(store.FileExists("universes/starfall/series/long-dark/books/Ember/book.md"));
        Assert.False(store.FileExists("universes/starfall/series/long-dark/books/ember/book.md"));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-5")]
    public void AFolderIsMovedWithEverythingInIt()
    {
        var store = Store();
        const string from = "universes/starfall/series/long-dark/books/ember";
        const string to = "universes/starfall/books/ember";
        var outline = store.ReadFile($"{from}/outline.md");

        store.MoveFolder(from, to);

        Assert.False(store.FolderExists(from));
        Assert.Equal(outline, store.ReadFile($"{to}/outline.md"));
        Assert.True(store.FileExists($"{to}/book.md"));
    }

    [Fact]
    public void AMoveDoesntOverwriteOrMoveAFolderIntoItself()
    {
        var store = Store();
        const string kessan = "universes/starfall/entities/locations/kessan-reach.md";

        Assert.Equal(DenStoreError.AlreadyExists, Assert.Throws<DenStoreException>(() => store.MoveFile(kessan, Ilse)).Error);
        Assert.Equal(DenStoreError.AlreadyExists, Assert.Throws<DenStoreException>(() => store.MoveFolder("universes/drift", "universes/starfall")).Error);
        Assert.Equal(DenStoreError.InvalidPath, Assert.Throws<DenStoreException>(() => store.MoveFolder("universes/starfall", "universes/starfall/inner")).Error);
        Assert.Equal(DenStoreError.NotFound, Assert.Throws<DenStoreException>(() => store.MoveFile($"{Characters}/nobody.md", $"{Characters}/somebody.md")).Error);
        Assert.Equal(DenStoreError.NotAFile, Assert.Throws<DenStoreException>(() => store.MoveFolder(Ilse, $"{Characters}/ilse")).Error);
        Assert.True(store.FileExists(kessan));
        Assert.True(store.FileExists(Ilse));
    }

    [Fact]
    public void DeletingAFileOrAnEmptyFolder()
    {
        var store = Store();

        store.DeleteFile(Ilse);
        Assert.False(File.Exists(Full(Ilse)));
        Assert.Equal(DenStoreError.NotFound, Assert.Throws<DenStoreException>(() => store.DeleteFile(Ilse)).Error);

        const string ships = "universes/starfall/series/long-dark/entities/ships";
        var notEmpty = Assert.Throws<DenStoreException>(() => store.DeleteFolder(ships));
        Assert.Equal(DenStoreError.NotEmpty, notEmpty.Error);
        Assert.True(Directory.Exists(Full(ships)));

        store.DeleteFolder(Characters);
        Assert.False(store.FolderExists(Characters));
        Assert.Equal(DenStoreError.NotFound, Assert.Throws<DenStoreException>(() => store.DeleteFolder(Characters)).Error);
    }

    [Fact]
    public void AFolderWhoseOnlyEntriesAreHiddenIsntDeletedAndTheMessageNamesOne()
    {
        var store = Store();
        store.DeleteFile(Ilse);
        File.WriteAllText(Path.Combine(Full(Characters), ".ilse-tarrow.md.swp"), "swap");

        var e = Assert.Throws<DenStoreException>(() => store.DeleteFolder(Characters));

        Assert.Equal(DenStoreError.NotEmpty, e.Error);
        Assert.Contains("'.ilse-tarrow.md.swp', which is hidden", e.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Full(Characters)));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-5")]
    public void ACaseOnlyFolderRenameThatFailsHalfWayIsPutBack()
    {
        // As on Windows, where the plain move is refused or does nothing, so
        // the rename goes through a hidden name, and its second step fails.
        const string books = "universes/starfall/series/long-dark/books";
        var moves = 0;
        var store = new FileSystemDenStore(_root)
        {
            OneNameForAllCases = true,
            DirectoryMove = (from, to) =>
            {
                switch (++moves)
                {
                    case 1:
                        throw new IOException("Source and destination path must be different.");
                    case 3:
                        throw new IOException("The disk is full.");
                    default:
                        Directory.Move(from, to);
                        break;
                }
            },
        };

        var e = Assert.Throws<IOException>(() => store.MoveFolder($"{books}/ember", $"{books}/Ember"));

        Assert.Equal("The disk is full.", e.Message);
        Assert.Equal(4, moves);
        Assert.Equal(["ash", "ember"], Directory.EnumerateDirectories(Full(books)).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(store.FileExists($"{books}/ember/book.md"));
    }

    [Fact]
    public void WhenARenameCantBePutBackTheErrorSaysWhereTheHiddenFolderIs()
    {
        const string books = "universes/starfall/series/long-dark/books";
        var moves = 0;
        var store = new FileSystemDenStore(_root)
        {
            OneNameForAllCases = true,
            DirectoryMove = (from, to) =>
            {
                switch (++moves)
                {
                    case 1:
                        throw new IOException("Source and destination path must be different.");
                    case 2:
                        Directory.Move(from, to);
                        break;
                    case 3:
                        throw new IOException("The disk is full.");
                    default:
                        throw new UnauthorizedAccessException("Access to the path is denied.");
                }
            },
        };

        var e = Assert.Throws<DenStoreException>(() => store.MoveFolder($"{books}/ember", $"{books}/Ember"));

        // The code is the rollback's own, not a made-up one.
        Assert.Equal(DenStoreError.AccessDenied, e.Error);
        var hidden = Assert.Single(Directory.EnumerateDirectories(Full(books)).Select(Path.GetFileName), n => n!.StartsWith(FileSystemDenStore.TempPrefix, StringComparison.Ordinal));
        Assert.Contains($"it's now the folder '{hidden}' beside it, which is hidden, so the den doesn't show it", e.Message, StringComparison.Ordinal);
        Assert.Contains("Rename that folder back to 'ember'", e.Message, StringComparison.Ordinal);
    }

    // ----- names with invisible characters -----

    public static TheoryData<string> RealNames() => new()
    {
        "\u2764\uFE0F-notes.md",
        "family-\U0001F468\u200D\U0001F469\u200D\U0001F467.md",
        "\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645.md",
    };

    [Theory]
    [MemberData(nameof(RealNames))]
    [Trait("Scenario", "SC-STORE-3")]
    public void ANoteNamedWithEmojiOrAJoinerCanBeMadeMovedAndDeleted(string name)
    {
        var store = Store();
        var path = $"{Characters}/{name}";
        var folder = $"universes/starfall/entities/{name.Replace(".md", "", StringComparison.Ordinal)}";

        store.WriteFile(path, Bytes("---\nname: x\n---\n"), DenWriteMode.Create);
        store.MoveFile(path, $"{folder}/{name}");
        store.MoveFolder(folder, $"{folder}-2");
        store.DeleteFile($"{folder}-2/{name}");

        Assert.False(store.FileExists($"{folder}-2/{name}"));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-3")]
    public void ANewNameDisguisedByAnInvisibleCharacterIsRefused()
    {
        var store = Store();
        const string disguised = $"{Characters}/ilse\u200B-tarrow.md";

        var create = Assert.Throws<DenStoreException>(() => store.WriteFile(disguised, Bytes("x")));
        var move = Assert.Throws<DenStoreException>(() => store.MoveFile(Ilse, disguised));
        var folder = Assert.Throws<DenStoreException>(() => store.WriteFile("universes/starfall/entities/\u202Eships/x.md", Bytes("x")));

        Assert.All([create, move, folder], e => Assert.Equal(DenStoreError.InvalidPath, e.Error));
        Assert.Contains("U+200B", create.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Full(disguised)));
        Assert.True(File.Exists(Full(Ilse)));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-3")]
    public void AFileAlreadyNamedWithAnInvisibleCharacterCanStillBeSavedMovedAndDeleted()
    {
        // Made outside Wolfden: Wolfden won't make such a name, but doesn't strand one.
        const string odd = $"{Characters}/ilse\u200B-tarrow-copy.md";
        File.WriteAllText(Full(odd), "---\nname: Copy\n---\n");
        var store = Store();

        store.WriteFile(odd, Bytes("---\nname: Saved\n---\n"), DenWriteMode.Replace);
        Assert.Equal("---\nname: Saved\n---\n", File.ReadAllText(Full(odd)));
        store.MoveFile(odd, $"{Characters}/ilse-tarrow-copy.md");
        File.WriteAllText(Full(odd), "again");
        store.DeleteFile(odd);

        Assert.False(File.Exists(Full(odd)));
        Assert.True(store.FileExists($"{Characters}/ilse-tarrow-copy.md"));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-5")]
    public void ACaseOnlyFolderRenameGoesThroughAHiddenNameWhenThePlainMoveIsRefused()
    {
        const string books = "universes/starfall/series/long-dark/books";
        var moves = 0;
        var store = new FileSystemDenStore(_root)
        {
            OneNameForAllCases = true,
            DirectoryMove = (from, to) =>
            {
                if (++moves == 1)
                {
                    throw new IOException("Source and destination path must be different.");
                }

                Directory.Move(from, to);
            },
        };

        store.MoveFolder($"{books}/ember", $"{books}/Ember");

        Assert.Equal(3, moves);
        Assert.Equal(["Ember", "ash"], Directory.EnumerateDirectories(Full(books)).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-3")]
    public void ACaseOnlyRenameOntoAnotherFileWithThatNameIsRefused()
    {
        // Only Linux can hold ilse-tarrow.md and Ilse-Tarrow.md side by side.
        File.WriteAllText(Full($"{Characters}/Ilse-Tarrow.md"), "another Ilse");
        if (Directory.GetFiles(Full(Characters)).Length < 2)
        {
            Assert.Skip("This file system takes names that differ only in case as one name.");
        }

        var e = Assert.Throws<DenStoreException>(() => new FileSystemDenStore(_root).MoveFile(Ilse, $"{Characters}/ILSE-TARROW.md"));

        Assert.Equal(DenStoreError.CaseConflict, e.Error);
        Assert.Equal("another Ilse", File.ReadAllText(Full($"{Characters}/Ilse-Tarrow.md")));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-3")]
    public void ANameInAnotherUnicodeFormIsRefused()
    {
        // "Café" with é as one character (NFC), and with e and a combining accent (NFD).
        var nfc = "caf\u00E9.md";
        var nfd = "cafe\u0301.md";
        var store = Store();
        store.WriteFile($"{Characters}/{nfc}", Bytes("---\nname: Caf\u00E9\n---\n"), DenWriteMode.Create);

        var e = Assert.Throws<DenStoreException>(() => store.WriteFile($"{Characters}/{nfd}", Bytes("another caf\u00E9")));

        Assert.Equal(DenStoreError.CaseConflict, e.Error);
        Assert.Contains("Unicode form", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOnlyFileIsRefusedAtOnceWithAClearReason()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("On Linux and macOS a read-only file can be replaced: its folder's permissions decide.");
        }

        var store = Store();
        File.SetAttributes(Full(Ilse), FileAttributes.ReadOnly);
        try
        {
            // Counted, not timed: the write flushes to disk, which a busy
            // CI machine can make take longer than all the retries would.
            using var retries = new RetryCounter(_root);
            var watch = Stopwatch.StartNew();
            var e = Assert.Throws<DenStoreException>(() => store.WriteFile(Ilse, Bytes("x"), DenWriteMode.Replace));
            watch.Stop();

            Assert.Equal(DenStoreError.AccessDenied, e.Error);
            Assert.Equal($"'{Ilse}' can't be written: the file is read-only.", e.Message);
            Assert.IsType<UnauthorizedAccessException>(e.InnerException);
            Assert.True(retries.Count == 0, $"It was tried {retries.Count + 1} times: a read-only file isn't worth trying again.");

            // A backstop for waits the counter can't see (it counts only Run's retries), far above any disk flush.
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"It took {watch.ElapsedMilliseconds} ms.");
        }
        finally
        {
            File.SetAttributes(Full(Ilse), FileAttributes.Normal);
        }
    }

    [Fact]
    public void AFileAnotherProgramHoldsOpenIsReportedAsInUse()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows keeps an open file from being replaced.");
        }

        var store = Store();
        using var held = new FileStream(Full(Ilse), FileMode.Open, FileAccess.Read, FileShare.None);
        using var retries = new RetryCounter(_root);

        var write = Assert.Throws<DenStoreException>(() => store.WriteFile(Ilse, Bytes("x"), DenWriteMode.Replace));
        var delete = Assert.Throws<DenStoreException>(() => store.DeleteFile(Ilse));
        var move = Assert.Throws<DenStoreException>(() => store.MoveFile(Ilse, $"{Characters}/ilse.md"));

        // A file in use may be let go in a moment, so each is tried again
        // first. This also shows the counter sees retries, so the read-only
        // test's count of none means something.
        Assert.Equal(3 * (FileSystemDenStore.Attempts - 1), retries.Count);
        Assert.All([write, delete, move], e => Assert.Equal(DenStoreError.InUse, e.Error));
        Assert.Equal($"'{Ilse}' can't be written: another program has it open. Close it there and try again.", write.Message);
        Assert.IsType<UnauthorizedAccessException>(write.InnerException);
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-1")]
    public void ASaveGoesThroughWhenWhatHoldsTheFileLetsGoDuringTheRetries()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows keeps an open file from being replaced.");
        }

        var store = Store();
        var saved = Bytes("the new note");
        using var held = new FileStream(Full(Ilse), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var retries = new RetryCounter(_root, count =>
        {
            if (count == 2)
            {
                held.Dispose();
            }
        });

        store.WriteFile(Ilse, saved, DenWriteMode.Replace);

        Assert.True(retries.Count >= 2, $"It was tried {retries.Count + 1} times, so the holder never got in the way.");
        Assert.Equal(saved, File.ReadAllBytes(Full(Ilse)));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-1")]
    public void ASaveDeniedByWhatHasLetGoSinceIsTriedAgain()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows denies replacing an open file.");
        }

        // Denied on every normal try, with nothing holding the file when the
        // store asks: a reader that closed between the denial and the question.
        var moves = 0;
        var store = new FileSystemDenStore(_root)
        {
            FileMove = (from, to, overwrite) =>
            {
                if (++moves <= FileSystemDenStore.Attempts)
                {
                    throw new UnauthorizedAccessException("Access to the path is denied.");
                }

                File.Move(from, to, overwrite);
            },
        };
        var saved = Bytes("the new note");

        store.WriteFile(Ilse, saved, DenWriteMode.Replace);

        Assert.Equal(FileSystemDenStore.Attempts + 1, moves);
        Assert.Equal(saved, File.ReadAllBytes(Full(Ilse)));
    }

    [Fact]
    public void AFileThatStaysDeniedWithNothingHoldingItIsReportedAsDenied()
    {
        var old = File.ReadAllBytes(Full(Ilse));
        var moves = 0;
        var store = new FileSystemDenStore(_root)
        {
            FileMove = (_, _, _) =>
            {
                moves++;
                throw new UnauthorizedAccessException("Access to the path is denied.");
            },
        };

        var e = Assert.Throws<DenStoreException>(() => store.WriteFile(Ilse, Bytes("x"), DenWriteMode.Replace));

        Assert.Equal(DenStoreError.AccessDenied, e.Error);
        Assert.Equal($"'{Ilse}' can't be written: access is denied.", e.Message);
        Assert.Equal(OperatingSystem.IsWindows() ? FileSystemDenStore.Attempts + FileSystemDenStore.LetGoAttempts : 1, moves);
        Assert.Equal(old, File.ReadAllBytes(Full(Ilse)));
        Assert.Empty(Directory.EnumerateFiles(Full(Characters), FileSystemDenStore.TempPrefix + "*"));
    }

    // ----- a write compares the file again before each try (#323) -----

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void ASaveMadeWhileTheStoreWaitsIsntReplaced()
    {
        SkipUnlessWindows();
        var builtOn = File.ReadAllBytes(Full(Ilse));
        var theirs = Bytes("their whole save");

        // A real refusal: the move can't go through until the program closes the file.
        using var saving = new FileStream(Full(Ilse), FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var retries = new RetryCounter(_root, count =>
        {
            if (count == 1)
            {
                saving.SetLength(0);
                saving.Write(theirs);
                saving.Dispose();
            }
        });

        var e = Assert.Throws<DenStoreException>(() => Store().WriteFile(Ilse, Bytes("built on the old note"), DenWriteMode.Replace, builtOn));

        Assert.Equal(DenStoreError.Changed, e.Error);
        Assert.Equal(Ilse, e.Path);
        Assert.Equal(1, retries.Count);
        Assert.Equal(theirs, File.ReadAllBytes(Full(Ilse)));
        Assert.Empty(Directory.EnumerateFiles(Full(Characters), FileSystemDenStore.TempPrefix + "*"));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void PartOfASaveStillUnderWayCountsAsAChange()
    {
        SkipUnlessWindows();
        var builtOn = File.ReadAllBytes(Full(Ilse));
        using var saving = new FileStream(Full(Ilse), FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var retries = new RetryCounter(_root, count =>
        {
            if (count == 1)
            {
                saving.SetLength(0);
                saving.Write(Bytes("the first part"));
                saving.Flush();
            }
        });

        var e = Assert.Throws<DenStoreException>(() => Store().WriteFile(Ilse, Bytes("x"), DenWriteMode.Replace, builtOn));

        Assert.Equal(DenStoreError.Changed, e.Error);
        Assert.Equal(1, retries.Count);
    }

    [Theory]
    [Trait("Scenario", "SC-STORE-6")]
    [InlineData(FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)]
    [InlineData(FileAccess.Read, FileShare.Read)]
    [InlineData(FileAccess.Read, FileShare.None)]
    [InlineData(FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)]
    public void AFileOnlyOpenAndUnchangedIsStillTriedAgain(FileAccess access, FileShare share)
    {
        SkipUnlessWindows();
        var builtOn = File.ReadAllBytes(Full(Ilse));
        var saved = Bytes("the new note");

        // As a virus scanner or indexer holds a file: nothing about it changes, and it lets go.
        using var held = new FileStream(Full(Ilse), FileMode.Open, access, share);
        using var retries = new RetryCounter(_root, count =>
        {
            if (count == 2)
            {
                held.Dispose();
            }
        });

        Store().WriteFile(Ilse, saved, DenWriteMode.Replace, builtOn);

        Assert.True(retries.Count >= 2, $"It was tried {retries.Count + 1} times, so the holder never got in the way.");
        Assert.Equal(saved, File.ReadAllBytes(Full(Ilse)));
    }

    [Theory]
    [Trait("Scenario", "SC-STORE-6")]
    [InlineData(FileAccess.Read, FileShare.None)]
    [InlineData(FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)]
    public void AFileHeldUnchangedThroughEveryTryIsStillInUse(FileAccess access, FileShare share)
    {
        SkipUnlessWindows();
        var builtOn = File.ReadAllBytes(Full(Ilse));
        using var held = new FileStream(Full(Ilse), FileMode.Open, access, share);
        using var retries = new RetryCounter(_root);

        var e = Assert.Throws<DenStoreException>(() => Store().WriteFile(Ilse, Bytes("x"), DenWriteMode.Replace, builtOn));

        Assert.Equal(DenStoreError.InUse, e.Error);
        Assert.Equal(FileSystemDenStore.Attempts - 1, retries.Count);
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AFileThatCantBeReadIsntTriedAndTheNextLookDecides()
    {
        SkipUnlessWindows();
        var builtOn = File.ReadAllBytes(Full(Ilse));
        var theirs = Bytes("their whole save");
        var moves = 0;
        var store = new FileSystemDenStore(_root)
        {
            FileMove = (from, to, overwrite) =>
            {
                moves++;
                File.Move(from, to, overwrite);
            },
        };

        // Locked against reading from the start, so nothing can be said of it until it's let go, changed.
        using var locked = new FileStream(Full(Ilse), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var retries = new RetryCounter(_root, count =>
        {
            if (count == 2)
            {
                locked.SetLength(0);
                locked.Write(theirs);
                locked.Dispose();
            }
        });

        var e = Assert.Throws<DenStoreException>(() => store.WriteFile(Ilse, Bytes("x"), DenWriteMode.Replace, builtOn));

        Assert.Equal(DenStoreError.Changed, e.Error);
        Assert.Equal(0, moves);
        Assert.Equal(theirs, File.ReadAllBytes(Full(Ilse)));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AFileDeletedWhileTheStoreWaitsIsntBroughtBack()
    {
        SkipUnlessWindows();
        var builtOn = File.ReadAllBytes(Full(Ilse));
        var store = new FileSystemDenStore(_root)
        {
            FileMove = (_, to, _) =>
            {
                File.Delete(to);
                throw new UnauthorizedAccessException("Access to the path is denied.");
            },
        };

        var e = Assert.Throws<DenStoreException>(() => store.WriteFile(Ilse, Bytes("x"), DenWriteMode.Replace, builtOn));

        Assert.Equal(DenStoreError.Changed, e.Error);
        Assert.False(File.Exists(Full(Ilse)));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void ASaveJustBeforeTheFirstTryIsLeftOnEveryOs()
    {
        var builtOn = File.ReadAllBytes(Full(Ilse));
        var theirs = Bytes("their whole save");
        var store = new FileSystemDenStore(_root)
        {
            // After the temporary file is written and flushed, which can take a while, and before the move.
            BeforeReplace = _ => File.WriteAllBytes(Full(Ilse), theirs),
        };

        var e = Assert.Throws<DenStoreException>(() => store.WriteFile(Ilse, Bytes("x"), DenWriteMode.Replace, builtOn));

        Assert.Equal(DenStoreError.Changed, e.Error);
        Assert.Equal(theirs, File.ReadAllBytes(Full(Ilse)));
        Assert.Empty(Directory.EnumerateFiles(Full(Characters), FileSystemDenStore.TempPrefix + "*"));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AWriteThatMatchesWhatItBuiltOnGoesThroughOnEveryOs()
    {
        var saved = Bytes("the new note");

        Store().WriteFile(Ilse, saved, DenWriteMode.Replace, File.ReadAllBytes(Full(Ilse)));

        Assert.Equal(saved, File.ReadAllBytes(Full(Ilse)));
    }

    [Fact]
    [Trait("Scenario", "SC-STORE-6")]
    public void AMoveOrDeleteLeavesASaveMadeWhileTheStoreWaits()
    {
        SkipUnlessWindows();
        var builtOn = File.ReadAllBytes(Full(Ilse));
        var moved = $"{Characters}/ilse.md";
        void Try(Action<FileSystemDenStore> write)
        {
            // No sharing of delete, so a move or delete is refused while it's open.
            using var saving = new FileStream(Full(Ilse), FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            using var retries = new RetryCounter(_root, count =>
            {
                if (count == 1)
                {
                    saving.SetLength(0);
                    saving.Write(Bytes("their whole save"));
                    saving.Dispose();
                }
            });

            Assert.Equal(DenStoreError.Changed, Assert.Throws<DenStoreException>(() => write(Store())).Error);
            Assert.Equal(Bytes("their whole save"), File.ReadAllBytes(Full(Ilse)));
            File.WriteAllBytes(Full(Ilse), builtOn);
        }

        Try(store => store.MoveFile(Ilse, moved, builtOn));
        Assert.False(File.Exists(Full(moved)));
        Try(store => store.DeleteFile(Ilse, builtOn));
    }

    [Theory]
    [InlineData("/", "/universes/x.md", true)]
    [InlineData("/den", "/den/universes/x.md", true)]
    [InlineData("/den", "/den", false)]
    [InlineData("/den", "/denmark/x.md", false)]
    [InlineData("/", "/", false)]
    public void ADenAtTheRootOfAFileSystemCanBeWritten(string root, string full, bool inside)
    {
        var separator = Path.DirectorySeparatorChar.ToString();
        var drive = OperatingSystem.IsWindows() ? "C:" : "";
        Assert.Equal(inside, FileSystemDenStore.IsInside(drive + root.Replace("/", separator), drive + full.Replace("/", separator)));
    }

    // ----- a store that lives as long as its host -----

    [Fact]
    public void ReadsSeeAFolderRenamedOutsideTheStore()
    {
        // A long-lived store, as in the MCP server: something else renames
        // characters/ to Characters/ between two reads.
        var store = Store();
        Assert.True(store.FileExists(Ilse));
        var renamed = Ilse.Replace("/characters/", "/Characters/", StringComparison.Ordinal);

        Directory.Move(Full(Characters), Full(Characters) + "-moving");
        Directory.Move(Full(Characters) + "-moving", Full(Characters.Replace("characters", "Characters", StringComparison.Ordinal)));

        Assert.False(store.FileExists(Ilse));
        Assert.True(store.FileExists(renamed));
        store.WriteFile(renamed, Bytes("---\nname: Ilse Tarrow\n---\n"), DenWriteMode.Replace);
        Assert.Equal(DenStoreError.CaseConflict, Assert.Throws<DenStoreException>(() => store.WriteFile(Ilse, Bytes("x"), DenWriteMode.Replace)).Error);
    }

    [Fact]
    public void ASnapshotReadsOneLoad()
    {
        var store = Store();

        var snapshot = store.Snapshot();

        Assert.Equal(store.Root, snapshot.Root);
        Assert.True(snapshot.FileExists(Ilse));
    }

    // ----- read-only stores -----

    [Fact]
    [Trait("Scenario", "SC-STORE-4")]
    public void TheBuiltInPacksRefuseEveryWrite()
    {
        IDenStore packs = BuiltInPacks.Instance;
        const string thread = "core/types/thread.yaml";
        var before = packs.ReadFile(thread);

        var refusals = new[]
        {
            Assert.Throws<DenStoreException>(() => packs.WriteFile(thread, Bytes("name: thread\n"))),
            Assert.Throws<DenStoreException>(() => packs.WriteFile("core/types/ship.yaml", Bytes("name: ship\n"), DenWriteMode.Create)),
            Assert.Throws<DenStoreException>(() => packs.DeleteFile(thread)),
            Assert.Throws<DenStoreException>(() => packs.MoveFile(thread, "core/types/plot.yaml")),
            Assert.Throws<DenStoreException>(() => packs.MoveFolder("core", "mine")),
            Assert.Throws<DenStoreException>(() => packs.DeleteFolder("core/templates")),
        };

        Assert.True(packs.IsReadOnly);
        Assert.All(refusals, e => Assert.Equal(DenStoreError.ReadOnly, e.Error));
        Assert.Equal($"'{thread}' can't be written: the built-in packs ship with Wolfden and can't be changed. To change a built-in type, extend it from a pack in the den's packs/ folder (types.md).", refusals[0].Message);
        Assert.Equal(before, packs.ReadFile(thread));
        Assert.False(packs.FileExists("core/types/ship.yaml"));
    }

    [Fact]
    public void AStoreOnDiskCanBeWritten()
    {
        Assert.False(Store().IsReadOnly);
    }

    // ----- reads through the store -----

    [Fact]
    public void TheStoreReadsTheDenAsTheSourceOnDiskDoes()
    {
        IDenSource store = Store();
        IDenSource source = new FileSystemDenSource(_root);

        Assert.Equal(source.GetDirectories("universes"), store.GetDirectories("universes"));
        Assert.Equal(source.GetFiles(Characters), store.GetFiles(Characters));
        Assert.Equal(source.ReadFile(Ilse), store.ReadFile(Ilse));
        Assert.Equal(source.TryReadFile("nothing.md"), store.TryReadFile("nothing.md"));
        Assert.True(store.FolderExists(Characters));
        Assert.False(store.FileExists($"{Characters}/Ilse-Tarrow.md"));
        Assert.Equal(
            Describe(Wolfden.Core.Checks.DenCheck.Run(source)),
            Describe(Wolfden.Core.Checks.DenCheck.Run(store)));
    }

    // ----- watching -----

    [Fact]
    public void TheStoreReportsChangesWithDenPaths()
    {
        var store = Store();
        var changes = new BlockingCollection<DenChange>();
        var observer = new Recorder(changes);
        const string wren = $"{Characters}/wren-ashby.md";

        using (store.Watch(observer))
        {
            store.WriteFile(wren, Bytes("---\nname: Wren Ashby\n---\n"), DenWriteMode.Create);
            WaitFor(changes, c => c.Path == wren && c.Kind is DenChangeKind.Renamed or DenChangeKind.Created or DenChangeKind.Changed);

            store.DeleteFile(wren);
            WaitFor(changes, c => c.Path == wren && c.Kind == DenChangeKind.Deleted);
        }

        Assert.Empty(observer.Errors);
        while (changes.TryTake(out _))
        {
        }

        File.AppendAllText(Full(Ilse), "\nAfter the watch.\n");
        Assert.False(changes.TryTake(out _, TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void ARenameOutOfAndIntoTheDenRootIsReportedAsDeletedAndCreated()
    {
        // A FileSystemWatcher reports only paths under the den, so the
        // others are reported by DenPath's rules, checked here directly.
        Assert.Null(FileSystemDenStore.DenPath(_root, _root));
        Assert.Null(FileSystemDenStore.DenPath(_root, Path.Combine(_outside, "x.md")));
        Assert.Equal("universes/x.md", FileSystemDenStore.DenPath(_root, Path.Combine(_root, "universes", "x.md")));
        Assert.Equal(".wolfden/index.db", FileSystemDenStore.DenPath(_root, Path.Combine(_root, ".wolfden", "index.db")));
    }

    private static string Describe(Wolfden.Core.Checks.DenCheck check) =>
        string.Join("\n", check.Diagnostics.Select(d => d.ToString()));

    private static T WaitFor<T>(BlockingCollection<T> items, Func<T, bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (items.TryTake(out var item, TimeSpan.FromMilliseconds(200)) && done(item))
            {
                return item;
            }
        }

        throw new TimeoutException("Nothing matching within 30 s.");
    }

    private static void SkipUnlessWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows keeps an open file from being replaced.");
        }
    }

    /// <summary>Counts the store's retries (<see cref="FileSystemDenStore.Retrying"/>) of paths in one den, while it's alive.</summary>
    private sealed class RetryCounter : IDisposable
    {
        private readonly string _root;
        private readonly Action<int>? _onRetry;
        private int _count;

        public RetryCounter(string root, Action<int>? onRetry = null)
        {
            _root = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            _onRetry = onRetry;
            FileSystemDenStore.Retrying += OnRetrying;
        }

        public int Count => Volatile.Read(ref _count);

        public void Dispose() => FileSystemDenStore.Retrying -= OnRetrying;

        private void OnRetrying(string full)
        {
            if (full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            {
                var count = Interlocked.Increment(ref _count);
                _onRetry?.Invoke(count);
            }
        }
    }

    private sealed class Recorder(BlockingCollection<DenChange> changes) : IDenChangeObserver
    {
        public ConcurrentBag<Exception> Errors { get; } = [];

        public void Changed(DenChange change) => changes.Add(change);

        public void Lost() => Errors.Add(new InternalBufferOverflowException());

        public void Broken(Exception error) => Errors.Add(error);
    }

    /// <summary>
    /// Makes <paramref name="link"/> a link to the folder <paramref name="target"/>:
    /// a symbolic link where the OS allows it, else a junction on Windows,
    /// else the test is skipped. Disposing it removes the link, not what it
    /// links to.
    /// </summary>
    private static Link LinkFolder(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return new Link(link);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Windows without developer mode or admin rights; try a junction, which needs neither.
        }

        if (OperatingSystem.IsWindows())
        {
            using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            mklink.WaitForExit();
            if (mklink.ExitCode == 0 && Directory.Exists(link))
            {
                return new Link(link);
            }
        }

        Assert.Skip("This system doesn't allow folder links to be created.");
        return null!;
    }

    private sealed class Link(string path) : IDisposable
    {
        public void Dispose() => Directory.Delete(path);
    }

    private static string ShortPath(string full)
    {
        var buffer = new StringBuilder(1024);
        var length = GetShortPathName(full, buffer, buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : full;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);
}

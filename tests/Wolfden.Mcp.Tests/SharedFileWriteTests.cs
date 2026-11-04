using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Wolfden.Core.History;
using Wolfden.Git;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// Views read a file another program has open to write; what a write builds on
/// doesn't, so it's refused rather than built on part of a save (#298). These
/// go through every write path. A refusal can only happen on Windows: on Linux
/// and macOS a program writing a file doesn't normally say so, and the tests
/// that need it say that.
/// </summary>
public sealed class SharedFileWriteTests : IDisposable
{
    private const string Book = "universes/dracula/books/dracula";
    private const string BookFile = Book + "/book.md";
    private const string UniverseFile = "universes/dracula/universe.md";
    private const string Swales = Book + "/entities/characters/mr-swales.md";
    private const string Lucy = Book + "/entities/characters/lucy-westenra.md";
    private const string Notes = "universes/dracula/entities/notes";

    /// <summary>A note whose second half has links every den-wide write below cares about.</summary>
    private const string Note = Notes + "/whitby-dialect.md";

    /// <summary>A note that links to nothing the writes below change.</summary>
    private const string Elsewhere = Notes + "/whitby-almanac.md";

    private const string Head = "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z1\ntype: note\nname: Whitby dialect\nstatus: draft\n---\n\nAll about the dialect. ";
    private const string Tail = "See [[Mr. Swales]], [[Mr. Swales#Voice]], and [[the Count]].\n\nThe last words of the save.\n";
    private const string LastWords = "The last words of the save.";

    private readonly TempDen _den = TempDen.Dracula();

    public SharedFileWriteTests()
    {
        _den.Write(Note, Head + Tail);
        _den.Write(Elsewhere, "---\nid: 01M4TF5F0EKGGWD5FNVBAEY8Z2\ntype: note\nname: Whitby almanac\nstatus: draft\n---\n\nFog, mostly.\n");
    }

    private DenTools Tools => new(_den.Root);

    public void Dispose() => _den.Dispose();

    private FileStream OpenToWrite(string path) => new(Full(path), FileMode.Open, FileAccess.Write, FileShare.Read);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static TheoryData<string> WritesThatReadTheWholeDen() =>
    [
        "rename_entity", "change_alias", "rename_section", "move_entity", "copy_entity", "create_entity", "add_idea_new_note",
    ];

    private void CallDenWide(string tool, string swales, string count)
    {
        var tools = Tools;
        _ = tool switch
        {
            "rename_entity" => (object)tools.RenameEntity("Mr. Swales", "Old Swales", swales),
            "change_alias" => tools.ChangeAlias("Count Dracula", "the Count", count, newAlias: "the Nobleman"),
            "rename_section" => tools.RenameSection("Mr. Swales", "Voice", "Dialect", swales),
            "move_entity" => tools.MoveEntity("Mr. Swales", swales, keepEntry: true),
            "copy_entity" => tools.CopyEntity("Mr. Swales", "Young Swales"),
            "create_entity" => tools.CreateEntity("note", "Whitby weather"),
            "add_idea_new_note" => tools.AddIdea("Fog at the abbey.", name: "Whitby fog"),
            "create_book" => tools.CreateBook("Fresh Book"),
            "create_series" => tools.CreateSeries("Fresh Series"),
            _ => throw new ArgumentException(tool),
        };
    }

    private (string Swales, string Count) Hashes() => (Tools.GetEntity("Mr. Swales").Entity!.Hash!, Tools.GetEntity("Count Dracula").Entity!.Hash!);

    // Views.

    [Fact]
    [Trait("Scenario", "SC-CHECK-64")]
    public void PagesAndCheckSeeANoteOpenElsewhereAndAWrongNameDoesntBlameIt()
    {
        using (OpenToWrite(Note))
        {
            Assert.Equal("Whitby dialect", Tools.GetEntity("Whitby dialect").Entity!.Name);
            Assert.NotEmpty(Tools.GetSource("Whitby dialect").Hash);
            Assert.DoesNotContain(Tools.Check().Diagnostics, d => d.Rule == "F-GEN-5");

            var wrong = Assert.Throws<DenToolException>(() => Tools.GetEntity("Nobody at all"));
            Assert.Equal(ToolErrorCodes.UnknownEntity, wrong.Code);
            Assert.DoesNotContain(Note, wrong.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-64")]
    public void ReadToolsSeeABookFileOpenElsewhere()
    {
        using (OpenToWrite(BookFile))
        {
            var tools = Tools;
            Assert.NotEmpty(tools.GetChapter("dracula").Chapters);
            Assert.NotEmpty(tools.GetChapterDefaults(book: "dracula").Hash);
            Assert.Equal("draft", tools.GetBookFreeze("dracula").Status);
            Assert.NotEmpty(tools.GetFileSource(BookFile).Hash);
            Assert.NotEmpty(tools.ListTypes().Types);
        }
    }

    // Writes, on Windows: every path is refused rather than built on what's open.

    [Theory]
    [MemberData(nameof(WritesThatReadTheWholeDen))]
    [Trait("Scenario", "SC-MCP-43")]
    public void AWriteThatReadsTheWholeDenIsRefusedNamingTheNoteOpenElsewhereOnWindows(string tool)
    {
        SkipUnlessWindows();
        var (swales, count) = Hashes();
        var before = Files();

        DenToolException refused;
        using (OpenToWrite(Elsewhere))
        {
            refused = Assert.Throws<DenToolException>(() => CallDenWide(tool, swales, count));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(Elsewhere, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());

        CallDenWide(tool, swales, count);
    }

    [Theory]
    [InlineData("create_book", UniverseFile)]
    [InlineData("create_book", BookFile)]
    [InlineData("create_series", UniverseFile)]
    [InlineData("create_series", BookFile)]
    [Trait("Scenario", "SC-MCP-43")]
    public void ANewBookOrSeriesIsRefusedNamingTheFileThatsOpenInItsStoryOrderOnWindows(string tool, string held)
    {
        SkipUnlessWindows();
        var (swales, count) = Hashes();
        var before = Files();

        DenToolException refused;
        using (OpenToWrite(held))
        {
            refused = Assert.Throws<DenToolException>(() => CallDenWide(tool, swales, count));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(held, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());

        CallDenWide(tool, swales, count);
    }

    public static TheoryData<string> EditsOfOneNote() =>
        ["replace_lead", "append_section", "update_fields", "remove_section", "replace_file", "replace_file_at"];

    private void CallEdit(string tool, string hash, string text = "")
    {
        var tools = Tools;
        var changed = text.ReplaceLineEndings("\n") + "\nMore.\n";
        _ = tool switch
        {
            "replace_lead" => tools.ReplaceLead("Mr. Swales", "A new opening.\n", hash),
            "append_section" => tools.AppendSection("Mr. Swales", "Voice", "More.\n", hash),
            "update_fields" => tools.UpdateFields("Mr. Swales", hash, Json("""[{"op": "set", "path": "status", "value": "canon"}]""")),
            "remove_section" => tools.RemoveSection("Mr. Swales", 2, "Arc", hash),
            "replace_file" => tools.ReplaceFile("Mr. Swales", changed, hash),
            "replace_file_at" => tools.ReplaceFileAt(Swales, changed, hash),
            _ => throw new ArgumentException(tool),
        };
    }

    [Theory]
    [MemberData(nameof(EditsOfOneNote))]
    [Trait("Scenario", "SC-MCP-43")]
    public void AnEditIsRefusedNamingItsNoteWhileThatsOpenElsewhereOnWindows(string tool)
    {
        SkipUnlessWindows();
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var before = _den.Read(Swales);

        DenToolException refused;
        using (OpenToWrite(Swales))
        {
            refused = Assert.Throws<DenToolException>(() => CallEdit(tool, hash, before));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(Swales, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, _den.Read(Swales));
    }

    [Theory]
    [InlineData(BookFile)]
    [InlineData(UniverseFile)]
    [InlineData("den.yaml")]
    [Trait("Scenario", "SC-MCP-43")]
    public void AnEditNamesTheFileOpenAboveItsNoteAndDoesntBlameTheNoteOnWindows(string held)
    {
        SkipUnlessWindows();
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var before = Files();

        DenToolException refused;
        using (OpenToWrite(held))
        {
            refused = Assert.Throws<DenToolException>(() => CallEdit("append_section", hash));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(held, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("has errors", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());
    }

    [Theory]
    [InlineData("add_chapter")]
    [InlineData("update_chapter_fields")]
    [InlineData("update_chapter_defaults")]
    [InlineData("freeze")]
    [Trait("Scenario", "SC-MCP-43")]
    public void AWriteToABooksFileNamesItWhileItsOpenElsewhereOnWindows(string tool)
    {
        SkipUnlessWindows();
        var tools = Tools;
        var hash = tools.GetBookFreeze("dracula").Hash;
        var before = Files();

        DenToolException refused;
        using (OpenToWrite(BookFile))
        {
            refused = Assert.Throws<DenToolException>(() => CallBookWrite(tools, tool, hash));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(BookFile, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());

        // It isn't a book that's gone: the same call works once it's closed.
        CallBookWrite(tools, tool, hash);
    }

    private static void CallBookWrite(DenTools tools, string tool, string hash)
    {
        _ = tool switch
        {
            "add_chapter" => (object)tools.AddChapter("dracula", "A new chapter", expectedHash: hash),
            "update_chapter_fields" => tools.UpdateChapterFields("dracula", "1", hash, Json("""[{"op": "set", "field": "narration", "value": "third"}]""")),
            "update_chapter_defaults" => tools.UpdateChapterDefaults(hash, Json("""[{"op": "set", "field": "narration", "value": "third"}]"""), book: "dracula"),
            "freeze" => tools.FreezeBook("dracula", hash),
            _ => throw new ArgumentException(tool),
        };
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-43")]
    public void AnUnfreezeNamesTheBookFileOpenElsewhereOnWindows()
    {
        SkipUnlessWindows();
        var tools = Tools;
        var hash = tools.FreezeBook("dracula", tools.GetBookFreeze("dracula").Hash).Hash;

        DenToolException refused;
        using (OpenToWrite(BookFile))
        {
            refused = Assert.Throws<DenToolException>(() => tools.UnfreezeBook("dracula", hash));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(BookFile, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-43")]
    public void AFreezeWithoutHistoryIsRefusedWhileANoteInThatBookIsOpenElsewhereOnWindows()
    {
        SkipUnlessWindows();
        var tools = Tools;
        var hash = tools.GetBookFreeze("dracula").Hash;
        var before = Files();

        DenToolException refused;
        using (OpenToWrite(Lucy))
        {
            refused = Assert.Throws<DenToolException>(() => tools.FreezeBook("dracula", hash));
        }

        Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
        Assert.Contains(Lucy, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Files());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-38")]
    public void AWriteToOneFileIsntStoppedByANoteOpenElsewhere()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;

        using (OpenToWrite(Elsewhere))
        {
            Assert.True(Tools.AppendSection("Mr. Swales", "Voice", "More.\n", hash).Changed);
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
    [Trait("Scenario", "SC-MCP-43")]
    public void AnotherNoteChangingBeforeTheSaveIsNamedAsWhatChanged()
    {
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var tools = new DenTools(_den.Root) { BeforeSaveComparison = () => _den.Write(Note, Head + Tail + "And more.\n") };

        var refused = Assert.Throws<DenToolException>(() => tools.RenameEntity("Mr. Swales", "Old Swales", hash));

        Assert.Equal(ToolErrorCodes.StaleFile, refused.Code);
        Assert.Equal(Note, refused.StalePath);
        Assert.Contains(Note, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-43")]
    public void AnotherNoteOpenedBeforeTheSaveIsNamedAsOpenRatherThanChangedOnWindows()
    {
        SkipUnlessWindows();
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        FileStream? held = null;
        var tools = new DenTools(_den.Root) { BeforeSaveComparison = () => held = OpenToWrite(Note) };
        var before = Files();

        try
        {
            var refused = Assert.Throws<DenToolException>(() => tools.RenameEntity("Mr. Swales", "Old Swales", hash));
            Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
            Assert.Contains(Note, refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            held?.Dispose();
        }

        Assert.Equal(before, Files());
    }

    // A real separate process, stalled in the middle of a save.

    [Fact]
    [Trait("Scenario", "SC-MCP-43")]
    public async Task ARenameIsRefusedByAStalledSaveInAnotherProcessAndTheNoteIsLeftWhole()
    {
        SkipUnlessWindows();
        var hash = Tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var before = Files();

        await using (var saving = await StalledWriter.StartAsync(Full(Note), Head + Tail, Head.Length))
        {
            // A page can see the part saved so far: that's the read a write mustn't build on.
            Assert.DoesNotContain("Mr. Swales", Tools.GetEntity("Whitby dialect").Entity!.Lead!, StringComparison.Ordinal);

            var refused = Assert.Throws<DenToolException>(() => Tools.RenameEntity("Mr. Swales", "Old Swales", hash));
            Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
            Assert.Contains(Note, refused.Message, StringComparison.Ordinal);

            await saving.FinishAsync();
        }

        // Nothing was written, and the save is whole.
        Assert.Equal(before, Files());

        // The same call goes through now, and the link that was still to come follows.
        Assert.True(Tools.RenameEntity("Mr. Swales", "Old Swales", hash).Changed);
        var after = _den.Read(Note);
        Assert.Contains("[[Old Swales]]", after, StringComparison.Ordinal);
        Assert.Contains(LastWords, after, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stalls short enough for a save's retries to outlast them, which is how a
    /// write built on a part read overwrote the finished save (review of #307).
    /// Whether a write is refused or goes ahead after the save finishes depends
    /// on timing, so this asserts what must hold either way: the save's text is there.
    /// </summary>
    [Theory]
    [InlineData("rename_entity", 100)]
    [InlineData("rename_entity", 700)]
    [InlineData("add_chapter", 100)]
    [InlineData("add_chapter", 700)]
    [InlineData("append_section", 100)]
    [InlineData("append_section", 700)]
    [Trait("Scenario", "SC-MCP-43")]
    public async Task AShortStallNeverLosesTheRestOfTheSave(string tool, int stallMs)
    {
        SkipUnlessWindows();
        var tools = Tools;
        var noteHash = tools.GetEntity("Whitby dialect").Entity!.Hash!;
        var swales = tools.GetEntity("Mr. Swales").Entity!.Hash!;
        var path = tool == "add_chapter" ? BookFile : Note;
        var whole = tool == "add_chapter" ? _den.Read(BookFile) + "\n" + LastWords + "\n" : Head + Tail;

        await using (var saving = await StalledWriter.StartAsync(Full(path), whole, whole.Length / 2, stallMs))
        {
            try
            {
                _ = tool switch
                {
                    "rename_entity" => (object)tools.RenameEntity("Mr. Swales", "Old Swales", swales),
                    "add_chapter" => tools.AddChapter("dracula", "A new chapter"),
                    "append_section" => tools.AppendSection("Whitby dialect", "Later", "More.\n", noteHash, newSection: true),
                    _ => throw new ArgumentException(tool),
                };
            }
            catch (DenToolException e) when (e.Code is ToolErrorCodes.DenUnreadable or ToolErrorCodes.StaleFile or ToolErrorCodes.WriteFailed or ToolErrorCodes.UnknownEntity or ToolErrorCodes.UnknownScope)
            {
                // Refused: fine.
            }

            await saving.FinishAsync();
        }

        Assert.Contains(LastWords, _den.Read(path), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-43")]
    public async Task AFreezeIsRefusedByAStalledSaveInAnotherProcessAndTagsNothing()
    {
        SkipUnlessWindows();
        var config = Path.Combine(_den.Root, "..", $"gitconfig-{Guid.NewGuid():N}");
        File.WriteAllText(config, "[user]\n\tname = Test Writer\n\temail = writer@example.com\n[commit]\n\tgpgsign = false\n[tag]\n\tgpgsign = false\n[core]\n\tautocrlf = false\n", new UTF8Encoding(false));
        try
        {
            var git = new GitOptions
            {
                Environment = new Dictionary<string, string?> { ["GIT_CONFIG_GLOBAL"] = config, ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_DIR"] = null, ["GIT_WORK_TREE"] = null, ["GIT_INDEX_FILE"] = null },
            };
            RunGit(git, "init", "--quiet");
            RunGit(git, "add", "--all");
            RunGit(git, "commit", "--quiet", "-m", "Start");
            var head = RunGit(git, "rev-parse", "HEAD");
            var tools = new DenTools(_den.Root) { History = () => GitHistory.ForDen(_den.Root, git) };
            var whole = _den.Read(Lucy);
            var book = _den.Read(BookFile);

            await using (var saving = await StalledWriter.StartAsync(Full(Lucy), whole, whole.Length / 2))
            {
                var refused = Assert.Throws<DenToolException>(() => tools.FreezeBook("dracula", null));
                Assert.Equal(ToolErrorCodes.DenUnreadable, refused.Code);
                Assert.Contains(Lucy, refused.Message, StringComparison.Ordinal);
                await saving.FinishAsync();
            }

            Assert.Equal(whole, _den.Read(Lucy));
            Assert.Equal(book, _den.Read(BookFile));
            Assert.Equal(head, RunGit(git, "rev-parse", "HEAD"));
            Assert.Empty(RunGit(git, "tag", "--list"));

            var frozen = tools.FreezeBook("dracula", null);
            Assert.Equal(FreezeSnapshot.Tagged, frozen.Snapshot);
        }
        finally
        {
            File.Delete(config);
        }
    }

    private string RunGit(GitOptions git, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = _den.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        foreach (var (name, value) in git.Environment)
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed");
        _ = error;
        return output;
    }

    private static void SkipUnlessWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows can refuse a program that's writing a file: on Linux and macOS it doesn't normally say so.");
        }
    }

    private string Full(string path) => Path.Combine(_den.Root, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Every file of the den but the index, with its text.</summary>
    private Dictionary<string, string> Files() =>
        Directory.EnumerateFiles(_den.Root, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(_den.Root, f).StartsWith(".wolfden", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetRelativePath(_den.Root, f), File.ReadAllText);
}

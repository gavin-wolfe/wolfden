using Wolfden.Core.Editing;

namespace Wolfden.Core.Tests;

/// <summary>
/// Planning a new chapter (#215; format.md §6.4, F-BOOK-3..6): its key, its
/// display number, and the numbers after it that change. Keys never change.
/// </summary>
public sealed class NewChapterTests
{
    private static ChapterSlot C(string key, long? number = null) => new(key, number);

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void AnInsertedChapterGetsTheFirstUnusedLetterSuffix()
    {
        HashSet<string> none = [];
        Assert.Equal("2b", NewChapter.KeyAfter("2", none));
        Assert.Equal("2c", NewChapter.KeyAfter("2", new HashSet<string> { "2b" }));
        Assert.Equal("2d", NewChapter.KeyAfter("2", new HashSet<string> { "2b", "2c" }));

        // A key that ends in a letter starts at a: after 2b, 2ba (maintainer decision on #215).
        Assert.Equal("2ba", NewChapter.KeyAfter("2b", none));
        Assert.Equal("2bb", NewChapter.KeyAfter("2b", new HashSet<string> { "2ba" }));
        Assert.Equal("prologuea", NewChapter.KeyAfter("prologue", none));
        Assert.Equal("ch-07b", NewChapter.KeyAfter("ch-07", none));

        // Every single letter taken: two letters.
        var taken = Enumerable.Range('b', 25).Select(c => "2" + (char)c).ToHashSet();
        Assert.Equal("2ba", NewChapter.KeyAfter("2", taken));

        // r and a digit is reserved for rows (F-KEY-2), but a suffix never makes that form; too long gives none.
        Assert.Equal("ra", NewChapter.KeyAfter("r", none));
        Assert.Equal("r-2b", NewChapter.KeyAfter("r-2", none));
        Assert.Null(NewChapter.KeyAfter(new string('a', 64), none));
        Assert.Equal(new string('a', 63) + "a", NewChapter.KeyAfter(new string('a', 63), none));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void AtTheEndItsTheNextWholeNumber()
    {
        Assert.Equal("1", NewChapter.NextWholeNumber([]));
        Assert.Equal("1", NewChapter.NextWholeNumber(["prologue", "epilogue"]));
        Assert.Equal("4", NewChapter.NextWholeNumber(["prologue", "1", "2", "2b", "3"]));
        Assert.Equal("6", NewChapter.NextWholeNumber(["1", "5", "2"]));
        Assert.Equal("8", NewChapter.NextWholeNumber(["07"]));
        Assert.Equal("100000000000000000000", NewChapter.NextWholeNumber(["99999999999999999999"]));
        Assert.Null(NewChapter.NextWholeNumber([new string('9', 64)]));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void AnEmptyBookGetsChapterOneNumberedOne()
    {
        Assert.Equal(new ChapterPlan("1", 0, 1, []), NewChapter.Plan([], null), PlanComparer.Instance);
        Assert.Throws<ArgumentException>(() => NewChapter.Plan([], "1"));
        Assert.Throws<ArgumentException>(() => NewChapter.Plan([C("1", 1)], "9"));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void InsertingRenumbersTheChaptersAfterItAndAddingAtTheEndRenumbersNone()
    {
        // Ember: prologue, 1, 2, 2b (3), 3 (4).
        ChapterSlot[] ember = [C("prologue"), C("1", 1), C("2", 2), C("2b", 3), C("3", 4)];

        var end = NewChapter.Plan(ember, null)!;
        Assert.Equal(("4", 5, 5L), (end.Key, end.Index, end.Number));
        Assert.Empty(end.Renumbered);

        // After the last chapter is the end, so it's a whole number too.
        Assert.Equal("4", NewChapter.Plan(ember, "3")!.Key);

        var after2 = NewChapter.Plan(ember, "2")!;
        Assert.Equal(("2c", 3, 3L), (after2.Key, after2.Index, after2.Number));
        Assert.Equal([new Renumbering("2b", 4, 3, 4), new Renumbering("3", 5, 4, 5)], after2.Renumbered);

        var after2b = NewChapter.Plan(ember, "2b")!;
        Assert.Equal(("2ba", 4, 4L), (after2b.Key, after2b.Index, after2b.Number));
        Assert.Equal([new Renumbering("3", 5, 4, 5)], after2b.Renumbered);

        // After the unnumbered prologue: it takes the first number, and every number after moves up.
        var afterPrologue = NewChapter.Plan(ember, "prologue")!;
        Assert.Equal(("prologuea", 1, 1L), (afterPrologue.Key, afterPrologue.Index, afterPrologue.Number));
        Assert.Equal(["1", "2", "2b", "3"], afterPrologue.Renumbered.Select(r => r.Key));
        Assert.Equal([2L, 3, 4, 5], afterPrologue.Renumbered.Select(r => r.To));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void HandSetNumbersAreMovedOnlyAsFarAsNeeded()
    {
        // A gap absorbs the insert; an interlude with no number is passed over and keeps none.
        ChapterSlot[] gap = [C("1", 1), C("2", 2), C("3", 3), C("interlude"), C("4", 10), C("5", 11)];
        var plan = NewChapter.Plan(gap, "1")!;
        Assert.Equal(("1b", 2L), (plan.Key, plan.Number));
        Assert.Equal([new Renumbering("2", 2, 2, 3), new Renumbering("3", 3, 3, 4)], plan.Renumbered);

        // After an interlude, the number follows the numbered chapter before it.
        var afterInterlude = NewChapter.Plan(gap, "interlude")!;
        Assert.Equal(("interludea", 4, 4L), (afterInterlude.Key, afterInterlude.Index, afterInterlude.Number));
        Assert.Empty(afterInterlude.Renumbered);

        // A book with no numbers keeps none; at the end, too.
        ChapterSlot[] bare = [C("1"), C("2"), C("3"), C("epilogue")];
        Assert.Null(NewChapter.Plan(bare, "2")!.Number);
        Assert.Empty(NewChapter.Plan(bare, "2")!.Renumbered);
        Assert.Equal(("4", (long?)null), (NewChapter.Plan(bare, null)!.Key, NewChapter.Plan(bare, null)!.Number));

        // A book whose last numbered chapter isn't last: the end follows it.
        ChapterSlot[] epilogue = [C("1", 1), C("2", 2), C("epilogue")];
        Assert.Equal(("3", 3L), (NewChapter.Plan(epilogue, null)!.Key, NewChapter.Plan(epilogue, null)!.Number));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-71")]
    public void AnItemIsWrittenAsTheFormatWritesOne()
    {
        Assert.IsType<Yaml.YamlScalarValue>(NewChapter.Item("4", null, null, bare: true));
        var item = Assert.IsType<Yaml.YamlMappingValue>(NewChapter.Item("2b", 3, "The Long Watch", bare: true));
        Assert.Equal(["key", "number", "title"], item.Entries.Select(e => e.Key));
        Assert.Equal(["key"], Assert.IsType<Yaml.YamlMappingValue>(NewChapter.Item("4", null, null, bare: false)).Entries.Select(e => e.Key));
    }

    private sealed class PlanComparer : IEqualityComparer<ChapterPlan>
    {
        public static readonly PlanComparer Instance = new();

        public bool Equals(ChapterPlan? x, ChapterPlan? y) =>
            x is not null && y is not null && (x.Key, x.Index, x.Number) == (y.Key, y.Index, y.Number) && x.Renumbered.SequenceEqual(y.Renumbered);

        public int GetHashCode(ChapterPlan obj) => obj.Key.GetHashCode(StringComparison.Ordinal);
    }
}

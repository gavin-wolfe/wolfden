using static Wolfden.Index.Tests.IncrementalUpdateTests;

namespace Wolfden.Index.Tests;

/// <summary>
/// Chapter headings in the index (format.md F-CTR-6, #227): a chapter row
/// keeps what its heading is built from, and links are named when it's
/// read, so a note's new name shows after the watcher's update, without a
/// rebuild of the chapter rows.
/// </summary>
public sealed class ChapterHeadingIndexTests
{
    private const string Ember = "universes/starfall/series/long-dark/books/ember/book.md";
    private const string Mara = "universes/starfall/series/long-dark/entities/characters/mara-voss.md";

    private static string Headings(TempDen den, IReadOnlySet<string>? met = null, long? upTo = null)
    {
        using var index = DenIndex.Open(den.Root);
        var db = index.Context;
        var ember = db.Containers.Single(c => c.Kind == "book" && c.Key == "ember");
        var chapters = db.Chapters.Where(c => c.BookId == ember.Id).ToList().OrderBy(c => c.Index).ToList();
        var headings = ChapterHeadingRows.Headings(db, ember.UniverseId, chapters, upTo is { } at ? c => c.Ordinal <= at : null, met);
        return string.Join(" | ", chapters.Select(c => headings[c.Id] ?? "(none)"));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-77")]
    public void AHeadingNamesANoteByItsNameNow()
    {
        using var den = TempDen.Starfall();
        den.Write(Ember, """
            ---
            id: 01JAQKFBF5KZNWJ47TAN9ZT24M
            name: Ember
            chapter_heading: [pov, when]
            chapter_defaults: { pov: ["[[mara-voss]]"] }
            chapters:
              - { key: prologue, title: Before the Dark, fields: { pov: [], when: "2284" } }
              - { key: "1", number: 1, title: Kessan Reach }
              - { key: "2", number: 2, fields: { when: "2291" } }
            ---
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        var updater = new IndexUpdater(den.Root);
        updater.Rebuild("start");

        Assert.Equal("Before the Dark · 2284 | Kessan Reach · Mara Voss | Mara Voss — 2291", Headings(den));
        // As of a position: no fields after it, and a link to a note the reader hasn't met shows as written.
        Assert.Equal("Before the Dark · 2284 | Kessan Reach · mara-voss | mara-voss — 2291", Headings(den, met: new HashSet<string>()));
        using (var index = DenIndex.Open(den.Root))
        {
            var first = index.PointOf("starfall", "ember:1");
            Assert.Equal("Before the Dark · 2284 | Kessan Reach · Mara Voss | (none)", Headings(den, upTo: first));
        }

        den.Write(Mara, File.ReadAllText(Path.Combine(den.Root, Mara)).Replace("name: Mara Voss", "name: Mara Ellery", StringComparison.Ordinal));
        var update = updater.Apply(Batch(den, (WatcherChangeTypes.Changed, Mara, null)), out _);

        Assert.Equal(IndexUpdateKind.Updated, update!.Kind);
        Assert.Equal("Before the Dark · 2284 | Kessan Reach · Mara Ellery | Mara Ellery — 2291", Headings(den));
        AssertSameAsFullRebuild(den, "after the rename");
    }
}

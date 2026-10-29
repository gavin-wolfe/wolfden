using System.Text;
using Wolfden.Core.Editing;
using Wolfden.Core.Structure;

namespace Wolfden.Core.Tests;

/// <summary>
/// A new series' or book's file (#213; format.md §6.3, §6.4), and the write
/// check reading it as the den will, before it's saved.
/// </summary>
public sealed class NewContainerTests
{
    private const string Id = "01M61E94MCCVS4AW3XFZ8SRY5P";

    [Fact]
    [Trait("Scenario", "SC-WEB-69")]
    public void ABookFileHasItsIdNameAndChaptersAsTheFormatWritesThem()
    {
        Assert.Equal(
            $"---\nid: {Id}\nname: \"Ember: Book One\"\nchapters:\n  - {{ key: \"1\", number: 1 }}\n  - {{ key: \"2\", number: 2 }}\n---\n",
            Encoding.UTF8.GetString(NewContainer.File(Id, "Ember: Book One", 2).ToBytes()));
        Assert.Equal($"---\nid: {Id}\nname: The Long Dark\n---\n", Encoding.UTF8.GetString(NewContainer.File(Id, "The Long Dark").ToBytes()));

        Assert.Throws<ArgumentException>(() => NewContainer.File("not-an-id", "Ember"));
        Assert.Throws<ArgumentException>(() => NewContainer.File(Id, " "));
        Assert.Throws<ArgumentException>(() => NewContainer.File(Id, "Ember\nAsh"));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewContainer.File(Id, "Ember", -1));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-69")]
    public void TheWriteCheckReadsANewContainerFileWithTheListThatNamesIt()
    {
        var den = MemoryDenSource.FromDirectory(MemoryDenSource.StarfallRoot);
        var structure = DenStructure.Load(den);
        const string Path = "universes/starfall/series/long-dark/books/cinder/book.md";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Path] = NewContainer.File(Id, "Cinder", 3).ToBytes(),
            ["universes/starfall/series/long-dark/series.md"] =
                Encoding.UTF8.GetBytes("---\nid: 01JA3E28JT97KB6CQ643DZVMXX\nname: The Long Dark\nbooks: [ember, ash, cinder]\n---\n"),
        };

        var check = DenWriteCheck.Check(den, structure, files);

        Assert.False(check.HasErrors);
        Assert.Empty(check.Diagnostics[Path]);
        var book = check.Structure.FindUniverse("starfall")!.FindBook("cinder")!;
        Assert.Equal(("Cinder", 3, "long-dark"), (book.Name, book.Chapters.Count, book.Series!.Key));

        // A file that isn't where a container's file goes is still refused.
        Assert.Throws<ArgumentException>(() => DenWriteCheck.Check(
            den, structure, new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["universes/starfall/books/x/y/book.md"] = files[Path] }));
    }
}

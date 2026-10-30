using System.Net;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// Chapter headings built from chapter fields (format.md F-CTR-6, #227) in
/// <c>/api/hierarchy</c> and <c>/api/book/chapters</c>, over a copy of the
/// invented Starfall den.
/// </summary>
public sealed class ChapterHeadingEndpointTests : IDisposable
{
    private const string LongDark = "universes/starfall/series/long-dark";
    private const string Ember = LongDark + "/books/ember/book.md";
    private const string Series = LongDark + "/series.md";
    private const string Mara = LongDark + "/entities/characters/mara-voss.md";

    private readonly TempDen _den = TempDen.Starfall();

    public ChapterHeadingEndpointTests()
    {
        // The series heads its chapters with POV, place, and date; Kessan Reach is the place throughout.
        _den.Write(Series, """
            ---
            id: 01JA3E28JT97KB6CQ643DZVMXX
            name: The Long Dark
            books: [ember, ash]
            chapter_heading: [pov, setting, when]
            chapter_defaults: { setting: "[[kessan-reach]]" }
            ---
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        // Halden Pike enters at the start of Ash, so a reader of Ember hasn't met him.
        _den.Write(Ember, """
            ---
            id: 01JAQKFBF5KZNWJ47TAN9ZT24M
            name: Ember
            chapters:
              - { key: prologue, title: Before the Dark, fields: { when: "2284" } }
              - { key: "1", number: 1, fields: { pov: ["[[mara-voss]]"] } }
              - { key: "2", number: 2, fields: { pov: ["[[halden-pike]]", "[[mara-voss|the Commander]]"] } }
              - { key: 2b, number: 3, title: The Long Watch, fields: { pov: ["[[mara-voss]]"] } }
              - { key: "3", number: 4, fields: { pov: ["[[mara-voss]]"] } }
            ---
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    public void Dispose() => _den.Dispose();

    private static async Task<List<(string Key, string? Title, string? Heading)>> EmberChapters(Server server, string? asOf = null)
    {
        var hierarchy = await server.GetJsonAsync($"/api/hierarchy?universe=starfall{(asOf is null ? "" : $"&asOf={asOf}")}", HttpStatusCode.OK);
        var ember = hierarchy.GetProperty("universe").GetProperty("series").EnumerateArray()
            .Single(s => s.GetProperty("key").GetString() == "long-dark").GetProperty("books").EnumerateArray()
            .Single(b => b.GetProperty("key").GetString() == "ember");
        return ember.GetProperty("chapters").EnumerateArray()
            .Select(c => (c.GetProperty("key").GetString()!, c.GetProperty("title").GetString(), c.GetProperty("heading").GetString()))
            .ToList();
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-77")]
    public async Task TheHierarchyGivesEachChaptersHeading()
    {
        await using var server = await Server.StartAsync(new WolfdenApiOptions(_den.Root));

        Assert.Equal(
            [
                ("prologue", "Before the Dark", "Before the Dark · Kessan Reach — 2284"),
                ("1", null, "Mara Voss — Kessan Reach"),
                ("2", null, "Halden Pike & the Commander — Kessan Reach"),
                ("2b", "The Long Watch", "The Long Watch · Mara Voss — Kessan Reach"),
                ("3", null, "Mara Voss — Kessan Reach"),
            ],
            await EmberChapters(server));

        // Ash's chapters have no fields but inherit the series' place.
        var ash = (await server.GetJsonAsync("/api/hierarchy?universe=starfall", HttpStatusCode.OK)).GetProperty("universe").GetProperty("series")
            .EnumerateArray().Single(s => s.GetProperty("key").GetString() == "long-dark").GetProperty("books").EnumerateArray()
            .Single(b => b.GetProperty("key").GetString() == "ash").GetProperty("chapters")[0];
        Assert.Equal("Kessan Reach", ash.GetProperty("heading").GetString());

        // A rename follows: the heading names the note by its name now.
        _den.Write(Mara, _den.Read(Mara).Replace("name: Mara Voss", "name: Mara Venn", StringComparison.Ordinal));
        Assert.Equal("Mara Venn — Kessan Reach", (await EmberChapters(server))[1].Heading);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-77")]
    public async Task AsOfAPositionAHeadingNamesOnlyWhatTheReaderHasMet()
    {
        await using var server = await Server.StartAsync(new WolfdenApiOptions(_den.Root));

        // At ember:2, Halden Pike isn't met, so his link shows as written; a
        // chapter after the position shows its title alone (or none).
        Assert.Equal(
            [
                ("prologue", "Before the Dark", "Before the Dark · Kessan Reach — 2284"),
                ("1", null, "Mara Voss — Kessan Reach"),
                ("2", null, "halden-pike & the Commander — Kessan Reach"),
                ("2b", "The Long Watch", "The Long Watch"),
                ("3", null, null),
            ],
            await EmberChapters(server, "ember:2"));

        // At the prologue, Kessan Reach (from ember:1) isn't met yet.
        Assert.Equal("Before the Dark · kessan-reach — 2284", (await EmberChapters(server, "ember:prologue"))[0].Heading);
        // Once Pike is met, his name shows.
        Assert.Equal("Halden Pike & the Commander — Kessan Reach", (await EmberChapters(server, "ash:1"))[2].Heading);
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-77")]
    public async Task TheBooksChaptersGiveTheirHeadings()
    {
        await using var server = await Server.StartAsync(new WolfdenApiOptions(_den.Root));

        var book = await server.GetJsonAsync("/api/book/chapters?universe=starfall&book=ember", HttpStatusCode.OK);

        Assert.Equal(
            ["Before the Dark · Kessan Reach — 2284", "Mara Voss — Kessan Reach", "Halden Pike & the Commander — Kessan Reach", "The Long Watch · Mara Voss — Kessan Reach", "Mara Voss — Kessan Reach"],
            book.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("heading").GetString()));
        Assert.Equal(JsonValueKind.Null, book.GetProperty("chapters")[1].GetProperty("title").ValueKind);
    }
}

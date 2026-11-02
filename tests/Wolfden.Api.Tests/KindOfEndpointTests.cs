using System.Net;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// What follows a type's <c>kind_of</c> (types.md F-TYPE-13) beyond the link
/// check: a polity's page lists its members as a faction's does, a ref's field
/// type says which types its picker offers, the type list says what each type
/// is a kind of, and a filter by a type includes its kinds.
/// </summary>
public class KindOfEndpointTests
{
    private const string Polity = "universes/dracula/entities/polities/reach-compact.md";
    private const string Tarrow = "universes/dracula/entities/characters/ilse-tarrow.md";

    private static TempDen Den()
    {
        var den = TempDen.Dracula();
        den.Write("den.yaml", den.Read("den.yaml") + "packs: [space-opera]\n");
        den.Write(Polity, "---\ntype: polity\nname: Reach Compact\nstatus: canon\n---\n# Reach Compact\n");
        den.Write(Tarrow, "---\ntype: character\nname: Ilse Tarrow\nstatus: canon\nfields:\n  factions: [\"[[reach-compact]]\"]\n---\n# Ilse Tarrow\n");
        return den;
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-51")]
    public async Task APolitysPageListsItsMembersAsAFactionsDoes()
    {
        using var den = Den();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var page = await server.GetJsonAsync($"/api/entity?entity={Uri.EscapeDataString(Polity)}", HttpStatusCode.OK);

        var from = Assert.Single(page.GetProperty("entity").GetProperty("backlinks").EnumerateArray());
        Assert.Equal((Tarrow, "factions"), (from.GetProperty("path").GetString(), from.GetProperty("field").GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-51")]
    public async Task ARefsFieldTypeSaysWhichTypesItsPickerOffers()
    {
        using var den = Den();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var source = await server.GetJsonAsync($"/api/entity/source?entity={Uri.EscapeDataString(Tarrow)}", HttpStatusCode.OK);

        var factions = source.GetProperty("fieldTypes").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "factions");
        Assert.Equal(["faction"], Strings(factions.GetProperty("to")));
        Assert.Equal(["faction", "polity"], Strings(factions.GetProperty("accepts")));

        // A field that isn't a ref has none.
        var born = source.GetProperty("fieldTypes").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "born");
        Assert.Equal(JsonValueKind.Null, born.GetProperty("accepts").ValueKind);

        // And what it offers finds the polity, where the field's `to` alone wouldn't.
        var offered = string.Join(',', Strings(factions.GetProperty("accepts")));
        var found = await server.GetJsonAsync($"/api/entities/lookup?universe=dracula&query=reach&type={offered}", HttpStatusCode.OK);
        Assert.Single(found.GetProperty("matches").EnumerateArray());

        // A lookup follows the rule every type filter does, so the field's `to` alone finds it too.
        var byTo = await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=reach&type=faction", HttpStatusCode.OK);
        Assert.Equal("polity", Assert.Single(byTo.GetProperty("matches").EnumerateArray()).GetProperty("entity").GetProperty("type").GetString());
        var other = await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=reach&type=character", HttpStatusCode.OK);
        Assert.Empty(other.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-51")]
    public async Task TheTypeListSaysWhatEachTypeIsAKindOf()
    {
        using var den = Den();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var types = (await server.GetJsonAsync("/api/types", HttpStatusCode.OK)).GetProperty("types").EnumerateArray().ToList();

        Assert.Equal(["faction"], Strings(types.Single(t => t.GetProperty("key").GetString() == "polity").GetProperty("kindOf")));
        Assert.Empty(Strings(types.Single(t => t.GetProperty("key").GetString() == "species").GetProperty("kindOf")));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-51")]
    public async Task AChapterFieldSaysWhichTypesItsPickerOffers()
    {
        using var den = Den();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var book = await server.GetJsonAsync("/api/book/chapter-fields?universe=dracula&book=dracula", HttpStatusCode.OK);

        var setting = book.GetProperty("fieldTypes").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "setting");
        Assert.Equal(["location"], Strings(setting.GetProperty("to")));
        Assert.Equal(["location", "settlement", "ship", "star-system", "world"], Strings(setting.GetProperty("accepts")));
    }

    /// <summary>A world added by a test, which the reader meets at dracula:20.</summary>
    private const string FarVesta = "Far Vesta";

    /// <summary>The name and own type of each note in a place: every kind of location, by every route, and a character beside them.</summary>
    private static readonly Dictionary<string, string> Places = new()
    {
        ["Kessan Reach"] = "location",
        ["Vesta"] = "world",
        ["Little Vesta"] = "moon",
        ["Vesta Yards"] = "settlement",
        ["Tessaly"] = "ship",
        ["Lantern"] = "sentient-ship",
        ["Halo Station"] = "orbital-habitat",
        ["Ilse Tarrow"] = "character",
    };

    /// <summary>
    /// The space opera pack and a den-local one: a moon is a world, so a chain; a sentient ship is a ship and a character, so several parents; an orbital
    /// habitat is a ship and a settlement, so a diamond with location at its top.
    /// Each note holds the word "waymark", which nothing in the sample does.
    /// </summary>
    private static TempDen Filtering()
    {
        var den = TempDen.Dracula();
        den.Write("den.yaml", den.Read("den.yaml") + "packs: [space-opera, starfall]\n");
        den.Write("packs/starfall/pack.yaml", "name: Starfall\n");
        den.Write("packs/starfall/types/moon.yaml", "name: Moon\nplural: moons\nkind_of: world\n");
        den.Write("packs/starfall/types/sentient-ship.yaml", "name: Sentient Ship\nplural: sentient-ships\nkind_of: [ship, character]\n");
        den.Write("packs/starfall/types/orbital-habitat.yaml", "name: Orbital Habitat\nplural: orbital-habitats\nkind_of: [ship, settlement]\n");
        foreach (var (name, type) in Places)
        {
            den.Write(
                $"universes/dracula/entities/{type}s/{name.ToLowerInvariant().Replace(' ', '-')}.md",
                $"---\ntype: {type}\nname: {name}\nstatus: canon\n---\n# {name}\n\n## Overview\nA waymark.\n");
        }

        return den;
    }

    /// <summary>What <c>list_entities</c>, <c>search</c>, and <c>canon_as_of</c> give for a filter, through the same code as the MCP tools, as each result's name and own type, in order.</summary>
    private static async Task<(string Name, string Type)[][]> Filter(Server server, string type, string asOf = "")
    {
        var listed = await server.GetJsonAsync($"/api/entities?universe=dracula&type={type}{asOf}", HttpStatusCode.OK);
        var found = await server.GetJsonAsync($"/api/search?universe=dracula&query=waymark&type={type}{asOf}", HttpStatusCode.OK);
        var canon = await server.GetJsonAsync($"/api/canon?universe=dracula&type={type}{asOf}", HttpStatusCode.OK);
        return
        [
            NameAndType(listed.GetProperty("entities").EnumerateArray()),
            NameAndType(found.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("entity"))),
            NameAndType(canon.GetProperty("entities").EnumerateArray()),
        ];
    }

    private static (string Name, string Type)[] NameAndType(IEnumerable<JsonElement> notes) =>
        notes.Select(n => (n.GetProperty("name").GetString()!, n.GetProperty("type").GetString()!)).Where(n => Places.ContainsKey(n.Item1) || n.Item1 == FarVesta).ToArray();

    [Theory]
    [Trait("Scenario", "SC-CHECK-52")]
    [InlineData("location", "Halo Station,Kessan Reach,Lantern,Little Vesta,Tessaly,Vesta,Vesta Yards")] // a chain, several parents, and a diamond, each once
    [InlineData("world", "Little Vesta,Vesta")] // narrowing leaves out other locations
    [InlineData("moon", "Little Vesta")]
    [InlineData("ship", "Halo Station,Lantern,Tessaly")]
    [InlineData("settlement", "Halo Station,Vesta Yards")]
    [InlineData("character", "Ilse Tarrow,Lantern")] // the other parent
    public async Task FilteringByTypeIncludesItsKinds(string type, string names)
    {
        using var den = Filtering();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        foreach (var results in await Filter(server, type))
        {
            Assert.Equal(names.Split(','), results.Select(r => r.Name).Order(StringComparer.Ordinal));

            // Each result still shows its own type.
            Assert.All(results, r => Assert.Equal(Places[r.Name], r.Type));
        }
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-52")]
    public async Task AMixedCanonListKeepsItsOrder()
    {
        using var den = Filtering();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var canon = (await Filter(server, "location"))[2];

        // All at one scope here, so by own type, then name (F-VIEW-3).
        Assert.Equal(canon.OrderBy(r => r.Type, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal), canon);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-52")]
    public async Task AKindTheReaderHasntMetStaysOutOfAFilterAtAPosition()
    {
        using var den = Filtering();
        den.Write("universes/dracula/entities/worlds/far-vesta.md", "---\ntype: world\nname: Far Vesta\nstatus: canon\nfrom: \"dracula:20\"\n---\n# Far Vesta\n\n## Overview\nA waymark.\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        foreach (var type in new[] { "location", "world" })
        {
            // list_entities, search, and canon_as_of, then a lookup.
            var early = await Filter(server, type, "&asOf=dracula:5");
            var later = await Filter(server, type, "&asOf=dracula:20");
            Assert.All(early, results =>
            {
                Assert.DoesNotContain(results, r => r.Name == FarVesta);

                // The filter still finds the kinds the reader has met.
                Assert.Contains(results, r => r.Name == "Little Vesta");
            });
            Assert.All(later, results => Assert.Contains(results, r => r.Name == FarVesta));

            var lookedUpEarly = await server.GetJsonAsync($"/api/entities/lookup?universe=dracula&query=vesta&type={type}&asOf=dracula:5", HttpStatusCode.OK);
            var lookedUpLater = await server.GetJsonAsync($"/api/entities/lookup?universe=dracula&query=vesta&type={type}&asOf=dracula:20", HttpStatusCode.OK);
            var earlyNames = lookedUpEarly.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("entity").GetProperty("name").GetString()).ToList();
            Assert.DoesNotContain(FarVesta, earlyNames);
            Assert.Contains("Little Vesta", earlyNames);
            Assert.Contains(lookedUpLater.GetProperty("matches").EnumerateArray(), m => m.GetProperty("entity").GetProperty("name").GetString() == FarVesta);
        }
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-52")]
    [InlineData("location", "", "Far Vesta,Little Vesta,Vesta")]
    [InlineData("world", "", "Far Vesta,Little Vesta,Vesta")]
    [InlineData("moon", "", "Little Vesta")]
    [InlineData("character", "", "Ilse Tarrow")]
    [InlineData("ship", "", "")]
    [InlineData("location", "&asOf=dracula:5", "Little Vesta,Vesta")]
    [InlineData("world", "&asOf=dracula:5", "Little Vesta,Vesta")]
    [InlineData("moon", "&asOf=dracula:5", "Little Vesta")]
    [InlineData("location", "&asOf=dracula:20", "Far Vesta,Little Vesta,Vesta")]
    public async Task ASearchFindsKindsInFieldValues(string type, string asOf, string names)
    {
        // The word is only in field values, so only the field search can find it.
        using var den = Filtering();
        const string Fields = "status: canon\nfields:\n  kind: cairnstone\n---\n";
        den.Write("universes/dracula/entities/worlds/vesta.md", $"---\ntype: world\nname: Vesta\n{Fields}# Vesta\n");
        den.Write("universes/dracula/entities/moons/little-vesta.md", $"---\ntype: moon\nname: Little Vesta\n{Fields}# Little Vesta\n");
        den.Write("universes/dracula/entities/characters/ilse-tarrow.md", $"---\ntype: character\nname: Ilse Tarrow\n{Fields}# Ilse Tarrow\n");
        den.Write("universes/dracula/entities/worlds/far-vesta.md", $"---\ntype: world\nname: {FarVesta}\nfrom: \"dracula:20\"\n{Fields}# {FarVesta}\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        foreach (var field in new[] { "", "&field=kind" })
        {
            var found = await server.GetJsonAsync($"/api/search?universe=dracula&query=cairnstone&type={type}{asOf}{field}", HttpStatusCode.OK);

            var hits = found.GetProperty("hits").EnumerateArray().ToList();
            Assert.Equal(
                names.Split(',', StringSplitOptions.RemoveEmptyEntries),
                hits.Select(h => h.GetProperty("entity").GetProperty("name").GetString()!).Order(StringComparer.Ordinal));
            Assert.All(hits, h => Assert.Equal("kind", h.GetProperty("field").GetString()));
        }
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-52")]
    public async Task ATypeWithNoKindsAndAnUnknownTypeBehaveAsBefore()
    {
        using var den = TempDen.Dracula();
        den.Write("universes/dracula/entities/zeppelins/graf.md", "---\ntype: zeppelin\nname: Graf\nstatus: canon\n---\n# Graf\n");
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        var characters = await server.GetJsonAsync("/api/entities?universe=dracula&type=character", HttpStatusCode.OK);
        Assert.NotEmpty(characters.GetProperty("entities").EnumerateArray());
        Assert.All(characters.GetProperty("entities").EnumerateArray(), e => Assert.Equal("character", e.GetProperty("type").GetString()));

        // A type only a note uses still finds that note.
        var zeppelins = await server.GetJsonAsync("/api/entities?universe=dracula&type=zeppelin", HttpStatusCode.OK);
        Assert.Equal("Graf", Assert.Single(zeppelins.GetProperty("entities").EnumerateArray()).GetProperty("name").GetString());

        // A type nothing knows is refused.
        foreach (var path in new[] { "/api/entities?universe=dracula&type=dragon", "/api/search?universe=dracula&query=x&type=dragon", "/api/canon?universe=dracula&type=dragon" })
        {
            var refused = await server.GetJsonAsync(path, HttpStatusCode.BadRequest);
            Assert.Equal("unknown_type", refused.GetProperty("code").GetString());
        }
    }

    private static List<string> Strings(JsonElement list) => list.EnumerateArray().Select(e => e.GetString()!).ToList();
}

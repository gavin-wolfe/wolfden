using System.Net;
using System.Text.Json;

namespace Wolfden.Api.Tests;

/// <summary>
/// What follows a type's <c>kind_of</c> (types.md F-TYPE-13) beyond the link
/// check: a polity's page lists its members as a faction's does, a ref's field
/// type says which types its picker offers, and the type list says what each
/// type is a kind of.
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
        var exact = await server.GetJsonAsync("/api/entities/lookup?universe=dracula&query=reach&type=faction", HttpStatusCode.OK);
        Assert.Empty(exact.GetProperty("matches").EnumerateArray());
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
        Assert.Equal(["location", "planet", "settlement", "ship", "star-system"], Strings(setting.GetProperty("accepts")));
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-51")]
    [InlineData("faction", 0)]
    [InlineData("polity", 1)]
    public async Task FilteringByTypeMatchesANotesOwnTypeOnly(string type, int polities)
    {
        using var den = Den();
        await using var server = await Server.StartAsync(new WolfdenApiOptions(den.Root));

        // list_entities, search, and canon_as_of, through the same code as the MCP tools.
        var listed = await server.GetJsonAsync($"/api/entities?universe=dracula&type={type}", HttpStatusCode.OK);
        var found = await server.GetJsonAsync($"/api/search?universe=dracula&query=reach&type={type}", HttpStatusCode.OK);
        var canon = await server.GetJsonAsync($"/api/canon?universe=dracula&type={type}", HttpStatusCode.OK);

        Assert.Equal(polities, listed.GetProperty("entities").EnumerateArray().Count(e => e.GetProperty("name").GetString() == "Reach Compact"));
        Assert.Equal(polities > 0, found.GetProperty("hits").EnumerateArray().Any(h => h.GetProperty("entity").GetProperty("name").GetString() == "Reach Compact"));
        Assert.Equal(polities, canon.GetProperty("entities").EnumerateArray().Count(e => e.GetProperty("name").GetString() == "Reach Compact"));
    }

    private static List<string> Strings(JsonElement list) => list.EnumerateArray().Select(e => e.GetString()!).ToList();
}

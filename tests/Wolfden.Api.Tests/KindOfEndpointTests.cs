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

    private static List<string> Strings(JsonElement list) => list.EnumerateArray().Select(e => e.GetString()!).ToList();
}

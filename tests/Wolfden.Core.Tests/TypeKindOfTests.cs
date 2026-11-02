using Wolfden.Core.Documents;
using Wolfden.Core.Entities;
using Wolfden.Core.Links;
using Wolfden.Core.Structure;
using Wolfden.Core.Types;
using static Wolfden.Core.Tests.LinkDens;

namespace Wolfden.Core.Tests;

/// <summary>
/// A type's <c>kind_of</c> (types.md F-TYPE-13): an "is a" relationship that
/// carries identity only, so a type is accepted wherever a ref's <c>to</c>
/// names one of its ancestors (format.md F-LINK-11).
/// </summary>
public class TypeKindOfTests
{
    private const string Canon = "status: canon\n";

    private static string Note(string type, string name, string fields = "") =>
        $"type: {type}\nname: {name}\n{Canon}{(fields.Length > 0 ? $"fields:\n{fields}" : "")}".TrimEnd();

    /// <summary>The usual den with the space opera pack and a den-local pack, <c>starfall</c>.</summary>
    private static MemoryDenSource Own() =>
        Den().With("den.yaml", "format: 1\npacks: [space-opera, starfall]\n")
            .With("packs/starfall/pack.yaml", "name: Starfall\n");

    /// <summary>A chapter whose <c>fields</c> are <paramref name="fields"/>.</summary>
    private static MemoryDenSource Chapter(MemoryDenSource den, string fields) =>
        den.With($"{Ember}/book.md", $"---\nname: Ember\nchapters:\n  - {{ key: \"1\", fields: {{ {fields} }} }}\n---\n");

    private static DenTypes Types(MemoryDenSource den) => DenStructure.Load(den).Types;

    private static (string Path, string Rule, int? Line)[] Diagnostics(MemoryDenSource den) =>
        DenStructure.Load(den).Diagnostics.Select(d => (d.Path, d.Diagnostic.RuleId, d.Diagnostic.Line)).ToArray();

    [Fact]
    [Trait("Scenario", "SC-CHECK-48")]
    public void AChainIsFollowedAllTheWayUp()
    {
        var den = Chapter(
            Own()
            .With("packs/starfall/types/moon.yaml", "name: Moon\nplural: moons\nkind_of: planet\n")
            .Entity(Starfall, "little-vesta", Note("moon", "Little Vesta"), folder: "moons"),
            "setting: \"[[little-vesta]]\"");

        var types = Types(den);

        Assert.Empty(DenStructure.Load(den).Diagnostics);
        Assert.Equal(["planet"], types.Find("moon")!.KindOf);
        Assert.Equal(["planet", "location"], types.Find("moon")!.Ancestors);
        Assert.True(types.FindChapterField("setting")!.Accepts("moon"));
        Assert.Empty(LinkIndex.Load(den).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-52")]
    public void AFilterMatchesATypeAndItsKinds()
    {
        // The den's own world, so this holds whichever types the pack names.
        var types = Types(
            Own()
            .With("packs/starfall/types/world.yaml", "name: World\nplural: worlds\nkind_of: location\n")
            .With("packs/starfall/types/moon.yaml", "name: Moon\nplural: moons\nkind_of: world\n")
            .With("packs/starfall/types/sentient-ship.yaml", "name: Sentient Ship\nplural: sentient-ships\nkind_of: [ship, character]\n"));

        Assert.Equal(["moon", "world"], types.WithKinds(["world"]).Order(StringComparer.Ordinal));
        Assert.Equal(["character", "sentient-ship"], types.WithKinds(["character"]).Order(StringComparer.Ordinal));
        Assert.Equal(["species"], types.WithKinds(["species"]));

        // A type no pack defines is only itself, and a kind named too counts once.
        Assert.Equal(["zeppelin"], types.WithKinds(["zeppelin"]));
        Assert.Equal(["moon", "world"], types.WithKinds(["world", "moon"]).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-48")]
    public void OnlyAKindIsAcceptedNotItsParentOrASibling()
    {
        // "Is a" goes one way: a planet is a location, but a location isn't a planet.
        var den = Own()
            .With("packs/starfall/extensions/character.yaml", "fields:\n  homeworld: { type: ref, to: planet }\n")
            .Entity(Starfall, "kessan-reach", Note("location", "Kessan Reach"), folder: "locations")
            .Entity(Starfall, "vesta-yards", Note("settlement", "Vesta Yards"), folder: "settlements")
            .Entity(Starfall, "ilse", Character("Ilse", "fields:\n  homeworld: \"[[kessan-reach]]\""))
            .Entity(Starfall, "pike", Character("Pike", "fields:\n  homeworld: \"[[vesta-yards]]\""));

        var homeworld = Types(den).Find("character")!.FindField("homeworld")!;

        Assert.False(homeworld.Accepts("location"));
        Assert.False(homeworld.Accepts("settlement"));
        Assert.Equal(["F-LINK-11", "F-LINK-11"], LinkIndex.Load(den).Diagnostics.Select(d => d.Diagnostic.RuleId));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-48")]
    public void ASentientShipIsAcceptedAsACharacterAndAsAPlace()
    {
        var den = Chapter(
            Own()
            .With("packs/starfall/types/sentient-ship.yaml", "name: Sentient Ship\nplural: sentient-ships\nkind_of: [ship, character]\n")
            .Entity(Starfall, "lantern", Note("sentient-ship", "Lantern"), folder: "sentient-ships"),
            "pov: \"[[lantern]]\", setting: \"[[lantern]]\"");

        var types = Types(den);

        Assert.Empty(DenStructure.Load(den).Diagnostics);
        Assert.Equal(["ship", "character", "location"], types.Find("sentient-ship")!.Ancestors);
        Assert.True(types.FindChapterField("pov")!.Accepts("sentient-ship"));
        Assert.True(types.FindChapterField("setting")!.Accepts("sentient-ship"));
        Assert.Empty(LinkIndex.Load(den).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-48")]
    public void KindOfCarriesIdentityOnly()
    {
        // No fields, sections, or template come with it, so nothing can conflict.
        var den = Own()
            .With("packs/starfall/types/sentient-ship.yaml", "name: Sentient Ship\nplural: sentient-ships\nkind_of: [ship, character]\n");

        var type = Types(den).Find("sentient-ship")!;

        Assert.Empty(type.Fields);
        Assert.Empty(type.Sections);
        Assert.True(type.Template.IsGenerated);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-48")]
    public void ADiamondGivesNoDuplicatesAndNoWarnings()
    {
        var den = Own()
            .With("packs/starfall/types/top.yaml", "name: Top\nplural: tops\n")
            .With("packs/starfall/types/left.yaml", "name: Left\nplural: lefts\nkind_of: top\n")
            .With("packs/starfall/types/right.yaml", "name: Right\nplural: rights\nkind_of: top\n")
            .With("packs/starfall/types/bottom.yaml", "name: Bottom\nplural: bottoms\nkind_of: [left, right, left]\n")
            .With("packs/starfall/extensions/character.yaml", "fields:\n  charm: { type: ref, to: [top, left] }\n")
            .Entity(Starfall, "b", Note("bottom", "B"), folder: "bottoms")
            .Entity(Starfall, "ilse", Character("Ilse", "fields:\n  charm: \"[[b]]\""));

        var types = Types(den);

        Assert.Empty(DenStructure.Load(den).Diagnostics);
        Assert.Equal(["left", "right"], types.Find("bottom")!.KindOf);
        Assert.Equal(["left", "right", "top"], types.Find("bottom")!.Ancestors);

        // Left is in `to` as written, so only the others are added to it, once.
        var charm = types.Find("character")!.FindField("charm")!;
        Assert.Equal(["top", "left"], charm.CheckedTo);
        Assert.Equal(["bottom", "right"], charm.AcceptedKinds);
        Assert.Empty(LinkIndex.Load(den).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-49")]
    public void ALoopIsAWarningOnEachFileInItAndAllOfItIsIgnored()
    {
        var den = Own()
            .With("packs/starfall/types/hull.yaml", "name: Hull\nplural: hulls\nkind_of: [frame, location]\n")
            .With("packs/starfall/types/frame.yaml", "name: Frame\nplural: frames\nkind_of: hull\n");

        var types = Types(den);

        Assert.Equal(
            [("packs/starfall/types/frame.yaml", "F-TYPE-13", (int?)3), ("packs/starfall/types/hull.yaml", "F-TYPE-13", 3)],
            Diagnostics(den));
        Assert.Contains(DenStructure.Load(den).Diagnostics, d => d.Diagnostic.Message.Contains("(hull -> frame -> hull)", StringComparison.Ordinal));

        // Which parent is dropped doesn't depend on any order, and the parent outside the loop stays.
        Assert.Empty(types.Find("frame")!.Ancestors);
        Assert.Equal(["location"], types.Find("hull")!.Ancestors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-49")]
    public void ATypeThatIsAKindOfItselfIsALoop()
    {
        var den = Own().With("packs/starfall/types/hull.yaml", "name: Hull\nplural: hulls\nkind_of: hull\n");

        var warning = Assert.Single(DenStructure.Load(den).Diagnostics);

        Assert.Equal(("packs/starfall/types/hull.yaml", "F-TYPE-13", DiagnosticSeverity.Warning), (warning.Path, warning.Diagnostic.RuleId, warning.Diagnostic.Severity));
        Assert.Empty(Types(den).Find("hull")!.Ancestors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-49")]
    public void ALoopADenClosesThroughAPackDropsOnlyTheDensParent()
    {
        // A den can't remove a pack's parent (F-TYPE-8), even by mistake.
        var den = Own().With("packs/starfall/extensions/location.yaml", "kind_of: planet\n");

        var types = Types(den);

        Assert.Equal([("packs/starfall/extensions/location.yaml", "F-TYPE-13", (int?)1)], Diagnostics(den));
        Assert.Empty(types.Find("location")!.Ancestors);
        Assert.Equal(["location"], types.Find("planet")!.Ancestors);
        Assert.Equal(["location"], types.Find("ship")!.Ancestors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-49")]
    public void ALoopThroughADensOwnTypeDropsOnlyTheDensParentToo()
    {
        // The same with a den's location that replaces core's by key.
        var den = Own().With("packs/starfall/types/location.yaml", "name: Place\nplural: locations\nkind_of: planet\n");

        var types = Types(den);

        Assert.Equal(
            [("packs/starfall/types/location.yaml", "F-TYPE-13", (int?)3), ("packs/starfall/types/location.yaml", "F-TYPE-4", null)],
            Diagnostics(den).OrderBy(d => d.Rule, StringComparer.Ordinal));
        Assert.Empty(types.Find("location")!.Ancestors);
        Assert.Equal(["location"], types.Find("planet")!.Ancestors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-49")]
    public void DroppingADensParentKeepsEveryBuiltInParentInALongerLoop()
    {
        // location -> hull is the den's. hull -> ship -> location are the den's and the pack's.
        var den = Own()
            .With("packs/starfall/types/hull.yaml", "name: Hull\nplural: hulls\nkind_of: ship\n")
            .With("packs/starfall/extensions/location.yaml", "kind_of: hull\n");

        var types = Types(den);

        Assert.Equal(
            [("packs/starfall/extensions/location.yaml", "F-TYPE-13", (int?)1), ("packs/starfall/types/hull.yaml", "F-TYPE-13", 3)],
            Diagnostics(den));
        Assert.Empty(types.Find("location")!.Ancestors);
        Assert.Empty(types.Find("hull")!.Ancestors);
        Assert.Equal(["location"], types.Find("ship")!.Ancestors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-49")]
    public void ALaterBuiltInParentThatClosesALoopDropsTheDensParent()
    {
        // A release adds species -> location, where the den already said
        // location -> species (types.md §2.4). The built-in parent stays.
        var builtIn = BuiltInPacks.FromFiles(new Dictionary<string, string>
        {
            ["core/pack.yaml"] = "name: Core\n",
            ["core/types/location.yaml"] = "name: Location\nplural: locations\n",
            ["core/types/species.yaml"] = "name: Species\nplural: species\nkind_of: location\n",
        });
        var source = new MemoryDenSource()
            .With("packs/starfall/pack.yaml", "name: Starfall\n")
            .With("packs/starfall/extensions/location.yaml", "kind_of: species\n");

        var types = DenTypes.Load(source, ["starfall"], builtIn);

        var warning = Assert.Single(types.Diagnostics);
        Assert.Equal(("packs/starfall/extensions/location.yaml", "F-TYPE-13"), (warning.Path, warning.Diagnostic.RuleId));
        Assert.Empty(types.Find("location")!.Ancestors);
        Assert.Equal(["location"], types.Find("species")!.Ancestors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-49")]
    public void ALoopOfBuiltInParentsAloneIsDroppedWholeAndAParentADenRepeatsStaysBuiltIn()
    {
        var builtIn = BuiltInPacks.FromFiles(new Dictionary<string, string>
        {
            ["core/pack.yaml"] = "name: Core\n",
            ["core/types/location.yaml"] = "name: Location\nplural: locations\nkind_of: species\n",
            ["core/types/species.yaml"] = "name: Species\nplural: species\nkind_of: location\n",
        });
        var source = new MemoryDenSource()
            .With("packs/starfall/pack.yaml", "name: Starfall\n")
            .With("packs/starfall/extensions/location.yaml", "kind_of: species\n");

        var types = DenTypes.Load(source, ["starfall"], builtIn);

        Assert.Equal(
            ["builtin:packs/core/types/location.yaml", "builtin:packs/core/types/species.yaml"],
            types.Diagnostics.Select(d => d.Path));
        Assert.Empty(types.Find("location")!.Ancestors);
        Assert.Empty(types.Find("species")!.Ancestors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-49")]
    public void AnUnknownParentIsAWarningAndTheOthersStillCount()
    {
        var den = Own().With("packs/starfall/types/hull.yaml", "name: Hull\nplural: hulls\nkind_of:\n  - vehicle\n  - location\n");

        var warning = Assert.Single(DenStructure.Load(den).Diagnostics);

        Assert.Equal(("packs/starfall/types/hull.yaml", "F-TYPE-13", (int?)4), (warning.Path, warning.Diagnostic.RuleId, warning.Diagnostic.Line));
        Assert.Contains("'vehicle'", warning.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(["location"], Types(den).Find("hull")!.Ancestors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-49")]
    public void AParentFromAPackThatIsntEnabledIsUnknown()
    {
        // F-TYPE-2: a reserved name can't be a type either.
        var den = Den()
            .With("packs/starfall/pack.yaml", "name: Starfall\n")
            .With("den.yaml", "format: 1\npacks: [starfall]\n")
            .With("packs/starfall/types/hull.yaml", "name: Hull\nplural: hulls\nkind_of: [ship, chapter]\n");

        Assert.Equal(["F-TYPE-13", "F-TYPE-13"], DenStructure.Load(den).Diagnostics.Select(d => d.Diagnostic.RuleId));
        Assert.Empty(Types(den).Find("hull")!.Ancestors);
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-49")]
    [InlineData("kind_of: { location: true }\n", 0)]
    [InlineData("kind_of: Not A Key\n", 0)]
    [InlineData("kind_of: [location, { x: y }]\n", 1)]
    public void AValueThatIsntATypeKeyIsAWarningAndOnlyItIsIgnored(string kindOf, int parents)
    {
        var den = Own().With("packs/starfall/types/hull.yaml", $"name: Hull\nplural: hulls\n{kindOf}");

        var warning = Assert.Single(DenStructure.Load(den).Diagnostics);

        Assert.Equal("F-TYPE-13", warning.Diagnostic.RuleId);
        Assert.Equal(parents, Types(den).Find("hull")!.KindOf.Count);
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-49")]
    [InlineData("kind_of:\n")]
    [InlineData("kind_of: []\n")]
    public void AnEmptyKindOfMeansNoParents(string kindOf)
    {
        var den = Own().With("packs/starfall/types/hull.yaml", $"name: Hull\nplural: hulls\n{kindOf}");

        Assert.Empty(DenStructure.Load(den).Diagnostics);
        Assert.Empty(Types(den).Find("hull")!.KindOf);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-48")]
    public void ADensExtensionAddsAParentToAnyPacksType()
    {
        // In line with "extensions add": the pack's own parent stays, and a repeat is harmless.
        var den = Chapter(
            Own()
            .With("packs/starfall/extensions/ship.yaml", "kind_of: [character, location]\n")
            .With("packs/starfall/extensions/species.yaml", "kind_of: faction\n")
            .Entity(Starfall, "lantern", Note("ship", "Lantern"), folder: "ships"),
            "pov: \"[[lantern]]\", setting: \"[[lantern]]\"");

        var types = Types(den);

        Assert.Empty(DenStructure.Load(den).Diagnostics);
        Assert.Equal(["location", "character"], types.Find("ship")!.KindOf);
        Assert.Equal(["faction"], types.Find("species")!.Ancestors);
        Assert.Empty(LinkIndex.Load(den).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-50")]
    public void ADensOwnTypeHasOnlyItsOwnParents()
    {
        // The pack's ship is ignored along with its kind_of (F-TYPE-4), so
        // the den's ship still means what it meant: not a location.
        var den = Chapter(
            Own()
            .With("packs/starfall/types/ship.yaml", "name: Vessel\nplural: ships\n")
            .Entity(Starfall, "lantern", Note("ship", "Lantern"), folder: "ships"),
            "setting: \"[[lantern]]\"");

        var clash = Assert.Single(DenStructure.Load(den).Diagnostics);

        Assert.Equal("F-TYPE-4", clash.Diagnostic.RuleId);
        Assert.Empty(Types(den).Find("ship")!.Ancestors);
        Assert.Equal("F-LINK-11", Assert.Single(LinkIndex.Load(den).Diagnostics).Diagnostic.RuleId);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-50")]
    public void ADensOwnTypeCanSayItsAKindOfAPacksType()
    {
        var den = Chapter(
            Own()
            .With("packs/starfall/types/ship.yaml", "name: Vessel\nplural: ships\nkind_of: location\n")
            .Entity(Starfall, "lantern", Note("ship", "Lantern"), folder: "ships"),
            "setting: \"[[lantern]]\"");

        Assert.Equal(["location"], Types(den).Find("ship")!.Ancestors);
        Assert.Empty(LinkIndex.Load(den).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-50")]
    public void ADensOwnParentKeepsThePacksKindsWhenItReplacesTheParentByKey()
    {
        // A den's location is still 'location', so what's a kind of it by key still is.
        var den = Chapter(
            Own()
            .With("packs/starfall/types/location.yaml", "name: Place\nplural: locations\n")
            .Entity(Starfall, "vesta", Note("planet", "Vesta"), folder: "planets"),
            "setting: \"[[vesta]]\"");

        Assert.Equal(["location"], Types(den).Find("planet")!.Ancestors);
        Assert.Empty(LinkIndex.Load(den).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-50")]
    public void ReplacingAParentByItsFolderIsReportedOnceOnTheDensFile()
    {
        // F-TYPE-5: 'place' takes the folder, so 'location' is gone. The pack's
        // files name it, but they can't be edited, so the den's file says so.
        var den = Own().With("packs/starfall/types/place.yaml", "name: Place\nplural: locations\n");

        var diagnostics = DenStructure.Load(den).Diagnostics.Where(d => d.Diagnostic.RuleId == "F-TYPE-13").ToList();

        var warning = Assert.Single(diagnostics);
        Assert.Equal("packs/starfall/types/place.yaml", warning.Path);
        Assert.Contains("planet, settlement, ship, star-system", warning.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(Types(den).Find("planet")!.Ancestors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-51")]
    public void AnExampleLinkCanBeAKindOfTheFieldsType()
    {
        var den = Own()
            .Entity(Starfall, "vesta", Note("planet", "Vesta"), folder: "planets")
            .Entity(Starfall, "ilse", Character("Ilse"));
        var catalog = EntityCatalog.Load(den);

        var example = new ExampleLinks(catalog.Entities).For(catalog.Structure.Types.FindChapterField("setting")!);

        Assert.Equal("[[vesta]]", example);
    }
}

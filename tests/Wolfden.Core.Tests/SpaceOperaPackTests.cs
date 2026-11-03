using Wolfden.Core.Documents;
using Wolfden.Core.Entities;
using Wolfden.Core.Links;
using Wolfden.Core.Structure;
using Wolfden.Core.Types;
using static Wolfden.Core.Tests.LinkDens;

namespace Wolfden.Core.Tests;

/// <summary>
/// The built-in space opera pack (types.md §11): a den turns it on in
/// <c>den.yaml</c>, its types link to one another, and a den's own
/// definitions still win. Built-in packs change only by adding (types.md
/// §2.4), so the tables here pin what writers come to rely on: a failure
/// that comes from renaming or removing something is a breaking change, not
/// a test to update.
/// </summary>
public class SpaceOperaPackTests
{
    private const string Canon = "status: canon\n";

    /// <summary>A den with the Ember universe of <see cref="LinkDens.Den"/>, and the pack listed in <c>den.yaml</c>.</summary>
    private static MemoryDenSource Space() => Den().With("den.yaml", "format: 1\npacks: [space-opera]\n");

    private static DenTypes Types => DenTypes.Load(null, ["space-opera"]);

    private static TypeDefinition Type(string key) => Types.Find(key) ?? throw new InvalidOperationException($"No type '{key}'.");

    private static string Note(string type, string name, string fields = "") =>
        $"type: {type}\nname: {name}\n{Canon}{(fields.Length > 0 ? $"fields:\n{fields}" : "")}".TrimEnd();

    [Fact]
    [Trait("Scenario", "SC-CHECK-45")]
    public void ListingThePackInDenYamlEnablesItAfterCore()
    {
        var den = DenStructure.Load(Space());

        Assert.Empty(den.Diagnostics);
        Assert.Equal(["core", "space-opera"], den.Types.Packs.Select(p => p.Key));
        var pack = den.Types.Packs[1];
        Assert.Equal(("Space Opera", true, "builtin:packs/space-opera"), (pack.Name, pack.IsBuiltIn, pack.Folder));
        Assert.Equal(17, den.Types.Types.Count);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-45")]
    public void ADenThatDoesntListThePackDoesntHaveItsTypes()
    {
        Assert.Equal(["core"], DenStructure.Load(Den()).Types.Packs.Select(p => p.Key));
        Assert.Null(DenStructure.Load(Den()).Types.Find("ship"));

        var catalog = EntityCatalog.Load(Den().Entity(Starfall, "lantern", Note("ship", "Lantern"), folder: "ships"));

        Assert.Null(Assert.Single(catalog.Entities).Definition);
    }

    [Theory]
    [InlineData("star-system", "star-systems", "Star System")]
    [InlineData("world", "worlds", "World")]
    [InlineData("settlement", "settlements", "Settlement")]
    [InlineData("polity", "polities", "Polity")]
    [InlineData("ship-class", "ship-classes", "Ship Class")]
    [InlineData("ship", "ships", "Ship")]
    [InlineData("technology", "technologies", "Technology")]
    public void TheSevenTypesHaveTheirFoldersNamesAndTemplates(string key, string plural, string name)
    {
        var type = Type(key);

        Assert.Equal((plural, name), (type.Plural, type.Name));
        Assert.Equal($"builtin:packs/space-opera/types/{key}.yaml", type.Path);
        Assert.False(string.IsNullOrWhiteSpace(type.Description));
        Assert.False(type.Template.IsGenerated);
        Assert.Equal($"{key}.md", type.Template.FileName);
        Assert.Contains("# {{name}}", type.Template.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("star-system", "part_of stars controlled_by")]
    [InlineData("world", "part_of kind population controlled_by")]
    [InlineData("settlement", "part_of kind population controlled_by")]
    [InlineData("polity", "government leader capital part_of")]
    [InlineData("ship-class", "role builder crew technologies")]
    [InlineData("ship", "class captain owner location condition")]
    [InlineData("technology", "kind created_by controlled_by introduced")]
    public void FieldsAreInOrder(string key, string fields)
    {
        Assert.Equal(fields.Split(' '), Type(key).Fields.Select(f => f.Name));
    }

    [Theory]
    [InlineData("star-system", "controlled_by")]
    [InlineData("world", "population controlled_by")]
    [InlineData("settlement", "population controlled_by")]
    [InlineData("polity", "government leader capital part_of")]
    [InlineData("ship-class", "")]
    [InlineData("ship", "captain owner location condition")]
    [InlineData("technology", "controlled_by")]
    public void WhatChangesOverTheStoryIsTemporal(string key, string temporal)
    {
        Assert.Equal(temporal.Split(' ', StringSplitOptions.RemoveEmptyEntries), Type(key).Fields.Where(f => f.IsTemporal).Select(f => f.Name));
    }

    [Theory]
    [InlineData("star-system", "Overview|Description|Routes|History")]
    [InlineData("world", "Overview|Environment|Society|History")]
    [InlineData("settlement", "Overview|Description|Society|History")]
    [InlineData("polity", "Overview|Government|Goals|Military|History")]
    [InlineData("ship-class", "Overview|Design|Capabilities|History")]
    [InlineData("ship", "Overview|Description|Crew|Service history")]
    [InlineData("technology", "Overview|How it works|Limits|Consequences|History|Open questions")]
    public void SectionsAreInOrderAndEachTemplateHasThem(string key, string sections)
    {
        var type = Type(key);
        var names = sections.Split('|');

        Assert.Equal(names, type.Sections.Select(s => s.Name));
        Assert.All(type.Sections, s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));

        // The template's headings, in section order (F-TPL-4).
        var headings = type.Template.Text.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).Select(l => l[3..]);
        Assert.Equal(names, headings);
    }

    [Theory]
    [InlineData("star-system", "part_of", "location")]
    [InlineData("star-system", "controlled_by", "polity faction")]
    [InlineData("world", "part_of", "star-system world location")]
    [InlineData("world", "controlled_by", "polity faction")]
    [InlineData("settlement", "part_of", "world star-system location")]
    [InlineData("settlement", "controlled_by", "polity faction")]
    [InlineData("polity", "leader", "character")]
    [InlineData("polity", "capital", "world settlement location")]
    [InlineData("polity", "part_of", "polity faction")]
    [InlineData("ship-class", "builder", "polity faction")]
    [InlineData("ship-class", "technologies", "technology")]
    [InlineData("ship", "class", "ship-class")]
    [InlineData("ship", "captain", "character")]
    [InlineData("ship", "owner", "character polity faction")]
    [InlineData("ship", "location", "star-system world settlement location")]
    [InlineData("technology", "created_by", "character species polity faction")]
    [InlineData("technology", "controlled_by", "polity faction")]
    public void RefsLinkToTheTypesTheyName(string key, string field, string to)
    {
        var definition = Type(key).FindField(field)!;
        var names = to.Split(' ');

        Assert.Equal(FieldKind.Ref, definition.Kind);
        Assert.Equal(names, definition.To);

        // Each name is an enabled type, so F-FT-9 skips none of them.
        Assert.Equal(names, definition.CheckedTo);
    }

    [Fact]
    public void ThePackHasNoRequiredFieldsDefaultsOrRequiredSections()
    {
        // Like core (F-CORE-1): a hand-written note never gets a "missing"
        // warning, and adding a default later is a breaking change (§2.4).
        var types = Types.Types.Where(t => t.Pack.Key == "space-opera").ToList();

        Assert.Equal(7, types.Count);
        Assert.All(types.SelectMany(t => t.Fields), f => Assert.False(f.IsRequired));
        Assert.All(types.SelectMany(t => t.Fields), f => Assert.Null(f.Default));
        Assert.All(types.SelectMany(t => t.Sections), s => Assert.False(s.IsRequired));
        Assert.All(types.SelectMany(t => t.Fields), f => Assert.False(string.IsNullOrWhiteSpace(f.Description)));
    }

    [Fact]
    public void ThePackAddsToCoreAndChangesNothingInIt()
    {
        var enabled = Types;

        Assert.Empty(enabled.Diagnostics);
        Assert.All(DenTypes.BuiltIn.Types, t =>
            Assert.Equal(t.Fields.Select(f => f.Name), enabled.Find(t.Key)!.Fields.Select(f => f.Name)));
        Assert.Equal(DenTypes.BuiltIn.ChapterFields.Select(f => f.Name), enabled.ChapterFields.Select(f => f.Name));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-45")]
    public void TheTypesLinkToOneAnotherWithoutWarnings()
    {
        var source = Space()
            .Entity(Starfall, "kessan-reach", Note("location", "Kessan Reach"), folder: "locations")
            .Entity(Starfall, "ilse-tarrow", Character("Ilse Tarrow", "fields:\n  species: \"[[velari]]\""))
            .Entity(Starfall, "tessaly", Note("star-system", "Tessaly", "  part_of: \"[[kessan-reach]]\"\n  controlled_by: \"[[reach-compact]]\"\n"), folder: "star-systems")
            .Entity(Starfall, "vesta", Note("world", "Vesta", "  part_of: \"[[tessaly]]\"\n"), folder: "worlds")
            .Entity(Starfall, "little-vesta", Note("world", "Little Vesta", "  part_of: \"[[vesta]]\"\n  kind: moon\n"), folder: "worlds")
            .Entity(Starfall, "vesta-yards", Note("settlement", "Vesta Yards", "  part_of: \"[[little-vesta]]\"\n"), folder: "settlements")
            .Entity(Starfall, "velari", Note("species", "Velari", "  origin: \"[[vesta]]\"\n"), folder: "species")
            .Entity(Starfall, "reach-compact", Note("polity", "Reach Compact", "  leader: \"[[ilse-tarrow]]\"\n  capital: \"[[vesta-yards]]\"\n"), folder: "polities")
            .Entity(Starfall, "relay", Note("technology", "Relay", "  created_by: \"[[velari]]\"\n  controlled_by: \"[[reach-compact]]\"\n"), folder: "technologies")
            .Entity(Starfall, "survey", Note("ship-class", "Survey", "  builder: \"[[reach-compact]]\"\n  technologies: [\"[[relay]]\"]\n"), folder: "ship-classes")
            .Entity(Starfall, "lantern", Note("ship", "Lantern", "  class: \"[[survey]]\"\n  captain: \"[[ilse-tarrow]]\"\n  owner: \"[[reach-compact]]\"\n  location: \"[[tessaly]]\"\n  condition: in service\n"), folder: "ships");

        // These hand-written notes have no ids yet (F-ID-2), which is all the catalog says.
        Assert.All(EntityCatalog.Load(source).Diagnostics, d => Assert.Equal("F-ID-2", d.Diagnostic.RuleId));
        Assert.Empty(LinkIndex.Load(source).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-45")]
    public void AGasGiantIsAWorldThatMoonsAndSettlementsCanLinkTo()
    {
        var source = Space()
            .Entity(Starfall, "tessaly", Note("star-system", "Tessaly"), folder: "star-systems")
            .Entity(Starfall, "orrin", Note("world", "Orrin", "  part_of: \"[[tessaly]]\"\n  kind: gas giant\n"), folder: "worlds")
            .Entity(Starfall, "little-orrin", Note("world", "Little Orrin", "  part_of: \"[[orrin]]\"\n  kind: moon\n"), folder: "worlds")
            .Entity(Starfall, "orrin-skimmer", Note("settlement", "Orrin Skimmer", "  part_of: \"[[orrin]]\"\n  kind: fuel-mining platform\n"), folder: "settlements");

        Assert.All(EntityCatalog.Load(source).Diagnostics, d => Assert.Equal("F-ID-2", d.Diagnostic.RuleId));
        Assert.Empty(LinkIndex.Load(source).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-45")]
    public void ALinkToTheWrongKindOfNoteIsTheUsualWarning()
    {
        var source = Space()
            .Entity(Starfall, "ilse-tarrow", Character("Ilse Tarrow"))
            .Entity(Starfall, "lantern", Note("ship", "Lantern", "  class: \"[[ilse-tarrow]]\"\n"), folder: "ships");

        var index = LinkIndex.Load(source);

        var warning = Assert.Single(index.Diagnostics);
        Assert.Equal((Starfall + "/entities/ships/lantern.md", "F-LINK-11"), (warning.Path, warning.Diagnostic.RuleId));
        Assert.Contains("class is a ref to ship-class", warning.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("star-system", "location")]
    [InlineData("world", "location")]
    [InlineData("settlement", "location")]
    [InlineData("ship", "location")]
    [InlineData("polity", "faction")]
    [InlineData("ship-class", "")]
    [InlineData("technology", "")]
    [Trait("Scenario", "SC-CHECK-45")]
    public void ThePacksPlacesAreLocationsAndItsPolitiesAreFactions(string key, string parent)
    {
        // Removing one of these would turn links that are fine into warnings: a breaking change (types.md §2.4).
        Assert.Equal(parent.Split(' ', StringSplitOptions.RemoveEmptyEntries), Type(key).KindOf);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-46")]
    public void CoreFieldsTakeThePacksKindsOfTheirTypes()
    {
        // A pack can't change a core field (F-TYPE-8), and doesn't need to:
        // a world is a location, and a polity is a faction (F-TYPE-13).
        var source = Space()
            .Entity(Starfall, "tessaly", Note("star-system", "Tessaly"), folder: "star-systems")
            .Entity(Starfall, "vesta", Note("world", "Vesta"), folder: "worlds")
            .Entity(Starfall, "vesta-yards", Note("settlement", "Vesta Yards"), folder: "settlements")
            .Entity(Starfall, "lantern", Note("ship", "Lantern"), folder: "ships")
            .Entity(Starfall, "reach-compact", Note("polity", "Reach Compact"), folder: "polities")
            .Entity(Starfall, "the-bar", Note("location", "The Bar", "  part_of: \"[[vesta-yards]]\"\n  controlled_by: \"[[reach-compact]]\"\n"), folder: "locations")
            .Entity(Starfall, "survey-corps", Note("faction", "Survey Corps", "  part_of: \"[[reach-compact]]\"\n  base: \"[[lantern]]\"\n"), folder: "factions")
            .Entity(Starfall, "the-lamp", Note("item", "The Lamp", "  owner: \"[[reach-compact]]\"\n  location: \"[[tessaly]]\"\n"), folder: "items")
            .Entity(Starfall, "the-landing", Note("event", "The Landing", "  location: \"[[vesta]]\"\n  participants: [\"[[reach-compact]]\"]\n"), folder: "events")
            .Entity(Starfall, "ilse-tarrow", Character("Ilse Tarrow", "fields:\n  factions: [\"[[reach-compact]]\"]"))
            .With($"{Ember}/book.md", "---\nname: Ember\nchapters:\n  - { key: \"1\", fields: { setting: [\"[[vesta-yards]]\", \"[[lantern]]\"] } }\n---\n");

        Assert.Empty(LinkIndex.Load(source).Diagnostics);
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-46")]
    [InlineData("ilse-tarrow")]
    [InlineData("velari")]
    public void ASettingThatIsntAPlaceStillWarnsAndSaysWhatsAccepted(string slug)
    {
        var source = Space()
            .Entity(Starfall, "ilse-tarrow", Character("Ilse Tarrow"))
            .Entity(Starfall, "velari", Note("species", "Velari"), folder: "species")
            .With($"{Ember}/book.md", $"---\nname: Ember\nchapters:\n  - {{ key: \"1\", fields: {{ setting: \"[[{slug}]]\" }} }}\n---\n");

        var warning = Assert.Single(LinkIndex.Load(source).Diagnostics);

        Assert.Equal(("F-LINK-11", DiagnosticSeverity.Warning), (warning.Diagnostic.RuleId, warning.Diagnostic.Severity));
        Assert.Contains("setting is a ref to location or a kind of it (settlement, ship, star-system, world)", warning.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-46")]
    public void WithoutThePackAWarningNamesOnlyTheFieldsType()
    {
        var source = Den()
            .Entity(Starfall, "ilse-tarrow", Character("Ilse Tarrow"))
            .With($"{Ember}/book.md", "---\nname: Ember\nchapters:\n  - { key: \"1\", fields: { setting: \"[[ilse-tarrow]]\" } }\n---\n");

        var warning = Assert.Single(LinkIndex.Load(source).Diagnostics);

        Assert.EndsWith("setting is a ref to location.", warning.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-46")]
    public void ADensOwnPackCanStillRedefineACoreRef()
    {
        // No longer needed for the pack's types, but the den's definition
        // still wins (F-PACK-11), with one clash warning on its own file.
        var source = Space()
            .With("den.yaml", "format: 1\npacks: [space-opera, starfall]\n")
            .With("packs/starfall/pack.yaml", "name: Starfall\n")
            .With("packs/starfall/chapter.yaml", "fields:\n  setting: { type: ref, to: [location, star-system, world, settlement, ship], list: true }\n")
            .Entity(Starfall, "vesta", Note("world", "Vesta"), folder: "worlds")
            .With($"{Ember}/book.md", "---\nname: Ember\nchapters:\n  - { key: \"1\", fields: { setting: \"[[vesta]]\" } }\n---\n");

        var den = DenStructure.Load(source);

        Assert.Empty(LinkIndex.Load(source).Diagnostics);
        var clash = Assert.Single(den.Diagnostics);
        Assert.Equal(("packs/starfall/chapter.yaml", "F-CHAP-2", DiagnosticSeverity.Warning), (clash.Path, clash.Diagnostic.RuleId, clash.Diagnostic.Severity));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-46")]
    public void AnExtensionCanStillRedefineACoreFieldWithOneWarningForEachField()
    {
        // Character and faction fields go in extensions/<type>.yaml. Each
        // redefined field is its own clash warning (F-TYPE-8, F-PACK-11).
        var source = Space()
            .With("den.yaml", "format: 1\npacks: [space-opera, starfall]\n")
            .With("packs/starfall/pack.yaml", "name: Starfall\n")
            .With("packs/starfall/extensions/character.yaml", "fields:\n  factions: { type: ref, to: [faction, polity], list: true, temporal: true }\n")
            .With("packs/starfall/extensions/faction.yaml", "fields:\n  part_of: { type: ref, to: [faction, polity] }\n")
            .Entity(Starfall, "reach-compact", Note("polity", "Reach Compact"), folder: "polities")
            .Entity(Starfall, "survey-corps", Note("faction", "Survey Corps", "  part_of: \"[[reach-compact]]\"\n"), folder: "factions")
            .Entity(Starfall, "ilse-tarrow", Character("Ilse Tarrow", "fields:\n  factions: [\"[[reach-compact]]\"]"));

        var den = DenStructure.Load(source);

        Assert.Empty(LinkIndex.Load(source).Diagnostics);
        Assert.Equal(
            [
                ("packs/starfall/extensions/character.yaml", "F-TYPE-8", DiagnosticSeverity.Warning),
                ("packs/starfall/extensions/faction.yaml", "F-TYPE-8", DiagnosticSeverity.Warning),
            ],
            den.Diagnostics.Select(d => (d.Path, d.Diagnostic.RuleId, d.Diagnostic.Severity)).Order());
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-47")]
    public void ADensOwnShipWinsOverThePacks()
    {
        // types.md §10: a den that defined ship before enabling the pack still
        // means what it meant, with the F-TYPE-4 warning on its own file.
        var den = DenStructure.Load(Space()
            .With("den.yaml", "format: 1\npacks: [starfall, space-opera]\n")
            .With("packs/starfall/pack.yaml", "name: Starfall\n")
            .With("packs/starfall/types/ship.yaml", "name: Vessel\nplural: ships\nfields:\n  class: string\n"));

        Assert.Equal(["core", "space-opera", "starfall"], den.Types.Packs.Select(p => p.Key));
        var ship = den.Types.Find("ship")!;
        Assert.Equal("Vessel", ship.Name);
        Assert.Equal(FieldKind.String, ship.FindField("class")!.Kind);
        var clash = Assert.Single(den.Diagnostics);
        Assert.Equal(("packs/starfall/types/ship.yaml", "F-TYPE-4"), (clash.Path, clash.Diagnostic.RuleId));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-47")]
    public void ADenCanExtendAPackTypeAndCharacters()
    {
        // types.md §10's rank and posting, now that ship is built in.
        var den = DenStructure.Load(Space()
            .With("den.yaml", "format: 1\npacks: [space-opera, starfall]\n")
            .With("packs/starfall/pack.yaml", "name: Starfall\n")
            .With("packs/starfall/extensions/character.yaml", "fields:\n  rank: { type: string, temporal: true }\n  posting: { type: ref, to: [ship, location], temporal: true }\n")
            .With("packs/starfall/extensions/ship.yaml", "fields:\n  registry: string\n"));

        Assert.Empty(den.Diagnostics);
        Assert.Equal(["class", "captain", "owner", "location", "condition", "registry"], den.Types.Find("ship")!.Fields.Select(f => f.Name));
        Assert.Equal(["ship", "location"], den.Types.Find("character")!.FindField("posting")!.CheckedTo);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-47")]
    public void ADenLocalPackNamedSpaceOperaShadowsTheBuiltInOne()
    {
        var den = DenStructure.Load(Space()
            .With("packs/space-opera/pack.yaml", "name: My Space Opera\n")
            .With("packs/space-opera/types/ship.yaml", "name: Vessel\nplural: vessels\n"));

        var pack = den.Types.Packs[1];
        Assert.Equal(("My Space Opera", false, true), (pack.Name, pack.IsBuiltIn, pack.Shadows));
        Assert.Equal("Vessel", den.Types.Find("ship")!.Name);
        Assert.Null(den.Types.Find("world"));
        var shadow = Assert.Single(den.Diagnostics);
        Assert.Equal(("packs/space-opera/pack.yaml", "F-PACK-10"), (shadow.Path, shadow.Diagnostic.RuleId));
    }
}

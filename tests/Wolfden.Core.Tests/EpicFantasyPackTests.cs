using Wolfden.Core.Checks;
using Wolfden.Core.Documents;
using Wolfden.Core.Entities;
using Wolfden.Core.Links;
using Wolfden.Core.Structure;
using Wolfden.Core.Types;

namespace Wolfden.Core.Tests;

/// <summary>
/// The built-in epic fantasy pack (types.md §12): a den turns it on in
/// <c>den.yaml</c>, its types link to one another and to core's through
/// <c>kind_of</c>, and a den's own definitions still win. As with the space
/// opera pack, a failure that comes from renaming or removing something is a
/// breaking change (types.md §2.4), not a test to update. The notes here are
/// the pack's example notes: a few invented ones, not a sample universe.
/// </summary>
public class EpicFantasyPackTests
{
    private const string Fable = "universes/fable";
    private const string Book = Fable + "/books/the-long-tithe";

    private static MemoryDenSource Plain() =>
        new MemoryDenSource()
            .With("den.yaml", "format: 1\n")
            .Universe("fable")
            .Book("fable", null, "the-long-tithe", "chapters: [1, 2]");

    private static MemoryDenSource Fantasy() => Plain().With("den.yaml", "format: 1\npacks: [epic-fantasy]\n");

    private static DenTypes Types => DenTypes.Load(null, ["epic-fantasy"]);

    private static TypeDefinition Type(string key) => Types.Find(key) ?? throw new InvalidOperationException($"No type '{key}'.");

    private static MemoryDenSource Note(MemoryDenSource source, string type, string folder, string slug, string name, string fields = "") =>
        source.With($"{Fable}/entities/{folder}/{slug}.md", $"---\ntype: {type}\nname: {name}\nstatus: canon\n{(fields.Length > 0 ? $"fields:\n{fields}" : "")}---\n");

    private static string Chapter(string fields) => $"---\nname: The Long Tithe\nchapters:\n  - {{ key: \"1\", fields: {{ {fields} }} }}\n---\n";

    /// <summary>One note of every type in the pack, and of the core types they link to.</summary>
    private static MemoryDenSource Examples()
    {
        var den = Fantasy();
        den = Note(den, "location", "locations", "varrowland", "Varrowland", "  kind: continent\n");
        den = Note(den, "realm", "realms", "vaskerrow", "Vaskerrow",
            "  part_of: \"[[varrowland]]\"\n  kind: kingdom\n  controlled_by:\n    - { value: \"[[house-ostrand]]\", from: \"the-long-tithe:1\" }\n  seat: \"[[tenhallow]]\"\n  borders: the salt marshes to the east\n  population: about two million\n");
        den = Note(den, "realm", "realms", "the-underhush", "The Underhush", "  kind: plane\n  borders: reached through still water, at night\n");
        den = Note(den, "location", "locations", "tenhallow", "Tenhallow", "  part_of: \"[[vaskerrow]]\"\n  kind: city\n  controlled_by: \"[[house-ostrand]]\"\n");
        den = Note(den, "location", "locations", "the-moss-quarter", "The Moss Quarter", "  part_of: \"[[tenhallow]]\"\n  kind: district\n");
        den = Note(den, "faction", "factions", "house-ostrand", "House Ostrand", "  leader: \"[[maret-ostrand]]\"\n  base: \"[[tenhallow]]\"\n");
        den = Note(den, "magic-system", "magic-systems", "cindertithe", "Cindertithe",
            "  kind: bargain magic\n  source: \"[[oshelleth]]\"\n  practiced_by: [\"[[ondrahi]]\", \"[[sedgefolk]]\", \"[[banked-flame]]\", \"[[house-ostrand]]\", \"[[maret-ostrand]]\"]\n");
        den = Note(den, "species", "species", "ondrahi", "Ondrahi", "  origin: \"[[vaskerrow]]\"\n  lifespan: about 90 years\n");
        den = Note(den, "culture", "cultures", "sedgefolk", "The Sedgefolk", "  kind: ethnic group\n  homeland: \"[[vaskerrow]]\"\n  languages: [Sedge Cant]\n");
        den = Note(den, "creature", "creatures", "sedge-drake", "Sedge Drake", "  kind: beast\n  habitat: \"[[varrowland]]\"\n  diet: sheep\n  danger: takes a whole flock\n  population: thought extinct\n");
        den = Note(den, "deity", "deities", "oshelleth", "Oshelleth",
            "  species: a sleeping god\n  factions: [\"[[house-ostrand]]\"]\n  domains: [the banked fire, debts]\n  worshippers: [\"[[sedgefolk]]\", \"[[ondrahi]]\", \"[[vaskerrow]]\", \"[[banked-flame]]\", \"[[house-ostrand]]\", \"[[maret-ostrand]]\"]\n  condition: sleeping\n");
        den = Note(den, "artifact", "artifacts", "widows-wick", "The Widow's Wick",
            "  kind: lamp wick\n  owner: \"[[banked-flame]]\"\n  location: \"[[the-moss-quarter]]\"\n  created_by: \"[[ondrahi]]\"\n  powered_by: \"[[cindertithe]]\"\n  condition: whole\n");
        den = Note(den, "order", "orders", "banked-flame", "The Banked Flame",
            "  kind: monastery\n  leader: \"[[maret-ostrand]]\"\n  base: \"[[vaskerrow]]\"\n  part_of: \"[[house-ostrand]]\"\n  patron: \"[[oshelleth]]\"\n");
        den = Note(den, "character", "characters", "maret-ostrand", "Maret Ostrand", "  species: \"[[ondrahi]]\"\n  heritage: \"[[sedgefolk]]\"\n  factions: [\"[[banked-flame]]\", \"[[house-ostrand]]\"]\n");
        den = Note(den, "character", "characters", "vethrisk", "Vethrisk", "  species: \"[[sedge-drake]]\"\n");
        return den;
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-53")]
    public void ListingThePackInDenYamlEnablesItAfterCore()
    {
        var den = DenStructure.Load(Fantasy());

        Assert.Empty(den.Diagnostics);
        Assert.Equal(["core", "epic-fantasy"], den.Types.Packs.Select(p => p.Key));
        var pack = den.Types.Packs[1];
        Assert.Equal(("Epic Fantasy", true, "builtin:packs/epic-fantasy"), (pack.Name, pack.IsBuiltIn, pack.Folder));
        Assert.Equal(16, den.Types.Types.Count);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-53")]
    public void ADenThatDoesntListThePackDoesntHaveItsTypes()
    {
        Assert.Null(DenStructure.Load(Plain()).Types.Find("realm"));

        var catalog = EntityCatalog.Load(Note(Plain(), "realm", "realms", "vaskerrow", "Vaskerrow"));

        Assert.Null(Assert.Single(catalog.Entities).Definition);
    }

    [Theory]
    [InlineData("realm", "realms", "Realm")]
    [InlineData("magic-system", "magic-systems", "Magic System")]
    [InlineData("creature", "creatures", "Creature")]
    [InlineData("deity", "deities", "Deity")]
    [InlineData("artifact", "artifacts", "Artifact")]
    [InlineData("order", "orders", "Order")]
    public void TheSixTypesHaveTheirFoldersNamesAndTemplates(string key, string plural, string name)
    {
        var type = Type(key);

        Assert.Equal((plural, name), (type.Plural, type.Name));
        Assert.Equal($"builtin:packs/epic-fantasy/types/{key}.yaml", type.Path);
        Assert.False(string.IsNullOrWhiteSpace(type.Description));
        Assert.False(type.Template.IsGenerated);
        Assert.Equal($"{key}.md", type.Template.FileName);
        Assert.Contains("# {{name}}", type.Template.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("realm", "part_of kind controlled_by seat borders population")]
    [InlineData("magic-system", "kind source practiced_by")]
    [InlineData("creature", "kind habitat diet danger lifespan population")]
    [InlineData("deity", "gender pronouns species factions domains worshippers condition")]
    [InlineData("artifact", "kind owner location created_by powered_by condition")]
    [InlineData("order", "kind leader base part_of patron")]
    public void FieldsAreInOrder(string key, string fields)
    {
        Assert.Equal(fields.Split(' '), Type(key).Fields.Select(f => f.Name));
    }

    [Theory]
    [InlineData("realm", "part_of controlled_by seat borders population")]
    [InlineData("magic-system", "source practiced_by")]
    [InlineData("creature", "habitat population")]
    [InlineData("deity", "gender pronouns factions worshippers condition")]
    [InlineData("artifact", "owner location condition")]
    [InlineData("order", "leader base patron")]
    public void WhatChangesOverTheStoryIsTemporal(string key, string temporal)
    {
        Assert.Equal(temporal.Split(' '), Type(key).Fields.Where(f => f.IsTemporal).Select(f => f.Name));
    }

    [Theory]
    [InlineData("realm", "Overview|Land|Society|Rule|Neighbors|History")]
    [InlineData("magic-system", "Overview|How it works|Costs|Limits|Who can use it|Consequences|History|Open questions")]
    [InlineData("creature", "Overview|Body|Habits|Lore|History")]
    [InlineData("deity", "Overview|Want|Powers|Worship|Myths|History|Open questions")]
    [InlineData("artifact", "Overview|Description|Powers|Price|History|Open questions")]
    [InlineData("order", "Overview|Creed|Ranks|Goals|Methods|Members|History")]
    public void SectionsAreInOrderAndEachTemplateHasThem(string key, string sections)
    {
        var type = Type(key);
        var names = sections.Split('|');

        Assert.Equal(names, type.Sections.Select(s => s.Name));
        Assert.All(type.Sections, s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));

        var headings = type.Template.Text.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).Select(l => l[3..]);
        Assert.Equal(names, headings);
    }

    [Theory]
    [InlineData("realm", "part_of", "realm location")]
    [InlineData("realm", "controlled_by", "faction")]
    [InlineData("realm", "seat", "location")]
    [InlineData("magic-system", "practiced_by", "culture species order faction character")]
    [InlineData("creature", "habitat", "location")]
    [InlineData("deity", "factions", "faction")]
    [InlineData("deity", "worshippers", "culture species realm order faction character")]
    [InlineData("artifact", "owner", "character order faction")]
    [InlineData("artifact", "location", "realm location")]
    [InlineData("artifact", "created_by", "character species culture order faction")]
    [InlineData("artifact", "powered_by", "magic-system")]
    [InlineData("order", "leader", "character")]
    [InlineData("order", "base", "location")]
    [InlineData("order", "part_of", "order faction")]
    [InlineData("order", "patron", "deity character faction")]
    public void RefsLinkToTheTypesTheyName(string key, string field, string to)
    {
        var definition = Type(key).FindField(field)!;
        var names = to.Split(' ');

        Assert.Equal(FieldKind.Ref, definition.Kind);
        Assert.Equal(names, definition.To);
        Assert.Equal(names, definition.CheckedTo);
    }

    [Fact]
    public void ThePackHasNoRequiredFieldsDefaultsOrRequiredSections()
    {
        var types = Types.Types.Where(t => t.Pack.Key == "epic-fantasy").ToList();

        Assert.Equal(6, types.Count);
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
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-53")]
    public void TheExampleNotesLinkToOneAnotherWithoutWarnings()
    {
        var source = Examples();

        // Every type in the pack has a note here, so a type that's left out fails.
        var catalog = EntityCatalog.Load(source);
        Assert.Equal(
            ["artifact", "character", "creature", "culture", "deity", "faction", "location", "magic-system", "order", "realm", "species"],
            catalog.Entities.Select(e => e.Definition!.Key).Distinct().Order(StringComparer.Ordinal));

        // DenCheck runs every check, fields included. These hand-written notes
        // have no ids yet (F-ID-2), which is all it should say.
        Assert.All(DenCheck.Run(source).Diagnostics, d => Assert.Equal("F-ID-2", d.Diagnostic.RuleId));
    }

    [Theory]
    [InlineData("realm", "location")]
    [InlineData("order", "faction")]
    [InlineData("artifact", "item")]
    [InlineData("deity", "character")]
    [InlineData("creature", "species")]
    [InlineData("magic-system", "")]
    [Trait("Scenario", "SC-CHECK-54")]
    public void EachTypeIsAKindOfWhatItIs(string key, string parent)
    {
        // Removing one of these would turn links that are fine into warnings: a breaking change (types.md §2.4).
        Assert.Equal(parent.Split(' ', StringSplitOptions.RemoveEmptyEntries), Type(key).KindOf);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-54")]
    public void CoreFieldsTakeThePacksKindsOfTheirTypes()
    {
        var source = Examples()
            .With($"{Book}/book.md", Chapter("setting: [\"[[vaskerrow]]\", \"[[tenhallow]]\"], pov: \"[[oshelleth]]\""));
        source = Note(source, "faction", "factions", "tenhallow-regency", "The Tenhallow Regency", "  leader: \"[[oshelleth]]\"\n  part_of: \"[[banked-flame]]\"\n  base: \"[[vaskerrow]]\"\n");
        source = Note(source, "item", "items", "the-ledger", "The Ledger", "  owner: \"[[oshelleth]]\"\n  location: \"[[vaskerrow]]\"\n");
        source = Note(source, "event", "events", "the-sinking", "The Sinking", "  location: \"[[vaskerrow]]\"\n  participants: [\"[[oshelleth]]\", \"[[banked-flame]]\"]\n");

        Assert.Empty(LinkIndex.Load(source).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-54")]
    public void AFieldThatAsksForAnItemTakesAnArtifact()
    {
        // No core field links to an item, so a den's own field shows it.
        var source = Examples()
            .With("den.yaml", "format: 1\npacks: [epic-fantasy, fable]\n")
            .With("packs/fable/pack.yaml", "name: Fable\n")
            .With("packs/fable/extensions/character.yaml", "fields:\n  carries: { type: ref, to: item }\n");
        source = Note(source, "character", "characters", "maret-ostrand", "Maret Ostrand", "  carries: \"[[widows-wick]]\"\n");

        Assert.Empty(LinkIndex.Load(source).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-55")]
    public void ALocationCanSayWhatSortOfPlaceItIs()
    {
        var kind = DenTypes.BuiltIn.Find("location")!.FindField("kind")!;

        Assert.Equal((FieldKind.Text, false), (kind.Kind, kind.IsTemporal));
        Assert.Contains("  kind:\n", DenTypes.BuiltIn.Find("location")!.Template.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-55")]
    public void AWholeStackOfPlacesNestsThroughPartOf()
    {
        var source = Examples();
        var index = LinkIndex.Load(source);

        // F-FLD-2 comes from DenCheck, so this is what shows `kind` is a location's field.
        Assert.DoesNotContain(DenCheck.Run(source).Diagnostics, d => d.Diagnostic.RuleId == "F-FLD-2");
        Assert.Empty(index.Diagnostics);
        var realm = Assert.Single(index.Catalog.Entities, e => e.Name == "Vaskerrow");
        Assert.Contains(index.LinksTo(realm), l => l.Occurrence.Path == $"{Fable}/entities/locations/tenhallow.md");
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-55")]
    public void TheRealmsRulersAreAFactionSoAPlaceHeldByARealmWarns()
    {
        // A realm is a place and not a government (design.md §4, #33). Rule
        // can change while the land carries on, so who holds a place is a faction.
        var source = Note(Examples(), "location", "locations", "the-marsh-fort", "The Marsh Fort", "  controlled_by: \"[[vaskerrow]]\"\n");

        var warning = Assert.Single(LinkIndex.Load(source).Diagnostics);

        Assert.Equal("F-LINK-11", warning.Diagnostic.RuleId);
        Assert.Contains("controlled_by is a ref to faction", warning.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-54")]
    public void ALinkToTheWrongKindOfNoteIsTheUsualWarning()
    {
        var source = Fantasy()
            .With($"{Book}/book.md", Chapter("setting: \"[[oshelleth]]\""));
        source = Note(source, "deity", "deities", "oshelleth", "Oshelleth");

        var warning = Assert.Single(LinkIndex.Load(source).Diagnostics);

        Assert.Equal("F-LINK-11", warning.Diagnostic.RuleId);
        Assert.Contains("setting is a ref to location or a kind of it (realm)", warning.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-56")]
    public void OneNamedBeastsSpeciesLinksToItsCreature()
    {
        // One named individual is a character. The field is core's text, so a
        // link takes no check on its type, and the creature's page lists it.
        var source = Note(Note(Fantasy(), "creature", "creatures", "sedge-drake", "Sedge Drake"),
            "character", "characters", "vethrisk", "Vethrisk", "  species: \"[[sedge-drake]]\"\n");

        var index = LinkIndex.Load(source);

        Assert.Empty(index.Diagnostics);
        var creature = Assert.Single(index.Catalog.Entities, e => e.Name == "Sedge Drake");
        Assert.Contains(index.LinksTo(creature), l => l.Occurrence.Path == $"{Fable}/entities/characters/vethrisk.md");
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-56")]
    public void AFieldThatAsksForASpeciesTakesACreatureButNotTheOtherWayRound()
    {
        var source = Fantasy()
            .With("den.yaml", "format: 1\npacks: [epic-fantasy, fable]\n")
            .With("packs/fable/pack.yaml", "name: Fable\n")
            .With("packs/fable/extensions/character.yaml", "fields:\n  mount: { type: ref, to: creature }\n  people: { type: ref, to: species }\n");
        source = Note(source, "creature", "creatures", "sedge-drake", "Sedge Drake");
        source = Note(source, "species", "species", "ondrahi", "Ondrahi");
        source = Note(source, "character", "characters", "maret-ostrand", "Maret Ostrand", "  mount: \"[[sedge-drake]]\"\n  people: \"[[sedge-drake]]\"\n");
        source = Note(source, "character", "characters", "vethrisk", "Vethrisk", "  mount: \"[[ondrahi]]\"\n");

        var warning = Assert.Single(LinkIndex.Load(source).Diagnostics);

        Assert.Equal(($"{Fable}/entities/characters/vethrisk.md", "F-LINK-11"), (warning.Path, warning.Diagnostic.RuleId));
        Assert.Equal(["species"], Type("creature").Ancestors);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-57")]
    public void ADeityDeclaresItsOwnSpeciesAndFactionsSinceKindOfPassesNoFields()
    {
        var core = Type("character");
        var deity = Type("deity");

        Assert.Equal((core.FindField("species")!.Kind, core.FindField("species")!.IsList), (deity.FindField("species")!.Kind, deity.FindField("species")!.IsList));
        foreach (var name in new[] { "gender", "pronouns" })
        {
            var (field, expected) = (deity.FindField(name)!, core.FindField(name)!);
            Assert.Equal((expected.Kind, expected.IsList, expected.IsTemporal, expected.Description), (field.Kind, field.IsList, field.IsTemporal, field.Description));
        }

        Assert.Equal(core.Sections.Single(s => s.Name == "Want").Description, deity.Sections.Single(s => s.Name == "Want").Description);
        var (ours, theirs) = (deity.FindField("factions")!, core.FindField("factions")!);
        Assert.Equal((theirs.Kind, theirs.IsList, theirs.IsTemporal), (ours.Kind, ours.IsList, ours.IsTemporal));
        Assert.Equal(theirs.To, ours.To);

        // A kind of god is a species, a pantheon is a faction, and worshippers can be a culture.
        var source = Fantasy();
        source = Note(source, "species", "species", "aurrendi", "Aurrendi");
        source = Note(source, "faction", "factions", "nine-embers", "The Nine Embers");
        source = Note(source, "culture", "cultures", "sedgefolk", "The Sedgefolk");
        source = Note(source, "deity", "deities", "oshelleth", "Oshelleth",
            "  species: \"[[aurrendi]]\"\n  factions: [\"[[nine-embers]]\"]\n  worshippers: [\"[[sedgefolk]]\"]\n");

        Assert.Empty(LinkIndex.Load(source).Diagnostics);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-55")]
    public void ARealmChangedToALocationKeepsItsLinksAndSaysWhatToFix()
    {
        // What the guide says happens when a writer changes their mind.
        var source = Note(Fantasy(), "location", "locations", "varrowland", "Varrowland");
        source = source.With($"{Fable}/entities/realms/vaskerrow.md", $"---\ntype: location\nname: Fablerow\nstatus: canon\nfields:\n  part_of:\n    - {{ value: \"[[varrowland]]\", from: \"the-long-tithe:1\" }}\n  seat: \"[[varrowland]]\"\n---\n");

        var check = DenCheck.Run(source);

        Assert.Equal(0, check.ErrorCount);
        Assert.Equal(
            ["F-FLD-2", "F-LAY-6", "F-TMP-8"],
            check.Diagnostics.Where(d => d.Path.EndsWith("vaskerrow.md", StringComparison.Ordinal) && d.Diagnostic.RuleId != "F-ID-2").Select(d => d.Diagnostic.RuleId).Order(StringComparer.Ordinal));
        Assert.Contains(LinkIndex.Load(source).Links, l => l.Occurrence.Path.EndsWith("vaskerrow.md", StringComparison.Ordinal) && l.Target?.Name == "Varrowland");
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-53")]
    public void BothGenrePacksCanBeOnTogether()
    {
        var types = DenTypes.Load(null, ["space-opera", "epic-fantasy"]);

        Assert.Empty(types.Diagnostics);
        Assert.Equal(23, types.Types.Count);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-53")]
    public void ADensOwnRealmWinsOverThePacks()
    {
        var den = DenStructure.Load(Fantasy()
            .With("den.yaml", "format: 1\npacks: [fable, epic-fantasy]\n")
            .With("packs/fable/pack.yaml", "name: Fable\n")
            .With("packs/fable/types/realm.yaml", "name: Land\nplural: realms\n"));

        Assert.Equal("Land", den.Types.Find("realm")!.Name);
        Assert.Empty(den.Types.Find("realm")!.KindOf);
        var clash = Assert.Single(den.Diagnostics);
        Assert.Equal(("packs/fable/types/realm.yaml", "F-TYPE-4"), (clash.Path, clash.Diagnostic.RuleId));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-53")]
    public void ADenLocalPackNamedEpicFantasyShadowsTheBuiltInOne()
    {
        var den = DenStructure.Load(Fantasy()
            .With("packs/epic-fantasy/pack.yaml", "name: My Fantasy\n")
            .With("packs/epic-fantasy/types/realm.yaml", "name: Land\nplural: lands\n"));

        var pack = den.Types.Packs[1];
        Assert.Equal(("My Fantasy", false, true), (pack.Name, pack.IsBuiltIn, pack.Shadows));
        Assert.Null(den.Types.Find("deity"));
        var shadow = Assert.Single(den.Diagnostics);
        Assert.Equal(("packs/epic-fantasy/pack.yaml", "F-PACK-10"), (shadow.Path, shadow.Diagnostic.RuleId));
    }
}

using Wolfden.Core.Checks;
using Wolfden.Core.Documents;
using Wolfden.Core.Entities;
using static Wolfden.Core.Tests.LinkDens;

namespace Wolfden.Core.Tests;

/// <summary>
/// The plain-text name checks (format.md F-NAME-1, F-NAME-2, #67): a name readers
/// learn later, written as plain text that readers reach first, or in the file name.
/// </summary>
public class NameCheckTests
{
    private const string Courier = $"{Starfall}/entities/characters/the-courier.md";
    private const string Ledger = $"{Starfall}/entities/characters/the-ledger.md";
    private const string MaraVoss = "known_as: [{ value: Mara Voss, from: \"ash:2\" }]";

    private static MemoryDenSource WithCourier(string knownAs = MaraVoss, bool spoilerChecks = true) =>
        Den(spoilerChecks).Entity(Starfall, "the-courier", Character("The Courier", knownAs));

    /// <summary>The F-NAME rules found, as (path, rule, line), in order.</summary>
    private static List<(string Path, string Rule, int? Line)> Names(MemoryDenSource source) =>
        DenCheck.Run(source).Diagnostics
            .Where(d => d.Diagnostic.RuleId.StartsWith("F-NAME-", StringComparison.Ordinal))
            .Select(d => (d.Path, d.Diagnostic.RuleId, d.Diagnostic.Line))
            .ToList();

    /// <summary>The 1-based line of the first line of <paramref name="file"/> that holds <paramref name="marker"/>.</summary>
    private static int LineOf(string file, string marker) =>
        file.Split('\n').Select((l, i) => (l, i)).First(p => p.l.Contains(marker, StringComparison.Ordinal)).i + 1;

    [Fact]
    [Trait("Scenario", "SC-CHECK-65")]
    public void ANameInPlainTextBeforeItsRevealIsAWarning()
    {
        const string body = """

            Mara Voss signed it. (lead)

            ## Ember
            <!-- wolfden: from=ember:2 -->
            Her sister's name is Mara Voss's too. **MARA VOSS** again. (possessive)
            Then there's Mara
            Voss, split over two lines. (wrapped)
            Mara Vossler isn't her. Nor is Mara, the other one.
            `Mara Voss` is in code. <!-- Mara Voss is in a comment. -->
            A stranger, [[Mara Voss|the stranger]], paid, and [[The Courier|Mara Voss]] left. (label)
            Mara <!-- what --> Voss reads as one name. (joined)
            Mara [[The Courier]] Voss doesn't.

            ```
            Mara Voss in a code block.
            ```

            ## Later
            <!-- wolfden: from=ash:2 -->
            Mara Voss arrives.

            ## Session notes
            Mara Voss, to remember.

            ## Ideas
            <!-- wolfden: status=idea -->
            Mara Voss could have a twin.

            """;
        var source = WithCourier().Entity(Starfall, "the-ledger", Character("The Ledger"), body);
        var file = System.Text.Encoding.UTF8.GetString(source.ReadFile(Ledger));

        var found = Names(source);

        Assert.Equal(
            new[] { "(lead)", "(possessive)", "Then there's Mara", "(label)", "(joined)" }.Select(m => (Ledger, "F-NAME-1", (int?)LineOf(file, m))),
            found);
        var messages = DenCheck.Run(source).Diagnostics.Where(d => d.Diagnostic.RuleId == "F-NAME-1").Select(d => d.Diagnostic.Message).ToList();
        Assert.StartsWith("'Mara Voss' is a name of 'The Courier' that readers learn at ash:2, and this text is at the start of the story.", messages[0], StringComparison.Ordinal);
        Assert.StartsWith("'Mara Voss' is a name of 'The Courier' that readers learn at ash:2, and this text is at ember:2.", messages[1], StringComparison.Ordinal);
        Assert.Contains("allow_in", messages[0], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-65")]
    public void TheNotesOwnTextHeadingsTitleAndFieldValuesAreChecked()
    {
        const string body = "\n# Mara Voss\n\nThe Courier, really Mara Voss.\n\n## Mara Voss's past\n<!-- wolfden: from=ember:1 -->\nA heading names her.\n";
        var source = Den().Entity(Starfall, "the-courier", Character("The Courier", MaraVoss,
            "fields:\n  title: Mara Voss of the docks\n  rank:\n    - { value: \"Mara Voss's second\", from: \"ember:3\" }\n    - { value: \"Voss, Mara Voss\", from: \"ash:3\" }"), body);
        var file = System.Text.Encoding.UTF8.GetString(source.ReadFile(Courier));

        var found = Names(source);

        Assert.Equal(
            new[] { "# Mara Voss", "really Mara Voss", "## Mara Voss's past", "title:", "Mara Voss's second" }
                .Select(m => (Courier, "F-NAME-1", (int?)LineOf(file, m))).OrderBy(f => f.Item3),
            found.OrderBy(f => f.Line));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-65")]
    public void TextReadersReachOnlyAfterTheyMeetTheNoteIsAtThatEntry()
    {
        // A note met at ash:3 shows nothing before then, so its lead is fine; a section that says ember:1 is too.
        var source = WithCourier().Entity(Starfall, "the-ledger", Character("The Ledger", "from: \"ash:3\""),
            "\nMara Voss signed it.\n\n## Early\n<!-- wolfden: from=ember:1 -->\nMara Voss again.\n");

        Assert.Empty(Names(source));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-65")]
    public void ANameReadersLearnOnlyWhenTheyMeetTheNoteCountsFromThere()
    {
        // The from is earlier than the note's own entry, so readers learn the name when they meet the note, at ash:3.
        var source = Den()
            .Entity(Starfall, "the-courier", Character("The Courier", "from: \"ash:3\"", "known_as: [{ value: Mara Voss, from: \"ember:1\" }]"))
            .Entity(Starfall, "the-ledger", Character("The Ledger"), "\n## Then\n<!-- wolfden: from=ash:1 -->\nMara Voss signed it.\n");

        var warning = Assert.Single(DenCheck.Run(source).Diagnostics, d => d.Diagnostic.RuleId == "F-NAME-1");
        Assert.Contains("readers learn at ash:3, and this text is at ash:1", warning.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-66")]
    [InlineData("known_as: [{ value: Rose, from: \"ember:2\" }]", false)]
    [InlineData("known_as: [{ value: Rose, from: \"ember:2\", check_text: true }]", true)]
    [InlineData("known_as: [{ value: Rose, from: \"ember:2\", check_text: TRUE }]", true)]
    [InlineData("known_as: [{ value: Rose Hale, from: \"ember:2\", check_text: false }]", false)]
    [InlineData("known_as: [{ value: Rose Hale, from: \"ember:2\" }]", true)]
    public void AOneWordNameIsCheckedOnlyWhenTheEntrySaysSo(string knownAs, bool warns)
    {
        var source = WithCourier(knownAs).Entity(Starfall, "the-ledger", Character("The Ledger"), "\nThe rose bloomed, said Rose Hale.\n");

        Assert.Equal(warns, Names(source).Count == 1);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-66")]
    public void AOneWordNameThatIsntCheckedStillHides()
    {
        var entity = Assert.Single(EntityCatalog.Load(WithCourier("known_as: [{ value: Rose, from: \"ember:2\" }]")).Entities);

        var rose = Assert.Single(entity.KnownAs);
        Assert.False(rose.IsTextChecked);
        Assert.True(rose.IsLive);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-66")]
    public void ACheckTextThatIsntABooleanIsAWarningAndChecks()
    {
        var source = WithCourier("known_as: [{ value: Rose, from: \"ember:2\", check_text: yes }]")
            .Entity(Starfall, "the-ledger", Character("The Ledger"), "\nRose went home.\n");

        var check = DenCheck.Run(source);

        var warning = Assert.Single(check.Diagnostics, d => d.Path == Courier && d.Diagnostic.RuleId == "F-ENT-10");
        Assert.Contains("check_text of 'Rose' is 'yes', which isn't true or false, so the name is checked in plain text as if it said true", warning.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Single(check.Diagnostics, d => d.Path == Ledger && d.Diagnostic.RuleId == "F-NAME-1");
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-67")]
    public void AllowInSilencesAWholeNoteAndNoOther()
    {
        var source = WithCourier("known_as: [{ value: Mara Voss, from: \"ash:2\", allow_in: [the-old-ledger] }]")
            .Entity(Starfall, "the-old-ledger", Character("The Old Ledger"), "\nA different Mara Voss, long dead.\n\n## More\nMara Voss again.\n")
            .Entity(Starfall, "the-ledger", Character("The Ledger"), "\nMara Voss signed it.\n");

        var check = DenCheck.Run(source);

        Assert.Equal([(Ledger, "F-NAME-1", (int?)7)], Names(source));
        Assert.DoesNotContain(check.Diagnostics, d => d.Path == Courier && d.Diagnostic.RuleId != "F-ID-2");
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-67")]
    [InlineData("allow_in: the-old-ledger", null)]
    [InlineData("allow_in: [The Old Ledger]", null)]
    [InlineData("allow_in: [the-old-leger]", "names 'the-old-leger', which reaches no one note: 'the-old-leger' matches no entity")]
    [InlineData("allow_in: [[the-old-ledger]]", "has 'a list', which isn't a note's slug, name, alias, or id")]
    [InlineData("allow_in: { the-old-ledger: true }", "has 'a mapping'")]
    public void AnAllowInThatReachesNoNoteIsAWarningAndKeepsChecking(string allowIn, string? warning)
    {
        var source = WithCourier($"known_as: [{{ value: Mara Voss, from: \"ash:2\", {allowIn} }}]")
            .Entity(Starfall, "the-old-ledger", Character("The Old Ledger"), "\nA different Mara Voss.\n");

        var check = DenCheck.Run(source);

        var courier = check.Diagnostics.Where(d => d.Path == Courier && d.Diagnostic.RuleId == "F-ENT-10").Select(d => d.Diagnostic.Message).ToList();
        var silenced = !check.Diagnostics.Any(d => d.Diagnostic.RuleId == "F-NAME-1");
        if (warning is null)
        {
            Assert.Empty(courier);
            Assert.True(silenced);
        }
        else
        {
            Assert.Contains(warning, Assert.Single(courier), StringComparison.Ordinal);
            Assert.False(silenced);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-68")]
    public void WithSpoilerChecksOffNothingIsCheckedAndTheNameStillHides()
    {
        var source = WithCourier(spoilerChecks: false)
            .With($"{Starfall}/entities/characters/mara-voss.md", $"---\n{Character("The Courier's twin", "known_as: [{ value: Mara Voss, from: \"ash:2\", allow_in: [nobody] }]")}\n---\n")
            .Entity(Starfall, "the-ledger", Character("The Ledger"), "\nMara Voss signed it.\n");

        var check = DenCheck.Run(source);

        Assert.DoesNotContain(check.Diagnostics, d => d.Diagnostic.RuleId.StartsWith("F-NAME-", StringComparison.Ordinal));
        Assert.DoesNotContain(check.Diagnostics, d => d.Diagnostic.Message.Contains("allow_in", StringComparison.Ordinal));
        var courier = Assert.Single(check.Catalog.Entities, e => e.Name == "The Courier");
        Assert.True(Assert.Single(courier.KnownAs).IsLive);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-68")]
    public void TheCheckThatOnlyWantsUnreadableFilesSkipsTheNames()
    {
        var source = WithCourier().Entity(Starfall, "the-ledger", Character("The Ledger"), "\nMara Voss signed it.\n");

        Assert.Single(DenCheck.Run(source, null).Diagnostics, d => d.Diagnostic.RuleId == "F-NAME-1");
        Assert.DoesNotContain(DenCheck.Run(source, null, namesInText: false).Diagnostics, d => d.Diagnostic.RuleId == "F-NAME-1");
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-69")]
    [InlineData("mara-voss", MaraVoss, true)]
    [InlineData("the-courier-mara-voss", MaraVoss, true)]
    [InlineData("the-courier", MaraVoss, false)]
    [InlineData("mara-vossler", MaraVoss, false)]
    [InlineData("voss", "known_as: [{ value: Voss, from: \"ash:2\" }]", true)]
    [InlineData("voss", "known_as: [{ value: Voss, from: \"ash:2\", check_text: false }]", true)]
    [InlineData("mara-voss", "from: \"ash:2\"\nknown_as: [{ value: Mara Voss, from: \"ash:2\" }]", false)]
    public void AFileNameThatHoldsTheLaterNameIsAWarning(string slug, string knownAs, bool warns)
    {
        var path = $"{Starfall}/entities/characters/{slug}.md";
        var source = Den().Entity(Starfall, slug, Character("The Courier", knownAs));

        var found = Names(source).Where(f => f.Rule == "F-NAME-2").ToList();

        List<(string, string, int?)> expected = warns ? [(path, "F-NAME-2", LineOf(System.Text.Encoding.UTF8.GetString(source.ReadFile(path)), "value:"))] : [];
        Assert.Equal(expected, found);
        if (warns)
        {
            var message = DenCheck.Run(source).Diagnostics.Single(d => d.Diagnostic.RuleId == "F-NAME-2").Diagnostic.Message;
            Assert.StartsWith($"The file name '{slug}.md' holds", message, StringComparison.Ordinal);
            Assert.Contains("Rename the file", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoWarningWhenTheWordsAreAlreadyANameReadersKnow()
    {
        // The Harbourmaster is Old Tam from the start; the other note's Old Tam arrives at ash:2 (design §10).
        var source = Den()
            .Entity(Starfall, "the-harbourmaster", Character("The Harbourmaster", "aliases: [Old Tam]"))
            .Entity(Starfall, "the-stranger", Character("The Stranger", "known_as: [{ value: Old Tam, from: \"ash:2\" }]"))
            .Entity(Starfall, "the-ledger", Character("The Ledger"), "\nOld Tam signed it.\n");

        Assert.Empty(Names(source));

        // Once The Harbourmaster is met only at ash:1, earlier text does warn.
        var later = Den()
            .Entity(Starfall, "the-harbourmaster", Character("The Harbourmaster", "from: \"ash:1\"", "aliases: [Old Tam]"))
            .Entity(Starfall, "the-stranger", Character("The Stranger", "known_as: [{ value: Old Tam, from: \"ash:2\" }]"))
            .Entity(Starfall, "the-ledger", Character("The Ledger"), "\nOld Tam signed it.\n\n## Then\n<!-- wolfden: from=ash:1 -->\nOld Tam again.\n");
        Assert.Equal([(Ledger, "F-NAME-1", (int?)7)], Names(later));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-65")]
    public void MatchingIgnoresAccentsBothWays()
    {
        // D5: the first look folds the name's words, so an accented name is found in unaccented text and the reverse.
        var source = Den()
            .Entity(Starfall, "the-courier", Character("The Courier", "known_as: [{ value: Zoë Arkwright, from: \"ash:2\" }, { value: Noel Varga, from: \"ash:2\" }]"))
            .Entity(Starfall, "plain", Character("Plain"), "\nZoe Arkwright signed it.\n")
            .Entity(Starfall, "accented", Character("Accented"), "\nNoël Várga signed it. ZOË ARKWRIGHT too.\n")
            .Entity(Starfall, "decomposed", Character("Decomposed"), "\nZoë Arkwright signed it.\n");

        var found = Names(source).Select(f => f.Path[(f.Path.LastIndexOf('/') + 1)..]).ToList();

        Assert.Equal(["accented.md", "accented.md", "decomposed.md", "plain.md"], found.Order());
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-65")]
    public void LettersThatDontDecomposeArentFolded()
    {
        var source = Den()
            .Entity(Starfall, "the-courier", Character("The Courier", "known_as: [{ value: Søren Holt, from: \"ash:2\" }]"))
            .Entity(Starfall, "the-ledger", Character("The Ledger"), "\nSoren Holt signed it. SØREN HOLT too.\n");

        // ø has no decomposition, so Soren isn't Søren; the case still folds.
        var warning = Assert.Single(DenCheck.Run(source).Diagnostics, d => d.Diagnostic.RuleId == "F-NAME-1");
        Assert.Equal(Ledger, warning.Path);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-65")]
    public void AnUnaccentedSpellingListedAsItsOwnEntryIsCheckedFromItsOwnFrom()
    {
        // Each spelling is its own entry; an unaccented one is checked like any other. Folded, the two are the
        // same words, so text at ash:2 has a name readers know (Zoë Ark, from ash:1), and doesn't warn.
        var source = Den()
            .Entity(Starfall, "the-courier", Character("The Courier", "known_as:\n  - { value: Zoë Ark, from: \"ash:1\" }\n  - { value: Zoe Ark, from: \"ash:3\" }"))
            .Entity(Starfall, "the-ledger", Character("The Ledger"),
                "\n## Early\n<!-- wolfden: from=ember:1 -->\nZoe Ark signed it.\n\n## Middle\n<!-- wolfden: from=ash:2 -->\nZoe Ark again.\n");

        var warning = Assert.Single(DenCheck.Run(source).Diagnostics, d => d.Diagnostic.RuleId == "F-NAME-1");
        Assert.Contains("'Zoë Ark' is a name of 'The Courier' that readers learn at ash:1, and this text is at ember:1", warning.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CrlfFilesAreReadTheSame()
    {
        var source = WithCourier().With(Ledger, "---\r\ntype: character\r\nname: The Ledger\r\nstatus: canon\r\n---\r\n\r\nFirst line.\r\n<!--\r\nMara Voss, hidden.\r\n-->\r\nMara\r\nVoss signed it.\r\n");

        Assert.Equal([(Ledger, "F-NAME-1", (int?)11)], Names(source));
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-65")]
    public void ANotesNameAndAliasesAreChecked()
    {
        var source = WithCourier().Entity(Starfall, "the-letter", Character("Mara Voss's Letter", "aliases: [the letter, Voss letter, letter of Mara Voss]"));
        var path = $"{Starfall}/entities/characters/the-letter.md";
        var file = System.Text.Encoding.UTF8.GetString(source.ReadFile(path));

        Assert.Equal([(path, "F-NAME-1", (int?)LineOf(file, "name:")), (path, "F-NAME-1", LineOf(file, "aliases:"))], Names(source));
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-65")]
    [InlineData("<div>\n\nPara.\n\n    Mara Voss, indented code.\n")]
    [InlineData("<div>\r\n\r\nPara.\r\n\r\n    Mara Voss, indented code.\r\n")]
    [InlineData("<div>\n\n```\nMara Voss, in a fence nothing closes.\n")]
    [InlineData("<div>\r\n\r\n```\r\nMara Voss, in a fence nothing closes.\r\n")]
    [InlineData("Text.\r\n\r\n```text\r\nMara Voss\r\n```\r\n\r\nMore `Mara Voss` text.\r\n")]
    public void CodeAfterAnHtmlBlockIsCodeWhateverTheLineBreaks(string body)
    {
        var source = WithCourier().Entity(Starfall, "the-ledger", Character("The Ledger"), "\n" + body);

        Assert.Empty(Names(source));
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-65")]
    [InlineData("Mara Voss`'s` ship.")]
    [InlineData("Mara Voss[[the-courier]] left.")]
    [InlineData("A `code`Mara Voss.")]
    [InlineData("[[the-ledger]]Mara Voss.")]
    public void CodeOrALinkTouchingTheNameDoesntHideIt(string text)
    {
        var source = WithCourier().Entity(Starfall, "the-ledger", Character("The Ledger"), $"\n{text}\n");

        Assert.Single(Names(source));
    }

    [Theory]
    [Trait("Scenario", "SC-CHECK-65")]
    [InlineData("known_as: [{ value: Mara Voss, from: \"ash:9\" }]")]
    [InlineData("known_as: [{ value: Mara Voss }]")]
    public void ANameReadersNeverLearnIsLookedForEverywhere(string knownAs)
    {
        // Its from is missing or doesn't resolve, so readers never learn it, and every use gives it away.
        var source = WithCourier(knownAs).Entity(Starfall, "the-ledger", Character("The Ledger"),
            "\n## Late\n<!-- wolfden: from=ash:3 -->\nMara Voss signed it.\n");

        var warning = Assert.Single(DenCheck.Run(source).Diagnostics, d => d.Diagnostic.RuleId == "F-NAME-1");
        Assert.Contains("that readers learn at no point in the story, since its from doesn't resolve, and this text is at ash:3", warning.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-65")]
    public void ANameSpelledWithAYamlEscapeIsFound()
    {
        // The raw text holds neither the accent nor the folded word, so the first look reads the frontmatter's strings too.
        var source = Den()
            .Entity(Starfall, "the-courier", Character("The Courier", "known_as: [{ value: Amélie Roux, from: \"ash:2\" }]"))
            .Entity(Starfall, "the-ledger", Character("The Ledger", "fields:\n  signer: \"Am\\u00e9lie Roux\""));

        Assert.Single(Names(source));
    }

    [Fact]
    public void NotesOutOfCanonAreNeitherCheckedNorCheckedFor()
    {
        var source = Den()
            .Entity(Starfall, "the-courier", Character("The Courier", "status: idea", MaraVoss))
            .Entity(Starfall, "the-ledger", Character("The Ledger"), "\nMara Voss signed it.\n")
            .Entity(Starfall, "the-twin", Character("The Twin", "known_as: [{ value: Ilse Varn, from: \"ash:2\" }]"))
            .Entity(Starfall, "the-draft", Character("The Draft", "status: idea"), "\nIlse Varn signed it.\n");

        Assert.Empty(Names(source));
    }

    [Fact]
    public void EachLineWarnsOnceForEachName()
    {
        var source = WithCourier().Entity(Starfall, "the-ledger", Character("The Ledger"), "\nMara Voss and Mara Voss and Mara Voss.\n");

        Assert.Single(Names(source));
    }

    [Fact]
    public void CheckTextAndAllowInAreKnownKeys()
    {
        var catalog = EntityCatalog.Load(WithCourier("known_as: [{ value: Mara Voss, from: \"ash:2\", check_text: false, allow_in: [the-ledger, Old Tam] }]"));

        Assert.DoesNotContain(catalog.Diagnostics, d => d.Path == Courier && d.Diagnostic.RuleId == "F-ENT-10");
        var known = Assert.Single(Assert.Single(catalog.Entities).KnownAs);
        Assert.False(known.CheckText);
        Assert.Equal(["the-ledger", "Old Tam"], known.AllowIn);
    }
}

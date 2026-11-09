using System.Text;
using Wolfden.Core.Checks;
using Wolfden.Core.Structure;
using static Wolfden.Core.Tests.LinkDens;

namespace Wolfden.Core.Tests;

/// <summary>
/// The write-time side of the later-name checks (#67, PR 5): the warnings a write
/// adds to the notes it saves, from <c>check</c>'s own F-NAME-1, F-NAME-2, and
/// F-LINK-13 for a link by a later name, run on those notes alone.
/// </summary>
public class WrittenTextCheckTests
{
    private const string Courier = $"{Starfall}/entities/characters/the-courier.md";
    private const string Ledger = $"{Starfall}/entities/characters/the-ledger.md";
    private const string MaraVoss = "known_as: [{ value: Mara Voss, from: \"ash:2\" }]";

    private static MemoryDenSource WithCourier(bool spoilerChecks = true) =>
        Den(spoilerChecks).Entity(Starfall, "the-courier", Character("The Courier", MaraVoss));

    private static string LedgerFile(string body, params string[] extra) => $"---\n{Character("The Ledger", extra)}\n---\n{body}";

    /// <summary>What a write of <paramref name="text"/> to <paramref name="path"/> adds, over <paramref name="den"/> as it is.</summary>
    private static IReadOnlyList<DenDiagnostic> Write(MemoryDenSource den, string path, string text) =>
        WrittenTextChecks.LaterNames(den, DenStructure.Load(den), new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = Encoding.UTF8.GetBytes(text) });

    /// <summary><c>check</c>'s later-name warnings in the file at <paramref name="path"/>, as (rule, line, message).</summary>
    private static List<(string, int?, string)> Checked(MemoryDenSource den, string path) =>
        DenCheck.Run(den).Diagnostics
            .Where(d => d.Path == path && (d.Diagnostic.RuleId.StartsWith("F-NAME-", StringComparison.Ordinal)
                || (d.Diagnostic.RuleId == "F-LINK-13" && d.Diagnostic.Message.Contains("is a name of", StringComparison.Ordinal))))
            .Select(d => (d.Diagnostic.RuleId, d.Diagnostic.Line, d.Diagnostic.Message))
            .OrderBy(d => d.Line).ThenBy(d => d.RuleId, StringComparer.Ordinal)
            .ToList();

    private static List<(string, int?, string)> Said(IReadOnlyList<DenDiagnostic> found) =>
        found.Select(d => (d.Diagnostic.RuleId, d.Diagnostic.Line, d.Diagnostic.Message)).OrderBy(d => d.Line).ThenBy(d => d.RuleId, StringComparer.Ordinal).ToList();

    public static TheoryData<string> Bodies => new()
    {
        """

        Mara Voss signed it. (lead)

        ## Ember
        <!-- wolfden: from=ember:2 -->
        Her sister's name is Mara Voss's too. **MARA VOSS** again. (possessive)
        Then there's Mara
        Voss, split over two lines. (wrapped)
        Mara Vossler isn't her. `Mara Voss` is in code. <!-- Mara Voss is in a comment. -->
        A stranger, [[Mara Voss|the stranger]], paid, and [[The Courier|Mara Voss]] left. (label)
        Mara <!-- what --> Voss reads as one name. [[Mara Voss]] signed. (joined, link)

        ## Later
        <!-- wolfden: from=ash:2 -->
        Mara Voss arrives, and [[Mara Voss]] signs.

        ## Session notes
        Mara Voss, to remember. [[Mara Voss]] too.

        """,
        "\r\nKept at the Reach.\r\n\r\n## Entries\r\n<!-- wolfden: from=ember:3 -->\r\n```\r\nMara Voss\r\n```\r\nMara Voss paid. [[Mara Voss]]\r\n",
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    [Trait("Scenario", "SC-MCP-55")]
    public void ANewNotesWarningsAreExactlyChecksOwn(string body)
    {
        var text = LedgerFile(body, "fields: { motto: \"Ask Mara Voss.\" }");

        var said = Write(WithCourier(), Ledger, text);

        var checkedNow = Checked(WithCourier().With(Ledger, text), Ledger);
        Assert.NotEmpty(checkedNow);
        Assert.Contains(checkedNow, d => d.Item1 == "F-LINK-13");
        Assert.Equal(checkedNow, Said(said));
        Assert.All(said, d => Assert.Equal(Ledger, d.Path));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void OnlyWhatTheWriteAddsIsSaid()
    {
        var before = LedgerFile("\nMara Voss signed it.\n");
        var den = WithCourier().With(Ledger, before);

        // A line added above moves the name, and nothing new is said.
        Assert.Empty(Write(den, Ledger, LedgerFile("\nKept at the Reach.\n\nMara Voss signed it.\n")));

        // A second mention at the same position is new, and so is a link by the name.
        var added = Write(den, Ledger, LedgerFile("\nMara Voss signed it.\n\n## Entries\nMara Voss paid. [[Mara Voss]] left.\n"));
        Assert.Equal([("F-NAME-1", 10), ("F-LINK-13", 10)], added.Select(d => (d.Diagnostic.RuleId, d.Diagnostic.Line ?? 0)));
        Assert.StartsWith("'Mara Voss' is a name of 'The Courier' that readers learn at ash:2, and this text is at the start of the story.", added[0].Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(
            "[[Mara Voss]]: 'Mara Voss' is a name of 'The Courier' that readers learn at ash:2, after this link's position, the start of the story. It may be a spoiler.",
            added[1].Diagnostic.Message);

        // Text given a position after the reveal says nothing; one before it, but not at the same place, is new.
        Assert.Empty(Write(den, Ledger, LedgerFile("\n## Later\n<!-- wolfden: from=ash:3 -->\nMara Voss signed it.\n")));
        var moved = Assert.Single(Write(den, Ledger, LedgerFile("\n## Ember\n<!-- wolfden: from=ember:2 -->\nMara Voss signed it.\n")));
        Assert.Contains("this text is at ember:2", moved.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void ANoteThatGainsALaterNameHearsAboutItsOwnText()
    {
        var den = WithCourier().Entity(Starfall, "the-harbour", Character("The Harbour"), "\nRun by the Lamplighter.\n");
        var harbour = $"{Starfall}/entities/characters/the-harbour.md";

        var added = Write(den, harbour, $"---\n{Character("The Harbour", "known_as: [{ value: the Lamplighter, from: \"ash:1\" }]")}\n---\n\nRun by the Lamplighter.\n");

        var found = Assert.Single(added);
        Assert.Equal("F-NAME-1", found.Diagnostic.RuleId);
        Assert.StartsWith("'the Lamplighter' is a name of 'The Harbour' that readers learn at ash:1", found.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void NothingIsSaidWithSpoilerChecksOffOrNoLaterNames()
    {
        var text = LedgerFile("\nMara Voss signed it. [[Mara Voss]]\n");

        Assert.Empty(Write(WithCourier(spoilerChecks: false), Ledger, text));
        Assert.Empty(Write(Den().Entity(Starfall, "the-courier", Character("The Courier", "aliases: [Mara Voss]")), Ledger, text));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-55")]
    public void AWriteOutsideTheNotesSaysNothing()
    {
        var den = WithCourier();

        Assert.Empty(Write(den, $"{Ember}/book.md", "---\nchapters: [prologue, 1, 2, 3]\n---\nMara Voss.\n"));
    }
}

using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// An assistant's read at a story position has no HTML comments in it, and a
/// read without one has them all (format.md F-VIEW-6, #306). What counts as a
/// comment, and what's left of the text, is in <c>MarkdownCommentsTests</c>.
/// </summary>
public sealed class CommentsAtAPositionTests : IDisposable
{
    private const string Ledger = "The tide ledger";
    private const string LedgerPath = "universes/starfall/entities/notes/the-tide-ledger.md";

    private const string LedgerText =
        """
        ---
        type: note
        name: The tide ledger
        status: canon
        from: "ember:1"
        fields:
          motto: "Kept <!-- not a comment here --> dry"
        ---

        # The tide ledger <!-- Wraithmoor burns it -->

        Lists every ship. <!-- Wraithmoor burns it -->

        ## Keeper
        <!-- wolfden: pin=true -->
        Orrin keeps it. <!-- Wraithmoor burns it -->

        ### Copies
        <!-- wolfden: pin=true -->
        <!-- Wraithmoor burns it -->
        Two copies.

        ## Binding <!-- Wraithmoor burns it --> style
        Bound in sailcloth.

        ## Empty heading
        ## Nothing but a comment
        <!-- Wraithmoor burns it -->

        ## Marked by mistake


        <!-- wolfden: until=ash:2 -->
        Body after two blanks.

        <!-- wolfden: from=ash:3 -->
        And mid-body.

        ## How to write it
        Use `<!-- Quietstone -->` for a note.

        ```
        <!-- Quietstone -->
        ```

        <!-- Wraithmoor burns it -->

        ## Last
        The last section.
        """;

    private readonly TempDen _den = TempDen.Starfall();
    private readonly WolfdenTools _tools;

    public CommentsAtAPositionTests()
    {
        _den.Write(LedgerPath, LedgerText);
        _tools = new WolfdenTools(new DenTools(_den.Root));
    }

    public void Dispose() => _den.Dispose();

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void EveryPlaceAReadAtAPositionHasTextLosesItsComments()
    {
        var entity = Text(_tools.GetEntity(Ledger, "starfall", "ember:1"));
        var canon = Text(_tools.CanonAsOf("ember:1", "starfall", type: "note"));
        var brief = Text(_tools.CanonAsOf("ember:1", "starfall", type: "note", brief: true));

        Assert.Contains("Lists every ship", entity, StringComparison.Ordinal);
        Assert.Contains("Lists every ship", canon, StringComparison.Ordinal);
        Assert.Contains("Orrin keeps it", brief, StringComparison.Ordinal); // Pinned, which brief still gives.
        foreach (var answer in new[] { entity, canon, brief })
        {
            Assert.DoesNotContain("Wraithmoor", answer, StringComparison.Ordinal);
            Assert.DoesNotContain("wolfden:", answer, StringComparison.Ordinal);
            Assert.DoesNotContain("until=", answer, StringComparison.Ordinal);
            Assert.DoesNotContain("from=", answer, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void TheTextReadsAsIfTheCommentWasNeverThere()
    {
        using var json = Read(_tools.GetEntity(Ledger, "starfall", "ember:1"));
        var entity = json.RootElement.GetProperty("entity");

        Assert.Equal("# The tide ledger\n\nLists every ship.", entity.GetProperty("lead").GetString());
        Assert.Equal("Orrin keeps it.", Body(entity, "Keeper"));
        Assert.Equal("Two copies.", Body(entity, "Copies"));
        Assert.Equal("Body after two blanks.\n\nAnd mid-body.", Body(entity, "Marked by mistake"));
        Assert.Equal("Orrin keeps it.", entity.GetProperty("pinned").GetProperty("sections")[0].GetProperty("body").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void ACommentOnlySectionReadsLikeAnEmptyOneAndStaysListed()
    {
        using var json = Read(_tools.GetEntity(Ledger, "starfall", "ember:1"));
        var entity = json.RootElement.GetProperty("entity");

        Assert.Equal("", Body(entity, "Empty heading"));
        Assert.Equal("", Body(entity, "Nothing but a comment"));
        var empty = Section(entity, "Empty heading").EnumerateObject().Select(p => p.Name);
        Assert.Equal(empty, Section(entity, "Nothing but a comment").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void ACommentOnlyLeadReadsLikeNoLead()
    {
        _den.Write("universes/starfall/entities/notes/no-lead.md", "---\ntype: note\nname: No lead\nstatus: canon\n---\n\n## Part\nText.\n");
        _den.Write("universes/starfall/entities/notes/hushed-lead.md", "---\ntype: note\nname: Hushed lead\nstatus: canon\n---\n\n<!-- Wraithmoor burns it -->\n\n<!-- Again -->\n\n## Part\nText.\n");

        using var none = Read(_tools.GetEntity("No lead", "starfall", "ember:1"));
        using var hushed = Read(_tools.GetEntity("Hushed lead", "starfall", "ember:1"));

        Assert.Equal("", none.RootElement.GetProperty("entity").GetProperty("lead").GetString());
        Assert.Equal("", hushed.RootElement.GetProperty("entity").GetProperty("lead").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void LineNumbersStayTheNotesOwn()
    {
        using var position = Read(_tools.GetEntity(Ledger, "starfall", "ember:1"));
        using var full = Read(_tools.GetEntity(Ledger, "starfall"));

        var lines = File.ReadAllLines(Path.Combine(_den.Root, LedgerPath));
        var line = Section(position.RootElement.GetProperty("entity"), "Last").GetProperty("line").GetInt32();
        Assert.Equal("## Last", lines[line - 1]);
        Assert.Equal(Section(full.RootElement.GetProperty("entity"), "Last").GetProperty("line").GetInt32(), line);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void ACommentShownAsCodeStays()
    {
        using var json = Read(_tools.GetEntity(Ledger, "starfall", "ember:1"));

        Assert.Equal(
            "Use `<!-- Quietstone -->` for a note.\n\n```\n<!-- Quietstone -->\n```",
            Body(json.RootElement.GetProperty("entity"), "How to write it"));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void ACommentInTheMiddleOfAHeadingGoesToo()
    {
        using var json = Read(_tools.GetEntity(Ledger, "starfall", "ember:1"));
        var binding = Section(json.RootElement.GetProperty("entity"), "Binding style");

        Assert.Equal(["Binding style"], binding.GetProperty("heading_path").EnumerateArray().Select(h => h.GetString()));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void AnUnclosedCommentDoesntReachTheAssistant()
    {
        _den.Write(
            "universes/starfall/entities/notes/the-open-note.md",
            """
            ---
            type: note
            name: The open note
            status: canon
            ---

            ## Early
            Early text.

            <!-- Wraithmoor burns it

            ## Swallowed
            Also swallowed.
            """);
        _den.Write(
            "universes/starfall/entities/notes/the-open-line.md",
            """
            ---
            type: note
            name: The open line
            status: canon
            ---

            ## Early
            Early text <!-- Wraithmoor burns it

            The next paragraph.
            """);

        using var block = Read(_tools.GetEntity("The open note", "starfall", "ember:1"));
        using var inline = Read(_tools.GetEntity("The open line", "starfall", "ember:1"));

        Assert.Equal("Early text.", Body(block.RootElement.GetProperty("entity"), "Early"));
        Assert.Equal("Early text\n\nThe next paragraph.", Body(inline.RootElement.GetProperty("entity"), "Early"));

        // And the writer's read, at no position, has it all.
        using var full = Read(_tools.GetEntity("The open note", "starfall"));
        Assert.Contains("Also swallowed", full.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void AReadWithNoPositionKeepsThemAll()
    {
        var full = Text(_tools.GetEntity(Ledger, "starfall"));
        var end = Text(_tools.CanonAsOf(universe: "starfall", type: "note"));

        foreach (var answer in new[] { full, end })
        {
            Assert.Contains("Wraithmoor", answer, StringComparison.Ordinal);
        }

        Assert.Contains("wolfden: until=ash:2", full, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void TheCodeTheWebReadsFromStillHasThem()
    {
        var view = new DenTools(_den.Root).GetEntity(Ledger, "starfall", "ember:1");

        Assert.Contains("Wraithmoor", view.Entity!.Lead, StringComparison.Ordinal);
        Assert.Contains(view.Entity.Sections!, s => s.Body.Contains("Wraithmoor", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void AFieldsValueIsntMarkdownSoItStaysAsWritten()
    {
        using var json = Read(_tools.GetEntity(Ledger, "starfall", "ember:1"));
        var motto = json.RootElement.GetProperty("entity").GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "motto");

        Assert.Equal("Kept <!-- not a comment here --> dry", motto.GetProperty("value").GetString());
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void AddingToASectionAfterAReadAtAPositionKeepsWhatTheReadLeftOut()
    {
        using var read = Read(_tools.GetEntity(Ledger, "starfall", "ember:1"));
        var hash = read.RootElement.GetProperty("entity").GetProperty("hash").GetString()!;

        Text(_tools.AppendSection(Ledger, "Nothing but a comment", "Now with text.", hash, "starfall", cancellationToken: TestContext.Current.CancellationToken));

        var written = File.ReadAllText(Path.Combine(_den.Root, LedgerPath)).ReplaceLineEndings("\n");
        Assert.Contains("## Nothing but a comment\n<!-- Wraithmoor burns it -->\n", written, StringComparison.Ordinal);
        Assert.Contains("Now with text.", written, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void NoToolTheAssistantHasCanReplaceASectionsText()
    {
        // The web UI's replace_section-style routes aren't tools, so a read at a position can't turn into a deleted comment here. A new one needs a guard, or at least the warning.
        var written = typeof(WolfdenTools).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false).Length > 0)
            .Select(m => ((ModelContextProtocol.Server.McpServerToolAttribute)m.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)[0]).Name!)
            .ToList();

        Assert.Contains("append_section", written);
        Assert.DoesNotContain(written, name => name.Contains("replace", StringComparison.OrdinalIgnoreCase) || name.Contains("edit", StringComparison.OrdinalIgnoreCase) || name.Contains("rewrite", StringComparison.OrdinalIgnoreCase) || name == "update_section" || name == "update_lead");
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void TheGuidanceSaysSoAndNamesTheWriteHazard()
    {
        var descriptions = typeof(WolfdenTools).GetMethods()
            .SelectMany(m => m.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Cast<System.ComponentModel.DescriptionAttribute>().Select(d => (m.Name, d.Description)))
            .ToDictionary(d => d.Name, d => d.Description);

        foreach (var tool in new[] { nameof(WolfdenTools.GetEntity), nameof(WolfdenTools.CanonAsOf) })
        {
            Assert.Contains("HTML comments are left out", descriptions[tool], StringComparison.Ordinal);
            Assert.Contains("text the writer put in a section or lead, or a field the note's type doesn't define, or a ref field's value", Unfold(descriptions[tool]), StringComparison.Ordinal);
        }

        Assert.Contains("deletes comments you never saw", Unfold(WolfdenMcpServer.Instructions), StringComparison.Ordinal);
    }

    private static string Unfold(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static JsonDocument Read(CallToolResult result) => JsonDocument.Parse(Text(result));

    private static string Text(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    private static JsonElement Section(JsonElement entity, string name) =>
        entity.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    private static string? Body(JsonElement entity, string name) => Section(entity, name).GetProperty("body").GetString();
}

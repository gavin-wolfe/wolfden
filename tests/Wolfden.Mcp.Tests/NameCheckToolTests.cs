using System.Text.Json;

namespace Wolfden.Mcp.Tests;

/// <summary>
/// The plain-text name checks through the MCP tools (format.md F-NAME-1, F-NAME-2,
/// #67): <c>check</c> lists them, and <c>update_fields</c> sets an entry's
/// <c>check_text</c> and <c>allow_in</c> without touching a link.
/// </summary>
public sealed class NameCheckToolTests : IDisposable
{
    private const string Courier = "universes/starfall/entities/notes/mara-voss.md";
    private const string Ledger = "universes/starfall/entities/notes/the-ledger.md";

    private readonly TempDen _den = TempDen.Starfall();

    public NameCheckToolTests()
    {
        // Filed under the real name, so the file name gives it away (F-NAME-2).
        _den.Write(
            Courier,
            """
            ---
            id: 01M4TF5F0EKGGWD5FNVBAEY8K1
            type: note
            name: The Courier
            known_as:
              - { value: Mara Voss, from: "ash:2" }
            status: canon
            from: "ember:1"
            ---

            Carries the post.
            """);
        _den.Write(
            Ledger,
            """
            ---
            id: 01M4TF5F0EKGGWD5FNVBAEY8K2
            type: note
            name: The ledger
            status: canon
            from: "ember:1"
            ---

            Kept at the Reach.

            ## Entries
            Mara Voss signed it, and [[Mara Voss|a stranger]] paid.
            """);
    }

    private DenTools Tools => new(_den.Root);

    public void Dispose() => _den.Dispose();

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private List<DiagnosticView> NameFindings() =>
        Tools.Check(null, null, 5000, "starfall").Diagnostics.Where(d => d.Rule.StartsWith("F-NAME-", StringComparison.Ordinal)).ToList();

    [Fact]
    [Trait("Scenario", "SC-CHECK-65")]
    [Trait("Scenario", "SC-CHECK-69")]
    public void CheckListsBothRules()
    {
        var found = NameFindings();

        Assert.Equal([(Courier, "F-NAME-2", "warning"), (Ledger, "F-NAME-1", "warning")], found.Select(d => (d.Path, d.Rule, d.Severity)));
        Assert.Equal(12, found[1].Line);
        Assert.StartsWith("'Mara Voss' is a name of 'The Courier' that readers learn at ash:2, and this text is at ember:1.", found[1].Message, StringComparison.Ordinal);
        Assert.StartsWith("The file name 'mara-voss.md' holds 'Mara Voss'", found[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-CHECK-67")]
    public void UpdateFieldsSetsAllowInAndCheckTextAndChangesNoLink()
    {
        var hash = Tools.GetEntity("The Courier", "starfall").Entity!.Hash!;
        var ledgerBefore = File.ReadAllText(Path.Combine(_den.Root, Ledger));

        var result = Tools.UpdateFields("The Courier", hash, Json("""
            [{"op": "set", "path": "known_as.0.allow_in", "value": ["the-ledger"]},
             {"op": "set", "path": "known_as.0.check_text", "value": true}]
            """), "starfall");

        Assert.True(result.Changed);
        Assert.Equal(ledgerBefore, File.ReadAllText(Path.Combine(_den.Root, Ledger)));
        var text = File.ReadAllText(Path.Combine(_den.Root, Courier));
        Assert.Contains("allow_in", text, StringComparison.Ordinal);
        Assert.Contains("check_text: true", text, StringComparison.Ordinal);
        Assert.Equal([("F-NAME-2", Courier)], NameFindings().Select(d => (d.Rule, d.Path)));

        // Removing it brings the warning back; a name still can't be changed here.
        var removed = Tools.UpdateFields("The Courier", result.Hash, Json("""[{"op": "remove", "path": "known_as.0.allow_in"}]"""), "starfall");
        Assert.Contains(NameFindings(), d => d.Rule == "F-NAME-1");
        var refused = Assert.Throws<DenToolException>(() => Tools.UpdateFields("The Courier", removed.Hash, Json("""[{"op": "set", "path": "known_as.0.value", "value": "Mara Holt"}]"""), "starfall"));
        Assert.Equal("unsupported_edit", refused.Code);
    }
}

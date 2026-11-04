using Wolfden.Core.Documents;

namespace Wolfden.Core.Tests;

/// <summary>Separating HTML comments (F-SEC-11) from the text around them.</summary>
public class MarkdownCommentsTests
{
    [Fact]
    public void TextWithoutCommentsIsUnchanged()
    {
        Assert.Equal(("Plain *text*.\n", ""), MarkdownComments.Split("Plain *text*.\n"));
    }

    [Fact]
    public void InlineAndBlockCommentsAreSplitOut()
    {
        var (text, comments) = MarkdownComments.Split("Keeper. <!-- todo: harbour -->\n\n<!--\nsecond\n-->\nAfter.\n");

        Assert.DoesNotContain("harbour", text, StringComparison.Ordinal);
        Assert.DoesNotContain("second", text, StringComparison.Ordinal);
        Assert.Contains("Keeper.", text, StringComparison.Ordinal);
        Assert.Contains("After.", text, StringComparison.Ordinal);
        Assert.Equal("todo: harbour\nsecond", comments);
    }

    [Fact]
    public void CommentsInCodeAreText()
    {
        var markdown = "Use `<!-- x -->` here.\n\n```\n<!-- y -->\n```\n";

        Assert.Equal((markdown, ""), MarkdownComments.Split(markdown));
    }

    [Fact]
    public void AnUnclosedCommentRunsToTheEndOfItsBlock()
    {
        var (text, comments) = MarkdownComments.Split("Before.\n\n<!-- open\nstill open\n");

        Assert.Contains("Before.", text, StringComparison.Ordinal);
        Assert.Equal("open\nstill open", comments);
    }

    // --- Strip: what a read at a position returns (F-VIEW-6). ---

    [Theory]
    [Trait("Scenario", "SC-MCP-48")]
    // On a line with text.
    [InlineData("Paid <!-- x --> each season.", "Paid each season.")]
    [InlineData("Paid<!-- x --> each season.", "Paid each season.")]
    [InlineData("Paid <!-- x -->each season.", "Paid each season.")]
    [InlineData("Paid each<!-- x -->, season.", "Paid each, season.")]
    [InlineData("Paid each season. <!-- x -->", "Paid each season.")]
    [InlineData("Paid each season. <!-- x -->\nAnd more.", "Paid each season.\nAnd more.")]
    [InlineData("<!-- x --> Paid each season.", "Paid each season.")]
    [InlineData("<!-- x -->     Paid each season.", "Paid each season.")]
    [InlineData("Two <!-- x --> <!-- y --> comments.", "Two comments.")]
    [InlineData("<!-- x --> <!-- y --> Two.", "Two.")]
    // On lines of their own: no stray blank lines.
    [InlineData("<!-- x -->", "")]
    [InlineData("  <!-- x -->  ", "")]
    [InlineData("\n<!-- x -->\n\n", "")]
    [InlineData("<!-- x -->\n<!-- y -->", "")]
    [InlineData("<!-- x -->\nText.", "Text.")]
    [InlineData("Text.\n<!-- x -->", "Text.")]
    [InlineData("<!-- x -->\n\nText.", "Text.")]
    [InlineData("Text.\n\n<!-- x -->", "Text.")]
    [InlineData("One.\n\n<!-- x -->\n\nTwo.", "One.\n\nTwo.")]
    [InlineData("One.\n\n<!-- x -->\n\n<!-- y -->\n\nTwo.", "One.\n\nTwo.")]
    [InlineData("One.\n\n<!-- x -->\nTwo.", "One.\n\nTwo.")]
    [InlineData("One.\n<!-- x -->\n\nTwo.", "One.\n\nTwo.")]
    [InlineData("One.\n\n\n<!-- x -->\n\n\nTwo.", "One.\n\n\n\nTwo.")] // The writer's other blank lines stay.
    // A comment is its own block, so the lines around it stay separate paragraphs.
    [InlineData("One.\n<!-- x -->\nTwo.", "One.\n\nTwo.")]
    [InlineData("One.\n<!-- x -->\n<!-- y -->\nTwo.", "One.\n\nTwo.")]
    // Across lines.
    [InlineData("One.\n\n<!--\nx\n\ny\n-->\n\nTwo.", "One.\n\nTwo.")]
    [InlineData("One <!-- x\ny --> two.", "One two.")]
    [InlineData("<!-- x\ny --> Two.", "Two.")]
    // In an HTML block.
    [InlineData("<div>\n<!-- x -->\nText\n</div>", "<div>\n\nText\n</div>")]
    [InlineData("<div>Text<!-- x --></div>", "<div>Text</div>")]
    [InlineData("<pre><!-- x --></pre>", "<pre></pre>")]
    [InlineData("<div><!-- x\n\ny --></div>", "<div></div>")]
    // In containers.
    [InlineData("> One.\n> <!-- x -->\n> Two.", "> One.\n>\n> Two.")]
    [InlineData("> One.\n>\n> <!-- x -->\n>\n> Two.", "> One.\n>\n> Two.")]
    [InlineData("> One. <!-- x -->", "> One.")]
    [InlineData("Text.\n\n> <!-- x -->\n\nMore.", "Text.\n\nMore.")]
    [InlineData("- One\n- <!-- x -->\n- Three", "- One\n- Three")]
    [InlineData("- One <!-- x -->\n- Two", "- One\n- Two")]
    [InlineData("- <!-- x -->\n  One", "-\n  One")]
    [InlineData("1. <!-- x -->\n2. Two", "2. Two")]
    // The short forms CommonMark 0.31.2 allows.
    [InlineData("One <!--> two.", "One two.")]
    [InlineData("One <!---> two.", "One two.")]
    [InlineData("One <!----> two.", "One two.")]
    // A wolfden comment anywhere a body can hold one.
    [InlineData("<!-- wolfden: until=ash:2 -->\nBody.", "Body.")]
    [InlineData("Body.\n\n<!-- wolfden: from=ash:2 -->\n\nMore.", "Body.\n\nMore.")]
    [InlineData("Body. <!-- wolfden: from=ash:2 -->", "Body.")]
    public void CommentsComeOutAndLeaveNoSign(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownComments.Strip(markdown));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-48")]
    // No comment at all.
    [InlineData("Plain text with --> an arrow.")]
    [InlineData("  Leading spaces, and\n\n\ntwo blank lines.\n")]
    // Code spans.
    [InlineData("Use `<!-- x -->` here.")]
    [InlineData("Use `` `<!-- x -->` `` here.")]
    [InlineData("Use `<!-- x\ny -->` here.")]
    // Fenced code.
    [InlineData("```\n<!-- x -->\n\n\ny\n```")]
    [InlineData("~~~ md\n<!-- x -->\n~~~\n\nAfter.")]
    [InlineData("```\n<!-- x")] // A fence nothing closes runs to the end.
    // Indented code.
    [InlineData("Text.\n\n    <!-- x -->\n\n    y")]
    [InlineData("    <!-- x -->")]
    // Code in containers.
    [InlineData("> ```\n> <!-- x -->\n> ```")]
    [InlineData("- item\n\n      <!-- x -->")]
    [InlineData("- `<!-- x -->`")]
    // Escaped, so it shows as text.
    [InlineData(@"An escaped \<!-- x --> comment.")]
    public void WhatIsntACommentStaysExactly(string markdown)
    {
        Assert.Equal(markdown, MarkdownComments.Strip(markdown));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-48")]
    [InlineData("Keep `<!-- a -->` and <!-- b --> this.", "Keep `<!-- a -->` and this.")]
    [InlineData("```\n<!-- a -->\n\n\n```\n\n<!-- b -->\n\nAfter.", "```\n<!-- a -->\n\n\n```\n\nAfter.")]
    [InlineData("Keep.\n\n    <!-- a -->\n\n<!-- b -->\nAfter.", "Keep.\n\n    <!-- a -->\n\nAfter.")]
    [InlineData(@"Two \\<!-- x --> slashes.", @"Two \\ slashes.")] // The slash is the one escaped.
    public void CodeBesideACommentStays(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownComments.Strip(markdown));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-48")]
    // On its own line, CommonMark's HTML block has no end, so all that's left goes with it.
    [InlineData("Before.\n\n<!-- she dies", "Before.")]
    [InlineData("Before.\n\n<!-- she dies\n\nAnd more.\n\nAnd more still.", "Before.")]
    [InlineData("<!-- she dies", "")]
    // Inside a paragraph, CommonMark shows it as text, and it's hidden to the end of the paragraph, not past it.
    [InlineData("Before <!-- she dies", "Before")]
    [InlineData("Before <!-- she dies\nstill here\n\nThe next paragraph.", "Before\n\nThe next paragraph.")]
    [InlineData("> Quoted <!-- she dies\n\nAfter.", "> Quoted\n\nAfter.")]
    [InlineData("- Item <!-- she dies\n- Next", "- Item\n- Next")]
    [InlineData("# Title <!-- she dies\n\nLead.", "# Title\n\nLead.")]
    [InlineData("Before `code` <!-- she `dies", "Before `code`")]
    // In an HTML block that ends at a blank line, to the end of that block.
    [InlineData("<div><!-- she dies\n</div>\n\nAfter.", "<div>\n\nAfter.")]
    public void AnUnclosedCommentDoesntLeak(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownComments.Strip(markdown));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-48")]
    // Closed later, in another paragraph: as a browser would read it, it's all comment.
    [InlineData("Before <!-- she\n\ndies --> after.", "Before after.")]
    [InlineData("Before <!-- she\n\ndies\n\nalone -->\n\nAfter.", "Before\n\nAfter.")]
    // Unbalanced: it ends at the first close, and what follows is text, as CommonMark has it.
    [InlineData("<!-- a <!-- b --> Visible.", "Visible.")]
    [InlineData("<!-- a <!-- b -->\nVisible.\n\n<!-- c -->\n\nMore.", "Visible.\n\nMore.")]
    [InlineData("Text <!-- a <!-- b --> visible.", "Text visible.")]
    // Balanced: the rest of the outer comment is what the writer meant to hide.
    [InlineData("<!-- a <!-- b --> she dies --> Visible.", "Visible.")]
    [InlineData("<!-- outer\n<!-- inner -->\nshe dies\n-->\n\nVisible.", "Visible.")]
    [InlineData("Text <!-- a <!-- b --> she dies --> visible.", "Text visible.")]
    // A comment inside a code span in a comment isn't a real opener, and in later code it's neither.
    [InlineData("<!-- a `<!--` b --> Visible.", "Visible.")]
    [InlineData("<!-- a <!-- b --> Visible.\n\n```\n-->\n```", "Visible.\n\n```\n-->\n```")]
    [InlineData("Text <!-- a <!-- b --> visible `-->`.", "Text visible `-->`.")]
    // A close with no open is only text.
    [InlineData("<!-- a --> she --> dies", "she --> dies")]
    public void NestedLookingCommentsDontLeak(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownComments.Strip(markdown));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void TextTooDeepToParseStillLosesItsComments()
    {
        var markdown = new string('>', 100_000) + " Text <!-- she dies -->";
        var stripped = MarkdownComments.Strip(markdown);

        Assert.DoesNotContain("she dies", stripped, StringComparison.Ordinal);
        Assert.Contains("Text", stripped, StringComparison.Ordinal);
    }
}

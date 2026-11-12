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
    // Closed later, in another paragraph, with nothing opening on the way: as a browser would read it, it's all comment.
    [InlineData("Before <!-- she\n\ndies --> after.", "Before after.")]
    [InlineData("Before <!-- she\n\ndies\n\nalone -->\n\nAfter.", "Before\n\nAfter.")]
    // A later comment's close isn't this one's, so a forgotten close can't take the paragraphs between.
    [InlineData("Keep. <!-- forgot\n\nCanon.\n\n<!-- real note -->\n\nAfter.", "Keep.\n\nCanon.\n\nAfter.")]
    [InlineData("Keep. <!-- forgot\n\nCanon <!-- real note --> too.", "Keep.\n\nCanon too.")]
    // A comment block runs to its first close whatever it holds, in CommonMark and on the page, so it stays hidden.
    [InlineData("<!-- old\n\nHidden.\n\n<!-- real -->\nAfter.", "After.")]
    // A blank line ends the balancing, so one stray marker can't take the paragraphs after it.
    [InlineData("<!-- old <!-- older -->\n\nCanon one.\n\nCanon two.\n\nRoute: Vesta --> Ardent.", "Canon one.\n\nCanon two.\n\nRoute: Vesta --> Ardent.")]
    [InlineData("Text <!-- old <!-- older --> kept.\n\nRoute: Vesta --> Ardent.", "Text kept.\n\nRoute: Vesta --> Ardent.")]
    [InlineData("<!-- outer\n\n<!-- inner -->\nshe dies\n-->\n\nVisible.", "Visible.")] // No blank line after the first close.
    // Inside an inner short form, the first close is still the first.
    [InlineData("x <!-- s <!--> Visible.\n\nMore.", "x Visible.\n\nMore.")]
    [InlineData("x <!-- s <!---> Visible.\n\nMore.", "x Visible.\n\nMore.")]
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

    [Theory]
    [Trait("Scenario", "SC-MCP-48")]
    [InlineData("```js <!-- she dies -->\nvar a;\n```", "```js\nvar a;\n```")]
    [InlineData("``` <!-- she dies -->\n<!-- shown -->\n```", "```\n<!-- shown -->\n```")]
    [InlineData("~~~<!-- a --><!-- b -->\ncode\n~~~", "~~~\ncode\n~~~")]
    [InlineData("~~~js <!-- she dies\n<!-- shown -->\n~~~\n\nAfter.", "~~~js\n<!-- shown -->\n~~~\n\nAfter.")] // Unclosed, it ends with the line.
    [InlineData("> ```js <!-- she dies -->\n> code\n> ```", "> ```js\n> code\n> ```")]
    public void AFencesInfoStringLosesItsCommentAndTheCodeStays(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownComments.Strip(markdown));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-48")]
    [InlineData(@"<div>\<!-- she dies --></div>", @"<div>\</div>")]
    [InlineData("<div>\n\\<!-- she dies -->\n</div>", "<div>\n\\\n</div>")]
    public void ABackslashEscapesNothingInAnHtmlBlock(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownComments.Strip(markdown));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-48")]
    [InlineData("1999.<!-- x -->", "1999.")]
    [InlineData("-<!-- x -->", "-")]
    [InlineData("+<!-- x -->\nText.", "+\nText.")]
    [InlineData("Text.\n1999. <!-- x -->\nMore.", "Text.\n1999.\nMore.")] // Only a list starting at 1 can interrupt a paragraph, so this one's text.
    [InlineData("> 1999.<!-- x -->", "> 1999.")]
    public void TextThatOnlyLooksLikeAListMarkerStays(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownComments.Strip(markdown));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-48")]
    public void TwoCodeSpansAroundACommentStayTwo()
    {
        Assert.Equal("`a` `b`", MarkdownComments.Strip("`a`<!-- x -->`b`"));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-48")]
    [InlineData("<!-- ", "\n\n")] // Each unclosed, in its own paragraph.
    [InlineData("Text <!-- ", "\n\n")]
    [InlineData("Text <!-- ", " ")] // All unclosed, in one paragraph.
    [InlineData("<!-- x -->", "\n\nText\n\n")] // Closed, as in a normal note.
    [InlineData("a <!-- <!-- b --> ", "")] // Never balanced, all on one line.
    [InlineData("a <!-- b <!-- ", "\n\n", "Closes here -->")] // Each one opening before a close far away.
    [InlineData("<!-- a <!-- b --> Text\n", "\n")]
    public void TheWorkGrowsAboutInLineWithTheNote(string repeated, string between, string end = "")
    {
        var (smallSteps, smallBytes) = Work(1_000);
        var (largeSteps, largeBytes) = Work(10_000);

        // Ten times the note costs about ten times the work, and looking things up in sorted lists a little more. Anything per comment over the whole note would cost about a hundred.
        Assert.InRange(smallSteps, 1, long.MaxValue);
        Assert.InRange((double)largeSteps / smallSteps, 1, 20);
        Assert.InRange((double)largeBytes / smallBytes, 1, 20);

        (long Steps, long Bytes) Work(int count)
        {
            var markdown = string.Concat(Enumerable.Repeat(repeated + between, count)) + end;
            var before = GC.GetAllocatedBytesForCurrentThread();
            MarkdownComments.Strip(markdown, out var steps);
            return (steps, GC.GetAllocatedBytesForCurrentThread() - before);
        }
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

    [Theory]
    [InlineData("Keep.\n\n<!-- unclosed comment\n\nStill going.", "Unclosed HTML comment '<!--'")]
    [InlineData("Keep.\n\n<!-- outer <!-- nested --> text -->", "Nested-looking HTML comment")]
    [InlineData("Route: Vesta --> Ardent", "Stray '-->'")]
    public void CheckWarnsAboutUnclosedNestedAndStrayComments(string markdown, string expectedMessageSubstring)
    {
        var source = Wolfden.Core.Text.SourceText.FromText(markdown, false);
        var diagnostics = MarkdownComments.Check(markdown, source, 0);

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, d => Assert.Equal("F-SEC-11", d.RuleId));
        Assert.Contains(diagnostics, d => d.Message.Contains(expectedMessageSubstring, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Keep `<!-- unclosed` in code span.")]
    [InlineData("```\n<!-- unclosed in code block\n```")]
    [InlineData("Keep `-->` stray in code span.")]
    [InlineData(@"Escaped \<-- not a comment.")]
    public void CodeBlocksAndSpansDoNotWarnOnComments(string markdown)
    {
        var source = Wolfden.Core.Text.SourceText.FromText(markdown, false);
        var diagnostics = MarkdownComments.Check(markdown, source, 0);

        Assert.Empty(diagnostics);
    }
}

/// <summary>The review of #344: warnings derived from what Strip pairs.</summary>
public class MarkdownCommentsReviewTests
{
    private static List<Diagnostic> Check(string markdown, int bodyStart = 0) =>
        MarkdownComments.Check(markdown, Wolfden.Core.Text.SourceText.FromText(markdown, false), bodyStart);

    [Fact]
    [Trait("Scenario", "SC-MCP-49")]
    public void TheCasesTheIssueWasFiledForWarn()
    {
        // An open in a paragraph, blank lines, then a --> far below: the read
        // hides to the end of that block, so the writer must be told.
        var markdown = "Keep. <!-- forgot\n\nTwo canon paragraphs...\n\nMore canon...\n\nVesta --> Ardent\n";

        Assert.Contains(
            Check(markdown),
            d => d.Message.Contains("Unclosed HTML comment", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Scenario", "SC-MCP-49")]
    [InlineData(@"Escaped \<!-- literal -->")] // escaped opening: its --> is text, not stray
    [InlineData("<!-->")]                      // complete comments in CommonMark 0.31.2
    [InlineData("<!--->")]
    [InlineData("## Heading <!-- note -->\n\nBody.")]
    public void CasesThatMustStayQuietDoNotWarn(string markdown)
    {
        Assert.Empty(Check(markdown));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-49")]
    public void AMarkerAfterAClosedCommentIsStray()
    {
        var diagnostics = Check("<!-- a --> b -->");

        Assert.Contains(
            diagnostics,
            d => d.Message.Contains("Stray '-->'", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-49")]
    public void CrlfBlankLinesEndTheBlockTheySeparate()
    {
        // A blank line between the opens makes the first one unclosed; reading
        // the \r as part of the line would pair it with the later --> instead.
        var markdown = "Keep. <!-- a\r\n\r\n<!-- b -->\r\n\r\nAfter\r\n";

        var diagnostics = Check(markdown);

        Assert.Contains(
            diagnostics,
            d => d.Message.Contains("Unclosed HTML comment", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            d => d.Message.Contains("Nested-looking", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-49")]
    public void ACommentCannotPairWithAMarkerInTheNextSection()
    {
        // check reads the same pieces a read strips, so an open in one section stays
        // unclosed even when a --> sits in the next (review of #344).
        var markdown = "# Alpha\n\nText <!-- open\n\n## Beta\n\nArrow --> here\n";

        var diagnostics = Check(markdown);

        var warning = Assert.Single(diagnostics, d => d.Message.Contains("Unclosed HTML comment", StringComparison.Ordinal));
        Assert.Equal(3, warning.Line);
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("Nested-looking", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "SC-MCP-49")]
    public void LineNumbersCountFromTheSourceWithAnOffsetBody()
    {
        const string source = "Title\n\nKeep. <!-- unclosed\n";
        const int bodyStart = 7;

        var diagnostics = MarkdownComments.Check(
            source[bodyStart..],
            Wolfden.Core.Text.SourceText.FromText(source, false),
            bodyStart);

        var warning = Assert.Single(diagnostics);
        Assert.Equal(3, warning.Line);
    }
}

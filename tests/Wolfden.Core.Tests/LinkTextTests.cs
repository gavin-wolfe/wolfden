using Wolfden.Core.Entities;
using Wolfden.Core.Links;

namespace Wolfden.Core.Tests;

/// <summary>Link text normalization (format.md F-LINK-4) and bookkeeping patterns (F-SEC-12).</summary>
public class LinkTextTests
{
    [Theory]
    [InlineData("Ilse Tarrow", "ilse tarrow")]
    [InlineData("  ILSE \t  tarrow\n", "ilse tarrow")]
    [InlineData("ilse-tarrow", "ilse-tarrow")]
    [InlineData("Straße", "strasse")]
    [InlineData("STRASSE", "strasse")]
    [InlineData("ΣΊΣΥΦΟΣ", "σίσυφοσ")]
    [InlineData("σίσυφος", "σίσυφοσ")]
    [InlineData("Zoë", "zoë")]
    [InlineData("Ｉｌｓｅ", "ｉｌｓｅ")]
    [InlineData("a b", "a b")]
    public void TextIsFoldedTrimmedAndCollapsed(string text, string normalized)
    {
        Assert.Equal(normalized, LinkText.Normalize(text));
    }

    [Theory]
    [InlineData("Ilse Tarrow", "ilse tarrow", true)]
    [InlineData("Ilse Tarrow", "ilse-tarrow", false)]
    [InlineData("Zoë", "ZOË", true)]
    public void MatchingIsCaseInsensitiveButKeepsHyphens(string a, string b, bool match)
    {
        Assert.Equal(match, LinkText.Matches(a, b));
    }

    [Theory]
    [InlineData("Mina's Journal", "Mina’s Journal")]
    [InlineData("Mina‘s Journal", "Minaʼs Journal")]
    [InlineData("Mina＇s Journal", "MINA'S JOURNAL")]
    public void EveryApostropheIsTheSameCharacter(string a, string b)
    {
        Assert.True(LinkText.Matches(a, b));
    }

    [Theory]
    [InlineData("Mina's Journal", "Minas Journal")]
    [InlineData("Mina's Journal", "Mina s Journal")]
    [InlineData("Mina's Journal", "Mina`s Journal")]
    [InlineData("Mina's Journal", "Minaʻs Journal")]
    public void WhatIsNotAnApostropheStaysDistinct(string a, string b)
    {
        Assert.False(LinkText.Matches(a, b));
    }

    [Fact]
    public void AnEnumsCaseFoldLeavesApostrophesAlone()
    {
        Assert.Equal("mina’s", LinkText.CaseFold("MINA’S"));
    }

    [Fact]
    public void TheLetterWithABuiltInApostropheFoldsToTheSameText()
    {
        // ŉ folds to ʼn, whose apostrophe is folded with the others.
        Assert.True(LinkText.Matches("ŉ", "'n"));
        Assert.True(LinkText.Matches("ŉ", "’N"));
    }

    [Theory]
    [InlineData("Change log", true)]
    [InlineData("Changelog 2026", true)]
    [InlineData("**Change log**", true)]
    [InlineData("v12 session notes (merged)", true)]
    [InlineData("Session Notes", true)]
    [InlineData("Revision history 2026", true)]
    [InlineData("Revision history: draft 3", false)]
    [InlineData("`Changelog`", true)]
    [InlineData("Session", false)]
    [InlineData("Change logistics", false)]
    [InlineData("Possession notes", false)]
    [InlineData("Overview", false)]
    public void BuiltInBookkeepingPatternsMatchAsTheSpecSays(string name, bool bookkeeping)
    {
        Assert.Equal(bookkeeping, new Bookkeeping().MatchesName(name));
    }

    [Theory]
    [InlineData("Scratch", true)]
    [InlineData("scratch", true)]
    [InlineData("Scratchpad", false)]
    [InlineData("Ash working notes", true)]
    [InlineData("working notes", false)]
    public void DenPatternsAddToTheBuiltInOnes(string name, bool bookkeeping)
    {
        Assert.Equal(bookkeeping, new Bookkeeping(["Scratch", "* working notes"]).MatchesName(name));
    }
}

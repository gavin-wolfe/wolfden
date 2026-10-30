using Wolfden.Index.Reading;

namespace Wolfden.Index.Tests;

/// <summary>How the link pickers' lookup (#198) matches and ranks a note.</summary>
public class EntityLookupTests
{
    private const string Id = "01M4TF5F0B7KDTSAS8D2NYT37J";

    private static readonly string[] MinaAliases = ["Mina", "Mina Murray", "Wilhelmina Murray", "Madam Mina"];

    private static LookupMatch? Mina(string query) => EntityLookup.Match(query, "Mina Harker", "mina-harker", Id, MinaAliases);

    [Theory]
    [Trait("Scenario", "SC-WEB-78")]
    [InlineData("Mina Harker", 0, "name", null)]
    [InlineData("  mina   HARKER ", 0, "name", null)]
    [InlineData("mina-harker", 1, "slug", null)]
    [InlineData(Id, 1, "id", null)]
    [InlineData("01m4tf5f0b7kdtsas8d2nyt37j", 1, "id", null)]
    [InlineData("mina", 2, "alias", "Mina")]
    [InlineData("madam mina", 2, "alias", "Madam Mina")]
    [InlineData("min har", 3, "name", null)]
    [InlineData("har", 4, "name", null)]
    [InlineData("har min", 4, "name", null)]
    [InlineData("wil", 6, "alias", "Wilhelmina Murray")]
    [InlineData("murr", 7, "alias", "Mina Murray")]
    [InlineData("mad", 6, "alias", "Madam Mina")]
    [InlineData("01M4TF5F0B", 8, "id", null)]
    public void ANoteMatchesByWhatWasTypedBestFirst(string query, int rank, string by, string? alias)
    {
        var match = Mina(query);
        Assert.NotNull(match);
        Assert.Equal(new LookupMatch(rank, by, alias), match);
    }

    [Theory]
    [Trait("Scenario", "SC-WEB-78")]
    [InlineData("arker")]
    [InlineData("mina jonathan")]
    [InlineData("01M4TF5F0")]
    [InlineData("TF5F0B7KDT")]
    [InlineData(" - ")]
    public void ANoteThatDoesntStartAWordForEachWordTypedDoesntMatch(string query) => Assert.Null(Mina(query));

    [Fact]
    [Trait("Scenario", "SC-WEB-78")]
    public void WordsCompareIgnoringCaseAndLatinAccentsAsSearchDoes()
    {
        Assert.Equal(new LookupMatch(3, "name", null), EntityLookup.Match("zoe ark", "Zoë Arkwright", "zoe-arkwright", null, []));
        Assert.Equal(new LookupMatch(3, "name", null), EntityLookup.Match("ZOË", "Zoë Arkwright", null, null, []));
        Assert.Equal(new LookupMatch(7, "alias", "the Quartermaster"), EntityLookup.Match("quarter", "Zoë Arkwright", null, null, ["the Quartermaster"]));

        // Punctuation splits words, as in search: "R. M. Renfield" has the words r, m, renfield.
        Assert.Equal(new LookupMatch(3, "name", null), EntityLookup.Match("r m ren", "R. M. Renfield", "renfield", null, ["Renfield"]));
        Assert.Equal(new LookupMatch(2, "alias", "Renfield"), EntityLookup.Match("renfield", "R. M. Renfield", null, null, ["Renfield"]));
    }

    [Fact]
    [Trait("Scenario", "SC-WEB-78")]
    public void AnExactSlugComesBeforeAnExactAliasButAfterAnExactName()
    {
        // As a link matches (F-LINK-6), a slug comes before an alias; the picker puts an exact name first.
        Assert.Equal("name", EntityLookup.Match("renfield", "Renfield", "renfield", null, ["Renfield"])!.By);
        Assert.Equal("slug", EntityLookup.Match("renfield", "R. M. Renfield", "renfield", null, ["Renfield"])!.By);
    }
}

using System.Text;

namespace Wolfden.Accounts.Tests;

/// <summary>The common-password blocklist and the context words (#354; NIST SP 800-63B rev. 4 §3.1.1.2).</summary>
public class CommonPasswordTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [Trait("Scenario", "SC-ACCT-10")]
    [InlineData("passwordpassword")]
    [InlineData("PasswordPassword")]
    [InlineData("PASSWORD PASSWORD")]
    [InlineData("Pass-Word-Pass-Word")]
    [InlineData("qwertyuiop12345")]
    [InlineData("QwertyUiop!12345")]
    [InlineData("123456789012345")]
    [InlineData("dragondragondragon")]
    [InlineData("basketball2026!!")]
    [InlineData("films+pic+galeries")] // The one entry of 15 characters or more, as it stands on the list.
    [InlineData("FILMS PIC GALERIES")]
    public void ACommonPasswordIsRefusedWhateverItsCase(string password)
    {
        Assert.Equal(PasswordRules.TooCommon, PasswordRules.Problem("wren", password));
    }

    [Theory]
    [Trait("Scenario", "SC-ACCT-10")]
    [InlineData("copper lantern harbor")]
    [InlineData("lantern otter quietly brass")]
    [InlineData("correct horse battery")]
    [InlineData("passwordbasketball")] // Two common ones together aren't one repeated.
    [InlineData("2000183746592018")] // A common year, then more random digits than it has.
    [InlineData("dragon938475610")] // More digits than word, and the digits are random: accepted.
    public void AnUncommonPassphraseIsAccepted(string password)
    {
        Assert.Null(PasswordRules.Problem("wren", password));
    }

    [Theory]
    [Trait("Scenario", "SC-ACCT-10")]
    [InlineData("wrencalder", "WrenCalderWrenCalder")]
    [InlineData("wrencalder", "wren calder 2026")]
    [InlineData("wrencalder", "wrencalder12345")]
    [InlineData("wren", "WOLFDEN WOLFDEN!")]
    [InlineData("wren", "wolfden 2026!!!!")]
    [InlineData("wren", "wrenwrenwrenwren")]
    public void TheUsernameOrWolfdenRepeatedOrWithDigitsIsRefused(string userName, string password)
    {
        Assert.Contains("can't be the username or \"wolfden\"", PasswordRules.Problem(userName, password), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-10")]
    public void TheRefusalNeverSaysWhichEntryMatched()
    {
        var problem = PasswordRules.Problem("wren", "baseballbaseball");

        Assert.Equal("that password is too common. Try a few random words in a row, like \"copper lantern harbor\".", problem);
        Assert.DoesNotContain("baseball", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Null(PasswordRules.Problem("wren", "copper lantern harbor"));
    }

    [Theory]
    [Trait("Scenario", "SC-ACCT-10")]
    [InlineData("ｐａｓｓｗｏｒｄｐａｓｓｗｏｒｄ")] // Fullwidth letters fold to plain ones (NFKC).
    [InlineData("ＰＡＳＳＷＯＲＤ　１２３４５６７")] // Fullwidth capitals, an ideographic space, fullwidth digits.
    [InlineData("password١٢٣٤٥٦٧")] // Arabic-Indic digits added.
    [InlineData("१२३४५६७८९०१२३४५")] // Devanagari digits for 123456789012345.
    public void CompatibilityFormsAndOtherDigitsCompareAsTheirPlainForms(string password)
    {
        Assert.Equal(PasswordRules.TooCommon, PasswordRules.Problem("wren", password));
    }

    [Fact]
    public void TheComparisonFormIsNeverTheHashedForm()
    {
        const string fullwidth = "ｃｏｐｐｅｒ ｌａｎｔｅｒｎ ｈａｒｂｏｒ";

        Assert.Equal("copperlanternharbor", PasswordRules.Skeleton(fullwidth));
        Assert.Equal(fullwidth, PasswordRules.Normalize(fullwidth)); // NFC keeps fullwidth; only the comparison folds it.
        Assert.Equal("password1234", PasswordRules.Skeleton("Password١٢٣٤"));
    }

    [Fact]
    public void TheSkeletonKeepsLettersMarksAndDigitsInLowercase()
    {
        Assert.Equal("password2026", PasswordRules.Skeleton("Pass Word-2026!"));
        Assert.Equal("cafédumatin", PasswordRules.Skeleton(PasswordRules.Normalize("CAFÉ du matin")));
        Assert.Equal(string.Empty, PasswordRules.Skeleton("!!! ??? ..."));
    }

    [Fact]
    public void TheBuiltInListLoadsAndHoldsTheTenThousand()
    {
        // 9,992 lines; a few symbol-only entries collapse together or are too short to keep.
        Assert.InRange(CommonPasswords.Default.Count, 9_900, 10_000);
        Assert.True(CommonPasswords.Default.Contains("password"));
        Assert.True(CommonPasswords.Default.Contains("filmspicgaleries"));
        Assert.False(CommonPasswords.Default.Contains("amp")); // From "&amp", shorter than ShortestEntry.
    }

    [Fact]
    public async Task TheListIsReadOnceAndOnlyWhenFirstNeeded()
    {
        var opened = 0;
        var list = new CommonPasswords(() =>
        {
            Interlocked.Increment(ref opened);
            return new MemoryStream(Encoding.UTF8.GetBytes("password\nDragon\n"));
        });

        Assert.Equal(0, list.Loads);
        Assert.Equal(0, opened);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => Assert.True(list.Contains("dragon")), Token)));
        Assert.False(list.Contains("copper"));

        Assert.Equal(1, list.Loads);
        Assert.Equal(1, opened);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    [Trait("Scenario", "SC-ACCT-10")]
    public async Task TheStoreRefusesACommonPasswordOnAddAndReset()
    {
        using var temp = new TempHome();
        await using var store = await AccountStore.OpenAsync(temp.Folder, Token);

        var added = await store.AddAsync("wren", "superman12345678", AccountRole.Owner);
        Assert.Equal("bad_password", added.Code);
        Assert.Equal(PasswordRules.TooCommon, added.Message);
        Assert.Empty(await store.ListAsync(Token));

        Assert.True((await store.AddAsync("wren", "copper lantern harbor", AccountRole.Owner)).Ok);
        var reset = await store.ResetPasswordAsync("wren", "Wolfden Wolfden 1");
        Assert.Equal("bad_password", reset.Code);
    }
}

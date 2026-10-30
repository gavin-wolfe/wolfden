using System.Text;
using Wolfden.Core.Documents;
using Wolfden.Core.Yaml;

namespace Wolfden.Core.Tests;

/// <summary>
/// format.md F-RT-1, F-RT-2, §12 (#197): moving a list item in place moves
/// its own lines, or its own text in a flow list, with the comments that
/// belong to it, and changes nothing else. Replacing an item keeps the
/// comment on it. Each test spells out the whole expected frontmatter.
/// </summary>
[Trait("Scenario", "SC-RT-17")]
public class ListItemMoveTests
{
    private static MarkdownFile Md(string frontmatter) => MarkdownFile.Parse(TestFiles.Utf8("---\n" + frontmatter + "---\nBody.\n"));

    private static string Fm(MarkdownFile file) => file.FrontmatterText["---\n".Length..^"---\n".Length];

    private static string Moved(string frontmatter, int from, int to, params string[] path) =>
        Fm(Md(frontmatter).WithListItemMoved(path.Length == 0 ? ["pov"] : path, from, to));

    private const string Block = """
        pov:
          - "[[mina-harker]]"
          - "[[jonathan-harker]]"
          - "[[john-seward]]"
        tense: past

        """;

    [Theory]
    [InlineData(0, 2, "jonathan-harker", "john-seward", "mina-harker")]
    [InlineData(2, 0, "john-seward", "mina-harker", "jonathan-harker")]
    [InlineData(0, 1, "jonathan-harker", "mina-harker", "john-seward")]
    [InlineData(1, 2, "mina-harker", "john-seward", "jonathan-harker")]
    [InlineData(2, 1, "mina-harker", "john-seward", "jonathan-harker")]
    public void ABlockItemMovesToAnyPlace(int from, int to, string a, string b, string c)
    {
        Assert.Equal($"pov:\n  - \"[[{a}]]\"\n  - \"[[{b}]]\"\n  - \"[[{c}]]\"\ntense: past\n", Moved(Block, from, to));
    }

    [Fact]
    public void MovingAnItemToWhereItIsChangesNothing()
    {
        var file = Md(Block);

        Assert.Same(file, file.WithListItemMoved(["pov"], 1, 1));
    }

    [Fact]
    public void AnItemsCommentOnItsLineMovesWithIt()
    {
        const string before = """
            pov:
              - "[[mina-harker]]"  # main
              - "[[jonathan-harker]]"
              - "[[john-seward]]" # diary

            """;

        Assert.Equal(
            """
            pov:
              - "[[jonathan-harker]]"
              - "[[john-seward]]" # diary
              - "[[mina-harker]]"  # main

            """,
            Moved(before, 0, 2));
        Assert.Equal(
            """
            pov:
              - "[[john-seward]]" # diary
              - "[[mina-harker]]"  # main
              - "[[jonathan-harker]]"

            """,
            Moved(before, 2, 0));
    }

    [Fact]
    public void CommentLinesRightAboveAnItemMoveWithIt()
    {
        const string before = """
            pov:
              # the journal chapters
              - "[[mina-harker]]"
              # letters
              # and telegrams
              - "[[jonathan-harker]]"
              - "[[john-seward]]"

            """;

        Assert.Equal(
            """
            pov:
              # letters
              # and telegrams
              - "[[jonathan-harker]]"
              - "[[john-seward]]"
              # the journal chapters
              - "[[mina-harker]]"

            """,
            Moved(before, 0, 2));
        Assert.Equal(
            """
            pov:
              # letters
              # and telegrams
              - "[[jonathan-harker]]"
              # the journal chapters
              - "[[mina-harker]]"
              - "[[john-seward]]"

            """,
            Moved(before, 1, 0));
    }

    [Fact]
    public void CommentLinesIndentedUnderAnItemMoveWithIt()
    {
        const string before = """
            pov:
              - "[[mina-harker]]"
                # she keeps the typescript
              - "[[jonathan-harker]]"
              - "[[john-seward]]"
            # about tense
            tense: past

            """;

        Assert.Equal(
            """
            pov:
              - "[[jonathan-harker]]"
              - "[[john-seward]]"
              - "[[mina-harker]]"
                # she keeps the typescript
            # about tense
            tense: past

            """,
            Moved(before, 0, 2));
    }

    [Fact]
    public void ACommentAfterTheLastItemAtItsDashStaysAtTheEnd()
    {
        // A commented-out item after the last one isn't the last item's: it
        // stays where it is, at the end of the list.
        const string before = """
            pov:
              - "[[mina-harker]]"
              - "[[jonathan-harker]]"
              # - "[[lucy-westenra]]"
            tense: past

            """;

        Assert.Equal(
            """
            pov:
              - "[[jonathan-harker]]"
              - "[[mina-harker]]"
              # - "[[lucy-westenra]]"
            tense: past

            """,
            Moved(before, 1, 0));
    }

    [Fact]
    public void BlankLinesAndCommentsLeftOfTheDashStayWhereTheyAre()
    {
        const string before = """
            pov:
              - "[[mina-harker]]"

              - "[[jonathan-harker]]"
            # left of the dash
              - "[[john-seward]]"

            """;

        Assert.Equal(
            """
            pov:
              - "[[jonathan-harker]]"

              - "[[john-seward]]"
            # left of the dash
              - "[[mina-harker]]"

            """,
            Moved(before, 0, 2));
    }

    [Fact]
    public void AMultiLineItemMovesWhole()
    {
        const string before = """
            role:
              - value: clerk  # at first
                from: "dracula:1"
              - value: solicitor
                # qualified
                from: "dracula:5"
                pin: true
            name: Jonathan

            """;

        Assert.Equal(
            """
            role:
              - value: solicitor
                # qualified
                from: "dracula:5"
                pin: true
              - value: clerk  # at first
                from: "dracula:1"
            name: Jonathan

            """,
            Moved(before, 1, 0, "role"));
    }

    [Fact]
    public void ANestedListWithOddIndentationMovesByItsOwnLines()
    {
        const string before = """
            chapters:
            - key: "1"
              fields:
                  pov:
                  -   "[[mina-harker]]"   # main
                  -   '[[jonathan-harker]]'
            - key: "2"

            """;

        Assert.Equal(
            """
            chapters:
            - key: "1"
              fields:
                  pov:
                  -   '[[jonathan-harker]]'
                  -   "[[mina-harker]]"   # main
            - key: "2"

            """,
            Moved(before, 0, 1, "chapters", "0", "fields", "pov"));
    }

    [Fact]
    public void CrlfLinesStayCrlf()
    {
        var before = Block.Replace("\n", "\r\n", StringComparison.Ordinal);
        var file = MarkdownFile.Parse(TestFiles.Utf8("---\r\n" + before + "---\r\nBody.\r\n"));

        var moved = file.WithListItemMoved(["pov"], 2, 0);

        Assert.Equal(
            "---\r\npov:\r\n  - \"[[john-seward]]\"\r\n  - \"[[mina-harker]]\"\r\n  - \"[[jonathan-harker]]\"\r\ntense: past\r\n---\r\nBody.\r\n",
            Encoding.UTF8.GetString(moved.ToBytes()));
    }

    [Theory]
    [InlineData("pov: [a, b, c]\n", 0, 2, "pov: [b, c, a]\n")]
    [InlineData("pov: [a, b, c]\n", 2, 0, "pov: [c, a, b]\n")]
    [InlineData("pov: [ a,b , \"c\" ]  # three\n", 2, 1, "pov: [ a,\"c\" , b ]  # three\n")]
    [InlineData("pov: ['[[mina-harker]]', \"[[john-seward]]\"]\n", 1, 0, "pov: [\"[[john-seward]]\", '[[mina-harker]]']\n")]
    [InlineData("pov: [a, b,]\n", 0, 1, "pov: [b, a,]\n")]
    [InlineData("pov: [a, { value: b, from: \"dracula:3\" }]\n", 1, 0, "pov: [{ value: b, from: \"dracula:3\" }, a]\n")]
    public void AFlowItemMovesAndTheSeparatorsStay(string before, int from, int to, string after)
    {
        Assert.Equal(after, Moved(before, from, to));
    }

    [Fact]
    public void AFlowItemOnALineOfItsOwnTakesItsCommentAndLeavesTheCommas()
    {
        const string before = """
            pov: [
              "[[mina-harker]]",  # main
              "[[jonathan-harker]]",
              "[[john-seward]]"  # diary
            ]

            """;

        Assert.Equal(
            """
            pov: [
              "[[john-seward]]",  # diary
              "[[mina-harker]]",  # main
              "[[jonathan-harker]]"
            ]

            """,
            Moved(before, 2, 0));
        Assert.Equal(
            """
            pov: [
              "[[jonathan-harker]]",
              "[[john-seward]]",  # diary
              "[[mina-harker]]"  # main
            ]

            """,
            Moved(before, 0, 2));
    }

    [Theory]
    [InlineData("pov: [\n  # first\n  a,\n  b\n]\n")]
    [InlineData("pov: [a, # first\n  b]\n")]
    public void AFlowMoveIsRefusedWhenACommentIsntOnAnItemsLine(string before)
    {
        var file = Md(before);

        Assert.Throws<NotSupportedException>(() => file.WithListItemMoved(["pov"], 1, 0));
    }

    [Fact]
    public void AFlowListWithLeadingCommasIsRefused()
    {
        Assert.Throws<NotSupportedException>(() => Md("pov: [ a\n  , b ]\n").WithListItemMoved(["pov"], 1, 0));
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(0, 3)]
    [InlineData(-1, 0)]
    public void AnItemNumberOutsideTheListIsRefused(int from, int to)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Md(Block).WithListItemMoved(["pov"], from, to));
    }

    [Theory]
    [InlineData("tense")]
    [InlineData("missing")]
    public void MovingInsideSomethingThatIsntAListIsRefused(string key)
    {
        Assert.Throws<InvalidOperationException>(() => Md(Block).WithListItemMoved([key], 0, 1));
    }

    [Fact]
    public void ReplacingAnItemKeepsTheCommentsOnAndAboveIt()
    {
        const string before = """
            pov:
              # the narrator
              - "[[mina-harker]]"  # main
              - "[[jonathan-harker]]"

            """;

        var edited = Md(before).WithFrontmatterValue(["pov", "0"], YamlScalarValue.String("[[lucy-westenra]]"));

        Assert.Equal(
            """
            pov:
              # the narrator
              - "[[lucy-westenra]]"  # main
              - "[[jonathan-harker]]"

            """,
            Fm(edited));
        Assert.Equal(
            "pov: [a, \"[[lucy-westenra]]\"]  # two\n",
            Fm(Md("pov: [a, b]  # two\n").WithFrontmatterValue(["pov", "1"], YamlScalarValue.String("[[lucy-westenra]]"))));
    }

    [Fact]
    public void EveryMoveOfAListWithCommentsKeepsEveryLine()
    {
        // Every from and to: the lines are the same lines, in another order,
        // and moving back gives the bytes it started with (F-RT-3).
        const string before = """
            pov:
              # heading a
              - a  # on a
              - b
                # under b
              # heading c
              - c

              - d # on d
            tense: past

            """;
        var file = Md(before);
        var lines = before.Split('\n').Order(StringComparer.Ordinal).ToList();
        for (var from = 0; from < 4; from++)
        {
            for (var to = 0; to < 4; to++)
            {
                var moved = file.WithListItemMoved(["pov"], from, to);
                Assert.Equal(lines, Fm(moved).Split('\n').Order(StringComparer.Ordinal).ToList());
                Assert.Equal(before, Fm(moved.WithListItemMoved(["pov"], to, from)));
            }
        }
    }
}

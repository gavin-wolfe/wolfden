using System.Globalization;
using System.Text;
using Wolfden.Core.Documents;
using Wolfden.Core.Yaml;

namespace Wolfden.Core.Editing;

/// <summary>
/// Makes a new series' or book's file (format.md §6.3, §6.4; #213): an id,
/// the name, and for a book its chapters, keys <c>"1"</c> to <c>"N"</c>
/// numbered 1 to N, written as format.md writes them
/// (<c>- { key: "1", number: 1 }</c>). It never writes: the caller checks
/// the file (<see cref="DenWriteCheck"/>) and saves it.
/// </summary>
public static class NewContainer
{
    /// <summary>The new file.</summary>
    /// <param name="id">Its id (F-ID-1).</param>
    /// <param name="name">Its name, one line (<see cref="NewEntity.NameProblem"/>).</param>
    /// <param name="chapters">How many chapters a book starts with; 0 for none, and for a series.</param>
    /// <exception cref="ArgumentException">The id or name isn't valid, or <paramref name="chapters"/> is negative.</exception>
    /// <exception cref="EditVerificationException">The file didn't read back as built (a bug).</exception>
    public static MarkdownFile File(string id, string name, int chapters = 0)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(chapters);
        if (!Structure.Keys.IsId(id))
        {
            throw new ArgumentException($"'{id}' isn't a valid id.", nameof(id));
        }

        if (NewEntity.NameProblem(name) is { } problem)
        {
            throw new ArgumentException($"That can't be a name: {problem}.", nameof(name));
        }

        var sb = new StringBuilder("---\n");
        sb.Append("id: ").Append(id).Append('\n');
        sb.Append("name: ").Append(YamlScalarValue.String(name).Render(null)).Append('\n');
        if (chapters > 0)
        {
            sb.Append("chapters:\n");
            for (var i = 1; i <= chapters; i++)
            {
                var n = i.ToString(CultureInfo.InvariantCulture);
                sb.Append("  - { key: \"").Append(n).Append("\", number: ").Append(n).Append(" }\n");
            }
        }

        sb.Append("---\n");
        var file = MarkdownFile.Parse(Encoding.UTF8.GetBytes(sb.ToString()), requireFrontmatter: true);
        var fm = file.Frontmatter;
        var ok = fm is not null
            && YamlScalarValue.String(id).MatchesAsWritten(fm["id"])
            && YamlScalarValue.String(name).MatchesAsWritten(fm["name"])
            && (chapters == 0 ? fm["chapters"] is null : fm["chapters"] is YamlSequence { Items.Count: var count } && count == chapters);
        if (!ok)
        {
            throw new EditVerificationException("The new file didn't read back as built, so it wasn't made.");
        }

        return file;
    }
}

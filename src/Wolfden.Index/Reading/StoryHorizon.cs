using Wolfden.Core.Links;

namespace Wolfden.Index.Reading;

/// <summary>
/// How far a reader at one story position has reached, and which fields hold
/// positions, so a read can tell which of those values point ahead (format.md
/// F-VIEW-5). It compares by the index's ordinals, as canon views do (F-ORD-2,
/// F-ORD-3), so a bare book is the same point as its first chapter (F-POS-7).
/// It also holds the names readers know there (F-VIEW-7), and how each link
/// written by a name they learn later reads there instead.
/// </summary>
public sealed class StoryHorizon
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoNames = new Dictionary<string, IReadOnlyList<string>>();
    private static readonly IReadOnlyDictionary<(string Path, string Text), string> NoLinks = new Dictionary<(string, string), string>();

    private readonly IReadOnlySet<(string Type, string Field)> _positionFields;
    private readonly IReadOnlyDictionary<string, long> _points;
    private readonly long _at;

    internal StoryHorizon(IReadOnlySet<(string Type, string Field)> positionFields, IReadOnlyDictionary<string, long> points, long at)
    {
        _positionFields = positionFields;
        _points = points;
        _at = at;
    }

    /// <summary>The point, as the index's ordinal.</summary>
    public long Point => _at;

    /// <summary>For each note's path, the other names readers know at this point, in order: its plain aliases and the <c>known_as</c> names they've reached.</summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> Names { get; init; } = NoNames;

    /// <summary>
    /// For each link written by a name readers learn after this point, by the path of the
    /// note it's in and the link as written: the link as written by the note's name instead.
    /// </summary>
    internal IReadOnlyDictionary<(string Path, string Text), string> LaterNameLinks { get; init; } = NoLinks;

    /// <summary>True if <paramref name="type"/> defines <paramref name="field"/> as <c>position</c> (types.md F-FT-10).</summary>
    public bool IsPositionField(string type, string field) => _positionFields.Contains((type, field));

    /// <summary>
    /// True if <paramref name="position"/> resolves in the universe (F-POS-4)
    /// and is at or before this point. Text that isn't a position, or a
    /// position the universe doesn't have, has no place to compare, so it's false.
    /// </summary>
    public bool HasReached(string position) => _points.TryGetValue(position, out var point) && point <= _at;

    /// <summary>The other names readers know the note at <paramref name="path"/> by at this point (F-VIEW-7), in order.</summary>
    public IReadOnlyList<string> NamesOf(string path) => Names.TryGetValue(path, out var names) ? names : [];

    /// <summary>
    /// The link <paramref name="linkText"/>, written in the note at <paramref name="path"/>,
    /// as it reads at this point: by the note's name if it's written by one readers learn
    /// later (F-VIEW-7), or null if it reads as written.
    /// </summary>
    public string? LinkAsKnown(string path, string linkText) => LaterNameLinks.GetValueOrDefault((path, linkText));

    /// <summary>
    /// <paramref name="text"/>, from the note at <paramref name="path"/>, with each link
    /// written by a name readers learn later written by the note's name (F-VIEW-7).
    /// </summary>
    public string LinksAsKnown(string path, string text, bool markdown) =>
        LaterNameLinks.Count == 0 ? text : LinkSyntax.WithLinksRewritten(text, markdown, link => LinkAsKnown(path, link));
}

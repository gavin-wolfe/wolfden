namespace Wolfden.Index.Reading;

/// <summary>
/// How far a reader at one story position has reached, and which fields hold
/// positions, so a read can tell which of those values point ahead (format.md
/// F-VIEW-5). It compares by the index's ordinals, as canon views do (F-ORD-2,
/// F-ORD-3), so a bare book is the same point as its first chapter (F-POS-7).
/// </summary>
public sealed class StoryHorizon
{
    private readonly IReadOnlySet<(string Type, string Field)> _positionFields;
    private readonly IReadOnlyDictionary<string, long> _points;
    private readonly long _at;

    internal StoryHorizon(IReadOnlySet<(string Type, string Field)> positionFields, IReadOnlyDictionary<string, long> points, long at)
    {
        _positionFields = positionFields;
        _points = points;
        _at = at;
    }

    /// <summary>True if <paramref name="type"/> defines <paramref name="field"/> as <c>position</c> (types.md F-FT-10).</summary>
    public bool IsPositionField(string type, string field) => _positionFields.Contains((type, field));

    /// <summary>
    /// True if <paramref name="position"/> resolves in the universe (F-POS-4)
    /// and is at or before this point. Text that isn't a position, or a
    /// position the universe doesn't have, has no place to compare, so it's false.
    /// </summary>
    public bool HasReached(string position) => _points.TryGetValue(position, out var point) && point <= _at;
}

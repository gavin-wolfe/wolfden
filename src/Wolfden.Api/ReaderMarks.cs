using System.Text.Json;
using Wolfden.Index;

namespace Wolfden.Api;

/// <summary>
/// What a reader at one story position doesn't know, for the writer (#300,
/// design.md §9): the marks <c>marksAt</c> adds to a whole note or a search,
/// which show everything and say what's ahead of the position or no longer
/// true there.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the web API on purpose. An AI assistant's reads stay strict
/// (format.md F-VIEW-1, F-VIEW-4, F-VIEW-5), and <c>Wolfden.Mcp</c> doesn't
/// reference this project, so it can't serve a mark by accident
/// (<c>McpStaysStrictTests</c>).
/// </para>
/// <para>
/// A fact is compared by the index's effective range, the one canon views ask
/// about (<see cref="IndexQueries"/>), so a mark can't disagree with the
/// Reader's view: a section that takes its start from a thread's
/// <c>paid_off</c>, or an entry that hands over to the next, starts or ends
/// where the view says, written or not. The words a page shows are the page's.
/// </para>
/// <para>
/// A later <c>until</c> on a fact in effect has no mark here. The whole read
/// gives the <c>until</c>, and the web's <c>isAheadOf</c> tags it from the
/// position (#288), so that stays the one rule for it.
/// </para>
/// </remarks>
internal sealed class ReaderMarks
{
    private readonly IndexDbContext _db;
    private readonly Dictionary<int, ContainerRecord> _containers;
    private readonly HashSet<(string Type, string Field)> _positionFields;
    private readonly IReadOnlyDictionary<string, long> _points;
    private readonly Dictionary<long, string> _names;
    private readonly long _point;

    private ReaderMarks(DenIndex index, ContainerRecord universe, string position, long point)
    {
        _db = index.Context;
        _containers = _db.Containers.Where(c => c.UniverseId == universe.Id).ToList().ToDictionary(c => c.Id);
        _positionFields = _db.PositionFields.Select(p => new { p.TypeKey, p.Field }).AsEnumerable().Select(p => (p.TypeKey, p.Field)).ToHashSet();
        _points = index.PointsOf(universe.Key);

        // A chapter names a point rather than its book's bare key, which is the same point (F-POS-7).
        _names = _points
            .OrderBy(p => p.Key.Contains(':', StringComparison.Ordinal))
            .GroupBy(p => p.Value)
            .ToDictionary(g => g.Key, g => g.Last().Key);
        _point = point;
        Position = position;
        Universe = universe.Key;
    }

    /// <summary>The position, as given.</summary>
    public string Position { get; }

    /// <summary>The key of the universe the position is in.</summary>
    public string Universe { get; }

    /// <summary>
    /// The marks at <paramref name="position"/>. Its universe is chosen as an
    /// <c>asOf</c> read chooses it, so the position's book settles it. A
    /// position that can't be read is refused as an <c>asOf</c> is
    /// (<c>bad_position</c>), and every universe (<c>*</c>) doesn't go with one.
    /// </summary>
    public static ReaderMarks For(DenReader reader, DenIndex index, string? universe, string position)
    {
        var chosen = reader.ChooseUniverse(index.Context, universe, position, allowAll: true)!;
        long point;
        try
        {
            point = index.PointOf(chosen.Key, position);
        }
        catch (CanonViewException)
        {
            // The view says what's wrong in the words every read at a position uses.
            reader.CanonView(index, chosen.Key, position, null);
            throw;
        }

        return new ReaderMarks(index, chosen, position, point);
    }

    /// <summary>The marks of one note.</summary>
    public NoteMarks Of(EntityRecord note) => new(this, note);

    /// <summary><paramref name="page"/> with each hit's marks.</summary>
    public SearchPageResult Mark(SearchPageResult page)
    {
        var paths = page.Hits.Select(h => h.Entity.Path).Distinct().ToList();
        var notes = _db.Entities.Where(e => paths.Contains(e.Path)).ToList().ToDictionary(e => e.Path, Of, StringComparer.Ordinal);
        return page with
        {
            MarksAt = Position,
            Hits = page.Hits.Select(h => notes.TryGetValue(h.Entity.Path, out var note) ? note.Mark(h) : h).ToList(),
        };
    }

    /// <summary>The position to name for <paramref name="point"/>: as the writer wrote it if that's where it falls, otherwise the position that's there.</summary>
    private string? Named(string? written, long point) =>
        written is not null && _points.TryGetValue(written, out var at) && at == point ? written : _names.GetValueOrDefault(point);

    /// <summary>The marks of the facts of one note.</summary>
    internal sealed class NoteMarks
    {
        private readonly ReaderMarks _marks;
        private readonly EntityRecord _note;
        private readonly bool _placed;
        private readonly Dictionary<int, SectionRecord> _sections;
        private readonly List<FieldValueRecord> _values;

        public NoteMarks(ReaderMarks marks, EntityRecord note)
        {
            _marks = marks;
            _note = note;

            // An idea, or a note whose from doesn't resolve, isn't ahead of readers or behind them: readers never meet it.
            _placed = IsInCanon(note.Status) && note.FromOrdinal is not null;
            Note = _placed && note.FromOrdinal > marks._point
                ? new ReaderMark(ReaderMark.Ahead, EntityPages.Entry(note, marks._containers).Position)
                : null;
            _sections = marks._db.Sections.Where(s => s.EntityId == note.Id).ToList().ToDictionary(s => s.Line);
            _values = marks._db.FieldValues.Where(v => v.EntityId == note.Id).ToList();
        }

        /// <summary>The note's own mark, when readers meet it after the position.</summary>
        public ReaderMark? Note { get; }

        /// <summary>The mark of a section, by its heading's line.</summary>
        public ReaderMark? Section(int line) =>
            _sections.GetValueOrDefault(line) is { } section && Takes(section)
                ? Fact(section.FromOrdinal!.Value, section.UntilOrdinal!.Value, section.FromText, section.UntilText)
                : null;

        /// <summary>The mark of a field's entry, or of its plain value.</summary>
        public ReaderMark? Field(string field, int? entryIndex) =>
            Row(field, entryIndex) is { } row ? Fact(row) : null;

        /// <summary>
        /// The values of a position field later than the position (format.md
        /// F-VIEW-5), as written, or null if there are none. A value that can't
        /// be compared isn't one, since nothing says it's ahead.
        /// </summary>
        public IReadOnlyList<string>? AheadValues(string field, int? entryIndex, JsonElement value) =>
            Row(field, entryIndex) is { } row ? AheadValues(row, value) : null;

        /// <summary><paramref name="hit"/> with what readers at the position know of the text it found.</summary>
        public SearchPageHit Mark(SearchPageHit hit)
        {
            if (hit.Field is not null)
            {
                // Entries written on one line can't be told apart by a hit's line, so they're marked only if they agree.
                var rows = _values.Where(v => v.Field == hit.Field && v.Line == hit.Line).ToList();
                var marks = rows.Select(v => Takes(v) ? Fact(v) ?? Note : null).Distinct().ToList();
                var ahead = rows.SelectMany(AheadValues).Distinct(StringComparer.Ordinal).ToList();
                return hit with { ReaderMark = marks.Count == 1 ? marks[0] : null, AheadValues = ahead.Count > 0 ? ahead : null };
            }

            if (hit.Section is not null && hit.Line is { } line)
            {
                return hit with { ReaderMark = _sections.GetValueOrDefault(line) is { } section && Takes(section) ? Section(line) ?? Note : null };
            }

            // The name, an alias, or the lead: they hold wherever the note does.
            return hit with { ReaderMark = Note };
        }

        private FieldValueRecord? Row(string field, int? entryIndex) => _values.FirstOrDefault(v => v.Field == field && v.EntryIndex == entryIndex);

        private IEnumerable<string> AheadValues(FieldValueRecord row)
        {
            using var value = JsonDocument.Parse(row.ValueJson);
            return AheadValues(row, value.RootElement) ?? [];
        }

        private IReadOnlyList<string>? AheadValues(FieldValueRecord row, JsonElement value)
        {
            if (!Takes(row) || !_marks._positionFields.Contains((_note.Type, row.Field)))
            {
                return null;
            }

            var written = value.ValueKind switch
            {
                JsonValueKind.String => [value],
                JsonValueKind.Array => value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).ToList(),
                _ => [],
            };
            var ahead = written.Select(v => v.GetString()!).Where(v => _marks._points.TryGetValue(v, out var point) && point > _marks._point).ToList();
            return ahead.Count > 0 ? ahead : null;
        }

        private ReaderMark? Fact(FieldValueRecord value) =>
            Takes(value) ? Fact(value.FromOrdinal!.Value, value.UntilOrdinal!.Value, value.FromText, value.UntilText) : null;

        /// <summary>
        /// A fact past its end, or one readers reach after the position. In a
        /// note readers meet later, a fact that starts no later than the note
        /// has nothing to say that the note's own mark doesn't. A range that
        /// isn't one (a warning, F-TMP-3, F-SEC-8) is never in effect, so
        /// no position changes anything about it.
        /// </summary>
        private ReaderMark? Fact(long from, long until, string? fromText, string? untilText)
        {
            if (from >= until)
            {
                return null;
            }

            if (until <= _marks._point)
            {
                return new ReaderMark(ReaderMark.Ended, _marks.Named(untilText, until));
            }

            return from > _marks._point && !(Note is not null && from <= _note.FromOrdinal)
                ? new ReaderMark(ReaderMark.Ahead, _marks.Named(fromText, from))
                : null;
        }

        /// <summary>
        /// Whether a section can have a reader mark. An idea, a bookkeeping
        /// section, or one an error leaves out keeps its own badge instead, and one
        /// whose position doesn't resolve has no place in the story to compare.
        /// </summary>
        private bool Takes(SectionRecord s) =>
            _placed && IsInCanon(s.Status) && !s.IsBookkeeping && s.LeftOutJson is null && s.FromOrdinal is not null && s.UntilOrdinal is not null;

        /// <summary>Whether a field's value or entry can have a reader mark: one that isn't live (F-TMP-4) reaches a reader at no position.</summary>
        private bool Takes(FieldValueRecord v) => _placed && v.IsLive && v.FromOrdinal is not null && v.UntilOrdinal is not null;

        private static bool IsInCanon(string status) => status is "draft" or "canon";
    }
}

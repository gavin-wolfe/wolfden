using System.Text.Json;
using Wolfden.Index;

namespace Wolfden.Api;

/// <summary>
/// What a reader at one story position doesn't know, for the writer (#300,
/// design.md §9): the marks <c>marksAt</c> adds to a whole note or a search,
/// which show everything and say what readers there don't see, and why, and to
/// a list of notes, which has each note's own mark.
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
    private readonly int _universeId;

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
        _universeId = universe.Id;
        Position = position;
        Universe = universe.Key;
    }

    /// <summary>The position, as given.</summary>
    public string Position { get; }

    /// <summary>The key of the universe the position is in.</summary>
    public string Universe { get; }

    /// <summary>The story point of the position.</summary>
    public long Point => _point;

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

    /// <summary>
    /// The mark of a whole note, when readers meet it after the position, or no
    /// position places it. An idea isn't ahead of readers or behind them: its
    /// status says readers don't meet it.
    /// </summary>
    public ReaderMark? OfNote(EntityRecord note) =>
        !IsInCanon(note.Status) ? null
            : note.FromOrdinal is null ? new ReaderMark(ReaderMark.Unplaced, note.FromText)
            : note.FromOrdinal > _point ? new ReaderMark(ReaderMark.Ahead, EntityPages.Entry(note, _containers).Position)
            : null;

    /// <summary>
    /// A list as the Author's view shows it: <paramref name="all"/>, with the notes
    /// in the view at the position first and then the rest, each part in the order
    /// it came in, and each note's own mark. So a limit cuts what readers haven't
    /// met before anything they have.
    /// </summary>
    /// <remarks>
    /// Only the note's own mark, so this reads no note's facts, however long the list.
    /// Whether a note is met comes from the view itself rather than from having no
    /// mark, so an idea, which has none, isn't taken for one readers know.
    /// </remarks>
    public MarkedEntityListResult Mark(IReadOnlyList<EntitySummary> all, IReadOnlySet<string> met, int limit)
    {
        var notes = _db.Entities.Where(e => e.UniverseId == _universeId).ToList().ToDictionary(e => e.Path, StringComparer.Ordinal);
        var marked = all
            .Select(e => new MarkedEntity(e.Key, e.Id, e.Name, e.Type, e.Status, e.Universe, e.ScopeKind, e.ScopeKey, e.Path, e.From, met.Contains(e.Path), OfNote(notes[e.Path])))
            .OrderByDescending(e => e.Met)
            .ToList();
        var shown = marked.Take(limit).ToList();
        return new MarkedEntityListResult(ReadEndpoints.ResultVersion, shown.Count, marked.Count, marked.Count > limit, shown, Position);
    }

    /// <summary>
    /// The mark of a link in a chapter's fields or an outline: where it's written,
    /// as a view at a position asks. A link with no position has no mark: a
    /// container's own text, an outline's notes, and a chapter value or default
    /// that isn't held aren't in story order at all (format.md F-LINK-13), so
    /// there's nothing to fix or wait for. Bookkeeping or out of canon, it keeps
    /// its own badge instead.
    /// </summary>
    public ReaderMark? OfContainerLink(LinkRecord link)
    {
        if (link.IsBookkeeping || link.Status is not (null or "draft" or "canon"))
        {
            return null;
        }

        return link.PositionOrdinal is { } at && at > _point ? new ReaderMark(ReaderMark.Ahead, _names.GetValueOrDefault(at)) : null;
    }

    /// <summary>
    /// <paramref name="page"/> with each hit's marks, what readers can find first and
    /// then the rest, each part in the order it came in, cut to <paramref name="limit"/>.
    /// It reads the facts of every note with a hit at once, however many that is.
    /// </summary>
    public SearchPageResult Mark(SearchPageResult page, int limit)
    {
        var paths = page.Hits.SelectMany(h => (h.LinkedNotes ?? []).Prepend(h.Entity.Path)).Distinct().ToList();
        var records = _db.Entities.Where(e => paths.Contains(e.Path)).ToList().ToDictionary(e => e.Path, StringComparer.Ordinal);
        var ids = page.Hits.Select(h => records.GetValueOrDefault(h.Entity.Path)?.Id).OfType<int>().Distinct().ToList();
        var sections = _db.Sections.Where(s => ids.Contains(s.EntityId)).ToList().ToLookup(s => s.EntityId);
        var values = _db.FieldValues.Where(v => ids.Contains(v.EntityId)).ToList().ToLookup(v => v.EntityId);
        var notes = new Dictionary<string, NoteMarks>(StringComparer.Ordinal);
        NoteMarks NoteOf(EntityRecord record)
        {
            if (!notes.TryGetValue(record.Path, out var note))
            {
                note = new NoteMarks(this, record, sections[record.Id], values[record.Id]);
                notes[record.Path] = note;
            }

            return note;
        }

        var marked = page.Hits
            .Select(h => records.TryGetValue(h.Entity.Path, out var record) ? NoteOf(record).Mark(h, page.Query, records) : h with { Met = false })
            .ToList();
        var shown = marked.OrderByDescending(h => h.Met == true).Take(limit).ToList();
        return page with
        {
            MarksAt = Position,
            Count = shown.Count,
            Truncated = page.Truncated || marked.Count > limit,
            Hits = shown,
        };
    }

    /// <summary>What a reader doesn't know of the first linked note a snippet names that they haven't met, if there is one.</summary>
    private LaterLink? LaterOf(IReadOnlyList<string>? linked, Dictionary<string, EntityRecord> records)
    {
        foreach (var path in linked ?? [])
        {
            if (records.TryGetValue(path, out var note) && (!IsInCanon(note.Status) || OfNote(note) is not null))
            {
                return new LaterLink(note.Status, OfNote(note));
            }
        }

        return null;
    }

    /// <summary>The marks of the facts of one note.</summary>
    internal sealed class NoteMarks
    {
        private readonly ReaderMarks _marks;
        private readonly EntityRecord _note;
        private readonly bool _inCanon;
        private readonly Dictionary<int, SectionRecord> _sections;
        private readonly List<FieldValueRecord> _values;
        private readonly Dictionary<long, string> _written = [];

        public NoteMarks(ReaderMarks marks, EntityRecord note)
            : this(marks, note, marks._db.Sections.Where(s => s.EntityId == note.Id).ToList(), marks._db.FieldValues.Where(v => v.EntityId == note.Id).ToList())
        {
        }

        public NoteMarks(ReaderMarks marks, EntityRecord note, IEnumerable<SectionRecord> sections, IEnumerable<FieldValueRecord> values)
        {
            _marks = marks;
            _note = note;
            _inCanon = IsInCanon(note.Status);
            _sections = sections.ToDictionary(s => s.Line);
            _values = values.ToList();

            Note = marks.OfNote(note);

            foreach (var text in WrittenPositions())
            {
                if (marks._points.TryGetValue(text, out var point))
                {
                    _written.TryAdd(point, text);
                }
            }
        }

        /// <summary>The note's own mark, when readers meet it after the position, or no position places it.</summary>
        public ReaderMark? Note { get; }

        /// <summary>The mark of a section, by its heading's line.</summary>
        public ReaderMark? Section(int line) =>
            _sections.GetValueOrDefault(line) is { } section && Takes(section) ? Fact(section) : null;

        /// <summary>The mark of a field's entry, or of its plain value.</summary>
        public ReaderMark? Field(string field, int? entryIndex) =>
            Row(field, entryIndex) is { } row && Takes(row) ? Fact(row) : null;

        /// <summary>
        /// The values of a position field later than the position (format.md
        /// F-VIEW-5), as written, or null if there are none.
        /// </summary>
        public IReadOnlyList<string>? AheadValues(string field, int? entryIndex) =>
            Row(field, entryIndex) is { } row ? Values(row).Ahead : null;

        /// <summary>
        /// The values of a position field that can't be compared (F-VIEW-5), as
        /// written, or null if there are none. An assistant at a position isn't
        /// told them either, so what's in neither list is what readers have reached.
        /// </summary>
        public IReadOnlyList<string>? UnplacedValues(string field, int? entryIndex) =>
            Row(field, entryIndex) is { } row ? Values(row).Unplaced : null;

        /// <summary>The mark of a link in a section, by its heading's line: the section's, or the note's.</summary>
        public ReaderMark? OfSectionLink(int line) =>
            _sections.GetValueOrDefault(line) is { } section ? (Takes(section) ? Fact(section) ?? Note : null) : Note;

        /// <summary>The mark of a link in a field's value or entry: its own, or the note's.</summary>
        public ReaderMark? OfFieldLink(string field, int? entryIndex) =>
            Row(field, entryIndex) is { } row ? (Takes(row) ? Fact(row) ?? Note : null) : Note;

        /// <summary>
        /// <paramref name="hit"/> with what readers at the position know of the text it
        /// found. It's <c>met</c> if a reader's search there finds it as it stands: it has
        /// no mark, no later value, and no later name, it's in canon, and its words
        /// aren't only in text a reader's read takes for a comment.
        /// </summary>
        public SearchPageHit Mark(SearchPageHit hit, string query, Dictionary<string, EntityRecord> records)
        {
            var known = ReadersCanFind(hit, query);
            var marked = Marks(hit) with
            {
                NoteMark = Note,
                LaterLink = _marks.LaterOf(hit.LinkedNotes, records),
                InComment = hit.InComment || !known,
            };
            return marked with
            {
                Met = known && hit.InCanon && marked.ReaderMark is null && marked.AheadValues is null && marked.UnplacedValues is null && marked.LaterLink is null,
            };
        }

        private SearchPageHit Marks(SearchPageHit hit)
        {
            if (hit.Field is not null)
            {
                if (Row(hit.Field, hit.EntryIndex) is not { } row)
                {
                    return hit with { ReaderMark = Note };
                }

                var (ahead, unplaced) = Values(row);
                return hit with { ReaderMark = Takes(row) ? Fact(row) ?? Note : null, AheadValues = ahead, UnplacedValues = unplaced };
            }

            if (hit.Section is not null && hit.Line is { } line)
            {
                return hit with { ReaderMark = _sections.GetValueOrDefault(line) is { } section && Takes(section) ? Fact(section) ?? Note : null };
            }

            // The name, an alias, or the lead: they hold wherever the note does.
            return hit with { ReaderMark = Note };
        }

        /// <summary>Whether the words of a hit in text are there when the comments aren't, by the rule of a reader's search. Text with none has nothing to check.</summary>
        private bool ReadersCanFind(SearchPageHit hit, string query)
        {
            if (hit.Field is not null)
            {
                return true;
            }

            if (hit.Section is not null && hit.Line is { } line)
            {
                return _sections.GetValueOrDefault(line) is not { } section || !HasComment(section.Name, section.Body) || DenReader.ReadersCanFind(query, section.Name, section.Body);
            }

            if (!HasComment(_note.Lead))
            {
                return true;
            }

            var noteId = _note.Id;
            var aliases = _marks._db.Aliases.Where(a => a.EntityId == noteId).Select(a => a.Alias).ToList();
            return DenReader.ReadersCanFind(query, string.Join("\n", aliases.Prepend(_note.Name)), _note.Lead);
        }

        private static bool HasComment(params string[] texts) => texts.Any(t => t.Contains("<!--", StringComparison.Ordinal));

        private FieldValueRecord? Row(string field, int? entryIndex) => _values.FirstOrDefault(v => v.Field == field && v.EntryIndex == entryIndex);

        private (IReadOnlyList<string>? Ahead, IReadOnlyList<string>? Unplaced) Values(FieldValueRecord row)
        {
            if (!Takes(row) || !_marks._positionFields.Contains((_note.Type, row.Field)))
            {
                return (null, null);
            }

            var ahead = new List<string>();
            var unplaced = new List<string>();
            foreach (var value in ValuesOf(row))
            {
                if (!_marks._points.TryGetValue(value, out var point))
                {
                    unplaced.Add(value);
                }
                else if (point > _marks._point)
                {
                    ahead.Add(value);
                }
            }

            return (ahead.Count > 0 ? ahead : null, unplaced.Count > 0 ? unplaced : null);
        }

        /// <summary>A field's value as text: each item of a list, or the one value. An unset value has none.</summary>
        private static List<string> ValuesOf(FieldValueRecord row)
        {
            using var document = JsonDocument.Parse(row.ValueJson);
            var value = document.RootElement;
            var items = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [value];
            return items
                .Where(i => i.ValueKind != JsonValueKind.Null)
                .Select(i => i.ValueKind == JsonValueKind.String ? i.GetString()! : i.GetRawText())
                .ToList();
        }

        /// <summary>Every position the note's own text names, so a mark can name a point as the writer did.</summary>
        private IEnumerable<string> WrittenPositions()
        {
            foreach (var section in _sections.Values)
            {
                if (section.FromText is { } from)
                {
                    yield return from;
                }

                if (section.UntilText is { } until)
                {
                    yield return until;
                }
            }

            foreach (var value in _values)
            {
                if (value.FromText is { } from)
                {
                    yield return from;
                }

                if (value.UntilText is { } until)
                {
                    yield return until;
                }

                if (_marks._positionFields.Contains((_note.Type, value.Field)))
                {
                    foreach (var text in ValuesOf(value))
                    {
                        yield return text;
                    }
                }
            }
        }

        private ReaderMark? Fact(SectionRecord s) =>
            Fact(s.FromOrdinal, s.UntilOrdinal, s.FromText, s.UntilText, () => NotResolving(s));

        private ReaderMark? Fact(FieldValueRecord v) =>
            Fact(v.FromOrdinal, v.UntilOrdinal, v.FromText, v.UntilText, () => FirstNotResolving(v.FromText, v.UntilText));

        /// <summary>
        /// What readers at the position don't see of an in-canon fact, and why.
        /// No position reaches one that doesn't resolve (F-POS-4), or whose
        /// range holds nothing (F-TMP-3, F-SEC-8, F-SEC-9). Otherwise it's past
        /// its end, or starts after the position. In a note readers meet later,
        /// a fact that starts no later than the note has nothing to say that
        /// the note's own mark doesn't, and in a note no position places, no
        /// fact does.
        /// </summary>
        private ReaderMark? Fact(long? from, long? until, string? fromText, string? untilText, Func<string?> notResolving)
        {
            if (Note?.Kind == ReaderMark.Unplaced)
            {
                return null;
            }

            if (from is not { } start || until is not { } end)
            {
                return new ReaderMark(ReaderMark.Unplaced, notResolving());
            }

            if (start >= end)
            {
                return new ReaderMark(ReaderMark.Never, Named(fromText, start));
            }

            if (end <= _marks._point)
            {
                return new ReaderMark(ReaderMark.Ended, Named(untilText, end));
            }

            return start > _marks._point && !(Note is not null && start <= _note.FromOrdinal)
                ? new ReaderMark(ReaderMark.Ahead, Named(fromText, start))
                : null;
        }

        /// <summary>The position that keeps a section from resolving: its own, a thread's <c>paid_off</c> for its Payoff (F-CORE-4), or that of a section holding it (F-SEC-9).</summary>
        private string? NotResolving(SectionRecord section)
        {
            if (FirstNotResolving(section.FromText, section.UntilText) is { } own)
            {
                return own;
            }

            if (section.ParentId is { } parentId)
            {
                return _sections.Values.FirstOrDefault(s => s.Id == parentId) is { } parent ? NotResolving(parent) : null;
            }

            return section.NameNormalized == "payoff" && _values.FirstOrDefault(v => v.Field == "paid_off") is { } paidOff
                ? ValuesOf(paidOff).FirstOrDefault(v => !_marks._points.ContainsKey(v))
                : null;
        }

        private string? FirstNotResolving(params string?[] written) =>
            written.FirstOrDefault(w => w is not null && !_marks._points.ContainsKey(w));

        /// <summary>The position to name for <paramref name="point"/>: as written on the fact if that's where it falls, else as written elsewhere in the note, else the chapter that's there.</summary>
        private string? Named(string? written, long point) =>
            written is not null && _marks._points.TryGetValue(written, out var at) && at == point ? written
                : _written.GetValueOrDefault(point) ?? _marks._names.GetValueOrDefault(point);

        /// <summary>
        /// Whether a section can have a reader mark. An idea, a bookkeeping
        /// section, or one an error leaves out keeps its own badge instead.
        /// </summary>
        private bool Takes(SectionRecord s) => _inCanon && IsInCanon(s.Status) && !s.IsBookkeeping && s.LeftOutJson is null;

        /// <summary>Whether a field's value or entry can have a reader mark. One out of canon keeps its own status instead.</summary>
        private bool Takes(FieldValueRecord v) => _inCanon && IsInCanon(v.Status);

    }

    private static bool IsInCanon(string status) => status is "draft" or "canon";
}

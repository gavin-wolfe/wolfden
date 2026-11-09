using System.Buffers;
using Wolfden.Core.Documents;
using Wolfden.Core.Entities;
using Wolfden.Core.Links;
using Wolfden.Core.Positions;
using Wolfden.Core.Structure;
using Wolfden.Core.Text;
using Wolfden.Core.Yaml;

namespace Wolfden.Core.Checks;

/// <summary>
/// The spoiler checks on names readers learn later (format.md F-NAME-1, F-NAME-2,
/// #67): a <c>known_as</c> name written as plain text that readers reach before
/// they learn it, and a file name that holds one. They run over the den only in
/// <c>check</c>, and over the notes a write saves for its effects
/// (<see cref="WrittenTextChecks"/>), and only with <c>spoiler_checks: true</c>,
/// so a den without them pays one test.
/// </summary>
/// <remarks>
/// Most notes don't hold a name, so before a note is read closely, its raw text
/// gets a first look for the longest word of each name, ignoring case, which
/// allocates next to nothing. Every match of a name holds its longest word whole,
/// so the look can't miss one, except through an accent: <c>Zoe</c> has to match
/// <c>Zoë</c>. So the look is for the name's folded words, and a text with any
/// letter that folding might change is always read closely (design §8).
/// </remarks>
internal static class NameChecks
{
    /// <summary>Stands in for code and a link's hidden target, so the words on either side of it aren't side by side.</summary>
    private const char Gap = '';

    public static List<DenDiagnostic> Run(EntityCatalog catalog, LinkResolver resolver) => Run(catalog, resolver, null);

    /// <summary>
    /// F-NAME-1 and F-NAME-2 in the notes of <paramref name="only"/>, or in every
    /// note if it's null. The names looked for, and the names readers already
    /// know, are their universes' as always, so a note's warnings are the same
    /// either way. With <paramref name="only"/>, nothing else is reported: an
    /// <c>allow_in</c> that reaches no note is its owner's warning, which <c>check</c> gives.
    /// </summary>
    /// <param name="catalog">The den's notes.</param>
    /// <param name="resolver">Resolves <c>allow_in</c>.</param>
    /// <param name="only">The notes to read, or null for all.</param>
    /// <param name="keys">If given, each warning's <see cref="LaterNameKey"/>, by the warning's <see cref="Diagnostic"/>, for a write's effects.</param>
    internal static List<DenDiagnostic> Run(EntityCatalog catalog, LinkResolver resolver, IReadOnlySet<CatalogEntity>? only, Dictionary<Diagnostic, LaterNameKey>? keys = null)
    {
        var found = new List<DenDiagnostic>();
        if (!catalog.Structure.SpoilerChecks)
        {
            return found;
        }

        Bookkeeping? bookkeeping = null;
        var universes = catalog.Entities.Where(e => e.KnownAs.Count > 0).Select(e => e.Universe).Distinct();
        if (only is not null)
        {
            universes = universes.Where(u => only.Any(e => e.Universe == u));
        }

        foreach (var universe in universes.ToList())
        {
            var entities = catalog.Entities.Where(e => e.Universe == universe).ToList();
            var names = Names(entities, resolver, found, keys);
            if (names.Count > 0)
            {
                bookkeeping ??= new Bookkeeping(catalog.Structure.BookkeepingSections);
                new TextScan(names, entities, bookkeeping, found, keys).Run(only);
            }
        }

        return only is null ? found : found.Where(d => d.Diagnostic.RuleId is "F-NAME-1" or "F-NAME-2" && only.Any(e => e.Path == d.Path)).ToList();
    }

    /// <summary>
    /// The names of one universe that F-NAME-1 looks for, with F-NAME-2 for each
    /// note's file name, and the warnings for an <c>allow_in</c> that reaches no note.
    /// </summary>
    private static List<CheckedName> Names(List<CatalogEntity> entities, LinkResolver resolver, List<DenDiagnostic> found, Dictionary<Diagnostic, LaterNameKey>? keys)
    {
        var names = new List<CheckedName>();
        foreach (var entity in entities.Where(e => e.KnownAs.Count > 0))
        {
            // A note no view holds has no names readers learn.
            if (!entity.Status.IsInCanon() || entity.EntersCanon is not { } met)
            {
                continue;
            }

            var fileName = entity.Path[(entity.Path.LastIndexOf('/') + 1)..^3];
            var fileWords = SearchTokens.Tokens(fileName).ConvertAll(t => t.Folded);
            // An entry with no from, or one that doesn't resolve, is a name readers never learn (F-ENT-10 warns),
            // so every use of it gives it away.
            foreach (var known in entity.KnownAs)
            {
                var words = SearchTokens.Tokens(known.Value).ConvertAll(t => t.Folded).ToArray();
                if (words.Length == 0)
                {
                    continue;
                }

                var point = known.Point;
                var learnedAt = point is null ? "no point in the story, since its from doesn't resolve" : $"{known.From}";

                // F-NAME-2: a file name is chosen, so even a one-word name counts.
                if ((point is null || met < point) && IndexOf(fileWords, words) >= 0)
                {
                    var warning = new Diagnostic("F-NAME-2", DiagnosticSeverity.Warning, known.Line,
                        $"The file name '{fileName}.md' holds '{known.Value}', a name of '{entity.Name}' that readers learn at {learnedAt}, " +
                        $"and a file's name shows in every read from the note's entry, {entity.EntersCanonDescription}. " +
                        "Rename the file after the name the note is filed under, with Edit file in the web or by hand.");
                    found.Add(new DenDiagnostic(entity.Path, warning));
                    keys?.Add(warning, new LaterNameKey("F-NAME-2", entity.Path, $"{known.Value}\n{learnedAt}\n{fileName}\n{entity.EntersCanonDescription}"));
                }

                if (!known.IsTextChecked)
                {
                    continue;
                }

                var allowed = new HashSet<CatalogEntity>();
                foreach (var item in known.AllowIn)
                {
                    var resolution = resolver.Resolve(entity.Scope, item);
                    if (resolution.Entity is { } note)
                    {
                        allowed.Add(note);
                    }
                    else
                    {
                        found.Add(new DenDiagnostic(entity.Path, new Diagnostic("F-ENT-10", DiagnosticSeverity.Warning, known.Line,
                            $"known_as: allow_in of '{known.Value}' names '{item}', which reaches no one note: {resolution.Problem} " +
                            "So it silences nothing, and the name is still checked everywhere.")));
                    }
                }

                // Readers can't learn a name before they meet the note.
                var (learned, at) = point is null ? (null, learnedAt)
                    : met > point ? ((StoryPoint?)met, entity.EntersCanonDescription)
                    : (point, learnedAt);
                names.Add(new CheckedName(entity, known, words, string.Join(' ', words), learned, at, allowed));
            }
        }

        return names;
    }

    /// <summary>Where <paramref name="words"/> first occur side by side in <paramref name="tokens"/>, or -1.</summary>
    private static int IndexOf(List<string> tokens, string[] words)
    {
        for (var i = 0; i + words.Length <= tokens.Count; i++)
        {
            var j = 0;
            while (j < words.Length && tokens[i + j] == words[j])
            {
                j++;
            }

            if (j == words.Length)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>A name F-NAME-1 looks for.</summary>
    /// <param name="Entity">The note it's a name of.</param>
    /// <param name="Known">Its <c>known_as</c> entry.</param>
    /// <param name="Words">Its words, folded.</param>
    /// <param name="Phrase">Its words joined by spaces, to compare with other names.</param>
    /// <param name="Learned">Where readers learn it: its <c>from</c>, or the note's entry if that's later. Null if they never do: its <c>from</c> is missing or doesn't resolve.</param>
    /// <param name="LearnedAt">That position, for the message.</param>
    /// <param name="AllowIn">The notes its <c>allow_in</c> reaches.</param>
    private sealed record CheckedName(CatalogEntity Entity, KnownName Known, string[] Words, string Phrase, StoryPoint? Learned, string LearnedAt, HashSet<CatalogEntity> AllowIn);

    /// <summary>F-NAME-1 over one universe's notes.</summary>
    private sealed class TextScan
    {
        private readonly List<CatalogEntity> _entities;
        private readonly Bookkeeping _bookkeeping;
        private readonly List<DenDiagnostic> _found;
        private readonly Dictionary<Diagnostic, LaterNameKey>? _keys;
        private readonly Dictionary<string, List<CheckedName>> _byFirstWord = new(StringComparer.Ordinal);
        private readonly SearchValues<string> _firstLook;

        /// <summary>For each checked name's words, where each note's names that have the same words become known (F-VIEW-7).</summary>
        private readonly Dictionary<string, List<StoryPoint>> _knownFrom = new(StringComparer.Ordinal);

        public TextScan(List<CheckedName> names, List<CatalogEntity> entities, Bookkeeping bookkeeping, List<DenDiagnostic> found, Dictionary<Diagnostic, LaterNameKey>? keys)
        {
            _entities = entities;
            _bookkeeping = bookkeeping;
            _found = found;
            _keys = keys;
            foreach (var name in names.OrderBy(n => n.Learned is null).ThenBy(n => n.Learned))
            {
                if (!_byFirstWord.TryGetValue(name.Words[0], out var list))
                {
                    _byFirstWord[name.Words[0]] = list = [];
                }

                list.Add(name);
                _knownFrom.TryAdd(name.Phrase, []);
            }

            _firstLook = SearchValues.Create(names.Select(n => n.Words.MaxBy(w => w.Length)!).Distinct().ToArray(), StringComparison.OrdinalIgnoreCase);
            KnowNames();
        }

        /// <summary>Reads the notes of <paramref name="only"/> in this universe, or all of its notes if it's null.</summary>
        public void Run(IReadOnlySet<CatalogEntity>? only)
        {
            foreach (var entity in only is null ? _entities : _entities.Where(only.Contains))
            {
                if (entity.Status.IsInCanon() && entity.EntersCanon is not null && entity.File.IsReadable && (MayHold(entity.File.Text) || FrontmatterMayHold(entity)))
                {
                    ReadClosely(entity);
                }
            }
        }

        /// <summary>
        /// Fills <see cref="_knownFrom"/>: of each note in a view, its name and aliases are
        /// known from its entry, and each <c>known_as</c> name from the later of its
        /// <c>from</c> and that entry.
        /// </summary>
        private void KnowNames()
        {
            foreach (var entity in _entities)
            {
                if (!entity.Status.IsInCanon() || entity.EntersCanon is not { } met)
                {
                    continue;
                }

                foreach (var name in entity.Aliases.Prepend(entity.Name))
                {
                    Know(name, met);
                }

                foreach (var known in entity.KnownAs.Where(k => k.IsLive))
                {
                    Know(known.Value, known.Point!.Value > met ? known.Point.Value : met);
                }
            }

            void Know(string name, StoryPoint from)
            {
                if (_knownFrom.TryGetValue(string.Join(' ', SearchTokens.Tokens(name).Select(t => t.Folded)), out var list))
                {
                    list.Add(from);
                }
            }
        }

        /// <summary>The note's <c>name</c> and the aliases that count, as written in its frontmatter.</summary>
        private static IEnumerable<YamlScalar> Names(CatalogEntity entity)
        {
            var frontmatter = entity.File.Frontmatter;
            if (frontmatter?["name"] is YamlScalar { IsNull: false } name)
            {
                yield return name;
            }

            if (frontmatter?["aliases"] is YamlSequence aliases)
            {
                foreach (var alias in aliases.Items.OfType<YamlScalar>().Where(a => !a.IsNull && entity.Aliases.Contains(a.Text)))
                {
                    yield return alias;
                }
            }
        }

        /// <summary>
        /// The first look again, at the frontmatter's strings as YAML reads them, for a
        /// name the raw text spells with an escape, such as <c>"Amélie"</c>.
        /// </summary>
        private bool FrontmatterMayHold(CatalogEntity entity) =>
            entity.File.Frontmatter is { } frontmatter
            && Names(entity).Concat(frontmatter["fields"] is { } fields ? LinkScanner.Strings(fields) : []).Any(s => MayHold(s.Text));

        /// <summary>The first look: false only if <paramref name="text"/> can't hold any of the names.</summary>
        private bool MayHold(ReadOnlySpan<char> text) =>
            SearchTokens.MayHoldAccents(text) || text.IndexOfAny(_firstLook) >= 0;

        private void ReadClosely(CatalogEntity entity)
        {
            var file = entity.File;
            var source = file.SourceForReading!;
            var text = source.Text;
            var met = entity.EntersCanon!.Value;
            var facts = EntityFacts.For(entity, _bookkeeping);
            var sections = file.Sections;

            // The lead, title included, is at the note's entry.
            var leadEnd = sections.Count > 0 ? sections[0].HeadingStart : text.Length;
            ScanMarkdown(entity, source, file.BodyStart, leadEnd, met, entity.EntersCanonDescription);

            // So are its name and aliases. An alias that's also a known_as name was taken out (F-ENT-10), and
            // another note's name that's the same words is a name readers know there.
            foreach (var name in Names(entity))
            {
                ScanValues(entity, name, met, entity.EntersCanonDescription);
            }

            // Each section's heading and its own text, at its effective from, if it's live.
            foreach (var section in sections)
            {
                var fact = facts[section];
                if (fact.IsLive)
                {
                    var (point, at) = Later(fact.EffectiveFrom!, entity);
                    ScanMarkdown(entity, source, section.HeadingStart, section.OwnEnd, point, at);
                }
            }

            foreach (var field in facts.Fields)
            {
                if (field.Kind == FieldValueKind.Plain)
                {
                    ScanValues(entity, field.Value, met, entity.EntersCanonDescription);
                    continue;
                }

                foreach (var entry in field.Entries.Where(e => e.IsLive && e.Value is not null))
                {
                    var from = new FactBound(entry.EffectiveFrom!.Value, entry.FromText ?? entity.Universe.StartDescription);
                    var (point, at) = Later(from, entity);
                    ScanValues(entity, entry.Value!, point, at);
                }
            }
        }

        /// <summary>A fact's position, or the note's entry if that's later: nothing in a note shows before readers meet it.</summary>
        private static (StoryPoint Point, string At) Later(FactBound from, CatalogEntity entity) =>
            entity.EntersCanon is { } met && met > from.Point ? (met, entity.EntersCanonDescription) : (from.Point, from.Description);

        /// <summary>The text of a lead or section, without its comments, code, and links' hidden targets.</summary>
        private void ScanMarkdown(CatalogEntity entity, SourceText source, int start, int end, StoryPoint point, string at)
        {
            if (end <= start || !MayHold(source.Text.AsSpan(start, end - start)))
            {
                return;
            }

            var markdown = source.Text[start..end];
            var shown = markdown.ToCharArray();
            var (comments, code) = NotProse(markdown);

            // A read leaves a comment out as if it weren't there, so the words around it meet.
            foreach (var (from, to) in comments)
            {
                Blank(shown, from, to, gap: false);
            }

            foreach (var (from, to) in code)
            {
                Blank(shown, from, to, gap: true);
            }

            foreach (var link in LinkSyntax.Scan(markdown, 0, markdown.Length, new RangeSet(comments.Concat(code))))
            {
                BlankLink(markdown, shown, link);
            }

            Find(entity, new string(shown), offset => source.LineIndexOf(start + offset) + 1, point, at);
        }

        /// <summary>
        /// <see cref="MarkdownComments.NotProse"/> for text that may have CRLF or lone CR
        /// line breaks: the comment rules and the parser read LF text, as a read gives
        /// them, so it's read with LF breaks and the ranges are mapped back.
        /// </summary>
        internal static (List<(int Start, int End)> Comments, List<(int Start, int End)> Code) NotProse(string markdown)
        {
            if (!markdown.Contains('\r', StringComparison.Ordinal))
            {
                return MarkdownComments.NotProse(markdown);
            }

            // map[i] is where the LF text's offset i is in markdown; one past the end maps to the end.
            var lf = new System.Text.StringBuilder(markdown.Length);
            var map = new List<int>(markdown.Length + 1);
            for (var i = 0; i < markdown.Length; i++)
            {
                if (markdown[i] == '\r' && i + 1 < markdown.Length && markdown[i + 1] == '\n')
                {
                    continue;
                }

                lf.Append(markdown[i] == '\r' ? '\n' : markdown[i]);
                map.Add(i);
            }

            map.Add(markdown.Length);
            var (comments, code) = MarkdownComments.NotProse(lf.ToString());
            return (comments.ConvertAll(Back), code.ConvertAll(Back));

            (int Start, int End) Back((int Start, int End) r) => (map[Math.Min(r.Start, map.Count - 1)], map[Math.Min(r.End, map.Count - 1)]);
        }

        /// <summary>The strings of a field's value, with links' hidden targets taken out. A field's value isn't markdown.</summary>
        private void ScanValues(CatalogEntity entity, YamlNode value, StoryPoint point, string at)
        {
            foreach (var scalar in LinkScanner.Strings(value))
            {
                var text = scalar.Text;
                if (!MayHold(text))
                {
                    continue;
                }

                var shown = text.ToCharArray();
                foreach (var link in LinkSyntax.Scan(text, 0, text.Length, null))
                {
                    BlankLink(text, shown, link);
                }

                Find(entity, new string(shown), _ => scalar.Line, point, at);
            }
        }

        /// <summary>
        /// A link as a read shows it: a label stays, since readers see it, and the
        /// target goes. A link with no label shows its target or the note's name,
        /// which F-LINK-13 checks, not this.
        /// </summary>
        private static void BlankLink(string text, char[] shown, LinkSyntax.RawLink link)
        {
            var bar = link.Label is null ? -1 : text.IndexOf('|', link.Offset, link.Length);
            if (bar < 0)
            {
                Blank(shown, link.Offset, link.Offset + link.Length, gap: true);
                return;
            }

            Blank(shown, link.Offset, bar + 1, gap: false);
            Blank(shown, link.Offset + link.Length - 2, link.Offset + link.Length, gap: false);
        }

        private static void Blank(char[] shown, int from, int to, bool gap)
        {
            to = Math.Min(to, shown.Length);
            for (var i = from; i < to; i++)
            {
                shown[i] = ' ';
            }

            // In the middle, with a space on each side, so it's a word of its own and doesn't join the words beside it.
            if (gap && to - from >= 3)
            {
                shown[from + ((to - from) / 2)] = Gap;
            }
        }

        /// <summary>Warns, once a line, for each name in <paramref name="shown"/> that readers at <paramref name="point"/> haven't learned.</summary>
        private void Find(CatalogEntity entity, string shown, Func<int, int> lineOf, StoryPoint point, string at)
        {
            var tokens = SearchTokens.Tokens(shown);
            // Names with the same words (two spellings, or two notes) warn once, for the one readers learn first.
            HashSet<(int, string)>? warned = null;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!_byFirstWord.TryGetValue(tokens[i].Folded, out var names))
                {
                    continue;
                }

                foreach (var name in names)
                {
                    if (name.Learned <= point || !Matches(tokens, i, name.Words) || name.AllowIn.Contains(entity) || IsKnown(name, point))
                    {
                        continue;
                    }

                    var line = lineOf(tokens[i].Start);
                    if ((warned ??= []).Add((line, name.Phrase)))
                    {
                        var warning = new Diagnostic("F-NAME-1", DiagnosticSeverity.Warning, line,
                            $"'{name.Known.Value}' is a name of '{name.Entity.Name}' that readers learn at {name.LearnedAt}, and this text is at {at}. " +
                            "It may be a spoiler. If it is, give the text a later from, or take the name out; if it means someone else, " +
                            $"list this note in the name's allow_in.");
                        _found.Add(new DenDiagnostic(entity.Path, warning));
                        _keys?.Add(warning, new LaterNameKey("F-NAME-1", name.Entity.Path, $"{name.Known.Value}\n{name.LearnedAt}\n{at}"));
                    }
                }
            }
        }

        private static bool Matches(List<SearchToken> tokens, int at, string[] words)
        {
            if (at + words.Length > tokens.Count)
            {
                return false;
            }

            for (var j = 1; j < words.Length; j++)
            {
                if (tokens[at + j].Folded != words[j])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True if the same words are already a name readers know at <paramref name="point"/>, of this note or another, so the text may mean that one.</summary>
        private bool IsKnown(CheckedName name, StoryPoint point) =>
            _knownFrom[name.Phrase].Any(from => from <= point);
    }
}

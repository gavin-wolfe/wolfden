using Wolfden.Core.Documents;
using Wolfden.Core.Editing;
using Wolfden.Core.Entities;
using Wolfden.Core.Links;
using Wolfden.Core.Structure;

namespace Wolfden.Core.Checks;

/// <summary>
/// The write-time side of the spoiler checks on names readers learn later
/// (#67, design §7 "The write-time version"): the warnings a write adds to the
/// notes it writes. They are <c>check</c>'s own F-NAME-1, F-NAME-2, and
/// F-LINK-13 (a link by a later name), from the same code, run on those notes
/// alone rather than the whole den. Only with <c>spoiler_checks: true</c>.
/// </summary>
/// <remarks>
/// A warning is new if the note didn't have it before the write, compared by
/// a <see cref="LaterNameKey"/> and counted, but not by line: the rule, the
/// note the name belongs to (by path, not its display name, so renaming it
/// changes nothing), the name, where readers learn it, the text's position,
/// and a link's text. So a name that was already in the note at the same
/// position isn't reported again, wherever the write moved its line, and a
/// second one is. When a key occurs more than once, old and new warnings are
/// paired by the text of their lines first, so the one reported is the new
/// line. A write that changes a position, or where readers learn a name,
/// changes the key, so the note's warnings that follow from it are new. The
/// warnings never refuse a write.
/// </remarks>
public static class WrittenTextChecks
{
    /// <summary>
    /// The warnings a write of <paramref name="files"/> adds to the notes among
    /// them. It loads the notes of the universe they're in (or the whole den, for
    /// notes in more than one), so a caller that has the den's notes loaded
    /// already should call <see cref="LaterNames(EntityCatalog, IDenSource, IReadOnlyCollection{string}, IReadOnlyDictionary{string, string}?)"/>.
    /// </summary>
    /// <param name="den">The den before the write.</param>
    /// <param name="structure">The den's structure.</param>
    /// <param name="files">The files the write saves, by path.</param>
    public static IReadOnlyList<DenDiagnostic> LaterNames(IDenSource den, DenStructure structure, IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(den);
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(files);
        if (!structure.SpoilerChecks)
        {
            return [];
        }

        var universes = files.Keys.Select(p => EntityCatalog.EntityFileScope(structure, p)?.Universe).OfType<Universe>().Distinct().ToList();
        if (universes.Count == 0)
        {
            return [];
        }

        // One universe's notes are all a note's names and links can reach (F-LINK-5).
        var before = universes.Count == 1 ? EntityCatalog.Load(structure, new UniverseScopedSource(den, universes[0].Key)) : EntityCatalog.Load(structure, den);
        return LaterNames(before, new OverlayDenSource(den, files), files.Keys.ToList());
    }

    /// <summary>The warnings a write adds to the notes it writes, given the den's notes before it.</summary>
    /// <param name="before">The den's notes before the write.</param>
    /// <param name="after">The den after the write.</param>
    /// <param name="written">The paths the write saves. Paths that aren't notes are skipped.</param>
    /// <param name="movedFrom">For a note that moves, its new path to its old one.</param>
    public static IReadOnlyList<DenDiagnostic> LaterNames(
        EntityCatalog before, IDenSource after, IReadOnlyCollection<string> written, IReadOnlyDictionary<string, string>? movedFrom = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(written);
        if (!before.Structure.SpoilerChecks)
        {
            return [];
        }

        // Before the den's notes are put together again: with no name readers learn later in the universes written,
        // and none in the notes the write saves, there's nothing to say.
        var universes = written.Concat(movedFrom?.Values ?? []).Select(p => EntityCatalog.EntityFileScope(before.Structure, p)?.Universe.Key).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (!universes.Any(u => HasLaterNames(before, u))
            && !written.Any(p => universes.Count > 0 && after.TryReadFile(p) is { } bytes && System.Text.Encoding.UTF8.GetString(bytes).Contains("known_as", StringComparison.Ordinal)))
        {
            return [];
        }

        return LaterNames(before, before.Update(after, written.Concat(movedFrom?.Values ?? [])), written, movedFrom);
    }

    /// <summary>The warnings a write adds to the notes it writes, given the den's notes before and after it.</summary>
    /// <param name="before">The den's notes before the write.</param>
    /// <param name="after">The den's notes after the write, over the same structure.</param>
    /// <param name="written">The paths the write saves. Paths that aren't notes are skipped.</param>
    /// <param name="movedFrom">For a note that moves, its new path to its old one.</param>
    public static IReadOnlyList<DenDiagnostic> LaterNames(
        EntityCatalog before, EntityCatalog after, IReadOnlyCollection<string> written, IReadOnlyDictionary<string, string>? movedFrom = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(written);
        if (!after.Structure.SpoilerChecks)
        {
            return [];
        }

        var oldPath = written.ToDictionary(p => p, p => movedFrom?.GetValueOrDefault(p) ?? p, StringComparer.Ordinal);
        var wrote = written.Select(after.FindByPath).OfType<CatalogEntity>().ToHashSet();
        var had = oldPath.Values.Select(before.FindByPath).OfType<CatalogEntity>().ToHashSet();

        // A den with no names readers learn later, in the universes written, stops here.
        if (!wrote.Concat(had).Select(e => e.Universe.Key).Distinct().Any(u => HasLaterNames(before, u) || HasLaterNames(after, u)))
        {
            return [];
        }

        var newPath = oldPath.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);
        string Now(string path) => newPath.GetValueOrDefault(path, path);

        // The old warnings' line texts, by key, each one paired off at most once.
        var old = new Dictionary<(string, LaterNameKey), List<string>>();
        var beforeLines = new Lines(before);
        foreach (var (d, key) in Scan(before, had))
        {
            var k = (Now(d.Path), key with { Owner = Now(key.Owner) });
            if (!old.TryGetValue(k, out var texts))
            {
                old[k] = texts = [];
            }

            texts.Add(beforeLines.Of(d));
        }

        var afterLines = new Lines(after);
        var found = Scan(after, wrote);
        var matched = new bool[found.Count];

        // First by the text of the line, so a new mention above an old one is the one said; then by count alone.
        foreach (var byText in new[] { true, false })
        {
            for (var i = 0; i < found.Count; i++)
            {
                if (matched[i] || !old.TryGetValue((found[i].Warning.Path, found[i].Key), out var texts) || texts.Count == 0)
                {
                    continue;
                }

                var at = byText ? texts.IndexOf(afterLines.Of(found[i].Warning)) : 0;
                if (at >= 0)
                {
                    texts.RemoveAt(at);
                    matched[i] = true;
                }
            }
        }

        return found.Where((_, i) => !matched[i]).Select(f => f.Warning).ToList();
    }

    private static bool HasLaterNames(EntityCatalog catalog, string universe) =>
        catalog.Entities.Any(e => e.KnownAs.Count > 0 && e.Universe.Key == universe);

    /// <summary>F-NAME-1, F-NAME-2, and F-LINK-13 by a later name, in <paramref name="notes"/>, by path and line, each with its key.</summary>
    private static List<(DenDiagnostic Warning, LaterNameKey Key)> Scan(EntityCatalog catalog, IReadOnlySet<CatalogEntity> notes)
    {
        if (notes.Count == 0)
        {
            return [];
        }

        var resolver = new LinkResolver(catalog);
        var bookkeeping = new Bookkeeping(catalog.Structure.BookkeepingSections);
        var keys = new Dictionary<Diagnostic, LaterNameKey>(ReferenceEqualityComparer.Instance);
        return NameChecks.Run(catalog, resolver, notes, keys)
            .Concat(notes.SelectMany(e => LinkIndex.LaterNameLinks(resolver, e, bookkeeping, keys)))
            .OrderBy(d => d.Path, StringComparer.Ordinal).ThenBy(d => d.Diagnostic.Line ?? 0)
            .Select(d => (d, keys[d.Diagnostic]))
            .ToList();
    }

    /// <summary>The text of a warning's line in its note, read once a note.</summary>
    private sealed class Lines(EntityCatalog catalog)
    {
        private readonly Dictionary<string, string[]> _files = new(StringComparer.Ordinal);

        public string Of(DenDiagnostic warning)
        {
            if (!_files.TryGetValue(warning.Path, out var lines))
            {
                var file = catalog.FindByPath(warning.Path)?.File;
                _files[warning.Path] = lines = file is null ? [] : System.Text.Encoding.UTF8.GetString(file.ToBytes()).Split('\n');
            }

            var line = (warning.Diagnostic.Line ?? 0) - 1;
            return line >= 0 && line < lines.Length ? lines[line].TrimEnd('\r') : "";
        }
    }
}

/// <summary>
/// What tells one later-name warning from another across a write, for its effects
/// (<see cref="WrittenTextChecks"/>): its rule, the note whose name it is, by path, and
/// the rest of what the message says (the name, where readers learn it, the text's
/// position, a link's text), without that note's display name, which a rename changes.
/// </summary>
/// <param name="Rule">The rule.</param>
/// <param name="Owner">The path of the note whose name it is.</param>
/// <param name="Rest">The rest, joined by line breaks.</param>
internal sealed record LaterNameKey(string Rule, string Owner, string Rest);

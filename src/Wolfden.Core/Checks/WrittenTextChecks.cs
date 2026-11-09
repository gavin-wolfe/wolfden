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
/// rule and message and counted, but not by line. The message names the name,
/// where readers learn it, and the text's position, so a name that was already
/// in the note at the same position isn't reported again, wherever the write
/// moved its line, and a second one is. A write that changes a position, or
/// where readers learn a name, changes the message, so the note's warnings
/// that follow from it are new. The warnings never refuse a write.
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
        var counts = new Dictionary<(string, string, string), int>();
        foreach (var d in Scan(before, had))
        {
            var key = (newPath.GetValueOrDefault(d.Path, d.Path), d.Diagnostic.RuleId, d.Diagnostic.Message);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var added = new List<DenDiagnostic>();
        foreach (var d in Scan(after, wrote))
        {
            var key = (d.Path, d.Diagnostic.RuleId, d.Diagnostic.Message);
            if (counts.GetValueOrDefault(key) is > 0 and var left)
            {
                counts[key] = left - 1;
            }
            else
            {
                added.Add(d);
            }
        }

        return added;
    }

    private static bool HasLaterNames(EntityCatalog catalog, string universe) =>
        catalog.Entities.Any(e => e.KnownAs.Count > 0 && e.Universe.Key == universe);

    /// <summary>F-NAME-1, F-NAME-2, and F-LINK-13 by a later name, in <paramref name="notes"/>, by path and line.</summary>
    private static List<DenDiagnostic> Scan(EntityCatalog catalog, IReadOnlySet<CatalogEntity> notes)
    {
        if (notes.Count == 0)
        {
            return [];
        }

        var resolver = new LinkResolver(catalog);
        var bookkeeping = new Bookkeeping(catalog.Structure.BookkeepingSections);
        return NameChecks.Run(catalog, resolver, notes)
            .Concat(notes.SelectMany(e => LinkIndex.LaterNameLinks(resolver, e, bookkeeping)))
            .OrderBy(d => d.Path, StringComparer.Ordinal).ThenBy(d => d.Diagnostic.Line ?? 0)
            .ToList();
    }
}

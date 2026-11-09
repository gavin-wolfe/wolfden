using Wolfden.Core.Documents;
using Wolfden.Core.Editing;
using Wolfden.Core.Entities;
using Wolfden.Core.Links;
using Wolfden.Core.Structure;
using Wolfden.Core.Yaml;
using Wolfden.Index.Reading;

namespace Wolfden.Index.Writing;

/// <summary>
/// Renaming an entity, and changing or removing one of its aliases, with
/// the links that used the old text following in the same change (format.md
/// F-RT-5, maintainer decision 2026-10-25, #150).
/// </summary>
/// <remarks>
/// By default every link that reached the entity by the old text is
/// rewritten to the new text, in any file of the den, as
/// <see cref="RenameSection"/> rewrites heading links. Instead, the old text
/// can be kept as an alias, and then no link changes. If a link to rewrite
/// is in a final book, nothing changes, and the error suggests keeping the
/// old text, which leaves the frozen book alone. A rewrite that would send a
/// rewritten link somewhere else is refused. So is one that would re-point a
/// link that reached another entity (a new name that's another entity's
/// alias takes its links, F-LINK-6), unless <c>allow_relinks</c> says to go
/// ahead (maintainer decision, 2026-10-26). Any other link that would
/// resolve differently afterwards (an alias kept that another entity also
/// has, F-LINK-7, say), and any relinked link allowed, is listed in the
/// result's effects.
/// </remarks>
public sealed partial class DenTools
{
    /// <summary>How many links whose resolution changes are listed in effects, before "and N more".</summary>
    private const int ListedLinkChanges = 10;

    /// <summary>
    /// True to refuse a change that re-points a link in a final book even with
    /// <c>allow_relinks</c>. The maintainer's decision (2026-10-26) lets
    /// <c>allow_relinks</c> cover them for now.
    /// </summary>
    private const bool RefuseRelinksInFinalBooks = false;

    /// <summary>
    /// Changes an entity's <c>name</c>, and rewrites every link that reached
    /// it by the old name to the new one, in any file of the den (format.md
    /// F-RT-5). With <paramref name="keepOldName"/>, the old name becomes an
    /// alias instead, and no link changes. A display title (<c># Old name</c>)
    /// that reads the old name follows. The file isn't renamed.
    /// </summary>
    public WriteResult RenameEntity(string entity, string newName, string expectedHash, bool keepOldName = false, string? universe = null, bool allowRelinks = false)
    {
        var hash = RequireHash(expectedHash);
        if (NewEntity.NameProblem(newName) is { } problem)
        {
            throw new DenToolException(
                ToolErrorCodes.BadArgument,
                $"Give the entity's new name in 'new_name': one line of text, at most {NewEntity.MaxNameLength} characters. That one can't be used: {problem}.");
        }

        newName = newName.Trim();
        return ChangeText(entity, universe, hash, allowRelinks, target =>
        {
            var oldName = target.Name;
            if (oldName == newName)
            {
                return null;
            }

            var sameForLinks = LinkText.Matches(oldName, newName);
            var keep = keepOldName && !sameForLinks;
            var alreadyAlias = target.Aliases.Any(a => LinkText.Matches(a, oldName));

            // Renamed to one of its own aliases: the alias goes, so the name isn't also an alias.
            var aliasItems = (target.File.GetFrontmatterValue("aliases") as YamlSequence)?.Items ?? [];
            var ownAlias = sameForLinks ? -1 : aliasItems.ToList().FindIndex(i => i is YamlScalar { IsNull: false } s && LinkText.Matches(s.Text, newName));
            var dropped = ownAlias >= 0 ? ((YamlScalar)aliasItems[ownAlias]).Text : null;

            // Renamed to one of its own known_as names: the entry goes, since a name can't be hidden (F-ENT-10).
            var ownKnown = sameForLinks || target.KnownNameMatching(newName) is null ? null : NameSlot.Find(target, newName);
            const string Hint = "pass keep_old_name: true to keep the old name as an alias instead, which leaves those links as they are";
            var notes = new List<string>();
            return new TextChange(
                LinkMatchKind.Name, oldName, newName, keep || sameForLinks ? null : newName,
                file =>
                {
                    file = Apply(() => file.WithFrontmatterValue(["name"], YamlScalarValue.String(newName)));
                    if (ownAlias >= 0)
                    {
                        file = Apply(() => NoteEmptied(
                            notes, file.WithoutFrontmatterValue(["aliases", ownAlias.ToString(System.Globalization.CultureInfo.InvariantCulture)], EmptiedList.Null, out var comments), comments, "aliases"));
                    }

                    if (ownKnown is not null)
                    {
                        file = Apply(() => ownKnown.Remove(file, notes));
                    }

                    if (keep && !alreadyAlias)
                    {
                        file = Apply(() => file.WithNewListItem(["aliases"], YamlScalarValue.String(oldName), null, inTemplateOrder: true));
                    }

                    return file.Title is { } title && title.Trim() == oldName ? Apply(() => file.WithTitle(newName)) : file;
                },
                Kept: keep ? oldName : null,
                NewAlias: null,
                SameForLinks: sameForLinks,
                KeepHint: $"To make the change, {Hint}.",
                LostHint: "choose another name, or " + Hint,
                DroppedAlias: dropped,
                Notes: notes)
            {
                DroppedKnown = ownKnown is null ? null : (ownKnown.Text, ownKnown.From),
            };
        });
    }

    /// <summary>
    /// Changes one of an entity's aliases to <paramref name="newAlias"/>, or
    /// removes it when <paramref name="newAlias"/> is null, and rewrites every
    /// link that reached the entity by the old alias, in any file of the den
    /// (format.md F-RT-5): to the new alias, or, for a removed alias, to the
    /// entity's name. With <paramref name="keepOldAlias"/>, the new alias is
    /// added beside the old one instead, and no link changes. The alias can be
    /// a plain one (<c>aliases</c>) or a name readers learn later (<c>known_as</c>,
    /// F-ENT-10): a new or changed one stays in the same key, with the same
    /// <c>from</c>.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="alias">The alias as written.</param>
    /// <param name="expectedHash">The hash of the file the change is based on.</param>
    /// <param name="newAlias">The new alias, or null to remove it, or to keep it and only change its <paramref name="from"/>.</param>
    /// <param name="keepOldAlias">True to add the new alias beside the old.</param>
    /// <param name="universe">The universe, or null.</param>
    /// <param name="allowRelinks">True to go ahead when a link to another entity would change target.</param>
    /// <param name="from">
    /// Where readers learn the alias, the new one if there is one: a position makes it a <c>known_as</c>
    /// name from there. Null, empty, or blank leaves it as it is. Moving a name between the two keys, or changing its <c>from</c>, rewrites no
    /// link, since links don't depend on positions (#67, §9).
    /// </param>
    /// <param name="plain">True to make the alias (the new one, if any) a plain alias, known wherever the entity is. Not with <paramref name="from"/>.</param>
    public WriteResult ChangeAlias(
        string entity, string alias, string expectedHash, string? newAlias = null, bool keepOldAlias = false, string? universe = null, bool allowRelinks = false,
        string? from = null, bool plain = false)
    {
        var hash = RequireHash(expectedHash);
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, "Give the alias to change in 'alias'.");
        }

        if (newAlias is not null && (string.IsNullOrWhiteSpace(newAlias) || newAlias.Any(char.IsControl)))
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, "An alias is one line of text, not empty (F-ENT-4). To remove the alias, leave out 'new_alias'.");
        }

        if (newAlias is null && keepOldAlias)
        {
            throw new DenToolException(
                ToolErrorCodes.BadArgument, "'keep_old_alias' keeps the old alias beside a new one: give the new one in 'new_alias'. Without it, the alias is removed.");
        }

        // An empty or blank from counts as left out: a client that fills optional strings with "" mustn't make a secret public.
        if (plain && !string.IsNullOrWhiteSpace(from))
        {
            throw new DenToolException(
                ToolErrorCodes.BadArgument,
                "Give 'plain' (known wherever the entity is) or 'from' (a name readers learn there), not both.");
        }

        newAlias = newAlias?.Trim();

        // Internally, an empty 'learned' means plain, and null means as it is.
        var learned = plain ? "" : string.IsNullOrWhiteSpace(from) ? null : from.Trim();
        return ChangeText(entity, universe, hash, allowRelinks, target =>
        {
            if (learned is { Length: > 0 })
            {
                CheckKnownFrom(target.Universe, newAlias ?? alias.Trim(), learned);
            }

            var slot = NameSlot.Find(target, alias.Trim()) ?? throw new DenToolException(
                ToolErrorCodes.BadArgument,
                $"'{target.Name}' has no alias '{alias.Trim()}'. Its aliases: " +
                (target.Aliases.Count == 0 ? "none" : string.Join(", ", target.Aliases.Select(a => $"'{a}'"))) +
                (target.KnownAs.Count == 0 ? "." : $"; names readers learn later (known_as): {string.Join(", ", target.KnownAs.Select(k => $"'{k.Value}'"))}."));
            var oldAlias = slot.Text;

            // Only where readers learn it changes: the name moves between the keys, or its from, and no link changes.
            if (newAlias is null && learned is not null)
            {
                return Relearned(target, slot, learned);
            }

            if (newAlias == oldAlias && learned is null)
            {
                return null;
            }

            var sameForLinks = newAlias is not null && LinkText.Matches(oldAlias, newAlias);
            if (newAlias is not null && !sameForLinks
                && (LinkText.Matches(target.Name, newAlias) || target.AllAliases.Any(a => LinkText.Matches(a, newAlias))))
            {
                throw new DenToolException(
                    ToolErrorCodes.BadArgument,
                    $"'{target.Name}' already goes by '{newAlias}' ({(LinkText.Matches(target.Name, newAlias) ? "its name" : "another alias")}). " +
                    $"To drop '{oldAlias}', call change_alias without 'new_alias', which points its links at the name.");
            }

            var keep = keepOldAlias && !sameForLinks;

            // The new alias goes in the key 'from' says, or the old one's, with the old one's from.
            var toKnown = learned is null ? slot.IsKnownAs : learned.Length > 0;
            var newFrom = learned is { Length: > 0 } ? learned : slot.From;
            var notes = new List<string>();
            MarkdownFile Edit(MarkdownFile f)
            {
                if (newAlias is null)
                {
                    return slot.Remove(f, notes);
                }

                if (!keep && toKnown == slot.IsKnownAs && (newFrom == slot.From || !toKnown))
                {
                    return slot.Rename(f, newAlias);
                }

                if (!keep && toKnown == slot.IsKnownAs)
                {
                    return slot.Retime(slot.Rename(f, newAlias), newFrom);
                }

                var added = keep ? f : slot.Remove(f, notes);
                return NameSlot.Add(added, newAlias, toKnown, toKnown ? newFrom : null);
            }

            return new TextChange(
                LinkMatchKind.Alias, oldAlias, newAlias, keep || sameForLinks ? null : newAlias ?? target.Name,
                f => Apply(() => Edit(f)),
                Kept: keep ? oldAlias : null,
                NewAlias: newAlias,
                SameForLinks: sameForLinks,
                KeepHint: newAlias is null
                    ? "Leave the alias as it is."
                    : "To make the change, pass keep_old_alias: true to add the new alias beside the old one instead, which leaves those links as they are.",
                LostHint: newAlias is null
                    ? "keep the alias, or change those links to the entity's id or file name first"
                    : "choose another alias, or pass keep_old_alias: true to add the new alias beside the old one instead, which leaves those links as they are",
                Notes: notes)
            {
                Learned = learned is null ? null : Learning(newAlias ?? oldAlias, toKnown ? newFrom : null, slot),
            };
        });
    }

    /// <summary>A change to where readers learn a name, with no other change and no link rewritten.</summary>
    private TextChange? Relearned(CatalogEntity target, NameSlot slot, string learned)
    {
        if (learned.Length == 0 ? !slot.IsKnownAs : slot.IsKnownAs && slot.From == learned)
        {
            return null;
        }

        var notes = new List<string>();

        // The same name may be in aliases too (an F-ENT-10 warning): then it only leaves known_as.
        var alsoPlain = NameSlot.FindPlain(target.File, slot.Text) is not null;
        MarkdownFile Edit(MarkdownFile f) =>
            learned.Length == 0 ? (alsoPlain ? slot.Remove(f, notes) : NameSlot.Add(slot.Remove(f, notes), slot.Text, false, null))
            : slot.IsKnownAs ? slot.Retime(f, learned)
            : NameSlot.Add(slot.Remove(f, notes), slot.Text, true, learned);
        return new TextChange(
            LinkMatchKind.Alias, slot.Text, slot.Text, null, f => Apply(() => Edit(f)), Kept: null, NewAlias: null, SameForLinks: true,
            KeepHint: "Leave the alias as it is.", LostHint: "leave the alias as it is", Notes: notes)
        {
            Learned = Learning(slot.Text, learned.Length == 0 ? null : learned, slot),
        };
    }

    /// <summary>What a change to where readers learn a name means, as an effect.</summary>
    private static string Learning(string name, string? from, NameSlot slot) => from is null
        ? $"'{name}' is a plain alias now, known wherever the note is: a read at any story position lists it and finds the note by it." +
          (slot.IsKnownAs ? " If it's a secret, that gives it away; put it back in known_as with a from." : "")
        : $"'{name}' is a name readers learn at {from} now (known_as): a read at an earlier position doesn't know it, and shows a link written by it as the note's name.";

    /// <summary>Refuses a <c>known_as</c> <c>from</c> that isn't a position the universe has (F-ENT-10, F-POS-4).</summary>
    private void CheckKnownFrom(Universe universe, string name, string from)
    {
        if (!Wolfden.Core.Positions.StoryPosition.TryParse(from, out var position, out var error))
        {
            throw new DenToolException(
                ToolErrorCodes.BadPosition,
                $"'from' for '{name}' isn't a story position: {error.Message} Give one such as {PositionExample(universe)}, or plain: true for a plain alias.");
        }

        if (universe.Resolve(position).Problem is { } problem)
        {
            throw Unresolvable(
                $"'from' for '{name}'", from, universe, problem,
                "A name whose from doesn't resolve is known at no position, so nothing was changed.");
        }
    }

    /// <summary>
    /// Where one of an entity's other names is written: an item of <c>aliases</c>, or an entry of
    /// <c>known_as</c> (F-ENT-10), in a list or on its own.
    /// </summary>
    /// <param name="Path">The item's path: <c>aliases.2</c>, <c>known_as.0</c>, or <c>known_as</c> for a single entry.</param>
    /// <param name="Text">The name as written.</param>
    /// <param name="IsKnownAs">True for a <c>known_as</c> entry.</param>
    /// <param name="From">A <c>known_as</c> entry's <c>from</c> as written, or null.</param>
    /// <param name="Bare">True for a <c>known_as</c> item written as a plain string, with no <c>from</c> (a warning, F-ENT-10).</param>
    private sealed record NameSlot(IReadOnlyList<string> Path, string Text, bool IsKnownAs, string? From, bool Bare = false)
    {
        /// <summary>The slot of the name matching <paramref name="text"/> (F-LINK-4): the <c>known_as</c> entry that counts, else an alias.</summary>
        public static NameSlot? Find(CatalogEntity entity, string text)
        {
            var file = entity.File;
            if (entity.KnownNameMatching(text) is { } known)
            {
                switch (file.GetFrontmatterValue("known_as"))
                {
                    case YamlMapping single:
                        return new NameSlot(["known_as"], known.Value, true, known.From?.ToString());
                    case YamlSequence list:
                        // By value, as links match it (F-LINK-4): a flow list has every item on one line. Of a name
                        // listed twice, the entry that counts, with the from readers learn it at.
                        var matching = list.Items
                            .Select((item, i) => (Item: item, Index: i, Value: ValueOf(item)))
                            .Where(x => x.Value is not null && LinkText.Matches(x.Value, known.Value))
                            .ToList();
                        if (matching.Count > 0)
                        {
                            var counted = matching.FirstOrDefault(x => FromOf(x.Item) == known.From?.ToString() && x.Item.Line == known.Line);
                            var at = counted.Item is not null ? counted : matching.FirstOrDefault(x => FromOf(x.Item) == known.From?.ToString());
                            at = at.Item is not null ? at : matching[0];
                            return new NameSlot(["known_as", Index(at.Index)], at.Value!, true, FromOf(at.Item), at.Item is YamlScalar);
                        }

                        break;
                }
            }

            return FindPlain(file, text);
        }

        /// <summary>The slot of a plain alias matching <paramref name="text"/> (F-LINK-4), or null.</summary>
        public static NameSlot? FindPlain(MarkdownFile file, string text)
        {
            var aliases = file.GetFrontmatterValue("aliases") as YamlSequence;
            var index = aliases?.Items.ToList().FindIndex(i => i is YamlScalar { IsNull: false } s && LinkText.Matches(s.Text, text)) ?? -1;
            return index < 0 ? null : new NameSlot(["aliases", Index(index)], ((YamlScalar)aliases!.Items[index]).Text, false, null);
        }

        private static string? ValueOf(YamlNode item) => item switch
        {
            YamlScalar { IsNull: false } s => s.Text,
            YamlMapping m when m["value"] is YamlScalar { IsNull: false } v => v.Text,
            _ => null,
        };

        private static string? FromOf(YamlNode item) => item is YamlMapping m && m["from"] is YamlScalar { IsNull: false } f ? f.Text : null;

        /// <summary>The name written as <paramref name="name"/>, in place.</summary>
        public MarkdownFile Rename(MarkdownFile file, string name) =>
            file.WithFrontmatterValue(IsKnownAs && !Bare ? [.. Path, "value"] : Path, YamlScalarValue.String(name));

        /// <summary>The <c>known_as</c> entry with <paramref name="from"/> as its from, in place; a bare name becomes an entry.</summary>
        public MarkdownFile Retime(MarkdownFile file, string? from)
        {
            if (from is null)
            {
                return file;
            }

            if (Bare)
            {
                var name = file.GetFrontmatterValue(Path) is YamlScalar s ? s.Text : Text;
                return file.WithFrontmatterValue(Path, NewEntity.KnownAsList([new(name, from)]).Single());
            }

            return file.WithFrontmatterValue([.. Path, "from"], YamlScalarValue.String(from));
        }

        /// <summary>The name taken out of its key.</summary>
        public MarkdownFile Remove(MarkdownFile file, List<string> notes) =>
            NoteEmptied(notes, file.WithoutFrontmatterValue(Path, EmptiedList.Null, out var comments), comments, Path[0]);

        /// <summary>
        /// <paramref name="name"/> added as a plain alias, or with <paramref name="known"/> as a <c>known_as</c> entry with
        /// <paramref name="from"/>; with no from, it's known at no position, as the entry it's kept beside (F-ENT-10 fails closed).
        /// </summary>
        public static MarkdownFile Add(MarkdownFile file, string name, bool known, string? from)
        {
            if (!known)
            {
                return file.WithNewListItem(["aliases"], YamlScalarValue.String(name), null, inTemplateOrder: true);
            }

            if (file.GetFrontmatterValue("known_as") is YamlMapping)
            {
                throw new NotSupportedException("known_as is a single entry, not a list, so another can't be added in place. Write it as a list first.");
            }

            var entry = from is null ? new YamlMappingValue { { "value", YamlScalarValue.String(name) } } : NewEntity.KnownAsList([new(name, from)]).Single();
            return file.WithNewListItem(["known_as"], entry, null, inTemplateOrder: true);
        }

        private static string Index(int i) => i.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A change to a name or an alias.
    /// </summary>
    /// <param name="Kind">Whether it's the name or an alias.</param>
    /// <param name="OldText">The text as it is now.</param>
    /// <param name="NewText">The new text, or null for a removed alias.</param>
    /// <param name="LinkText">The target links that used the old text get, or null to leave them.</param>
    /// <param name="Edit">The edit of the entity's own file.</param>
    /// <param name="Kept">The old text, when it stays (as an alias).</param>
    /// <param name="NewAlias">For an alias: the new alias, or null if it's removed.</param>
    /// <param name="SameForLinks">True if the new text matches the old for links (F-LINK-4), so links needn't change.</param>
    /// <param name="KeepHint">What to do instead when a link to rewrite is in a final book, as a sentence.</param>
    /// <param name="LostHint">What to do instead when a rewritten link wouldn't reach the entity.</param>
    /// <param name="DroppedAlias">For a rename to one of the entity's own aliases: that alias, taken out of <c>aliases</c>.</param>
    /// <param name="Notes">Effects the edit of the entity's own file adds as it's made, such as comment lines that went with an emptied list.</param>
    private sealed record TextChange(
        LinkMatchKind Kind, string OldText, string? NewText, string? LinkText, Func<MarkdownFile, MarkdownFile> Edit, string? Kept, string? NewAlias, bool SameForLinks,
        string KeepHint, string LostHint, string? DroppedAlias = null, List<string>? Notes = null)
    {
        /// <summary>What a change to where readers learn the name means (F-ENT-10), or null if it doesn't change.</summary>
        public string? Learned { get; init; }

        /// <summary>For a rename to one of the entity's own <c>known_as</c> names: that name and its from, taken out of <c>known_as</c>.</summary>
        public (string Name, string? From)? DroppedKnown { get; init; }
    }

    private WriteResult ChangeText(string entity, string? universe, string hash, bool allowRelinks, Func<CatalogEntity, TextChange?> change)
    {
        var (path, ids) = Locate(entity, universe);
        using (EnterWrite())
        {
            var (den, structure, catalog, links) = LoadWholeDen();
            var target = catalog.FindByPath(path) ?? throw (den.FileExists(path)
                ? new DenToolException(
                    ToolErrorCodes.InvalidEdit, $"{path} has errors now, so the den leaves it out and it can't be edited here. {ToolWords.Cap(Words.CheckFile(path))} to see them.")
                : Gone(path));
            var original = target.File.ToBytes();
            CheckHash(path, original, hash);
            CheckNotFrozen(target);
            if (change(target) is not { } c)
            {
                return Unchanged(path, original, target);
            }

            var what = c.Kind == LinkMatchKind.Name ? "name" : "alias";
            EntityRenamePlan plan;
            try
            {
                plan = EntityRename.Plan(links, den, target, c.Kind, c.OldText, c.LinkText, c.Edit);
            }
            catch (ArgumentException e)
            {
                throw new DenToolException(ToolErrorCodes.BadArgument, e.Message.Split(" (Parameter", 2)[0]);
            }
            catch (Exception e) when (e is NotSupportedException or InvalidOperationException or EditVerificationException)
            {
                throw new DenToolException(ToolErrorCodes.UnsupportedEdit, $"The change can't be made in place: {e.Message}");
            }

            if (plan.FrozenPaths.Count > 0)
            {
                throw new DenToolException(
                    ToolErrorCodes.Frozen,
                    $"Links that use the {what} '{c.OldText}' are in files of a final (frozen) book, which can't be edited until it's unfrozen (F-BOOK-1): " +
                    $"{string.Join(", ", plan.FrozenPaths)}. Rewriting them is the only way they'd follow, so nothing was changed. {c.KeepHint} {Unfreezing(structure, plan.FrozenPaths)}");
            }

            var files = plan.Files.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            if (files[path].AsSpan().SequenceEqual(original) && files.Count == 1)
            {
                return Unchanged(path, original, target);
            }

            var idAdded = target.Id is null ? AddId(files, path, ids) : null;
            var check = Check(den, structure, files);

            // How every link resolves afterwards: a rewritten link must still reach the entity,
            // and any other link that resolves differently is said (F-LINK-6, F-LINK-7).
            var overlay = new OverlayDenSource(den, files);
            var afterLinks = LinkIndex.Build(EntityCatalog.Load(check.Structure, overlay), overlay);
            var changed = LinkChanges.Between(links, afterLinks);
            var rewritten = plan.Rewritten.ToHashSet();
            var lost = changed.Where(l => rewritten.Contains(l.Before)).ToList();
            if (lost.Count > 0)
            {
                throw new DenToolException(
                    ToolErrorCodes.BadArgument,
                    $"{lost.Count} link(s) that use '{c.OldText}' would be rewritten to [[{c.LinkText}]], which wouldn't reach '{target.Name}': " +
                    $"{string.Join("; ", lost.Take(ListedLinkChanges).Select(l => $"{Where(l.After)} would {Outcome(l.After)}"))}" +
                    $"{(lost.Count > ListedLinkChanges ? $"; and {lost.Count - ListedLinkChanges} more" : "")}. Nothing was changed. " +
                    $"To make the change, {c.LostHint}.");
            }

            // Links that weren't rewritten but resolve differently now are reported below. Those that
            // reached another entity and wouldn't any more (a name beats that entity's alias, say), or
            // reached this one and would reach another (a kept alias loses to another entity's name),
            // refuse the change unless allow_relinks says to go ahead (maintainer decision, 2026-10-26).
            // A link that was ambiguous or broken and now resolves, or one of this entity's that
            // becomes ambiguous, is only reported.
            var moved = changed.Where(l => !rewritten.Contains(l.Before)).ToList();
            var relinked = moved.Where(l => l.Before.Outcome == Core.Links.LinkOutcome.Resolved
                && (l.Before.Target!.Path != path || (l.After.Outcome == Core.Links.LinkOutcome.Resolved && l.After.Target!.Path != path))).ToList();
            if (relinked.Count > 0 && !allowRelinks)
            {
                throw new DenToolException(
                    ToolErrorCodes.BadArgument,
                    $"{relinked.Count} link(s) would change which entity they reach: " +
                    $"{string.Join("; ", relinked.Take(ListedLinkChanges).Select(l => $"{Where(l.After)}{(InFinalBook(l.Before) ? " (in a final book)" : "")} reaches {Reached(l.Before)} and would {Outcome(l.After)}"))}" +
                    $"{(relinked.Count > ListedLinkChanges ? $"; and {relinked.Count - ListedLinkChanges} more" : "")}. Nothing was changed. " +
                    "Choose other text, link those by id or file name first, or pass allow_relinks: true to make the change anyway.");
            }

            // Relinks in a final book: allowed with allow_relinks for now. To always refuse them,
            // set RefuseRelinksInFinalBooks to true.
            var frozenRelinks = relinked.Where(l => InFinalBook(l.Before)).ToList();
            if (frozenRelinks.Count > 0 && RefuseRelinksInFinalBooks)
            {
                throw new DenToolException(
                    ToolErrorCodes.Frozen,
                    $"{frozenRelinks.Count} link(s) in a final (frozen) book reach another entity and would change target (F-BOOK-1): " +
                    $"{string.Join("; ", frozenRelinks.Take(ListedLinkChanges).Select(l => $"{Where(l.After)} reaches {Reached(l.Before)}"))}. Nothing was changed. Choose other text.");
            }

            RefuseUnreadable(den);
            Save(files, plan.Originals, created: null);
            var after = EntityCatalog.ReadEntityFile(check.Structure, path, files[path]).Entity!;
            var ownMoved = moved.Any(l => l.Before.Target?.Path == path);
            var effects = new List<string> { Said(c, plan, target, files.Count, ownMoved) };
            if (c.Learned is { } learned)
            {
                effects.Add(learned);
            }

            effects.AddRange(c.Notes ?? []);
            if (c.Kind == LinkMatchKind.Name)
            {
                effects.Add($"The file keeps its name, {path[(path.LastIndexOf('/') + 1)..]}, so links by its file name still reach it; renaming the file is a separate change.");
                if (target.File.Title is { } title && title.Trim() == c.OldText)
                {
                    effects.Add($"Its title heading, '# {title}', now reads '# {c.NewText}' too.");
                }

                if (c.Kept is { } kept)
                {
                    effects.Add(
                        $"'{kept}' is a plain alias now, with no from, so readers know it wherever they know the note, at every story position. " +
                        "If readers should learn it partway through, give it a from, which makes it a known_as name.");
                }

                if (c.DroppedAlias is { } dropped)
                {
                    effects.Add($"'{dropped}' was one of its aliases; it's the name now, so it was taken out of aliases. Links that use it still reach it, by name now.");
                }

                if (c.DroppedKnown is { } known)
                {
                    effects.Add(
                        $"'{known.Name}' was a name readers learn {(known.From is { } at ? $"at {at}" : "at no position")} (known_as); it's the name now, so it was taken out of known_as, " +
                        "and readers know it wherever they know the note, at every position. Links that use it still reach it, by name now.");
                }
            }

            var name = c.Kind == LinkMatchKind.Name ? c.NewText : null;
            foreach (var text in new[] { name, c.Kept, c.NewAlias }.Where(t => t is not null).Distinct())
            {
                effects.AddRange(TextEffects(afterLinks.Catalog, after, text!, isName: text == name));
            }

            effects.AddRange(moved.Take(ListedLinkChanges).Select(l => $"{Where(l.After)} {Now(l.After)}; before, it {Was(l.Before)}."));
            if (moved.Count > ListedLinkChanges)
            {
                effects.Add($"And {moved.Count - ListedLinkChanges} more link(s) resolve differently now. {ToolWords.Cap(Words.CheckDen)} to see them.");
            }

            if (plan.SkippedPaths.Count > 0)
            {
                effects.Add(
                    $"These files have errors, so the den leaves them out and they weren't edited, but their text has links by '{c.OldText}', which may be links to it: " +
                    $"{string.Join(", ", plan.SkippedPaths)}. Once they're fixed, check those links and change the ones to it to '{c.LinkText}' by hand.");
            }

            if (idAdded is not null)
            {
                effects.Add($"The file had no id, so it was given {idAdded} (F-ID-6).");
            }

            return new WriteResult(
                DenReader.ResultVersion, true, path, HashOf(files[path]), Summary(after), effects, [], Warnings(check),
                files.OrderBy(p => p.Key == path ? 0 : 1).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => new WrittenFileView(p.Key, HashOf(p.Value))).ToList(),
                IdAdded: idAdded);
        }
    }

    /// <summary>Gives the entity's planned file a new id, if it can be added in place (F-ID-6).</summary>
    private string? AddId(Dictionary<string, byte[]> files, string path, HashSet<string> ids)
    {
        var file = MarkdownFile.Parse(files[path], requireFrontmatter: true);
        var id = AddId(ref file, ids);
        files[path] = file.ToBytes();
        return id;
    }

    /// <summary>The first effect: what changed, and what became of the links.</summary>
    private static string Said(TextChange c, EntityRenamePlan plan, CatalogEntity target, int files, bool ownMoved)
    {
        var unlessSaid = ownMoved ? ", except those listed below" : "";
        var what = c.Kind == LinkMatchKind.Name ? "name" : "alias";
        if (c.Learned is not null && c.NewText == c.OldText)
        {
            return $"Changed where readers learn '{c.OldText}'. No link was changed: a link reaches the note by it at every position.";
        }

        var change = c.Kind == LinkMatchKind.Name ? $"Renamed '{c.OldText}' to '{c.NewText}'"
            : c.Kept is not null ? $"Added the alias '{c.NewText}' beside '{c.OldText}'"
            : c.NewText is null ? $"Removed the alias '{c.OldText}'"
            : $"Changed the alias '{c.OldText}' to '{c.NewText}'";
        if (c.SameForLinks)
        {
            return $"{change}. Links match it ignoring case, spacing, and which apostrophe is used (F-LINK-4), so the links that use '{c.OldText}' still reach it, and none were changed.";
        }

        if (c.Kept is not null)
        {
            return c.Kind == LinkMatchKind.Name
                ? $"{change}, and kept '{c.OldText}' as an alias, so links that use it still reach it, by alias now{unlessSaid}; none were changed."
                : $"{change}, so links that use either reach it{unlessSaid}; none were changed.";
        }

        return plan.LinksRewritten == 0
            ? $"{change}. No links used the old {what}."
            : $"{change}, and rewrote {plan.LinksRewritten} link(s) that used it to [[{c.LinkText}]]{(c.Kind == LinkMatchKind.Alias && c.NewText is null ? ", its name" : "")} " +
              $"(files written: {files}).";
    }

    /// <summary>
    /// What the new name, or a new or kept alias, means for links from now
    /// on: another entity of the universe with that text as its slug, name,
    /// or an alias (F-LINK-6, F-LINK-7).
    /// </summary>
    private static IEnumerable<string> TextEffects(EntityCatalog catalog, CatalogEntity entity, string text, bool isName)
    {
        var mine = isName ? "name" : "alias";
        foreach (var other in catalog.Entities.Where(e => e.Universe.Key == entity.Universe.Key && e.Path != entity.Path))
        {
            var tier = other.Slug is { } slug && LinkText.Matches(slug, text) ? "slug"
                : LinkText.Matches(other.Name, text) ? "name"
                : other.AllAliases.Any(a => LinkText.Matches(a, text)) ? "alias" : null;
            if (tier is null)
            {
                continue;
            }

            yield return tier == "slug"
                ? $"'{text}' is also the file name of '{other.Name}' ({other.Path}), so a link [[{text}]] reaches '{other.Name}': a file name matches before {(mine == "alias" ? "an alias" : "a name")} (F-LINK-6)."
                : tier == mine
                ? $"'{text}' is also the {tier} of '{other.Name}' ({other.Path}), so a link [[{text}]] is ambiguous between them unless one is in a nearer scope (F-LINK-7)."
                : tier == "name"
                ? $"'{text}' is also the name of '{other.Name}' ({other.Path}), so a link [[{text}]] reaches '{other.Name}': a name matches before an alias (F-LINK-6)."
                : $"'{text}' is also an alias of '{other.Name}' ({other.Path}), so a link [[{text}]] reaches '{entity.Name}': a name matches before an alias (F-LINK-6).";
        }
    }

    private static string Where(ResolvedLink link) => $"{link.Occurrence.Link.Text} in {link.Occurrence.Path} (line {link.Occurrence.Link.Line})";

    private static string Outcome(ResolvedLink link) => link.Outcome switch
    {
        Core.Links.LinkOutcome.Resolved => $"reach '{link.Target!.Name}' ({link.Target.Path})",
        Core.Links.LinkOutcome.Ambiguous => $"be ambiguous: {link.Resolution.Problem?.TrimEnd('.')}",
        _ => "be broken",
    };

    private static bool InFinalBook(ResolvedLink link) => link.Occurrence.Scope is Book { Status: BookStatus.Final };

    private static string Reached(ResolvedLink link) => $"'{link.Target!.Name}' ({link.Target.Path})";

    private static string Now(ResolvedLink link) => link.Outcome switch
    {
        Core.Links.LinkOutcome.Resolved => $"now reaches '{link.Target!.Name}' ({link.Target.Path})",
        Core.Links.LinkOutcome.Ambiguous => $"is now ambiguous (F-LINK-7): {link.Resolution.Problem?.TrimEnd('.')}",
        _ => "is now broken (F-LINK-8)",
    };

    private static string Was(ResolvedLink link) => link.Outcome switch
    {
        Core.Links.LinkOutcome.Resolved => $"reached '{link.Target!.Name}' ({link.Target.Path})",
        Core.Links.LinkOutcome.Ambiguous => "was ambiguous",
        _ => "was broken",
    };
}

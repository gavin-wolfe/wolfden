using System.Text.RegularExpressions;
using Wolfden.Core.Documents;
using Wolfden.Core.Positions;
using Wolfden.Core.Structure;
using Wolfden.Core.Yaml;

namespace Wolfden.Core.Entities;

/// <summary>
/// The entities of a den: every entity file under an <c>entities/</c>
/// folder of a loaded container (format.md F-LAY-5), read into
/// <see cref="CatalogEntity"/> records. It's the in-memory model that link
/// resolution works against and that the index can persist. It never
/// writes (F-ID-6).
/// </summary>
/// <remarks>
/// <para>
/// An entity file with an error is left out (F-GEN-2), and the error is in
/// <see cref="Diagnostics"/>. The catalog checks what decides whether an
/// entity loads and how links find it: the file's parse errors, a bad
/// <c>from</c> or <c>until</c>, <c>id</c> (F-ID-1), <c>type</c> (F-ENT-2),
/// <c>name</c> (F-ENT-3), <c>aliases</c> (F-ENT-4), <c>status</c> (F-ENT-5),
/// and the shape of <c>fields</c> and their entries (F-ENT-7, F-FLD-1,
/// F-TMP-1, F-TMP-2, F-STAT-1, F-PIN-1). It also reports the warnings that
/// change how links reach an entity: a missing id (F-ID-2), a duplicate id
/// (F-ID-3), and a file name that isn't a valid slug (F-KEY-4).
/// </para>
/// <para>
/// Each entity's type is looked up among the den's enabled types
/// (<see cref="CatalogEntity.Definition"/>). Checks against types and
/// checks that only warn, such as unknown types, missing required fields
/// and sections, field values that don't match their field types (F-FLD-2,
/// F-FLD-3), and positions that don't resolve, are made by
/// <see cref="Checks.DenCheck"/> (<c>wolfden check</c>).
/// </para>
/// </remarks>
public sealed partial class EntityCatalog
{
    private readonly List<CatalogEntity> _entities = [];
    private readonly List<DenDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, CatalogEntity> _byPath = new(StringComparer.Ordinal);
    private readonly List<ExcludedEntity> _excluded = [];

    /// <summary>
    /// The entities whose id another file shares (F-ID-3). It's held here,
    /// not on the entities, because <see cref="Update"/> shares entity
    /// objects between catalogs, and each catalog has its own answer.
    /// </summary>
    private readonly HashSet<CatalogEntity> _duplicateIds = new(ReferenceEqualityComparer.Instance);

    /// <summary>What reading each entity file gave, by path, so <see cref="Update"/> can keep the files that didn't change.</summary>
    private readonly Dictionary<string, FileResult> _files = new(StringComparer.Ordinal);

    private readonly List<Container> _containers;

    private EntityCatalog(DenStructure structure)
    {
        Structure = structure;
        _containers = structure.Universes
            .SelectMany(u => new Container[] { u }.Concat(u.Series).Concat(u.Books))
            .ToList();
    }

    /// <summary>The den's structure, which the entities' scopes and positions refer to.</summary>
    public DenStructure Structure { get; }

    /// <summary>Every entity that loaded, by path in ASCII order.</summary>
    public IReadOnlyList<CatalogEntity> Entities => _entities;

    /// <summary>Problems with entity files, by path and then line.</summary>
    public IReadOnlyList<DenDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Loads the den whose root folder is <paramref name="root"/>: its structure, then its entities.</summary>
    public static EntityCatalog Load(string root) => Load(FileSystemDenSource.ForViews(root));

    /// <summary>Loads a den's structure, then its entities.</summary>
    public static EntityCatalog Load(IDenSource source) => Load(DenStructure.Load(source), source);

    /// <summary>Loads the entities of a den whose structure is already loaded.</summary>
    public static EntityCatalog Load(DenStructure structure, IDenSource source) => Load(structure, source, null);

    /// <summary>
    /// Loads the entities of a den whose structure is already loaded, when
    /// <paramref name="source"/> shows only part of it (a check of one
    /// universe): <paramref name="idsElsewhere"/> gives, for each id, the
    /// paths of the entity files outside that part that use it. They take
    /// part in F-ID-3 as if they'd been read, so an id shared with a file
    /// outside is a duplicate here too, with the same warning, and links
    /// don't resolve to it, as in a load of the whole den.
    /// </summary>
    public static EntityCatalog Load(DenStructure structure, IDenSource source, IReadOnlyDictionary<string, IReadOnlyList<string>>? idsElsewhere)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(source);
        var catalog = new EntityCatalog(structure) { _idsElsewhere = idsElsewhere ?? new Dictionary<string, IReadOnlyList<string>>() };
        catalog.LoadEntities(source);
        return catalog;
    }

    /// <summary>For each id, the paths of entity files outside what this catalog read that use it (<see cref="Load(DenStructure, IDenSource, IReadOnlyDictionary{string, IReadOnlyList{string}}?)"/>).</summary>
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _idsElsewhere = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>The entities of <paramref name="universe"/>, at every scope.</summary>
    public IEnumerable<CatalogEntity> InUniverse(Universe universe) => _entities.Where(e => e.Universe == universe);

    /// <summary>The entity whose file is at <paramref name="path"/>, or null.</summary>
    public CatalogEntity? FindByPath(string path) => _byPath.GetValueOrDefault(path);

    /// <summary>
    /// True if another entity or container file in this catalog has the
    /// same id as <paramref name="entity"/> (F-ID-3). The entity is still
    /// listed, but links don't resolve to it.
    /// </summary>
    public bool HasDuplicateId(CatalogEntity entity) => _duplicateIds.Contains(entity);

    /// <summary>Entity files left out for errors, with the texts a link could have matched them by.</summary>
    internal IReadOnlyList<ExcludedEntity> ExcludedEntities => _excluded;

    /// <summary>
    /// The catalog after some files changed, for a file watcher (#14): the
    /// same as <see cref="Load(DenStructure, IDenSource)"/> on the den as it
    /// is now, but it reads only <paramref name="changedPaths"/> again and
    /// keeps what it read for every other entity file. The den's structure
    /// must be unchanged; a change to <c>den.yaml</c>, a pack, or a
    /// container file needs a full load.
    /// </summary>
    /// <param name="source">The den as it is now.</param>
    /// <param name="changedPaths">
    /// Every path that may have been added, changed, or deleted since this
    /// catalog was loaded. Paths that aren't entity files (see
    /// <see cref="EntityFileScope"/>) are ignored.
    /// </param>
    /// <remarks>
    /// Entities of files that didn't change are the same objects in both
    /// catalogs. Neither changes the other: what can differ between them,
    /// such as <see cref="HasDuplicateId"/>, is held by each catalog.
    /// </remarks>
    public EntityCatalog Update(IDenSource source, IEnumerable<string> changedPaths)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(changedPaths);
        var catalog = new EntityCatalog(Structure) { _idsElsewhere = _idsElsewhere };
        foreach (var (path, result) in _files)
        {
            catalog._files[path] = result;
        }

        foreach (var path in changedPaths)
        {
            catalog._files.Remove(path);
            if (EntityFileScope(Structure, path) is { } scope && ReadFile(path, source.TryReadFile(path), scope, catalog._containers.IndexOf(scope)) is { } result)
            {
                catalog._files[path] = result;
            }
        }

        catalog.Assemble();
        return catalog;
    }

    /// <summary>
    /// The container whose entity file <paramref name="path"/> would be if
    /// it exists: a <c>.md</c> file at any depth under the <c>entities/</c>
    /// folder of a container that loaded (F-LAY-5), with no hidden name on
    /// the way (F-LAY-2). Null for any other path. It's how <see cref="Load(DenStructure, IDenSource)"/>
    /// finds entity files, for one path.
    /// </summary>
    public static Container? EntityFileScope(DenStructure structure, string path)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(path);
        if (!path.EndsWith(".md", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var universe in structure.Universes)
        {
            foreach (var container in new Container[] { universe }.Concat(universe.Series).Concat(universe.Books))
            {
                var folder = $"{container.Path}/entities/";
                if (path.StartsWith(folder, StringComparison.Ordinal))
                {
                    var rest = path[folder.Length..].Split('/');
                    return rest.All(name => name.Length > 0 && !name.StartsWith('.')) ? container : null;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reads one entity file from <paramref name="bytes"/> as a load of the
    /// den would, without reading anything else: the same checks, so the
    /// same errors (F-GEN-2) and the same warnings that depend only on the
    /// file. A writer checks a file it's about to save with this, so it never
    /// saves one the den would leave out (format.md §1.3). Checks that span
    /// files, such as duplicate ids (F-ID-3), aren't made.
    /// </summary>
    /// <param name="structure">The den's structure.</param>
    /// <param name="path">Where the file is or would be: under a loaded container's <c>entities/</c> folder (<see cref="EntityFileScope"/>).</param>
    /// <param name="bytes">The file's contents.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> isn't where an entity file goes.</exception>
    public static EntityFileRead ReadEntityFile(DenStructure structure, string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bytes);
        var scope = EntityFileScope(structure, path) ?? throw new ArgumentException($"'{path}' isn't under the entities folder of a container that loaded.", nameof(path));
        var diagnostics = new List<Diagnostic>();
        var entity = Read(path, bytes, scope, diagnostics, []);
        MarkExcluded(entity, diagnostics);
        return new EntityFileRead(entity, diagnostics.OrderBy(d => d.Line ?? 0).ToList());
    }

    /// <summary>In a file left out whole, a bad section comment leaves out no more than the file (format.md F-GEN-2).</summary>
    private static void MarkExcluded(CatalogEntity? entity, List<Diagnostic> diagnostics)
    {
        if (entity is null)
        {
            for (var i = 0; i < diagnostics.Count; i++)
            {
                diagnostics[i] = diagnostics[i].InExcludedFile();
            }
        }
    }

    private void LoadEntities(IDenSource source)
    {
        for (var i = 0; i < _containers.Count; i++)
        {
            var container = _containers[i];
            foreach (var path in EntityFiles(source, $"{container.Path}/entities"))
            {
                if (ReadFile(path, source.TryReadListedFile(path), container, i) is { } result)
                {
                    _files[path] = result;
                }
            }
        }

        Assemble();
    }

    /// <summary>Reads one entity file from its <paramref name="bytes"/>, or returns null without them: a file that's gone or can't be read is left out as if it weren't there (#112).</summary>
    private static FileResult? ReadFile(string path, byte[]? bytes, Container scope, int containerOrder)
    {
        if (bytes is null)
        {
            return null;
        }

        var diagnostics = new List<Diagnostic>();
        var excluded = new List<ExcludedEntity>();
        var entity = Read(path, bytes, scope, diagnostics, excluded);
        MarkExcluded(entity, diagnostics);
        return new FileResult(containerOrder, entity, diagnostics, excluded.Count > 0 ? excluded[0] : null);
    }

    /// <summary>
    /// Builds the lists from the files read, in the order a full load reads
    /// them (containers in structure order, then paths in ASCII order), so
    /// a catalog from <see cref="Update"/> is the same as a full load's.
    /// </summary>
    private void Assemble()
    {
        var ordered = _files
            .OrderBy(f => f.Value.ContainerOrder)
            .ThenBy(f => f.Key, StringComparer.Ordinal);
        foreach (var (path, result) in ordered)
        {
            foreach (var d in result.Diagnostics)
            {
                _diagnostics.Add(new DenDiagnostic(path, d));
            }

            if (result.Excluded is { } excluded)
            {
                _excluded.Add(excluded);
            }

            if (result.Entity is { } entity)
            {
                _entities.Add(entity);
                _byPath[path] = entity;
            }
        }

        _entities.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        FindDuplicateIds(_containers);
        var sorted = _diagnostics.OrderBy(d => d.Path, StringComparer.Ordinal).ThenBy(d => d.Diagnostic.Line ?? 0).ToList();
        _diagnostics.Clear();
        _diagnostics.AddRange(sorted);
    }

    /// <summary>What reading one entity file gave.</summary>
    /// <param name="ContainerOrder">Its container's place in the structure, which decides the order of a full load.</param>
    /// <param name="Entity">The entity, or null if the file has errors.</param>
    /// <param name="Diagnostics">The file's own diagnostics.</param>
    /// <param name="Excluded">For a file with errors, what a broken link could have matched it by.</param>
    private sealed record FileResult(int ContainerOrder, CatalogEntity? Entity, IReadOnlyList<Diagnostic> Diagnostics, ExcludedEntity? Excluded);

    /// <summary>Every <c>.md</c> file under <paramref name="folder"/>, at any depth, skipping hidden names (F-LAY-2).</summary>
    private static IEnumerable<string> EntityFiles(IDenSource source, string folder)
    {
        foreach (var name in source.GetFiles(folder))
        {
            if (!name.StartsWith('.') && name.EndsWith(".md", StringComparison.Ordinal))
            {
                yield return $"{folder}/{name}";
            }
        }

        foreach (var name in source.GetDirectories(folder))
        {
            if (!name.StartsWith('.'))
            {
                foreach (var path in EntityFiles(source, $"{folder}/{name}"))
                {
                    yield return path;
                }
            }
        }
    }

    /// <summary>
    /// F-ID-3: an id used by more than one entity or container file is a
    /// warning on each of them, and those entities are left out of link
    /// resolution.
    /// </summary>
    private void FindDuplicateIds(List<Container> containers)
    {
        var owners = _entities.Where(e => e.Id is not null).Select(e => (Id: e.Id!, e.Path, Entity: (CatalogEntity?)e))
            .Concat(containers.Where(c => c.Id is not null).Select(c => (Id: c.Id!, Path: c.FilePath, Entity: (CatalogEntity?)null)))
            .ToList();
        var loaded = owners.Select(o => o.Path).ToHashSet(StringComparer.Ordinal);
        owners.AddRange(_idsElsewhere
            .SelectMany(pair => pair.Value.Where(path => !loaded.Contains(path)).Select(path => (Id: pair.Key, Path: path, Entity: (CatalogEntity?)null))));
        foreach (var group in owners.GroupBy(o => o.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var paths = group.Select(o => o.Path).Order(StringComparer.Ordinal).ToList();
            foreach (var owner in group)
            {
                if (owner.Entity is { } entity)
                {
                    _duplicateIds.Add(entity);
                }

                var line = owner.Entity?.File.Frontmatter?["id"]?.Line;
                _diagnostics.Add(new DenDiagnostic(owner.Path, new Diagnostic("F-ID-3", DiagnosticSeverity.Warning, line,
                    $"The id {group.Key} is used by {paths.Count} files ({string.Join(", ", paths)}). Until it's unique, links that would reach any of them are broken. Give each copy a new id.")));
            }
        }
    }

    private static CatalogEntity? Read(string path, byte[] bytes, Container scope, List<Diagnostic> diagnostics, List<ExcludedEntity> excluded)
    {
        var file = MarkdownFile.Parse(bytes, requireFrontmatter: true);
        diagnostics.AddRange(file.Diagnostics);
        var fm = file.Frontmatter;
        if (fm is null)
        {
            // No frontmatter, or frontmatter that isn't a mapping: already an error.
            return null;
        }

        var errors = new List<Diagnostic>();
        void Error(string rule, int line, string message) => errors.Add(new Diagnostic(rule, DiagnosticSeverity.Error, line, message));

        string? id = null;
        switch (fm["id"])
        {
            case null or YamlScalar { IsNull: true }:
                // --fix doesn't edit a final book's files (F-BOOK-1), so don't suggest it there.
                diagnostics.Add(new Diagnostic("F-ID-2", DiagnosticSeverity.Warning, 1, scope is Book { Status: BookStatus.Final } frozen
                    ? $"no id, and book '{frozen.Key}' is final (frozen), so `wolfden check --fix` won't add one until the book is unfrozen. Until then, links can't reach this entity by id."
                    : "no id; run `wolfden check --fix` to add one. Until then, links can't reach this entity by id."));
                break;
            case YamlScalar s when Keys.IsId(s.Text):
                id = s.Text;
                break;
            case var node:
                Error("F-ID-1", node.Line, $"'{Describe(node)}' isn't a valid id: an id is a ULID, 26 uppercase Crockford base32 characters.");
                break;
        }

        var type = fm["type"] is YamlScalar { IsNull: false } t && t.Text.Trim().Length > 0 ? t.Text : null;
        if (type is null)
        {
            Error("F-ENT-2", fm["type"]?.Line ?? 1, "An entity needs a type, such as 'type: character'.");
        }

        var name = fm["name"] is YamlScalar { IsNull: false } n && n.Text.Trim().Length > 0 ? n.Text : null;
        if (name is null)
        {
            Error("F-ENT-3", fm["name"]?.Line ?? 1, "An entity needs a non-empty name.");
        }

        var aliases = new List<string>();
        switch (fm["aliases"])
        {
            case null or YamlScalar { IsNull: true }:
                break;
            case YamlSequence list:
                foreach (var item in list.Items)
                {
                    if (item is YamlScalar { IsNull: false } a && a.Text.Trim().Length > 0)
                    {
                        aliases.Add(a.Text);
                    }
                    else
                    {
                        Error("F-ENT-4", item.Line, "Each alias must be a non-empty string.");
                    }
                }

                break;
            case var node:
                Error("F-ENT-4", node.Line, "aliases must be a list of strings, such as 'aliases: [Tarrow]', even for one alias.");
                break;
        }

        var knownAs = ReadKnownAs(fm["known_as"], Error, diagnostics);

        var status = FactStatus.Draft;
        switch (fm["status"])
        {
            case null or YamlScalar { IsNull: true }:
                break;
            case YamlScalar s when FactStatuses.Parse(s.Text) is { } parsed:
                status = parsed;
                break;
            case var node:
                Error("F-ENT-5", node.Line,
                    $"'{Describe(node)}' isn't a status. Use idea, undecided, candidate, draft, canon, or retconned.");
                break;
        }

        CheckFields(fm["fields"], Error);

        // Syntax errors in from and until (F-POS-2, F-POS-3, F-POS-6).
        errors.AddRange(PositionScanner.Scan(file).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        diagnostics.AddRange(errors);

        var fileName = path[(path.LastIndexOf('/') + 1)..^3];
        var slug = Keys.IsKey(fileName) ? fileName : null;
        if (slug is null)
        {
            diagnostics.Add(new Diagnostic("F-KEY-4", DiagnosticSeverity.Warning, null,
                $"The file name '{fileName}.md' isn't a valid slug (lowercase letters and digits, with single hyphens between words), so links reach this entity only by id, name, or alias."));
        }

        if (file.HasFileErrors || errors.Count > 0)
        {
            // Kept only so a broken link can say the file it would reach has errors.
            var keys = new List<string>();
            if (fm["id"] is YamlScalar { IsNull: false } rawId)
            {
                keys.Add(rawId.Text);
            }

            keys.AddRange(new[] { slug, fm["name"] is YamlScalar { IsNull: false } rawName ? rawName.Text : null }
                .Concat((fm["aliases"] as YamlSequence)?.Items.OfType<YamlScalar>().Where(a => !a.IsNull).Select(a => a.Text) ?? [])
                .Concat(knownAs.Select(k => k.Value))
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => Links.LinkText.Normalize(k!)));
            excluded.Add(new ExcludedEntity(path, scope.Universe, keys));
            return null;
        }

        StoryPosition? from = null;
        PositionResolution? fromResolution = null;
        if (fm["from"] is YamlScalar { IsNull: false } f)
        {
            from = StoryPosition.Parse(f.Text);
            fromResolution = scope.Universe.Resolve(from);
        }

        var known = Settle(knownAs, name!, aliases, scope.Universe, diagnostics);
        return new CatalogEntity(path, file, scope, id, slug, name!, aliases, type!, status, from, fromResolution, known)
        {
            Definition = scope.Universe.Den.Types.Find(type!),
        };
    }

    /// <summary>A <c>known_as</c> entry as read, before its <c>from</c> is resolved.</summary>
    private sealed record KnownEntry(string Value, StoryPosition? From, int Line)
    {
        public bool? CheckText { get; init; }

        public IReadOnlyList<string> AllowIn { get; init; } = [];
    }

    /// <summary>The keys a <c>known_as</c> entry has a meaning for (F-ENT-10).</summary>
    private static readonly string[] KnownAsKeys = ["value", "from", "check_text", "allow_in"];

    /// <summary>
    /// An entry's <c>check_text</c> (F-ENT-10): a boolean, or null if it has none. Any
    /// other value is a warning and reads as true, so the name is still checked.
    /// </summary>
    private static bool? ReadCheckText(YamlMapping entry, string value, Action<int, string> warn)
    {
        switch (entry["check_text"])
        {
            case null or YamlScalar { IsNull: true }:
                return null;
            case YamlScalar scalar when scalar.AsBoolean() is bool on:
                return on;
            case var node:
                warn(node.Line, $"known_as: check_text of '{value}' is '{Describe(node)}', which isn't true or false, so the name is checked in plain text as if it said true.");
                return true;
        }
    }

    /// <summary>
    /// An entry's <c>allow_in</c> (F-ENT-10): a list of notes, by anything a link
    /// reaches a note by, or one. An item that isn't text is a warning and is left
    /// out, so the name is still checked there.
    /// </summary>
    private static List<string> ReadAllowIn(YamlMapping entry, string value, Action<int, string> warn)
    {
        var items = new List<string>();
        IReadOnlyList<YamlNode> nodes = entry["allow_in"] switch
        {
            null or YamlScalar { IsNull: true } => [],
            YamlSequence list => list.Items,
            YamlScalar one => [one],
            var other => [other],
        };
        foreach (var node in nodes)
        {
            if (node is YamlScalar { IsNull: false } item && item.Text.Trim().Length > 0 && item.Text.IndexOfAny(['\r', '\n']) < 0)
            {
                items.Add(item.Text.Trim());
            }
            else
            {
                warn(node.Line, $"known_as: allow_in of '{value}' has '{Describe(node)}', which isn't a note's slug, name, alias, or id, so it's ignored and the name is still checked everywhere.");
            }
        }

        return items;
    }

    /// <summary>
    /// F-ENT-10: <c>known_as</c> is a list of entries, or a single entry, each a
    /// name and the <c>from</c> where readers learn it. A value that isn't one line
    /// of text is an error. The rest fails closed, with a warning: an entry with no
    /// <c>from</c>, or a plain string, is a name no read at a position knows.
    /// </summary>
    private static List<KnownEntry> ReadKnownAs(YamlNode? node, Action<string, int, string> error, List<Diagnostic> diagnostics)
    {
        var entries = new List<KnownEntry>();
        void Warn(int line, string message) => diagnostics.Add(new Diagnostic("F-ENT-10", DiagnosticSeverity.Warning, line, message));

        IReadOnlyList<YamlNode> items = node switch
        {
            null or YamlScalar { IsNull: true } => [],
            YamlSequence list => list.Items,
            _ => [node],
        };
        foreach (var item in items)
        {
            switch (item)
            {
                case YamlScalar { IsNull: false } plain when IsOneLine(plain.Text):
                    Warn(plain.Line,
                        $"known_as: '{plain.Text}' has no from, so no read at a story position knows it. Write it as {{ value: {plain.Text}, from: \"book:chapter\" }}, or move it to aliases if readers know it wherever the note is.");
                    entries.Add(new KnownEntry(plain.Text, null, plain.Line));
                    break;
                case YamlMapping entry:
                    if (entry["value"] is not YamlScalar { IsNull: false } value || !IsOneLine(value.Text))
                    {
                        error("F-ENT-10", (entry["value"] ?? entry).Line, "Each known_as entry needs a value: the name, as one line of text, such as { value: Mara Voss, from: \"ember:38\" }.");
                        break;
                    }

                    foreach (var other in entry.Entries.Where(e => e.KeyText is not { } key || !KnownAsKeys.Contains(key)))
                    {
                        Warn(other.Key.Line, $"known_as: '{other.KeyText}' isn't a key a known_as entry has, so it's kept and ignored. An entry has a value and a from, and may have check_text and allow_in.");
                    }

                    var checkText = ReadCheckText(entry, value.Text, Warn);
                    var allowIn = ReadAllowIn(entry, value.Text, Warn);

                    StoryPosition? from = null;
                    if (entry["from"] is YamlScalar { IsNull: false } f && StoryPosition.TryParse(f.Text, out var parsed, out _))
                    {
                        from = parsed;
                    }
                    else if (entry["from"] is null or YamlScalar { IsNull: true })
                    {
                        Warn(entry.Line, $"known_as: '{value.Text}' has no from, so no read at a story position knows it. Add where readers learn it, such as from: \"book:chapter\".");
                    }

                    // A from that doesn't parse is already an error (F-POS-3), from the position scan.
                    entries.Add(new KnownEntry(value.Text, from, entry.Line) { CheckText = checkText, AllowIn = allowIn });
                    break;
                default:
                    error("F-ENT-10", item.Line, "known_as must be a list of entries, each a name and where readers learn it, such as '- { value: Mara Voss, from: \"ember:38\" }'.");
                    break;
            }
        }

        return entries;

        static bool IsOneLine(string text) => text.Trim().Length > 0 && text.IndexOfAny(['\r', '\n']) < 0;
    }

    /// <summary>
    /// The <c>known_as</c> entries that count (F-ENT-10), each with its <c>from</c>
    /// resolved. A name that's also the entity's own <c>name</c> is a warning, and
    /// the name wins, since a name can't be hidden. One that's also a plain alias is
    /// a warning, and the entry wins, since hiding is the safe reading: the alias is
    /// taken out of <paramref name="aliases"/>. The same name twice is a warning,
    /// and the entry readers learn first wins.
    /// </summary>
    private static List<KnownName> Settle(List<KnownEntry> entries, string name, List<string> aliases, Universe universe, List<Diagnostic> diagnostics)
    {
        void Warn(int line, string message) => diagnostics.Add(new Diagnostic("F-ENT-10", DiagnosticSeverity.Warning, line, message));
        var resolved = entries
            .Select(e => new KnownName(e.Value, e.From, e.From is { } f ? universe.Resolve(f) : null, e.Line) { CheckText = e.CheckText, AllowIn = e.AllowIn })
            .ToList();
        var kept = new List<KnownName>();
        foreach (var group in resolved.GroupBy(k => Links.LinkText.Normalize(k.Value), StringComparer.Ordinal))
        {
            if (group.Key == Links.LinkText.Normalize(name))
            {
                foreach (var entry in group)
                {
                    Warn(entry.Line, $"known_as: '{entry.Value}' is the note's own name, which readers know wherever the note is, so this entry has no effect.");
                }

                continue;
            }

            // Readers learn it at the earliest from; one that doesn't resolve comes last.
            var first = group.OrderBy(k => k.Point?.Ordinal ?? long.MaxValue).ThenBy(k => k.Line).First();
            foreach (var entry in group.Where(k => !ReferenceEquals(k, first)))
            {
                Warn(entry.Line, $"known_as: '{entry.Value}' is listed more than once, so only the entry on line {first.Line} counts.");
            }

            if (aliases.RemoveAll(a => Links.LinkText.Normalize(a) == group.Key) > 0)
            {
                Warn(first.Line, $"known_as: '{first.Value}' is also in aliases, which would show it wherever the note is. The known_as entry counts, so the name is known only from its from; take it out of aliases.");
            }

            kept.Add(first);
        }

        return kept.OrderBy(k => k.Line).ToList();
    }

    /// <summary>F-ENT-7, F-FLD-1, F-TMP-1, F-TMP-2, and the status and pin of entries (F-STAT-1, F-PIN-1).</summary>
    internal static void CheckFields(YamlNode? fields, Action<string, int, string> error)
    {
        if (fields is null or YamlScalar { IsNull: true })
        {
            return;
        }

        if (fields is not YamlMapping map)
        {
            error("F-ENT-7", fields.Line, "fields must be a mapping from field name to value.");
            return;
        }

        foreach (var entry in map.Entries)
        {
            if (entry.KeyText is not { } fieldName || !FieldName().IsMatch(fieldName))
            {
                error("F-FLD-1", entry.Key.Line,
                    $"'{entry.KeyText}' isn't a valid field name: use lowercase letters, digits, and '_', starting with a letter, up to 64 characters.");
            }

            var value = entry.Value;
            if (value is YamlSequence list)
            {
                var entries = list.Items.Count(IsEntry);
                if (entries > 0 && entries < list.Items.Count)
                {
                    error("F-TMP-1", list.Line, $"'{entry.KeyText}' mixes entries (mappings with a value) and plain items. Make every item an entry, or none.");
                    continue;
                }

                foreach (var item in list.Items.OfType<YamlMapping>().Where(IsEntry))
                {
                    CheckEntry(item, error);
                }
            }
            else if (value is YamlMapping single && IsEntry(single))
            {
                CheckEntry(single, error);
            }
        }
    }

    private static void CheckEntry(YamlMapping entry, Action<string, int, string> error)
    {
        var value = entry["value"];
        if (value is YamlMapping inner && IsEntry(inner) || value is YamlSequence { Items.Count: > 0 } list && list.Items.All(IsEntry))
        {
            error("F-TMP-2", value.Line, "An entry's value can't itself be an entry or a list of entries.");
        }

        switch (entry["status"])
        {
            case null or YamlScalar { IsNull: true }:
                break;
            case YamlScalar s when FactStatuses.Parse(s.Text) is not null:
                break;
            case var node:
                error("F-STAT-1", node.Line, $"'{Describe(node)}' isn't a status. Use idea, undecided, candidate, draft, canon, or retconned.");
                break;
        }

        switch (entry["pin"])
        {
            case null or YamlScalar { IsNull: true }:
                break;
            case YamlScalar s when s.AsBoolean() is not null:
                break;
            case var node:
                error("F-PIN-1", node.Line, $"pin must be true or false, not '{Describe(node)}'.");
                break;
        }
    }

    /// <summary>An entry is a mapping with a <c>value</c> key (F-TMP-1).</summary>
    internal static bool IsEntry(YamlNode node) => node is YamlMapping m && m.ContainsKey("value");

    private static string Describe(YamlNode node) => node switch
    {
        YamlScalar s => s.Text,
        YamlSequence => "a list",
        _ => "a mapping",
    };

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex FieldName();
}

/// <summary>What reading one entity file on its own gave (<see cref="EntityCatalog.ReadEntityFile"/>).</summary>
/// <param name="Entity">The entity, or null if the file has errors.</param>
/// <param name="Diagnostics">The file's diagnostics, errors and warnings, by line.</param>
public sealed record EntityFileRead(CatalogEntity? Entity, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>An entity file left out for errors (F-GEN-2), kept for broken-link hints.</summary>
/// <param name="Path">The file's path.</param>
/// <param name="Universe">The universe it's in.</param>
/// <param name="MatchKeys">Its id as written, and its slug, name, and aliases normalized (F-LINK-4).</param>
internal sealed record ExcludedEntity(string Path, Universe Universe, IReadOnlyList<string> MatchKeys);

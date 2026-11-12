using Wolfden.Core.Documents;
using Wolfden.Core.Links;
using Wolfden.Core.Positions;
using Wolfden.Core.Structure;
using Wolfden.Core.Types;

namespace Wolfden.Core.Entities;

/// <summary>
/// An entity file that loaded without errors (format.md §7, F-GEN-2): what
/// links resolve to and what views list. It's an in-memory record, built
/// from the file each time, that an index can persist.
/// </summary>
public sealed class CatalogEntity
{
    private readonly IReadOnlyList<string> _normalizedAliases;

    internal CatalogEntity(string path, MarkdownFile file, Container scope, string? id, string? slug, string name,
        IReadOnlyList<string> aliases, string type, FactStatus status, StoryPosition? from, PositionResolution? fromResolution,
        IReadOnlyList<KnownName>? knownAs = null)
    {
        Path = path;
        File = file;
        Scope = scope;
        Id = id;
        Slug = slug;
        Name = name;
        Aliases = aliases;
        Type = type;
        Status = status;
        From = from;
        FromResolution = fromResolution;
        NormalizedSlug = slug is null ? null : LinkText.Normalize(slug);
        NormalizedName = LinkText.Normalize(name);
        KnownAs = knownAs ?? [];
        _normalizedAliases = aliases.Concat(KnownAs.Select(k => k.Value)).Select(LinkText.Normalize).ToList();
    }

    /// <summary>The file's path, relative to the den root, with <c>/</c>.</summary>
    public string Path { get; }

    /// <summary>The parsed file.</summary>
    public MarkdownFile File { get; }

    /// <summary>The container the entity belongs to (F-LAY-5).</summary>
    public Container Scope { get; }

    /// <summary>The entity's universe.</summary>
    public Universe Universe => Scope.Universe;

    /// <summary>The id, or null if the file has none yet (F-ID-2).</summary>
    public string? Id { get; }

    /// <summary>The slug: the file name without <c>.md</c>, or null if that isn't a valid key (F-KEY-4).</summary>
    public string? Slug { get; }

    /// <summary>The name as written (F-ENT-3, F-YAML-5).</summary>
    public string Name { get; }

    /// <summary>The aliases as written, in order (F-ENT-4): the names readers know wherever the entity is.</summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// The names readers learn partway through the story (F-ENT-10), in order: a
    /// link or a lookup reaches the entity by them as by an alias, and a read at a
    /// position knows each one only from its <c>from</c> (F-VIEW-7).
    /// </summary>
    public IReadOnlyList<KnownName> KnownAs { get; }

    /// <summary>Every name a link can reach the entity by in the alias tier (F-LINK-6): the aliases, then the <c>known_as</c> values.</summary>
    public IEnumerable<string> AllAliases => Aliases.Concat(KnownAs.Select(k => k.Value));

    /// <summary>The <c>known_as</c> entry whose value matches <paramref name="text"/> under F-LINK-4, or null.</summary>
    public KnownName? KnownNameMatching(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (KnownAs.Count == 0)
        {
            return null;
        }

        var normalized = LinkText.Normalize(text);
        return KnownAs.FirstOrDefault(k => LinkText.Normalize(k.Value) == normalized);
    }

    /// <summary>The type key as written (F-ENT-2).</summary>
    public string Type { get; }

    /// <summary>
    /// The combined definition of the entity's type (types.md F-TYPE-8), or
    /// null if the type isn't enabled in the den: an unknown type, so the
    /// entity is untyped (F-ENT-2).
    /// </summary>
    public TypeDefinition? Definition { get; internal init; }

    /// <summary>The entity's status, <c>draft</c> if it has none (F-ENT-5).</summary>
    public FactStatus Status { get; }

    /// <summary>The entity's <c>from</c>, or null if it has none (F-ENT-6).</summary>
    public StoryPosition? From { get; }

    /// <summary>Where <see cref="From"/> resolves, or null if there's no <c>from</c>.</summary>
    public PositionResolution? FromResolution { get; }

    /// <summary>
    /// Where the entity enters canon (F-ENT-6): its <c>from</c>, or without
    /// one the start of its book, its series, or the story. Null if its
    /// <c>from</c> doesn't resolve (F-POS-4).
    /// </summary>
    public StoryPoint? EntersCanon => From is null ? Scope.Start : FromResolution?.Resolved?.Point;

    /// <summary>Describes <see cref="EntersCanon"/> for messages: the <c>from</c> as written, or the start it defaults to.</summary>
    public string EntersCanonDescription => From?.ToString() ?? Scope.StartDescription;

    /// <summary>What the index stores for a link to this entity: its id, or its path while it has none (F-LINK-10).</summary>
    public string Key => Id ?? Path;

    internal string? NormalizedSlug { get; }

    internal string NormalizedName { get; }

    /// <summary>The aliases and <c>known_as</c> values, normalized (F-LINK-4): the alias tier of F-LINK-6.</summary>
    internal IReadOnlyList<string> NormalizedAliases => _normalizedAliases;

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Path})";
}

/// <summary>
/// A name readers learn partway through the story: an entry of <c>known_as</c>
/// (format.md F-ENT-10), such as <c>{ value: Mara Voss, from: "ember:38" }</c>.
/// </summary>
/// <param name="Value">The name as written.</param>
/// <param name="From">The entry's <c>from</c>, or null if it has none.</param>
/// <param name="FromResolution">Where <paramref name="From"/> resolves, or null if there's no <c>from</c>.</param>
/// <param name="Line">The 1-based line of the entry in the file.</param>
public sealed record KnownName(string Value, StoryPosition? From, PositionResolution? FromResolution, int Line)
{
    /// <summary>
    /// Where readers learn the name, or null if they never do: the entry has no
    /// <c>from</c>, or it doesn't resolve. Such an entry isn't live, so no read at a
    /// position knows the name (F-ENT-10 fails closed).
    /// </summary>
    public StoryPoint? Point => FromResolution?.Resolved?.Point;

    /// <summary>True if the entry has a <c>from</c> that resolves, so readers learn the name there.</summary>
    public bool IsLive => Point is not null;

    /// <summary>
    /// The entry's <c>check_text</c>: true or false as written, null if it has
    /// none. A value that isn't a boolean is a warning and reads as true, so the
    /// name is still checked (format.md F-ENT-10, F-NAME-1).
    /// </summary>
    public bool? CheckText { get; init; }

    /// <summary>
    /// The entry's <c>allow_in</c> as written: notes, by slug, name, alias, or
    /// id, whose text F-NAME-1 doesn't check for this name. Items that aren't
    /// text are left out, with a warning.
    /// </summary>
    public IReadOnlyList<string> AllowIn { get; init; } = [];

    /// <summary>
    /// True if F-NAME-1 looks for the name in plain text: as <see cref="CheckText"/>
    /// says, or without it, if the name has two or more words as search splits
    /// them, so a common one-word name such as <c>Rose</c> doesn't fire across a den.
    /// </summary>
    public bool IsTextChecked => CheckText ?? Text.SearchTokens.Tokens(Value).Count >= 2;
}

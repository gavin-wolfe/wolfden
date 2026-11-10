# Wolfden Design

**Status:** draft v0.1 (2026-10-10). Everything here is open to revision until
there's code that depends on it. Decisions already made are recorded in
[`CLAUDE.md`](../CLAUDE.md). This doc explains them and fills in the shape.

## 1. Goals and non-goals

### Goals

1. **Keep a series' lore consistent across many books.** Characters,
   settings, timelines, plot threads, and invented things of any kind.
2. **Know when each fact becomes true.** Every fact can carry the story
   position where it enters canon, so "what's true as of Book 2, chapter 14?"
   has an answer.
3. **Plain files, no lock-in.** Notes are plain markdown files in a folder
   you own, stored wherever you like: on your own machine, in a synced
   folder, or in a git repo, local only or with a private remote. You can
   read them, edit them in any editor, and keep them forever. Git is the
   recommended way to get history (§6).
4. **AI-native.** AI assistants are first-class users, connected through an
   MCP server and working from structured data instead of pasted documents.
5. **Works with notes edited outside the app.** You can edit the files in
   VS Code, an AI agent can rewrite a section, and Wolfden picks up the change.

### Non-goals (for now)

- **Not a manuscript editor.** Wolfden holds the story bible, not the prose.
  (Scrivener, Word, and plain markdown already do prose well.)
- **No publishing or formatting.**
- **No real-time collaboration in v0.** Multi-user hosting is a later phase
  (§10). The v0 design must not *rule it out*, but doesn't build for it.

## 2. Core concepts

| Concept | What it is |
|---|---|
| **Den** | A Wolfden workspace: the folder that holds `den.yaml`, and the notes in it. It can hold one or many universes. A den can be a git repository of its own or a subfolder of a larger one (§6). A universe that needs its own private remote goes in a den that is its own git repository. |
| **Author Identity** | Who a work is by: a real name, a pen name, or a collaboration. Metadata in v0, and the anchor for ownership in multi-user mode later. |
| **Universe** | A shared setting for one or more related works. |
| **Series** | An *ordered* sequence of books within a universe. |
| **Book** | A single work, either in a series or standalone in a universe. It has an ordered list of chapters. Scenes within chapters come later. |
| **Entity** | A single note about one thing: a character, a planet, a faction, a plot thread. Every entity has a **type**. |
| **Type** | A schema for entities: frontmatter fields, standard sections, and a template. |
| **Pack** | A bundle of types for a genre or purpose (core, space opera, epic fantasy, ...). |
| **Story position** | A point in the story: a book key plus a chapter key, written `ember:14`. v0 positions are chapter-level. `ember:14.2` (scene 2 of chapter 14) is reserved for later. **Decided (#2):** a bare `ember` means the start of that book. |

### Scope and inheritance

Every entity lives at exactly one **scope**: Universe, Series, or Book.
Where an entity is filed decides where it enters canon when it has no
`from`, which entities a series or book filter keeps, and which match wins
when links tie. It doesn't decide whether a canon view shows the entity: a
canon view at a position covers every scope of the universe, in story
order, so a Book 4 view shows a side character filed under Book 1
([`format.md`](format.md) F-VIEW-1). A character who appears across a whole
series lives at Series (or Universe) scope. A one-book side character lives
at Book scope. [`where-to-file.md`](where-to-file.md) is a short guide for
writers on choosing a scope.

Entities can **reference across scopes** in any direction. Book 3 can link to
a character introduced in a standalone book in the same universe. A reference
to a *later* book is allowed. A den that turns on spoiler checks gets it
flagged (§7), since it's usually a spoiler or a mistake.

### Entities don't span universes

A universe is the boundary for story entities, links, and story positions.
Positions are ordered only among one universe's books, so an entity shared
between universes would have no clear "canon as of" answer.

- **Crossovers mean it's one universe.** If two series share characters and
  their history matters (a Cosmere-style shared cosmology), model them as two
  Series in one Universe.
- **Incarnations are copies.** The same idea recast per setting (an
  archetype, a recurring narrator voice) is a separate entity in each
  universe. A `see_also` field can point between the copies.
- **Links resolve within the universe.** A link that would match only an
  entity in another universe is flagged, not resolved.

**Planned (#45): Author and den notes.** Timeless material that isn't story
canon, such as voice and style rules or real-world research, will be able to
live above Universe: at Author Identity scope (`authors/<slug>/notes/`) or
den scope (`notes/`). Universes will inherit them, and they can't carry story
positions. Until then, keep such notes at Universe scope. Moving one up later
is a file move, since ids don't depend on paths.

## 3. On-disk format

The normative rules for §3 and §5 are in [`format.md`](format.md). This
section explains the shape. Where the two differ, `format.md` wins.

### Den layout

```text
my-den/
  den.yaml                      # den settings, author identities, enabled packs
  packs/                        # den-local custom types (optional)
  # notes/ and authors/ are reserved for Author and den notes (§2, #45)
  # anything else at the root (a README, a LICENSE) is ignored
  universes/
    starfall/
      universe.md               # frontmatter + overview prose
      entities/                 # universe-scope entities, grouped by type
        species/
          velari.md
        locations/
          kessan-reach.md
      series/
        long-dark/
          series.md             # frontmatter lists book order
          entities/
            characters/
              ilse-tarrow.md
          books/
            ember/
              book.md           # frontmatter lists chapters
              entities/
              outline.md        # outline rows (see §5)
      books/                    # standalone books in this universe
        a-quiet-moon/
          book.md
```

Folder names are slugs for humans. **Identity comes from the `id` in
frontmatter, not from the path** (once a file has one; tools add it), so files
can be renamed or moved between scopes without breaking links. The app rewrites
slug links and positions when it renames something. After a rename by hand,
links by id, name, or alias still work (format.md F-RT-5).

### An entity file

```markdown
---
id: 01JA7Q8M4XK3V9R2D5W8HN6TQB   # ULID, assigned on creation, never changes
type: character
name: Ilse Tarrow
aliases: [Tarrow, "the Commander"]
status: canon                    # see format.md §9.3 for all six
fields:
  species: human
  rank:
    - { value: Lieutenant, from: "ember:1" }
    - { value: Commander,  from: "ember:22" }
  born: "2291"
---

# Ilse Tarrow

## Overview
Executive officer of the *Tessaly*...

## Appearance
Silver-streaked hair, a burn scar along her left forearm.

## Shoulder injury
<!-- wolfden: from=ember:19 -->
Takes a flechette round at Kessan Reach; the arm never fully recovers.

## Custom: Things she never says aloud
...
```

Key points:

- **Fields can change over time.** A plain value means it's always true. A list
  of `{value, from, until?}` entries means it's true at story positions.
  **Decided (#66):** a single entry with a `from` is allowed on any field,
  even one that isn't temporal (except `row`). It means "known from here":
  a death or a hidden identity stays out of canon views from before the
  reader learns it.
- **Sections can carry positions too.** A `<!-- wolfden: from=... -->`
  comment on the line under a heading (one blank line allowed; with an
  optional `until=`)
  marks the whole section as entering canon at that point. GitHub,
  Obsidian, and VS Code hide HTML comments, so headings render cleanly and
  heading links keep working. Other HTML comments are left alone. This is a
  structured version of "Book 3, chapter 23 additions" sections in a
  hand-kept character file. **Decided (#71):** a later section with the
  same standard heading (a new `## Appearance` for a new era) takes over
  from the earlier one at its `from`, the way a new field entry does, so
  the earlier one needs no `until`. Custom sections don't hand over, so
  cumulative ones can repeat. **Decided (#68):** a thread's `## Payoff`
  enters canon at the thread's `paid_off` unless it says otherwise.
- **Links** are wiki-style: `[[ilse-tarrow]]`, `[[Ilse Tarrow]]`, or an alias.
  They resolve by slug, name, or alias to an `id` within the same universe
  (§2), and the index stores the id (or the path, for a file that has
  no id yet). Ambiguous links are flagged.
- **Status** keeps undecided material out of canon views without moving it
  elsewhere. `idea` entities are what a hand-kept "idea bank" holds.
- **Entities enter canon at a position.** An optional top-level `from`
  says where the reader first meets the entity. It defaults to the start of
  the entity's book, series, or the story, by scope, and canon-as-of views
  leave the entity out before it.
- **Sections and field values have a status too.** The section comment
  takes `status=` (`<!-- wolfden: status=candidate from=ember:19 -->`), and
  a field entry takes `status:`. Entities and facts share one vocabulary:
  `idea`, `undecided` (a question with options banked), `candidate` (one
  option), `draft`, `canon`, and `retconned`. An entity or outline row
  without a status is `draft`. Canon views leave out
  non-canon sections and values, and a section can't be more canon than its
  entity.
- **Pinned facts are hard canon.** `pin=true` on a section or `pin: true`
  on a field entry marks the facts most important to check before drafting.
  A **pinned canon** view lists them as of a position, for the whole
  universe or filtered to a series or book.
- Unknown frontmatter keys and sections are kept intact. The app must never
  discard what a human or an agent wrote.

## 4. Types and packs

The normative rules for packs, types, field types, and templates are in
[`types.md`](types.md), and the core pack's files are in
[`packs/core/`](../packs/core/).

A pack is a folder with a `pack.yaml` manifest, a YAML file per type in
`types/`, and markdown templates in `templates/`. A type is a YAML file
named by its key:

```yaml
# packs/core/types/character.yaml, shortened
name: Character
plural: characters                    # its folder under entities/
fields:
  role:     { type: text, temporal: true }
  born:     date                      # in-world date: free text plus a sort key
  species:  text                      # plain text, or a link to a species entity
  factions: { type: ref, to: faction, list: true, temporal: true }
sections:                             # standard sections, in section order
  - Overview
  - Appearance
  - Want
template: character.md
```

Field types: `string`, `text`, `number`, `bool`, `date`, `enum`, `ref` (a
link, optionally to given types), and `position`, each optionally a `list`.
`temporal: true` marks values that change over time, and `required` and
`default` are available. Every type also has the `row` and `placeholder`
fields.

**Packs:**

- **core** (always on): character, location, faction, item, event, thread
  (a plot thread with planted and paid-off positions), idea, note
  (free-form), species, culture.
- **space-opera** (built in, enabled in `den.yaml`): star-system, world,
  settlement, polity, ship-class, ship, technology.
- **epic-fantasy** (built in, enabled in `den.yaml`): realm, magic-system,
  creature, deity, artifact, order.

Dens can define their own types in `packs/`, or extend any enabled type
by adding fields, sections, or a template (`extensions/`). Extensions
add; they don't change what another pack defines.

**Decided (#6): the den wins.** When a den-local definition clashes with
a built-in one (a pack key, a type, a `plural`, an extension's field or
section, or a chapter field), the den-local one is used and the built-in
one is ignored, with a warning that names the clash. A den-local pack
named like a built-in pack shadows it. So an upgrade never overrides a
den's own definitions. Built-in packs change only by adding: changing an
existing built-in field's type, renaming a section, or adding or changing
a default is a breaking change, while template prompts and headings may
change in any release, since they only affect new entities. A writer who
relies on a custom field should define it in a den-local pack (types.md
§2.4). Versioning policy is #56. Clashes between two den-local packs are
settled by pack order (see types.md F-PACK-11).

**Decided (#265): a type can be a kind of another.** A type declares
`kind_of: <type>` or a list, and it's then accepted wherever a `ref` field's
`to` names one of its ancestors. It carries identity only: no fields,
sections, or templates come with it, so several parents can't conflict
(field inheritance could come later under its own keyword, if dogfooding
shows a need). It chains, a den's extension can add parents to any pack's
type, and an unknown parent or a loop is a warning and is ignored. There
are no abstract types. In the space opera pack, `star-system`, `world`,
`settlement`, and `ship` are kinds of `location`, and `polity` of `faction`;
epic fantasy follows the same pattern (types.md F-TYPE-13, §12).

This changes what #32 recorded in types.md F-SPACE-2: that a core field
links only to core's own types, so a chapter set on a planet warns, and a
den redefines each field to stop it. That left a den with the pack either a
warning on every chapter set on a planet, or a clash warning for each field
it redefined. These really are "is a" relationships, so the types now say
so, and no field is redefined. The cost is that every field that asks for
`location` takes all of them, so a star system's `part_of` takes a world.
"Extensions add" still holds: an extension adds a parent, and `to` stays as
written.

**Decided (#269): a filter by type includes the type's kinds.** This changes
what #265 first shipped, where filters matched a note's own type only. We
said a world is a location, and the link picker already treated it as one.
An exact filter also fails silently: an agent asking `canon_as_of` for
locations got a clean, incomplete list and could report it as complete, and
returning too much is a much milder failure. Narrowing still works (`world`
leaves out other locations), each result shows its own type, and grouping and
folders don't change. There's no opt-out until a plausible use case appears.

**Decided (#270): the pack's `planet` type is now `world`.** It's a planet,
moon, asteroid, or gas giant: a body in space that people can live on, visit,
or build around, so it covers a gas giant nobody lives on but things orbit or
float in. They share a structure and fields aren't inherited, so one type
with a `kind` covers them. A moon or an asteroid filed under `planet` is
simply wrong, and `world` is the genre's own word. `body` was rejected
because a note's "body" already means its text in Wolfden. The type's
description names "planet" so writers looking for that word find it. A den
that wants separate types can define them, such as `moon` with
`kind_of: world`, and a filter by `world` then finds those too (#269).
Nothing had been released, so nothing needed migrating.

**Decided (#33): a realm is the land, and whoever governs it is a faction.** A
`realm` is a land defined by who rules it, or a plane of existence, and is
`kind_of: location` only. A continent or other plain geography is a `location`
with a `kind`, which core now has, and sits above realms through `part_of`. A
city in a realm is a `location` `part_of` the realm. Rule can change while the
land carries on, so the rulers are a separate `faction`, named in the realm's
`controlled_by` with a value for each stretch of the story. This differs from
the space opera pack, where the `polity` is the state and is a faction, and
a world is the place it holds: a fantasy story's kingdom is usually thought of
as a land first. What sets a realm apart from a location is its `seat` of power
and its `borders`, along with `population` and `kind`.

**Decided (#33): species and culture are core, and a creature is a kind of
species.** A kind of being isn't special to one genre, so `species` moved from
the space opera pack to core, with `homeworld` renamed `origin`. A `creature`
is `kind_of: species`, in the epic fantasy pack, and the line is whether it has
a culture: a dragon that holds councils and keeps laws is a species, a dragon
that eats sheep is a creature, and one named dragon is a character, whose
`species` field says which. A `culture` is new in core: a group sharing ancestry,
language, or customs. It's a kind of nothing, not of faction, since it has no
leaders or members list. A character's `heritage` stays text that can link to
one. Every den now has these two types. A den that already defined its own, under
the same key, is unchanged, apart from the F-TYPE-4 warning on its file. A den's
type that uses the folder under another key, such as an `alien` with
`plural: species`, replaces the core type instead (types.md F-TYPE-5), with that
warning on its file. With a pack on whose fields name that core type, such as
epic fantasy's `worshippers` or space opera's `created_by`, those references
warn too (F-FT-9), and epic fantasy's `creature` loses its parent (F-TYPE-13). A
link in those fields to the den's type gets F-LINK-11. All of it clears when the
den gives its type another folder and `kind_of: species`, or `kind_of: culture`.
Neither one alone is enough.

**Decided (#33): rulers are factions, and a god is a character.** There's no
fantasy government type: a realm names its rulers in `controlled_by`, as space
opera places do. A `polity` stays separate, and is a faction that governs
territory, at any size. A `deity` is `kind_of: character`, and declares its own
`gender`, `pronouns`, `species`, `factions`, and `Want` section, since `kind_of`
passes on no fields. A kind of god is
a species, a pantheon is a faction, and worshippers can be a culture.

**Decided (#6): your writing is yours.** Wolfden's licenses place no
conditions on the notes, manuscripts, or other content writers create with
it. The built-in packs in `packs/` are licensed MIT-0 (no attribution
required), so headings and prompts copied from a template carry no
conditions either.

## 5. Structure and time

The normative rules for books, chapters, positions, and outlines are in
[`format.md`](format.md) §6, §8, and §11.

### Books and chapters

`book.md` frontmatter holds the ordered chapter list. Each chapter has a
stable key, a number, an optional title, and optional fields from packs:
core defines POV, narration (first person, third limited, and so on),
tense, an in-world date, and a setting (the chapter's locations). A
universe, series, or book can set `chapter_defaults` once (say, third
person past tense for a whole series), and a chapter's own fields
override them. A `chapter_heading` list can build each chapter's displayed
heading from those fields (#227). Story positions refer to chapter
keys, so renumbering chapters doesn't break positions. Book and chapter keys
can't contain `:` or `.`, which the position syntax uses as separators.

### Outline rows

An outline is a list of **rows** (beats) with stable IDs that keep counting
across books in a series. Book 1 might end at row 36, so Book 2 starts at row
37. That way a reference like "row 69a" is never ambiguous. Rows can be
split (`69a`, `69b`) without renumbering anything. **Decided (#2):** an
outline is a set of GFM tables with `row` and `beat` columns, plus
`chapter`, `pov`, `status`, and any custom columns. Rows stay one line, and
details live in an entity linked from the row.

Positions stay `book:chapter`: rows aren't positions in v0. An event can
point at rows with a plain `row:` field, and `ember:r75` is reserved for
row positions later, the same way `ember:14.2` is reserved for scenes.

### Timeline

Timeline entries are `event` entities with two independent axes:

- **In-world time:** when it happens in the story world. Free text plus a
  sortable key in v0 (the `when` field, a `date`, types.md). A date such
  as `2291-05-14 06:40`, with an optional time of day, sorts by itself.
  Custom calendars (fantasy months, stardates) come later (#37).
- **Story position:** where the reader learns of it. Flashbacks and reveals
  make these differ, and the difference is often the interesting part.

### Freezing a finished book

When a book is done, **freezing** it marks it `status: final` (format.md
F-BOOK-1). That is the freeze: after it, edits to that book's scope are
refused unless explicitly unfrozen. When the den has git history (§6),
freezing also adds a tag as an extra snapshot. This replaces hand-made
`_FINAL` copies. **Decided (#31):** the tag is
`book/<universe>/<book>/final` (`book/vael/ember/final`), since book keys
are unique only within a universe (format.md F-KEY-5).

**Decided (maintainer, 2026-10-25 and 2026-10-28, #31):**

- **Refused while a section is left out.** A book with a bad section
  comment in its `book.md` or a note filed in it (F-GEN-2, #50) can't be
  frozen, since it can't be fixed without unfreezing, and a snapshot would
  record text no view shows. It's refused before anything is changed.
- **Freezing** takes three steps, in this order: set `status: final` in
  the book's `book.md` (edited in place, the rest of the file kept);
  checkpoint (§6), a commit that holds that change; then tag that
  commit. The tag then always
  names a snapshot where the book already says it's final. Without
  history, the first step alone is the freeze, and the result says no
  snapshot was taken.
- **If a step fails,** what's done stands. A checkpoint that fails (a
  hook refuses it, say) leaves the book final with no commit or tag; a
  tag that fails leaves the commit. Freezing again picks up where it
  stopped.
- **Re-freezing gets a numbered tag.** Tags never move and are never
  deleted, so each freeze keeps its own snapshot: the first is
  `…/final`, and each later freeze of the same book adds the next name,
  `final-2`, `final-3`, and so on. Tags belong to the whole repository,
  and Wolfden can't tell its own earlier tag from one another den in the
  same repository made, so it counts up past that too. The result names
  the tag added.
- **Unfreezing** writes `status: draft` (rather than removing the line),
  then checkpoints, with no tag, so the history shows when the book was
  reopened. If the checkpoint fails, the change stands and the result
  says so.
- **For the writer only.** Freezing and unfreezing are in the web UI
  (the book's page) and the CLI (`wolfden freeze`, `wolfden unfreeze`).
  MCP agents get no tools for them in v0; their `frozen` errors tell
  them to ask the writer whether to unfreeze the book (§8). The same
  errors in the web UI and the CLI say how the writer unfreezes it
  instead (#209).

v0 behaviour, not recorded decisions (#31): a freeze or unfreeze takes
the hash of the `book.md` it's based on and is refused with `stale_file`
if the file has changed since, as other writes are (§8); the CLI reads
the file and writes it in one go, so it passes none. The checkpoint (of
a freeze or an unfreeze) records the whole den, as every checkpoint does
by default (§8, "Files only; commits by `checkpoint`"), so it can take in
other work not checkpointed yet; the confirmation says how many other
changed files it will record, and the result how many it did, counted
and never named (so a pinned server names nothing in another universe).
The tag's number is one more than the highest of the book's freeze tags
already there, so the highest is always the latest freeze. Freezing a
book that's already final tells "done" from "a step is missing" by its
history and the whole book, not `book.md` alone, since a writer can mark
a book final by hand and edit it after: if anything in the book's folder
(`book.md`, the outline, its notes) differs from the latest checkpoint,
it checkpoints; then it tags the latest checkpoint that changed the
book's folder, which holds the book as it is now, unless one of the
book's freeze tags already names that checkpoint or a later one (one
made from it, in the history the den is on: two `git merge-base
--is-ancestor` checks, #201), in which case it does nothing. A freeze
tag on something that isn't a commit is skipped. A tag from an earlier
freeze doesn't count, so a book unfrozen and frozen again gets its own
tag. If git records nothing of the book (it ignores `book.md`, say), no
tag is added, since no checkpoint would hold the book final. The web UI
uses the same test to offer **Finish freezing** on a final book that no
snapshot holds as it is now: edited outside Wolfden, marked final by
hand, or one whose checkpoint or tag failed.

## 6. History and versioning

Wolfden doesn't keep its own audit trail. It keeps the format apart from
where a den is stored and how its history is kept:

- **Format:** a den is plain files in a folder (format.md). Nothing in the
  format depends on where the folder is stored or how its history is kept.
- **History:** a capability that a backend provides. Git is the v0 backend
  and the recommended default. Per-entity history, diffs, checkpoints, and
  freeze tags all come from it.
- **No backend:** a den without one still loads, validates, and indexes.
  History features say that history isn't available, instead of erroring.

The den store and the history provider are interfaces in `Wolfden.Core`
(#121; v0 behaviour, not a recorded decision):

- **Den store.** `IDenSource` is the read half, and everything that only
  reads a den takes it. `IDenStore` adds the write half: write a file
  (create, replace, or either), delete a file, move or rename a file or
  folder, and delete an empty folder. Paths are den-relative with `/`.
  Every write checks its paths first (`DenPaths`): nothing absolute, no
  `..`, nothing hidden (so never `.git/` or `.wolfden/`), never through a
  folder link, and only names that are the same file on every OS (no
  `\`, `:`, or other characters Windows doesn't allow, no trailing space
  or dot, no device names like `CON`). A name being made mustn't be
  disguised by invisible characters (a zero-width space, a byte order
  mark, a direction control, or one at either end), though emoji and
  the joiners some scripts need are fine; existing names aren't checked
  for these. A name that differs only in case or Unicode form from one
  that's there is refused too, except in a rename of that very file or
  folder. Refusals
  are a `DenStoreException` with a reason code for the API and MCP tools
  to map, including a read-only file and one another program has open.
  The check and the write aren't one step, so another program can change
  the den in between; the store still never writes outside the den, and
  never overwrites a file made in between. A write can say what it built on
  (`builtOn`), and the store compares that just before each try and refuses with
  `Changed` if it differs (#323). A store that can't be written says so
  (`IsReadOnly`) and refuses every write: the built-in packs are one.
  `IDenChangeFeed` is the watch half: it reports changes with den-relative
  paths, lost events, and a broken watch, and the index's watcher listens
  through it.
- **On disk,** `FileSystemDenStore` is all three. A write goes to a hidden
  temporary file in the same folder, then replaces the file in one move,
  so a reader or a crash sees the old file or the new one, and the bytes
  are exactly those given. The temporary file is hidden, so the den and
  the watcher ignore it (F-LAY-2). A store lives as long as its host, so
  each read looks at the disk afresh; a whole-den load reads through a
  `Snapshot()`, which keeps what it lists for that load (#119).
- **History provider.** `IHistoryProvider` gives log, diff, checkpoint,
  and tag (and lists tags), scoped to the den's paths, with
  `HistoryCapabilities` flags for what the backend can do. An operation
  the backend can't do returns a result that says why, for the writer,
  instead of throwing; a backend that fails while doing one throws a
  `HistoryException`. Hidden paths are never recorded or reported, and a
  checkpoint commits only den files, leaving anything else the user has
  staged as it was. Tags belong to the whole repository, not the den.
  The log pages with a limit and a `before` revision, a diff without a
  `from` starts at the latest checkpoint, and every operation can be
  cancelled, up to a step that can't be stopped part way without harm,
  such as git writing a commit. `NoHistory` is the provider of a den without a backend.
  Freezing adds its tag only when the provider has the `Tag` capability.
  `Wolfden.Git` implements the interface (#19).
- **The git provider** (#19; v0 behaviour, not a recorded decision,
  apart from the git CLI). **Decided (2026-10-22):** Wolfden runs the
  user's own `git` command, not a library, so their configuration
  applies: commit signing, hooks, credentials, and `safe.directory`. Git
  runs in the den's folder, with that folder as the pathspec and hidden
  paths excluded, so paths in and out are den-relative and nothing
  outside the den is listed, compared, or committed. A den outside any
  repository has no history (`NoHistory`); when git isn't installed, or
  refuses the repository, every history operation says so instead.
  A checkpoint is `git commit --only -- <den paths>`: new den files are
  first marked with `git add --intent-to-add`, which records no content,
  and whatever else the user staged stays staged. A checkpoint records
  each den file as it is on disk, so a den file the user staged in part
  (`git add -p`) goes in whole, and a den file the user stopped tracking
  with `git rm --cached` is tracked again. A checkpoint with nothing to
  record makes no commit. If git refuses the commit (a hook, signing, or
  a merge in progress), the checkpoint says so and takes its
  intent-to-add marks off again, so the user's index is as it was.
  Every git command has a time limit, since git can wait outside the
  terminal on a signing passphrase, a hook, or a credential helper
  (#143): a command that only reads is stopped after a minute, but one
  that writes the user's index or refs is never stopped once started,
  since that would leave `.git/index.lock` behind (#145). After two
  minutes Wolfden stops waiting, leaves git running, and says so; if git
  then ends without committing, the marks come off. Cancelling a
  checkpoint works up to its commit. Wolfden never deletes git's lock
  files. A diff against the files as they
  are now includes new files git doesn't track yet (not ignored ones),
  through a temporary index, so it never changes the user's index, and
  it writes nothing into the repository's objects (#142). Clean filters
  still run on the files compared, and Git LFS's stores what it cleans in
  `.git/lfs/objects`. In a sparse checkout, files marked skip-worktree
  (the ones it keeps off the disk, or any marked by hand) count as
  unchanged; without one, a file marked by hand is compared as it is on
  disk. Edits inside a submodule aren't the den's, and a submodule isn't
  a den file: log and diff leave out its additions, deletions, and
  commit moves (#158), and a checkpoint that changed only submodules is
  still listed in the log, with no files. A note that becomes a symbolic link, or the other
  way round, is modified, with git's patch of the link's target path; a
  note that becomes a submodule is deleted, and one that replaces a
  submodule is added. Git doesn't see what a link leads to, so a note
  replaced by a link to a folder, or by a broken one, is still modified,
  though the den skips the first and takes the second as absent. The
  log of a file follows its current path, not back through renames
  (`--follow` can't be paged), but lists the checkpoint that renamed
  it as a rename. Every log is in `--date-order`, so no checkpoint is
  listed before one that came from it, even when a merged commit's
  clock was wrong; it keeps date order wherever ancestry allows, where
  `--topo-order` would also group each branch's commits (#181). A diff
  of one file or folder (a checkpoint's own, between two, or up to the
  files now) shows a file renamed into or out of it as a rename: the
  path's own diff comes first, and only when it adds, deletes, or
  renames a file does a raw diff of the whole den (no patches) find
  the rename's other side; then each change the path's own diff got
  wrong is patched on its own: a rename from a diff of its two paths,
  with literal pathspecs, an add or a delete from a `--no-renames`
  diff of its path, a hundred paths to a run (#175). The raw diff sees
  hidden paths too, so a note moved into a hidden folder (`.trash/`)
  shows as deleted, never as renamed to a den file that looks like it,
  and one moved out of one shows as added; a log entry that may hide
  such a pairing is read again the same way. No patch comes from a run
  that sees a hidden path, so a patch never names one or shows its
  text, and such a delete or add has a true delete's or add's patch
  (#184 review). The same goes for a diff of the whole den. A
  checkpoint's own diff of a path that can't be a den file has no
  files. The log pages by position: each page
  walks the same log from HEAD with the same path filter and starts
  after the `before` entry, since a history with merges isn't a line
  and the cursor's ancestors alone would miss main-line commits. A
  `before` that isn't an entry of that log is refused. Revisions from
  callers are resolved to commit hashes first, so one can't be read as
  an option. "Not a repository" is told from "repository refused" by
  looking for a `.git` above the den, stopping at
  `GIT_CEILING_DIRECTORIES` as git does. Wolfden needs git 2.31 or
  later (`--diff-merges`). Tests run git with a configuration of their
  own, never the developer's.

**A den inside a larger repository.** The den is the folder that holds
`den.yaml`, and it can be a subfolder of a larger git repository. History
is then scoped to the den's paths: log and diff cover only the den's
files, and checkpoints commit only them.

**Turning history on** (maintainer decisions, 2026-10-28, #195; the
rest is v0 behaviour). A den without history gets it from `wolfden init
--git` in its folder or from **Turn on history** in the web UI's
checkpoint dialog (§9), which share one implementation
(`IHistorySetup`, git's in `Wolfden.Git`). In order: `git init` in the
den's folder; then the name and email git will use for it (below), with
any given saved, before anything else can fail and send the writer to try
again without them; then `.wolfden/` in the den's `.gitignore` (created if
there's none; appended unless `git check-ignore` says the file already
leaves it out, so a later `!.wolfden/` or a line with a leading space doesn't
count; otherwise left alone); and a first
checkpoint, "Start of history", of the whole den, unless the writer opts
out (a checkbox, ticked by default, or `--no-checkpoint`). The name and
email are what git itself would use for the den's repository (`git
config user.name` and `user.email`, which count the global and system
settings), read once the repository exists, since a setting can depend on
where the repository is (`includeIf "gitdir:…"`), which git can't apply
before. They're asked for only when one is missing, and then both are. What's given wins for this den
(maintainer decision, 2026-11-03, #286): it's saved with `git config
--local`, in that repository only, never globally, before the first
checkpoint, over what git has, and also over a global `author.name` or
`committer.email` of the kind git prefers to `user.name` and `user.email`.
Refused with nothing changed: one given without the other, always, even if
it's what git has, since it would be recorded beside what git has for the
other (a pen name with the everyday email; maintainer decision, 2026-11-04,
#286; the CLI refuses it before anything is made, a new den included; the API
answers `identity_needed`, saying which is needed); and any given while `GIT_DIR` or
`GIT_COMMON_DIR` points git at another repository. An environment variable
such as `GIT_AUTHOR_NAME` doesn't refuse: Wolfden's commits don't use it
(below), and the result says it's set, since git run by hand does. None of these messages
repeats what git has. A save that fails in a repository the run
made takes the repository away again; in one that was there, what was saved
is taken back if it can be, and the result names what couldn't be. Once saved, git is asked what it would
record (`git var GIT_AUTHOR_IDENT` and `GIT_COMMITTER_IDENT`, without the
environment's name, email or config, as the gate below asks it) and whether
it would sign; anything that still beats them refuses before any commit, with
what was saved taken back as for a failed save, since left saved, the next
commit would record the name given and sign it with another key.
A clone has none of this, as with any git setting. When a name and
email are given, signing is saved as off in that repository too, always
(`commit.gpgsign`, `tag.gpgsign`, `tag.forceSignAnnotated`, `push.gpgSign`;
maintainer decision, 2026-11-04, #286), even if they're what git has or the
repository names its own key: the key needn't follow the name
(`gpg.ssh.defaultKeyCommand`, `local-user` in `gpg.conf`), Wolfden can't see
every way it's chosen, and a signature would tie the pen name to it for good.
The result says so if signing was on, and how to turn it back on, including
that `local-user` also applies. When a commit or tag fails
and git's settings say that call would sign, git's output isn't repeated, at
any checkpoint or tag, since it can name who the key belongs to; otherwise it
is, whatever words it uses. A new den
given a name or email inside a repository refuses (exit 2) before anything
is made; on an existing den, the refusals say it wasn't applied. Any name will do, a pen name
included, and the email can be a placeholder, as long as git will record
the pair (`git var GIT_COMMITTER_IDENT`: a name made only of characters
git strips, such as `;`, is refused, and so is an email given that git
would record as empty, such as `;`, #207). A missing one that wasn't given,
or a pair git won't record, refuses with nothing changed: the repository
the run just made, still without a commit, is taken away again: moved
in one rename to a hidden folder in the den, then deleted (if the rename
fails, the result says the den is now an empty repository, and trying
again picks up from there; if the delete fails, the den has no
repository and the result names the folder left behind). Before the den is a repository, the web
status can only say a name or email *seems* missing (`identityCertain`
is false), so the dialog's fields are optional then. It also refuses,
with nothing changed: a den inside a larger repository (which keeps its
history already), a den whose own repository already has checkpoints, a
missing git, and a name or email that isn't one line of at most 200
characters without control characters or `<`/`>`. A step that fails
stops the rest, and the result says what was done and what wasn't. A den
whose folder is the top of its own repository (as git says: `git
rev-parse --show-prefix` is empty there, so a `.git` folder git doesn't
recognise, inside a larger repository, doesn't count, and nor does a
linked worktree of another repository, whose `--git-dir` and
`--git-common-dir` differ and whose config is shared) with no
checkpoints yet (made by `git init`, or by a turn-on that stopped part
way) is finished rather than refused: every step but `git init` runs, so
trying again is safe, and the web UI's checkpoint dialog, which sees
history there, offers that den's first checkpoint as usual. A server
pinned with `serve --universe` may turn history on: it covers the whole
den, as its checkpoints do, and its result names no files. The server
asks for the den's history provider on each request, so checkpoints, the
History tab, freezing, and automatic checkpoints work at once, without a
restart; while a turn-on runs, an automatic checkpoint skips its poll.

**One gate for every commit and tag** (#311; the #311/#315 design, §3.4
steps 3 and 4, D4). Every checkpoint and tag Wolfden makes, from any
surface, passes `CommitGate` in `Wolfden.Git` first, and `GitProcess`
refuses to run a command that makes commits or tags without what the gate
decided, so a new path can't skip it. The gate:

- works out the name and email from git's settings for the den (`git var
  GIT_AUTHOR_IDENT` with `user.useConfigOnly` on and `GIT_AUTHOR_*`,
  `GIT_COMMITTER_*`, `EMAIL`, `GIT_CONFIG_PARAMETERS` and `GIT_CONFIG_COUNT`
  taken out of its environment, so only git's config files count), and refuses,
  with nothing changed, when there are none, rather than let git guess one
  from the computer's account. It first checks that git finds the den's
  repository without that config too (`git rev-parse --absolute-git-dir`,
  the same folder either way), and refuses otherwise: a repository trusted
  only through `safe.directory` in `GIT_CONFIG_COUNT` or
  `GIT_CONFIG_PARAMETERS` would have `git var` answer from outside it;
- hands them to that one git process as `GIT_AUTHOR_NAME`,
  `GIT_AUTHOR_EMAIL`, `GIT_COMMITTER_NAME` and `GIT_COMMITTER_EMAIL`, over
  anything inherited, so an inherited `GIT_AUTHOR_NAME` doesn't win, the
  committer (or an annotated tag's tagger) is the author, and no config is
  written;
- pins `commit.gpgsign`, `tag.gpgsign` and `tag.forceSignAnnotated` with
  `-c` to the den's own `.git/config` (`git config --local`, which doesn't
  follow includes), for each one that file sets, so an `[include]`, a
  `config.worktree`, `GIT_CONFIG_COUNT` or `GIT_CONFIG_PARAMETERS` can't
  turn signing on, or off. When git's own answer for that write differs,
  the write goes ahead and its result carries a warning (maintainer
  decision D4, 2026-11-09: pin and warn, never refuse). One the file
  doesn't set is left to git. The pin doesn't choose a key.

A co-author (MCP `checkpoint`'s `co_author`) whose name or email equals one
of git's global `user.*`, `author.*` or `committer.*` settings (read without
includes, and with them from a folder outside any repository, so a plain
`[include]` counts but an `includeIf "gitdir:…"` doesn't), compared
without case, is refused when the den records another name (D9): an agent
that has learned the writer's other name could write it into a permanent
trailer. Neither value is repeated in the message.

**Git or a synced folder, not both.** A git repository inside a synced
folder (Dropbox, OneDrive, iCloud) can be corrupted when two machines
sync while git is writing. Use git with a private remote, or a synced
folder without git, but not both.

With git as the backend:

- **Per-entity history** is `git log` on the entity's file, with diffs, shown
  in the UI and exposed through MCP.
- **Saving vs. committing:** edits are written to the working tree straight
  away, and saving never makes a checkpoint (#28). **Checkpoints** are
  commits with a message. **Decided (maintainer, 2026-10-27, #30):** they
  are manual by default: made when the writer asks in the web UI (§9,
  "Checkpoints in the web UI"), naming it ("Notes pass after drafting
  ch 12"), or when an AI agent asks through the MCP `checkpoint` tool.
  Commits made by AI agents through MCP record the agent as
  `Co-Authored-By`; the den's name, from the gate above, is the author.
  Automatic checkpoints are opt-in, below.
- **Automatic checkpoints** (#30; opt-in is the maintainer's decision,
  the rest v0 behaviour). With `auto_checkpoint: 30m` in `den.yaml`
  (format.md F-DEN-9; from `5m` to `24h`), a running `wolfden serve`
  makes a checkpoint once the den's files have gone that long without a
  change. Only `serve` runs the timer: `mcp` doesn't, since an agent
  makes its own checkpoints with a descriptive message and a
  co-author, and an automatic one in its process would take its
  session's edits without either; `index --watch` doesn't, since it
  only keeps the index. The server looks at the den's files every 30
  seconds (paths, sizes, and times, never contents or hidden files); a
  change restarts the wait, and once they've been still that long it
  records what changed since the latest checkpoint, with a generated
  message: `Auto-checkpoint: edited 3 notes`, or `Auto-checkpoint:
  changed 4 notes (2 edited, 1 added, 1 deleted)`, then a blank line and
  a line per file (`edited universes/…`, at most 20, then "and N more").
  It makes one checkpoint per quiet spell, not one per poll. It never
  starts while a git write may be running (`IHistoryProvider.IsWriting`:
  a checkpoint in this process, a git Wolfden stopped waiting for, or an
  `index.lock` there) and tries again on the next poll; after ten busy
  polls in a row (five minutes, as a stale `index.lock` would make) it
  says so in the log, once. It never stops git. Nor does it start while
  git is in the middle of something (`IHistoryProvider.InterruptedOperation`:
  `rebase-merge/`, `rebase-apply/`, `MERGE_HEAD`, `CHERRY_PICK_HEAD`,
  `REVERT_HEAD`, or `BISECT_LOG`, found through `git rev-parse
  --git-path`, or a detached HEAD), since the commit would land inside
  it or on no branch; it says so once and makes the checkpoint after.
  A manual checkpoint is still made then, as git would make it (git
  itself refuses one during a merge), and the web UI warns. The
  per-repository gate is in-process only, so two Wolfdens on one den (or
  the writer's own git) may race: nothing to record does nothing, and a
  checkpoint git refuses is logged once until the files change, and
  tried again after another quiet spell, never in a loop. Without
  history the setting does nothing apart from a warning in `serve`'s
  log. The setting is read from `den.yaml` on each poll, so it needs no
  restart. A signed commit needs the passphrase cached (by `gpg-agent`,
  say), or git waits on a prompt; while it waits, saving isn't blocked,
  since the ledger is never locked across git (§9, "Saved here or
  elsewhere"). Two limits: the fingerprint misses a change that keeps a
  file's size and time to the tick, which the next checkpoint, manual
  or after another change, records anyway; and an automatic checkpoint
  in `serve` records whatever an MCP session saved and hasn't
  checkpointed yet, without that agent's `Co-Authored-By` (its later
  `checkpoint` then has those files no more).
- **Remotes:** optional, and never required. Wolfden doesn't push or pull
  yet; a den's repository works with `git remote` and `git push` like any
  other. **Decided (maintainer, 2026-11-04, #312):** Wolfden will handle
  backup to a private remote for the common path.

With or without a backend, **the index is never the only copy of
anything.** Deleting it loses nothing (§7).

## 7. Index, queries, and continuity checks

A SQLite database (`.wolfden/index.db`, gitignored inside the den) built
from the files:

- Tables for entities, field values (with from/until positions), sections
  (with positions), links, outline rows, and positions, plus FTS5 for
  full-text search of names, aliases, leads, and sections. Field values
  are searched from their own table (#179, see the search page in §9).
- **Rebuilt** fully on demand and incrementally through a file watcher, so
  edits made outside the app show up within moments.
- **Self-healing.** A full rebuild writes a new file and swaps it in, so a
  crash never leaves a half-built index. An index that's missing,
  unreadable (say, after a sync conflict), or from another schema version
  is rebuilt, not repaired. Wolfden puts a `.gitignore` inside `.wolfden/`
  so git skips the folder without changes to the den's own `.gitignore`.
- **Folder links are skipped** (#102; v0 behaviour, not a recorded
  decision). Every walk of a den (the loader, the files hash, the watcher,
  and `init`'s search for dens below) skips symbolic links and junctions
  to folders, as git does, so links can't loop and each note has one
  path. `check` warns about each link where the den reads files
  (format.md F-LAY-2), so linked notes aren't dropped silently.
  Cloud-sync placeholders (OneDrive, Dropbox) aren't links and are read
  as usual.
- **A file that can't be read is absent** (#112; v0 behaviour, not a
  recorded decision). A link to a file that's gone or that loops, a file
  deleted between listing and reading, one another program holds open,
  or one the user may not read is left out of `check`, the files hash,
  and the index, as if it weren't there, rather than stopping the
  command. A file the index left out isn't in its files hash, so once the
  file can be read the index is stale and is rebuilt. A folder that can't
  be listed (the user may not list it, or it's deleted while it's read) is
  absent in the same way (#116): it isn't even a folder to the den, so a
  book folder that can't be listed isn't reported as one without its
  `book.md`. Views stay that way. What has to tell "not there" from "there
  but unreadable" reads through a `TrackingDenSource`, which remembers each
  failure on the same reads as the load (#284): `check` lists them as errors
  (format.md F-GEN-5), and a write that depends on every file (rename, move,
  section rename, alias change, copy, create, `chapter_defaults`, a new book
  or series, and a freeze or unfreeze that will checkpoint) is refused as a
  whole. A write to one file isn't stopped by another that can't be read.
- **Views share a file with programs writing it; writes don't** (#298;
  maintainer decision, 2026-11-04). Pages, read tools, the index, and `check` read
  through `FileSystemDenSource.ForViews`, with `FileShare.ReadWrite | Delete`
  (`SharedFile`), so a note open elsewhere doesn't vanish, unless the program
  keeps others from reading. Such a read can land part way through a save, and the
  next read corrects it. Everything a write builds on reads through `Snapshot()`,
  or the store, with `FileShare.Read`, so on Windows it's refused while another
  program has the file open to write. That's the default for
  `new FileSystemDenSource`, so a source made that way fails safe. But the
  overloads that take a folder (`DenStructure.Load(string)`,
  `EntityCatalog.Load(string)`, `LinkIndex.Load(string)`, `DenCheck.Run(string)`)
  read as views, since today's callers only look things up or report: a write must
  not build on one. Sharing reads with
  writes too was tried and rejected: a stalled save could be read in part, then
  overwritten once the save finished and the store's retry went through, losing
  text silently (review of #307). A refusal is visible, and clears on retry; the
  cost is that a write is refused while a note is open to write, even one that
  program won't change again. To revisit while dogfooding. A write's lookup still
  goes through the index, so it can find what its own read then can't, or miss
  what the index read part of. Either way it's `den_unreadable` and names what's
  open. The lookup's own error comes first, since a wrong name would look the same,
  so during a stalled save it can say "has no book". It doesn't tell the writer to
  fix the open file: if the lookup's error is an error in that file, it's part of a
  save, so only the file is named (#323). That holds for adding an image too, which
  reads what's above the note to know whether the book is final.
  The limits: on Linux and macOS, .NET takes the same shared `flock` for any
  sharing, and a program writing a file doesn't normally take a lock, so both kinds
  of read behave the same and a write there can read part of a save, as it always
  could. `checkpoint` hands files to git, which reads them itself, so a
  checkpoint can record part of a save; a freeze refuses first but can't stop
  a save that starts after that look.
- **A write compares again before each try** (#323; maintainer decision,
  2026-11-04). A write that replaces a file gives the store what it built on, and
  the store compares just before each try, the first one too, reading as a view does.
  On Windows the move is refused while a program has the file open, and the store
  retries for about 300 ms. Without the comparison, a program that started saving
  after the write's own comparison, stalled, and closed the file during those
  retries had its save replaced, and the write reported success. Now a file that's
  different, even by part of a save still under way, or gone, is `stale_file`, naming
  it. A file only held, as a virus scanner or indexer holds it, is unchanged, so it
  retries as ever. One that can't be read uses up a try without a move, and the next
  look decides. If the tries run out, it's the refusal for a file in use, as before. In a
  whole-den write, what was already written is put back, each file only if it still
  holds what was written. Putting back and each step of a move compare too. If
  everything was put back, it's `stale_file`. If not, reading again and retrying isn't
  the answer, so it's `write_failed`, as a write left part way always is, with no
  `current`, so a page shows the message. The message sorts what's left by what's in
  it now: a file that holds the edit can be restored from history, and one another
  program saved since holds that save, so it says not to.
  `builtOn` has no default, so a write has to say `null` on purpose.
  **The gap isn't closed:** a program can save in the moment between the last
  comparison and the move, and Wolfden can't tell. A save that stalls can't land there,
  since the file is open and the move is refused, but a temp-and-rename save, or one
  that opens, writes, and closes at once, can. Measured on Windows only, with real
  processes and the same delays on both builds, on saves aimed at the call. Stalled in
  place (starting 0–120 ms in, stalling 100–250 ms): 0 of 300 lost by section adds, and
  12–13 of 300 on `main`. Temp-and-rename, aimed across the call: 6 of 300 lost by
  `append_section`, 4 of 300 by a web save, and 2 of 300 by `rename_entity`, against
  18, 21, and 63 on `main`. A file deleted in the gap can come back in the same way:
  2 and 5 of 100, against 65 and 84. Those aren't everyday rates. Linux and macOS have
  no retries, but do compare before the one try. `check --fix` doesn't use the store,
  so it has no retry, but it compares just before its one write and skips a file that differs.
- **Watched** (#14; v0 behaviour, not a recorded decision). The file
  watcher collects a burst of saves (an editor's temporary file and
  rename, a git checkout) into one batch, and ignores hidden paths such as
  `.wolfden/` and `.git/` (format.md F-LAY-2). For a batch of changed
  entity files and outlines it reads only those files again, then redoes
  the steps that span files: duplicate ids and resolving every link. A
  change to `den.yaml`, a pack, or a universe, series, or book file, a
  folder added, moved, or deleted where it can change the structure, lost
  events, or an index someone else rebuilt meanwhile gets a full rebuild
  instead. Files the index doesn't read, such as a `README.md` at the den
  root, change nothing, not even the files hash (#103). Each
  batch is written in one transaction, so a query sees the index before
  the batch or after it. After it commits, the watcher checks that the
  index file it wrote is still the one in place, since on Linux another
  process's rebuild can replace it mid-batch (#106), and rebuilds if it
  isn't. A window remains, as known v0 behaviour: a rebuild in another
  process that read the files before a save, but swaps its index in
  after the watcher's check, leaves an index without that save. It
  heals itself: the watcher's next batch finds the index isn't the one it
  wrote and rebuilds, and a `query` meanwhile sees that the files hash
  differs and rebuilds too. Closing the window would need a lock shared
  across processes for the whole rebuild. The index always ends up as a full rebuild of the
  same files would make it, apart from the ids of rows that came from
  single files, which queries never order by.

**Canon as of** is the main query. Given a position, it returns each
entity's field values and sections in effect at that point, leaving out
`idea` and `retconned` material. Point-in-story reading, spoiler-safe
reference and continuity checks all build on it.

**`wolfden check`** (continuity lint), v0 rules:

- Broken or ambiguous links, including a link that matches only an entity
  in another universe.
- Missing required fields or sections.
- Positions that refer to books or chapters that don't exist.
- Overlapping temporal values for the same field.
- Duplicate IDs (for example, from copying files by hand).
- **Spoiler checks**, off by default: a link to an entity the reader
  hasn't met yet at the link's position, such as a reference from an
  earlier book to an entity that first appears in a later one, and a name
  readers learn later written as plain text before they learn it.

**Decided (#68): spoiler checks are opt-in.** The positions on facts record
where the reader learns each one, so a writer can pace the reveals. Not
every writer tracks that, so the check that helps them is off unless
`den.yaml` sets `spoiler_checks: true` (format.md F-DEN-7). It sees links,
not prose, so summary prose that runs ahead of the story is the writer's
to place (format.md §9.5). The one exception is a name readers learn later
(#67): with spoiler checks on, `check` also looks for a `known_as` name written
as plain words before its `from`, and for a file name that holds one
(F-NAME-1, F-NAME-2). Names of one word are looked for only on request, and
`allow_in` silences a note. A first look at each file's raw text for the names'
longest words keeps it cheap, and the pass over the den runs only in `check`,
never on a write or an index update, nor when the web reads the den's errors.
A write runs the same code on the notes it saves, and nothing else: its
`effects` list the F-NAME-1, F-NAME-2, and F-LINK-13 (by a later name) warnings
those notes didn't have before. They're compared by rule, the note the name
belongs to (by path, so renaming it changes nothing), the name, where readers
learn it, the text's position, and a link's text, not by line; repeats are
paired by their lines' text first, so a new mention above an old one is the one
said. A `change_alias` that renames a later name says each early link it
rewrites again under the new name, a departure from §9's "the same warning as
before" in #67's design, since the writer may not know those links are there.
It never refuses the write. With spoiler checks off, or no `known_as` in the
universe, it reads nothing more: a write that loads only its own note asks the
index, in the lookup it already makes, whether the universe has any `known_as`.

Later rules: a character appearing after their death, travel times that
don't fit the timeline, and open threads at a frozen book's end.

### The command line (v0)

**v0 behaviour, not a recorded decision** (#17). The CLI has thirteen
commands, and `wolfden <command> --help` describes each one:

| Command | What it does |
|---|---|
| `wolfden init [<folder>] [--name <name>] [--universe <key>] [--git [--no-checkpoint] [--author-name <name>] [--author-email <email>]]` | Creates a den: a `den.yaml`, and with `--universe` a first universe with an id. It never overwrites anything, and refuses a folder inside another den or one that holds a den up to four folder levels below it (format.md F-LAY-1; the downward search skips hidden folders and folder links, and stops after 2,000 folders). The new den passes `check` with no warnings. By default the den is a plain folder; `--git` also turns on its history (maintainer decisions, #57, #122, #195; §6, "Turning history on"): `git init`, `.wolfden/` in its `.gitignore`, a name and email if they're given (they win over git's for this den, #286) or git has none, and a first checkpoint, "Start of history", unless `--no-checkpoint`. A new den already inside a repository is left as it is, and `init` names the repository (§6, "A den inside a larger repository"). Without git installed, the den is still made, and `init` says the repository wasn't (exit 0); if a step fails, or git needs a name or email that wasn't given, the den is kept and `init` exits 2; a name or email given for a new den in a repository refuses (exit 2) before anything is made. On a folder that's already a den, `init` refuses (exit 2) unless `--git` is given: then it turns on that den's history, refusing `--name` and `--universe`, a den inside a larger repository, and one whose own repository already has checkpoints. |
| `wolfden check [<den>] [--universe <key>] [--no-index] [--fix]` | The continuity lint above. It reads the files directly, not the index. It checks the whole den, every universe included, since it validates files rather than a story view; `--universe` checks only that universe's files and the den-level ones (see "Which universe" below), and `*` means the whole den. Without `--fix` it never writes the den's own files; `--universe` writes only the index (derived data, `.wolfden/`), and `--no-index` keeps it from doing even that. `--fix` first gives every container and entity file that loads without an `id` a new one (format.md F-ID-2, F-ID-6), skipping files in a final book (F-BOOK-1), and then checks the den as it is after the fix. |
| `wolfden index [<den>] [--watch]` | Rebuilds `.wolfden/index.db` from the files and says what it holds. `--watch` then keeps it up to date as files change (#14), printing one line for each batch of changes it applies (with `--json`, one JSON object per line), until Ctrl+C. Only one watcher runs per den; a second exits 2. |
| `wolfden query [<universe>] [--universe <key>] [--at <position>]` | Canon as of a position (format.md F-VIEW-1), from the index, of one universe (see "Which universe" below; `*` is refused, since a view is of one universe). `--series` or `--book` narrows the view, `--type` (any type an enabled pack defines, with its kinds, types.md F-TYPE-13) and `--entity` (matched as a link is, F-LINK-6, among the entities in the view, so one the reader meets later can't shadow one they've met) pick entities out of it, `--pinned` gives pinned canon (F-VIEW-2), and `--brief` lists one line per entity. |
| `wolfden new <type> <name> [--in <scope>] [--from <position>] [--slug <slug>] [--allow-relinks]` | Creates a note from its type's template (types.md F-TPL-6), as the MCP tool `create_entity` does (§8, #29): a new id, the name, and a file name made from it (`-2`, `-3` when it's taken; a `--slug` that's taken is refused). `--in` names where it goes, read exactly as `copy --to` is (the same code, with its messages naming `--in`); without it, the den's universe, when it has one, and with several the error says to name one `with --in`. `--from` gives the position where the reader first meets it, which must resolve in the universe; without it, the note has no `from`. A name that would take links from the entity they reach now (§8, "Creating") is refused, listing them, unless `--allow-relinks`, as for `copy`; the output then lists the links that moved. It writes the den's files only, and exits 2 when the create is refused (an unknown type or scope, a final book, a slug that's taken, a `--from` that doesn't resolve, or a name that isn't one line of at most 200 characters). |
| `wolfden new series <name> [--in <universe>] [--after <key>] [--first]`, `wolfden new book <name> [--in <universe or series>] [--after <key>] [--first] [--chapters <n>]` | Creates a series, or a book in a series or standalone in the universe, as the web UI's **New series** and **New book** do (§9, #213; `series` and `book` are reserved type keys, types.md F-TYPE-2, so they never name a note's type): the folder, its key made from the name, and its `series.md` or `book.md` with a new id, the name, and with `--chapters` (1 to 1000) chapters `"1"` to `"N"` numbered 1 to N. It goes in story order at the end, `--after` the work named, or with `--first` at the start (not with `--after`), in the series' `books` or the universe's `order`. `--in` is read as for a note, except that a universe's key alone always names the universe (a book holds no series or books), and a book or a series named for a series is refused. It reads the file it edits and writes it in one go, so it passes no hash, as `freeze` does. `--from`, `--slug`, and `--allow-relinks` are for notes and are refused here, as `--after`, `--first`, and `--chapters` are for a note. A bare `--in` key that names no universe, in a den with several, is refused with a hint that names universes and series only. It exits 2 when the create is refused (§9, "New series and books"). |
| `wolfden new chapter [<title>] --in <book> [--after <chapter>]` | Adds a chapter to a book's `chapters` in its `book.md`, as the web UI's **Add chapter** and **Insert chapter…** do (§9, "Adding chapters", #215), with the title if one is given: at the end, the next whole-number key, or `--after` a chapter, that key with a letter added (`2b`), renumbering the `number`s after it; keys never change. It's a `new`, beside `new series` and `new book`, since it makes something (`chapter` is a reserved type key too, types.md F-TYPE-2), rather than a separate `chapter` command for one action. `--in` names the book: its key, `book:<key>`, or `<universe>/<book>`; a bare key is the book in whichever universe has one, so `--in dracula` in the Dracula sample is the book, though it's the universe's key too (only a book holds chapters), and a key two universes' books share is refused with both spellings. It reads `book.md` and writes it in one go, so it passes no hash. `--first`, `--chapters`, `--from`, `--slug`, and `--allow-relinks` are refused. It exits 2 when the add is refused: no `--in`, no such book or chapter, a final book, or a title that isn't one line of at most 200 characters. |
| `wolfden copy <entity> <new name> [--to <scope>] [--universe <key>] [--slug <slug>] [--allow-relinks]` | Copies an entity under a new name, with a new id, as the MCP tool `copy_entity` does (§8, #129): beside the original, or `--to` a universe (`<universe>`, or `<universe>/`) or one of its series or books (`<universe>/<key>`, or `<universe>/series:<key>` or `<universe>/book:<key>` when a series and a book share the key). In a den with one universe, a bare series or book key (or `series:<key>`, `book:<key>`) will do (as for `check`, #127), unless it's also the universe's key, which is refused with every spelling suggested. It writes the den's files only. Its messages name its options (`--allow-relinks`, `--slug`) and commands (`wolfden check`), not the tool's parameters: the tools take the words their advice uses as a setting, and names the writer gave come through as written. It exits 2 when the copy is refused. |
| `wolfden freeze <book> [--universe <key>]` | Freezes a finished book (§5, #31): `status: final` in its `book.md`, then, when the den has git history, a checkpoint of the whole den and a tag on it, `book/<universe>/<book>/final` (or `final-2`, `final-3`, … for a book frozen before). `--universe` names the book's universe, when the den has several (see "Which universe" below). Freezing a book that's already final finishes a freeze whose checkpoint or tag failed, and otherwise does nothing. It prints the result's message, and exits 0 when the book is frozen (with its snapshot, or with no history to take one in), and 2 when it's refused (no such book, or a section an error leaves out, F-GEN-2) or a step in the history failed; then the message, on standard error, says what to do. |
| `wolfden unfreeze <book> [--universe <key>]` | Unfreezes a book (§5, #31): `status: draft` in its `book.md`, then a checkpoint, with no tag, when the den has history. A book that isn't final is left as it is. It exits 0 when the book is unfrozen (or wasn't frozen), and 2 when it's refused or the checkpoint failed. |
| `wolfden new-id [<count>]` | Prints new ids for files made by hand (format.md F-ID-5). |
| `wolfden serve [<den>] [--universe <key>] [--port <n>] [--no-open] [--no-watch] [--home <folder>]` | Serves the web UI and the API on 127.0.0.1 (§9, #23), opens the default browser unless `--no-open`, and runs until Ctrl+C. The port is 9653 by default (WOLF on a phone keypad); `--port 0` picks a free one, and the address is printed either way. A port in use, or one the system won't let it use (reserved, or below 1024 on Linux), exits 2 and says so. `--universe` serves only that universe, as `mcp --universe` pins one; a key the den doesn't have exits 2. It hosts the same watcher as `mcp`, unless `--no-watch`. It's the one command that makes automatic checkpoints, when `den.yaml` sets `auto_checkpoint` (§6). Without a built UI, the API still works and the page says how to build the UI. When the installation (`--home`, see `user`) has login on, it warns once that this version doesn't ask for a login yet (#315); it never stops for that. |
| `wolfden user add <name> (--role <role> \| --owner) [--password-stdin]`, `user list`, `user remove <name>`, `user set-role <name> <role>`, `user reset-password <name> [--password-stdin]`, `user unlock <name>`, each with `[--home <folder>] [--json]` | The accounts of this installation (#315): ASP.NET Core Identity's stores in `accounts.db`, in the **installation folder**, never in a den and never pushed. That folder is `%LOCALAPPDATA%\Wolfden` on Windows, `~/Library/Application Support/Wolfden` on macOS, and `$XDG_DATA_HOME/wolfden` (or `~/.local/share/wolfden`) elsewhere; `WOLFDEN_HOME` (a full path; a relative one is refused) moves it, and `--home` beats both. A folder that is a den or inside one is refused, by name and by where any symbolic link or junction along its path really leads, so a checkpoint can't record the accounts; one inside a git repository is allowed with a warning. Outside Windows, `accounts.db` is made 0600, and a folder other accounts can open gets a warning. It also holds `settings.json` (whether login is on) and, later, the cookie keys. A role is owner, writer or viewer: what the account may do, not what it sees (the Reader's view stays a filter anyone can turn on). `add` needs a role (`--owner` is `--role owner`), and asks for the password twice without echo, or reads one line from standard input with `--password-stdin`; a password is never a command-line argument. A password is 15 to 256 characters (15 is NIST SP 800-63B rev. 4's minimum for a single factor; maintainer decision, 2026-11-10), counted as Unicode code points after NFC normalization (which is also the form hashed), with no composition rules; the username itself and one character repeated are refused, and so is (#354) a common password from a public-domain list of 10,000 built into `Wolfden.Accounts` (`src/Wolfden.Accounts/CommonPasswords/README.md` says where it comes from; no network calls), the username, or `wolfden`, compared ignoring case, spaces and punctuation and refused when said once or repeated, optionally followed by digits no longer than the repeated part. A username is 1 to 64 letters, digits, `.`, `-` and `_`, compared without regard to case. No email or phone is stored: those columns aren't in the database. `add --role viewer` and `set-role … viewer` say that a viewer sees everything a writer sees, spoilers included. Removing or demoting the last owner is refused. `set-role` and `reset-password` end the account's sessions (a new security stamp); `reset-password` also ends a lockout. After 5 failed logins an account is locked for 15 minutes, and `unlock` ends that at once. `list` names the installation folder and whether login is on, and creates nothing. Each exits 0 when it did its job and 2 when it's refused or the installation can't be read. |
| `wolfden login [on \| off] [--home <folder>] [--json]` | Turns login for this installation's web pages on or off in its `settings.json`, or with neither says whether it's on; it always names the installation folder. `on` is refused (exit 2) while there's no owner, so nobody can turn it on and lock themselves out, and prints what login protects and what it doesn't (the files, the CLI, stdio MCP, backups and sync). `off` always works: a `settings.json` that `login` refuses is first copied to `settings.json.broken`, then, if it's still one JSON object, only its `login` is set to false and its other keys are kept; otherwise (bad JSON, or a setting named twice) it's replaced with one that turns login off. It says which. Nothing enforces the setting until the web's login lands (#315 PR 4b): `login` and `serve` say so, and `--json` reports `enforced: false`. |
| `wolfden mcp [<den>] [--universe <key>] [--no-watch]` | Runs the MCP server over stdio for an AI client (§8, #20) until the client disconnects or Ctrl+C. `--universe` pins the session to one universe (§8); a key the den doesn't have exits 2. Standard output carries only the protocol; log messages go to standard error. It exits 2 if there's no den and 3 if it stops because of a failure after it started, but serves a den whose `den.yaml` doesn't load, so the client can call `check` to find out why. |

They share one surface:

- **Which den.** `--den <folder>` names it (`check`, `index`, `serve`, and
  `mcp` also take it as a plain argument; `init` takes the new den's
  folder as one, and has no `--den`). Otherwise it's the current folder, or the
  nearest folder above it that holds a `den.yaml`, as git finds its
  repository.
- **Which universe** (maintainer decision, 2026-10-23). A den with one
  universe uses it. In a den with several, a command that reads a story
  view (`query`, and the MCP tools, §8) needs one: named with
  `--universe` (or `query`'s plain argument), or settled by a position
  whose book key only one universe has (a book belongs to one universe,
  format.md F-POS-5). A key in more than one universe, or no universe
  and no position, is an error that lists the universes and nothing
  else. `*` (every universe) is accepted only where a search or list
  makes sense, and never with a position. `check` is the exception: it
  validates files, not a story view, so it checks the whole den unless
  `--universe` names one (`*` is the whole den too). Then it reads that
  universe's files and the den-level ones every universe shares
  (`den.yaml`, packs, authors, and the den's layout), and of the other
  universes only their container files, so positions resolve as in the
  whole den; their notes and outlines aren't read, and their findings
  are left out. Ids are still compared across universes (maintainer
  decision, 2026-10-23): the ids the other universes' notes use come
  from the index, in one query, after the index is brought up to date
  (hashing the whole den, which costs more than the check of one
  universe, but a stale index could miss a duplicate). The universe key
  is checked first, from `den.yaml` and the container files, so an
  unknown key is reported without that wait and leaves the index alone
  (#130). An id shared with
  a note in another universe is a duplicate in the scoped check as in
  the whole den: the same F-ID-3 warning, naming the other copy, and the
  same broken links. If the index can't be built, or `den.yaml` doesn't
  load (then no index is built), the rest of the check runs and a note
  says the id comparison was skipped. `--no-index` skips it on purpose,
  with the same note, for a check that writes nothing at all, not even
  the index. The one remaining difference from a
  whole-den check: a broken link that only another universe's note
  matches is reported without the hint that names that note. `--fix`
  works on the whole den, so it doesn't go with `--universe`.
- **Output.** Text by default. `check` prints one line per diagnostic,
  `path:line: severity rule: message`, with paths relative to the den
  root, then a summary. `query` prints each entity as a heading, its
  fields as a list, then its lead and sections, each marked with any
  `from`, `until`, `pinned`, or `draft`. `--json` prints one JSON
  document instead, with a `version` for its shape. Within a `version`,
  fields may be added, but renaming or removing a field, or changing what
  one means, needs a new `version`. Results go to standard output, and
  mistakes and failures to standard error.
- **Exit codes.** 0 when the command did its job; for `check`, when the
  den has no errors (warnings alone pass, format.md F-GEN-1); for `index
  --watch` and `serve`, when it's stopped with Ctrl+C; for `mcp`, when the client
  disconnects or it's stopped with Ctrl+C. 1 only from
  `check`, when the den has at least one error. 2 when the command
  couldn't do its job: a wrong command line, no den, a port `serve` can't
  listen on, a `den.yaml` that
  doesn't load, a universe, position, filter, type, or entity that doesn't
  exist (for `query`, a den with no universes yet, or with several and
  none named), or an unexpected failure. A `query` whose view is empty,
  such as one for a character the reader hasn't met yet, exits 0. 3 only
  from `mcp`, when the server started but then stopped because of a
  failure.
- **A fresh index.** `query` reads the index, but first compares a hash
  of the files the index reads (`den.yaml`, packs, container files,
  entity files, and outlines) with the one recorded when the index was
  built, and rebuilds it if they differ, so an answer never
  lags behind an edit. `index --watch` keeps both the index and that hash
  up to date as files change, so while it runs a query rarely needs to
  rebuild: when the hash differs and a watcher is running, `query` first
  waits up to about a second for it to catch up (#108). The long-running
  `mcp` hosts the same watcher (unless `--no-watch`, or another Wolfden
  is already watching the den), and checks the hash the same way before
  each tool call; `serve` does the same, before each request.
  `--watch` keeps going when `den.yaml` stops loading, so the writer can
  fix it, and reports a batch that fails on standard error, then rebuilds.

## 8. AI integration (MCP)

Wolfden ships an MCP server, using the official C# MCP SDK, over the same core
library the API uses. `wolfden mcp` runs it over stdio (§7); how to add
it to Claude Code, Claude Desktop, and other clients is in
[`mcp-clients.md`](mcp-clients.md). Tool surface:

| Tool | Status | Purpose |
|---|---|---|
| `search` | shipped (#20) | Full-text search across the den, or one universe or type; field values too, or one field's (#179); optionally `as_of` a position (#67) |
| `get_entity` | shipped (#20) | One entity, optionally `as_of` a position |
| `list_entities` | shipped (#20) | By universe, type, status, series, or book; optionally `as_of` a position (#67) |
| `canon_as_of` | shipped (#20) | Canon at a position, with an optional series or book filter |
| `check` | shipped (#20) | Run continuity checks and return findings |
| `get_history` | shipped (#19) | Commit history and diffs for an entity |
| `get_chapter` | shipped (#21) | A book's chapters with each chapter field's own, effective, and inherited value, each chapter's heading (#227), and `book.md`'s hash |
| `create_entity` | shipped (#21) | From a type template |
| `update_fields` / `append_section` | shipped (#21) | Structured edits that keep everything else intact |
| `add_idea` | shipped (#21) | Record undecided material with `status: idea` |
| `rename_section` | shipped (#21) | Rename a section and rewrite the links to it, den-wide (format.md F-RT-5) |
| `rename_entity` / `change_alias` | shipped (#150) | Change an entity's name, or change or remove an alias, rewriting the links that used the old text den-wide, or keep the old text as an alias (F-RT-5) |
| `move_entity` | shipped (#89) | Move an entity to another scope of its universe, keeping where it enters canon and rewriting links that would stop reaching it to its id (F-RT-5) |
| `copy_entity` | shipped (#129) | Copy an entity under a new name, with a new id and file, beside it or into another scope or universe |
| `update_chapter_fields` | shipped (#21) | Set, add to, remove from, or clear a chapter's own fields |
| `get_chapter_defaults` | shipped (#151) | A universe's, series', or book's `chapter_defaults`, what its books would inherit from above, the books it reaches, and its file's hash |
| `update_chapter_defaults` | shipped (#151) | Set, add to, remove from, or clear a field of a universe's, series', or book's `chapter_defaults` |
| `checkpoint` | shipped (#21) | Commit with a message, naming the agent as co-author |

Writes go through the same validation as the UI. An agent can't save a file
the parser would reject.

`canon_as_of` and `get_entity` serve **pinned canon** first
([`format.md`](format.md) §9.4), so an agent sees the hard facts before
the rest.

**The read tools (#20; v0 behaviour, not a recorded decision).** They
reuse the index's queries and `check`, so they answer as `wolfden query`
and `wolfden check` do, and the web API's read endpoints answer from the
same code (§9):

- **Positions are opt-in.** Without one, `get_entity`, `list_entities`,
  and `search` give the author's full view: every entity and fact,
  whatever its status, each marked with its status, pin, and `from` and
  `until`. `canon_as_of` without a position is the end of the story, as
  in `query`. With a position, `canon_as_of` and `get_entity` give the
  canon-as-of view (format.md F-VIEW-1), so nothing the reader meets later
  is in the result. `get_entity` then matches the
  name, every tier of it (F-LINK-6), among the entities the reader has met
  by then, so an entity met later can't shadow one met already, tie with
  it, or show up in an `ambiguous_entity` error. Only when nothing there
  matches does it ask all the entities, to tell one the reader hasn't met
  (`in_view: false`, and nothing about it) from one that doesn't exist
  (`unknown_entity`). A name readers learn later (`known_as`, format.md
  F-VIEW-7, #67) matches in neither step, so it's `unknown_entity`, word for
  word as a made-up name, and never `in_view: false`, which would confirm it.
- **`search` and `list_entities` take `as_of` too** (#67, maintainer decision
  2026-11-05), so an assistant drafting a chapter can't search or list its way to
  later notes. `search` with `as_of` calls `DenReader.SearchAsReaders`, the Reader's
  view's search (§9), so there's no second rule, and no marks: those stay in the web's
  Author's path, in `Wolfden.Api`, which `Wolfden.Mcp` can't reach. `list_entities` with
  `as_of` lists the canon-as-of view's entities, and its `total` counts only those. A
  series or book that doesn't exist is refused there without naming the others. An
  unreadable position is `bad_position`, as in `canon_as_of`, and `universe: "*"`
  is refused. `as_of` is per call, so an assistant can still forget it; a session
  default (`wolfden mcp --at`) is left for dogfooding.
- **Names at a position** (#67, maintainer decisions D1 to D4, 2026-11-05). A name
  readers learn partway through the story goes in `known_as` with its `from`
  (format.md F-ENT-10), beside `aliases`, which hold for the whole story. At a
  position, a name is known if its note is in the view and it's the name, a plain
  alias, or a `known_as` value whose `from` it has reached (F-VIEW-7). So
  `get_entity`, `canon_as_of`, and `search` with `as_of` list the known names in
  `aliases`, as plain strings (D3, reversing the #300 call that a Reader's search
  doesn't match aliases), and a name not known is no name at all: `unknown_entity`,
  no hit, not listed. A link written by such a name reads as written by the note's
  name (D4), in text, headings, and field values, so an assistant drafting an early
  chapter isn't handed the name by the writer's own link. It's in `AsOfReads`, from
  link rows that keep the point of the `known_as` value they matched, so the web's
  Reader's view does the same. Link resolution doesn't change: a link reaches its
  note at every position, so renames and backlinks need no position. With
  `spoiler_checks`, F-LINK-13 warns about such a link. Writes take no position;
  `bad_position` names no book but the one asked for (#67, option C).
- **A future `until` is left out** (maintainer decision on #288). With a
  position, `canon_as_of` and `get_entity` leave out each fact's `until`
  (format.md F-VIEW-4), since it tells an assistant drafting that chapter
  that a "truth" gets overturned, and it could write toward that. To plan
  ahead, an assistant reads `get_entity` without `as_of`. It's done by
  `AsOfReads` in `Wolfden.Index`, which these tools always apply, and the web
  API's Reader's view (`readers=true`, §9) applies too, so there's one rule for
  the two. `query`, and the web API's other reads, keep the date for the writer.
- **A later position value is left out** (maintainer decision on #305).
  With a position, `canon_as_of` and `get_entity` leave out each value of a
  `position` field that's later than it (format.md F-VIEW-5), since a thread's
  `paid_off` says when it resolves, just as an `until` does. A value that
  can't be compared goes too. It's in `AsOfReads` as well, so the web API's
  Reader's view leaves it out, and `query` and its other reads keep every value. The index keeps which fields of each type are
  `position` fields, since a field's type isn't in its value.
- **Comments are left out at a position** (maintainer decision on #306). With a
  position, `canon_as_of` and `get_entity` leave out every HTML comment in a lead
  or section (format.md F-VIEW-6), since a comment can name a later fact, and a
  misplaced wolfden comment names a date. A comment shown as code stays. It's in
  `AsOfReads` too, so the web API's Reader's view leaves them out, and its other
  reads, `query`, and reads with no position keep them.
  No MCP tool replaces a section's or lead's text, and `append_section` leaves
  comments in place. `rename_section` given a heading's full name drops a comment
  in that heading, but only a read with no position shows the full name: with the
  name a read at a position shows, it's refused, and its message lists headings
  without their comments. An assistant editing a file by other means is told to
  read with no position first.
- **Pinned first.** Results of `canon_as_of` and `get_entity` start with
  `pinned`, the pinned canon view (F-VIEW-2), and list the same facts in
  place too, so each entity is complete.
- **Naming things.** `get_entity` takes a name, alias, file name, or id,
  matched as a link is (F-LINK-6), or a key or path. A series or book
  filter means what it means in a canon view (F-VIEW-1), for
  `list_entities` too.
- **One universe per call** (maintainer decision, 2026-10-23; §7, "Which
  universe"). Every tool but `check` reads one universe: the den's only
  one, the one named in `universe`, or the one a position's book belongs
  to; otherwise `universe_required`, which lists the universe keys and
  never entities. So an ambiguity never spans universes. `universe: "*"`
  reads every universe, in `search` and `list_entities` only, never with
  a position or a series or book filter; elsewhere it's `bad_argument`.
  `check` checks the whole den unless given a universe, and `"*"` means
  the whole den; a scoped check compares ids across universes through
  the index, as in §7, and its `note` says when it couldn't.
  `wolfden mcp --universe <key>` pins the session (`*` can't be pinned): every
  call reads that universe (`check` checks it and the den-level files),
  a call that names another universe or `"*"` gets `bad_argument`, and
  the server instructions say so.
- **Results** are JSON with snake_case names, like the tools' arguments,
  sent both as text and as structured content, with a `version` for
  their shape (the same rule as `--json`). Errors are error results whose
  JSON holds a stable `code` (such as `bad_position`, `unknown_entity`,
  `ambiguous_entity`, `den_not_loaded`) and a message that says what to
  do next. Arguments that don't bind to a tool's parameters (a required
  one missing, or a value of the wrong type) are reported by the SDK in
  its own words; the server wraps that as `bad_argument` in the same
  shape. In every result, `count` is how many items it holds, `truncated`
  says whether `limit` cut the list short, and `list_entities` and
  `check` also give the `total` that matched. A `search` with a filter
  (`universe` in a den with several, `type`, or `canon_only`) filters a
  capped set of raw hits (1,000, or 20 times `limit` if that's more), so
  its `truncated` is also true when there were raw hits past that cap,
  which may hold more matches. `canon_as_of` is never cut short.
- **Fresh answers.** Each call checks the files hash first and rebuilds
  the index if it differs, waiting briefly for the watcher the server
  hosts (§7). `check` reads the files, not the index, except that a
  check of one universe takes the other universes' ids from the index
  (brought up to date first) to find ids shared across universes
  (F-ID-3, §7 "Which universe").
- **History** (#19). `get_history` names an entity as `get_entity`
  does, without a position, since history isn't a story view, and gives
  the checkpoints that changed its file, newest first, a page at a time:
  `limit`, then `before` set to the result's `next_before`. With `diff`
  it adds what changed in the file since the latest checkpoint, and with
  `diff_from`, since that checkpoint. A den without history isn't an
  error: `available` is false, and `reason` says why in the writer's
  words. A `before` or `diff_from` that isn't a checkpoint of that
  history is `unknown_checkpoint`; a git failure is `history_failed`.
  `wolfden mcp` looks for the den's repository on each call, so a den
  made a repository while it runs gets history. A note with errors,
  which the index leaves out (F-GEN-2), can still be named by its path,
  since that's when the writer most wants to see what changed (#181);
  the index knows only its path, so its summary's name is the file
  name and its type and status are empty.
- **Hashes.** `get_entity` gives the SHA-256 of the entity's file as
  the index read it (`hash`), `get_chapter` the hash of `book.md`, and
  `get_chapter_defaults` the hash of the container's file: the
  `expected_hash` the write tools take.
- **Serve first, watcher in the background.** The server answers the
  client before the watcher's first build of the index is done, so a
  slow build never delays the handshake. A client that waits too long
  for `server/discover` falls back to `initialize`, which the server
  refuses once it has answered the late probe (#135). If the watcher
  can't start because of an error that can clear, such as a call holding
  the index open on Windows while the first build replaces it, or SQLite
  finding the index busy, the server tries again a few times before
  going on without it. Other errors, such as `.wolfden` being a file,
  don't clear by themselves, so it goes on without a watcher at once
  (#137). The server doesn't pin
  an MCP protocol version: it serves both the 2026-07-28 revision and
  the `initialize` handshake, so older clients keep working.

**The write tools (#21; v0 behaviour, apart from the maintainer
decisions marked).** They reuse the document edit API and the den
store, so an edit changes only what it means to (format.md §12):

- **Checked as the den reads it.** Before anything is saved, each file
  is read as a load of the den would read it (`DenWriteCheck`): an edit
  that would add an error is refused (`invalid_edit`, with the errors),
  so an agent can't save a file the parser would reject. A bad section
  comment already in the file (F-GEN-2, #50) doesn't block an edit, so a
  note with a typo can still be worked on: it's known across the edit by
  its rule, attribute, and value, counted, never by its line or heading.
  The result lists the file's warnings, from the same checks as `check`
  for that file's own fields, and any such error it kept.
- **Stale files** (maintainer decision, 2026-10-23). Every edit of an
  existing file takes the hash it was based on, and is refused
  (`stale_file`) if the file has changed since; the file is compared
  again just before it's written. Each result gives the new hash, so
  edits can follow one another without reading again. Writes are made
  one at a time, across processes too (#221): each holds the den's
  write lock, `.wolfden/write.lock`, from reading the files it's based
  on until the last one is written or put back, so `serve`, an MCP
  session, and a `wolfden new` take turns, and none saves over another's
  change. The lock is the file held open with no sharing, which the OS
  lets go of when the process ends, crash included, so a stale lock
  can't happen and no process id is guessed at; the file staying behind
  means nothing. It's one lock for the whole den, so a rename's den-wide
  link rewrite can't deadlock with another write. A write that finds it
  held waits, then gives up with `den_busy` and writes nothing; a
  command says once that it's waiting, and for how long. One 30-second
  wait covers both the wait for the session's own earlier writes (such
  as `serve`'s other requests) and the wait for other Wolfdens, so a
  write queued behind several gives up after 30 seconds, not 30 for each.
  A call that's cancelled while it waits (an MCP client's cancel, or a
  browser that goes away from `serve`) stops waiting at once and writes
  nothing (#234); it ends as a cancelled call does, not with `den_busy`.
  `serve` hears that a browser went away only when it handles the
  closed connection, which is usually within milliseconds but on a busy
  machine can be later; if the write's turn comes first, it's saved, as
  if the browser had gone away just after.
  Only a sharing or lock violation counts as held (on Windows; on Linux
  and macOS, `flock`'s `EWOULDBLOCK`); any other failure to open the
  lock file is `write_failed` at once. `check --fix` takes the lock too.
  Freezing and unfreezing hold it around their whole change, git commit
  and tag included, so a freeze waiting on a signing passphrase makes
  other Wolfdens' writes `den_busy` after 30 seconds. `checkpoint` (the
  tool, the API's `/checkpoint`, and automatic checkpoints) doesn't take
  it: it writes no den file, git has its own lock, and a save mustn't
  wait on a checkpoint that's waiting on git (SC-WEB-53). The cost: a
  checkpoint can record another process's multi-file write (a rename's
  link rewrites, a move) half done, if git reads the files while that
  write is part way through. Nothing is lost on disk, and the next
  checkpoint records the rest. Taking the lock only while the files are
  staged, then committing and signing outside it, wouldn't close this
  (#234): a checkpoint is `git commit --only -- <den paths>`, which
  reads the files from the disk itself as it commits, whatever was
  staged before. Closing it without holding the lock across the commit
  would mean committing a tree staged under the lock (a temporary
  index, then a commit of it), a change to how every checkpoint is
  made. The `checkpoint` tool holds its own session's turn across the
  commit, so that session's next write waits for it, and `den_busy`
  names "other writes to the den, or a checkpoint". Other programs
  don't know the lock, so for an editor a small window remains, as with
  the den store (§6): it can save the file after that last comparison
  and before the write replaces it. Closing it would need a lock every
  editor honours.
- **Frozen and pinned** (maintainer decision, 2026-10-22). Nothing in
  a final book's scope is written (`frozen`): its entities, `book.md`,
  or outline (format.md F-BOOK-1). There are no tools to freeze or
  unfreeze a book (maintainer decision, 2026-10-28, #31): that's the
  writer's, in the web UI or the CLI (§5), and a `frozen` error is the
  agent's cue to ask them. Each one ends with what to do next, in the
  caller's words (`ToolWords.Unfreeze`, #209): for MCP, "Ask the writer
  whether to unfreeze book 'dracula'; only they can."; on the web page,
  **Unfreeze…** on the book's page; for the CLI, `wolfden unfreeze
  <book> --universe <key>` (with `--den <den>` when the command was
  given one). One that names files in several final books says it
  for each. The entity page's and the source's `frozen` reason
  ends the same way. Edits to pinned canon are made, and
  `pinned_canon` in the result lists each pinned fact touched. A
  series' or universe's `chapter_defaults` isn't in a book's scope, but
  reaches its chapters, so `update_chapter_defaults` refuses an edit
  there that would change the effective value (types.md F-CHAP-5) of
  any chapter of a final book, and makes one the final book doesn't
  see, because its chapters or its own defaults set the field (#151; v0
  behaviour, as `rename_section` refuses to break links in a final
  book). The check fails closed: books are matched by path, since two
  can share a key (F-KEY-5); links are compared by their text and by
  the entity they reach from the container that holds the value
  (types.md F-CHAP-4), so a different spelling of the same link also
  counts as a change; a `book.md` below that the den leaves out for
  errors stops the edit if it says `status: final` or its frontmatter
  doesn't parse, and a draft one is named in the `effects`; and the
  `book.md` and `series.md` files below are compared again just before
  the write, so a book frozen meanwhile is `stale_file`, with a message
  to call `get_chapter_defaults` again.
- **Files only; commits by `checkpoint`** (maintainer decision,
  2026-10-22). Writes go to the working tree. `checkpoint` records the
  den's files (or the paths given) as one commit through the history
  provider, with the agent's `co_author` as a `Co-Authored-By` line
  (§6). A den without history isn't an error: `available` is false.
  **Decided (2026-10-25):** by default a checkpoint records the whole
  den, so it can hold edits the writer made elsewhere, and its result
  tells them apart. The server remembers the bytes its write tools last
  saved to each file. `written_by_session` lists the recorded files it
  wrote that weren't recorded since (a write the writer already
  committed some other way, which the latest commit holds byte for
  byte, is forgotten), and `changed_outside_session` lists the recorded
  files that hold anything else: ones the session never wrote, and
  ones it wrote that were changed after, which are in both lists, so
  the writer's edits are always surfaced. A file a failed write put
  back as it was doesn't count as the session's. With `paths`, only the
  files named are recorded and listed. Paths compare as the file
  system does, ignoring case on Windows and macOS. The comparison with
  the latest commit is the history provider's `HoldsAtLatest` (for
  git, the blob at HEAD against `git hash-object --path` of the bytes).
- **Ids** (maintainer decision, 2026-10-23). `create_entity` gives a
  new id that no file in the den uses, checked through the index even
  in a session pinned to one universe. `update_fields` refuses an `id`
  edit (F-ID-4). An edited entity or container file without an id gets one
  in the same write (F-ID-6), unless the edit changes nothing, which
  writes nothing. Files a rename rewrites only for their links don't.
- **Effects, not just bytes** (maintainer decision, 2026-10-23). A
  result's `effects` say what the edit means where that depends on
  more than the file: a chapter left with `pov: []` "now has no POV",
  with what it would inherit from `chapter_defaults` without its own
  value; a field whose type default now shows; an entity whose status
  takes it out of canon views; a new standard section that hands over
  from, or overlaps, an earlier one. `update_chapter_fields`'s `clear`
  goes back to inheriting (format.md F-RT-6, types.md F-CHAP-5).
  `update_chapter_defaults` (maintainer decision, 2026-10-25, #151)
  works the same way on a universe's, series', or book's
  `chapter_defaults`: removing a list's last item leaves `[]`, and
  `clear` goes back to inheriting from the container above. Its
  `effects` give each book below in runs of chapters, such as "Book
  'ember': chapters 1–2b now have pov [[ilse-tarrow]]; chapters prologue,
  3 keep their own", and name a nearer default that keeps chapters as
  they were.
- **Creating** follows types.md F-TPL-6: the path from the type's
  folder, a slug made from the name (`-2`, `-3` when it's taken; a
  slug asked for that's taken is `already_exists`), and frontmatter in
  template order with the caller's values in place of the template's.
  A `from` must resolve in the universe (`bad_position` otherwise,
  format.md F-POS-4), since an entity whose `from` doesn't is in no
  canon view; a book's entity whose `from` is in another book is made,
  with the F-ENT-6 warning in `warnings` (#29). A name, for
  `create_entity`, `add_idea`, `copy_entity`, and `rename_entity`, is one
  line (no control characters, and no U+2028 or U+2029, which would
  split the `# name` heading) of at most 200 characters (#29): room for
  any title, and the name never reaches the path, whose slug is cut to
  64. Links are checked as for a copy (maintainer decision on #29,
  2026-10-28): before the file is written, every link is resolved again
  with it there, and one that reached an entity and would reach the new
  one instead (a name in a nearer scope wins, F-LINK-7) or become
  ambiguous refuses the create (`bad_argument`, naming each link) unless
  `allow_relinks`; with it, `effects` list the links that moved, as do
  links that were broken and now reach the new entity. It's the copy's
  check, the same code, and `add_idea`'s new idea entity gets it too,
  with its own `allow_relinks`. With `entity`, `add_idea` only adds a
  section, so it refuses `allow_relinks` (`bad_argument`), as it does
  the new entity's `name`, `type`, `series`, `book`, and `fields` (#204). A `from` that's empty or only spaces is
  no `from`, as on disk (F-ENT-6). The web UI's refusal doesn't suggest
  a slug, since its create takes none.
  `update_fields` puts a new top-level key in template order too
  (F-TPL-7); `append_section` puts a new standard section in the
  type's section order and a custom one at the end (F-TPL-8).
- **Positions a write sets must resolve** (maintainer decisions on
  #211 and #217, 2026-10-29). Every entity write refuses a position it
  adds that names a book or chapter the universe doesn't have, with
  `bad_position`, as `create_entity` does for its `from`: the entity's
  `from`, a field entry's `from` and `until`, a section comment's
  `from` and `until`, and a value of a `position` field (types.md
  F-FT-10, such as a thread's `planted`). That covers `update_fields`,
  `append_section`, `create_entity`'s fields, and the web UI's edits,
  which share the edit loop (Edit file included). A create checks its
  positions before the links it would take, so the writer isn't asked
  to create it anyway and then refused. Only the positions the write
  adds count. A position that doesn't resolve and was already in the
  file at the same place (the same kind of position, of the same field
  or section by name, with the same text) doesn't stop the write. A
  section's `from` or `until` left over is then matched by its kind and
  text alone, against one that was a section's before, so renaming the
  heading in Edit file keeps it, as `rename_section` does. One moved to
  the entity's `from` or a field, or written once more, is refused, and
  so is one under a field renamed in Edit file. An empty
  value is none, as on disk: `null` in `update_fields`, and an empty or
  blank `from` or `until` for `append_section`, which then appends to a
  section that's there as if none was given. A `position` field value
  that isn't a position at all (`chapter nine`, `dracula:9.2`) is
  refused too (`bad_position`), so a worse value can't get past where
  a near miss is refused, and so is a mapping or a list where a
  position goes (`paid_off: {a: 1}`, #222). On disk each stays the
  F-FT-10 warning, so a file that already holds one loads as before,
  and the same-place rule applies to it (a mapping or a list matches
  one with the same content in the same field: its scalars by their
  text, quoted or not (F-YAML-5), and a mapping's keys in any order,
  so layout and comments don't count either). The refusal names the
  value, cut to 80 characters, and says where to
  find a chapter key in the caller's words (`get_chapter` for MCP, the
  book's chapters on the page). Its example, as for `create_entity`'s
  `from`, is in the universe's own keys: the first chapter of its
  first book, in story order, that lists one and whose key no other
  book shares (`'dracula:1'`), or `'book:chapter'` when none does
  (#222). `update_chapter_fields`, `update_chapter_defaults`,
  `move_entity`, and `copy_entity` take no position: a move writes the
  entry point the entity had, and a copy keeps the original's positions
  and says which don't resolve where the copy is.
- **Renames** (maintainer decision, 2026-10-23). `rename_section`
  rewrites every link that reaches the section by heading, in any file
  (F-RT-5; none if only its apostrophes change, since they still match,
  #308), as one change: if any of those files is in a final book,
  nothing is renamed, and if a write fails part way, the files already
  written are put back, newest first, and the result names any that
  couldn't be, or that another program changed meanwhile (those are
  left as they are). The files are written one after another, so a
  reader, such as the watcher or an editor, can briefly see some
  rewritten and others not; the next batch the watcher reads settles it. A link written verbatim in a frontmatter value is
  spliced in place, so the value keeps its style; one the YAML escapes
  is written anew. A new name that would change where existing links
  go is refused: one that a later section has (its links would reach
  the renamed one first, F-LINK-9), or an earlier one has while links
  name the section. Files the den leaves out for errors can't be
  edited, so an entity or container file whose text looks like it
  links to the section is named in `effects` for the writer to fix by
  hand (outlines with errors aren't searched).
- **Entity renames** (maintainer decision, 2026-10-25, #150).
  `rename_entity` changes an entity's `name`, and `change_alias`
  changes or removes one of its aliases; `update_fields` refuses both
  (`unsupported_edit`) and only adds aliases. By default every link that
  reached the entity by the old text (by name for a name, by alias for
  an alias) is rewritten, in any file, with the same machinery and
  guarantees as `rename_section`: links in bodies, frontmatter values,
  `book.md`, and outline rows, spliced in place with their spacing,
  `#heading`, and label kept. A removed alias's links get the name.
  Links by id or file name don't depend on the text and are left
  alone, as is the file's name; a title heading that reads the old name
  follows. Instead, `keep_old_name` (or `keep_old_alias`) keeps the old
  text as an alias and changes no link, which suits an in-story name
  change. If a link to rewrite is in a final book, nothing changes and
  the error suggests keeping the old text. Before anything is written,
  every link in the den is resolved again as it would be after the
  change: a rewritten link that would no longer reach the entity (the
  new text is another entity's file name, say) refuses the change. So
  does one that would change the target of a link that reached a
  different entity (a new name that's another entity's alias takes its
  links, since a name matches before an alias), with or without
  keeping the old text (maintainer decision, 2026-10-26), and so does
  one that would send a link that reached the entity to another one (a
  kept alias loses to another entity's name): the error
  lists each such link, its file, and its old and new target, and
  `allow_relinks` makes the change anyway. For now `allow_relinks`
  covers links in final books too; that case is a separate check, so
  it can become an always-refuse. Any other link that would resolve
  differently (a kept alias another entity also has makes it
  ambiguous, F-LINK-7), and any relinked link allowed, is listed in
  `effects`. A link that was ambiguous or broken and now resolves
  reached no entity before, so it's only reported, never refused. A change that only alters case, spacing, or which apostrophe is used changes no link
  (F-LINK-4). Renaming an entity to one of its own aliases takes that
  alias out.
- **Names readers learn later** (#67, §9 of its design). Writes take no
  position. `change_alias` finds a name in `aliases` or `known_as`
  (format.md F-ENT-10), and a changed or kept name stays in the same key
  with the same `from`, or none, if it has none, so a kept name stays
  hidden. Its `from` and `plain` arguments move a name between the keys,
  or change its `from`, with **no link rewritten**, since links don't
  depend on positions: a position in `from` makes it a `known_as` name
  from there, and `plain: true` makes it a plain alias (remove-then-add
  would rewrite every link to the filed name). An empty or blank `from`
  counts as left out, so a client that fills optional strings with `""`
  can't make a secret public by accident. `update_fields` adds
  `known_as` entries (it doesn't move them, since their order decides nothing), each a `value` and a `from` that must resolve, and
  sets an entry's `from` as an ordinary edit; changing or removing a name
  there is `unsupported_edit`, pointing to `change_alias`. `rename_entity`
  to one of the note's own `known_as` names takes the entry out, and says
  the name is known everywhere now. `create_entity` takes `known_as`.
  `copy_entity` copies no `known_as`, as it copies no aliases. A link
  written by a later name is written as asked, and `check` warns
  (F-LINK-13).
- **Moves** (maintainer decision, 2026-10-24, #89). `move_entity`
  moves an entity's file into a series, a book, or the universe itself,
  within its universe, keeping its path below `entities/`. It takes the
  file's hash, refuses a move out of or into a final book, and
  refuses a file already at the new path (in any case). Canon views stay
  as they were (format.md F-RT-5): an entity without `from` gets the
  entry point it had written as `from`, unless the new scope starts at
  that very point. If the reader meets the entity outside the new scope,
  the move is refused (`entry_outside_scope`), since MCP can't ask; the
  error names both ways forward, and `keep_entry` (keep it as `from`,
  with the F-ENT-6 warning) or `enter_at_new_scope` (take `from` out, so
  it enters at the new scope's start, which `pinned_canon` flags if it
  holds pinned facts) makes the move. For an entity met before the new
  scope, that leaves it out of the views in between; for one met only
  after it (`from: ash:2` moved into Ember), it enters earlier than the
  reader met it before, and the error and `effects` say which. Neither
  changes anything when the entry point is inside the new scope. Every
  link in the den is resolved
  again as it would be after the move. A link to the entity that would
  stop reaching it (F-LINK-7 tie-breaks change with scope) is rewritten
  to its id, showing the same text (`[[id|Tessaly]]`; `\|` in a table
  row, with or without its outer pipes, in a blockquote too), and the
  entity gets an id if it has none; if such a link is in a
  final book, nothing moves. The moved file's own links to other
  entities that would reach another one, or become ambiguous, from the
  new scope are rewritten the same way, each to the id of the entity it
  reaches now, keeping its text, `#section`, and label
  (`[[id|Lantern]]`), and each is listed in `effects` (maintainer
  decision on #164); with `allow_relinks` they're left as written, to
  follow the new scope, and listed as links that resolve differently.
  Broken and ambiguous links in it stay as written, and so does one
  whose target has no id, which then counts as below. Any other link
  that would change which
  entity it reaches refuses the
  move unless `allow_relinks`, as for renames, and links that resolve
  differently, such as an ambiguous one that now resolves, are listed
  in `effects` rather than prevented (format.md F-RT-5). The steps keep
  one copy of the entity, with the same views, at every point: the file
  is rewritten in place (its id, a kept `from`, its own links), then the
  links to it in other files, then it's moved whole with one atomic
  rename (which also refuses a name taken in any case), and last, for
  `enter_at_new_scope`, its `from` is taken out at the new path. A
  failure puts back every step made, newest first. The session counts
  the old path as its own deletion, so a checkpoint lists it under
  `written_by_session` even when git sees a delete and an add rather
  than a rename; a checkpoint with `paths` needs both the old and the
  new path to record the move.
- **Copies** (maintainer decisions, 2026-10-23, #129). `copy_entity`
  (and `wolfden copy`) starts a new entity from an existing one, where
  `create_entity` starts from the type's template. The copy gets a new id
  that no file in the den uses, checked as `create_entity`'s is, the new
  `name`, a slug made from it (`-2`, `-3` when it's taken, or one asked
  for, `already_exists` if taken), and no `aliases`, since an alias
  usually belongs to the original (comment lines inside a block
  `aliases` list go with it, and `effects` count them). A title heading that reads the
  original's name takes the new one. Everything else is the original's,
  byte for byte: the type, fields, sections, status, and `from` and
  `until` values, and the links in it, which still reach what they
  reached. It goes beside the original, or into another universe,
  series, or book, at the original's folder path under `entities/`
  (its type's folder, and any folders below it). Nothing is copied into a final book (`frozen`); copying out
  of one is fine. The copy is written as a new file only, and checked as
  the den reads it. Before it's written, every link is resolved again
  with the copy there: one that reached an entity and would reach the
  copy instead (its name or file name, in a nearer scope, or a name
  beating an alias) or become ambiguous (a tie with the copy, F-LINK-7)
  refuses the copy unless `allow_relinks`, as for a rename; the error
  lists each one. A link that was broken or ambiguous and now reaches
  the copy reached nothing before, so it's only listed in `effects`. Copied into another universe, links in
  the copy can break (F-LINK-5, F-LINK-8): that's allowed, and `effects`
  lists each link in the copy that doesn't reach what it reaches in the
  original, and the positions in it that don't resolve there (F-POS-4).
  A copy in another scope without a `from` enters canon at that scope's
  start (F-ENT-6), and `effects` say so; a `from` outside the book it's
  filed in is said instead. `effects` also count the links in the copy that
  still reach the original, such as its own `[[Name#Section]]` links.

## 9. Architecture

```text
src/
  Wolfden.Core/     # model, markdown and frontmatter parsing, positions, canon-as-of logic
  Wolfden.Index/    # EF Core + SQLite index, file watcher, FTS
  Wolfden.Git/      # git history provider: log, diffs, checkpoints, tags (git CLI, #19)
  Wolfden.Api/      # ASP.NET Core minimal API (.NET 10)
  Wolfden.Mcp/      # MCP server
  Wolfden.Cli/      # `wolfden init | check | index | query | copy | new-id | serve | mcp`
web/                # React + TypeScript + Vite + TanStack Query
samples/            # sample dens (CC-BY-4.0)
docs/
```

- **Storage and history are separate.** The core reads and writes a den
  through the den store (`IDenSource`, `IDenStore`) and asks a history
  provider for log, diffs, checkpoints, and tags (§6). `Wolfden.Git` is the v0 history
  provider, so a different store or provider can replace either later.
- **v0 runs locally as one process.** `wolfden serve` hosts the API and the
  built web UI on localhost and opens a browser. `wolfden mcp` runs the MCP
  server over stdio for AI clients.
- **The web scaffold** (#23; v0 behaviour, not a recorded decision).
  `Wolfden.Api` is a library (an ASP.NET Core minimal API), not a program
  of its own, so `wolfden serve` hosts it in the same process. v0 is
  single-user with no accounts or authentication, so the server guards
  the den from everything but its own pages on this computer. It listens
  on 127.0.0.1 only, never on another network interface, and no
  configuration can add a listener: Kestrel endpoints in environment
  variables or an `appsettings.json` are ignored, and `ASPNETCORE_URLS`
  is overridden, with a warning; `serve` also checks the addresses after starting and stops
  if one isn't loopback. It answers only requests addressed to
  `localhost` or `127.0.0.1`, so a web page on another site can't reach
  the den through a DNS name that points at this machine (DNS rebinding).
  The API refuses, with 403 `cross_site`, a request whose
  `Sec-Fetch-Site` is `cross-site` or whose `Origin` isn't the server
  itself (`localhost` or `127.0.0.1` on its port), and sends no CORS
  headers, so another site's script can't read an answer or send a
  write. Requests without those headers, such as `curl`'s, are answered.
  Every response the app sends, its errors included (a refused host, a
  `cross_site` 403, an `internal_error`), has `X-Content-Type-Options:
  nosniff`, and pages are sent with `Cache-Control: no-cache`, so a new
  build shows on the next load. Every response the app sends also has a
  Content-Security-Policy (#180), as defence in depth. A page's is `default-src 'self'; script-src 'self';
  style-src 'self'; img-src 'self' data:; connect-src 'self'; font-src
  'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none';
  form-action 'self'`: the built UI has no inline script or style, and
  `data:` is there only for its empty icon. In a den with
  `remote_images: true` (format.md F-DEN-8), `img-src` adds `https:
  http:`; `den.yaml` is read for each page, so a change applies on the
  next load. Everything else, the API's JSON, the UI's files, and error
  pages, gets `default-src 'none'`, and the page that says how to build the UI allows
  only its own inline style. The den's own images, under `/den-files/`,
  get a policy with `sandbox` ("Images in the den" in §9, #186). The policy comes from `serve`, so `npm run
  dev`, whose page has an inline script for hot reload, isn't affected.
  Hosting for several users (§10) brings its own authentication.
  The API is JSON under `/api`, in camelCase, as is usual for a web
  client (the MCP tools keep snake_case, §8); errors are `{code,
  message}`, with the MCP tools' codes where they mean the same thing
  (`den_not_loaded`, `den_unreadable`), and `not_found`,
  `method_not_allowed`, `cross_site`, and `internal_error` otherwise.
  Results carry a `version` for their shape, as `--json` does. Each request brings the
  index up to date first, as an MCP tool call does. It has
  `/api/health`, `/api/den` (the den's name, its universes, and
  `remoteImages`, F-DEN-8), and the
  read endpoints below for M3's pages (#24 to #27). The UI in `web/` is built with npm (`npm run
  build`, Node.js ^22.22.2, ^24.15 or 26+) into `web/dist`, which `dotnet build`
  copies next to `wolfden` as `wwwroot` when it's there; `dotnet build`
  never runs npm, so .NET work doesn't need Node. Paths that aren't files
  get `index.html`, so the UI's own routes survive a reload. For UI work,
  `npm run dev` serves the UI with hot reload and passes `/api` to a
  running `wolfden serve --no-open` (port 9653, or `WOLFDEN_PORT`). The
  proxy drops the `Origin` of the dev server's own page, since its port
  is Vite's, and passes any other `Origin` on, so the API still refuses
  a request from another site that comes through the dev server.
- **The read API** (#24 to #27; v0 behaviour, not a recorded decision).
  Every endpoint is `GET`, with query parameters in camelCase. The
  answers come from the same read queries as the MCP read tools
  (`DenReader` in `Wolfden.Index`), so the same question gets the same
  answer, in camelCase instead of snake_case; where a tool answers the
  question, the endpoint returns its result, and a page adds to it.
  `universe` works as the tools' does (§8, "One universe per call"),
  and `serve --universe` pins every request as `mcp --universe` pins a
  session. `asOf` is the tools' `as_of`: a position, with the same
  syntax, checks, and canon-as-of meaning; without it, the author's
  full view. `marksAt` is a position for the writer, which no MCP tool
  has: "Marks for the writer" below. `readers=true` on `/api/entity`,
  `/api/search`, `/api/fields`, `/api/entities`, `/api/hierarchy`, `/api/positions`, and
  `/api/book/chapter-fields`, beside `asOf`, is the UI's Reader's view: "The
  Reader's view" below. An endpoint refuses a parameter it doesn't take (so
  `as_of` isn't quietly ignored) or one given twice. An empty value
  counts as left out, except an empty `asOf` or `marksAt`, which is `bad_argument`:
  left out it would give the full view, spoilers and all, so a page that
  meant to send a position fails closed. A revision that can't be a
  checkpoint (it starts with `-` or holds a space) is `bad_argument`,
  and a message repeats at most 80 characters of one. Errors use the tools' codes, with 400 for
  `bad_argument`, `bad_position`, `universe_required`, and
  `unknown_type`; 404 for `unknown_universe`, `unknown_scope`,
  `unknown_entity`, `unknown_checkpoint`, `unknown_file`, and `no_universe`; 409 for
  `ambiguous_entity` (with its `candidates`); 503 for `den_not_loaded`
  and `den_unreadable`; and 500 for `history_failed`.

  | Endpoint | Parameters | Answers |
  |---|---|---|
  | `/api/hierarchy` | `universe`, `asOf` | The universe, its series in story order with their books, its standalone books, and each book's chapters (key, number, title, position). Each scope counts the entities filed in it, in all and by type; as of a position, only those in the canon view there. |
  | `/api/positions` | `universe` | Every story position in order, with its book's name and series and the chapter's number and title, for a position picker. A book with no chapters yet is listed by its bare key. |
  | `/api/entities` | `universe`, `type`, `status`, `series`, `book`, `asOf`, `limit`, `marksAt` | `list_entities`; with `asOf`, only the entities in the canon view there, in its order; with `marksAt`, all of them, those in that view first ("Marks for the writer" below). |
  | `/api/entities/lookup` | `query`, `universe`, `type`, `asOf`, `limit` | The notes whose name, alias, file name, or id has a word starting with each word typed, best first, each with what matched and which alias, for the link pickers (#198; "Looking notes up by name and alias" below). `type` takes several types separated by commas; with `asOf`, only notes the reader has met by then. |
  | `/api/entity` | `entity`, `universe`, `asOf`, `marksAt`, `readers` | `get_entity`, as a page: each field value or entry and each section lists its links, resolved from the index (the entity reached, or broken or ambiguous); also the scope's container chain, where the entity enters canon (its `from`, or its scope's start, inherited, format.md F-ENT-6), and its backlinks (which entity, field, section, chapter, or outline row). As of a position, a link to an entity the reader hasn't met doesn't say what it reaches, and only links in what the reader has met count as backlinks. |
  | `/api/canon` | `universe`, `asOf`, `series`, `book`, `type`, `brief` | `canon_as_of`. |
  | `/api/search` | `query`, `universe`, `type`, `canonOnly`, `limit`, `status`, `series`, `book`, `asOf`, `field`, `marksAt`, `readers` | `search`, with snippets; it can also keep entities of one status, those a series or book filter covers (as in a canon view), or, as of a position, hits in the entities, sections, and field values in the canon view there. Each hit adds `text`, the snippet without marks, and `matches`, the `start` and `length` of each match in it (UTF-16 code units), so the page never mistakes a note's own brackets for a match (#179). |
  | `/api/den/files` | | A fingerprint of the den's files (their paths, sizes, and times, no contents), for an open page to see that something changed (#285). It's all this answers, and it doesn't bring the index up to date. |
  | `/api/fields` | `universe`, `asOf`, `readers` | The names of the fields notes give values for, for the search page's Field filter (#179); as of a position, only those of values in the canon view there, and with `readers=true`, of values a reader there can see. |
  | `/api/history` | `entity`, `universe`, `limit`, `before` | `get_history`'s checkpoints, a page at a time. |
  | `/api/history/diff` | `entity`, `universe`, and `checkpoint`, or `from` and `to` | The entity file's unified diff: what one checkpoint changed (against the one before it, or for the first, against nothing, through the history provider's `DiffCheckpoint`), the change between two (`from` and `to`), or since one (`from`, or by default the latest) up to the file as it is now. |

  History endpoints, like `get_history`, treat a den without history as
  an answer, not an error: `available` is false, `backend` is `none`,
  and `reason` says why. `/api/history/diff` takes `to` only with
  `from`. A checkpoint's own diff, and a compare, show a checkpoint
  that renamed the entity's file as a rename (§6). A note with errors
  has a history by its path, as with `get_history`, but no
  `/api/entity` page: its `unknown_entity` carries `leftOut` (below).

  **A den's errors** (#280). `GET /api/den/errors` takes no parameters, and
  so refuses `asOf`: `count`, `denLoaded`, `truncated` (it lists at most 500),
  and `errors`, each with `path`, `universe`, `name` (a note the index has),
  `kind`, `line`, `rule`, `message`, `reach` (`section`, `file`, `container`, or
  `den`), `section`, `sectionLine`, and `canEdit`. It's errors only, the same
  ones `wolfden check` has, since the index keeps outlines' errors too (schema
  9). A pinned server lists its universe's and the den-level ones. Unlike the
  other endpoints, it brings the index up to date only if a stat walk of the
  files (`AutoCheckpointer.FilesFingerprint`) changed, or the answer is a minute
  old, since letting go of a file held open changes neither its size nor its time.
  So polling it doesn't hash the den every time. An empty list doesn't prove
  the den is clean: a note held open is absent from the index (#112). The top bar's indicator asks every 5 seconds while the page
  shows. It shows everything at any position, like editing; it's the writer's.

  `GET /api/file/source` (`path`) and `POST /api/file/edit` (`path`, `text`,
  `expectedHash`) open and save a file by its path, including one the den leaves
  out: the `/file?path=` page, "Edit file" in the list. A path must be a note, an
  outline, a container's file, or `den.yaml`, exactly as written on disk, with no
  link on the way, and for a pinned server in its universe and not `den.yaml`.
  Anything else is 404 `unknown_file`, whether it exists or not. What holds it
  must load (else `invalid_edit`, naming what to fix). A save is refused on a
  stale hash (409 `stale_file` with `currentFile`), or in a final book (409
  `frozen`). A note the den reads goes through `/api/edit/file`'s code. Other
  files that load are checked as their other writes are. A file left out whole
  may keep errors, if it has no more than before, or if its frontmatter didn't
  parse (format.md §1.3). A save that brings it back is checked as an ordinary
  write, positions included. Status and id are read from the parsed frontmatter,
  so any spelling counts. An id can't change or copy another's, including one in a
  file left out. `status: final` can't be written (Freeze does that). A `book.md`
  whose unparsed frontmatter holds `final` in any case, or a backslash, isn't saved
  here, since it might be final; any other that doesn't parse can be, and a save must
  leave a `book.md` parsing. A loaded series or universe above a final book that
  loaded keeps its frontmatter. While a `book.md` below says final or can't be read,
  no series or universe changes `chapter_defaults`, `chapter_heading`, `books`, or
  `order` (the lists compared with repeats removed) or saves frontmatter that doesn't
  parse, and one that doesn't parse now, or has such a list that isn't one, is sent to
  the writer's own editor. Empty text is refused, invisible characters included. `den.yaml` is saved only while it doesn't load.

  **Marks for the writer** (#300, maintainer decisions). In the web UI the
  writer is the author: show everything, and tag what readers don't know yet.
  Filtering exists for AI agents, which could spoil prose. `marksAt` gives the
  API's half of that. With it, `/api/entity` is the full view and `/api/search`
  finds what it finds with no position, in the universe the position settles,
  and the answer says what readers there don't know. Without it, nothing is
  different: `asOf` is still the canon view, and giving both is `bad_argument`.
  A position that can't be read is
  `bad_position`, with the message `asOf` gets, and `universe=*` doesn't go with
  one. `Wolfden.Api`'s `ReaderMarks` makes the marks, and no MCP tool can ask for
  them or reach that project (`McpStaysStrictTests`). What's added, only with
  `marksAt`, and left out where there's nothing to say:

  ```json
  {
    "view": "full", "marksAt": "ember:3",
    "entity": {
      "name": "The salt tithe",
      "fields": [
        { "name": "planted", "value": ["ember:1", "ash:2", "dusk:9"], "aheadValues": ["ash:2"], "unplacedValues": ["dusk:9"] },
        { "name": "keeper", "value": "Orrin Vale", "until": "ash:2" },
        { "name": "keeper", "value": "Morvane", "from": "ash:2", "readerMark": { "kind": "ahead", "at": "ash:2" } }
      ],
      "sections": [
        { "name": "Terms", "until": "ember:2", "readerMark": { "kind": "ended", "at": "ember:2" } },
        { "name": "Payoff", "from": null, "readerMark": { "kind": "ahead", "at": "ash:3" } },
        { "name": "The last toll", "from": "dusk:9", "readerMark": { "kind": "unplaced", "at": "dusk:9" } },
        { "name": "Old draft", "from": "ash:3", "until": "ash:1", "readerMark": { "kind": "never", "at": "ash:3" } }
      ]
    }
  }
  ```

  - **`readerMark`**, on the page's `entity`, a field's value or entry, a section,
    or a search hit. `kind` is `ahead`, for a note or fact readers reach after
    the position (the page says `enters at …` for a note, `ahead of readers` for
    a fact), or `ended`, for a fact whose end is at or before it (`no longer
    true here`, not `ahead of readers`). Two more say no position reaches it, at
    any position: `unplaced`, for a note or fact with a position that doesn't
    resolve (F-POS-4), such as a chapter of a book not outlined yet, and `never`,
    for a fact whose positions resolve but whose range holds nothing (F-TMP-3,
    F-SEC-8), or a subsection that starts after its parent ends (F-SEC-9). They're
    apart because the writer does different things about them: list the chapter,
    or fix the dates. `at` is the position that makes it so, and the words are
    the page's. `ahead` and `ended` compare by the index's effective ranges, as
    canon views do, so `at` may not be written on the fact: a thread's
    `## Payoff` starts at `paid_off`, and an entry or section that hands over
    (F-TMP-4, F-SEC-8) ends where the next starts. `at` is as the note writes
    that point if it does, else the chapter there. For `unplaced` it's the written
    position that doesn't resolve, the `paid_off` of a Payoff, or a parent's.
    For `never`, it's where the range starts.
  - **`aheadValues`** and **`unplacedValues`**, on a field or a hit in a field:
    which of a `position` field's values are later than the position, and which
    can't be compared (F-VIEW-5), as written, since only some of a list may be.
    An assistant at the position gets neither. The field keeps its whole `value`.
  - **Names readers learn later** (`known_as`, #67). The page's `knownAs` lists each
    with its `from`, in the full view too, and with `marksAt`, a `readerMark` until
    readers know it: `ahead` at the later of its `from` and the note's entry, or
    `unplaced` if no `from` places it. An unlabelled link written by one gets a
    `nameMark` the same way, on the page's links and in **Linked from**, since the
    Reader's view shows the note's name for it there. A search hit found only by one
    has `hiddenIn: "laterName"` and a `nameMark`, and comes after what readers can find.
  - **`marksAt`** on the answer repeats the position.
  - **A later `until` on a fact in effect has no mark.** The read gives the
    `until`, and the page's `isAheadOf` tags it from the position, as it already
    does (#288), so that's still one rule.
  - **No reader marks** on what's kept out for a reason other than the position:
    a note, section, or entry out of canon, a bookkeeping section, or a section
    an error leaves out. Each has its own badge.
  - **A note readers meet later** has `readerMark` at where it enters, the same
    position as its `entry`. Its facts that start with it, or earlier, say
    nothing more; a fact of its own that starts later still has a mark, and so
    does one that's `unplaced` or `never`. In an `unplaced` note, only the note
    has a mark, since nothing in it can reach a reader.
  - **What has no mark and isn't badged is what a reader sees,** at every
    position, with one exception: an entry that loses an overlap (F-TMP-5, which
    `wolfden check` warns about) isn't ahead or ended, so it has no mark, though
    the canon view shows only the other.
  - **A search hit** has the mark of the text it found: its section's or entry's,
    or else the note's, and `noteMark`, the note's own. A field hit whose snippet
    shows the name or alias of a linked note readers don't know has `laterLink`,
    with that note's status and mark. A reader's search can't match by that name,
    though it may find the hit by what the link shows until then. `hiddenIn` says the
    words are only in `alias`es or a link's `linkTarget`, as written, which a reader's
    page doesn't show. `met` says whether a search in the Reader's view there finds the hit: it has
    no mark or later value, it's in canon, and its words are in what that page shows.
    Those come first, each part in rank order. It reads up to 1,000 text hits and 1,000
    field values, each from notes readers have met first, and then cuts to the limit, so
    a word in thousands of later notes doesn't lose a met one. What it can't promise,
    with `truncated` then true: over 1,000 matches in met notes that readers can't find,
    such as in their later sections, can push out one they can. Links and
    the page's `pinned` carry no marks. (The page shows pinned facts in place, where
    each has its own mark, and doesn't list `pinned`.)
  - **A backlink** has the mark of what holds it, as a search hit does: its
    section's or entry's, or else the note's. One in a chapter's fields or an
    outline compares where it's written. One with no position, such as in a
    container's own text (F-LINK-13), has no mark: there's nothing to fix or wait
    for, so it's a second exception to "no mark means a reader sees it". One in a
    section an error leaves out has `leftOut`, for the page's badge, and no mark.

  **Lists.** `marksAt` on `/api/entities` gives every note the filters keep, as
  `MarkedEntity`: `list_entities`' line with `met`, true if it's in the canon view at
  the position, and its own `readerMark`, `ahead` or `unplaced`. An idea isn't met and
  has no mark. `met` comes from the view, so one answer can't mix two readings of the
  den, and the page needs no second list. What's met comes first, then the rest, each
  in the usual order, and only then the `limit`, so a long list cuts what readers
  haven't met first. It reads no note's facts. `list_entities` and `EntitySummary` don't
  change, as `MarkedEntity` is only in `Wolfden.Api`.

  **The Reader's view** (#300). `readers=true` beside `asOf` on `/api/entity` gives
  the page as an AI assistant's read at that position has it, since `asOf` alone
  keeps later `until`s, position values, and comments for the writer (F-VIEW-4,
  F-VIEW-5, F-VIEW-6). `Wolfden.Api` and `Wolfden.Mcp` apply the one filter,
  `AsOfReads` in `Wolfden.Index`, so a page's fields, sections, and `pinned` at every
  position equal `get_entity`'s (`ReadersViewTests`), and neither project references
  the other. A link or backlink in text the filter leaves out as a comment is gone
  too, and a heading's comment doesn't come through a backlink's section name or a
  link's `section`, which the index keeps as written. It goes only with `asOf`: without it, or with `marksAt`, it's `bad_argument`,
  and `readers=false` is the same as leaving it out. `/api/canon` doesn't take it,
  since no page needs it yet.

  **Beyond `get_entity`, for the page's own words.** A heading is read as it shows: each
  link in it, in `name`, `headingPath`, and a backlink's section, is what it shows, as
  plain words, since `get_entity` gives a heading as written and the page prints it. So
  that one member differs from MCP where a heading has a link. A link's `heading` and
  `section` are null unless readers know that section of a note they've met, so
  `[[a-note#Later heading]]` shows `A note` and goes to no later anchor.

  **A position that can't be read** is refused with `bad_position` on every one of
  these, as `'<position>' isn't a position in this story.`, since an `asOf` read's own
  message lists the universe's books, later ones included, and a stale bookmark is enough
  to get there. That's why `/api/entities` takes `readers=true`: its list is the same as
  with `asOf` alone. Without `readers`, the message is as it was, for the writer and MCP.

  **Book and chapter pages and the position bar ask only for what's reached.** The
  filter is on the server, not the page, so no answer holds what a reader doesn't know,
  and a page can't let one slip.
  - `/api/hierarchy` with `readers=true` is the tree readers know: the books and chapters
    reached, and a later book or series only where a met note is filed, with no later
    chapters. A series with nothing yet isn't there. A chapter's `heading` follows its
    fields' rules below: no later position value, no link's heading. A labelled link
    in a chapter's `title` is only its label, here and in the next two.
  - `/api/positions` with `asOf` and `readers=true` is the positions reached. `asOf` means
    nothing there without `readers`, and is refused.
  - `/api/positions/next` (`after`, `step=chapter|book`) gives one position and nothing
    else. It's how that view moves forward, asked only when the writer presses, so
    nothing's told until they go there. It says `position: null` at the end.
  - `/api/book/chapter-fields` with `asOf` and `readers=true` is its own shape
    (`ReadersChapters`), with none of what an edit needs. It has the chapters reached, and
    their fields with a value, following a note's fields' rules: no later position value
    (the same `AsOfReads.Reached`), and every link what it shows. A link to a note not
    met is only those words, not a link, and one to a note met is written again by its
    key with the words it shows, so no alias, target, or later name is in the answer. A
    book that isn't in the tree above is refused in the same words, whether it's later or
    doesn't exist, and names no others.
  - `/api/den/errors` with `countOnly=true` is how many problems there are, and no list.

  The test is word by word: `ReadersBookPagesTests` puts a made-up word in each place
  and checks each answer at every position, and `ReaderBookPages.test.tsx` holds what
  those pages may ask for, so anything new has to be checked there too. What's filtered
  on the page's side alone is what it asks for, by view (`usePositionsInView`).

  **A note's text is read from its source.** The Reader's view replaces each link in the
  markdown before it's parsed (`readersSource`, the page's copy of `WithLinksShown`), so
  formatting in a label can't split a link from its target: `[[a-key|the *Ardent*]]` is a
  link to what it reaches, reading "the *Ardent*" in italics, or those words alone if readers haven't
  met it. A link's in code only if its `[[` is. A `\[[` and a `![[` aren't links, and stay
  as written, on the page and in search. The other views are as they were.

  **What still asks for more.** The banner on a note readers haven't met asks for the
  whole list and every position, for that note's own name and where it enters
  (SC-WEB-13), on opening that note, or on switching to the Reader's view on it. And
  `/api/entity` keeps text as `get_entity` has it, since `ReadersViewTests` holds the two
  equal and the page has to match links to their text. So `body`, `links`, and
  `backlinks[].text` can hold a target behind a label, an alias a link is written by, or a
  later section's name. None of it is on the page, but it's in that answer. A link written
  by a name readers learn later (`known_as`) is the one case this no longer holds for:
  it reads by the note's name there too, `links[].text` and `target` and
  `backlinks[].text` included (format.md F-VIEW-7, #67).

  `readers=true` on `/api/search` (`DenReader.SearchAsReaders`) looks for the words in
  that same text, each note read through `AsOfReads`, rather than in the search table,
  which holds every note's whole text. So a word finds a hit exactly when the Reader's
  page at that position shows it (`ReadersSearchTests`, word by word), and there's no
  second rule: a comment in a lead, section, or heading, however badly formed, a later
  or unplaced position value, and a later `until` neither match nor show. Text and field
  values are matched as the search table matches them, and the table only gives the
  order, with comments not counted. There's no cap on what's read, so a word in many
  later notes can't push out a met one.

  **Every link in text, headings, and values is what it shows on the page,** for matching
  and for the snippet (`ReadersLinks`, from the index's link rows): its label, else the
  note it reaches if readers have met it, else its target as written, with `: heading`
  if it has no label and readers know that section. So a link written by an alias or key finds by the name it shows, not by what
  was written. A link written by a `known_as` name readers know there is the exception: it
  shows as written (`byKnownName` on `/api/entity`'s links, #67 §4), so text after a reveal
  keeps the real name. A label hides its target, even a met note's; and a later note's name
  doesn't match until readers meet it. `get_entity` gives links as written, so this is in
  the search, and MCP's reads don't change. The rule is written twice, as `linkText` in
  `Links.tsx` and `ReadersLinks.Shown`, since the server can't call the web. Both are
  tested against `tests/shared/link-text-cases.json`, and `ReadersSearchTests` compares
  search with the Reader's page data word by word.

  **Where search and the page still differ.**
  - Nothing for names any more: a note's name and the other names readers know at the
    position match, and the page lists them in `aliases` (D3 on #67, maintainer decision
    2026-11-05, which reverses the earlier call that aliases don't match). A `known_as`
    name readers learn later neither matches nor shows. A secret written as a plain
    alias shows at every position, so it belongs in `known_as`. The search table keeps
    `known_as` names in a column of their own, which the ranking leaves out, so a name
    readers haven't reached can't order hits either.
  - A type's defaults don't match, though the page shows them, since they aren't the
    note's writing, as with no position.
  - What isn't a note's text isn't searched: field names, the note's type and scope, and
    **Linked from**.
  - **A note's markup (#332).** The search looks at a note's text with only its comments
    and links read, so it finds words in a link's URL (`[text](https://word.example/x)`),
    an HTML attribute (`<span title="word">`), and an image's path (`![alt](word.png)`),
    though the page doesn't show the first two, and shows the path only if the image
    isn't found. The writer has to have typed the word, so it can wait for dogfooding.
  - A comment inside a link shows on the page, and search finds only the label.

  The rest now read the same. A table isn't rendered, in any view, so its pipes show, but
  a link in a cell (`| [[note\|label]] |`) is a link like any other. That's on every
  page, as `\|` is only `|` escaped. In the Reader's view, a heading, a mapping's key, an
  image's alt text, and the words of a `[text](url)` show a link as what it shows, and
  so does a labelled link split over two lines, which isn't a link but reads as one.

  **Where the Reader's search finds what the Author's doesn't.** The Author's view looks
  at the text as written, so a section with `[[Secretalias]]` isn't found there by the
  name the page shows, as with no position. A word split by a comment
  (`gluea<!-- x -->glueb`) reads as one word only to a reader.

  A comment in a field value isn't a comment to a read (F-VIEW-6 is about leads
  and sections), so it shows and matches there as on the page. `/api/fields` with `readers=true` lists only
  fields with a value a reader can see, so a thread's `paid_off` isn't named before it
  pays off, and a `field` with none is refused as a made-up one is. MCP's `search`
  with `as_of` calls this same search (#67), without the page's `text` and `matches`, and
  with no marks; with no `as_of` it's the full search, as it was. `asOf` alone on `/api/search` is as it
  was, for the writer: the web no longer asks it. It doesn't match `known_as` names at all,
  since it can't tell which ones readers know (#67).

  An `/api/entity` for a note left out is still 404 `unknown_entity`, with
  `leftOut` (`path`, `line`, `rule`, `message`), found by its path, a path under a
  container left out, or the id, name, or file name written in the file. As of a
  position, only a path or an id finds it, `leftOut`'s members are null, and the
  message names neither. With `marksAt`, it answers as a full read does. The MCP tools share the message. Each MCP result but
  `check`'s adds `den_notice` while there are errors, a count that names nothing.

  **As of a position, nothing names what the reader hasn't met.** A
  link's `problem` is made again from what the reader has met: an
  ambiguous link names only the matches met by then, adding "and others
  not met yet" without naming them, and a broken one says only that it
  doesn't resolve, never the file or entity the check's message names.
  A link to an entity met later has no `entity`, and `inView` is false.
  So `outcome: "ambiguous"` and `inView: false` reveal that some entity
  the reader hasn't met exists, never which one; that's intended, since
  the page must still show the link as the writer wrote it. In a server
  pinned to one universe, a broken link's `problem` doesn't name an
  entity in another universe (the F-LINK-8 hint).

  **Decided (maintainer, 2026-10-26): chapter titles stay.**
  `/api/hierarchy` and `/api/positions` list every chapter, with its
  title, whatever the `asOf`: the author is the one viewing, and the
  as-of view hides notes, not the table of contents.
- **The browsing UI** (#24, #26). Four choices are maintainer
  decisions (2026-10-26), the first three worded as on the issues:
  - **Layout: a sidebar tree.** A tree on the left (universe → series →
    books → chapters and entity types → entities), always visible, with
    the page on the right. It collapses on narrow screens.
  - **Story position: an always-on bar.** A bar across the top of every
    page, with a slider along the whole story and book and chapter
    dropdowns to jump. It starts at "Everything" (the end of the story).
    The position carries from page to page and is kept in the URL, so a
    bookmark or a shared link keeps it. In the Reader's view it has nothing ahead
    in it (below).
  - **Notes the reader hasn't met** (#300, which replaces #26's "N more appear
    later"). At a position, the Author's view of a list has every note: what the
    reader has met first, then the rest, greyed, below a dashed line, and tagged
    with where they enter. Nothing collapses. The Reader's view has only what the
    reader has met.
  - **The Author's view and the Reader's view** (maintainer decisions on #300,
    which replace #26's "Show it anyway" on a note's pages). At a position, a
    note's page and History tab show everything by default, since the writer is
    the author, and tag what readers there don't know. A two-way control,
    `Author's view` and `Reader's view`, appears once a position is picked, with
    the current one always visible, so the page always says which it is. It's on
    every page, as the sidebar is, but for Edit file by path, which is always the
    writer's. It's kept in the URL (`?readers=1`) and isn't remembered, so a fresh
    start opens the Author's view, and it ends with the position.
    - **The Author's view of a book or chapter** has all of it, headings and fields in
      full, and can be edited. A chapter readers haven't reached is greyed and tagged
      `ahead of readers`, in the book's list, in its own page's heading, and in prev
      and next, in a list's plain words with one line to explain them. So is a link in a
      reached chapter's field to a note they haven't met, with `enters at`, and a
      position field's later value. In a later chapter, the chapter's tag says it. The
      book's notes are counted as `2 of 5`. The position bar's menus aren't tagged, as
      an option can't hold one.
    - **The Author's view** asks `marksAt`. A note the reader hasn't met shows,
      tagged `enters at chapter 11`, and it edits the real note, in the same read
      it already has. Tags are `ahead of readers`, `no longer true here`, `not
      placed in the story yet`, and `never true (its dates don't overlap)`, in one
      place (`readerWords.ts`), with the sentence on hover. A note-page tag is a
      button, as a keyboard or touch can't hover. A list uses plain text and one
      line to explain it (#302), as "Linked from" and the sidebar do. Nothing has two tags, and
      what keeps its own badge (out of canon, bookkeeping, left out) has none. A
      type's field defaults aren't listed, as with no position, since they aren't
      in the note.
    - **The Reader's view** asks `asOf` with `readers=true`, so it holds what an
      AI assistant reads there, a type's defaults included, badged. The note's page
      is read-only. A list asks `/api/entities` with `asOf`, what `canon_as_of`
      holds, and doesn't ask for the rest, so it can't name or count them. (The banner
      for a note the reader hasn't met does ask for the whole list, for that note's own
      name and where it enters, and shows nothing else of it.) The sidebar
      and the universe's and series' pages leave out books and chapters readers haven't
      got to, and a series with nothing yet, keeping a later book or series only where a
      met note is filed. They have no New buttons or chapter defaults, and nor does the
      top bar. Search finds only what a reader can (see the search page below).
      - **A book's and a chapter's pages** have the chapters reached and their fields to
        read, with no Edit, Freeze, Add chapter, New, or chapter defaults. Prev and next
        stay within what's reached. A later book or chapter says only "Readers haven't
        got to this chapter by this point in the story", and the tab has no title, so
        neither names it. It can't tell a later chapter from one that doesn't exist.
      - **The position bar** has no slider, since its length and its thumb's place would
        say how far through the story this is. The menus list only what's reached. `›`
        and **Next chapter** and **Next book** in the menus ask for the next position
        only when pressed, and go there. An option can't name what's further without
        giving it away, so **Pick a later point in the Author's view…** goes there,
        keeping the position, and the menus' tooltip says why. At the end, it says
        "No later chapter." only when asked, and stays.
      - **The rest of the top bar.** The checkpoint indicator isn't there, and doesn't
        ask, since it names changed files. The ⚠ list names files and quotes them, so
        here it's only a count, `⚠ 3 problems`, which goes to the Author's view when
        pressed. It stays because what an error leaves out is missing from this view too.

      The writer's tools are a click away: Edit file by path drops `readers`, so
      it's plainly the writer's, and the API's writes know nothing of views. A note the
      reader hasn't met shows the banner, for example "Readers first
      meet Quincey Morris in chapter 11", with the Author's view as a way to see
      it. Saved versions can hold anything, so its History tab fetches none of
      them and says so.
  - **`ahead of readers`** (maintainer decision on #288). In the Author's
    view, a fact's `until` later than the position has a small muted
    text tag, `ahead of readers`, with the tooltip "Readers at this
    point in the story don't know this yet." It's a button that also shows
    that sentence in place, since a tooltip only comes on hover. It's 24px
    tall, for touch, and has a dotted underline, so a touch user can tell it
    opens. An `until` the position has reached is `no longer true here`.
    The tag's component is `AheadOfReaders`.

  The rest is v0 behaviour, not a recorded decision.
  - **Routes and URLs.** The UI routes with TanStack Router:
    `/u/<universe>` (the overview), `/u/<universe>/s/<series>`,
    `/u/<universe>/b/<book>` (its chapters, with their titles),
    `/u/<universe>/b/<book>/c/<chapter>` (the chapter, and the notes that
    enter there), and `/u/<universe>/e/<entity>`, where the entity is
    named by its id, or by its path while it has none. `serve` answers
    any path under `/u` with `index.html`, so a path ending in `.md`
    reloads too.
  - **The position in the URL.** It's `?at=<position>`, such as
    `?at=dracula:9`. "Everything" leaves `at` out and sends no `asOf`. A
    blank `at=` is sent as it is, so the API refuses it and the page
    offers "Show everything" rather than quietly showing everything. A
    change of position replaces the history entry, so Back leaves the
    page rather than stepping back through chapters.
  - **The bar.** It has step buttons and a "Show everything" reset. The
    arrow keys step one chapter, and Home and End go to the first
    chapter and to everything. A chapter without a number, such as
    Dracula's closing note, is named by its title.
  - **The tree.** A den with several universes has a universe switcher.
    `/` goes straight to the universe of a server pinned with
    `serve --universe`, or of a den with one. On a narrow screen the
    tree folds behind a Contents button and opens over the page, below
    the top bar. Focus moves into it, and Escape closes it.
  - **Notes out of view.** In a list's Author's view, a note out of view for its
    status rather than its entry point comes after what the reader has met, with its
    status badge and no tag. One no position places says `not placed in the story yet`.
    Each group's count says how many the reader has met of them, `2 of 5`. A list longer
    than the API's limit says how many of how many it has, and that groups count only what's
    listed, and a chapter's list says more may enter there instead of saying none do. The banner for a
    note not met yet also offers to go to where it enters, or to show
    everything. In the Reader's view, the tab's title names only what's in view.
  - **Entity pages.** An entity page shows the name, type, status, scope
    breadcrumb, and entry point; the fields; the sections, as markdown;
    and "Linked from", grouped by source. `[[...]]` links become in-app
    links through the API's resolved `links`, matched by their text in
    order, with `\|` the same as `|`. A broken link is marked as broken, and an ambiguous link
    lists its matches in a tooltip. In the Reader's view, a link to a note
    the reader hasn't met is plain text. Markdown is rendered with
    react-markdown, which builds React elements and never HTML strings.
    Raw HTML in a note is dropped, and unsafe URLs such as `javascript:`
    are removed.
  - **Images from websites** (maintainer decision on #180, 2026-10-26).
    By default an image in a note whose address is on a website
    (`https:`, `http:`, or `//host/...`; in practice, any address that
    resolves to another origin than the page's) doesn't load, so opening
    a note doesn't tell that website the den was opened. The renderer
    shows a small placeholder instead, with the image's alt text and its
    address as a link that opens in a new tab (`rel="noopener
    noreferrer"`); inside a note's link it shows the address as text, so
    links don't nest. `remote_images: true` in `den.yaml` (format.md
    F-DEN-8) loads them, with `referrerpolicy="no-referrer"`. The UI reads
    the setting from `/api/den` and blocks until it knows it. The page's
    policy is fixed when it loads, so the UI keeps the setting's first
    answer: turned off while the page is open, images are blocked at
    once; turned on, the placeholder stays and says to reload the page,
    rather than showing images the policy would break. The
    blocking is done by the renderer, so it doesn't depend on the browser
    enforcing the Content-Security-Policy, which blocks them too. An image
    kept in the den isn't affected by the setting; it loads from `serve`
    (next item). Nothing else in a rendered note fetches anything: links
    are plain links, and raw HTML is dropped.
  - **Images in the den** (maintainer decision on #186, 2026-10-27). An
    image's path in a note is a path in the den (format.md F-LAY-9):
    relative to the folder of the file it's written in, as on GitHub, in
    VS Code's preview, and in Obsidian's "Relative path to note" mode;
    from the den root if it starts with `/`; and never above the den
    root. The renderer resolves it against the note's `path` from
    `/api/entity` (the entity's file), refuses a path whose `../` climbs
    above the root (such an image never loads), and rewrites it to
    `/den-files/<path>`, each name percent-encoded. An address with a
    scheme or a host is left as it is. `serve` answers `GET` and `HEAD`
    on `/den-files/{**path}`, a prefix that can't be a UI route (`/u/...`)
    or the API (`/api/...`), and serves the file only if: the path is a
    den path as the store's writes have it (relative, `/` between names,
    no `..`, `\`, `:`, wildcards, or names that Windows would change);
    no name starts with `.`, so nothing under `.wolfden/`, `.git/`, or
    `.obsidian/` (F-LAY-2); the extension is `.png`, `.jpg`, `.jpeg`,
    `.gif`, `.webp`, `.avif`, or `.svg`, in any case, which sets the
    Content-Type; each name is on disk as written (in any case on
    Windows), so a short name such as `WOLFDE~1` can't stand in for a
    hidden folder; no folder on the way is a folder link, which the den
    never follows; and a file that's a symbolic link leads to a file
    that passes the same checks, so it can't reach outside the den. A
    server pinned with `serve --universe` serves only that universe, as
    for the API: a path under another universe's folder
    (`universes/<other>/`; a universe's key is its folder name, F-KEY-3)
    is refused, and den-level paths outside `universes/`, such as
    `/images/`, are still served.
    Anything else, and a request a browser marks as cross-site, is a
    404, never a 403, so a request can't tell a refused file from a
    missing one. A file's response has `X-Content-Type-Options: nosniff`,
    `Cross-Origin-Resource-Policy: same-origin`, and the
    Content-Security-Policy `default-src 'none'; style-src
    'unsafe-inline'; frame-ancestors 'none'; sandbox`, so an SVG opened
    at its own address can't run its scripts as Wolfden; an image a page
    loads with `<img>` runs nothing anyway. Writers change images while
    the page is open, so it's sent with `Cache-Control: no-cache` and an
    `ETag` and `Last-Modified`: the browser asks each time and gets a 304
    while the file is the same. An image that doesn't load gets a
    placeholder like the one for a website's image, "Image not found:
    <alt text>", with the path as written, and isn't asked for again
    until its path changes. A lint warning for a missing image is a
    separate follow-up. Where to keep images, and how to set up
    Obsidian for them, is in [`using-wolfden.md`](using-wolfden.md#images).
- **The search page** (#25; v0 behaviour, not a recorded decision). A
  search box sits in the top bar; `/` focuses it from anywhere but a
  text box. Typing searches once it pauses (300 ms), on
  `/u/<universe>/search?q=<words>`, which also keeps the filters
  (`field`, `type`, `series` or `book`, `status`, `canon=true`) and
  `at`. The page sends them to `/api/search` and lists the hits by note,
  with the note's type and scope, the section (linked to its anchor on
  the note's page) or field, and the snippet with the matched words
  marked. The snippet is rendered as text, and the matches are marked
  from the ranges the API gives (#179), so a note's own brackets, such
  as a `[[link]]`, are never taken for a match.
  "Show more matches" asks again with a bigger `limit` (20, 50, 100,
  200), as the API has no offset. At a position, in the Author's view (#300), the
  search sends `marksAt` and lists everything, with notes a reader can find something
  in first, then a dashed line, then the rest. Tags are plain text, as in other lists,
  so a row adds no Tab stop, with one legend line: a note's `enters at` by its name, and
  a hit's own (`ahead of readers`, `no longer true here`, `not placed in the story yet`,
  or, for a snippet that names a later linked note, that note's `enters at`, or its
  status if it's an idea). Every type and field is offered. In the Reader's view, it
  sends `asOf` with `readers=true`, says so ("Searching as of Dracula chapter 9"), and
  links to the Author's view. Nothing a reader can't find shows, with no tags. The
  Type, Field, and Scope filters list only what readers have reached, and what a type
  is a kind of, since filtering by a type includes its kinds (types.md F-TYPE-13). Any
  already chosen stays. A blank `at` fails closed, as on every page. A last answer stays
  on screen while the next loads only at the same position and in the same view.
- **Field search** (#179; the semantics are the maintainer's decision
  of 2026-10-26, the page and API shape are v0 behaviour). Search also
  finds words in field values, and a Field filter (`field=<name>` in
  the URL and the API) searches one field's values only, such as every
  character whose `occupation:` holds "physician". A value matches when
  it holds every word of the query, ignoring case and accents, as the
  main search does: `home: Saltmarsh Docks` matches "saltmarsh". A
  list's items and a mapping's keys and values are its words. A link in
  a value matches by the linked note's name and aliases as well as by
  the text as written, so `home: [[saltmarsh]]` still matches "old
  saltmarsh" after the place is renamed "Old Saltmarsh". As of a
  position, only the values in the canon view there count, and a link
  matches by name or alias only when the reader has met the linked
  note, so a search never confirms the name of a note they haven't.
  (`marksAt` does match it, and tags it.) A
  field hit says which field matched ("Field: home"), and its snippet is
  the value as written, then `→` and the linked note's name or alias
  where that's what matched. The Field filter lists the fields notes
  give values for (`/api/fields`); as of a position, only those of
  values in view, as the Type filter goes by met types. A field no
  note gives a value for is `bad_argument`; as of a position, so is one
  only notes the reader hasn't met give values for, judged from the
  same list, so the answer can't tell a later note's field from a
  made-up one. Types' defaults aren't
  searched: they aren't the note's writing. Field values aren't in the
  FTS5 table: a link's name changes when its note is renamed, without
  the linking file changing, and what counts depends on the position,
  so values are matched when a search asks (they're few next to
  sections), with the search table's tokenizer rules (`unicode61`,
  `remove_diacritics 2`: Latin accents are folded, other scripts' marks
  aren't), and field hits come after the full-text hits. When the
  full-text hits run past the raw cap a filtered search fetches (1,000,
  or 20 times the limit), field hits aren't added at all, since more
  full-text hits would come before them; the result says `truncated`,
  and a narrower query or the Field filter reaches them.
- **Phrase search** (#185; v0 behaviour, not a recorded decision). A
  query's words match anywhere in the text, in any order, except a
  phrase: words in a closed pair of double quotes, such as
  `"silver key"`, match side by side and in order, as the parts of a
  word joined by punctuation (`ilse-tarrow`) always have. Phrases and
  words mix (`"silver key" harbour`), and the same rules hold for text,
  field values, and a linked note's name, in the search page,
  `/api/search`, and the MCP `search` tool, with one exception: the
  search table holds a note's name and aliases as one text, so a phrase
  can run from the name into the first alias, or from one alias into
  the next (`"arkwright the"` finds Zoë Arkwright, "the Quartermaster"),
  where field search matches each name and alias on its own (#192).
  Punctuation between a phrase's words doesn't matter, so
  `"silver key"` matches "silver-key", and a phrase can run across
  sentences, or across a list value's items, as it can across any
  punctuation. Typographic quotes count as double quotes: any opening
  quote (`"`, `＂`, `“`, or German `„`) is closed by any closing one
  (`"`, `＂`, `“`, or `”`), so `“silver key”`, `„silver key“`, and
  `"silver key”` are all phrases. An unclosed quote is plain text, so
  its words are ordinary words, and empty quotes are ignored; no query
  is an error. A `*` touching the end of a phrase's last word, inside
  the quotes or just after them (`"silver ke*"`, `"silver ke"*`), makes
  that word a prefix; any other `*` in a phrase, such as one standing
  alone (`"silver *"`), is punctuation. A snippet marks a phrase as one
  match.
- **The history page** (#27; v0 behaviour, not a recorded decision,
  except the reveal, which applies the #26 decision). An entity page
  has Note and History tabs, below the breadcrumb on both. History is
  the same route with `?view=history`, since the entity's name, slashes
  and all, ends the path; the diff shown is in the URL too
  (`checkpoint`, or `from` and `to`).
  - **The list.** The page lists the entity's checkpoints from
    `/api/history`, newest first, twenty at a time, each with when it
    was written, its author, its message, and how it changed the file,
    under "Uncommitted changes" when the file differs from the latest
    checkpoint. The date is git's author date, which is what the API
    gives; the page says "written", without the jargon, and gives the
    exact time in a tooltip.
  - **The diff.** The page shows the uncommitted changes first, or else
    the latest checkpoint's. Picking a checkpoint shows its own change;
    ticking two compares them (ticking the uncommitted changes as one
    compares with the file now). A short id, tag, or `HEAD~1` in the
    URL is matched to its row through the commit the API resolved it
    to. The UI parses the patch itself, with no diff library: line
    numbers, added and removed lines (named for screen readers too),
    hunk headers, "No newline at end of file", a visible mark for each
    `\r`, U+0085, U+2028, and U+2029, git's mode and empty-file headers
    in words, and the changed words within a line that replaced one. It
    renders text only. A checkpoint's diff, or one between two, can't
    change, so it's fetched once; the uncommitted one is fetched again
    after a few seconds at most.
  - **Position.** History isn't a story view, so it ignores the
    position, and says so. In the Author's view, a note readers meet
    later has its `enters at` tag in the header. The Reader's view
    fetches none of it, and says to switch; where the reader hasn't met
    the note, the banner shows, as on its Note tab.
  - **Limits.** Since the log follows the file's current path (§6), it
    ends at the checkpoint that renamed the file, which it lists as a
    rename; a compare across it shows the rename too. The page needs
    the note's `/api/entity` page, so a note with errors, which the
    index leaves out, has no History tab yet, though `/api/history`
    gives its history by its path (#181). Without history, the page
    gives the API's reason and how to start one.
- **Editing notes** (#28, part a). Four choices are maintainer
  decisions (on #28, 2026-10-27):
  - **One piece at a time.** Each field, the lead, and each section has
    its own Edit control and is edited in place, so the rest of the note
    stays readable. **Edit file** opens the whole file's Markdown and
    YAML source for anything the other controls can't do.
  - **A Markdown source editor with a preview.** The lead and sections
    are edited as Markdown in CodeMirror 6 (MIT), with highlighting,
    `[[link]]` autocomplete, and a Preview toggle. There's no rich-text
    (WYSIWYG) editor: it would rewrite Markdown the writer didn't touch
    and break lossless edits (format.md §12).
  - **Explicit Save.** A Save button or Ctrl/⌘+Enter, and a warning
    before leaving with unsaved changes. Every save carries the hash of
    the file it was based on, as the MCP write tools do (§8, "Stale
    files"); if the file changed meanwhile, the save is refused and the
    writer sees both versions and chooses. Saving never makes a
    checkpoint (checkpoints are #30).
  - **Editing shows everything.** Editing works on the full note, every
    dated entry and section, whatever the story position, with an
    "Editing shows everything" badge. At a position, that's the Author's
    view (#300), so it needs no other read of the note, and its tags stay.

  Fields, with dated entries' `from` and `until`, are part b of #28;
  chapter fields and inherited values are #138. The rest is v0
  behaviour, not a recorded decision.
  - **One write path.** The write tools moved from `Wolfden.Mcp` to
    `Wolfden.Index.Writing` (`DenTools`), as the read queries did
    (`DenReader`, #174), and the API's edits go through their edit loop:
    the same `DenWriteCheck` (`invalid_edit`), hash checks
    (`stale_file`), frozen books (`frozen`), F-ID-6 ids, warnings,
    `effects`, and `pinned_canon`, which the API calls `pinnedCanon`.
    `serve` holds one `DenTools` for the server, so its writes are made
    one at a time, and the den's write lock makes them take turns with
    other Wolfdens' (#221). A lead or section edit replaces the piece's text
    between the blank lines around it (`MarkdownFile.WithLead`,
    `WithSectionBody`); a section rename is `rename_section`'s den-wide
    link rewrite. Every edit lists the pinned facts it changed, added,
    or removed, comparing the note's facts before and after.
  - **The API.** `GET /api/entity/source` (`entity`, `universe`) reads
    the file itself, not the index: its `hash`, its whole `text` (LF
    line breaks, no byte order mark, with `lineEndings` and
    `byteOrderMark` saying what the file has), `frozen` (why it can't be
    edited, or null), the `lead`, each section's `index`, `name`,
    `level`, `line`, `comment`, own `body`, `pinned`, and `parent`, and
    the type's `standardSections`. `/api/entity` adds `frozen` too, so
    the page can turn its controls off without asking. Edits are `POST`
    with a JSON body of `entity`, `universe`, `expectedHash`, and the
    edit's own members, each at most once: `/api/edit/lead` (`text`),
    `/api/edit/section` (`index`, `name`, `text`),
    `/api/edit/section/add` (`name`, `text`, and `beforeIndex` with
    `beforeName`, or neither for the end; the new section takes the
    level of the one it goes before, or 2 at the end),
    `/api/edit/section/rename` (`index`, `name`, `newName`),
    `/api/edit/section/remove` (`index`, `name`), and `/api/edit/file`
    (`text`). Part b adds `/api/edit/field` beside them (below). #67 adds a note's
    other names: `/api/edit/name/add` (`name`, `knownAs`, `from`; `update_fields`'s
    add to `aliases` or `known_as`, and a `known_as` name needs a `from`) and
    `/api/edit/name` (`name`, and one of `newName` with `keepOld`, `from`, `plain`,
    or `remove: true` alone; `change_alias`). Each answers the
    write tools' `WriteResult`. Errors add 409 for `stale_file`,
    `frozen`, `ambiguous_section`, and `already_exists`; 422 for
    `invalid_edit` and `unsupported_edit`; 404 for `unknown_section`;
    503 for `den_busy`; and 500 for `write_failed`. A `stale_file` error has `current`, the
    file's source as it is now, so the page can show both versions.
  - **Naming a section.** A section is sent by its `index` (its place
    among the file's sections, subsections included) and its `name`
    together, both checked against the file the hash names; a mismatch
    is `unknown_section`. So neither a stale page nor two sections of one
    name can send an edit to the wrong one.
  - **Not from another site.** Besides the cross-site guard and host
    filter (#23), an edit must be sent as `application/json`, or it's
    415 `unsupported_media_type`: a plain HTML form can't send that, and
    a script on another site needs a CORS preflight, which the server
    never allows. An edit endpoint answers only `POST`. `serve
    --universe` pins edits as it pins reads.
  - **What the editor holds.** A section's editor holds its own text as
    written, with HTML comments, up to its first subsection; its section
    comment (`<!-- wolfden: ... -->`, format.md §9.2) is kept aside,
    shown above the editor, and changed with Edit file, so a body edit
    can't change a section's status, range, or pin by accident. Text
    that would add a heading or section comment is refused
    (`bad_argument`), as `WithSectionBody` refuses it. The lead's editor
    holds the lead as written, title heading included. Line breaks are
    LF in the editor and written as the file's own (the more common one
    in a file that mixes them), and the byte order mark is kept. Text
    that equals the piece's, line breaks aside, writes nothing, even in a
    file that mixes them, and the page doesn't send it (Save is off until
    the text changes).
  - **Section names.** A name typed with a heading's `#` marks (`## Arc`)
    is refused, with the name to give instead, rather than stripped (a name
    that only starts with `#`, such as `#1 Fan`, is a name like any other): the
    level comes from where the section goes, and a writer's text isn't
    changed behind their back. Adding or renaming to a name another
    section has is saved, and `effects` says a link to it reaches the
    first one (F-LINK-9); `rename_section` says so too now.
  - **Edit file.** It refuses a change to, or removal of, an existing id
    (F-ID-4, `unsupported_edit`). A note without one may be given one,
    but not an id another file of the den has (`already_exists`), checked
    through the index across universes. `DenWriteCheck` reads only the
    file written (F-GEN-4), so it doesn't see F-ID-3, which compares
    files; the MCP tools never take an id by hand, so only Edit file
    needs the check. An id that isn't one is F-ID-1, an error, so
    `invalid_edit`. Edit file doesn't rewrite links when the name or an
    alias changes; the result says so.
  - **Words for writers.** The edits' errors and effects are shown on the
    page, so the API's `DenTools` uses its own `ToolWords`
    (`WriteEndpoints.Words`): they name the search box, an editor, and
    `wolfden check`, and say that an AI assistant working through Wolfden
    can rename a note or change an alias and rewrite the links, where the
    MCP tools' words name `rename_entity` and `change_alias`. A `frozen`
    refusal says to use **Unfreeze…** on the book's page, where the MCP
    tools' words say to ask the writer (#209). The command line has its
    own words too: its options, `wolfden query`, and, for `frozen`,
    `wolfden unfreeze <book> --universe <key>`, with the `--den` the
    command was given.
  - **Removing a section** takes its subsections, and doesn't rewrite
    links to it by heading: `effects` lists them, and what they reach
    now (a later section of that name, or only the note).
  - **The page.** CodeMirror is loaded the first time an editor opens,
    and mounted in a shadow root: its styles are a constructed style
    sheet there, which the page's Content-Security-Policy doesn't
    govern, where in the page they'd be an inline `<style>` that
    `style-src 'self'` refuses. `[[` autocomplete asks the lookup
    (`/api/entities/lookup`, "Looking notes up by name and alias" below)
    for the words typed so far. Prose stays readable as plain Markdown,
    so a pick is linked by the text it matched: `[[Mina Harker]]` for a
    name, `[[Madam Mina]]` for an alias (aliases resolve like names,
    F-LINK-6). An alias that wouldn't reach the picked note alone gives
    way to its name, if that would (the lookup's `linkText`, resolved as
    links are); failing both, it's linked as a `ref` field's picker
    links it, `[[slug]]` or `[[id]]`. A note no link is sure to reach is
    offered with the picker's hint (add an id with Edit file) and picking
    it inserts nothing: a link by its name could reach another note
    cleanly, by a file name, with nothing for `wolfden check` to report.
    Preview uses the page's own
    renderer, with links shown as written until the note is saved. One
    piece is edited at a time, and an editor opens only on the source
    fetched when it opened, never a cached answer. Leaving through the
    app with unsaved changes (a link, Back, or the position bar) asks in
    the page (TanStack Router's blocker), and closing or reloading the
    tab makes the browser ask (`beforeunload`); Cancel or Escape asks the
    same way before dropping unsaved text. A change of position ends the
    edit: the page is made again for the new position. Remove asks
    in the page, never in a browser dialog. A stale save shows the
    writer's version and the file's side by side, with the lines that
    differ marked, and offers Keep mine (save it again on the new hash,
    to the section at the same index with the same name, or the only one
    of that name),
    Take theirs, and Copy mine and reload (copy it to the clipboard, and
    open the editor on the file as it is now). A save refreshes every
    cached answer, since a rename or a new name can change lists,
    searches, and links anywhere, and shows its effects, the pinned
    canon it touched, the file's warnings, and that it isn't a
    checkpoint. At a position, a note can be edited from the Author's view, even
    one the reader hasn't met, and the Reader's view is read-only. A write from outside refreshes an open page the
    same way: while it's visible the page asks for `/api/den/files` every 5
    seconds, and when the answer changes it refreshes every cached answer.
    While an editor or form is open it waits, the note's page stays as the
    editor opened on it, and an error waits for the form to close, so
    neither a new answer nor a failed one can take the editor away. That
    question is the one thing asked again by itself: other answers aren't
    refetched on focus or when the network comes back, and queries and
    writes ignore the browser's online state, since the server is on this
    computer. A
    failed answer is tried again every 15 seconds while the server answers
    (#285).
  - **Adding images** (#187; v0 behaviour, not a recorded decision). The
    lead, section, Add section, and Edit file editors have **Add
    image…** under them, and take an image dropped or pasted into the
    text; dropped or pasted files that aren't images are left to
    CodeMirror, and a drop's other files are named in the status line.
    A paste whose clipboard has non-empty `text/plain` is left to
    CodeMirror even when it holds an image too, since Office on Windows
    adds a bitmap of the copied text. While a batch is being added, the
    button is off and more images dropped or pasted are refused with a
    note (not queued). Each file goes to `POST /api/entity/image?entity=&universe=&name=`,
    with the file itself as the body, sent as `application/octet-stream`
    or an `image/...` type (anything else is 415, so a plain HTML form
    can't send one, as for the JSON edits). It's the write tools'
    `DenTools.AddImage`, so it holds the den's write lock, writes
    atomically through the den store, never replacing a file
    (`DenWriteMode.Create`), lands in the server's checkpoint ledger,
    and doesn't checkpoint. The server picks the path: the `images/`
    folder beside the note's file (an existing one in another case, such
    as `Images/`, is used as it's spelled, in the path and the link; a
    file or folder link of that name is `already_exists`, 409, saying
    what to rename; as Obsidian's "In subfolder under
    current folder" does, and as "Where to put them" in
    `using-wolfden.md` suggests), and a name made from `name` as a slug
    (F-TPL-6's rules; `image` if nothing's left; `-image` added to a
    Windows device name), with its extension lowercased. A name taken
    in that folder, in any case, gets `-2`, `-3`, and so on, and the
    result's `effects` says so. Only the image types `serve` shows are
    taken, and the contents must be the type the extension says, by
    their first bytes (an SVG must be well-formed XML whose first
    element is `svg`, in UTF-8 or BOM-marked UTF-16; its DOCTYPE is
    skipped, never read, so an SVG that declares entities, as
    Illustrator's SVG 1.1 Save As does, is refused with how to export it
    without them); a mismatch is
    `bad_argument`, naming the type it is. A body over 10 MiB is 413
    `too_large`. A note in a final book is `frozen`, with the
    Unfreeze hint, and `serve --universe` pins it as it pins edits. The
    answer (201) has the image's den `path`, the `link` to write
    (relative to the note's folder, F-LAY-9, such as
    `images/castle-map.png`), `alt` text made from the file's name
    (`-` and `_` as spaces, brackets dropped, at most 100 characters cut
    at a word), `bytes`, and `effects`.
    The page puts `![alt](link)` in at the cursor (or where it was
    dropped); the note is saved only when the writer saves it, and an
    image added in an edit that's cancelled stays in the den.
- **Editing fields** (#28, part b). The maintainer decisions above
  apply: one field at a time, explicit Save with stale-file conflicts,
  and the full note while editing. The rest is v0 behaviour, not a
  recorded decision. Chapter fields are below (#225).
  - **The API.** `/api/entity/source` adds `fields` (each field under
    `fields:` in file order: its `name`, the `line` of its key, and its
    `value` as written, as JSON, with a plain `true`/`false` as one, any
    other plain scalar YAML doesn't read as a string (a number in any
    form, `0x1F`, `1_000`, `.inf`, `True`, `yes`) as
    `{"$plain": "2.50"}` with its text as written, and every other
    scalar as its text) and `fieldTypes` (the
    type's fields in field order, each with `type`, `list`,
    `temporal`, `required`, enum `values`, ref `to`, `accepts` (`to`
    and every type that's a kind of one, types.md F-TYPE-13; null for
    any type), `description`, and `default`, then the universal `placeholder`; `row` is edited in
    the file). `POST /api/edit/field` takes `field` and `changes`, a
    list of `{op, path, value, index}` with `op` one of `set`, `add`,
    `remove`, and `move` (#197), and `path` a list of keys and item numbers under the
    field (none for the field itself). It's `update_fields` with every
    path under `fields.<field>`, so an edit can't reach `id`, `name`,
    `status`, or another field, and it has the same hash check, frozen
    books, write check, pinned canon, effects, ledger, and lossless
    edits. Numbers go both ways as text (`{"$plain": "2.50"}`), since a
    browser's JSON number would turn `2.50` into `2.5` and cut a long
    number's digits; the server writes the text as given, plain, so a
    value rewritten whole or moved keeps its form (`0x1F`, `.inf`) and
    never gains quotes. `$plain` (it was `$number` before #199, and
    carries `True`, `yes`, and timestamps too) takes only such a
    value: text YAML reads as a string, a null (`~`, `null`), YAML 1.1's
    `<<` and `=`, and anything with a line break are `bad_argument`. A
    mapping whose only key is `$plain` is sent as `{"$$plain": …}`, one
    more `$`, and the write takes it off again, so it's never read as a
    plain value. A
    number that isn't whole is written plain by the MCP tools too now,
    and one too large to be a number (`1e400`) is `invalid_edit` for
    both.
  - **Only what changed.** The page works out the changes from the value
    as written and the form, following each list item and dated entry
    from where it came: one taken out is removed, a new one added where
    it goes, one in another place is moved there in place (`move`, from
    the top), and each one kept has only its changed keys set where it
    ends up (`[1, "until"]`). A move keeps the item's quoting and the
    comments that belong to it (format.md F-RT-7, #197); so does a
    changed item, which is set in place. A reorder may be sent as
    several moves (each kept item moved up into its place in turn),
    which give the same bytes as one move would. Dated entries are
    never moved (F-TMP-7): the form has no control for it, and the
    server refuses a `move` of one. Removing an item with a comment
    says so in `effects`, and so does a client that takes an item out
    and adds its value again, which writes it afresh ("Removed
    fields.tags.2 and added the same value as item 0 of fields.tags,
    written afresh, so the comment on it was dropped: '# from
    Varna'"). It doesn't call that a move, since another item may just
    have the same value. A value the writer didn't touch,
    and a list whose items are all as they were, is sent back as
    written, so `Dried` isn't rewritten `dried` and an entry's
    `value: ~` stays `~`. A plain value that gains a date or a pin, or a field whose
    shape changes, is set as a whole. Removing the last item leaves the
    key as F-RT-6 says (`key:` for a block list, `[]` for a flow one).
  - **One form for every field.** A field is edited as its entries: a
    plain value is one entry with nothing but its value, and saves as a
    plain value again unless the writer gives it a From, a status, or a
    pin. A field that changes over the story (`temporal`) lists each
    entry with From and Until, and has **Add a dated entry** and
    **Remove entry**; one that doesn't has one entry, with "Known from",
    status, and pin under a disclosure (F-TMP-8). Its entries aren't
    reordered (F-TMP-7). List fields have **Add item**, **Remove**, and
    move up and down buttons.
  - **Controls.** `string` an input, `text` a text area, `number` an
    input checked against F-FT-6, `bool` a yes/no dropdown, `enum` a
    dropdown of its values (a value written in another case shows as
    its key; one that isn't a value shows as written, so the form never
    changes it unasked), `date` its text and an optional sort key (the
    mapping form of F-FT-11 when there's a sort key), `ref` a combobox
    that asks the lookup (`/api/entities/lookup`, below) as letters are
    typed and offers the notes of the types the field `accepts` whose name,
    alias, or file name has a word starting with each word typed (not
    notes that only mention it), showing the alias a note was found by
    ("Mina Harker, as *Madam Mina*"), and `position`,
    `from`, and `until` a book
    and chapter picker from the den's positions, with "Type a
    position…" for any other text. Status is a dropdown of the six
    statuses or "the note's status", and pin a checkbox.
  - **The link a pick writes.** `[[slug]]`, by the note's file name, as
    the den's own files write links, where no other note of the universe
    has that file name, ignoring case as links match (F-LINK-4). A slug
    is the second tier of resolution (F-LINK-6), so a namesake could
    take the link or make it ambiguous (F-LINK-7): then, or if the page
    can't list the universe's notes, or the file name isn't a slug, the
    link is by id (`[[01J...]]`), which reaches only that note. A note
    without an id is linked by its name only where the name can be
    written in a link (no `|`, `#`, `[`, `]`) and no other note's file
    name or name matches it; otherwise the pick is refused, with "add
    an id with Edit file", rather than writing a link that may not reach
    it. Typed words are split as names are, so `dr. van` finds "Dr. Van
    Helsing".
  - **Looking notes up by name and alias** (#198; v0 behaviour, not a
    recorded decision). `GET /api/entities/lookup?query=&universe=&type=&asOf=&limit=`
    gives the notes of one universe (the pinned one under
    `serve --universe`) that each word typed starts a word of: of the
    name, an alias, or the file name, or the id when 10 or more of its
    characters are typed (a ULID's first ten are its time, which notes
    made together share). Words are split and compared as search does
    (`unicode61`, `remove_diacritics 2`), so `zoe ark` finds Zoë
    Arkwright and `mur wil` finds the alias Wilhelmina Murray. Any
    apostrophe F-LINK-4 counts splits a word as a straight one does, so
    `mina’s jour` finds "Mina's Journal". Matches
    come best first: the whole text as a link would match it (F-LINK-4)
    to the name, then to the file name or id, then to an alias; then a
    name that starts with the words, in order; a name that holds them;
    a file name that holds them; an alias that starts with them; an
    alias that holds them; the start of the id. The exact tiers compare
    as links do (F-LINK-4: case folded, accents kept), so `zoe arkwright`
    isn't an exact match for Zoë Arkwright, and ranks as a name that
    starts with the words. Ties go by name, then
    path. Each match says what matched (`matchedBy`: name, slug, id, or
    alias) and, for an alias, which one (`alias`), and gives `linkText`:
    that text where `[[text]]` reaches the note and no other, by the
    link resolver's tiers (F-LINK-6; a tie, F-LINK-7, has no file's scope
    to break it, so it doesn't count), or for an alias that doesn't, the
    name where that does; otherwise null. As of a position, it's
    resolved among the notes met by then, as `get_entity` matches a name
    there. `type` is a type key,
    or several separated by commas (a `ref` field's `accepts`), and
    matches those types and their kinds, as every filter by type does
    (types.md F-TYPE-13). So a field's `to` and its `accepts` find the
    same notes; the picker sends `accepts`. A type no
    enabled pack defines and no note uses is skipped, as `to` skips it
    (types.md F-FT-9, a warning); only when none of them is known is the
    lookup refused (`unknown_type`), and the picker then says it couldn't
    look notes up rather than showing an empty list. `limit` is 1
    to 100, 20 by default, and `truncated` says more matched. The
    editors never send `asOf`: an editing surface shows everything
    (#28), so a field or section can link a note the reader meets later.
    With `asOf`, only the notes met by then are looked at (#26), for a
    reading view, by the names readers know there (format.md F-VIEW-7):
    every plain alias, and a `known_as` name from its `from`. The MCP tools have no lookup of their
    own: `search` matches names and aliases (`madam*`), and `get_entity`
    takes a name or alias as a link does.
  - **Checked in the page.** Numbers, links, sort keys, one-line
    strings, empty values, and empty list items are checked before
    sending, with the message by the control and focus on the first.
    Positions are checked by the server: a `from` or `until` that isn't
    a position is an error, so `invalid_edit`, but a `position` field's
    value that isn't one is saved with the F-FT-10 warning, as a
    field's data.
  - **What the form can't show.** A field the type doesn't define (or
    any field of an untyped note), and a value the form can't hold (a
    list in a field that isn't a list, such as an unquoted `[[link]]`,
    F-FLD-6; a list that mixes entries and plain items, F-TMP-1; a date
    with `calendar`; a mapping that isn't a date) show as written with
    **Edit in file** and why. An entry's other keys are kept.
  - **Add field and Clear field.** Add field offers the type's fields
    the note doesn't set, or sets to nothing, and opens that field's
    form. Clear field asks in the page, then removes the key.
  - **A stale save.** If only another part of the note changed, the
    page sends the same changes again on the new hash without asking,
    but only where nothing of the field can be overwritten: no change
    sets the field as a whole, and the field's lines (from its key to
    the next field, or for the last one to the closing `---`, in a CRLF
    file too, #199), comments and quoting included, are byte for byte
    as they were. Otherwise it shows the writer's value (or
    "(not set)" for Clear field) and the file's side by side. Keep mine
    sends the same changes on the new hash if the field itself didn't
    change, and otherwise sets the field to the writer's value as a
    whole, or removes it again for Clear field; Take theirs drops it.
    There's no "Copy mine and reload" for a field.
  - **Focus.** Edit in file by a field returns focus to that field's
    button when it closes.

- **Checkpoints in the web UI** (#30; manual by default is the
  maintainer's decision of 2026-10-27, §12 Q4; the rest is v0
  behaviour, not a recorded decision).
  - **The API.** `GET /api/checkpoint` says whether the den has
    history that can make checkpoints, the latest checkpoint (its id and
    time only, since its message could name another universe's notes),
    what git is in the middle of, if anything (`inProgress`: a rebase, a
    merge, a detached HEAD), and the den files a checkpoint now would
    record (`IHistoryProvider.Pending`: what changed since the latest
    checkpoint, or every den file before the first, from one raw diff
    with names and no patches), each with its kind and universe.
    `POST /api/checkpoint`
    takes `{"message": "…"}` and makes one. Both go through the same
    `CheckpointLedger` (`Wolfden.Core`) and history provider as the MCP
    `checkpoint` tool, so a checkpoint records only den files and
    leaves what the writer staged alone; the writer is the author, with
    no co-author. The message is required, at most 5,000 characters,
    and has no control characters but line breaks and tabs. On top of
    the API's cross-site guard, a POST must be `application/json`
    (anything else is 415 `unsupported_media_type`), which a page on
    another site can't send without a CORS preflight the server never
    answers. Without history, the status says why and how to turn it
    on (**Turn on history**, or `wolfden init --git` in the den's
    folder), with `turnOn`: whether this server can (`possible`, or a
    `reason` why not) and whether git needs a name or email first
    (`needsName`, `needsEmail`); and a POST is 409
    `history_unavailable`. A refusal by git (a hook, signing,
    a lock) or a git that didn't finish in time is 500
    `history_failed`, with git's reason. A checkpoint made while git is
    in the middle of something says so in its result (`inProgress`).
  - **Saved here or elsewhere.** The server keeps a ledger of the files
    its own write endpoints saved (#28): the editing endpoints' `DenTools`
    records each save in the server's `CheckpointLedger`, as an MCP
    session's does in its own, so each change says whether this server
    saved it and nothing changed it since (`savedHere`). Anything else
    changed another way: in an editor, by an agent through MCP (another
    process), or before the server started. The ledger's lock is held
    only to take a snapshot before a checkpoint and to reconcile after
    it, never while git runs, so a save or a status never waits on a
    checkpoint (one waiting on a passphrase prompt, say). A file saved
    while a checkpoint runs may or may not be in it, so it stays the
    server's; the next checkpoint forgets it if the history holds it.
  - **Turning history on** (#195; §6, "Turning history on"). The
    no-history dialog offers **Turn on history**, with a checkbox, ticked
    by default, for the first checkpoint, and name and email fields when
    the status says git needs them (or when the server answers
    `identity_needed`), which say a pen name and a placeholder email will
    do. Before the den is a repository, the status can't be sure
    (`turnOn.identityCertain` is false), so the fields are optional: git
    may have a name and email for the repository once it's made, and the
    server asks again only if it doesn't: `identity_needed` carries
    `needsName` and `needsEmail`, and the dialog then asks for just
    those, required, keeping a field the writer already filled in. What
    it asks for stays put while the retry is pending (#207). When git has
    them, **Use a different name for this den**, closed by default, opens
    the same two fields, both required, and what that does to signing;
    they're sent only while it's open, and what git has isn't shown (#314).
    Enter on the first-checkpoint box, which has focus at first, doesn't
    submit, and a held Enter does nothing in the dialog.
    `POST /api/history/turn-on` takes `{"authorName"?, "authorEmail"?,
    "firstCheckpoint"?}` (it defaults to true), with the same guards as
    the checkpoint POST (cross-site, `application/json`, no query, 64 KB),
    and answers with the steps done, in words, `repositoryMade`,
    `gitIgnore` (`created`, `added`, or `present`), `identitySaved`, and
    the first checkpoint (its id, time, author, message, and file count,
    never file names). A name or email that's refused is 400
    `bad_argument`, a missing one, or one given alone, 400 `identity_needed`; a den in a
    larger repository, one with checkpoints already, or no git is 409
    (`in_repository`, `has_repository`, `git_not_installed`); a server
    without a way to turn it on is 409 `history_unavailable`; a step that
    fails part way is 500 `history_failed` (or `write_failed` for
    `.gitignore`), with what was done and what wasn't, and `identitySaved`
    if that try left a name and email saved. Two requests take
    turns, and automatic checkpoints skip their poll meanwhile. The
    status's git calls stop when the request is aborted. The first checkpoint goes through the server's
    `CheckpointLedger`. After it, the page asks for everything again.
  - **Pinned.** A checkpoint covers the den (§6), as the
    MCP tool's does in a pinned session, so `serve --universe` records
    every den file too. It doesn't name files in other universes, as for
    the rest of a pinned server: it lists the pinned universe's files
    and the den-level ones, and counts the rest (`otherUniverses`).
  - **The indicator.** The top bar shows "3 notes changed since the
    last checkpoint · Checkpoint…" ("not checkpointed yet" before the
    first), and nothing when nothing has changed. It asks again every
    minute, when the window gets focus back, and whenever its query
    (`['checkpoint-status']`) is invalidated, which the editor's save
    does (it invalidates every cached answer). Without history it says
    "No history · Turn on…"; when the status can't be read, "History
    unavailable", which opens the reason.
  - **The dialog.** A modal dialog (not the browser's): focus goes to
    the message box and stays in the dialog, on the dialog itself while
    the checkpoint is being made and back on the box after an error.
    Cancel closes it; Escape or a click outside does too, but asks
    first if a name has been typed, as the editor asks before
    discarding. Focus goes back to the indicator. While git is in the
    middle of a rebase or the like, or HEAD is detached, it warns that
    the checkpoint becomes part of it. It lists the changes grouped
    for reviewing a notes pass: den-level files first, then each
    universe's own files, then each series and book, paths shortened
    within the group, with "saved in Wolfden" on the server's own saves.
    The message box starts empty: the writer names the checkpoint, and
    an empty name is refused before anything is sent. The result, or
    git's reason for refusing, shows in the dialog; afterwards the
    indicator and the History tabs (#27) ask again, so the new
    checkpoint shows. Without history, the dialog explains how to turn
    it on instead.

- **Freezing in the web UI** (#31; the steps and the tags are the
  maintainer's decisions in §5; the rest is v0 behaviour, not a recorded
  decision).
  - **The API.** `GET /api/book/freeze?book=<key>&universe=<key>` gives
    the book's name, status, the hash of its `book.md`, whether the den
    has history (and why not), for a draft book the tag a freeze would
    add (`nextTag`), and for a final one whether its freeze is
    `unfinished` (by the test in §5), and how many changed files outside
    the book a checkpoint now would also record (`otherChanges`).
    `POST /api/book/freeze` and
    `POST /api/book/unfreeze` take `{book, universe, expectedHash}` as
    `application/json` and answer with what was done: the status now,
    `snapshot` (`tagged`, `already_tagged`, `no_history`,
    `checkpoint_failed`, `tag_failed`, `not_tagged`, `checkpointed`,
    `not_checkpointed`, or `not_frozen`), the checkpoint and tag by name,
    and a `message` for the writer. A step in the history that fails
    isn't an HTTP error, since the status change stands: it's a
    `snapshot` and a message that says what to do. A stale hash is 409
    `stale_file`. They write through the editing endpoints' `DenTools`,
    so one write runs at a time and the save lands in the server's
    ledger, and the checkpoint goes through the same ledger as the
    checkpoint dialog's. The result names the checkpoint only by its id,
    and counts the files it recorded outside the book (`otherFiles`)
    without naming them, so a pinned server names nothing in other
    universes.
  - **The page.** The book's page has **Freeze…** (or **Unfreeze…** for
    a final book) under its title. It asks first, in the page, saying
    what will happen: what can't be edited, and, with history, the
    checkpoint and the tag it will add, with the count of other changed
    files it takes in (or, without, that no snapshot is taken). Escape
    or Cancel closes it. Afterwards it shows the result's message,
    marked as a warning when a step failed, and every cached answer is
    asked again (the control waits only for its own status), so the book's notes lose (or regain)
    their edit controls. A final book that no snapshot holds as it is
    now (edited outside Wolfden, marked final by hand, or a checkpoint
    or tag that failed) also has **Finish freezing…**, which freezes
    again.
- **New notes in the web UI** (#29). Four choices are maintainer
  decisions (on #29, 2026-10-28): a minimal form (the type, the name, and
  where the note goes, by default where the writer is), then the new
  note's page, edited one piece at a time as in #28; no `from` by default,
  with "Enters the story at <position>" offered when the writer views at
  a position (only one the universe has, and only where the chosen place
  can see it: the universe, a series it's in, or its own book, so a
  book's note never enters in another book, F-ENT-6); two ways in, a top-bar **New ▾** menu (any type) and a
  **New <type>** button on each type's list, with the type and the place
  filled in; and `wolfden new` in the CLI (§7). The rest is v0 behaviour,
  not a recorded decision.
  - **The API.** `GET /api/types` lists the den's enabled types (key,
    display name, folder, description, pack, whether it's built in, and
    `kindOf`, every type it's a kind of, nearest first), by display name. Types are the den's, not a universe's (types.md
    F-PACK-7), and containers and chapters aren't types (F-TYPE-2), so
    every type listed can be created anywhere: none is held back from
    the menu. `POST /api/entity/create` takes `{type, name, universe,
    series, book, from, allowRelinks}` as `application/json` and calls
    `create_entity`'s code (`DenTools.CreateEntity`) through the editing
    endpoints' `DenTools`, so it runs one write at a time, lands in the
    server's checkpoint ledger ("saved in Wolfden"), and respects a
    server pinned with `serve --universe`. It answers 201 with the write
    result, whose `entity.key` the page navigates to. A create refused
    because links would move to the new note (§8, "Creating") answers 400
    `bad_argument` with those links in `relinks`, in words; resent with
    `allowRelinks: true`, it's made. It takes no slug,
    status, aliases, or field values: the form doesn't ask for them, and
    the note's page edits them.
  - **The page.** The top bar's **New ▾** is a menu button (arrow keys,
    Home, End, Escape) listing the types; choosing one opens a modal form
    whose place starts as where the writer is: the book of a book or
    chapter page, the series of a series page, a note's own place on its
    page, and otherwise the universe. The contents tree's type groups
    under each place are the type lists, and each has **New <type>**. A
    final book is listed, marked final; choosing it says it takes nothing
    until it's unfrozen, and Create is off (the server refuses it anyway,
    `frozen`). A name that would take links from another note is
    refused in the form: it lists the links that would move, takes focus,
    and offers **Create anyway**, which resends with `allowRelinks`;
    changing the type, name, place, or position ends the refusal, and
    Cancel still closes. After a create, every cached answer is asked again, as
    after any save (`useAfterSave`), and the page goes to
    the new note, keeping the story position, with a notice naming the
    file that takes focus and lasts until the writer leaves the page. A
    note made at a place the reader meets after the position opens with
    its `enters at` tag, in the Author's view, or the usual "not met yet"
    banner in the Reader's view. Escape or Cancel closes the form and returns focus to what
    opened it.
  - **New note on the place pages** (#203). A type group exists in the
    tree only where that type already has notes, so each universe,
    series, and book page also has **New note ▾**: the same menu as the
    top bar's, listing every enabled type, but with its place fixed to
    the page's universe, series, or book. A final book's page has none,
    as it has no Add chapter.
- **New series and books** (#213). Maintainer decisions (on #213,
  2026-10-29): series and books are created from the web UI and the CLI
  (`wolfden new series`, `wolfden new book`, §7); a new one goes at the
  end of its container's story order by default, and the writer can
  place it after another work instead (**Place after…**, `--after`),
  or at the start (Orchestrator decision on #213, from "allow the
  ordering to be overridden": "At the start", `--first`);
  a new book can start with N chapters, keys `"1"` to `"N"` and `number`
  1 to N, or none. Reordering existing works and adding or inserting
  chapters are separate. The rest is v0 behaviour, not a recorded
  decision.
  - **Where it goes and what it's called.** A universe's page makes a
    series or a standalone book, and a series' page a book of the
    series. The folder's name, the key, is made from the name as a
    note's slug is (types.md F-TPL-6, so "Mina's Journal" is
    `minas-journal`), but a key that's taken, by a
    folder of any case or by a clash F-KEY-5 forbids (a book key
    anywhere in the universe, or a series key and a standalone book
    key), is refused (`already_exists`) rather than given `-2`, since
    positions name a book by its key alone; so is a name that gives no
    key starting with a letter (F-KEY-2), with its own message for a
    name in another script ("Дракула"): keys are a–z and 0–9 for now
    (#133), so it asks for a Latin name too. The message says the name can
    be changed in the file afterwards, since the key never follows it.
  - **The files.** The new file holds `id`, `name`, and for a book its
    `chapters` as `- { key: "1", number: 1 }` items, as format.md §6.4
    writes them; no `status` (a draft by default) and no body. The
    container's list (`books` in `series.md`, `order` in `universe.md`)
    gets the new key through the lossless list edit
    (`WithNewListItem`), the rest of the file kept. A list that was
    left out gets one when there's a work already (F-UNI-3, F-SER-3),
    with that work first, so `check` stays quiet; with none, the file
    isn't touched. Works a list leaves out, which come after the listed
    ones by key, are added to it in that order first, so their place in
    story order doesn't change and the new work goes where it was asked
    to. A list that already names the key (a work planned ahead) keeps
    that place (works the list left out are still listed first), and
    placing it elsewhere then is refused. A
    `universe.md` or `series.md` without an id gets one (F-ID-6). Both
    files are checked as the den reads them before either is written
    (`DenWriteCheck` takes a new container file), written together, and
    if the second write fails, the new file and its folder are removed,
    with a `books/` or `series/` folder above it that the create made.
  - **Links and freezing.** Containers aren't link targets (format.md
    F-LINK-5), so a new one never changes which note a link reaches,
    and there's no "create anyway" here. A final book's scope is its
    `book.md`, outline, and notes (F-BOOK-1), so `series.md` and
    `universe.md` are never in one, even when every book of the series
    is final: a freeze doesn't stop a new book beside it. A write into
    a final book's folder is refused (`frozen`) as a backstop that no
    create reaches. Placing a new work before a final book is allowed
    (none of its files change), though canon views at its positions then
    include what the reader meets in the new work first.
  - **The API.** `GET /api/works?universe=<key>&series=<key>` gives the
    universe's series and standalone books (or the series' books) in
    story order, by kind, key, and name, with the path and hash of the
    file that lists them. `POST /api/series/create` takes `{name,
    universe, after, first, expectedHash}` and `POST /api/book/create` takes
    `{name, universe, series, after, first, chapters, expectedHash}`, as
    `application/json` (`first: true` is the start, not with `after`), through the editing endpoints' `DenTools`, so
    one write runs at a time, both files land in the checkpoint ledger,
    and a pinned server creates only in its universe. `expectedHash` is
    required, and a file that changed since is 409 `stale_file`. An
    `after` that isn't one of those works, a series named that isn't
    there, or `chapters` outside 1 to 1000 is refused. It answers 201
    with the kind, key, name, series, path, hash, id, chapter count,
    effects in words, warnings, and every file written.
  - **The page.** The universe's page has **New series** and **New
    book** under its title, and a series' page **New book**. The modal
    form asks for the name, **Place after…** (a list of the works,
    starting with "At the start" and "At the end", the default; not
    shown when there are none yet), and
    for a book **Number of chapters**, optional, a whole number from 1
    to 1000 (text the number field can't read is refused too, not taken
    as none), checked before anything is sent. A stale file says the
    story order changed in another program and asks for the works
    again, keeping what was typed. After a create every cached answer is
    asked again and waited for, then the page goes to the new series or
    book, with a notice naming the file and what the create did, which
    takes focus and lasts until the writer leaves the page. Escape or
    Cancel closes the form and returns focus to the button.
- **Adding chapters** (#215). Maintainer decisions (on #215,
  2026-10-29): a book's page has **Add chapter** (at the end) and
  **Insert chapter…** (after a chapter), with an optional **Title**, and
  the CLI has `wolfden new chapter` (§7). A chapter inserted after key
  `K` gets the first unused letter suffix, `Kb`, then `Kc`, and so on
  (after `2b`, `2ba`); one at the end gets the next unused whole-number
  key. The `number`s after an insert are renumbered, and keys never
  change (format.md F-BOOK-6). Maintainer decision (on #229,
  2026-10-30): when the next chapter's key is `K` with letters added,
  the inserted key takes a suffix that sorts between them as an ASCII
  string, as F-OUT-4 orders row suffixes (between `2` and `2b`, `2a`),
  and falls back to the first unused suffix when none does (between `2`
  and `2a`, with `2b` taken, `2c`). Existing keys never change.
  Removing chapters, and editing a chapter's title from the UI, are out
  of scope. The rest is v0 behaviour, not a recorded decision.
  - **Keys** (`NewChapter` in `Wolfden.Core/Editing`). The rule behind
    both suffix examples: a key that ends in a digit starts at `b` (as if
    `2` were `2a`), one that ends in a letter at `a`, so `prologue` gives
    `prologuea`. When every single letter is taken, two letters follow
    (`2ba`, `2bb`, …), and a key that would pass 64 characters (F-KEY-1)
    is refused. The next whole number is one more than the largest key
    that's all digits (`07` counts as 7), or `"1"`. Inserting after the
    last chapter is adding at the end, so the end always gets a whole
    number. Chapter order is the `chapters` list (F-BOOK-2), so "sorts
    between" only matters when the next key is `K` followed by a letter:
    any other next key sorts after every suffix of `K` already, or
    before all of them. Between, shorter suffixes come first. Within
    each length, the ones the first-unused rule would try come first (so
    between `2` and `2d` it's still `2b`), then, after a digit, those
    starting with `a` of the same length: `2a` between `2` and `2c` when
    `2b` is taken, but `2ba` before `2aa` when `2a` is taken too (rules
    chosen, #229).
  - **Numbers.** `number` is display only (F-BOOK-3), and there's no
    rule for renumbering beyond F-BOOK-5 (numbers should rise) and
    F-BOOK-6 (keys stay), so this is Wolfden's reading. The new chapter
    gets the number of the nearest numbered chapter before it plus 1,
    or, with none before (after an unnumbered prologue), the number of
    the first numbered chapter after it. The numbered chapters after it
    are pushed along only as far as needed: each whose number isn't
    above the one before gets that one plus 1, and the first that
    already is stops the renumbering, so a hand-set gap (1, 2, 3, 10)
    absorbs the insert and the numbers after it stay. Chapters with no
    number, or one F-BOOK-3 ignores, are left alone. A book whose
    chapters have no numbers at all keeps that style: the new chapter
    has none either, and is written as a bare key in a list of bare keys
    (`chapters: [1, 2, 3]`). A book with no chapters gets
    `chapters:` with `- { key: "1", number: 1 }`, as a new book's are
    written.
  - **The file.** The item (`key`, then `number` and `title` when it has
    them) goes in through the lossless list edit (`WithNewListItem`),
    in the list's style and copying the quoting of the item before it,
    and each changed number is a scalar edit in place, so comments and
    every other key are kept. A `book.md` without an id gets one
    (F-ID-6). It's checked as the den reads it, and the result must
    read back as planned (the same keys, the new one in its place, only
    the planned numbers changed), or nothing is written. Blank lines
    between items and after the last one, and comment lines that group
    them (`# Part One`), stay where they were: the new item goes right
    after the one before it. A final book is refused (`frozen`), as any
    edit of its `book.md` is (F-BOOK-1), saying how to unfreeze it in
    the caller's words (`ToolWords.Unfreeze`, #209),
    and so is an `after` the book doesn't list (`unknown_chapter`) and a
    title that isn't one line of at most 200 characters, the limits for
    a name (`bad_argument`).
  - **The API.** `GET /api/book/chapters?book=<key>&universe=<key>`
    gives the book's chapters with the hash of `book.md` and whether
    it's final (the MCP tool `get_chapter`'s result). `POST
    /api/chapter/create` takes `{book, universe, title, after,
    expectedHash}` as `application/json`; `expectedHash` is required, and
    a `book.md` that changed since is 409 `stale_file`. It answers 201
    with the key, position, number, title, index, the chapters
    renumbered (key, from, to), effects in words, warnings, and the file
    written.
  - **The page.** Under the chapter list, **Add chapter**, and with two
    or more chapters **Insert chapter…**; a final book has neither. The
    modal form has **Place** ("At the end, after …", the default for
    Add, then "After …" for every chapter but the last, the first being
    the default for Insert) and **Title**, which is focused for Add
    (Place for Insert); a title over 200 characters is refused before
    anything is sent. A stale file says the chapters changed in another
    program and asks for them again; if the chapter chosen in **Place**
    is no longer one of the choices then, Place goes back to the end and
    the form says so. After an add every cached answer is
    asked again, the form closes, and a notice under the list says what
    it did, including the renumbering, and takes focus until dismissed.
    Escape or Cancel closes the form and returns focus to the button.
- **Chapter fields** (#225). The decisions already recorded apply:
  values and inheritance as types.md §8 says (F-CHAP-5), the editing
  rules of #28 (one field at a time, explicit Save, stale-file
  conflicts), and on #132 that removing the last item of a list leaves
  `[]`, "none here", while going back to inheriting is an explicit
  clear. The rest is v0 behaviour, not a recorded decision.
  - **The API.** `GET /api/book/chapter-fields?book=&universe=` gives a
    book's chapters, each with every chapter field the den's packs
    define (in pack order, then any other name the chapter writes or
    inherits): whether the chapter writes it (`set`), its value as
    written (`own`, in the form of `/api/entity/source`'s values,
    numbers as `{"$plain": "2.50"}`), whether that's `ignored`
    (F-CHAP-3), the value that holds and where from (`chapter`, or the
    `book`, `series`, or `universe` and its key), whether it's `none`,
    what it would inherit without its own (`default`), and the links in
    the value, resolved from the container that holds it (F-CHAP-4). It
    has the den's chapter field definitions (`fieldTypes`, as for a
    note, with `temporal`, `required`, and `default` always off, F-CHAP-1),
    whether each chapter is a bare key, and the hash of `book.md`. `GET
    /api/chapter-defaults?universe=&series=&book=` gives a container's
    `chapter_defaults` the same way, with the books they reach. `POST
    /api/chapter/edit` (`{book, universe, chapter, field, changes,
    expectedHash}`) and `POST /api/chapter-defaults/edit` (`{universe,
    series, book, field, changes, expectedHash}`) change one field: each
    change is `{op, value, index, item}`, `op` one of `set`, `add`,
    `remove`, and `clear`, given the `field`, and they're the MCP
    `update_chapter_fields` and `update_chapter_defaults`, so frozen
    books (with `ToolWords.Unfreeze`), a series' or universe's default
    that would change a final book's chapters, hash checks, the write
    check, effects, and the ledger are theirs. `POST /api/chapter/create`
    takes `fields`, an object from field name to value (a name F-CHAP-1
    reserves is refused, and one given null is left out), written last
    in the new item's `fields`; in a list of bare keys, fields make the
    new chapter a mapping.
  - **Only what changed.** The form is a note field's (#196), and its
    changes are worked out the same way, then given as the chapter
    tools' ops, which name no path inside a field: an item taken out is
    `remove` by its number, a new one `add` where it goes, and one
    changed in place is taken out and added again; any other change (a
    date's sort key, a plain value) sets the whole value. Untouched
    items keep their bytes.
  - **The pages.** A book's chapter list shows each chapter's values
    under its title, inherited ones greyed and marked ("book default").
    As of a story position, a chapter after it shows no fields (nor does
    any while the page doesn't yet know where the position is), and in the
    others a link to a note the reader hasn't met is plain text that
    doesn't say what it reaches, as elsewhere in the reading view (#26);
    the chapter page's fields, which are edited there, show everything.
    A chapter's page has **Fields**: each field, what holds, and
    "inherited from book 'x' defaults" where it's inherited, with
    **Edit** for each field the packs define, and **Use the default**
    for one the chapter writes, which first says what the chapter will
    have then ("Nothing above sets pov, so this chapter will have
    none"). A field the packs don't define, a value the form can't show,
    and a bare-key chapter say why, and are changed in the file. The
    universe's, series', and book's pages have **Chapter defaults**,
    edited the same way, with **Clear**. A final book has no Edit and
    says to unfreeze it; a refusal shows the server's words.
  - **Fields a pack adds** (such as the core `setting`, #231, or a den
    pack's own) get their form from their definition, with nothing in the
    page naming them.
  - **The form.** It starts from the value written here, or, where
    there's none, from the inherited value, so a change to it becomes
    the chapter's own and saving it unchanged sends nothing. A list
    emptied in the form says it will save `[]` and what that replaces;
    where something above sets a value that isn't `[]`, it shows the
    outcome inline with the other choice a click away (#138), `pov:
    ~~Ilse~~ → none in this chapter · use book default (Tarrow) instead`,
    which saves a `clear` instead (no change, for a field the chapter
    only inherited). There's no confirmation step.
    The form keeps the hash and value it was opened on, so a refetch
    while it's open (on window focus, say) can't move it onto a file the
    writer never saw. A stale `book.md` (or container file) is sent again
    on the new hash only as #196 does for a note: every change adds,
    removes, moves, or replaces an item, and the lines the field is written in (the
    chapter's item, or the default's entry; the API gives them as
    `text`) are byte for byte as they were. Otherwise both values are
    shown, and Keep mine sets the writer's value as a whole. **Use the
    default** and **Clear** on a stale file are never sent again: the
    page shows what the file has now to choose from, or says the field
    was already cleared. The page sends a moved item as `move` and a
    changed one as `replace`, which keep the item's comments (#197). A
    removed item's comment goes with it, and the effects say so, as for
    a note's fields; so do they when a client takes an item out and adds
    it, or a new value, again.
  - **Adding a chapter** shows a form for each chapter field, empty, with
    what the chapter will inherit if it stays empty. Only fields given a
    value are sent; an empty list is inherited at create, not `[]`.
- **Chapter headings** (#227). **Decided (maintainer, 2026-10-30):** a
  container key `chapter_heading: [pov, setting, when]`, inherited down
  like `chapter_defaults` (nearest container wins), builds a chapter's
  displayed heading from the listed chapter fields, in order, after an
  explicit `title`; leaving it out changes nothing, and links show the
  linked note's current name, so renames follow (format.md F-CTR-6). The
  rest is v0 behaviour, not a recorded decision.
  - **One builder.** Core's `ChapterHeadings` puts every heading
    together; surfaces differ only in how a link's note is named. The
    index keeps each chapter's parts (its fields' items, links as
    written) on its chapter row and names the links when it's read,
    from the link rows and entity names, which every update rewrites,
    so a rename shows without a rebuild of the chapter rows.
  - **Where it shows.** `/api/hierarchy`'s chapters, `get_chapter` (and
    `/api/book/chapters`), and `update_chapter_fields`' result have
    `heading`. `/api/positions` and the position bar keep titles, which
    are shown whatever the position, as does the sidebar tree, to stay
    short. The book page lists chapters by heading, and the chapter
    page uses it for its title, its tab, and its links to the chapters
    before and after.
  - **As of a position** (#26), `/api/hierarchy?asOf=` builds headings
    from what the reader has met: a chapter after the position shows its
    title alone, and a link to a note the reader hasn't met shows as
    written (its label, or else its target), as the book page shows such
    a link among a chapter's fields (#225). The chapter page keeps
    reading its structure without `asOf`, so a change of position
    doesn't reload it or close a field's form, and asks for headings as
    of the position on the side, showing the title until they come.
  - **Editing.** `chapter_heading` is written in the file for now; the
    Chapter defaults panels don't edit it.
- **Parsing round-trips.** Read-modify-write keeps formatting, ordering, and
  unknown content. This gets heavy test coverage, since it decides whether
  people trust the tool with their notes.
- **Docker comes with hosting (§10).** It isn't needed for local use.

## 10. Phases

| Phase | Delivers |
|---|---|
| **M0: Format** | This doc settled; the format spec written; the Dracula sample written *by hand* in the format. The format can be used with any editor and an AI assistant before any code exists. |
| **M1: Core + CLI** | Parse, index, `wolfden check`, and canon-as-of queries from the command line. Round-trip test suite over the samples. |
| **M2: MCP** | The MCP server. Ships early because it's useful on its own: an AI assistant can work with a den before any UI exists. |
| **M3: Web UI (read)** | Browse the hierarchy, entity pages, search, a story-position "as of" slider, history and diffs. |
| **M4: Web UI (edit)** | Edit fields and sections, create from templates, checkpoints, freeze and unfreeze. |
| **M5: Packs + full sample** | Space opera and fantasy packs, plus the invented multi-book sample with history. |
| **Later** | Import from existing markdown notes; custom calendars; multi-user hosting (accounts, GitHub and Google OAuth, tenants, roles), Docker, collaboration. |

## 11. Sample datasets

These live under `samples/` and are licensed CC-BY-4.0. They're used for docs,
demos, and test fixtures.

- **`dracula`** (small): Bram Stoker's *Dracula* (1897, public domain). It's
  told in dated journals and letters by several narrators, which makes it a
  well-known, checkable test of timelines, POV, and story position vs.
  in-world time. Readers can verify its accuracy for themselves.
- **`starfall`** (full, invented): the universe **Starfall**, with the
  original two-book series *The Long Dark*: *Ember* frozen and *Ash* in
  progress. A relay network connects the settled worlds of the Kessan Reach,
  and one by one its stations are going silent. Lieutenant Ilse Tarrow's ship,
  the *Tessaly*, goes silent too, and she's left with the *Lantern*, an aging
  survey ship, to find out why. A standalone, *A Quiet Moon*, sits beside the
  series. It shows ideas, open threads, a retcon, outline rows continuing
  across books, and packs. It ships with **history**: a script
  (`scripts/starfall-history/replay.mjs`) replays a series of snapshots into a
  fresh git repo, so the versioning features have something real to show.
- **Possible later sample:** the Oz series, whose author contradicted
  himself often across 14 books. It would be a good stress test for
  continuity checks.

## 12. Open questions

1. ~~**Positions on sections:** is `{from="..."}` on headings the right
   syntax, or would HTML comments render more cleanly in other markdown
   tools?~~ **Decided (#3):** an HTML comment on the line under the heading,
   `<!-- wolfden: from=ember:19 until=ember:30 -->`. Attribute braces show up
   as literal text in GitHub, Obsidian, and VS Code and break heading links.
2. ~~**Positions finer than a chapter:** are scenes needed in v0?~~
   **Decided (#4):** no. v0 positions are chapter-level. The syntax
   `ember:14.2` is reserved for scenes, and v0 rejects it with a clear
   "scenes not supported yet" error instead of misreading it.
3. ~~**Entities that span universes** (the same author's recurring
   narrator, say): are they needed, or are copies across universes fine?~~
   **Decided (#5):** not in v0. Crossovers mean one universe, incarnations
   are copies, and links resolve within a universe (§2). Timeless Author and
   den notes are planned (#45), and their folder names are reserved now.
4. ~~**Auto-checkpoint defaults:** an idle-time commit, or manual only by
   default?~~ **Decided (#30, 2026-10-27):** manual by default. The web
   UI shows a quiet "N notes changed since the last checkpoint ·
   Checkpoint…" indicator with a dialog to name one, agents checkpoint
   through MCP, and `auto_checkpoint` in `den.yaml` (format.md F-DEN-9)
   opts in to idle-time checkpoints in `wolfden serve` (§6, §9).
5. **Desktop packaging:** is a localhost web app enough, or wrap it later
   (Tauri, Electron, .NET MAUI)?
6. ~~**Name for the invented sample universe.**~~ **Decided (#35):**
   Starfall, with the series *The Long Dark* (*Ember*, then *Ash*), the
   standalone *A Quiet Moon*, and Lieutenant Ilse Tarrow as the lead (§11).
7. ~~**Undecided and must-check material inside an entity:** how to mark a
   section or field value as an option still being weighed, or as a fact
   that must not be broken?~~ **Decided (#2):** sections and field entries
   carry their own `status`, from one vocabulary shared with entities that
   now includes `undecided` and `candidate`, capped by the entity's status.
   `pin` marks hard canon, served first as **pinned canon**. Positions stay
   `book:chapter`, and `ember:r75` is reserved for outline-row positions,
   which v0 rejects. A bare `ember` means the start of
   the book. A section headed like a change log or session notes
   (built-in patterns plus any in `den.yaml`), or one marked `canon=false`, is
   bookkeeping and stays out of canon views. There's no separate "locked"
   state in v0: writers use `status: canon` plus `pin`. If agents need write
   protection, it belongs to the MCP write tools (M2), not the format.

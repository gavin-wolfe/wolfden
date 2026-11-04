# Using Wolfden

This is the practical guide: how to set up and keep your den (your Wolfden
folder of notes) so it works well, in Wolfden and in the editor you write
in. It grows as features land. The exact rules are in
[`format.md`](format.md), and the reasons behind them are in
[`design.md`](design.md). For choosing whether a note belongs to the
universe, a series, or a book, see [Where to file things](where-to-file.md).
Things that tend to surprise people are collected in [Gotchas](#gotchas).

## Getting started

Set things up in this order. Each step builds on the ones before it.

1. **Build Wolfden.** There's no installer yet, so you build it from a
   clone of the repository. You need the
   [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), and
   [Node.js](https://nodejs.org) 22.22.2 or later on the 22 line, or 24.15 or later on the 24 line, for the web UI. Build the
   web UI first, in the clone's `web/` folder, so the next command
   includes it:

   ```sh
   npm ci
   npm run build
   ```

   Then, from the clone's root, publish the `wolfden` command to a folder
   of your choice:

   ```sh
   dotnet publish src/Wolfden.Cli -c Release -o <folder>
   ```

   Add that folder to your PATH, so `wolfden` works from any folder; the
   steps below assume it does. After updating your clone, build the web
   UI again before you publish, or the old one is copied.
   [mcp-clients.md §1](mcp-clients.md#1-build-wolfden) has more detail.
2. **Try the Dracula sample first (optional).** It's a complete den, so
   it's a safe place to try everything below before you use your own
   notes. Copy `samples/dracula` out of the clone to a folder of its own,
   and turn on its history with `wolfden init <copy> --git`. Inside the
   clone, it's part of Wolfden's own repository, so its checkpoints would
   land there ([mcp-clients.md §2](mcp-clients.md#2-pick-a-den)).

   To see a den partway through a series, try the invented
   [Starfall sample](../samples/starfall/): one book frozen, one in progress,
   a retcon, and things not yet decided. It comes with real history. With
   [Node](https://nodejs.org/) installed, this replays eight checkpoints into a
   new folder outside the clone, which you can then serve:

   ```sh
   node scripts/starfall-history/replay.mjs <new-folder>
   wolfden serve <new-folder>
   ```
3. **Make your den, with history.** History is what lets you keep
   versions, see what changed, and freeze a finished book, so turn it on
   from the start:

   ```sh
   wolfden init my-notes --universe vael --git
   ```

   This makes the folder `my-notes`, with a `den.yaml` and a first
   universe, `vael` (a key: lowercase letters, digits, and hyphens,
   starting with a letter). It makes the folder a git repository, and
   makes a first checkpoint. If git doesn't
   know your name and email yet, or you want this den under another name,
   add `--author-name` and `--author-email`: a pen name is fine, and
   they're saved for this den only. For a den you already have, run `wolfden init --git` in its
   folder instead. Don't put a den with history in Dropbox, OneDrive, or
   iCloud. See [Turning on history](#turning-on-history).
4. **Add your series and books.** In the den's folder, in reading order:

   ```sh
   wolfden new series "The Long Dark"
   wolfden new book "Ember" --in the-long-dark --chapters 24
   wolfden new book "A Quiet Moon" --chapters 18
   ```

   Each makes the folder and its `series.md` or `book.md`, with an id and
   the name, and adds it to the end of the reading order: `books` in the
   series' `series.md`, or `order` in `universe.md`. A series' key comes
   from its whole name, so "The Long Dark" is `the-long-dark`. The first
   book above goes in the series; the second, without `--in`, is a
   standalone book of the universe. Give the number of chapters now: a story
   position, such as "this character enters at chapter 3", only works for
   a chapter the book lists. Once `wolfden serve` is running (step 6),
   **New series** and **New book** on the universe's and series' pages do
   the same. See [Adding series and books](#adding-series-and-books).
5. **Run `wolfden check`.** In the den's folder, it checks every note
   and says what's wrong, file by file. It exits 0 when there are no
   errors (warnings alone pass) and 1 when there are. An error leaves out
   the file it's in, or for a typo in a section comment that section, until
   it's fixed, so start from a clean check. While `wolfden serve` runs, the
   top bar shows the same errors (see [Fixing problems](#fixing-problems)).
6. **Run `wolfden serve`.** It opens the den in your browser, on this
   computer only. From there you can [make notes](#making-a-new-note),
   [edit them](#editing-notes), [add series and
   books](#adding-series-and-books), make
   [checkpoints](#checkpoints), and
   [freeze a finished book](#freezing-a-finished-book). Start with a
   checkpoint of the books you just added. It runs until you press
   Ctrl+C.
7. **Connect an AI assistant (optional).** Wolfden's MCP server lets
   Claude or another assistant read and edit the den through Wolfden's
   own tools, which keep links, ids, and finished books safe. Follow
   [mcp-clients.md](mcp-clients.md), from §3; use full paths there.
8. **Set up your editor (optional).** If you also write in
   [Obsidian](#using-obsidian) or [VS Code](#using-vs-code), open the
   den's top folder (the one with `den.yaml`) in it, and change the
   settings those sections give. Read the
   [gotchas for other editors](#images-and-other-editors) too.
9. **Make it a habit.** Make a [checkpoint](#the-habit-one-checkpoint-per-notes-pass)
   after each notes pass or planning session, with a name that says what
   it was. When a book is published or sent off,
   [freeze it](#freezing-a-finished-book). After a big change in another
   editor, run `wolfden check`.

## Making a new note

In `wolfden serve`, choose **New ▾** in the top bar and pick a type:
character, location, event, and so on, plus any types from
[packs](#adding-a-genre-pack) your den turns on. There are two shortcuts that fill in the place for you:

- **New note ▾** on a universe's, series', or book's page. It lists every
  type, including ones that place has no notes of yet, so a book you've
  just made can have its first character. The note goes in that
  universe, series, or book. A book marked final has no **New note ▾**,
  since it takes nothing new until it's unfrozen.
- **New Character** (or Location, and so on) under a type's list in the
  contents tree. The type and the place are both filled in. A list only
  shows up once that place has a note of that type, so for the first
  one, use **New note ▾** on the page.

The form asks for three things:

- **Type.** What kind of note it is. Its template decides the fields and
  sections the note starts with.
- **Name.** What you'll call it, and what links use (`[[Sister Agatha]]`).
  The file is named from it.
- **Where it goes.** The universe, a series, or a book. It starts as the
  place you're looking at: the book on a book or chapter page, the series
  on a series page, the place a note is filed on that note's page, and
  otherwise the universe. A book
  marked final takes nothing new until it's unfrozen.

Choose **Create**, and Wolfden makes the file from the template and opens
the new note, with the file's name at the top. Fill it in there, one
field or section at a time, as below.

A new note is canon from the start of where it goes. If you're viewing the
story at a chapter, the form also offers **Enters the story at** that
chapter: tick it when the reader first meets this character (or place, or
thread) there, and the note gets `from:` that chapter. Leave it off for
something that's been true all along. It's offered only when the note
goes somewhere that chapter belongs: the universe, the chapter's series,
or its own book. A note filed in another book should enter in that book.

A name is one line, up to 200 characters.

**If the name would take over links.** Links go to the nearest note of a
name, so a second "Jonathan Harker" filed in the universe would take the
links in your universe notes away from the Jonathan in the book. Wolfden
won't do that quietly: it lists the links that would move and makes
nothing. Choose another name or place, or choose **Create anyway** if
those links really should reach the new note (check them afterwards).
From the command line, that's `--allow-relinks`.

From the command line, `wolfden new` does the same:

```text
wolfden new character "Sister Agatha" --in dracula/book:dracula --from dracula:8
```

`--in` takes the same places as `wolfden copy --to`; leave it out to file
the note in the universe, when your den has one. `--from` is optional,
and has to be a chapter (or book) your universe has: a typo is refused
rather than making a note no view shows. Edits are checked the same way
([Editing fields](#editing-fields)).

## Adding a genre pack

Every den has the core types: character, location, faction, item, event,
thread, idea, note, species, and culture. A **pack** adds types for a genre.
Wolfden comes with two, `space-opera` and `epic-fantasy`. Turn one on with a
line in `den.yaml`, or both, if your story has starships and dragons:

```yaml
packs: [space-opera, epic-fantasy]
```

The new types then show up everywhere the core ones do: under **New ▾**, in
`wolfden new ship "Columbia"`, and to AI assistants. Nothing else in your
den changes, and notes you've written stay as they are.

Fields that change over a story, such as `captain`, `location`,
`controlled_by`, or `population`, can have a value for each stretch of it,
so a view at a chapter shows the one true by then. Fields like `kind`,
`population`, and `condition` are plain words of your own, such as `moon`,
`about 40 million`, or `damaged`, rather than a fixed list.

**Ranks, postings, and anything else** a pack leaves out can be added from
your den's own pack
([types.md §4](types.md#4-extensions-and-combining-packs) and
[§10](types.md#10-appendix-a-den-local-pack)).

**If you pick a type and change your mind,** nothing breaks. Change the note's
`type:` line, and its links still lead to it. `wolfden check` then warns that
the file belongs in the new type's folder, and names any field the new type
doesn't have, whose value stays in the note. It also warns about a field with a
value for each stretch of the story, if the new type's field has only one, as a
realm's `part_of` does when the note becomes a location. A link gets a warning
only if the field it's in doesn't accept the new type.

The examples below come from history, astronomy, and old myth, because you
already know them. They show how to file things, not what your own story's
should be.

### What a note is, and where it sits

Two things sound alike and aren't: `kind_of` says what a note **is**, and
`part_of` says where it **sits**.

- **`kind_of` is written once, on a type.** `world` is `kind_of: location`,
  so every world counts as a location. A chapter's `setting` can link to
  one, and asking for notes of type `location` finds it. The packs have
  already set this up, so you only write `kind_of` for
  [types of your own](#your-own-types-and-kind_of).
- **`part_of` is written on a note.** Europa's `part_of` links to Jupiter,
  the planet it circles. Only that one moon says so.

"Europa is a world" is `kind_of`, and "Europa is at Jupiter" is `part_of`. A
type gets none of its parent's fields, so each note keeps its own type,
fields, and folder. A `part_of` can cross types as far as you need, since
every place here is a kind of location, and the worked examples below show
how they nest.

### Choosing a type: beings and peoples

`species` and `culture` are core types. `creature` comes with the epic fantasy
pack.

| You're writing about | Use | Why |
|---|---|---|
| Neanderthals | `species` | A kind of being with a culture of its own |
| Griffins, wolves | `creature` | A kind of being with no culture of its own |
| Bucephalus, Alexander the Great's horse | `character` | One named individual, whose `species` field links to the note on horses |
| The Norse | `culture` | People who share ancestry, language, and customs, with nobody in charge of them all |
| The Hanseatic League | `faction` | A group that acts together, with members and a purpose |

**Species or creature?** Ask whether it has a culture. A dragon that holds
councils and keeps laws is a species. A dragon that eats sheep is a creature.
One named dragon is a character, either way. A creature is a kind of species,
so a field that links to a species takes a creature too. Without the epic
fantasy pack, a species note does for both.

**Culture or faction?** A faction can act: it has leaders, members, and goals.
A culture is something people share, and it has no leaders or members list.
A character can have both, with `heritage` for where they come from and
`factions` for who they answer to. `heritage` and `species` are plain text
that can hold a link to a note: `heritage: "[[norse]]"`.

### Choosing a type: space opera

| Type | Use it for | Fields | Folder |
|---|---|---|---|
| Star System | A star or group of stars | `part_of`, `stars`, `controlled_by` | `star-systems` |
| World | A planet, moon, asteroid, or gas giant: a body in space that people can live on, visit, or build around | `part_of`, `kind`, `population`, `controlled_by` | `worlds` |
| Settlement | A place people live or work, on a world, orbiting it, floating in its atmosphere, or out on its own, linked to its star system: a city, colony, orbital station, or fuel-mining platform | `part_of`, `kind`, `population`, `controlled_by` | `settlements` |
| Polity | A faction that governs territory, at any size | `government`, `leader`, `capital`, `part_of` | `polities` |
| Ship Class | A design that ships are built to | `role`, `builder`, `crew`, `technologies` | `ship-classes` |
| Ship | One ship | `class`, `captain`, `owner`, `location`, `condition` | `ships` |
| Technology | A drive, a weapon, a network: anything with rules the story has to keep | `kind`, `created_by`, `controlled_by`, `introduced` | `technologies` |

**World, settlement, or star system?** Ask what it is in space.

| You're writing about | Use | Its `part_of` |
|---|---|---|
| The Solar System | `star-system` | A core location, if you need one, such as the Milky Way |
| Mars | `world` with `kind: rock planet` | The Solar System |
| Jupiter | `world` with `kind: gas giant` | The Solar System |
| Europa | `world` with `kind: moon` | Jupiter |
| Mir, the space station | `settlement` with `kind: orbital station` | Earth, the world it orbited |
| A module aboard Mir | `location` | Mir |

A world doesn't need anyone living on it. A settlement doesn't need ground under
it. One with no world, out on its own, links to its star system.

**Polity or faction?** A polity is a faction that governs territory, and any
size counts.

| You're writing about | Use | Why |
|---|---|---|
| The Roman Empire | `polity` | Governs territory |
| The Icelandic Commonwealth | `polity` | Governs territory too: one island, with no king |
| The Praetorian Guard | `faction`, `part_of` the Roman Empire | Acts as a group, and governs nothing of its own |
| The British East India Company | Either | A trading company that came to rule territory |

The last one shows the line blurring. Pick either, since a polity is a kind of
faction, so a character's `factions` and a place's `controlled_by` take both,
and the note's page lists its members either way. The places a polity holds
name it in `controlled_by`.

**A ship is a location that moves.** A ship is a kind of location, so a
chapter's `setting` can link to one, and a core location, such as its brig, can
name it in `part_of`. Where the ship is at the moment goes in its own `location`
field, not `part_of`, since it changes. What's true of every ship of a design
goes on the ship class, and what sets one ship apart goes on the ship:

```yaml
---
type: ship
name: Columbia
status: canon
fields:
  class: "[[apollo-command-module]]"
  location:
    - { value: "[[earth]]", from: "book-1:1" }
    - { value: "[[the-moon]]", from: "book-1:9" }
  condition: in service
---
```

**A worked example.** Each line's `part_of` links to the line it's indented
under:

```text
Milky Way          location     kind: galaxy
  Solar System     star-system
    Earth          world        kind: rock planet
      The Moon     world        kind: moon
      Mir          settlement   kind: orbital station
    Mars           world        kind: rock planet
    Jupiter        world        kind: gas giant
      Europa       world        kind: moon
```

The *Columbia* isn't in the tree, since it's a ship, and its `location` changes
as the story goes on. A polity wouldn't be either, since it isn't a place.

A link that isn't a place or a group, such as a `setting` that links to a
character, still gets a warning from `wolfden check`. Asking for notes of type
`location`, in a search or from your AI assistant, finds your worlds and ships
too. Asking for `world` finds worlds, and your own `moon` type if it has
`kind_of: world`, but no other locations.

The full list is in [types.md §11](types.md#11-the-space-opera-pack).

### Choosing a type: epic fantasy

| Type | Use it for | For example | Fields | Folder |
|---|---|---|---|---|
| Realm | A land defined by who rules it, or a plane of existence | The Kingdom of France | `part_of`, `kind`, `controlled_by`, `seat`, `borders`, `population` | `realms` |
| Magic System | A kind of magic with rules the story has to keep | Alchemy | `kind`, `source`, `practiced_by` | `magic-systems` |
| Creature | A kind of being with no culture of its own | Griffins | `kind`, `habitat`, `diet`, `danger`, `lifespan`, `population` | `creatures` |
| Deity | One named god or other worshipped power | Zeus | `gender`, `pronouns`, `species`, `factions`, `domains`, `worshippers`, `condition` | `deities` |
| Artifact | An object of power with rules of its own | Excalibur | `kind`, `owner`, `location`, `created_by`, `powered_by`, `condition` | `artifacts` |
| Order | A group bound by a creed and ranks | The Knights Templar | `kind`, `leader`, `base`, `part_of`, `patron` | `orders` |

**Realm, location, or faction?** Ask what's left if the rulers fall.

| You're writing about | Use | How it connects |
|---|---|---|
| Europe | `location` with `kind: continent` | Sits above realms, whose `part_of` links to it |
| The Kingdom of France | `realm` with `kind: kingdom` | `part_of` Europe |
| Paris | `location` with `kind: city` | `part_of` the Kingdom of France |
| The House of Valois, then the House of Bourbon | `faction` | The realm names each in `controlled_by`, for its stretch of the story |

A plane of existence is a realm too, with `kind: plane`. Rule can change while
the land carries on, so the land and its rulers are two notes. A realm's
`controlled_by` has a value for each stretch of the story, and so does its
`seat`:

```yaml
---
type: realm
name: Kingdom of France
status: canon
fields:
  part_of: "[[europe]]"
  kind: kingdom
  controlled_by:
    - { value: "[[house-of-valois]]", from: "book-1:1" }
    - { value: "[[house-of-bourbon]]", from: "book-2:14" }
  seat:
    - { value: "[[paris]]", from: "book-1:1" }
    - { value: "[[versailles]]", from: "book-3:2" }
---
```

A realm is a place, so a chapter's `setting` can link to one. It isn't a group,
so a character's `factions` or a castle's `controlled_by` that links to a realm
gets a warning from `wolfden check`. Link to the faction that rules it instead.

**Order or faction?** An order is bound by a creed and ranks, as the Knights
Templar were. A family, a court, or a warband, with no vows to keep, is a
faction, as the House of Valois is. An order is a kind of faction, so a
character's `factions` can link to one.

**Artifact or item?** An artifact is an object of power, with powers and a
price the story has to keep to, such as Excalibur. An ordinary object that
matters, such as a stolen letter, is a core item. An artifact is a kind of item.

**Gods.** One named god is a deity. A kind of god or divine being is a species,
and a pantheon is a faction.

| You're writing about | Use | How it connects |
|---|---|---|
| Zeus | `deity` | Its `factions` links to the Olympians |
| The Olympians | `faction` | The pantheon |
| The Titans, the valkyries | `species` | A deity's `species` field can link to one |
| The ancient Greeks | `culture` | A deity's `worshippers` can link to one |
| Odysseus | `character` | A mortal, however much the gods take an interest |

A deity is a kind of character, so it can lead a faction or be a chapter's
`pov`. Being a kind of character passes on no fields, so a deity has its own
`gender`, `pronouns`, `species`, and `factions`, and a `Want` section, which work
as a character's do. For anything else a character has, such as `Voice`, extend
the type from your den's own pack.

**A worked example.** Each line's `part_of` links to the line it's indented
under:

```text
Europe                 location   kind: continent
  Kingdom of France    realm      kind: kingdom
    Paris              location   kind: city
      The Latin Quarter location  kind: district
```

The House of Valois isn't in the tree, since it's a faction and not a place. The
realm names it in `controlled_by`.

**A realm isn't a polity.** If you use both packs, keep them apart: a realm is
the land, and is a kind of location, while a polity is the faction that governs
it. A realm's `controlled_by` can link to a polity.

The full list is in [types.md §12](types.md#12-the-epic-fantasy-pack).

### Your own types and `kind_of`

If your den has its own pack
([types.md §10](types.md#10-appendix-a-den-local-pack)), a type can say
what it's a kind of, with `kind_of` in its file. Then any field that links
to that other type takes it too:

```yaml
# packs/starfall/types/moon.yaml
name: Moon
plural: moons
kind_of: world
```

A moon is now accepted wherever a world is, and, since a world is a kind
of location, wherever a location is, such as a chapter's `setting`. A type
can have several parents, so a ship that's also a person can be both a
chapter's `setting` and its `pov`:

```yaml
# packs/starfall/types/sentient-ship.yaml
name: Sentient Ship
plural: sentient-ships
kind_of: [ship, character]
```

- **It's only about what links accept and what filters find.** Asking for
  `world` finds your moons too. The type gets none of its parents'
  fields, sections, or template. Give it the ones it needs, or leave them
  out.
- **You can add a parent to a type you don't own**, in your pack's
  `extensions/` folder. To let a species be a chapter's `pov`, say,
  `extensions/species.yaml` holds `kind_of: character`.
- **You don't need a note of the parent.** A `vehicle` type that no note
  uses is fine, if you only want a field that takes anything that's a
  vehicle (`to: vehicle`).
- **Mistakes get a warning.** `wolfden check` warns about a parent that
  isn't a type in your den, or one that makes a loop, and ignores it.

The rules are in [types.md F-TYPE-13](types.md#3-type-definitions).

## Adding series and books

In `wolfden serve`, a universe's page has **New series** and **New
book**, and a series' page has **New book**. A book made from the
universe's page is a standalone book: one that isn't in a series. The form
asks for:

- **Name.** The series' or book's name. Its folder is named from it: "The
  Long Dark" is `series/the-long-dark/`. That folder name is its *key*, which
  story positions use (`ember:3`), so it never changes, but you can
  change the name in its `series.md` or `book.md` later.
- **Place after…** Where it comes in reading order: at the end, at the
  start (a prequel), or after a book or series you choose. For a book in a
  series, that's among the series' books; otherwise among the universe's
  series and standalone books. A work placed before a final book is
  allowed (the frozen book's own files don't change), but it changes what
  the canon views at that book's chapters include, since the reader now
  meets the new work first.
- **Number of chapters** (books only, optional). 24 gives chapters 1 to
  24, so positions such as `ember:3` work straight away. Leave it empty
  for a book with no chapters yet. You can add more later
  ([Adding chapters](#adding-chapters)); a prologue, or a chapter's
  title, goes in the book's `book.md` with **Edit file** for now.

Choose **Create**, and Wolfden makes the folder and its file, adds the
new work to the reading order (the series' `books`, or the universe's
`order`), and opens its page, saying what it did. A list that wasn't
there before, because there was only one book, is written with that book
first. The rest of the file is kept as it was.

Wolfden refuses a name whose key another book or series already has,
since a story position names a book by its key alone: two books called
"Ember" in one universe, even in different series, would make `ember:3`
mean nothing. Choose another name, then change the name in `book.md`
afterwards if you want it back. A name that starts with a digit
("1984") can't make a key, since keys start with a letter: start it with
a word, and change it afterwards the same way. Keys use only the letters
a–z and the digits 0–9 for now, so a name written entirely in another
script ("Дракула", "東京") makes no key: give it a name in Latin letters,
such as a transliteration, then change the name in its file back to the
one you want; the key stays.

From the command line:

```text
wolfden new series "The Harker Papers" --after dracula
wolfden new book "Mina's Journal" --in the-harker-papers --chapters 12
wolfden new book "Dracula's Guest" --in dracula
```

`--in` names the universe, or a series as `<universe>/<series>` (or just
the series' key, in a den with one universe). `--after` names the work it
comes after, and `--first` puts it at the start instead; leave both out
for the end. `--chapters` is the number of chapters.

## Adding chapters

A story position such as `ember:3` only works for a chapter the book
lists, so add chapters as you draft them. On a book's page, **Add
chapter** adds one at the end, and **Insert chapter…** adds one after a
chapter you pick. The form asks for an optional **Title**; leave it empty
for none. Wolfden writes it into the `chapters` list in the book's
`book.md`, keeping the rest of the file as it was, and says what it did.

- **At the end**, the new chapter's key is the next whole number: after
  chapters 1 to 24, it's `25`.
- **Inserted after a chapter**, it gets that chapter's key with a letter
  added: after `2` it's `2b`. After `2b`, it's `2ba`. After a word key
  such as `prologue`, it's `prologuea`.
- **Inserted keys read in order where they can.** When the next
  chapter's key is the same key with letters added, the new key takes a
  letter that sorts between the two: inserting between `2` and `2b`
  gives `2a`, and between `2b` and `2bb` gives `2ba`. When no letter fits
  (between `2` and `2a`), it takes the first unused one instead: `2b`, or
  `2c` if `2b` is taken. That reads out of order but works the same: the
  list in `book.md`, not the keys, is the chapter order. Inserting after the newest chapter
  each time (`2b`, then after it `2ba`, then `2baa`) always reads in
  order.
- **Keys are for keeps; numbers move.** The key is what positions use,
  so it never changes, even when the chapter is no longer second. What
  changes is the number shown: inserting `2b` after chapter 2 makes it
  chapter 3 on the page, and the chapters after it move up one, as far
  as needed. `ember:3` still names the chapter it named before. A
  chapter with no number, such as a prologue, keeps none, and in a book
  whose chapters have no numbers, the new one has none either.
- **Before an epilogue.** Add chapter always goes at the very end, after
  an unnumbered epilogue too. To add a chapter before the epilogue, use
  **Insert chapter…** after the last numbered chapter (`--after` its key
  from the command line).

A final book has neither button: unfreeze it first. From the command
line:

```text
wolfden new chapter --in ember
wolfden new chapter "Signal Fires" --in ember --after 2
```

`--in` is the book's key (`<universe>/<book>` if two universes have a
book by that key), and `--after` the chapter it comes after; leave it
out for the end. The title is optional.

The web form also has the chapter's **Chapter fields**, such as its POV
and when it takes place (see the next section). They're optional: a
field you leave empty is inherited from the chapter defaults, and the
form says what that will be.

## Chapter fields: POV, when, and the rest

A chapter can say who tells it, and when and where it happens. The core
fields are `pov` (the point-of-view characters), `narration` (first
person, third limited, and so on), `tense`, `when` (the in-world date),
and `setting` (the places it happens, as links to location notes). Your
den's packs can add more; the pages show whatever chapter fields your
packs define. In `book.md` they go in a `fields` mapping on the
chapter's item:

```yaml
chapters:
  - { key: "1", number: 1, fields: { pov: "[[ilse-tarrow]]", setting: "[[kessan-reach]]" } }
```

**Set it once, change it where it differs.** A value that holds for
most chapters goes in the **Chapter defaults** (`chapter_defaults`) of
the book, its series, or the universe, each on its own page. A chapter
that differs sets its own value, which wins: a space opera might set
`when` to the story's present in the series' defaults, and give a
flashback chapter its own date and POV. The nearest wins: the chapter,
then its book, then its series, then the universe. The rules are in
[types.md §8](types.md#8-chapter-fields).

- **On a book's page,** each chapter in the list shows its fields under
  its title. A value the chapter inherits is greyed and marked where it
  comes from, such as "book default".
- **On a chapter's page,** **Fields** lists every chapter field, with
  what the chapter has and where that comes from. **Edit** opens the
  same controls as a note's fields: a link picker for `pov` and
  `setting`, a dropdown for `narration` and `tense`, a date (with an
  optional sort key) for `when`. If the chapter inherits the field, the
  form starts from the inherited value; what you save becomes the
  chapter's own.
- **"None here" and "use the default" are different.** Removing the last
  POV and saving writes `pov: []` (in the file, `[]` for any list field
  such as `pov` or `setting`): this chapter has none, even though the
  book has a default (an omniscient prologue, say, or a found document).
  When something above sets the field, the form shows what saving will
  do before you save, with the other choice beside it:
  `pov: ~~[[ilse-tarrow]]~~ → none in this chapter · use book default
  ([[tarrow]]) instead`. Click it, and saving takes `pov` out of the
  chapter, so it inherits; click again to go back to none. To have a
  chapter inherit the default again without editing it, use **Use the
  default**, which says first what the chapter will have.
- **Inherited values are greyed** on the chapter's page too, and marked
  where they come from ("inherited from book 'ember' defaults").
- **Chapter defaults** are edited the same way on the universe's,
  series', and book's pages. **Clear** takes a default out, so the
  chapters inherit from further up again.
- **A value that's ignored is still shown.** If the file holds a value
  the field can't take, such as `setting: Whitby harbour` where
  `setting` takes links to locations, the page shows it as written and
  why it's ignored ("written here as "Whitby harbour", which is ignored:
  setting is a link to one entity, …"), and the chapter has the default
  meanwhile. **Edit** says so again before you replace it; **Use the
  default** (or **Clear**, for chapter defaults) takes it out instead. An
  item of a list that doesn't fit, such as `Borgo Pass` in `setting:
  ["[[castle-dracula]]", Borgo Pass]`, is shown as left out, with why,
  and the rest of the list holds.

A final book's chapters and defaults can't be edited until it's
unfrozen. A series' or universe's default that would change what a
final book's chapters have is refused too, with the books named; set
the value in the draft books' own defaults instead. A chapter written
in `book.md` as a bare key (`chapters: [1, 2, 3]`) has no place for
fields: write it as `{ key: 1 }` first.

AI assistants working through Wolfden read and change the same fields
with `get_chapter`, `update_chapter_fields`, and
`update_chapter_defaults` ([`mcp-clients.md`](mcp-clients.md)).

If your den's own pack already defines a chapter field named `setting`,
yours is used and `wolfden check` warns that it replaces the core one.
Rename yours, or delete it to use the core field, whichever you prefer.
If you wrote `setting:` as plain text without defining it (say,
`setting: Kessan Reach`), `wolfden check` now warns about it and ignores
the value, since the core field takes links to location notes. Make it a
link to a location note (`setting: "[[kessan-reach]]"`), or define a
text `setting` in your den's pack to keep it as it is. Until then the
chapter's page, and an AI assistant's `get_chapter`, show the text as
written and why it's ignored, so it isn't replaced unseen.

### Chapter headings from fields

Many books head each chapter with whose chapter it is, or where and when
it happens. Add `chapter_heading` to `universe.md`, `series.md`, or
`book.md`, listing the chapter fields to show, and Wolfden builds each
chapter's heading from them, after its title if it has one. Leave it out
and nothing changes.

A series told by a rotating cast, where each chapter is one character's,
can head chapters with the POV alone. In `series.md`:

```yaml
chapter_heading: [pov]
```

and in `book.md`, with a label to keep the heading short:

```yaml
chapters:
  - { key: "1", number: 1, fields: { pov: ["[[tomas-reyne|Reyne]]"] } }
  - { key: "2", number: 2, fields: { pov: ["[[petra-varga|Varga]]"] } }
```

Chapter 1 is headed "Reyne" and chapter 2 "Varga". A space opera can
show the POV, the place, and the date, and set the place once for the
whole series with `chapter_defaults`:

```yaml
# series.md
chapter_heading: [pov, setting, when]
chapter_defaults: { setting: "[[vesta-yards]]" }
```

```yaml
# book.md
chapters:
  - { key: "3", number: 3, fields: { pov: ["[[ines-ardent|Ardent]]"], when: "2350-04-12" } }
  - { key: "4", number: 4, title: Dead Reckoning, fields: { pov: ["[[ines-ardent|Ardent]]", "[[kofi-mbeki]]"], setting: ["[[halcyon-relay]]"] } }
```

Chapter 3 is headed "Ardent — Vesta Yards, 2350-04-12", and chapter 4
"Dead Reckoning · Ardent & Kofi Mbeki — Halcyon Relay".

- **Where it shows.** A book's page lists each chapter by its heading,
  and a chapter's page uses it as its title, as do its links to the
  chapters before and after it. AI assistants get it from `get_chapter`
  as `heading`.
- **Names follow renames.** A link shows its label if it has one, and
  otherwise the note's name as it is now, so renaming the Vesta Yards
  note changes every heading that links it.
- **Missing values are skipped.** A chapter with no value for a listed
  field, or "none here" (`[]`), leaves that field out. A chapter with no
  title and nothing to show is listed as "Chapter 5", as before.
- **The nearest list wins, whole.** A book's `chapter_heading` replaces
  its series' list rather than adding to it. `chapter_heading: []` turns
  headings from fields off for that book (or series), even if the
  universe sets one.
- **Reading as of a chapter.** A chapter after the reading position
  shows only its title. Wolfden never supplies the name of a note the
  reader hasn't met yet: a link to one shows as you wrote it, its label
  if it has one, or else its target.
- **Tip: label a link to a note the reader meets later.** A POV
  character who stays a mystery for a while reads better as
  `[[halden-pike|the stranger]]`, which shows "the stranger" at every
  position. Without a label, a link by key shows the key
  (`halden-pike`), and a link by id shows the raw id.
- **Dates.** A `when` written as a mapping shows its `text`. With no
  text, it shows its sort key as a date, such as `2350-04-12`, leaving
  out a year of `0` (`05-03`), the way to sort dates with no year.
- `chapter_heading: pov`, one name on its own, is the same as
  `[pov]`. `wolfden check` warns about a `chapter_heading` that is
  neither, a repeated entry, or a name that isn't a chapter field (a
  typo such as `pvo`).

For now you write `chapter_heading` in the file itself, in your editor;
the pages don't have a form for it yet. The rules are in
[format.md F-CTR-6](format.md#61-common-rules).

## Editing notes

`wolfden serve` lets you edit a note on its page, one piece at a time.

- **In place.** Each section has **Edit**, **Rename**, and **Remove**,
  and the opening (the text before the first section) has **Edit
  opening**. Edit opens the text as Markdown, as it's written in the
  file. Type `[[` and a few letters of a note's name or alias to pick a
  note to link to. The link is the text you matched, so it reads well:
  `[[Mina Harker]]`, or `[[Madam Mina]]` if you typed her alias. When
  that text could mean another note too, it uses her name instead, or
  failing that her file name (`[[mina-harker]]`). A note that no link
  is sure to reach says to add an id with **Edit file** first. Use
  **Preview** to see it as the page shows it. **Save** (or Ctrl+Enter,
  ⌘+Enter on a Mac) saves; **Cancel** (or Escape) leaves it as it was.
  **Add image…** under the editor adds a picture to the note, and so
  does dropping or pasting one into the text
  ([Adding an image while you edit](#adding-an-image-while-you-edit)).
  **Add section** adds one at the end or before another. Renaming a
  section changes the links to it in every note; removing one asks first.
- **Edit file** opens the whole note, frontmatter and all, for anything
  the other controls don't do, such as a section's
  `<!-- wolfden: ... -->` comment.
- **While you edit, you see everything.** The page shows the whole note,
  whatever the story position, and says "Editing shows everything". The
  position view comes back when you save or cancel.
- **If something else changed the note meanwhile,** such as an AI
  assistant working through Wolfden, or you in another editor, the save
  is refused and you see both versions. Choose **Keep mine** (save yours
  over it), **Take theirs** (drop yours), or **Copy mine and reload**
  (copy yours, then edit the note as it is now).
- **Saving isn't a checkpoint.** It saves the file; it doesn't record a
  version in the den's history. Make a checkpoint as you do now.
- **Changes from outside show up by themselves.** When an AI assistant
  or another editor changes the den, an open page shows it within about
  5 seconds, longer on a very large den. While an editor or form is open
  the page waits, so your text never changes under you; it catches up
  when you save or cancel.

Notes in a book marked final can't be edited until the book is
unfrozen; their Edit controls are off, and the page says why. See
[Freezing a finished book](#freezing-a-finished-book).

### Editing fields

Each field on a note's page has its own **Edit**. The form fits the
field: a dropdown for a field with set choices, yes or no, a number, a
date, or a link, where you type a few letters of a note's name and pick
it. A link finds notes by their aliases and file names too, a word at a
time and ignoring case and accents: `madam` offers "Mina Harker, as
*Madam Mina*", and picking it links `[[mina-harker]]`. It offers every
note of the field's types, wherever you are in the story, since editing
shows everything. **Add field** offers the fields the note's type has that the note
doesn't set yet, and **Clear field** takes one out.

A fact that changes over the story is a list of dated values. Each has
a **From** and an **Until**, picked by book and chapter: "this fact
holds from chapter 3 until chapter 9". Leave From empty for the start
of the story, and Until empty for "until the next value". **Add a dated
entry** gives a field a new value from a later chapter; the one before
it holds until then. A fact that doesn't change can still have a
**Known from**, for a reveal the reader only learns later.

A typed From or Until has to be a chapter (or book) your universe has.
A typo, such as `dracula:999`, is refused rather than saved, since the
fact would drop out of every story-position view. So is one in a field
that holds positions, such as a thread's Planted, and so is a value
there that isn't a position at all, including a list or a mapping typed
in with **Edit file** (`paid_off: {a: 1}`). The same goes for a
`from=` or `until=` you type in a section comment with **Edit file**,
and for an AI assistant's edits. A typo that's already in the note, from
an edit outside Wolfden, doesn't stop you saving other changes as long
as it stays where it is (moving it out of a section comment, or renaming
the field that holds it, counts as typing it anew); `wolfden check` lists
it.

Any value can have its own status, such as `candidate` for an option
you haven't settled, and can be **Pinned** as hard canon.

A list field, such as a character's `occupation`, has **Add item**,
**Remove**, and buttons to move an item up or down. Saving changes only
the items you touched. A comment you wrote in the file goes with its
item when you reorder or change it: `- "[[mina-harker]]"  # main` keeps
its `# main`, and a comment line right above an item moves with it.
Comment lines above the first item head the whole list, so they stay at
the top, and blank lines stay where they are. Dated entries keep the
order they're written in, so they have no move buttons. Removing an item removes the comment on
it too, and the notice after saving says so. The same goes for chapter
fields and chapter defaults.

A field your note's type doesn't define, or one written in a way the
form can't show, has **Edit in file** instead, which says why.

## Images

Notes can show pictures: a map, a sketch of a ship, a family tree. Keep
the image file in your den, and point to it from the note. `wolfden serve`
shows it on the note's page.

### Where to put them

- **Next to the note, or in a folder beside it.** A map of the castle
  sits in the same folder as the castle's note, or in an `images/` folder
  there:

  ```text
  universes/dracula/entities/locations/
    castle-dracula.md
    images/
      castle-map.png
  ```

  The image travels with the note. If you move the folder, the picture
  still shows, because the path from the note to the image hasn't changed.
- **In one shared folder for images used everywhere.** A den-level
  `images/` folder (next to `den.yaml`) suits a world map or a crest that
  many notes show.

Wolfden shows `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.avif`, and `.svg`
files. It never shows anything in a folder or file whose name starts with
a dot (such as `.obsidian/` or `.git/`), and nothing outside the den.

### Adding an image while you edit

When you edit a note's opening, a section, or the whole file in
`wolfden serve`, you can add a picture without leaving the page:

- Click **Add image…** under the editor and pick one or more files,
  drag an image from your computer onto the text, or paste one (a
  screenshot, say) into it. Pasting text copied from Word, Excel, or
  OneNote pastes the text, even though they copy a picture of it too.
  Wait for one batch of images to finish before adding the next.
  **Save** and **Cancel** (and Ctrl+Enter and Escape) wait too, so the
  picture isn't left out of the text.
- Wolfden saves a copy in an `images/` folder beside the note, and puts
  `![alt text](images/name.png)` in the text where your cursor is (or
  where you dropped it). Adding `Castle Map.PNG` to
  `universes/dracula/entities/locations/castle-dracula.md` saves
  `universes/dracula/entities/locations/images/castle-map.png` and
  writes `![Castle Map](images/castle-map.png)`. If the note's folder
  already has an `Images/` folder (in any mix of capitals), Wolfden uses
  that one, spelled as it is. (On Linux, where `images/` and `Images/`
  can sit side by side, it uses `images/`.)
- The alt text comes from the file's name. Change it to say what the
  picture shows: it's what shows if the picture can't, and what screen
  readers say.
- The file name is made simple: lowercase, with `-` for spaces and
  other characters, so the path needs no `%20`. If the folder already
  has a file by that name, the new one gets a number (`castle-map-2.png`)
  and the page says so. Wolfden never replaces an image that's there.
- It takes `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.avif`, and `.svg`
  files of up to 10 MB, and checks that the file really is the kind of
  image its name says. A photo that's too big needs making smaller in an
  image editor first.
- The image is saved at once, but the note isn't: **Save** the note to
  keep the line that shows it. If you cancel instead, the image stays in
  the `images/` folder; delete it there if you don't want it.
- Like saving, adding an image doesn't make a checkpoint. The next
  checkpoint records it with your notes.
- A note in a book marked final can't get new images until the book is
  unfrozen.

To put an image somewhere else, such as the den's shared `images/`
folder, copy the file there yourself and write the path by hand (next
section).

### How to point to them

Write the image the usual Markdown way, `![what it shows](path)`. The text
in the brackets is what shows if the picture can't (and what screen
readers say):

- **From the note's folder:** `![Map of the castle](images/castle-map.png)`.
- **Up a folder:** `![Coach](../images/coach.jpg)`. You can go up, but
  not above the den's own folder.
- **From the den's top folder:** start with `/`, as in
  `![World map](/images/world-map.png)`. This works from any note, so it's
  the easy choice for the shared folder.

A space in a file name is written `%20`
(`![Map](images/castle%20map.png)`), or put the path in angle brackets:
`![Map](<images/castle map.png>)`. Simplest of all is a name without
spaces, such as `castle-map.png`. Avoid `%` and `#` in image file names:
`#` ends the path, and a `%` has to be written `%25` (`100%25.png` for
`100%.png`).

If the picture can't be found, the page shows "Image not found" with the
text from the brackets, so you can see which path to fix. Check the
spelling, and that the path starts from the note's folder.

When Wolfden moves a note to another scope (or an AI assistant does,
through Wolfden), the note's images stay where they are and their paths
aren't changed. Move the images too, or use a `/` path for an image whose
note may move.

Images from websites, such as `![Harbour](https://example.com/harbour.jpg)`,
don't show by default, so opening a note doesn't tell the website about
it. To allow them, add `remote_images: true` to `den.yaml`
([format.md F-DEN-8](format.md)).

### Using Obsidian

You can keep using [Obsidian](https://obsidian.md) to write in your den.

1. **Open the den's top folder as the vault,** the one with `den.yaml`
   in it. Then Obsidian and Wolfden agree on where everything is. Use
   paths relative to the note (the next setting does this for you),
   rather than ones starting with `/`.
2. In **Settings → Files and links**:
   - Set **New link format** to **Relative path to note**. Obsidian then
     writes image paths from the note's folder, as Wolfden reads them.
   - Set **Default location for new attachments** to **Same folder as
     current file**, or to **In subfolder under current folder** (and name
     the subfolder, such as `images`). A picture you paste or drag in then
     lands next to its note.

When a path doesn't lead to the file, Obsidian looks for an image with
that file name anywhere in the vault, so an image can show in Obsidian
and still be "not found" in Wolfden. Make sure the path points at the file
itself.

One thing to watch: Wolfden's links between notes use double brackets,
`[[Mina Harker]]`, as Obsidian's do, but an Obsidian image embed,
`![[castle-map.png]]`, isn't a Wolfden image in this version: Wolfden shows
it as plain text ([format.md F-LINK-2](format.md)). If you keep
Obsidian's **Use [[Wikilinks]]** setting on, so your note links stay
`[[...]]`, Obsidian will write pasted images as `![[...]]` too. Change
those to the `![what it shows](path)` form, such as
`![Map of the castle](images/castle-map.png)`, so they show in Wolfden.

### Using VS Code

VS Code's Markdown preview reads image paths the same way Wolfden does:
from the note's folder, and with `/` from the top of the folder you
opened. Open the den's top folder in VS Code, and a path that shows in its
preview shows in Wolfden too.

## Checkpoints

A **checkpoint** is a saved version of the whole den that you can come
back to. Wolfden saves your edits to the files as you make them, but a
checkpoint is what puts them in the den's history: each note's History
tab lists the checkpoints that changed it, and shows what changed between
them. Saving never makes a checkpoint by itself, so you decide when a
version is worth keeping.

Checkpoints need history turned on. Without it your notes still work,
and the top bar of `wolfden serve` says **No history · Turn on…**.

### Turning on history

Wolfden keeps the history with git, in the den's folder, but you don't
need to know git to use it. In `wolfden serve`, choose **No history ·
Turn on…** in the top bar, then **Turn on history**. Or, from the command
line, in the den's folder:

```text
wolfden init --git
```

Either way, Wolfden makes the den's folder a git repository, adds
`.wolfden/` (its index, which it rebuilds from your notes) to the folder's
`.gitignore`, and makes a first checkpoint, "Start of history", of the den
as it is. Untick **Make a first checkpoint** (or add `--no-checkpoint`) to
start with none. The page picks it up straight away: no restart.

Git records a name and email with each checkpoint. If it doesn't have
them yet, Wolfden asks: fields in the dialog, or `--author-name` and
`--author-email` on the command line. On the command line you can also
give them when git has its own, to keep this den under a pen name:

```text
wolfden init --git --author-name "Ash Calloway" --author-email ash@example.com
```

Any name will do, a pen name included, and the email can be a
placeholder. They're saved for this den only, before the first
checkpoint, never for the rest of your computer. If git already has a
name and email, nothing is asked, and what you give wins for this den.
Give both: a name alone is refused when git has a different name and an
email, since the email would be recorded beside your pen name. (If your git settings give a name only inside
certain folders, the dialog can't tell until the den is a repository: it
shows the fields, but you can leave them empty, and Wolfden asks again,
for just what's missing, only if they're really needed.)

If git signs your commits, the key it uses may belong to another name, and
Wolfden can't always tell. So when you give a name or email, signing stays
off for this den, even if they're what git already has: Wolfden says so,
and a signature can't tie your pen name to the other key. To sign with a key
of this name's, run `git config --local user.signingkey <key>` and `git
config --local commit.gpgsign true` in the den's folder. A `local-user` line
in your `gpg.conf` adds that key's signature as well, so check for one first.

A pen name given this way has limits worth knowing:

- **It's saved in this copy of the den's repository only.** Git doesn't copy
  a repository's settings when it's cloned, so a clone on another computer
  starts with that computer's global name and email, and its checkpoints
  are recorded under them and signed with its key. Before the first one
  there, run the commands below in the clone.
- **An environment variable still beats it.** If `GIT_AUTHOR_NAME` or one of
  its relatives is set when `wolfden serve` or a later checkpoint runs, git
  uses that, as it does in any repository.
- **It can't be added this way once there are checkpoints, or in a bigger
  repository.** Wolfden refuses, and says the name wasn't applied. With
  checkpoints, the commands below change it from then on; earlier
  checkpoints keep the name they have.

To put a repository of its own under a name by hand, run these in its folder.
The `author` and `committer` settings beat `user` if git has them, and the
last four keep git's own key from signing under that name:

```text
git config --local user.name <name>
git config --local user.email <email>
git config --local author.name <name>
git config --local author.email <email>
git config --local committer.name <name>
git config --local committer.email <email>
git config --local commit.gpgsign false
git config --local tag.gpgsign false
git config --local tag.forcesignannotated false
git config --local push.gpgsign false
```

A few things to know:

- **Not in a synced folder.** Don't turn on history for a den in Dropbox,
  OneDrive, or iCloud: two machines syncing while git writes can corrupt
  it. Use a private remote for a backup instead.
- **A den that's part of a bigger repository** already has history there,
  so there's nothing to turn on, and Wolfden says so.
- **If a step fails** (a git hook refuses the first checkpoint, say),
  Wolfden says what it did and what it didn't. History is on by then, so
  make the first checkpoint from the dialog once the problem is fixed, or
  run `wolfden init --git` again: it picks up where it stopped.
- **A new den** can start with history: `wolfden init my-notes --git`.
- **For scripts:** `wolfden init` exits 0 when its main job is done. For
  a new den that job is making the den, so a new den inside a larger
  repository, or on a computer without git, still exits 0. For an
  existing den the job is turning on history, so the same situations
  exit 2. Either way, it exits 2 if git needs a name and email that
  weren't given, or a step fails; a new den is kept.

### The habit: one checkpoint per notes pass

The rhythm that works best is a checkpoint at each natural stopping
point:

- **After each chapter's notes pass.** Once you've drafted a chapter,
  go over the notes it touched (characters, threads, the outline), then
  make one checkpoint named for that pass, such as "Notes pass after
  drafting ch 12".
- **After a planning session.** When a session settles new decisions,
  checkpoint them under a name that says what was decided, such as
  "Planning session: harbour subplot".

**Name every checkpoint.** The name is how you'll find this version
later, in a long list of them, so say what the pass was rather than
"edits" or the date (the date is recorded anyway). Wolfden never names a
checkpoint for you, unless you turn on automatic checkpoints (below).

### Making one

In `wolfden serve`, the top bar says when notes have changed since the
last checkpoint: "3 notes changed since the last checkpoint ·
Checkpoint…". It stays hidden when nothing has changed. Click it to see
the changes, grouped by universe and by book, so you can look the pass
over; notes saved on the page are marked "saved in Wolfden", and the
others changed some other way, in your editor or by an AI assistant.
Give the checkpoint a name, and choose **Checkpoint**. If git refuses
(a hook, say), the dialog shows why, and nothing is recorded.

### AI assistants make them too

An AI assistant working through Wolfden's MCP server (see
[mcp-clients.md](mcp-clients.md)) can make checkpoints with the
`checkpoint` tool, so a pass you do together ends the same way: the
assistant updates the notes, then makes one checkpoint with a
descriptive name. It records the assistant as a co-author, and tells you
about any notes in it that changed outside its session, such as edits
you made in your editor meanwhile. Ask it to checkpoint at the end of the
pass, not after every edit.

### Automatic checkpoints

If you'd rather not think about it, `wolfden serve` can make checkpoints
by itself. Add a line to `den.yaml`:

```yaml
auto_checkpoint: 30m
```

Then, while `wolfden serve` runs, once the den has gone 30 minutes
without a change, it makes a checkpoint named for what changed
("Auto-checkpoint: edited 3 notes"). Any time from `5m` to `24h` works,
such as `2h` or `1h30m`. Remove the line, or set it to `false`, to turn
it off. Automatic checkpoints don't replace named ones: their names say
only what changed, not why, so keep naming your passes and planning
sessions. See [format.md F-DEN-9](format.md) for the exact rule.

It waits while git is busy with something of yours, such as a rebase or
a merge, and makes the checkpoint once that's done. If your git signs
commits (with GPG, say), keep the passphrase cached (`gpg-agent` does
this), or an automatic checkpoint waits on the passphrase prompt. Saving
still works while it waits.

## Freezing a finished book

When a book is done (published, or sent off), **freeze** it. A frozen
book's notes, chapters, and outline can't be edited, by you in Wolfden or
by an AI assistant, so what the book says stays as it was published. It
replaces keeping a `_FINAL` copy of your notes.

In `wolfden serve`, open the book's page and choose **Freeze…**. Wolfden
says what will happen and asks first. Or, from the command line:

```text
wolfden freeze dracula
```

(add `--universe <key>` if your den has more than one universe).

Freezing does three things, in order:

1. It marks the book `status: final` in its `book.md`. That's the freeze
   itself.
2. If the den has history, it makes a checkpoint of the whole den, so
   anything you hadn't checkpointed yet goes in too. Before you
   confirm, Wolfden says how many other changed files that is.
3. It tags that checkpoint `book/<universe>/<book>/final`, such as
   `book/dracula/dracula/final`: a snapshot you can always come back to,
   because Wolfden never moves or deletes a tag.

Without history, step 1 is the whole freeze, and Wolfden says no
snapshot was taken.

**Fix any bad section comments first.** Wolfden won't freeze a book while a
typo in a section comment hides text in it, since a frozen book can't be fixed
without unfreezing. It names each one.

**To change a frozen book,** unfreeze it: **Unfreeze…** on its page, or
`wolfden unfreeze dracula`. Its `book.md` then says `status: draft`, and
with history Wolfden makes a checkpoint, so the history shows when you
reopened it. When you freeze it again, the new snapshot gets the next
tag, `final-2`, then `final-3`, and so on, and the earlier tags stay, so
each version you froze is kept.

**If git refuses** the checkpoint or the tag (a hook, say), the book is
still frozen, and Wolfden says what went wrong. Fix that, then freeze the
book again: the page offers **Finish freezing…**, and `wolfden freeze`
does the same. It makes only the checkpoint and the tag that are
missing. The same goes for a book you marked `status: final` by hand:
freezing it takes a snapshot of the book as it is now.

AI assistants can't freeze or unfreeze a book. If one tries to edit a
frozen book, it's told the book is frozen and to ask you whether to
unfreeze it. When Wolfden refuses your own edit for the same reason, on
the page or with `wolfden new` or `wolfden copy`, it says how to
unfreeze the book: **Unfreeze…** on its page, or `wolfden unfreeze`.

## Fixing problems

When something in the den has an error, the top bar says so: **⚠ 3 problems**.
It counts errors, not warnings, and updates by itself within a few seconds when
a file changes outside Wolfden. It clears when they're fixed, and shows nothing
when there are none. Warnings stay with `wolfden check`. That includes two notes
with the same id: they drop out of views, but it's a warning, so it isn't listed
here.

Click it for the list. Each entry has the note or file, the line, the message,
and what's left out because of it: one section, the whole note, or a whole book,
series, or universe. **Edit file** opens the file as text, even one that's left
out and has no page. Fix it and save.

- **One error at a time is fine.** A file that's already left out can be saved
  with errors still in it, as long as it doesn't have more than before. The
  result says how many are left. Fixing frontmatter that doesn't parse can show
  errors it was hiding. The save that brings the file back is checked like any
  other, so a position that doesn't resolve, or a new typo in a section comment,
  is refused then.
- **A note the den reads** keeps the usual rules: a save can't add an error.
- **If the file changed** since you opened it, you see both versions and choose.
- **A final book stays frozen.** Unfreeze it on its page first. A broken `book.md`
  that says it's final can't be unfrozen, so fix that one in your own editor.
  The same goes for a `book.md` whose frontmatter doesn't parse and has the word
  `final` or a backslash in it, since it might be final. Any other `book.md` that
  doesn't parse can be fixed here, and a save has to leave it parsing.
- **Above a final book,** a series or universe can be fixed, but while a book below
  says final or can't be read, its `chapter_defaults`, `chapter_heading`, `books`,
  and `order` can't change, since those decide what the book inherits, how it's
  headed, and what a reader has met by its chapters. Removing a repeat from a list
  is fine. If one of those lists isn't a list, or the frontmatter doesn't parse, use
  your own editor. A series or universe that loads also keeps the rest of its
  frontmatter while a final book that loads is below it.
- **A file can't be saved empty** here, since that can't be undone without history.
- **The id stays.** An id can't be changed here, and a file with none gets one
  once it's fixed.
- **If `den.yaml` doesn't load,** nothing shows but the list, and Edit file opens
  `den.yaml`, unless it's gone, when you restore it yourself. Once it loads, Edit file shows it but won't change it, since a change
  there can change how every file reads. On a server pinned with `--universe`,
  fix it in your own editor, as it's shared by every universe.
- **A pack's file, or a folder missing its `book.md` or `series.md`,** is listed,
  and you fix it outside Wolfden.
- **The story position doesn't change the list.** Like editing, it's for you, so
  it names sections even at an early chapter. Pages at a position stay
  spoiler-safe: a note left out says only that it's left out, with **Show
  everything** to see why.
- **A file another program holds locked can't be read,** so it's listed as that,
  with no **Edit file**, since this isn't about what's in it. Close it there,
  and the entry clears within a minute. Any errors inside it can't be seen until
  then, in the list or in `wolfden check`. A held `book.md`, `series.md`, or
  `universe.md` is also listed by its folder, saying not to add a file, since it's
  there. A held pack is also listed under `den.yaml` in the same way.
- **While such a file is held,** a fix can be refused if its save had to read that
  file: `den.yaml`, a pack, a book's, series', or universe's own file, or an
  outline. Another note being held doesn't stop a fix. Close the file named, and
  save again. Your text stays in the editor. If that file is above the one you
  opened, the message names it there too, rather than the file seeming to have an
  error: close it, then reload the page.
- **With `--universe`,** the list has that universe's errors and the den-level
  ones, as `wolfden check --universe` does.

AI assistants hear about it too: see [the MCP guide](mcp-clients.md).

## Gotchas

Things that surprise people, with what happens, why, and what to do.
Each links to the section with the details.

### Setting up

- **`wolfden serve` says it was built without its web UI.** The web UI
  is built with npm, separately, and `dotnet publish` (or `dotnet
  build`) includes it only if it was built first. Run `npm ci` and `npm
  run build` in the clone's `web/` folder, then publish again and
  restart `serve` ([Getting started](#getting-started), step 1).
- **Anyone using this computer can open the page.** `wolfden serve`
  listens on this computer only, never the network, but this version has
  no accounts, so anyone else signed in to the same computer can open it
  while it runs. Stop it with Ctrl+C when you're done.
- **A new den has no books.** `wolfden init` makes the den and, with
  `--universe`, a first universe. Add series and books with `wolfden new
  series` and `wolfden new book`, or **New series** and **New book** in
  the web UI ([Getting started](#getting-started), step 4).

### History and git

Git is the tool Wolfden uses to keep your den's history. A *repository*
is a folder whose history git keeps, and a *commit* is one saved version
in it, which Wolfden calls a checkpoint.

- **Saving isn't a checkpoint.** Every save goes to the file at once,
  but only a checkpoint puts it in the den's history. The top bar of
  `wolfden serve` says how many notes have changed since the last one.
  Make one after each notes pass ([Checkpoints](#checkpoints)).
- **Automatic checkpoints are off by default.** Turn them on with
  `auto_checkpoint: 30m` in `den.yaml`. Only `wolfden serve` makes them,
  while it runs. One takes in edits an AI assistant saved and hasn't
  checkpointed yet, without the assistant's name on them, so ask the
  assistant to checkpoint at the end of its pass
  ([Automatic checkpoints](#automatic-checkpoints)).
- **A checkpoint records the whole den.** It takes in every changed
  note: ones you changed in your editor, and ones in other universes when
  `serve --universe` shows only one. Freezing and unfreezing make one
  too. The checkpoint dialog lists the changes before you confirm (with
  `--universe`, it counts the other universes' changes rather than
  naming them), and
  the freeze prompt counts them.
- **Your git name and email are for this den only.** Git records a name
  and email on every checkpoint. Wolfden asks only when git has none,
  and saves what you give in this den's repository, never for the rest
  of your computer. A pen name is fine, and the email can be a
  placeholder. What you give wins over what git has for this den, and
  turns signing off here, since git's key may be another name's. A clone doesn't
  bring it along, and an environment variable such as `GIT_AUTHOR_NAME` still
  beats it
  ([Turning on history](#turning-on-history)).
- **Not in a synced folder.** Git and Dropbox, OneDrive, or iCloud
  together can corrupt the history when two computers sync while git is
  writing. Use one or the other: git with a private remote for a backup,
  or a synced folder without history.
- **A den inside a larger repository uses that repository.** There's no
  history to turn on: checkpoints are commits in the larger repository,
  of the den's files only, and anything else you've staged there (marked
  for your next commit) stays as it was. Freeze tags belong to the whole repository, so two dens in
  it with the same universe and book keys share the tag numbers. That's
  why you try the Dracula sample in a copy outside Wolfden's own clone.
- **`wolfden init --git` exit codes differ for new and existing dens.**
  A new den inside a repository, or on a computer without git, exits 0:
  the den is made, without a repository of its own. The same situations
  exit 2 for an existing den, where turning on history is the job.
  Either way, a missing name and email, or a step that fails, exits 2,
  and a new den is kept. Read what it prints.
- **Automatic checkpoints wait for git.** While you're in the middle of
  a git rebase or merge (ways of combining two lines of history), they
  wait until you've finished. If git signs
  your commits, keep the passphrase cached (`gpg-agent` does this), or
  they wait on the passphrase prompt.

### Editing

- **Two editors on one note.** If the note changed since you opened it
  (in Obsidian, say, or by an AI assistant), Wolfden refuses the save
  and shows both versions, so nothing is lost. Choose which to keep
  ([Editing notes](#editing-notes)). An assistant's edit is refused the
  same way, and it reads the note again.
- **Wolfdens take turns writing.** `wolfden serve`, an AI assistant,
  and a command such as `wolfden new` can all run on one den. Each save
  waits while another is being made, so none is lost; a command that
  has to wait says so. Freezing or unfreezing a book holds the others
  up until its checkpoint is made, so if git is waiting for your signing
  passphrase, other saves wait too. If one waits more than 30 seconds, it gives up
  and saves nothing, so try again. The turns are kept with
  `.wolfden/write.lock`, which can stay there and is safe to leave;
  editors such as Obsidian don't take turns, so a save there is caught
  by the check above instead.
- **A note with an error in its frontmatter drops out of the den.** Such a
  file is left out of Wolfden until it's fixed: its links, every view, and the
  tree. An error in the frontmatter of a `book.md` leaves out the whole book.
  The top bar says **⚠ 1 problem** within a few seconds, and its list opens the
  file to fix (see [Fixing problems](#fixing-problems)). A link to the note says
  it's left out and names the line. `wolfden check` lists the same errors.
- **A typo in a section comment hides that section.** With `status=maybe`,
  `from=dracula:5.2`, or `pin=yes` under a heading, Wolfden doesn't guess what
  you meant, since a reveal showing early would be worse. That section and
  its subsections are left out of every story view, pinned canon, and what
  assistants get at a position. The rest of the note, or the book, is fine.
  An earlier section of the same name, such as an older `## Appearance`, keeps
  applying until the typo is fixed. Nothing is lost: in Everything mode the
  section is in its place with a flag naming the line, and it can be searched
  there. `wolfden check` still fails. Wolfden lets you keep editing the note,
  but won't save a new typo, and a book can't be frozen until it's fixed.
- **A comment that doesn't close is only a warning, and it does more harm
  than a typo.** With a missing `-->`, the comment is ignored and everything up
  to the next `-->` in the file, or to the end of the file, is swallowed with
  it. That text, later headings included, stops being sections and becomes raw
  text in the section the comment is under, with no position of its own. On
  the page, that section's heading shows at every position where the note
  does, and the text after it, plus any later sections it swallowed, disappears.
  An assistant reading through the tools gets the raw text, so a reveal in it
  can reach an assistant at an early position. `wolfden check` still passes, so
  read its warnings: this one names the line where the swallowed text ends.
- **Copying a note's file by hand makes a duplicate.** Both files have
  the same `id`, so Wolfden leaves both out of views, and links to them
  break, until one gets a new id. Copy with `wolfden copy`, or ask an AI
  assistant, either of which gives the copy a new id. To fix a hand-made
  copy, replace its `id` with one from `wolfden new-id`.
- **Notes written by hand need an id.** One without an `id` still works,
  with a warning, but links can't reach it by id. `wolfden check --fix`
  adds the missing ids.
- **Chapter keys are for keeps.** Positions name a chapter by its key
  (`dracula:5`), not its number. **Insert chapter…** (or `wolfden new
  chapter --after`) gives a new chapter a new key, such as `2b`, and
  renumbers the chapters after it, so existing positions still point
  where they did ([Adding chapters](#adding-chapters), [format.md
  §6.4](format.md#64-bookmd)). If you edit `book.md` by hand, do the
  same: never change a key to match a new number.
- **Hidden files and folder links are ignored.** Anything whose name
  starts with a dot (`.obsidian/`, `.trash/`) isn't part of the den, so
  a note moved to `.trash/` is gone from Wolfden. Folder links (symbolic
  links and junctions) aren't followed; `wolfden check` warns about
  them.
- **`.wolfden/` is only an index.** Wolfden keeps its index there and
  rebuilds it from your notes, so never edit it, and nothing is lost if
  it's deleted. Checkpoints leave it out, and turning on history adds it
  to `.gitignore`.

### Links

- **A new note can take over links.** Links go to the nearest note of a
  name, so a new note named like an existing one can take its links.
  Wolfden refuses and lists the links that would move. Choose another
  name or place, or **Create anyway** (`--allow-relinks`) if they should
  move ([Making a new note](#making-a-new-note)).
- **Renaming a note by hand doesn't fix its links.** Changing `name:` in
  **Edit file** or in your editor leaves the links that used the old
  name broken. The web UI can't rename a note yet. An AI assistant
  working through Wolfden can, and rewrites the links, or keeps the old
  name as an alias.
- **Moving a note's file can change where links go.** Since the nearest
  note of a name wins, a file moved to another book, or up to the
  universe, can take links from another note, or lose its own. An AI
  assistant's move rewrites links so they reach what they did. After
  moving files yourself, run `wolfden check`.
- **Links don't cross universes.** A link reaches only notes in its own
  universe. `wolfden check` says when a broken link matches a note in
  another one.

### Books and freezing

- **A frozen book refuses edits, but only in Wolfden.** Wolfden and AI
  assistants working through it can't change a frozen book. Your own
  editor can, and Wolfden can't stop it. With history, the book's page
  then offers **Finish freezing…**, which takes a new snapshot that
  includes the change. If the change was a mistake, undo it first (the
  History tab of the note you changed, or `git diff`, shows what it
  was)
  ([Freezing a finished book](#freezing-a-finished-book)).
- **Freeze tags never move.** Each freeze keeps its own snapshot, so
  freezing a book again adds `final-2`, then `final-3`, and the earlier
  tags stay.
- **AI assistants can't freeze or unfreeze.** Only you can, on the
  book's page or with `wolfden freeze` and `wolfden unfreeze`. An
  assistant that tries to edit a frozen book should ask you.
- **Spoiler checks are off by default.** `wolfden check` flags a link to
  something the reader hasn't met yet only with `spoiler_checks: true`
  in `den.yaml` ([format.md F-DEN-7](format.md)). It sees links, not
  prose.

### Files another program has open

- **A note another program holds can stop a rename.** Wolfden can't read
  a file while another program has it open for writing, even if that
  program lets others read it too. While one does, Wolfden can't see
  what's in that note, so it refuses a rename, a move, a copy, a new note,
  a new book, or a freeze, says which file, and changes nothing. Otherwise
  links in that note would break without a word. Close the file there,
  then try again.
- **`wolfden check` and the top bar's problems list it as an error** (F-GEN-5),
  and links to that note can show as broken until it's closed. If no program has it open,
  check the file's permissions. The same goes for a folder Wolfden isn't
  allowed to look in.
- **Pages still open.** A note that can't be read is left out of pages and
  searches until it can be, so it can seem to have gone. Nothing happened
  to it. A program that saves a file and lets go doesn't cause any of this,
  and neither does one that has it open only to read and shares it.

### Images and other editors

- **Image paths start from the note's folder,** or from the den's top
  folder if they start with `/`. Not from wherever the image happens to
  be ([Images](#images)).
- **Moving a note doesn't move its images.** Its image paths stay as
  they were, so move the images too, or use a `/` path.
- **An image added in the editor stays even if you cancel.** Add
  image… saves the file at once; cancelling the edit leaves it in the
  note's `images/` folder, unused. Delete it there if you don't want it.
- **Website images don't show.** An image from a website is blocked
  unless `den.yaml` has `remote_images: true`, so opening a note
  doesn't tell the website.
- **Obsidian's `![[image]]` embeds show as text.** Wolfden shows only
  `![what it shows](path)` images. Obsidian writes the embed form when
  **Use [[Wikilinks]]** is on, so change pasted images to the Markdown
  form ([Using Obsidian](#using-obsidian), #189).
- **An image Obsidian shows can be "not found" in Wolfden.** When a path
  is wrong, Obsidian finds an image by its file name anywhere in the
  vault. Wolfden follows the path only.
- **Obsidian's Properties panel can't show `fields`.** It shows a note's
  nested `fields:` as raw text marked unsupported, and a position such
  as `from: dracula:1` as a web link. Editing there may rewrite them. In
  Obsidian's **Settings → Editor**, set **Properties in document** to
  **Source**, so the frontmatter is plain text you edit yourself (#73).

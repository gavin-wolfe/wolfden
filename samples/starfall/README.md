# Starfall sample den

An invented space opera, written for Wolfden, with chapter-level outlines and
notes and no prose. It shows what a den looks like partway through a series:
one book finished and frozen, one still being written, and things not yet
decided. It follows [`docs/format.md`](../../docs/format.md), the core and
space opera packs ([`docs/types.md`](../../docs/types.md)), format 1.

The story is a placeholder with tropes and small plot holes, and it's there to
give the features something to work on. Relay stations in the Kessan Reach are
going silent. Lieutenant Ilse Tarrow's ship, the *Tessaly*, goes silent too,
and she's left with the *Lantern*, an aging survey ship, to find out why.

[`samples/dracula`](../dracula/) is the place to go for a checkable public
domain text. This one is for what Dracula can't show: a series, a retcon, and
real version history.

## What's here

```text
den.yaml
packs/starfall/            a den-local pack: gives characters rank and posting
universes/starfall/
  universe.md
  entities/                the Reach, its worlds and stations, the Compact,
                           the Velari, the relays, the plague note
  series/long-dark/
    series.md              defaults for every chapter of the series
    entities/              Ilse, Pike, Reyne, the ships, the threads
    books/ember/           final: 30 chapters, outline rows 1 to 32
    books/ash/             draft: 30 chapters listed, 12 drafted, rows 33 to 63
  books/a-quiet-moon/      a small standalone, set decades earlier
```

Start with `books/ember/book.md`, then Ilse's note,
`series/long-dark/entities/characters/ilse-tarrow.md`.

## What to look at

- **Spoiler views.** The Velari are extinct as of `ember:14` and alive from
  `ember:27`:

  ```sh
  wolfden query --den samples/starfall --at ember:14 --entity Velari
  wolfden query --den samples/starfall --at ember:27 --entity Velari
  ```

- **Facts that change over time.** Ilse's `rank` (Commander from `ember:22`),
  the *Tessaly*'s `condition`, and the *Lantern*'s `captain`. Try them at
  `ember:5`, `ember:8`, and `ash:2`.
- **A retcon.** `universes/starfall/entities/notes/velari-plague.md` once said
  a plague killed the Velari. *Ash* changes that, and the old version is kept
  in history. *Ember* is frozen and still reads correctly, since nobody in it
  knows better. One outline row in *Ash* is marked `retconned`.
- **Things not decided.** Who the traitor is: three `candidate` notes in
  `books/ash/entities/ideas/`, under an `undecided` Payoff in
  `who-buried-the-records.md`. What happened to the crews: two `idea` notes.
  What the logs were cut off from saying: an open thread with no candidate yet.
- **Rows across books.** *Ash*'s outline starts at row 33, right after *Ember*
  ends.
- **Kinds of things.** Chapters are set aboard the *Lantern* (a ship) and at
  Kessan Station (a settlement), and Ilse's `factions` names the Kessan Compact
  (a polity). A ship and a settlement are kinds of location, and a polity is a
  kind of faction, so none of them needs anything extra (types.md F-TYPE-13).
- **How places nest.** The moon Tamber and the fuel platform Ostrel Skim both
  name the gas giant Ostrel in their `part_of`, and Ostrel names the star system
  Serrin, which names the Kessan Reach.
- **A placeholder name.** Admiral Sorrel Vance has `placeholder: true`.

## History

The den in this folder has no history of its own, since it's part of Wolfden's
repository. To get a copy with its history, replay it into a new folder outside
the clone. You need [Node](https://nodejs.org/) and git:

```sh
node scripts/starfall-history/replay.mjs <new-folder>
wolfden serve <new-folder>
```

That makes a git repository with eight checkpoints, ending in exactly the files
here. It needs a new or empty folder, and it refuses anything else. It uses no
network, and it doesn't touch Wolfden's own repository.

1. Start the Starfall notes.
2. Plan *The Long Dark* and outline *Ember*.
3. *Ember*, chapters 1 to 14. This adds an idea the next step of planning drops.
4. *Ember*, chapters 15 to 30.
5. Freeze *Ember*, with the tag `book/starfall/ember/final`.
6. Plan *Ash*, and delete the dropped idea.
7. **Retcon: there was no plague.** A checkpoint of its own.
8. *Ash* to chapter 12, and *A Quiet Moon*.

Open the plague note's History tab to see the retcon, or Ember's page to see it
frozen. The snapshots are in
[`scripts/starfall-history/`](../../scripts/starfall-history/), and the script's
header says how they're stored.

## Choices made in this den

- **Rank needs a pack.** `rank` isn't a core field, so the den-local pack in
  `packs/starfall/` adds it, as in types.md §10. That's why `den.yaml` lists
  `starfall` beside `space-opera`.
- **Status.** Everything the story has settled is `canon`. Options still being
  weighed are `candidate` or `idea`, and the sections that hold them are
  `undecided`.
- **Spoiler-safe.** `spoiler_checks` is on. Facts are placed where the reader
  learns them, using `from=`, and summaries that run ahead are split into
  subsections.
- **`wolfden check` is clean.** It finds no errors and no warnings, so there are
  no intentional spoiler warnings to explain. Each checkpoint of the replay is
  clean too.

## License

The text in this den (summaries, notes, and outline) is by Gavin Wolfe,
licensed under [CC BY 4.0](../LICENSE). The story and names are invented.

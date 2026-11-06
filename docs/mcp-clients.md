# Connecting Claude and other MCP clients

Wolfden has a built-in [MCP](https://modelcontextprotocol.io) server, so
an AI assistant can read your den and edit it through Wolfden's own tools
instead of guessing from pasted files. This page shows how to add it to
Claude Code, Claude Desktop, and other MCP clients, and gives example
prompts to try against the [Dracula sample](../samples/dracula/).

How the server behaves is described in [design.md §8](design.md#8-ai-integration-mcp),
and the `wolfden mcp` command in [§7](design.md#the-command-line-v0).

## 1. Build Wolfden

There's no installer or published package yet, so you build Wolfden from
source. You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
From the root of a clone, publish the command-line tool to a folder of
your choice:

```sh
dotnet publish src/Wolfden.Cli -c Release -o <folder>
```

The folder then holds `wolfden.exe` on Windows, or `wolfden` on macOS and
Linux. It needs the .NET 10 runtime and the ASP.NET Core 10 runtime; the
SDK includes both. Check that it runs:

```sh
<folder>/wolfden mcp --help
```

You can also run the server straight from the clone, with the command
`dotnet` and the arguments
`run --project <clone>/src/Wolfden.Cli -- mcp --den <den>`, where
`<clone>` is the clone's full path (a client doesn't start the server in
the clone). But that builds the project each time the client starts it,
which is slow. A published folder is better for everyday use.

## 2. Pick a den

The server works on one den, named with `--den <folder>`. If you leave
`--den` out, it uses the folder the client starts it in, or the nearest
folder above it with a `den.yaml`. MCP clients don't always say which
folder that is, so give `--den` with a full path.

To try the write tools, **copy the Dracula sample out of the clone first**,
to a new folder that doesn't exist yet, and turn on its history. From the clone's
root (in PowerShell, use `Copy-Item -Recurse` for `cp -r`):

```sh
cp -r samples/dracula <copy>
<folder>/wolfden init <copy> --git
```

`init --git` makes the copy a git repository and a first checkpoint. If
git doesn't have your name and email yet, or you want this den under
another name, add `--author-name` and `--author-email`; they're saved for
that den only
([using-wolfden.md](using-wolfden.md#turning-on-history)).

Inside the clone, the sample is part of Wolfden's own repository, so
`checkpoint` would commit to Wolfden and `get_history` would show
Wolfden's commits. In a copy with its own repository, history is the
den's. A checkpoint is a commit made with the git identity configured for
that repository. A den without git still works; `get_history` and
`checkpoint` then say history isn't available.

`samples/dracula` in the clone is fine for the read tools. The server
keeps its index in the den's `.wolfden/` folder, which the sample's
`.gitignore` already leaves out.

## 3. Add the server to your client

Every client needs the same two things: a command, the full path of
`wolfden` (or `wolfden.exe`), and its arguments, `mcp --den <den>`. The
server speaks MCP over stdio. It needs no network access, keys, or
environment variables. Use full paths in client settings, and don't count
on `~` or `%USERPROFILE%` being expanded there.

The examples below use `C:\Wolfden\bin\wolfden.exe` and
`C:\Wolfden\dracula` on Windows, and `/opt/wolfden/wolfden` and
`/opt/dens/dracula` on macOS and Linux. Replace them with your own
paths.

### Claude Code

Add it with `claude mcp add`. Everything after `--` is the command and its
arguments, so the server's own options don't get read as Claude Code's.
On Windows, in PowerShell or the Command Prompt:

```powershell
claude mcp add wolfden -- C:\Wolfden\bin\wolfden.exe mcp --den C:\Wolfden\dracula
```

On macOS and Linux:

```sh
claude mcp add wolfden -- /opt/wolfden/wolfden mcp --den /opt/dens/dracula
```

`--scope` (before the name) says where the setting is kept:

- `local`, the default: only you, only in the current project.
- `user`: you, in every project.
- `project`: everyone who works in the project, through a `.mcp.json`
  file at its root that you can commit. Claude Code asks each person to
  approve a project's servers before it starts them.

```sh
claude mcp add --scope user wolfden -- /opt/wolfden/wolfden mcp --den /opt/dens/dracula
```

You can also write `.mcp.json` by hand:

```json
{
  "mcpServers": {
    "wolfden": {
      "command": "C:\\Wolfden\\bin\\wolfden.exe",
      "args": ["mcp", "--den", "C:\\Wolfden\\dracula"]
    }
  }
}
```

A committed `.mcp.json` only works for people who have Wolfden and the den
at the same paths, so for a den you share, `local` or `user` scope is
usually the better fit.

`claude mcp list` shows the servers and whether they connect, and `/mcp`
inside a session shows Wolfden's status and tools.

### Claude Desktop

Open **Settings → Developer → Edit Config**, which opens (and creates, if
needed) `claude_desktop_config.json`:

- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`

Add Wolfden under `mcpServers`, keeping any servers already there:

```json
{
  "mcpServers": {
    "wolfden": {
      "command": "C:\\Wolfden\\bin\\wolfden.exe",
      "args": ["mcp", "--den", "C:\\Wolfden\\dracula"]
    }
  }
}
```

On macOS:

```json
{
  "mcpServers": {
    "wolfden": {
      "command": "/opt/wolfden/wolfden",
      "args": ["mcp", "--den", "/opt/dens/dracula"]
    }
  }
}
```

In JSON, a backslash in a Windows path is written twice. Save the file,
then quit Claude Desktop completely and start it again. If Wolfden doesn't
appear among the connectors, Claude Desktop's logs say why: the server's
own messages are in `mcp-server-wolfden.log`, in `%APPDATA%\Claude\logs`
on Windows or `~/Library/Logs/Claude` on macOS.

### Other MCP clients

Any client that can start a local (stdio) MCP server can use Wolfden.
Give it the command `wolfden` (full path) and the arguments
`mcp --den <den>`, plus any of these:

| Option | What it does |
|---|---|
| `--universe <key>` | Pins the session to one universe. Every call reads it, and a call that names another universe, or `*`, is refused. Useful in a den with several universes. |
| `--no-watch` | Doesn't watch the files. Each call still checks them first and rebuilds the index if they changed. |

Standard output carries only the protocol, and log messages go to
standard error. The server handles both the current MCP handshake and the
older `initialize` one. It exits when the client disconnects.

You can run more than one client against the same den. Only one Wolfden
watches a den's files at a time; the others check the files before each
call instead.

## 4. The tools

The server's own instructions and each tool's description tell the
assistant how to use them, so you don't need to name tools in your
prompts. This list is for knowing what the assistant can do.

**Read tools** change nothing:

| Tool | What it does |
|---|---|
| `search` | Full-text search over names, aliases, leads, section text, and field values (a link in a field matches its note's name and aliases too); `field` searches one field's values. A hit in a field has `field` (its name) and `line` (the entry's line), with no `section`, such as `{"field": "occupation", "line": 10, "snippet": "[physician], professor", …}`. A query's words match anywhere in any order; words in double quotes (`"silver key"`) match as a phrase, side by side and in order. A hit in a section a bad comment leaves out has `left_out: true` and `in_canon: false`, so `canon_only` drops it. With `as_of`, it searches only what the reader knows at that position, as the Reader's view does: see [Read by position](#7-gotchas-for-ai-assistants). |
| `get_entity` | One entity, by name, alias, file name, id, key, or path; with `as_of`, only what the reader knows at that position, with the names the reader knows it by in `aliases`. Without `as_of`, a section a bad comment leaves out comes in its place with `left_out` (each error's `line`, `rule`, and `message`). With `as_of` it isn't there at all, and nothing says so, since a heading can give away a reveal. With `as_of`, a fact's `until` is left out too, and so are a position field's values later than `as_of` and the writer's HTML comments. |
| `list_entities` | Entities by type, status, series, or book. A type includes its kinds here and in `search` and `canon_as_of`: `location` also finds a den's worlds ([types.md F-TYPE-13](types.md#3-type-definitions)), and each result says its own type. With `as_of`, only the entities in canon at that position: met by then, and draft or canon. `total` counts only those. |
| `canon_as_of` | Canon at a story position (or the end of the story), pinned canon first. Sections a bad comment leaves out aren't in it, at any position. At a position, a fact's `until` is left out, and so are a position field's values later than the position and the writer's HTML comments. |
| `get_chapter` | A book's chapters and their fields, with each value's source (the chapter, or `chapter_defaults` above it), and each chapter's `heading`: its title, then the fields a `chapter_heading` lists, such as `"Ardent — Vesta Yards, 2350-04-12"`. A field the chapter writes with a value that's ignored is listed in the chapter's `ignored`, as written and why, such as `{"field": "setting", "as_written": "Whitby harbour", "reason": "setting is a link to one entity, …"}`; a list item that's left out has its number in `item`. A field written with no value (null) isn't a fault, so it isn't listed. |
| `get_chapter_defaults` | A universe's, series', or book's `chapter_defaults`, and the books they reach. A default written with a value that's ignored has `ignored: true`, the value in `written`, and why in `reason`. |
| `check` | The continuity check, as `wolfden check` runs it. |
| `get_history` | The checkpoints that changed an entity's file, and optionally a diff. |

**Write tools** change the den's files:

| Tool | What it does |
|---|---|
| `create_entity` | A new entity from its type's template. If its name would take links from another entity, it's refused unless `allow_relinks`. |
| `update_fields` | Set, add to, or remove anything in an entity's frontmatter (its fields, `status`, `from`), add aliases and `known_as` entries, set a `known_as` entry's `from` (`known_as.0.from`), and move a list item (`move`), keeping its comments. |
| `append_section` | Add text to a section, or add a new section. |
| `add_idea` | Record undecided material with `status: idea`, as its own entity or as a section of one. A new idea entity whose name would take links from another entity is refused unless `allow_relinks`. An idea added to an existing `entity` creates nothing, so it refuses `allow_relinks`, as it does the new entity's `name`, `type`, `series`, `book`, and `fields`. |
| `rename_section` | Rename a section and rewrite the links to it, den-wide. |
| `rename_entity` | Rename an entity and rewrite the links that used the old name, or keep the old name as an alias. |
| `change_alias` | Change or remove an alias, or a `known_as` name, rewriting its links the same way. With `from` and no `new_alias`, it only moves a name between `aliases` and `known_as`, or changes its `from`, and rewrites no link. |
| `copy_entity` | Copy an entity under a new name and id, beside it or into another scope or universe. |
| `move_entity` | Move an entity's file to another scope of its universe (a series, a book, or the universe itself), keeping canon views as they were. Links to it that would stop reaching it, and its own links that would change target, are rewritten to ids. |
| `update_chapter_fields` | Change one chapter's own fields: set, add, remove, or clear, and move or replace one list item in place (`move`, `replace`), keeping its comments. |
| `update_chapter_defaults` | Change a universe's, series', or book's `chapter_defaults`, with the same changes. |
| `checkpoint` | Commit the den (or some paths) to git, with the assistant as co-author. |

## 5. What keeps the writes safe

- **Nothing is saved that the den couldn't read.** Each edit is checked
  as a load of the den would check it, and refused if it would add an
  error. A bad section comment that's already in the note doesn't block an
  edit: the save goes ahead, and the result lists that error along with any
  warnings, marked `error`.
- **No lost edits.** A write to an existing file takes the hash the
  assistant read (`get_entity`, `get_chapter`, or `get_chapter_defaults`
  give it), and is refused if the file has changed since, for example
  because you edited it in your text editor. The file is compared again
  just before each try at saving, so a save you make while Wolfden is
  waiting to save stays too. The assistant reads it again and retries. A
  save in the moment between that comparison and Wolfden's save can still
  be lost (format.md F-GEN-5).
- **Finished books stay finished.** Nothing in a book with `status: final`
  is written: its entities, `book.md`, or outline. A rename that would
  have to rewrite a link in one is refused as a whole, and so is a change
  to a series' or universe's `chapter_defaults` that would change what a
  final book's chapters have.
- **A file Wolfden can't read stops the writes that need it.** A rename,
  move, copy, or create checks links across the whole den, a change to
  `chapter_defaults` looks at every book below it, and a new book or
  series writes the story order. If a note, `book.md`, or outline is there
  but can't be read, because another program has it open to write, or a
  folder can't be listed, the write is refused as a whole with
  `den_unreadable`, names the file, and writes nothing (format.md F-GEN-5).
  So links in that file don't break, and a final book's refusal isn't
  skipped. `check` lists such a file if it's locked against reading. An error
  that a name or position wasn't found keeps its own code, and adds that a file
  couldn't be read and may be why. If a file is only open to write, a write's
  lookup that fails says the same, but its code is `den_unreadable`: check the name
  in the message too, since either could be the cause.
- **Effects, not just bytes.** Results say what an edit means for the
  story: a status that takes an entity out of canon views, a chapter left
  with no POV, the links a rename rewrote, and any link that would now
  reach a different entity (a change like that is refused unless the
  assistant is told to allow it).
- **Your comments stay put.** A YAML comment you wrote on a field or a
  list item (`- "[[mina-harker]]"  # main`) is kept when the assistant
  changes something else. Moving or changing one list item in place
  (`move`, `replace`, or `set` on an item's path) keeps the comments
  that belong to it. Removing an item, or taking it out and adding it
  again, drops its comment, and the result quotes what went, as it
  does for comments inside a multi-line value that's replaced whole.
  Dated entries are never moved (format.md F-TMP-7).
- **Files only, until a checkpoint.** Writes go to the files, not to git.
  `checkpoint` commits them, and lists which files the session wrote and
  which were changed outside it, so your own edits are never hidden in an
  assistant's commit. Until then, `git diff` shows exactly what changed.
  The server's instructions ask the assistant to checkpoint at natural
  stopping points, such as the end of a notes pass or a planning
  session, with a message that says what the pass was ("Notes pass after
  drafting ch 12"), rather than after every edit.
- **One universe at a time.** Every tool but `check` and `checkpoint`
  reads one universe; `search` and `list_entities` also take `*` for
  every universe. `--universe` pins a session to one.

Your MCP client may also ask you to approve each tool call. That's the
client's setting, not Wolfden's.

## 6. Example prompts

These work against the Dracula sample. The sample's one book has the key
`dracula`, so `dracula:9` is chapter 9. Prompts that write need the copy
described in [Pick a den](#2-pick-a-den).

| Prompt | Tools it uses |
|---|---|
| "Which characters has the reader met by chapter 9? Just the names." | `canon_as_of` |
| "What does the reader know about Renfield as of chapter 8? No spoilers from later chapters." | `get_entity` with `as_of` |
| "Where could I foreshadow Renfield letting the Count in? Read ahead." | `get_entity` with no `as_of` |
| "Draft a short scene with Renfield at chapter 20. He has already let the Count in, which the reader learns in chapter 21, so let that show in how he behaves without saying it." | `get_entity` with `as_of`, then with no `as_of` |
| "Where do my notes mention the bloofer lady?" | `search` |
| "Which characters are physicians?" | `search` with `field: occupation` |
| "I'm drafting chapter 20. What has the reader heard about the bloofer lady so far?" | `search` with `as_of` |
| "I'm drafting chapter 9. Which locations could I use without introducing a new one?" | `list_entities` with `type` and `as_of` |
| "Whose point of view is chapter 4, and when and where is it set?" | `get_chapter` |
| "Run a continuity check on the den and explain anything it finds." | `check` |
| "Add to Renfield's Want section: he wants to be trusted with something alive and large." | `get_entity`, then `append_section` |
| "What changed in Renfield's notes since the last checkpoint?" | `get_history` with `diff` |
| "Rename the faction 'The little band' to 'The Crew of Light', and fix any links." | `get_entity`, then `rename_entity` |
| "Note an idea, not canon: what if Renfield kept a diary of his own?" | `add_idea` |
| "Checkpoint this session as 'Renfield notes'." | `checkpoint` |

## 7. Gotchas for AI assistants

What an assistant must do, and must avoid, when it works on a writer's
den. The server's instructions and tool descriptions say the same in
fewer words; this list is for you, and for an assistant that reads the
docs. The writer's own list is in
[using-wolfden.md](using-wolfden.md#gotchas).

- **Work through the tools, not the files.** An assistant that can
  also edit files, such as Claude Code, should still change notes with
  Wolfden's tools. They check each edit as the den reads it, keep
  links, ids, and finished books safe, and remember what the session
  wrote for `checkpoint`. A file edited directly gets none of that.
  No tool yet adds a series, a book, or a chapter, or edits an outline;
  edit those files directly only when the writer asks, then run `check`.
- **Never touch `.wolfden/` or `.git/`.** `.wolfden/` is Wolfden's
  index, rebuilt from the files; `.git/` is the den's history. Neither
  holds anything to edit.
- **`frozen` means ask the writer.** A book with `status: final` is
  finished, and nothing in it can be written. There's no tool to unfreeze
  it, and an assistant mustn't work around the refusal, by editing the
  file directly or by copying a note out of the book to change it there.
  Ask the writer, who can unfreeze it in the web UI or with `wolfden
  unfreeze`. The error names the book and ends by saying so: "Ask the
  writer whether to unfreeze book 'dracula'; only they can."
- **`stale_file` means read again, then retry.** The file changed after
  the assistant read it, often because the writer is editing it. Read it
  again (`get_entity`, `get_chapter`, or `get_chapter_defaults`), and
  make the edit on what's there now. Never resend the old text over the
  writer's change.
- **`write_failed` listing files means tell the writer, and don't retry yet.** A
  write stopped part way, and those files couldn't be put back. Pass on what it says
  of each. Only a file it says holds the edit can be restored from history. One it
  says another program saved holds that save: restoring it would lose it.
- **`den_busy` means wait a moment, then retry.** Another Wolfden
  (`wolfden serve`, another assistant's session, or a command) was
  writing to the den for the whole 30 seconds a write waits. Nothing of
  yours was written; retry the same call. If it then comes back
  `stale_file`, read again as usual. If it keeps happening, ask the
  writer what's running.
- **`den_unreadable` naming a file means ask the writer to close it.**
  Another program has the file open, or its permissions don't let
  Wolfden read it. Nothing was written. Tell the writer which file it is,
  and retry the same call once they've closed it. Don't call it broken:
  this isn't about what's in it. `check` reports it as F-GEN-5 only if the program
  keeps others from reading: a file merely open to write can be read by you, and is
  clean in `check`, but a write won't build on it, in case it's part of a save.
  While it's locked, links to that file can show as broken.
- **`den_notice` means some of the den is missing.** While the den has
  errors, every result but `check`'s has a short `den_notice`, such as
  `"This den has 2 errors, so some notes or sections may be missing from this
  answer. Call check with severity 'error' to see them."` It names no note or
  section, so it's the same at a story position. Tell the writer once, not with every
  result. In a pinned
  session it counts that universe's errors and the den-level ones. A file that
  can't be read counts too. Duplicate ids are warnings, so they don't count. Asking for a note an error leaves out says
  so and names the line; with `as_of`, it says so only for a path or an id,
  and doesn't say which line or why.
- **`allow_relinks` only with the writer's say-so.** A create, copy,
  rename, or move that would send existing links to a different note is
  refused, with the links listed. Show them to the writer, and set
  `allow_relinks` only if they agree. The same goes for
  `entry_outside_scope` from `move_entity`: the writer picks
  `keep_entry` or `enter_at_new_scope`.
- **Show an ignored chapter value before replacing it.** A chapter
  field (or chapter default) can hold a value the field can't take, such
  as `setting: Whitby harbour` where `setting` takes links to locations.
  The den ignores it, but it's the writer's text. `get_chapter` lists it
  under `ignored` (and `get_chapter_defaults` marks it `ignored`), with
  why, and lists a list item that's left out by its number. An `add` or
  `remove` on a value that isn't a list, or on a field that takes one
  value (`tense`), is refused, naming the chapter (`chapter dracula:7
  (…)`), the file, what's written, and why it's ignored, and saying what
  to do: a `set` with the whole value replaces it, and `clear` goes back
  to the default (or, in a universe's `chapter_defaults`, takes it out).
  Ask the writer before either.
- **Positions must resolve.** A position is `book:chapter`, by key
  (`dracula:5`), and must be a chapter the book lists; `get_chapter`
  lists them. A read at one that doesn't is `bad_position`: for a book the
  universe doesn't have it names no other book, and for a chapter a book
  doesn't have it gives that book's chapters. So is a
  write that sets one as a `from` or `until`, or as the value of a
  position field such as a thread's `planted` (where a value that isn't
  a position is refused too): `create_entity`,
  `update_fields`, and `append_section` save nothing. One already in the
  file that the write doesn't touch doesn't stop it. To leave a position
  out, omit it (or, in `update_fields`, set it to `null`).
- **Read by position to avoid spoilers.** Without a position,
  `get_entity`, `list_entities`, and `search` give the author's full
  view, ideas and later reveals included. To draft or review a chapter,
  use `canon_as_of` at that chapter, and pass the same position as `as_of`
  to `get_entity`, `search`, and `list_entities`. `get_history` has no position.
- **A search at a position finds what the reader can.** `search` with `as_of`
  looks only in the notes the reader has met, as `get_entity` with `as_of` reads
  them: a later note, later or undecided text, a later value, and a comment
  neither match nor show, so a word that's only there gets the same empty answer as
  a word no note has. A note's name and the other names the reader knows there
  match, and a link matches by the text it shows, not by its target. A type's default value isn't searched, with
  or without `as_of`, though `get_entity` shows it, so no hit doesn't mean a note has
  no such value. `as_of` is per call, so a search without it
  sees everything again. `universe: "*"` doesn't go with `as_of`, and a position
  that can't be read is `bad_position`, in `search` and `list_entities` as in
  `canon_as_of`.
- **Read ahead when the work needs more than the reader knows.** Read the
  note with `get_entity` and no `as_of` for planning, outlining, foreshadowing,
  and reviewing continuity across chapters, or whenever the writer asks. A full
  read has ideas and retconned facts as well as canon, so check a fact's status
  before you rely on it.
- **Read ahead to write a character who knows more than the reader.** A read
  at a position leaves out what the reader hasn't learned, and that can be
  something a character already knows. Say the reader knows a stranger only as
  the Magpie, and her friend learned her real name in an earlier scene the
  narrator didn't hear. At that chapter the name isn't in the view, so an
  assistant reading only the view writes the friend as not knowing it.
- **Keep later facts out of the prose, not away from the writer.** Tell the
  writer anything they ask about, since spoilers are hidden from the prose and
  never from the author. In prose, the view at the chapter is what the reader
  knows. A later fact can shape what's hinted at, and how a character who'd
  already know it behaves: the friend can avoid the nickname, or go quiet when
  someone asks who the Magpie is. The prose must never state the fact, or have
  a narrator or point-of-view character reveal it.
- **A fact later for the reader isn't always known to the character yet.** A
  full read holds what a character already knows beside what hasn't happened to
  them. Renfield's note read at chapter 20 of the Dracula sample leaves out that
  he let the Count in, which he's already done, and his death, which he has
  ahead of him. Use only what the character would know by then: go by the note's
  dates, such as an event's `when`, or ask the writer. If you aren't sure which
  read applies, ask about that too.
- **A read at a position doesn't say when a fact ends.** A fact's `until`
  tells what the reader doesn't know yet, so `canon_as_of` and `get_entity`
  leave it out at a position, and nothing says it's gone: in the result, the
  fact just holds, as it does for the reader. To plan ahead, read the note
  with `get_entity` and no `as_of`, which has every `from` and `until`.
  `canon_as_of` with no position won't do, since it's the end of the story,
  and a fact that ends before then isn't in it. Read without `as_of` before
  changing a fact too, or the change can drop an `until` you weren't shown.
- **A read at a position doesn't say where a thread is going.** The value of
  a position field, such as a thread's `planted` or `paid_off`, can point
  ahead. At a position, `canon_as_of` and `get_entity` keep only the values at
  or before it: a later `paid_off` isn't there at all, and `planted` lists only
  what's been planted so far. A value that isn't a position the universe has
  can't be compared, so it's left out too. Nothing says any of them are gone.
  `get_entity` with no `as_of` has them all. `canon_as_of` with no position
  leaves none out for being later, but it's the end of the story, so it holds
  only each field's entry in effect then.
- **Read without `as_of` before changing a position field.** A field missing
  from a read at a position may be set in the note, and a list's item numbers
  may not match the note's. With `planted: ["ash:1", "ember:2"]`, a read at
  `ember:3` gives `["ember:2"]`, and `set` on `fields.planted.0` then replaces
  `ash:1`. The hash is the same from both reads, so the write isn't refused.
- **A read at a position leaves out HTML comments.** `canon_as_of` and
  `get_entity` at a position take every comment out of a lead or section, since
  one can name a later fact, and nothing says they're gone. That goes for a
  misplaced `<!-- wolfden: ... -->` too. A comment shown as code stays. Any
  guidance the writer left you in a comment is there only in a read with no
  position ([format.md F-VIEW-6](format.md#94-canon-views)).
- **Read without `as_of` before replacing a note's text.** No tool here replaces a
  section or lead: `append_section` adds to one and leaves its comments where they
  are. If you can also edit the file another way, read it with no position first.
  Rewriting a section from a read at a position deletes comments you never saw, and
  the hash is the same from both reads, so nothing refuses the write.
- **A name the reader hasn't learned isn't a name.** A note's `known_as` holds
  the names readers learn partway through the story, each with its `from`
  ([format.md F-ENT-10](format.md#71-frontmatter)). At a position before that
  `from`, `get_entity` by that name is `unknown_entity`, just as for a name no
  note has, `search` doesn't find it, and the note's `aliases` don't list it.
  From its `from` on, it's in `aliases` like any other name, with no date.
  Without `as_of`, `get_entity` lists `known_as` with each `from`. So when
  drafting, refer to a note by the names in `aliases` at that position, and
  never by a name you saw only in a read with no position.
- **A link by a name the reader hasn't learned reads by the note's name.** At a
  position before the name's `from`, `[[Mara Voss]] signed it` reads
  `[[The Courier]] signed it`, with a label kept (`[[The Courier|the stranger]]`).
  The file still says `[[Mara Voss]]`, and `line` numbers don't change. Another
  reason to read with no position before replacing text. You can still write a
  link by a later name: it resolves, and `check` warns that it's ahead of its
  position, which you should tell the writer.
- **A read at a position only goes by positions.** Text the writer put in a
  section or lead, a field the note's type doesn't define, or a `ref` field's
  value can still mention later events, and so can a later name written as plain
  text rather than as a link.
- **Writes land at once; only `checkpoint` records them.** An open page
  in the web UI shows each write within about 5 seconds, or once the
  writer closes an editor or form they have open there. If they save a
  note you changed meanwhile, they're shown both versions. A write isn't
  in the den's history until a checkpoint. Make one at the end of
  a pass, not after each edit, with a message that says what the pass
  was and the assistant as `co_author`. It records the whole den unless
  `paths` names some files, so tell the writer what's in
  `changed_outside_session`. After `move_entity`, `paths` needs both the
  old and the new path.
- **Pinned canon: check with the writer.** An edit to a pinned fact is
  made, and `pinned_canon` in the result lists it. Confirm it was meant.
- **A pinned session sees one universe.** With `--universe`, every call
  reads that universe, and one that names another universe, or `*`, is
  refused. `checkpoint` still records the whole den.
- **Images stay put.** `move_entity` and `copy_entity` don't move a
  note's images or change their paths, so tell the writer when a moved
  note's images need moving too.

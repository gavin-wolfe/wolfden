import type { BookNode, EntitySummary, PositionView, UniverseNode } from './api'

// Story positions as the pages show them, and which notes the reader has met.
// A position is what the API's asOf takes: "book:chapter", or a bare book key
// for the start of that book (format.md §8).

/** What an untitled chapter is called: by its number, which can move when a chapter is inserted, or by its key if it has none. */
export function chapterLabel(c: { key: string; number: number | null }): string {
  return `Chapter ${c.number ?? c.key}`
}

/**
 * A chapter in words: "chapter 5", or, for a chapter without a number (such
 * as Dracula's closing note), its title in quotes, else "chapter <key>".
 */
export function chapterWords(p: Pick<PositionView, 'chapter' | 'number' | 'title'>): string {
  if (p.number !== null) {
    return `chapter ${p.number}`
  }

  return p.title ? `“${p.title}”` : `chapter ${p.chapter}`
}

/** "Dracula, chapter 5: Letters—Lucy and Mina", or "Dracula, “Note”", for the position bar. */
export function positionLabel(p: PositionView): string {
  if (p.chapter === null) {
    return `${p.bookName}, start`
  }

  return `${p.bookName}, ${chapterWords(p)}${p.title && p.number !== null ? `: ${p.title}` : ''}`
}

/**
 * A position in words for a sentence: "Dracula chapter 11", "Dracula,
 * “Note”" for a chapter without a number, or "the start of Dracula" for a
 * bare book key. A position the list doesn't have is given as
 * written.
 */
export function describePosition(position: string, positions: readonly PositionView[]): string {
  const exact = positions.find((p) => p.position === position)
  if (exact) {
    if (exact.chapter === null) {
      return `the start of ${exact.bookName}`
    }

    return exact.number === null ? `${exact.bookName}, ${chapterWords(exact)}` : `${exact.bookName} ${chapterWords(exact)}`
  }

  const book = positions.find((p) => p.book === position)
  return book ? `the start of ${book.bookName}` : position
}

/**
 * A position as a tag names it: "chapter 11", or "Ash chapter 11" in a
 * universe with several books, or "the start of Ash" for a bare book. One the
 * list doesn't have is given as written.
 */
export function placeWords(position: string, positions: readonly PositionView[]): string {
  const exact = positions.find((p) => p.position === position)
  if (!exact || exact.chapter === null) {
    return describePosition(position, positions)
  }

  return positions.every((p) => p.book === exact.book) ? chapterWords(exact) : `${exact.bookName} ${chapterWords(exact)}`
}

/**
 * Where a position falls in story order, as an index into `positions`: a
 * chapter's own index, or just before a book's first chapter for its bare
 * key. Null when the list doesn't have it.
 */
export function ordinalOf(position: string, positions: readonly PositionView[]): number | null {
  const exact = positions.findIndex((p) => p.position === position)
  if (exact >= 0) {
    return exact
  }

  const book = positions.findIndex((p) => p.book === position)
  return book >= 0 ? book - 0.5 : null
}

/**
 * True if `position` comes after `at` in story order, so readers at `at`
 * haven't got there. A bare book orders the same as its first chapter
 * (format.md F-POS-7), so `ash:1` isn't ahead of `ash`. False with no position
 * being viewed, or for a position the list doesn't have, since nothing can be
 * said about it.
 */
export function isAheadOf(position: string, at: string | undefined, positions: readonly PositionView[]): boolean {
  const here = at === undefined ? null : ordinalOf(at, positions)
  const there = ordinalOf(position, positions)
  return here !== null && there !== null && Math.ceil(there) > Math.ceil(here)
}

/**
 * Where an entity enters canon, as a position: its own `from`, or the start
 * of the scope it's filed in (format.md F-ENT-6). Null for the start of the
 * story (an entity filed in the universe) or a series with no books yet.
 */
export function entryOf(entity: Pick<EntitySummary, 'from' | 'scopeKind' | 'scopeKey'>, positions: readonly PositionView[]): string | null {
  if (entity.from) {
    return entity.from
  }

  switch (entity.scopeKind) {
    case 'book':
      return entity.scopeKey
    case 'series':
      return positions.find((p) => p.series === entity.scopeKey)?.book ?? null
    default:
      return null
  }
}

/** True if readers at `at` have got to `position`. False when either isn't in the list, since nothing says they have. */
function reached(position: string, at: string, positions: readonly PositionView[]): boolean {
  const here = ordinalOf(at, positions)
  const there = ordinalOf(position, positions)
  return here !== null && there !== null && Math.ceil(there) <= Math.ceil(here)
}

/**
 * The universe's tree as the Reader's view shows it (#300), from the hierarchy as
 * of `at`: a book or chapter readers have got to, and a book or series filed with
 * a note they've met, so no met note is left without a place. The rest aren't
 * there, so nothing names or counts them. A series with no books yet isn't at any
 * position, so it's only there for a note.
 */
export function readersTree(u: UniverseNode, at: string, positions: readonly PositionView[]): UniverseNode {
  const book = (b: BookNode): BookNode => ({ ...b, chapters: b.chapters.filter((c) => reached(c.position, at, positions)) })
  const books = (bs: readonly BookNode[]) => bs.filter((b) => reached(b.position, at, positions) || b.entities.total > 0).map(book)
  return {
    ...u,
    series: u.series.map((s) => ({ ...s, books: books(s.books) })).filter((s) => s.books.length > 0 || s.entities.total > 0),
    books: books(u.books),
  }
}

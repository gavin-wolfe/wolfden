import type { PositionView, ReaderMark } from './api'
import { describePosition, placeWords } from './story'

// The words of the tags that say what readers at a story position don't know
// (#300), in one place, for the note pages and the lists. The API's marks say
// what's so, and these are how a page says it.

export const AHEAD_OF_READERS = 'ahead of readers'
export const AHEAD_WHY = "Readers at this point in the story don't know this yet."
export const NO_LONGER_TRUE = 'no longer true here'
export const NOT_PLACED = 'not placed in the story yet'
export const NEVER_TRUE = "never true (its dates don't overlap)"

/** The line above a list with tags in it, which are plain text there (#302), so a keyboard or a touch reads it where it can't hover. */
export const MARKS_LEGEND = "Tags say what readers at this point in the story don't see. Each note's page has the details."

/**
 * What the Reader's view doesn't cover yet. Remove this, and where it's shown,
 * when steps 5 and 6 of #300 give search, the book and chapter pages, and the
 * position bar's Book and Chapter menus the two views.
 */
export const READERS_VIEW_REACH = "This covers note pages and lists of notes. Search, book and chapter pages, and the Book and Chapter menus aren't part of the Reader's view yet."

/** The words for a list longer than the API's limit, with what it does list. `metFirst` is for the Author's view, which puts them first. */
export function listCutWords(listed: number, total: number, metFirst: boolean): string {
  return `${number(listed)} of ${number(total)} notes are listed; groups count only what's listed.${metFirst ? ' What readers have met is listed first.' : ''}`
}

/** The same, for a list of what enters at one chapter, which can't say none does. */
export function chapterListCutWords(listed: number, total: number): string {
  return `Only ${number(listed)} of ${number(total)} notes could be checked, so more may enter the story here.`
}

function number(n: number): string {
  return n.toLocaleString('en-US')
}

/** Why a position isn't placed, naming it if there's one to name. */
export function notPlacedWhy(position: string | null): string {
  return position === null
    ? 'Nothing places this in the story yet, so no view of the story shows it.'
    : `${position} isn't a chapter or book this universe has yet, so no view of the story shows this.`
}

/** A tag's words, and the sentence that explains it. `note` is for a whole note, rather than a fact. */
export function markWords(mark: ReaderMark, positions: readonly PositionView[], note = false): { words: string; why: string } {
  const { at } = mark
  const where = at === null ? null : describePosition(at, positions)
  switch (mark.kind) {
    case 'ahead':
      if (note) {
        return {
          words: at === null ? 'enters later in the story' : `enters at ${placeWords(at, positions)}`,
          why: where === null ? "Readers at this point in the story haven't met this note yet." : `Readers at this point in the story haven't met this note. They first meet it at ${where}.`,
        }
      }

      return { words: AHEAD_OF_READERS, why: where === null ? AHEAD_WHY : `${AHEAD_WHY} They reach it at ${where}.` }
    case 'ended':
      return { words: NO_LONGER_TRUE, why: where === null ? 'This stopped being true before this point in the story.' : `This stopped being true at ${where}.` }
    case 'unplaced':
      return { words: NOT_PLACED, why: notPlacedWhy(at) }
    case 'never':
      return { words: NEVER_TRUE, why: 'It ends before it starts, or starts after what holds it ends, so no view of the story shows it. Check its dates.' }
  }
}

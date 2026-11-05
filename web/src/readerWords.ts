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

/** The position bar's words for moving forward in the Reader's view, which names nothing ahead (#300). */
export const FURTHER_ON = 'Further on'
export const NEXT_CHAPTER = 'Next chapter'
export const NEXT_BOOK = 'Next book'
export const PICK_IN_AUTHORS_VIEW = "Pick a later point in the Author's view…"
export const MENUS_NAME_NOTHING_AHEAD = "Readers here don't know what's ahead, so this doesn't name it. Step forward with Next, or pick a later point in the Author's view: the position stays when you switch back."

/** The banner for a book or chapter the Reader's view has nothing of. It may well exist: it doesn't say, or name it. */
export function notReachedWords(what: 'book' | 'chapter' | 'series'): string {
  return `Readers haven't got to this ${what} by this point in the story.`
}

/** Tags for a search hit whose words are where a reader's page doesn't show them. */
export const ONLY_IN_ALIAS = 'only in an alias'
export const ONLY_IN_ALIAS_WHY = "A note's page doesn't list its aliases in the Reader's view, so a search there doesn't look at them."
export const ONLY_IN_LINK_TARGET = "only in a link's target"
export const ONLY_IN_LINK_TARGET_WHY = 'Readers see only the label of a labelled link, so a search in the Reader\'s view looks only at that.'

/** What a position field's values in a search hit are, for its tag: `value` for one, or the sentence for several. */
export function aheadValuesWhy(values: readonly string[]): string {
  return `${AHEAD_WHY} ${values.length === 1 ? `It names ${values[0]}.` : `It names ${values.join(', ')}.`}`
}

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

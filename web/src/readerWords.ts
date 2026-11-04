import type { PositionView, ReaderMark } from './api'
import { describePosition, placeWords } from './story'

// The words of the tags that say what readers at a story position don't know
// (#300), in one place, for the note pages now and the lists after them. The
// API's marks say what's so, and these are how a page says it.

export const AHEAD_OF_READERS = 'ahead of readers'
export const AHEAD_WHY = "Readers at this point in the story don't know this yet."
export const NO_LONGER_TRUE = 'no longer true here'
export const NOT_PLACED = 'not placed in the story yet'
export const NEVER_TRUE = "never true (its dates don't overlap)"

/** The line above a list with tags in it, which are plain text there (#302). */
export const MARKS_LEGEND = "Tags say what readers at this point in the story don't see. Hover over one for more."

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

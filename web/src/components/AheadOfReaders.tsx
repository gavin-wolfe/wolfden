import { useState } from 'react'
import type { PositionView, ReaderMark } from '../api'
import { AHEAD_OF_READERS, AHEAD_WHY, markWords } from '../readerWords'

/**
 * Says what readers at the story position being viewed don't know (#288, #300).
 * It's words, not only a colour, and it says nothing of the thing it marks, so
 * anything on a page can use it, apart from inside a link, since it's a button.
 * Hovering gives the sentence as a tooltip. A keyboard or a touch can't hover,
 * so pressing it shows the sentence beside it.
 */
export function ReaderTag({ words, why }: { words: string; why: string }) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button type="button" className="ahead-of-readers" title={why} aria-expanded={open} onClick={() => setOpen(!open)}>
        {words}
      </button>
      {open && <span className="ahead-of-readers-why">{why}</span>}
    </>
  )
}

/** The tag for a fact's date or value that readers haven't reached. */
export function AheadOfReaders() {
  return <ReaderTag words={AHEAD_OF_READERS} why={AHEAD_WHY} />
}

/** The tag for one of the API's marks. `note` is for a whole note, which `enters at` a chapter. */
export function MarkTag({ mark, positions, note = false }: { mark: ReaderMark; positions: readonly PositionView[]; note?: boolean }) {
  const { words, why } = markWords(mark, positions, note)
  return <ReaderTag words={words} why={why} />
}

/**
 * A mark in a list: the same words and tooltip as plain text, since a button in
 * every row would be a Tab stop each and can't go inside a link. The list has one
 * line that explains them all (#302).
 */
export function MarkList({ mark, positions }: { mark: ReaderMark; positions: readonly PositionView[] }) {
  const { words, why } = markWords(mark, positions)
  return (
    <span className="ahead-of-readers" title={why}>
      {words}
    </span>
  )
}

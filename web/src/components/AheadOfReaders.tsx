import { useState } from 'react'

const WHY = "Readers at this point in the story don't know this yet."

/**
 * Marks something the writer can see that readers don't know yet at the story
 * position being viewed (#288). It's words, not only a colour, and it says
 * nothing about the thing it marks, so anything on a page can use it, apart
 * from inside a link, since it's a button. Hovering gives the sentence as a
 * tooltip. A keyboard or a touch can't hover, so pressing it shows the
 * sentence beside it.
 */
export function AheadOfReaders() {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button type="button" className="ahead-of-readers" title={WHY} aria-expanded={open} onClick={() => setOpen(!open)}>
        ahead of readers
      </button>
      {open && <span className="ahead-of-readers-why">{WHY}</span>}
    </>
  )
}

/**
 * Marks something the writer can see that readers don't know yet at the story
 * position being viewed (#288). It's words, not only a colour, and it says
 * nothing about the thing it marks, so anything on a page can use it.
 */
export function AheadOfReaders() {
  return (
    <span className="ahead-of-readers" title="Readers at this point in the story don't know this yet.">
      ahead of readers
    </span>
  )
}

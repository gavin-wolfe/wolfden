import { useEffect } from 'react'

// While the writer has an editor or a form open, the page doesn't refresh
// itself from outside changes (liveRefresh.tsx), or when the window gets
// focus back. A refresh could take away what the form sits in: a section's
// editor follows its line in the file, and a failed answer replaces the page
// with the error. Nothing is lost by waiting: a save is still checked against
// the file's hash, and the refresh runs when the form closes.
//
// This module imports nothing of the app's, so api.ts can use it.

let holds = 0
const listeners = new Set<() => void>()

function changed(by: number) {
  holds += by
  listeners.forEach((listener) => listener())
}

/** True while an editor or form is open. */
export function refreshHeld(): boolean {
  return holds > 0
}

/** Calls `listener` when `refreshHeld` may have changed, until the function it returns is called. */
export function onRefreshHeldChange(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

/** The query client's `refetchOnWindowFocus`: as TanStack Query's default, but not while a form is open. */
export function refetchOnFocusUnlessHeld(): boolean {
  return !refreshHeld()
}

/** Holds back the page's refreshes while `active`, and while the component is on the page. */
export function useHoldRefresh(active: boolean) {
  useEffect(() => {
    if (!active) {
      return
    }

    changed(1)
    return () => changed(-1)
  }, [active])
}

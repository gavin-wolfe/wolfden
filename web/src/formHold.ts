import { useEffect, useSyncExternalStore } from 'react'

// While the writer has an editor or a form open, the page doesn't refresh
// itself from outside changes (liveRefresh.tsx). A refresh could take away
// what the form sits in: a section's editor follows its line in the file, and
// a failed answer replaces the page with the error. Nothing is lost by
// waiting: a save is still checked against the file's hash, and the refresh
// runs when the form closes.
//
// This module imports nothing of the app's, so api.ts can use it.

let holds = 0
const listeners = new Set<() => void>()

function changed(by: number) {
  holds += by
  listeners.forEach((listener) => listener())
}

function refreshHeld(): boolean {
  return holds > 0
}

function onRefreshHeldChange(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

/** True while an editor or form is open. */
export function useRefreshHeld(): boolean {
  return useSyncExternalStore(onRefreshHeldChange, refreshHeld)
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

/**
 * The error a page should show in place of a query's answer, or null. A
 * refresh already on its way when a form opened can still fail: the page and
 * the form stay until the form closes, since an error page would take the
 * writer's text with it.
 */
export function useShownError(query: { isError: boolean; error: Error | null; data: unknown }): Error | null {
  const held = useRefreshHeld()
  return query.isError && !(held && query.data !== undefined) ? query.error : null
}

/**
 * The query client's defaults for when answers are asked for again. Only one
 * question comes back by itself (`denFilesQuery`): it costs little, its
 * failure changes nothing on the page, and it knows about any change to the
 * files. So a tab coming back, with the server stopped, doesn't turn the page
 * into an error. The server is on this computer, so the browser's idea of
 * being online says nothing about reaching it.
 */
export const refreshDefaults = {
  networkMode: 'always',
  refetchOnWindowFocus: false,
  refetchOnReconnect: false,
} as const

import { useEffect, useRef, useSyncExternalStore } from 'react'
import { queryOptions, useQuery, useQueryClient } from '@tanstack/react-query'
import { getJson } from './api'
import { onRefreshHeldChange, refreshHeld } from './formHold'

/** How often an open page asks whether the den's files changed. */
export const LIVE_REFRESH_MS = 5_000

const FILES_KEY = 'den-files'

interface DenFiles {
  version: number
  fingerprint: string
}

/**
 * The den's files fingerprint: paths, sizes, and times, so asking costs a
 * walk of the folders and no file's contents. TanStack Query asks at the
 * interval only while the page is visible, and again when it comes back.
 */
export const denFilesQuery = queryOptions({
  queryKey: [FILES_KEY],
  queryFn: () => getJson<DenFiles>('/api/den/files'),
  refetchInterval: LIVE_REFRESH_MS,
  refetchOnWindowFocus: true,
  retry: false,
})

/**
 * Shows writes made outside this page, by an assistant or another editor
 * (#285). When the fingerprint changes, every cached answer is refreshed, as
 * after a save. Asking about the fingerprint, not each answer, keeps a quiet
 * den quiet: each answer checks the whole den's files first, and a failed
 * check of the fingerprint (the server stopped, say) changes nothing on the
 * page. While an editor or form is open it waits (formHold.ts).
 */
export function LiveRefresh() {
  const client = useQueryClient()
  const fingerprint = useQuery(denFilesQuery).data?.fingerprint
  const held = useSyncExternalStore(onRefreshHeldChange, refreshHeld)
  const shown = useRef<string | undefined>(undefined)
  useEffect(() => {
    if (fingerprint === undefined || fingerprint === shown.current || held) {
      return
    }

    const first = shown.current === undefined
    shown.current = fingerprint
    if (!first) {
      void client.invalidateQueries({ predicate: (query) => query.queryKey[0] !== FILES_KEY })
    }
  }, [fingerprint, held, client])
  return null
}

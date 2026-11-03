import { useEffect, useRef } from 'react'
import { queryOptions, useQuery, useQueryClient } from '@tanstack/react-query'
import { getJson } from './api'
import { useRefreshHeld } from './formHold'

/** How often an open page asks whether the den's files changed. */
export const LIVE_REFRESH_MS = 5_000

/** How long a failed answer on the page waits before it's asked for again, once the server is answering. */
export const RETRY_FAILED_MS = 15_000

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
 *
 * A refresh that failed isn't left on the page for good: while the server is
 * answering, it's tried again every `RETRY_FAILED_MS`.
 */
export function LiveRefresh() {
  const client = useQueryClient()
  const files = useQuery(denFilesQuery)
  const fingerprint = files.data?.fingerprint
  const answeredAt = files.dataUpdatedAt
  const held = useRefreshHeld()
  const shown = useRef<string | undefined>(undefined)
  useEffect(() => {
    if (fingerprint === undefined) {
      return
    }

    // The first answer is what the page was loaded on, even if a form is open by then.
    if (shown.current === undefined) {
      shown.current = fingerprint
      return
    }

    if (fingerprint !== shown.current && !held) {
      shown.current = fingerprint
      void client.invalidateQueries({ predicate: (query) => query.queryKey[0] !== FILES_KEY })
    }
  }, [fingerprint, held, client])
  useEffect(() => {
    if (answeredAt === 0 || held) {
      return
    }

    const before = Date.now() - RETRY_FAILED_MS
    // An answer can take longer than the time between these checks, on a large den, so one under way is left to finish.
    void client.refetchQueries(
      {
        type: 'active',
        predicate: (query) => query.queryKey[0] !== FILES_KEY && query.state.status === 'error' && query.state.errorUpdatedAt <= before,
      },
      { cancelRefetch: false },
    )
  }, [answeredAt, held, client])
  return null
}

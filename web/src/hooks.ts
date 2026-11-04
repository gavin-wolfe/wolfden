import { useCallback, useEffect } from 'react'
import { useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { entitiesQuery, markedEntitiesQuery, positionsQuery } from './api'
import { useShownError } from './formHold'
import type { EntitySummary, ReaderMark } from './api'
import type { StorySearch } from './router'

/**
 * The story position from the URL (`?at=`), or undefined for everything, and
 * a way to change it. A change replaces the history entry, so Back leaves the
 * page rather than stepping back through chapters. At a position, `readers`
 * says it's the Reader's view (`?readers=1`) rather than the Author's (#300).
 * Going back to everything ends it, since there's only the one view there.
 */
export function usePosition(): {
  at: string | undefined
  setAt: (at: string | undefined) => void
  readers: boolean
  setReaders: (readers: boolean) => void
} {
  const { at, readers } = useSearch({ strict: false }) as StorySearch
  const navigate = useNavigate()
  const setAt = useCallback(
    (next: string | undefined) => {
      void navigate({ to: '.', search: (prev: StorySearch) => ({ ...prev, at: next, readers: next === undefined ? undefined : prev.readers }), replace: true })
    },
    [navigate],
  )
  const setReaders = useCallback(
    (next: boolean) => {
      void navigate({ to: '.', search: (prev: StorySearch) => ({ ...prev, readers: next ? ('1' as const) : undefined }), replace: true })
    },
    [navigate],
  )
  return { at, setAt, readers: at !== undefined && readers === '1', setReaders }
}

/**
 * Sets the browser tab's title to `title` · Wolfden, or Wolfden while it's
 * undefined (as a page loads). A page in a reader's view of the story passes
 * only names the reader has met, so its tab never names a note met later.
 */
export function useDocumentTitle(title: string | undefined) {
  useEffect(() => {
    document.title = title ? `${title} · Wolfden` : 'Wolfden'
  }, [title])
}

/** The universe in the URL, if the page is in one. */
export function useUniverseKey(): string | undefined {
  return (useParams({ strict: false }) as { universe?: string }).universe
}

/** A note in a list: `met` if the reader at the position knows it, with its mark in the Author's view. */
export interface ListedEntity extends EntitySummary {
  met: boolean
  readerMark?: ReaderMark
}

/**
 * The universe's notes as this view lists them (#300). With no position, every
 * one. In the Author's view, every one, with what the reader has met first and
 * the rest marked. In the Reader's view, only what the reader has met, from the
 * same list MCP's `canon_as_of` holds: the rest are never asked for, so nothing
 * on the page can name them or count them.
 */
export function useEntitiesInView(universe: string) {
  const { at, readers } = usePosition()
  const marked = at !== undefined && !readers
  const plain = useQuery({ ...entitiesQuery(universe, at), enabled: !marked })
  const all = useQuery({ ...markedEntitiesQuery(universe, at ?? ''), enabled: marked })
  const positions = useQuery(positionsQuery(universe))

  const failedPlain = useShownError(plain)
  const failedAll = useShownError(all)
  const failedPositions = useShownError(positions)
  const list = marked ? all.data : plain.data
  const entities: ListedEntity[] | undefined = marked ? all.data?.entities : plain.data?.entities.map((e) => ({ ...e, met: true }))

  return {
    at,
    readers,
    entities,
    truncated: list?.truncated ?? false,
    positions: positions.data?.positions,
    isPending: (marked ? all.isPending : plain.isPending) || positions.isPending,
    error: (marked ? failedAll : failedPlain) ?? failedPositions,
  }
}

/**
 * What's known of a note a read at a position didn't give: only the banner for a
 * note the reader hasn't met asks, so the list isn't fetched otherwise.
 */
export function useNoteSummary(universe: string, key: string, enabled: boolean): EntitySummary | undefined {
  const all = useQuery({ ...entitiesQuery(universe, undefined), enabled })
  return all.data?.entities.find((e) => e.key === key || e.id === key || e.path === key)
}

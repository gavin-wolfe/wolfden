import { useCallback, useEffect } from 'react'
import { useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { entitiesQuery, positionsQuery } from './api'
import { useShownError } from './formHold'
import { splitByView, type LaterEntity } from './story'
import type { EntitySummary } from './api'
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

/**
 * The universe's entities, and which of them the reader has met at the
 * position: the unfiltered list, compared with the list as of the position.
 */
export function useEntitiesInView(universe: string) {
  const { at } = usePosition()
  const all = useQuery(entitiesQuery(universe, undefined))
  const inView = useQuery({ ...entitiesQuery(universe, at), enabled: at !== undefined })
  const positions = useQuery(positionsQuery(universe))

  const isPending = all.isPending || positions.isPending || (at !== undefined && inView.isPending)
  const failedAll = useShownError(all)
  const failedPositions = useShownError(positions)
  const failedInView = useShownError(inView)
  const error = failedAll ?? failedPositions ?? (at !== undefined ? failedInView : null)
  const viewKeys = at !== undefined && inView.data ? new Set(inView.data.entities.map((e) => e.key)) : null

  /** Splits some of the universe's entities into those met and those met later. */
  const split = (entities: readonly EntitySummary[]): { shown: EntitySummary[]; hidden: LaterEntity[] } =>
    splitByView(entities, viewKeys, at, positions.data?.positions ?? [])

  return { at, all: all.data, positions: positions.data?.positions, isPending, error, split }
}

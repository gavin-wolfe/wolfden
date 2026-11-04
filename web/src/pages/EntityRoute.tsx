import { getRouteApi } from '@tanstack/react-router'
import { EntityPageView } from './EntityPageView'
import { HistoryPage } from './HistoryPage'

const route = getRouteApi('/u/$universe/e/$')

/**
 * An entity's page, or with `?view=history` its history (#27). The history is
 * keyed by the note, so nothing it holds (a compare tick) carries over to
 * another note.
 */
export function EntityRoute() {
  const { universe, _splat: key = '' } = route.useParams()
  const { view, at, readers } = route.useSearch()
  return view === 'history' ? <HistoryPage key={JSON.stringify([universe, key])} /> : <EntityPageView key={JSON.stringify([universe, key, at ?? null, readers ?? null])} />
}

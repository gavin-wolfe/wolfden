import type { PositionView } from '../api'
import type { ListedEntity } from '../hooks'
import { MarkList } from './AheadOfReaders'
import { EntityLink } from './Links'
import { StatusBadge } from './States'

/**
 * A list of notes: all of them at no position, or in the Reader's view what the
 * reader has met. In the Author's view, those come first, then the rest, each
 * tagged, greyed, and below a line (#300). Nothing collapses. The tags are plain
 * text, so the page gives the list one line to explain them (`MARKS_LEGEND`).
 */
export function EntityList({
  universe,
  entities,
  positions,
  current,
  empty,
}: {
  universe: string
  entities: readonly ListedEntity[]
  positions: readonly PositionView[]
  current?: string
  empty?: string
}) {
  if (entities.length === 0) {
    return empty ? <p className="muted">{empty}</p> : <ul className="entity-list" />
  }

  const met = entities.filter((e) => e.met)
  const later = entities.filter((e) => !e.met)
  return (
    <ul className="entity-list">
      {met.map((e) => (
        <li key={e.key}>
          <EntityLink universe={universe} entityKey={e.key} className={e.key === current ? 'current' : undefined}>
            {e.name}
          </EntityLink>{' '}
          <StatusBadge status={e.status} />
        </li>
      ))}
      {later.map((e, i) => (
        <li key={e.key} className={i === 0 && met.length > 0 ? 'later later-first' : 'later'}>
          <EntityLink universe={universe} entityKey={e.key} className={e.key === current ? 'current' : undefined}>
            {e.name}
          </EntityLink>{' '}
          <StatusBadge status={e.status} />
          {e.readerMark && <MarkList mark={e.readerMark} positions={positions} note />}
        </li>
      ))}
    </ul>
  )
}

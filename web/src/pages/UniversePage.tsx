import { useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import { count, hierarchyQuery, positionsQuery, type BookNode, type EntityCounts, type PositionView, type SeriesNode, type UniverseNode } from '../api'
import { typePlural } from '../components/Links'
import { ChapterDefaultsSection } from '../components/ChapterFields'
import { NewContainerButton } from '../components/NewContainer'
import { NewMenu } from '../components/NewEntity'
import { ErrorState, Loading } from '../components/States'
import { useShownError } from '../formHold'
import { useDocumentTitle, usePosition } from '../hooks'
import { readersTree } from '../story'

const route = getRouteApi('/u/$universe')

/**
 * The hierarchy for a universe's or series' page at the position (#300). In the
 * Reader's view it's the tree readers know, and it waits for the story's positions
 * until it can say what that is, so nothing later shows meanwhile. That view is
 * read-only, so these pages have no New buttons or chapter defaults there.
 */
export function useHierarchyInView(universe: string) {
  const { at, readers } = usePosition()
  const hierarchy = useQuery(hierarchyQuery(universe, at))
  const positions = useQuery(positionsQuery(universe))
  const failedHierarchy = useShownError(hierarchy)
  const failedPositions = useShownError(positions)
  const list: PositionView[] | undefined = positions.data?.positions
  const whole = hierarchy.data?.universe
  return {
    readers,
    isPending: hierarchy.isPending || (readers && positions.isPending),
    failed: failedHierarchy ?? (readers ? failedPositions : null),
    universe: whole && readers && list && at !== undefined ? readersTree(whole, at, list) : readers ? undefined : whole,
  }
}

/** A universe's overview: its series and books in story order, and how many notes each holds. */
export function UniversePage() {
  const { universe } = route.useParams()
  const { at } = usePosition()
  const hierarchy = useHierarchyInView(universe)
  const failed = hierarchy.failed
  useDocumentTitle(hierarchy.universe?.name)

  if (hierarchy.isPending) {
    return <Loading what="the universe" />
  }

  if (failed || !hierarchy.universe) {
    return <ErrorState error={failed ?? new Error("The universe can't be read.")} />
  }

  const u = hierarchy.universe
  return (
    <div className="page-body">
      <p className="eyebrow">Universe</p>
      <h1>{u.name}</h1>
      <p className="lede">
        {count(totalNotes(u), 'note', 'notes')}
        {at === undefined ? ' in all' : ' the reader has met by this point'}, in{' '}
        {u.series.length > 0 && `${count(u.series.length, 'series', 'series')} and `}
        {count(u.books.length + u.series.reduce((n, s) => n + s.books.length, 0), 'book', 'books')}.
      </p>
      {!hierarchy.readers && (
        <div className="editor-actions">
          <NewContainerButton kind="series" inName={u.name} />
          <NewContainerButton kind="book" inName={u.name} />
          <NewMenu universe={universe} place={{ scope: { kind: 'universe', key: u.key }, name: u.name }} />
        </div>
      )}
      <Counts counts={u.entities} title="Filed in the universe" />
      {u.series.map((s) => (
        <section key={s.key}>
          <h2>
            <Link to="/u/$universe/s/$series" params={{ universe, series: s.key }}>
              {s.name}
            </Link>
          </h2>
          <BookList universe={universe} books={s.books} empty="No books yet." />
        </section>
      ))}
      {u.books.length > 0 && (
        <section>
          <h2>{u.series.length ? 'Standalone books' : 'Books'}</h2>
          <BookList universe={universe} books={u.books} />
        </section>
      )}
      {!hierarchy.readers && <ChapterDefaultsSection key={u.key} scope={{ universe }} name={u.name} />}
    </div>
  )
}

function totalNotes(u: UniverseNode): number {
  const books = (bs: BookNode[]) => bs.reduce((n, b) => n + b.entities.total, 0)
  return u.entities.total + books(u.books) + u.series.reduce((n: number, s: SeriesNode) => n + s.entities.total + books(s.books), 0)
}

export function BookList({ universe, books, empty }: { universe: string; books: BookNode[]; empty?: string }) {
  if (books.length === 0) {
    return empty ? <p className="muted">{empty}</p> : null
  }

  return (
    <ol className="card-list">
      {books.map((b) => (
        <li key={b.key}>
          <Link to="/u/$universe/b/$book" params={{ universe, book: b.key }}>
            {b.name}
          </Link>{' '}
          <span className="muted">
            {count(b.chapters.length, 'chapter', 'chapters')}, {count(b.entities.total, 'note', 'notes')}
            {b.status === 'final' ? ', final' : ''}
          </span>
        </li>
      ))}
    </ol>
  )
}

/** How many notes of each type are filed at a scope. */
export function Counts({ counts, title }: { counts: EntityCounts; title: string }) {
  const types = Object.entries(counts.byType)
  if (types.length === 0) {
    return null
  }

  return (
    <section>
      <h2>{title}</h2>
      <ul className="counts">
        {types.map(([type, n]) => (
          <li key={type}>
            {typePlural(type)} <span className="count">{n}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}

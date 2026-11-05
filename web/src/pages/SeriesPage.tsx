import { Link, getRouteApi } from '@tanstack/react-router'
import { ChapterDefaultsSection } from '../components/ChapterFields'
import { NewContainerButton } from '../components/NewContainer'
import { NewMenu } from '../components/NewEntity'
import { ErrorState, Loading } from '../components/States'
import { useDocumentTitle, usePosition } from '../hooks'
import { BookList, Counts, useHierarchyInView } from './UniversePage'

const route = getRouteApi('/u/$universe/s/$series')

/** A series: its books in series order. */
export function SeriesPage() {
  const { universe, series } = route.useParams()
  const { setReaders } = usePosition()
  const hierarchy = useHierarchyInView(universe)
  const failed = hierarchy.failed
  useDocumentTitle(hierarchy.universe?.series.find((x) => x.key === series)?.name)

  if (hierarchy.isPending) {
    return <Loading what="the series" />
  }

  if (failed || !hierarchy.universe) {
    return <ErrorState error={failed ?? new Error("The series can't be read.")} />
  }

  const u = hierarchy.universe
  const s = u.series.find((x) => x.key === series)
  if (!s && hierarchy.readers) {
    // It may well exist: the Reader's view doesn't say, or name it.
    return (
      <div className="page-body">
        <div className="banner" role="status">
          <p>Readers haven't got to this series by this point in the story.</p>
          <p className="banner-actions">
            <button type="button" onClick={() => setReaders(false)}>
              Author's view
            </button>
          </p>
        </div>
      </div>
    )
  }

  if (!s) {
    return (
      <div className="page-body">
        <h1>No such series</h1>
        <p>{u.name} has no series called '{series}'.</p>
      </div>
    )
  }

  return (
    <div className="page-body">
      <nav className="breadcrumb" aria-label="Where this is">
        <Link to="/u/$universe" params={{ universe }}>
          {u.name}
        </Link>
      </nav>
      <p className="eyebrow">Series</p>
      <h1>{s.name}</h1>
      {!hierarchy.readers && (
        <div className="editor-actions">
          <NewContainerButton kind="book" series={s.key} inName={s.name} />
          <NewMenu universe={universe} place={{ scope: { kind: 'series', key: s.key }, name: s.name }} />
        </div>
      )}
      <h2>Books</h2>
      <BookList universe={universe} books={s.books} empty={hierarchy.readers ? "Readers haven't got to a book in this series." : 'This series has no books yet.'} />
      {!hierarchy.readers && <ChapterDefaultsSection key={s.key} scope={{ universe, series: s.key }} name={s.name} />}
      <Counts counts={s.entities} title="Filed in the series" />
    </div>
  )
}

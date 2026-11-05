import { useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import { hierarchyQuery, type BookNode, type SeriesNode, type UniverseNode } from '../api'
import { bookChapterFieldsQuery, readersChapterFieldsQuery } from '../chapterFieldsApi'
import { MarkList } from '../components/AheadOfReaders'
import { AddChapter } from '../components/AddChapter'
import { ChapterDefaultsSection, ChapterFieldSummary, positionFieldsOf } from '../components/ChapterFields'
import { FreezeControl } from '../components/FreezeControl'
import { NewMenu } from '../components/NewEntity'
import { ErrorState, Loading } from '../components/States'
import { useShownError } from '../formHold'
import { useDocumentTitle, useEntitiesInView, usePosition } from '../hooks'
import { MARKS_LEGEND, notReachedWords } from '../readerWords'
import { chapterLabel, isAheadOf } from '../story'
import { Counts } from './UniversePage'

const route = getRouteApi('/u/$universe/b/$book')

/** Finds a book in a universe, with the series it's in, if any. */
export function findBook(u: UniverseNode, key: string): { book: BookNode; series: SeriesNode | null } | null {
  for (const s of u.series) {
    const book = s.books.find((b) => b.key === key)
    if (book) {
      return { book, series: s }
    }
  }

  const book = u.books.find((b) => b.key === key)
  return book ? { book, series: null } : null
}

/**
 * The Reader's view's page for a book, chapter, or series it has nothing of. It may
 * well exist: this doesn't say, or name it (#300).
 */
export function NotReached({ what }: { what: 'book' | 'chapter' | 'series' }) {
  const { setReaders } = usePosition()
  return (
    <div className="page-body">
      <div className="banner" role="status">
        <p>{notReachedWords(what)}</p>
        <p className="banner-actions">
          <button type="button" onClick={() => setReaders(false)}>
            Author's view
          </button>
        </p>
      </div>
    </div>
  )
}

function BookBreadcrumb({ universe, name, series }: { universe: string; name: string; series: SeriesNode | null }) {
  return (
    <nav className="breadcrumb" aria-label="Where this is">
      <Link to="/u/$universe" params={{ universe }}>
        {name}
      </Link>
      {series && (
        <>
          {' › '}
          <Link to="/u/$universe/s/$series" params={{ universe, series: series.key }}>
            {series.name}
          </Link>
        </>
      )}
    </nav>
  )
}

export function BookPage() {
  const { universe, book } = route.useParams()
  const { at, readers } = usePosition()
  return readers ? <ReadersBookPage universe={universe} book={book} at={at!} /> : <WritersBookPage universe={universe} book={book} />
}

/** A book as readers at the position know it: the chapters they've reached and what's on them, and nothing to change it with (#300). */
function ReadersBookPage({ universe, book, at }: { universe: string; book: string; at: string }) {
  const hierarchy = useQuery(hierarchyQuery(universe, at, true))
  const failed = useShownError(hierarchy)
  const found = hierarchy.data ? findBook(hierarchy.data.universe, book) : null
  // Only a book it knows of: another's error could say what else there is.
  const fields = useQuery({ ...readersChapterFieldsQuery(universe, book, at), enabled: found !== null })
  useDocumentTitle(found?.book.name)

  if (hierarchy.isPending) {
    return <Loading what="the book" />
  }

  if (failed) {
    return <ErrorState error={failed} />
  }

  if (!found) {
    return <NotReached what="book" />
  }

  const { book: b, series } = found
  const fieldsOf = (key: string) => fields.data?.chapters.find((x) => x.key === key)
  return (
    <div className="page-body">
      <BookBreadcrumb universe={universe} name={hierarchy.data!.universe.name} series={series} />
      <p className="eyebrow">Book{b.status === 'final' ? ', final' : ''}</p>
      <h1>{b.name}</h1>
      <h2>Chapters</h2>
      {b.chapters.length === 0 ? (
        <p className="muted">Readers haven't got to a chapter of this book.</p>
      ) : (
        <ol className="chapters">
          {b.chapters.map((c) => {
            const chapterFields = fieldsOf(c.key)
            return (
              <li key={c.key} aria-current={c.position === at ? 'step' : undefined}>
                <Link to="/u/$universe/b/$book/c/$chapter" params={{ universe, book: b.key, chapter: c.key }}>
                  <span className="chapter-number">{c.number ?? ''}</span>
                  <span className="chapter-title">{c.heading ?? chapterLabel(c)}</span>
                </Link>
                {c.position === at && <span className="badge">you are here</span>}
                {chapterFields && <ChapterFieldSummary universe={universe} chapter={chapterFields} readers />}
              </li>
            )
          })}
        </ol>
      )}
      <Counts counts={b.entities} title="Filed in this book, met by this point" />
    </div>
  )
}

/**
 * A book for the writer: its chapters with their headings and fields, each a link to
 * the chapter, and its chapter defaults. At a position, the Author's view has all of
 * it, tagged: a chapter readers haven't reached, a link to a note they haven't met in
 * a chapter they have, and how many of the notes filed here they've met (#300).
 */
function WritersBookPage({ universe, book }: { universe: string; book: string }) {
  const hierarchy = useQuery(hierarchyQuery(universe, undefined))
  const failed = useShownError(hierarchy)
  const fields = useQuery(bookChapterFieldsQuery(universe, book))
  const view = useEntitiesInView(universe)
  const { at, positions: known } = view
  useDocumentTitle(hierarchy.data ? findBook(hierarchy.data.universe, book)?.book.name : undefined)

  if (hierarchy.isPending) {
    return <Loading what="the book" />
  }

  const error = failed ?? (at === undefined ? null : view.error)
  if (error) {
    return <ErrorState error={error} />
  }

  const u = hierarchy.data!.universe
  const found = findBook(u, book)
  if (!found) {
    return (
      <div className="page-body">
        <h1>No such book</h1>
        <p>{u.name} has no book called '{book}'.</p>
      </div>
    )
  }

  const { book: b, series } = found
  const positions = known ?? []
  const fieldsOf = (key: string) => (fields.data?.book === b.key ? fields.data.chapters.find((x) => x.key === key) : undefined)
  const notes = at === undefined ? undefined : new Map((view.entities ?? []).flatMap((e) => (e.readerMark ? [[e.key, e.readerMark] as const] : [])))
  const later = (position: string) => isAheadOf(position, at, positions)
  const met = at === undefined || !view.entities ? undefined : metByType(view.entities, b.key)
  return (
    <div className="page-body">
      <BookBreadcrumb universe={universe} name={u.name} series={series} />
      <p className="eyebrow">Book{b.status === 'final' ? ', final' : ''}</p>
      <h1>{b.name}</h1>
      <FreezeControl universe={universe} book={b.key} />
      {/* A final book takes nothing new, so it has no New note menu, as it has no Add chapter (#203). */}
      {b.status !== 'final' && (
        <div className="editor-actions">
          <NewMenu universe={universe} place={{ scope: { kind: 'book', key: b.key }, name: b.name }} />
        </div>
      )}
      <h2>Chapters</h2>
      {b.chapters.length === 0 ? (
        <p className="muted">This book has no chapters yet.</p>
      ) : (
        <>
          {at !== undefined && b.chapters.some((c) => later(c.position)) && <p className="muted list-legend">{MARKS_LEGEND}</p>}
          <ol className="chapters">
            {b.chapters.map((c) => {
              const chapterFields = fieldsOf(c.key)
              const ahead = later(c.position)
              return (
                <li key={c.key} className={ahead ? 'later' : undefined} aria-current={c.position === at ? 'step' : undefined}>
                  <Link to="/u/$universe/b/$book/c/$chapter" params={{ universe, book: b.key, chapter: c.key }}>
                    <span className="chapter-number">{c.number ?? ''}</span>
                    <span className="chapter-title">{c.heading ?? chapterLabel(c)}</span>
                  </Link>
                  {c.position === at && <span className="badge">you are here</span>}
                  {ahead && <MarkList mark={{ kind: 'ahead', at: c.position }} positions={positions} />}
                  {chapterFields && (
                    // A chapter with its own tag needs no more on what's in it.
                    <ChapterFieldSummary universe={universe} chapter={chapterFields} marks={notes && at !== undefined && !ahead ? { notes, positions, at, positionFields: positionFieldsOf(fields.data?.fieldTypes ?? []) } : undefined} />
                  )}
                </li>
              )
            })}
          </ol>
        </>
      )}
      <AddChapter key={`add-chapter:${b.key}`} universe={universe} book={b.key} bookName={b.name} chapters={b.chapters.length} final={b.status === 'final'} />
      <ChapterDefaultsSection key={`chapter-defaults:${b.key}`} scope={{ universe, book: b.key }} name={b.name} />
      <Counts counts={b.entities} title="Filed in this book" met={met} />
    </div>
  )
}

/** How many of the notes filed in `book` readers have met, by type. */
function metByType(entities: readonly { met: boolean; type: string; scopeKind: string; scopeKey: string }[], book: string): Record<string, number> {
  const counts: Record<string, number> = {}
  for (const e of entities) {
    if (e.met && e.scopeKind === 'book' && e.scopeKey === book) {
      counts[e.type] = (counts[e.type] ?? 0) + 1
    }
  }

  return counts
}

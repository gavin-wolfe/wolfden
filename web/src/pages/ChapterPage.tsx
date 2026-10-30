import { useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import { hierarchyQuery, type ChapterNode } from '../api'
import { ChapterFieldsSection } from '../components/ChapterFields'
import { EntityList } from '../components/EntityList'
import { ErrorState, Loading } from '../components/States'
import { useDocumentTitle, useEntitiesInView, usePosition } from '../hooks'
import { entryOf } from '../story'
import { findBook } from './BookPage'

const route = getRouteApi('/u/$universe/b/$book/c/$chapter')

/**
 * A chapter: its number and heading, its chapter fields (POV, when, …), a
 * way to read the den as of it, and the notes that enter the story there.
 */
export function ChapterPage() {
  const { universe, book, chapter } = route.useParams()
  const { at, setAt } = usePosition()
  const hierarchy = useQuery(hierarchyQuery(universe, undefined))
  // Headings as of the position (#227): the page's structure comes from the
  // full hierarchy, so a change of position doesn't reload it (or close a
  // field's form), but a heading names only what the reader has met (#26).
  const asOf = useQuery({ ...hierarchyQuery(universe, at), enabled: at !== undefined })
  const headingOf = (ch: ChapterNode): string | null => {
    if (at === undefined) {
      return ch.heading
    }

    // Until the answer as of the position is in (or if it fails), the title alone, which is shown whatever the position.
    const there = asOf.data ? findBook(asOf.data.universe, book)?.book.chapters.find((x) => x.key === ch.key) : undefined
    return there ? there.heading : ch.title
  }
  const view = useEntitiesInView(universe)
  const titled = hierarchy.data ? findBook(hierarchy.data.universe, book) : null
  const titledChapter = titled?.book.chapters.find((x) => x.key === chapter)
  useDocumentTitle(titled && titledChapter ? `${headingOf(titledChapter) ?? `Chapter ${titledChapter.key}`} · ${titled.book.name}` : undefined)

  if (hierarchy.isPending) {
    return <Loading what="the chapter" />
  }

  if (hierarchy.isError) {
    return <ErrorState error={hierarchy.error} />
  }

  const u = hierarchy.data.universe
  const found = findBook(u, book)
  const index = found?.book.chapters.findIndex((c) => c.key === chapter) ?? -1
  if (!found || index < 0) {
    return (
      <div className="page-body">
        <h1>No such chapter</h1>
        <p>
          {found ? `${found.book.name} has no chapter '${chapter}'.` : `${u.name} has no book called '${book}'.`}
        </p>
      </div>
    )
  }

  const b = found.book
  const c = b.chapters[index]
  const prev = b.chapters[index - 1]
  const next = b.chapters[index + 1]
  const entering = view.all ? view.all.entities.filter((e) => entryOf(e, view.positions ?? []) === c.position) : []
  const { shown, hidden } = view.split(entering)

  return (
    <div className="page-body">
      <nav className="breadcrumb" aria-label="Where this is">
        <Link to="/u/$universe" params={{ universe }}>
          {u.name}
        </Link>
        {' › '}
        <Link to="/u/$universe/b/$book" params={{ universe, book: b.key }}>
          {b.name}
        </Link>
      </nav>
      <p className="eyebrow">Chapter {c.number ?? c.key}</p>
      <h1>{headingOf(c) ?? `Chapter ${c.number ?? c.key}`}</h1>
      <p>
        {at === c.position ? (
          <span className="badge">You are reading as of this chapter</span>
        ) : (
          <button type="button" onClick={() => setAt(c.position)}>
            Read the den as of this chapter
          </button>
        )}
      </p>
      <h2>Fields</h2>
      <ChapterFieldsSection key={`${b.key}:${c.key}`} universe={universe} book={b.key} chapter={c.key} label="this chapter" />
      <h2>Enters the story here</h2>
      {view.isPending ? (
        <Loading what="the notes" />
      ) : view.error ? (
        <ErrorState error={view.error} />
      ) : (
        <EntityList
          key={`${b.key}:${c.key}:${at ?? ''}`}
          universe={universe}
          shown={shown}
          hidden={hidden}
          empty="No notes enter the story in this chapter."
        />
      )}
      <nav className="prev-next" aria-label="Chapters">
        {prev ? (
          <Link to="/u/$universe/b/$book/c/$chapter" params={{ universe, book: b.key, chapter: prev.key }} rel="prev">
            ‹ {prev.number ?? prev.key}. {headingOf(prev)}
          </Link>
        ) : (
          <span />
        )}
        {next && (
          <Link to="/u/$universe/b/$book/c/$chapter" params={{ universe, book: b.key, chapter: next.key }} rel="next">
            {next.number ?? next.key}. {headingOf(next)} ›
          </Link>
        )}
      </nav>
    </div>
  )
}

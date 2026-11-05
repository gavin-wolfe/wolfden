import { useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import { hierarchyQuery, type ChapterNode } from '../api'
import { ChapterFieldsSection } from '../components/ChapterFields'
import { EntityList } from '../components/EntityList'
import { ErrorState, Loading } from '../components/States'
import { useShownError } from '../formHold'
import { useDocumentTitle, useEntitiesInView, usePosition } from '../hooks'
import { MARKS_LEGEND, chapterListCutWords } from '../readerWords'
import { chapterLabel, entryOf } from '../story'
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
  const failed = useShownError(hierarchy)
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
  useDocumentTitle(titled && titledChapter ? `${headingOf(titledChapter) ?? chapterLabel(titledChapter)} · ${titled.book.name}` : undefined)
  // In prev and next: "3. The Quiet Hour", or for an untitled chapter what its own page is headed.
  const stepLabel = (ch: ChapterNode): string => {
    const heading = headingOf(ch)
    return heading === null ? chapterLabel(ch) : `${ch.number ?? ch.key}. ${heading}`
  }

  if (hierarchy.isPending) {
    return <Loading what="the chapter" />
  }

  if (failed) {
    return <ErrorState error={failed} />
  }

  const u = hierarchy.data!.universe
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
  const entering = view.entities ? view.entities.filter((e) => entryOf(e, view.positions ?? []) === c.position) : []

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
      <h1>{headingOf(c) ?? chapterLabel(c)}</h1>
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
        <>
          {view.truncated && <p className="muted list-legend">{chapterListCutWords(view.entities?.length ?? 0, view.total)}</p>}
          {entering.some((e) => e.readerMark) && <p className="muted list-legend">{MARKS_LEGEND}</p>}
          <EntityList
            universe={universe}
            entities={entering}
            positions={view.positions ?? []}
            // It says nothing of a chapter readers haven't reached, whose notes they haven't met either.
            empty={view.truncated ? 'None of those enter the story here.' : view.readers ? 'Readers have met no notes that enter the story here.' : 'No notes enter the story in this chapter.'}
          />
        </>
      )}
      <nav className="prev-next" aria-label="Chapters">
        {prev ? (
          <Link to="/u/$universe/b/$book/c/$chapter" params={{ universe, book: b.key, chapter: prev.key }} rel="prev">
            ‹ {stepLabel(prev)}
          </Link>
        ) : (
          <span />
        )}
        {next && (
          <Link to="/u/$universe/b/$book/c/$chapter" params={{ universe, book: b.key, chapter: next.key }} rel="next">
            {stepLabel(next)} ›
          </Link>
        )}
      </nav>
    </div>
  )
}

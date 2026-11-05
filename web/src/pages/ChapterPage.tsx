import { useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import { hierarchyQuery, type ChapterNode } from '../api'
import { MarkList } from '../components/AheadOfReaders'
import { ChapterFieldsSection, ReadersChapterFieldsSection } from '../components/ChapterFields'
import { EntityList } from '../components/EntityList'
import { ErrorState, Loading } from '../components/States'
import { useShownError } from '../formHold'
import { useDocumentTitle, useEntitiesInView, usePosition } from '../hooks'
import { MARKS_LEGEND, chapterListCutWords } from '../readerWords'
import { chapterLabel, entryOf, isAheadOf } from '../story'
import { NotReached, findBook } from './BookPage'

const route = getRouteApi('/u/$universe/b/$book/c/$chapter')

/**
 * A chapter: its number and heading, its chapter fields (POV, when, …), a
 * way to read the den as of it, and the notes that enter the story there.
 * At a position, the Author's view has all of it, and tags a chapter readers
 * haven't reached, in this page's heading and its prev and next, and a link in a
 * field to a note they haven't met. The Reader's view has only what readers
 * know, so it has no later chapter's page or prev or next, and it's read-only (#300).
 */
export function ChapterPage() {
  const { universe, book, chapter } = route.useParams()
  const { at, setAt, readers } = usePosition()
  const hierarchy = useQuery(hierarchyQuery(universe, readers ? at : undefined, readers))
  const failed = useShownError(hierarchy)
  const view = useEntitiesInView(universe)
  const positions = view.positions ?? []
  const titled = hierarchy.data ? findBook(hierarchy.data.universe, book) : null
  const titledChapter = titled?.book.chapters.find((x) => x.key === chapter)
  useDocumentTitle(titled && titledChapter ? `${titledChapter.heading ?? chapterLabel(titledChapter)} · ${titled.book.name}` : undefined)
  // In prev and next: "3. The Quiet Hour", or for an untitled chapter what its own page is headed.
  const stepLabel = (ch: ChapterNode): string => (ch.heading === null ? chapterLabel(ch) : `${ch.number ?? ch.key}. ${ch.heading}`)

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
    if (readers) {
      return <NotReached what="chapter" />
    }

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
  const entering = view.entities ? view.entities.filter((e) => entryOf(e, positions) === c.position) : []
  const later = (ch: ChapterNode) => !readers && isAheadOf(ch.position, at, positions)
  const tag = (ch: ChapterNode) => later(ch) && <MarkList mark={{ kind: 'ahead', at: ch.position }} positions={positions} />
  const tagged = later(c) || (next !== undefined && later(next)) || (prev !== undefined && later(prev)) || entering.some((e) => e.readerMark)
  // A chapter with its own tag needs no more on what's in it.
  const notes = at === undefined || readers || later(c) ? undefined : new Map((view.entities ?? []).flatMap((e) => (e.readerMark ? [[e.key, e.readerMark] as const] : [])))

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
      <p className="eyebrow">
        Chapter {c.number ?? c.key} {tag(c)}
      </p>
      <h1>{c.heading ?? chapterLabel(c)}</h1>
      {tagged && <p className="muted list-legend">{MARKS_LEGEND}</p>}
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
      {readers ? (
        <ReadersChapterFieldsSection universe={universe} book={b.key} chapter={c.key} at={at!} />
      ) : (
        <ChapterFieldsSection key={`${b.key}:${c.key}`} universe={universe} book={b.key} chapter={c.key} label="this chapter" marks={notes && at !== undefined ? { notes, positions, at } : undefined} />
      )}
      <h2>Enters the story here</h2>
      {view.isPending ? (
        <Loading what="the notes" />
      ) : view.error ? (
        <ErrorState error={view.error} />
      ) : (
        <>
          {view.truncated && <p className="muted list-legend">{chapterListCutWords(view.entities?.length ?? 0, view.total)}</p>}
          <EntityList
            universe={universe}
            entities={entering}
            positions={positions}
            empty={view.truncated ? 'None of those enter the story here.' : view.readers ? 'Readers have met no notes that enter the story here.' : 'No notes enter the story in this chapter.'}
          />
        </>
      )}
      <nav className="prev-next" aria-label="Chapters">
        {prev ? (
          <span>
            <Link to="/u/$universe/b/$book/c/$chapter" params={{ universe, book: b.key, chapter: prev.key }} rel="prev">
              ‹ {stepLabel(prev)}
            </Link>{' '}
            {tag(prev)}
          </span>
        ) : (
          <span />
        )}
        {next && (
          <span>
            <Link to="/u/$universe/b/$book/c/$chapter" params={{ universe, book: b.key, chapter: next.key }} rel="next">
              {stepLabel(next)} ›
            </Link>{' '}
            {tag(next)}
          </span>
        )}
      </nav>
    </div>
  )
}

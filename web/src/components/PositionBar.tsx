import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useRouterState } from '@tanstack/react-router'
import { nextPosition, positionsQuery, readersPositionsQuery, type PositionView, type PositionsResult } from '../api'
import { usePosition } from '../hooks'
import { FURTHER_ON, MENUS_NAME_NOTHING_AHEAD, NEXT_BOOK, NEXT_CHAPTER, PICK_IN_AUTHORS_VIEW } from '../readerWords'
import { chapterWords, ordinalOf, positionLabel } from '../story'

/**
 * The story-position bar across the top of every page (#26): a slider along
 * every position, book and chapter menus to jump, and a way back to
 * everything. The position is the URL's `?at=`; without it the pages show
 * everything, the end of the story. The slider's last step is "Everything",
 * so the arrow keys step chapter by chapter up to the end, and Home and End
 * jump to the first chapter and to everything.
 *
 * In the Reader's view, it's {@link ReadersPositionBar}.
 */
export function PositionBar({ universe }: { universe: string | undefined }) {
  const { readers } = usePosition()
  if (!universe) {
    return (
      <div className="position-bar" role="group" aria-label="Story position">
        <span className="muted">Choose a universe to pick a point in the story.</span>
      </div>
    )
  }

  return readers ? <ReadersPositionBar universe={universe} /> : <UniversePositionBar universe={universe} />
}

function UniversePositionBar({ universe }: { universe: string }) {
  const { at, setAt } = usePosition()
  const positions = useQuery(positionsQuery(universe))

  if (positions.isPending) {
    return (
      <div className="position-bar" role="group" aria-label="Story position">
        <span className="muted">Loading the story's chapters…</span>
      </div>
    )
  }

  if (positions.isError) {
    return (
      <div className="position-bar" role="group" aria-label="Story position">
        <span className="muted">The story's chapters can't be read: {positions.error.message}</span>
      </div>
    )
  }

  const list = positions.data.positions
  const end = list.length
  const ordinal = at === undefined ? end : ordinalOf(at, list)
  // A bare book key (the start of a book) sits on that book's first chapter.
  const index = ordinal === null ? end : Math.ceil(ordinal)
  const next = ordinal === null ? end : Math.floor(ordinal) + 1
  const current: PositionView | undefined = list[index]
  const unknown = at !== undefined && ordinal === null
  const label = at === undefined ? 'Everything (the end of the story)' : unknown ? (at === '' ? 'an empty position, which isn’t one in this story' : `'${at}', which isn't a position in this story`) : at === current?.book ? `${current.bookName}, start` : current ? positionLabel(current) : at

  const go = (i: number) => setAt(i >= end ? undefined : list[Math.max(0, i)].position)
  const books = [...new Map(list.map((p) => [p.book, p.bookName])).entries()]
  const book = at === undefined || unknown ? '' : current?.book ?? ''
  const chapters = list.filter((p) => p.book === book && p.chapter !== null)

  return (
    <div className="position-bar" role="group" aria-label="Story position">
      <button type="button" className="step" onClick={() => go(index - 1)} disabled={end === 0 || index === 0} aria-label="Previous chapter" title="Previous chapter">
        ‹
      </button>
      <input
        type="range"
        className="slider"
        min={0}
        max={end}
        step={1}
        value={index}
        disabled={end === 0}
        aria-label="Story position"
        aria-valuetext={label}
        onChange={(e) => go(Number(e.currentTarget.value))}
        onKeyDown={(e) => {
          const to = { ArrowLeft: index - 1, ArrowDown: index - 1, ArrowRight: next, ArrowUp: next, Home: 0, End: end }[e.key]
          if (to !== undefined && end > 0) {
            e.preventDefault()
            go(Math.min(Math.max(to, 0), end))
          }
        }}
      />
      <button type="button" className="step" onClick={() => go(next)} disabled={end === 0 || at === undefined} aria-label="Next chapter" title="Next chapter">
        ›
      </button>
      <select aria-label="Book" value={book} onChange={(e) => setAt(e.currentTarget.value ? list.find((p) => p.book === e.currentTarget.value)!.position : undefined)}>
        <option value="">Everything</option>
        {books.map(([key, name]) => (
          <option key={key} value={key}>
            {name}
          </option>
        ))}
      </select>
      <select aria-label="Chapter" value={chapters.some((p) => p.position === at) ? at : ''} disabled={chapters.length === 0} onChange={(e) => e.currentTarget.value && setAt(e.currentTarget.value)}>
        {!chapters.some((p) => p.position === at) && <option value="">{book ? 'Start' : '—'}</option>}
        {chapters.map((p) => (
          <option key={p.position} value={p.position}>
            {chapterWords(p)}
            {p.title && p.number !== null ? `: ${p.title}` : ''}
          </option>
        ))}
      </select>
      <output className="position-label" aria-live="polite">
        {at === undefined ? <strong>Everything</strong> : <>As of <strong>{label}</strong></>}
      </output>
      {at !== undefined && (
        <button type="button" className="reset" onClick={() => setAt(undefined)}>
          Show everything
        </button>
      )}
      {at !== undefined && <ViewSwitch />}
    </div>
  )
}

/** What's in the menus' Further on groups. */
const NEXT = 'next'
const PICK = 'pick'

/** The list up to `at`, from what the last position's answer had, while the new one comes: moving back mustn't show what's now ahead. */
function upTo(previous: PositionsResult | undefined, at: string): PositionsResult | undefined {
  if (!previous) {
    return undefined
  }

  const list = previous.positions
  const exact = list.findIndex((p) => p.position === at)
  const last = exact >= 0 ? exact : list.findIndex((p) => p.book === at)
  return last < 0 ? undefined : { ...previous, count: last + 1, positions: list.slice(0, last + 1) }
}

/**
 * The position bar in the Reader's view (#300). It's the bar with nothing ahead in
 * it: the menus list only the books and chapters readers have reached, so there's no
 * later title or name, or how many there are, and no slider, whose length and whose
 * thumb's place would say how far through the story this is. To move forward, `›` and
 * Next in the menus ask for the next one only when pressed, so nothing is told until
 * the writer goes there. A menu can't pick further without naming, so it says that's
 * in the Author's view, where the position stays. The request for the list asks for
 * only what's reached.
 */
function ReadersPositionBar({ universe }: { universe: string }) {
  const { at: position, setAt, setReaders } = usePosition()
  const at = position!
  const positions = useQuery({
    ...readersPositionsQuery(universe, at),
    placeholderData: (previous: PositionsResult | undefined) => upTo(previous, at),
  })
  const [said, setSaid] = useState<{ at: string; words: string } | null>(null)

  const forward = async (step: 'chapter' | 'book') => {
    setSaid(null)
    try {
      const next = await nextPosition(universe, at, step)
      if (next === null) {
        setSaid({ at, words: step === 'book' ? 'No later book.' : 'No later chapter.' })
      } else {
        setAt(next)
      }
    } catch (e) {
      setSaid({ at, words: `It can't move on: ${(e as Error).message}` })
    }
  }

  if (!positions.data) {
    return (
      <div className="position-bar" role="group" aria-label="Story position">
        <span className="muted">{positions.isError ? positions.error.message : "Loading the story's chapters…"}</span>
        {positions.isError && (
          <button type="button" className="reset" onClick={() => setAt(undefined)}>
            Show everything
          </button>
        )}
        {positions.isError && <ViewSwitch />}
      </div>
    )
  }

  const list = positions.data.positions
  const exact = list.findIndex((p) => p.position === at)
  // A bare book key (the start of a book) sits on that book's first chapter.
  const index = exact >= 0 ? exact : list.findIndex((p) => p.book === at)
  const current: PositionView | undefined = list[index]
  const label = at === current?.book ? `${current.bookName}, start` : current ? positionLabel(current) : at
  const books = [...new Map(list.map((p) => [p.book, p.bookName])).entries()]
  const book = current?.book ?? ''
  const chapters = list.filter((p) => p.book === book && p.chapter !== null)
  const atChapter = chapters.some((p) => p.position === at)
  const further = (next: string) => (
    <optgroup label={FURTHER_ON}>
      <option value={NEXT}>{next}</option>
      <option value={PICK}>{PICK_IN_AUTHORS_VIEW}</option>
    </optgroup>
  )
  const pickFurther = (value: string, step: 'chapter' | 'book') => {
    if (value === PICK) {
      setReaders(false)
    } else if (value === NEXT) {
      void forward(step)
    }
  }

  return (
    <div className="position-bar" role="group" aria-label="Story position">
      <button type="button" className="step" onClick={() => setAt(list[index - 1].position)} disabled={index <= 0} aria-label="Previous chapter" title="Previous chapter">
        ‹
      </button>
      <button type="button" className="step" onClick={() => void forward('chapter')} aria-label={NEXT_CHAPTER} title={`${NEXT_CHAPTER}. ${MENUS_NAME_NOTHING_AHEAD}`}>
        ›
      </button>
      <select
        aria-label="Book"
        title={MENUS_NAME_NOTHING_AHEAD}
        value={book}
        onChange={(e) => {
          const { value } = e.currentTarget
          if (value === '') {
            setAt(undefined)
          } else if (value === NEXT || value === PICK) {
            pickFurther(value, 'book')
          } else {
            setAt(list.find((p) => p.book === value)!.position)
          }
        }}
      >
        <option value="">Everything</option>
        {books.map(([key, name]) => (
          <option key={key} value={key}>
            {name}
          </option>
        ))}
        {further(NEXT_BOOK)}
      </select>
      <select
        aria-label="Chapter"
        title={MENUS_NAME_NOTHING_AHEAD}
        value={atChapter ? at : ''}
        onChange={(e) => {
          const { value } = e.currentTarget
          if (value === NEXT || value === PICK) {
            pickFurther(value, 'chapter')
          } else if (value) {
            setAt(value)
          }
        }}
      >
        {!atChapter && <option value="">Start</option>}
        {chapters.map((p) => (
          <option key={p.position} value={p.position}>
            {chapterWords(p)}
            {p.title && p.number !== null ? `: ${p.title}` : ''}
          </option>
        ))}
        {further(NEXT_CHAPTER)}
      </select>
      <output className="position-label" aria-live="polite">
        As of <strong>{label}</strong>
      </output>
      {said?.at === at && (
        <span className="muted position-note" role="status">
          {said.words}
        </span>
      )}
      <button type="button" className="reset" onClick={() => setAt(undefined)}>
        Show everything
      </button>
      <ViewSwitch />
    </div>
  )
}

/**
 * Whose view of a chosen position this is (#300): both labels are always there,
 * and the current one is pressed, so the page always says which it is. It's on
 * every page, as the sidebar is, but for Edit file, which is always the writer's.
 */
function ViewSwitch() {
  const { readers, setReaders } = usePosition()
  const onFile = useRouterState({ select: (s) => s.location.pathname === '/file' })
  if (onFile) {
    return null
  }

  return (
    <div className="view-switch" role="group" aria-label="Whose view">
      <button type="button" aria-pressed={!readers} onClick={() => setReaders(false)} title="Everything, tagged with what readers at this point don't know yet. You can edit here.">
        Author's view
      </button>
      <button type="button" aria-pressed={readers} onClick={() => setReaders(true)} title="Only what readers at this point know. Read-only.">
        Reader's view
      </button>
    </div>
  )
}

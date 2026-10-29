import { createContext, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from '@tanstack/react-router'
import { ApiRequestError } from '../api'
import { createContainer, worksQuery, type ContainerCreateResult } from '../containerApi'
import { useAfterSave } from './Editing'
import { trapTab } from './NewEntity'
import '../newEntity.css'

/**
 * Creating series and books (#213; maintainer decisions on #213,
 * 2026-10-29). A universe's page has **New series** and **New book** (a
 * standalone book), and a series' page has **New book**. The form asks for
 * the name, **Place after…** (by default, at the end of the story order),
 * and for a book an optional **Number of chapters**; Wolfden makes the
 * folder and its series.md or book.md, adds it to the universe's `order` or
 * the series' `books`, and opens the new page, as New note does.
 */

type Kind = 'series' | 'book'

interface Request {
  kind: Kind
  /** The series a new book goes in; left out, a standalone book of the universe. */
  series?: string
  /** What the form calls where it goes: the universe's or series' name. */
  inName: string
}

interface Created {
  universe: string
  result: ContainerCreateResult
  /** True once the new page has been shown, so leaving it clears the notice. */
  seen: boolean
}

const NewContainerContext = createContext<((request: Request) => void) | null>(null)
const CreatedContext = createContext<{ created: Created; dismiss: () => void } | null>(null)

/**
 * Holds the New series and New book form for the whole app, and what the
 * last create made, so the new page can say which file it is. The notice
 * lasts until the writer leaves that page.
 */
export function NewContainerProvider({ universe, children }: { universe: string | undefined; children: ReactNode }) {
  const [request, setRequest] = useState<Request | null>(null)
  const [created, setCreated] = useState<Created | null>(null)
  const returnFocus = useRef<HTMLElement | null>(null)
  const params = useParams({ strict: false }) as { universe?: string; series?: string; book?: string; chapter?: string }
  const onCreatedPage =
    created !== null &&
    params.universe === created.universe &&
    params.chapter === undefined &&
    (created.result.kind === 'series' ? params.series === created.result.key : params.book === created.result.key)

  useEffect(() => {
    if (!created) {
      return
    }

    if (onCreatedPage && !created.seen) {
      setCreated({ ...created, seen: true })
    } else if (!onCreatedPage && created.seen) {
      setCreated(null)
    }
  }, [created, onCreatedPage])

  const open = useCallback((next: Request) => {
    returnFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    setRequest(next)
  }, [])

  const close = useCallback(() => {
    setRequest(null)
    const target = returnFocus.current
    requestAnimationFrame(() => target?.focus())
  }, [])

  const shown = useMemo(() => (onCreatedPage ? { created: created!, dismiss: () => setCreated(null) } : null), [onCreatedPage, created])

  return (
    <NewContainerContext.Provider value={open}>
      <CreatedContext.Provider value={shown}>{children}</CreatedContext.Provider>
      {request && universe && (
        <NewContainerDialog
          universe={universe}
          request={request}
          onClose={close}
          onCreated={(result) => {
            setRequest(null)
            setCreated({ universe, result, seen: false })
          }}
        />
      )}
    </NewContainerContext.Provider>
  )
}

/** A page's **New series** or **New book** button. */
export function NewContainerButton({ kind, series, inName }: { kind: Kind; series?: string; inName: string }) {
  const open = useContext(NewContainerContext)
  if (!open) {
    throw new Error('NewContainerButton is used outside NewContainerProvider.')
  }

  const label = kind === 'series' ? 'New series' : 'New book'
  return (
    <button type="button" aria-label={`${label} in ${inName}`} onClick={() => open({ kind, series, inName })}>
      {label}
    </button>
  )
}

/** On the new series' or book's page, what the create made. Focus moves here, so a screen reader hears it. */
export function ContainerCreatedNotice() {
  const shown = useContext(CreatedContext)
  const ref = useRef<HTMLDivElement>(null)
  const key = shown?.created.result.path
  useEffect(() => {
    if (key) {
      ref.current?.focus()
    }
  }, [key])

  if (!shown) {
    return null
  }

  const { result } = shown.created
  // The first effect starts by naming the file, which the line above names already; the rest of it (the key, the chapters) stays.
  const lead = `Created ${result.path}: `
  const effects = result.effects.map((e, i) => (i === 0 && e.startsWith(lead) ? e.charAt(lead.length).toUpperCase() + e.slice(lead.length + 1) : e))
  return (
    <div className="save-result created-notice" role="status" tabIndex={-1} ref={ref}>
      <p>
        Created <code>{result.path}</code>.
      </p>
      <ul>
        {effects.map((e) => (
          <li key={e}>{e}</li>
        ))}
      </ul>
      {result.warnings.length > 0 && (
        <ul className="warnings">
          {result.warnings.map((w) => (
            <li key={`${w.path}:${w.rule}:${w.line ?? ''}:${w.message}`}>
              {w.rule}: {w.message}
            </li>
          ))}
        </ul>
      )}
      <p className="editor-actions">
        <button type="button" onClick={shown.dismiss}>
          Dismiss
        </button>
      </p>
    </div>
  )
}

/** The most chapters a new book can start with, as the server allows. */
const MAX_CHAPTERS = 1000

/** The Place after… value for "At the start"; a key never holds a colon. */
const FIRST = ':first'

/** The New series or New book form: the name, where it goes in story order, and a book's chapters. */
function NewContainerDialog({
  universe,
  request,
  onClose,
  onCreated,
}: {
  universe: string
  request: Request
  onClose: () => void
  onCreated: (result: ContainerCreateResult) => void
}) {
  const { kind, series, inName } = request
  const titleId = useId()
  const descriptionId = useId()
  const nameId = useId()
  const afterId = useId()
  const chaptersId = useId()
  const nameErrorId = useId()
  const chaptersErrorId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const nameRef = useRef<HTMLInputElement>(null)
  const chaptersRef = useRef<HTMLInputElement>(null)
  const afterSave = useAfterSave()
  const client = useQueryClient()
  const navigate = useNavigate()
  const works = useQuery(worksQuery(universe, series))

  const [name, setName] = useState('')
  const [missing, setMissing] = useState(false)
  const [after, setAfter] = useState('')
  const [chapters, setChapters] = useState('')
  const [badChapters, setBadChapters] = useState(false)

  useEffect(() => {
    nameRef.current?.focus()
  }, [])

  const create = useMutation({
    mutationFn: ({ hash, count }: { hash: string; count: number | undefined }) =>
      createContainer(kind, {
        name: name.trim(),
        universe,
        series,
        after: after && after !== FIRST ? after : undefined,
        first: after === FIRST || undefined,
        chapters: count,
        expectedHash: hash,
      }),
    onSuccess: async (result) => {
      // Everything is asked again, as after any save, and waited for, so the new page finds its series or book.
      await afterSave()
      onCreated(result)
      if (result.kind === 'series') {
        void navigate({ to: '/u/$universe/s/$series', params: { universe, series: result.key } })
      } else {
        void navigate({ to: '/u/$universe/b/$book', params: { universe, book: result.key } })
      }
    },
    onError: async () => {
      // The file may have changed: ask again, so a retry is based on it as it is now.
      await client.invalidateQueries({ queryKey: worksQuery(universe, series).queryKey })
    },
  })

  const busy = create.isPending

  useEffect(() => {
    if (busy) {
      dialogRef.current?.focus()
    }
  }, [busy])

  useEffect(() => {
    if (create.isError) {
      nameRef.current?.focus()
    }
  }, [create.isError])

  // A place that's gone since the form opened falls back to the end.
  const list = works.data?.works ?? []
  useEffect(() => {
    if (after && after !== FIRST && works.data && !works.data.works.some((w) => w.key === after)) {
      setAfter('')
    }
  }, [after, works.data])

  const changed = () => {
    if (create.isError) {
      create.reset()
    }
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (name.trim() === '') {
      setMissing(true)
      nameRef.current?.focus()
      return
    }

    setMissing(false)
    let count: number | undefined
    if (kind === 'book' && chaptersRef.current?.validity.badInput) {
      // Text a number field can't read ("e", "--") reads as empty: refused, not taken as no chapters.
      setBadChapters(true)
      chaptersRef.current.focus()
      return
    }

    if (kind === 'book' && chapters.trim() !== '') {
      const n = Number(chapters.trim())
      if (!Number.isInteger(n) || n < 1 || n > MAX_CHAPTERS) {
        setBadChapters(true)
        chaptersRef.current?.focus()
        return
      }

      count = n
    }

    setBadChapters(false)
    if (create.isPending || !works.data) {
      return
    }

    create.mutate({ hash: works.data.hash, count })
  }

  const dismiss = () => {
    if (!busy) {
      onClose()
    }
  }

  const what = kind === 'series' ? 'series' : 'book'
  const listName = series ? 'books' : 'series and books'
  const error = create.error
  return (
    <div className="dialog-backdrop" onMouseDown={(e) => e.target === e.currentTarget && dismiss()}>
      <div
        className="dialog new-entity-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        aria-busy={busy}
        ref={dialogRef}
        tabIndex={-1}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            e.stopPropagation()
            dismiss()
          } else if (e.key === 'Tab') {
            trapTab(e, dialogRef.current)
          }
        }}
      >
        <form onSubmit={submit} noValidate>
          <h2 id={titleId}>
            New {what} in {inName}
          </h2>
          <p id={descriptionId} className="muted">
            Wolfden makes the {what}'s folder, named from its name, and its {what}.md, adds it to {inName}'s story order, then opens it.
          </p>
          {works.isError && (
            <p className="error" role="alert">
              {works.error.message}
            </p>
          )}
          <label htmlFor={nameId} className="new-entity-label">
            Name
          </label>
          <input
            id={nameId}
            ref={nameRef}
            type="text"
            autoComplete="off"
            required
            aria-required="true"
            aria-invalid={missing}
            aria-describedby={missing ? nameErrorId : undefined}
            value={name}
            disabled={busy}
            onChange={(e) => {
              setName(e.currentTarget.value)
              changed()
              if (e.currentTarget.value.trim() !== '') {
                setMissing(false)
              }
            }}
          />
          {missing && (
            <p id={nameErrorId} className="field-error" role="alert">
              Give the {what} a name. Its folder is named from it.
            </p>
          )}
          {list.length > 0 ? (
            <>
              <label htmlFor={afterId} className="new-entity-label">
                Place after…
              </label>
              <select
                id={afterId}
                value={after}
                disabled={busy}
                onChange={(e) => {
                  setAfter(e.currentTarget.value)
                  changed()
                }}
              >
                <option value={FIRST}>At the start, before {list[0].name}</option>
                <option value="">At the end, after {list[list.length - 1].name}</option>
                {list.map((w) => (
                  <option key={`${w.kind}:${w.key}`} value={w.key}>
                    {w.name}
                    {series ? '' : ` (${w.kind})`}
                  </option>
                ))}
              </select>
            </>
          ) : (
            works.isSuccess && (
              <p className="muted new-entity-hint">
                {inName} has no {listName} yet, so this is the first.
              </p>
            )
          )}
          {kind === 'book' && (
            <>
              <label htmlFor={chaptersId} className="new-entity-label">
                Number of chapters
              </label>
              <input
                id={chaptersId}
                ref={chaptersRef}
                type="number"
                inputMode="numeric"
                min={1}
                max={MAX_CHAPTERS}
                step={1}
                value={chapters}
                disabled={busy}
                aria-invalid={badChapters}
                aria-describedby={badChapters ? chaptersErrorId : undefined}
                onChange={(e) => {
                  setChapters(e.currentTarget.value)
                  setBadChapters(false)
                  changed()
                }}
              />
              {badChapters ? (
                <p id={chaptersErrorId} className="field-error" role="alert">
                  Give a whole number from 1 to {MAX_CHAPTERS}, or leave it empty for a book with no chapters yet.
                </p>
              ) : (
                <p className="muted new-entity-hint">Optional. Chapters 1 to N, numbered 1 to N; leave it empty for none yet.</p>
              )}
            </>
          )}
          {error && (
            <div className="error" role="alert">
              <p>{refusal(error, what)}</p>
            </div>
          )}
          <div className="dialog-actions">
            <button type="submit" className="primary" disabled={busy || !works.isSuccess}>
              {busy ? 'Creating…' : 'Create'}
            </button>
            <button type="button" onClick={onClose} disabled={busy}>
              Cancel
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

/** Why the create was refused, in the form's words where the server's are for agents. */
function refusal(error: Error, what: string): string {
  if (error instanceof ApiRequestError && error.code === 'stale_file') {
    return `The story order changed in another program since this form opened, so the ${what} wasn't created. The form has it as it is now: check where it goes, and create it again.`
  }

  return error.message
}

import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiRequestError, positionsQuery } from '../api'
import { bookChapterFieldsQuery, chapterDefaultsQuery, defaultsWords } from '../chapterFieldsApi'
import { addChapter, bookChaptersQuery, type BookChapters, type ChapterAddResult } from '../containerApi'
import type { SourceFieldType } from '../editApi'
import { fieldDraftOf, fieldProblems, jsonOfField, type FieldDraft, type Json, type Problem } from '../fieldModel'
import { valueWords } from './ChapterFields'
import { useAfterSave } from './Editing'
import { ValueEditor } from './FieldEditing'
import { trapTab } from './NewEntity'
import '../newEntity.css'

/**
 * Adding a chapter on a book's page (#215; maintainer decisions on #215,
 * 2026-10-29). **Add chapter** puts one at the end, and **Insert
 * chapter…** after a chapter the writer picks; the form asks for an
 * optional **Title**. Wolfden writes the item into book.md's `chapters`:
 * at the end the next whole number, after a chapter a letter added to its
 * key (`2b`, then `2c`; after `2b`, `2ba`), and the numbers after it move
 * up. Keys never change, so positions keep naming the same chapters. The
 * form also takes the chapter's own chapter fields (#225), such as its POV
 * and when it takes place; one left empty is inherited. A
 * final book has neither button: its book.md can't change until it's
 * unfrozen.
 */

/** The longest title the server takes, as for a name. */
const MAX_TITLE = 200

/** A chapter as the Place list names it: its number and title, and its key. */
function chapterLabel(c: BookChapters['chapters'][number]): string {
  const name = c.title ?? `Chapter ${c.key}`
  return `${c.number !== null ? `${c.number}. ` : ''}${name} (key ${c.key})`
}

/** The book page's Add chapter and Insert chapter… buttons, the form, and what the last add did. */
export function AddChapter({ universe, book, bookName, chapters, final }: { universe: string; book: string; bookName: string; chapters: number; final: boolean }) {
  const [mode, setMode] = useState<'end' | 'insert' | null>(null)
  const [added, setAdded] = useState<ChapterAddResult | null>(null)
  const returnFocus = useRef<HTMLElement | null>(null)
  const noticeRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (added) {
      noticeRef.current?.focus()
    }
  }, [added])

  const open = (next: 'end' | 'insert') => {
    returnFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    setAdded(null)
    setMode(next)
  }

  const close = () => {
    setMode(null)
    const target = returnFocus.current
    requestAnimationFrame(() => target?.focus())
  }

  return (
    <>
      {added && (
        <div className="save-result created-notice" role="status" tabIndex={-1} ref={noticeRef}>
          <p>
            Added chapter <code>{added.position}</code> to <code>{added.path}</code>.
          </p>
          <ul>
            {added.effects.map((e) => (
              <li key={e}>{e}</li>
            ))}
          </ul>
          {added.warnings.length > 0 && (
            <ul className="warnings">
              {added.warnings.map((w) => (
                <li key={`${w.path}:${w.rule}:${w.line ?? ''}:${w.message}`}>
                  {w.rule}: {w.message}
                </li>
              ))}
            </ul>
          )}
          <p className="editor-actions">
            <button type="button" onClick={() => setAdded(null)}>
              Dismiss
            </button>
          </p>
        </div>
      )}
      {!final && (
        <p className="editor-actions">
          <button type="button" aria-label={`Add chapter to ${bookName}`} onClick={() => open('end')}>
            Add chapter
          </button>
          {chapters >= 2 && (
            <button type="button" aria-label={`Insert chapter in ${bookName}`} onClick={() => open('insert')}>
              Insert chapter…
            </button>
          )}
        </p>
      )}
      {mode && (
        <AddChapterDialog
          universe={universe}
          book={book}
          bookName={bookName}
          insert={mode === 'insert'}
          onClose={close}
          onAdded={(result) => {
            setMode(null)
            setAdded(result)
          }}
        />
      )}
    </>
  )
}

/** The form: where the chapter goes, and its title. */
function AddChapterDialog({
  universe,
  book,
  bookName,
  insert,
  onClose,
  onAdded,
}: {
  universe: string
  book: string
  bookName: string
  insert: boolean
  onClose: () => void
  onAdded: (result: ChapterAddResult) => void
}) {
  const titleId = useId()
  const descriptionId = useId()
  const placeId = useId()
  const nameId = useId()
  const nameErrorId = useId()
  const hintId = useId()
  const goneId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const nameRef = useRef<HTMLInputElement>(null)
  const placeRef = useRef<HTMLSelectElement>(null)
  const afterSave = useAfterSave()
  const client = useQueryClient()
  const current = useQuery(bookChaptersQuery(universe, book))

  const [title, setTitle] = useState('')
  const fieldsInfo = useQuery(bookChapterFieldsQuery(universe, book))
  const defaults = useQuery(chapterDefaultsQuery({ universe, book }))
  const positions = useQuery(positionsQuery(universe))
  const fieldBase = useId().replace(/[^a-zA-Z0-9]/g, '')
  // Each field's form, made once, so its controls keep their ids (and focus) as the writer types.
  const [drafts, setDrafts] = useState<Record<string, FieldDraft>>({})
  const [fieldProblemList, setFieldProblemList] = useState<Problem[]>([])
  const fieldTypes = fieldsInfo.data?.fieldTypes ?? NO_TYPES
  useEffect(() => {
    const missing = fieldTypes.filter((t) => !(t.name in drafts))
    if (missing.length > 0) {
      setDrafts((d) => ({ ...d, ...Object.fromEntries(missing.map((t) => [t.name, fieldDraftOf(t, undefined) as FieldDraft])) }))
    }
  }, [fieldTypes, drafts])

  const [tooLong, setTooLong] = useState(false)
  // '' is the end; otherwise the key of the chapter it goes after. Insert starts at the first chapter.
  const [after, setAfter] = useState<string | null>(insert ? null : '')

  const list = current.data?.chapters ?? []
  const choices = list.slice(0, -1)
  const place = after ?? choices[0]?.key ?? ''

  useEffect(() => {
    ;(insert ? placeRef.current : nameRef.current)?.focus()
  }, [insert, current.isSuccess])

  // A chapter that's gone since the form opened (or is the last now) falls back to the end, and the form says so.
  const [gone, setGone] = useState<string | null>(null)
  useEffect(() => {
    if (after && current.data && !choices.some((c) => c.key === after)) {
      setGone(after)
      setAfter('')
    }
  }, [after, current.data, choices])

  const add = useMutation({
    mutationFn: ({ hash, fields }: { hash: string; fields: Record<string, Json> | undefined }) =>
      addChapter({ universe, book, title: title.trim() === '' ? undefined : title.trim(), after: place === '' ? undefined : place, fields, expectedHash: hash }),
    onSuccess: async (result) => {
      // Everything is asked again, as after any save, so the chapter list and the positions have the new one.
      await afterSave()
      onAdded(result)
    },
    onError: async () => {
      // book.md may have changed: ask again, so a retry is based on it as it is now.
      await client.invalidateQueries({ queryKey: bookChaptersQuery(universe, book).queryKey })
    },
  })

  const busy = add.isPending

  useEffect(() => {
    if (busy) {
      dialogRef.current?.focus()
    }
  }, [busy])

  useEffect(() => {
    if (add.isError) {
      nameRef.current?.focus()
    }
  }, [add.isError])

  const changed = () => {
    if (add.isError) {
      add.reset()
    }
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (title.trim().length > MAX_TITLE) {
      setTooLong(true)
      nameRef.current?.focus()
      return
    }

    setTooLong(false)
    const { fields, problems } = chapterFields(fieldTypes, drafts, fieldBase)
    setFieldProblemList(problems)
    if (problems.length > 0) {
      document.getElementById(problems[0].at)?.focus()
      return
    }

    if (busy || !current.data) {
      return
    }

    add.mutate({ hash: current.data.hash, fields })
  }

  const dismiss = () => {
    if (!busy) {
      onClose()
    }
  }

  const error = add.error
  const last = list[list.length - 1]
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
            {insert ? 'Insert a chapter in' : 'Add a chapter to'} {bookName}
          </h2>
          <p id={descriptionId} className="muted">
            Wolfden adds it to the chapters in book.md. At the end it gets the next whole number as its key; after a chapter, that chapter's key with a
            letter added, such as 2b. Keys never change, so the numbers shown after it move up instead.
          </p>
          {current.isError && (
            <p className="error" role="alert">
              {current.error.message}
            </p>
          )}
          {list.length > 0 ? (
            <>
              <label htmlFor={placeId} className="new-entity-label">
                Place
              </label>
              <select
                id={placeId}
                ref={placeRef}
                value={place}
                disabled={busy}
                aria-describedby={gone ? goneId : undefined}
                onChange={(e) => {
                  setAfter(e.currentTarget.value)
                  setGone(null)
                  changed()
                }}
              >
                <option value="">At the end, after {chapterLabel(last)}</option>
                {choices.map((c) => (
                  <option key={c.key} value={c.key}>
                    After {chapterLabel(c)}
                  </option>
                ))}
              </select>
              {gone && (
                <p id={goneId} className="field-error" role="status">
                  Chapter {gone} can't be chosen now (book.md changed: it's gone, or it's the last chapter), so the new chapter goes at the end. Choose
                  another place if that's not where it belongs.
                </p>
              )}
            </>
          ) : (
            current.isSuccess && <p className="muted new-entity-hint">{bookName} has no chapters yet, so this is chapter 1.</p>
          )}
          <label htmlFor={nameId} className="new-entity-label">
            Title
          </label>
          <input
            id={nameId}
            ref={nameRef}
            type="text"
            autoComplete="off"
            aria-invalid={tooLong}
            aria-describedby={tooLong ? nameErrorId : hintId}
            value={title}
            disabled={busy}
            onChange={(e) => {
              setTitle(e.currentTarget.value)
              setTooLong(false)
              changed()
            }}
          />
          {tooLong ? (
            <p id={nameErrorId} className="field-error" role="alert">
              A title is at most {MAX_TITLE} characters.
            </p>
          ) : (
            <p id={hintId} className="muted new-entity-hint">Optional; leave it empty for none. You can change it in book.md later.</p>
          )}
          {fieldTypes.length > 0 && (
            <fieldset className="new-chapter-fields" disabled={busy}>
              <legend>Chapter fields</legend>
              <p className="muted new-entity-hint">Optional. A field left empty is inherited from the chapter defaults, as the book's other chapters inherit it.</p>
              {fieldTypes.map((t) => {
                const draft = drafts[t.name]
                if (!draft) {
                  return null
                }

                const inherited = defaults.data?.fields.find((f) => f.name === t.name)
                return (
                  <div key={t.name} className="new-chapter-field" role="group" aria-label={t.name}>
                    <p className="field-about">
                      <strong>{t.name}</strong>
                      {t.description && <span className="muted"> {t.description}</span>}
                    </p>
                    <ValueEditor
                      idBase={`${fieldBase}${t.name}`}
                      type={t}
                      entry={draft.entries[0]}
                      positions={positions.data?.positions ?? []}
                      problems={fieldProblemList}
                      universe={universe}
                      onChange={(value) => {
                        setDrafts((d) => ({ ...d, [t.name]: { ...draft, entries: [{ ...draft.entries[0], value }] } }))
                        changed()
                      }}
                    />
                    <p className="muted new-entity-hint">
                      {inherited && inherited.value !== null && inherited.from && inherited.from !== 'chapter'
                        ? `Left empty: ${valueWords(inherited.value)}, from ${defaultsWords(inherited.from, inherited.fromKey)}.`
                        : 'Left empty: none.'}
                    </p>
                  </div>
                )
              })}
            </fieldset>
          )}
          {error && (
            <div className="error" role="alert">
              <p>{refusal(error)}</p>
            </div>
          )}
          <div className="dialog-actions">
            <button type="submit" className="primary" disabled={busy || !current.isSuccess}>
              {busy ? 'Adding…' : 'Add'}
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

const NO_TYPES: SourceFieldType[] = []

/**
 * The chapter fields the writer filled in, as JSON to send, and what to fix
 * first. A field left empty (no text, or a list with no items) isn't sent,
 * so the new chapter inherits it.
 */
function chapterFields(types: SourceFieldType[], drafts: Record<string, FieldDraft>, base: string): { fields: Record<string, Json> | undefined; problems: Problem[] } {
  const fields: Record<string, Json> = {}
  const problems: Problem[] = []
  for (const t of types) {
    const draft = drafts[t.name]
    const value = draft ? jsonOfField(t, draft).value : undefined
    if (value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0)) {
      continue
    }

    problems.push(...fieldProblems(t, draft, `${base}${t.name}`, { clear: 'leave it empty' }))
    fields[t.name] = value
  }

  return { fields: Object.keys(fields).length > 0 ? fields : undefined, problems }
}

/** Why the add was refused, in the form's words where the server's are for agents. */
function refusal(error: Error): string {
  if (error instanceof ApiRequestError && error.code === 'stale_file') {
    return "The book's chapters changed in another program since this form opened, so the chapter wasn't added. The form has them as they are now: check where it goes, and add it again."
  }

  return error.message
}

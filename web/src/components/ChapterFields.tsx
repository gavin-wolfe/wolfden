import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiRequestError, positionsQuery, type PositionView } from '../api'
import {
  bookChapterFieldsQuery,
  chapterDefaultsQuery,
  defaultsWords,
  editChapterDefault,
  editChapterField,
  linkViews,
  type ChapterFieldsView,
  type ChapterValueView,
  type DefaultsScope,
  type ValueFrom,
} from '../chapterFieldsApi'
import type { SourceFieldType, WriteResult } from '../editApi'
import {
  chapterFieldChanges,
  controlId,
  fieldDraftOf,
  fieldProblems,
  isPlainValue,
  jsonOfField,
  valueLines,
  type ChapterFieldChange,
  type FieldDraft,
  type Json,
  type Problem,
} from '../fieldModel'
import { FieldValue } from '../pages/EntityPageView'
import { Conflict, useAfterSave } from './Editing'
import { ValueEditor, typeWords } from './FieldEditing'
import { ErrorState, Loading } from './States'

// Chapter fields in the web UI (#225; types.md §8): each chapter's fields,
// and the chapter_defaults of a universe, series, or book, shown with where
// each value comes from, and edited one field at a time with the same
// controls as a note's fields (#196). A list emptied in the form is saved as
// [], "none here" (#132); going back to the default is its own action. The
// fields come from the den's chapter field definitions, so a field a pack
// adds gets its form like the core pack's.

/** A value as a note's field shows it: a number as its text. */
function shown(v: Json | null): unknown {
  if (isPlainValue(v)) {
    return v.$plain
  }

  if (Array.isArray(v)) {
    return v.map(shown)
  }

  if (v !== null && typeof v === 'object') {
    return Object.fromEntries(Object.entries(v).map(([k, x]) => [k, shown(x)]))
  }

  return v
}

/** A value in words for a hint: its items as written, or "none". */
export function valueWords(v: Json | null): string {
  if (Array.isArray(v)) {
    return v.length === 0 ? 'none ([])' : v.map((x) => valueWords(x)).join(', ')
  }

  if (isPlainValue(v)) {
    return v.$plain
  }

  if (v !== null && typeof v === 'object') {
    return typeof v.text === 'string' ? v.text : valueLines(v)
  }

  return v === null ? '' : String(v)
}

/** The value that holds: with its links, "none" for [], or "not set". */
export function ChapterValueText({ universe, view, none = 'none here', met }: { universe: string; view: ChapterValueView; none?: string; met?: ReadonlySet<string> }) {
  if (view.value === null) {
    return <span className="muted">not set</span>
  }

  if (view.none) {
    return (
      <span>
        none <span className="muted">({none}, [])</span>
      </span>
    )
  }

  return <FieldValue value={shown(view.value)} links={linkViews(view.links, met)} universe={universe} />
}

/** Where an inherited value comes from: "from book 'ember' defaults". Nothing for a value set here. */
function Origin({ view, own, short = false }: { view: ChapterValueView; own: ValueFrom; short?: boolean }) {
  if (view.value === null || view.from === null || view.from === own || view.from === 'chapter') {
    return null
  }

  const words = defaultsWords(view.from, view.fromKey)
  return (
    <span className="badge inherited" title={`Inherited from the chapter_defaults of ${view.from} '${view.fromKey}'`}>
      {short ? `${view.from} default` : `inherited from ${words}`}
    </span>
  )
}

/**
 * A chapter's fields under its title in the book's chapter list: what holds,
 * inherited values marked. As of a story position, `met` holds the notes the
 * reader has met, and a link to any other is plain text that doesn't say
 * what it reaches, as links are in the rest of the reading view (#26).
 */
export function ChapterFieldSummary({ universe, chapter, met }: { universe: string; chapter: ChapterFieldsView; met?: ReadonlySet<string> }) {
  const shownFields = chapter.fields.filter((f) => f.value !== null)
  if (shownFields.length === 0) {
    return null
  }

  return (
    <dl className="chapter-field-summary">
      {shownFields.map((f) => (
        <div key={f.name} className={f.from === 'chapter' ? undefined : 'inherited-value'}>
          <dt>{f.name}</dt>
          <dd>
            <ChapterValueText universe={universe} view={f} met={met} /> <Origin view={f} own="chapter" short />
          </dd>
        </div>
      ))}
    </dl>
  )
}

/** How a panel's edits are made: the chapter's fields, or a container's defaults. */
interface Target {
  universe: string
  /** What a value set here is: the chapter's own, or this container's default. */
  own: ValueFrom
  /** What the panel calls the place, in the effects' words: "chapter 2", "book 'ember'". */
  where: string
  /** The action that goes back to inheriting: "Use the default" for a chapter, "Clear" for a container. */
  clear: string
  /** What an emptied list means here. */
  noneWords: string
  /** The same, short, for the choice beside an emptied list: "none in this chapter instead". */
  noneChoice: string
  save: (field: string, changes: ChapterFieldChange[], hash: string) => Promise<WriteResult>
  /** The file and this panel's values as they are now, after a stale save, or null if the place is gone. */
  reload: () => Promise<{ hash: string; values: ChapterValueView[] } | null>
}

/** A field's edit button's id, where focus goes back when its editor closes. */
function editId(field: string): string {
  return `edit-chapter-field-${field}`
}

/**
 * Each chapter field with what holds and where it comes from, an Edit for
 * each the den's packs define, and, for one set here, a way back to the
 * default that says first what that will be.
 */
function ChapterValues({
  target,
  fieldTypes,
  values,
  hash,
  locked,
  label,
}: {
  target: Target
  fieldTypes: SourceFieldType[]
  values: ChapterValueView[]
  hash: string
  /** Why nothing here can be edited, or null. */
  locked: string | null
  label: string
}) {
  const positions = useQuery(positionsQuery(target.universe))
  const afterSave = useAfterSave()
  const [editing, setEditing] = useState<string | null>(null)
  // The field whose Use the default (or Clear) is being asked about, as it was when the writer asked.
  const [clearing, setClearing] = useState<{ name: string; hash: string; view: ChapterValueView } | null>(null)
  // A stale clear: the field as the file has it now, to choose between.
  const [clearConflict, setClearConflict] = useState<{ name: string; hash: string; theirs: ChapterValueView | null } | null>(null)
  // A stale clear found the field already gone from the file.
  const [alreadyClear, setAlreadyClear] = useState<string | null>(null)
  const [saved, setSaved] = useState<{ field: string; result: WriteResult } | null>(null)
  const [error, setError] = useState<Error | null>(null)
  const [busy, setBusy] = useState(false)
  const notice = useRef<HTMLDivElement>(null)
  const returnTo = useRef<string | null>(null)

  useEffect(() => {
    if (saved) {
      notice.current?.focus()
    } else if (editing === null && clearing === null && clearConflict === null && returnTo.current) {
      document.getElementById(returnTo.current)?.focus()
      returnTo.current = null
    }
  }, [saved, editing, clearing, clearConflict])

  const done = async (field: string, result: WriteResult | null) => {
    returnTo.current = editId(field)
    setEditing(null)
    setClearing(null)
    setClearConflict(null)
    if (result) {
      await afterSave()
      setSaved({ field, result })
    }
  }

  /**
   * Clears the field on the file whose hash is `on`. If the file changed
   * meanwhile, it's never cleared unseen (#196): the writer sees what the
   * file has now and chooses, or is told it's already cleared.
   */
  const clear = async (name: string, on: string) => {
    setBusy(true)
    setError(null)
    try {
      await done(name, await target.save(name, [{ op: 'clear' }], on))
    } catch (e) {
      if (e instanceof ApiRequestError && e.code === 'stale_file') {
        const now = await target.reload().catch(() => null)
        if (now) {
          const theirs = now.values.find((v) => v.name === name) ?? null
          setClearing(null)
          if (theirs?.set) {
            setClearConflict({ name, hash: now.hash, theirs })
          } else {
            returnTo.current = editId(name)
            setClearConflict(null)
            setAlreadyClear(name)
            await afterSave()
          }

          return
        }
      }

      setError(e as Error)
    } finally {
      setBusy(false)
    }
  }

  const types = new Map(fieldTypes.map((t) => [t.name, t]))
  return (
    <>
      {saved && (
        <div className="save-result" role="status" tabIndex={-1} ref={notice}>
          <p>
            <strong>{saved.result.changed ? `Saved ${saved.field}.` : `Nothing changed in ${saved.field}: the file already said that.`}</strong> Saving doesn't make a
            checkpoint.
          </p>
          {saved.result.changed && saved.result.effects.length > 0 && (
            <ul className="effects">
              {saved.result.effects.map((e) => (
                <li key={e}>{e}</li>
              ))}
            </ul>
          )}
          {saved.result.warnings.length > 0 && (
            <div className="warnings">
              <p>Warnings in the file:</p>
              <ul>
                {saved.result.warnings.map((w) => (
                  <li key={`${w.line}:${w.rule}:${w.message}`}>
                    {w.line !== null && `Line ${w.line}: `}
                    {w.message} <span className="muted">({w.rule})</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
          <button type="button" onClick={() => setSaved(null)}>
            Dismiss
          </button>
        </div>
      )}
      {alreadyClear && (
        <p className="save-result" role="status">
          {alreadyClear} was already taken out of the file by something else meanwhile, so there was nothing to clear. What it has now is shown below.{' '}
          <button type="button" onClick={() => setAlreadyClear(null)}>
            Dismiss
          </button>
        </p>
      )}
      {locked && <p className="muted">{locked}</p>}
      <div role="group" aria-label={label}>
      <dl className="fields chapter-fields">
        {values.map((view) => {
          const type = types.get(view.name)
          const form = type ? editable(type, view) : null
          return (
            <div key={view.name} className="field">
              <dt>{view.name}</dt>
              {editing === view.name && type ? (
                <dd className="field-editing">
                  <FieldForm
                    target={target}
                    type={type}
                    view={view}
                    hash={hash}
                    positions={positions.data?.positions ?? []}
                    onDone={(result) => void done(view.name, result)}
                  />
                </dd>
              ) : (
                <>
                  <dd className={view.value !== null && view.from !== null && view.from !== target.own && view.from !== 'chapter' ? 'inherited-value' : undefined}>
                    <ChapterValueText universe={target.universe} view={view} none={target.noneWords} /> <Origin view={view} own={target.own} />
                    <IgnoredNote view={view} />
                    {!type && <span className="muted"> (not a chapter field the den's packs define: change it in the file)</span>}
                  </dd>
                  {!locked && type && (
                    <dd className="field-tools">
                      {typeof form === 'string' ? (
                        <span className="muted">{form} Change it in the file.</span>
                      ) : (
                        <button
                          type="button"
                          className="edit-button"
                          id={editId(view.name)}
                          aria-label={`Edit ${view.name}`}
                          disabled={editing !== null || clearConflict !== null || busy}
                          onClick={() => {
                            setSaved(null)
                            setAlreadyClear(null)
                            setError(null)
                            setClearing(null)
                            setEditing(view.name)
                          }}
                        >
                          Edit
                        </button>
                      )}{' '}
                      {view.set && clearing?.name !== view.name && clearConflict?.name !== view.name && (
                        <button
                          type="button"
                          aria-label={`${target.clear}: ${view.name}`}
                          disabled={editing !== null || clearConflict !== null || busy}
                          onClick={() => {
                            setSaved(null)
                            setAlreadyClear(null)
                            setError(null)
                            setClearing({ name: view.name, hash, view })
                          }}
                        >
                          {target.clear}
                        </button>
                      )}
                      {clearing?.name === view.name && (
                        <ClearPrompt
                          target={target}
                          view={clearing.view}
                          busy={busy}
                          onClear={() => void clear(view.name, clearing.hash)}
                          onKeep={() => {
                            returnTo.current = editId(view.name)
                            setClearing(null)
                            setError(null)
                          }}
                        />
                      )}
                      {clearConflict?.name === view.name && (
                        <Conflict
                          title={`The file changed before ${view.name} was cleared`}
                          what={view.name}
                          mine={valueLines(undefined)}
                          theirs={valueLines(clearConflict.theirs?.own ?? null)}
                          saving={busy}
                          onKeepMine={() => void clear(view.name, clearConflict.hash)}
                          onTakeTheirs={() => {
                            returnTo.current = editId(view.name)
                            setClearConflict(null)
                            void afterSave()
                          }}
                        />
                      )}
                      {(clearing?.name === view.name || clearConflict?.name === view.name) && error && <Refused error={error} />}
                    </dd>
                  )}
                </>
              )}
            </div>
          )
        })}
      </dl>
      </div>
    </>
  )
}

/**
 * What a field writes that's ignored, and why (F-CHAP-3, #233): its whole
 * value, or the items of its list that are left out. Null if nothing is; a
 * value written as null isn't a fault, so it's never one of these.
 */
function ignoredWords(view: ChapterValueView): { whole: string | null; items: string | null } | null {
  const whole = view.ignored && view.own !== null ? `written here as ${valueLines(view.own)}, which is ignored: ${view.ignoredReason ?? `it isn't a value of ${view.name}.`}` : null
  const items = view.ignoredItems.length > 0 ? view.ignoredItems.map((i) => `item ${i.item + 1}, ${valueLines(i.asWritten)}, is left out: ${i.reason}`).join(' ') : null
  return whole || items ? { whole, items } : null
}

/** What a field writes that's ignored, and why, so the writer sees it before replacing it. */
function IgnoredNote({ view }: { view: ChapterValueView }) {
  const words = ignoredWords(view)
  if (!words) {
    return null
  }

  return <span className="muted ignored-value"> ({words.whole ?? words.items})</span>
}

/** "written here as …" with its first letter capitalized, to start a sentence. */
function sentence(words: string): string {
  return words.charAt(0).toUpperCase() + words.slice(1)
}

/** The form for a value, or why it can't show it (a dated entry, which a chapter field can't have, or a list where one value goes). */
function editable(type: SourceFieldType, view: ChapterValueView): FieldDraft | string {
  const draft = fieldDraftOf(type, view.set ? (view.own ?? null) : (view.default ?? undefined))
  if (typeof draft === 'string') {
    return draft
  }

  return draft.shape === 'entry' || draft.shape === 'entries' ? "It's written as a dated entry, which a chapter field can't have (types.md F-CHAP-3)." : draft
}

/** Asks before going back to the default, saying what that will be. */
function ClearPrompt({ target, view, busy, onClear, onKeep }: { target: Target; view: ChapterValueView; busy: boolean; onClear: () => void; onKeep: () => void }) {
  const id = useId()
  const outcome =
    view.default === null
      ? `Nothing above sets ${view.name}, so ${target.where} will have none.`
      : `${capitalized(target.where)} will have ${valueWords(view.default)}, from ${defaultsWords(view.defaultFrom!, view.defaultFromKey)}.`
  return (
    <div className="confirm" role="group" aria-labelledby={id}>
      <p id={id}>
        {target.clear} for {view.name}? It's taken out of the file. {outcome}
      </p>
      <p className="editor-actions">
        <button type="button" className="primary" disabled={busy} onClick={onClear}>
          {busy ? 'Saving…' : target.clear}
        </button>
        <button type="button" autoFocus onClick={onKeep} disabled={busy}>
          Keep it
        </button>
      </p>
    </div>
  )
}

function capitalized(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1)
}

/** Why a save was refused, in the page's words for a stale file; the server's words otherwise, which name Unfreeze… for a final book. */
function Refused({ error }: { error: Error }) {
  const code = error instanceof ApiRequestError ? error.code : null
  return (
    <div className="error" role="alert">
      <p>
        {code === 'frozen' ? "It can't be saved: " : "It wasn't saved: "}
        {error.message}
      </p>
    </div>
  )
}

/**
 * Edits one chapter field, or one default, with the control for its type.
 * It starts from the value set here, or, where nothing is, from what's
 * inherited, so a change to it becomes the value set here. Save (or
 * Ctrl/⌘+Enter) sends only what changed; Cancel (or Escape) leaves it.
 */
function FieldForm({
  target,
  type,
  view,
  hash,
  positions,
  onDone,
}: {
  target: Target
  type: SourceFieldType
  view: ChapterValueView
  hash: string
  positions: PositionView[]
  onDone: (result: WriteResult | null) => void
}) {
  const idBase = `c${useId().replace(/[^a-zA-Z0-9]/g, '')}`
  // What the form was opened on, kept while it's open: a refetch (say, on window focus) mustn't move it onto a
  // file the writer never saw, so the changes, the stale check, and the first send all use this.
  const [opened] = useState(() => ({ hash, set: view.set, own: view.own, text: view.text, default: view.default, defaultFrom: view.defaultFrom }))
  const [draft, setDraft] = useState(() => editable(type, view) as FieldDraft)
  const [initial] = useState(() => JSON.stringify(draft))
  const [problems, setProblems] = useState<Problem[]>([])
  const [error, setError] = useState<Error | null>(null)
  const [saving, setSaving] = useState(false)
  const [conflict, setConflict] = useState<{ hash: string; theirs: ChapterValueView | null } | null>(null)
  // With the list emptied, go back to inheriting (the field taken out) instead of saving [] (#138).
  const [inherit, setInherit] = useState(false)
  const client = useQueryClient()
  const entry = draft.entries[0]
  const before = opened.set ? (opened.own ?? null) : undefined
  const v = entry.value
  const emptyList = v.kind === 'list' && v.items.length === 0
  // What it would inherit without its own value, as the form was opened on, if that's something: then an emptied list offers it (#138).
  const inheritable = opened.default !== null && !(Array.isArray(opened.default) && opened.default.length === 0) ? opened.default : null
  const inheriting = inherit && emptyList && inheritable !== null
  // Choosing the default is a change too, even where the list was already empty (own []).
  const dirty = JSON.stringify(draft) !== initial || inheriting

  // Focus goes to the first control when the form opens.
  useEffect(() => {
    const first = draft.entries[0]
    const v = first.value
    document.getElementById(controlId(idBase, first, v.kind === 'list' && v.items.length === 0 ? 'add-item' : 'value', v.kind === 'list' ? v.items[0] : undefined))?.focus()
    // Only when it opens.
  }, [])

  /** Sends the changes on the file whose hash is `on`, where the field's lines were `basis` as the writer last saw them. */
  const send = async (on: string, changes: ChapterFieldChange[], basis: string) => {
    setSaving(true)
    setError(null)
    try {
      for (let attempt = 0; ; attempt++) {
        try {
          onDone(await target.save(view.name, changes, on))
          return
        } catch (e) {
          if (!(e instanceof ApiRequestError && e.code === 'stale_file')) {
            throw e
          }

          // The file changed meanwhile. Sent again only where nothing of the field can be overwritten (#196): no change
          // sets or clears it as a whole, and the lines it's written in, comments and quoting included, are as they were.
          const now = await target.reload()
          const theirs = now?.values.find((v) => v.name === view.name) ?? null
          const safe = changes.every((c) => c.op === 'add' || c.op === 'remove' || c.op === 'move' || c.op === 'replace')
          if (now && theirs && safe && theirs.text === basis && attempt < 2) {
            on = now.hash
            continue
          }

          if (!now) {
            throw e
          }

          setConflict({ hash: now.hash, theirs })
          return
        }
      }
    } catch (e) {
      setError(e as Error)
    } finally {
      setSaving(false)
    }
  }

  const save = async () => {
    // Ctrl/⌘+Enter goes by the same rule as the Save button.
    if (saving || !dirty) {
      return
    }

    const found = fieldProblems(type, draft, idBase, { clear: target.clear })
    setProblems(found)
    if (found.length > 0) {
      document.getElementById(found[0].at)?.focus()
      return
    }

    const changes = inheriting ? (opened.set ? [{ op: 'clear' as const }] : []) : chapterFieldChanges(type, before, draft)
    if (changes.length === 0) {
      onDone(null)
      return
    }

    await send(opened.hash, changes, opened.text)
  }

  if (conflict) {
    const after = inheriting ? undefined : jsonOfField(type, draft).value
    return (
      <Conflict
        title={`The file changed while you were editing ${view.name}`}
        what={view.name}
        mine={valueLines(after)}
        theirs={valueLines(conflict.theirs?.set ? (conflict.theirs.own ?? null) : undefined)}
        saving={saving}
        onKeepMine={() => {
          const on = conflict.hash
          setConflict(null)
          void send(on, after === undefined ? [{ op: 'clear' }] : [{ op: 'set', value: after }], conflict.theirs?.text ?? '')
        }}
        onTakeTheirs={() => {
          void client.invalidateQueries()
          onDone(null)
        }}
      />
    )
  }

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault()
      void save()
    } else if (e.key === 'Escape' && !e.defaultPrevented) {
      e.preventDefault()
      onDone(null)
    }
  }

  let start: ReactNode = null
  const ignored = ignoredWords(view)
  const noteId = `${idBase}-ignored`
  if (ignored) {
    // What's written stays in the file until a save replaces it (#233).
    start = (
      <p className="muted" role="note" id={noteId}>
        {ignored.whole ? `${sentence(ignored.whole)} Saving replaces it.` : `${sentence(ignored.items!)} Change or remove it here, or it stays as written.`}
      </p>
    )
  } else if (!view.set && view.default !== null) {
    start = (
      <p className="muted">
        This starts from the value inherited now, from {defaultsWords(view.defaultFrom!, view.defaultFromKey)}. A change you save is set here, in place of it.
      </p>
    )
  }

  return (
    <form
      className="editor-panel inline-form field-editor"
      aria-label={`Edit ${view.name}`}
      aria-describedby={ignored ? noteId : undefined}
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        void save()
      }}
      onKeyDown={onKeyDown}
    >
      <p className="field-about">
        <strong>{view.name}</strong>: {typeWords(type)}
        {type.description && <span className="muted"> {type.description}</span>}
      </p>
      {start}
      <ValueEditor
        idBase={idBase}
        type={type}
        entry={entry}
        positions={positions}
        problems={problems}
        universe={target.universe}
        onChange={(value) => setDraft({ ...draft, entries: [{ ...entry, value }] })}
      />
      {emptyList && inheritable === null && (
        <p className="muted" role="status">
          No items: saving sets {view.name}: [], {target.noneWords}.
        </p>
      )}
      {emptyList && inheritable !== null && (
        <InheritChoice
          name={view.name}
          was={before === undefined ? inheritable : before}
          value={valueWords(inheritable)}
          from={`${opened.defaultFrom} default`}
          none={target.noneWords}
          noneChoice={target.noneChoice}
          inheriting={inheriting}
          onToggle={() => setInherit(!inheriting)}
        />
      )}
      {problems.length > 0 && (
        <ul className="problems" role="alert">
          {problems.map((p) => (
            <li key={`${p.at}:${p.message}`}>{p.message}</li>
          ))}
        </ul>
      )}
      {error && <Refused error={error} />}
      <p className="editor-actions">
        <button type="submit" className="primary" disabled={saving || !dirty}>
          {saving ? 'Saving…' : 'Save'}
        </button>
        <button type="button" onClick={() => onDone(null)} disabled={saving}>
          Cancel
        </button>
        <span className="muted">Ctrl+Enter (⌘+Enter) saves, Escape cancels.</span>
      </p>
    </form>
  )
}

/**
 * An emptied list's outcome, shown before saving, with the other choice a
 * click away (#138): "pov: ~~Mara~~ → none in this chapter · use book
 * default (Voss) instead". Saving [] ("none here", #132) is the default;
 * the other takes the field out, so it inherits.
 */
function InheritChoice({
  name,
  was,
  value,
  from,
  none,
  noneChoice,
  inheriting,
  onToggle,
}: {
  name: string
  was: Json | null
  /** The inherited value in words, and where it's from: "book default". */
  value: string
  from: string
  none: string
  noneChoice: string
  inheriting: boolean
  onToggle: () => void
}) {
  const wasShown = Array.isArray(was) && was.length === 0 ? null : valueWords(was)
  return (
    <p className="inherit-choice">
      <span role="status">
        {name}:{' '}
        {wasShown && (
          <>
            <del>{wasShown}</del> →{' '}
          </>
        )}
        <strong>{inheriting ? `${value} (${from})` : none}</strong>
        <span className="visually-hidden">{inheriting ? `. Saving takes ${name} out, so it inherits.` : `. Saving sets ${name}: [].`}</span>
      </span>{' '}
      ·{' '}
      <button type="button" className="link-button" onClick={onToggle}>
        {inheriting ? `${noneChoice} instead` : `use ${from} (${value}) instead`}
      </button>
    </p>
  )
}

/** A chapter's fields on its page, with what each inherits, editable unless the book is final. */
export function ChapterFieldsSection({ universe, book, chapter, label }: { universe: string; book: string; chapter: string; label: string }) {
  const fields = useQuery(bookChapterFieldsQuery(universe, book))
  const client = useQueryClient()
  if (fields.isPending) {
    return <Loading what="the chapter's fields" />
  }

  if (fields.isError) {
    return <ErrorState error={fields.error} />
  }

  const data = fields.data
  const c = data.chapters.find((x) => x.key === chapter)
  if (!c) {
    return null
  }

  const locked = data.final
    ? "The book is final (frozen), so its chapters' fields can't be edited until it's unfrozen: use Unfreeze… on the book's page."
    : c.bare
      ? `book.md writes this chapter as a bare key (${c.key}), which has no place for fields. To give it fields, write it as { key: ${c.key} } in book.md first.`
      : null
  const target: Target = {
    universe,
    own: 'chapter',
    where: label,
    clear: 'Use the default',
    noneWords: 'none in this chapter',
    noneChoice: 'none in this chapter',
    save: (field, changes, expectedHash) => editChapterField({ universe, book, chapter, field, changes, expectedHash }),
    reload: async () => {
      const now = await client.fetchQuery({ ...bookChapterFieldsQuery(universe, book), staleTime: 0 })
      const here = now.chapters.find((x) => x.key === chapter)
      return here ? { hash: now.hash, values: here.fields } : null
    },
  }
  return <ChapterValues target={target} fieldTypes={data.fieldTypes} values={c.fields} hash={data.hash} locked={locked} label={`Fields of ${label}`} />
}

/** A universe's, series', or book's chapter_defaults: what the chapters below have unless they set their own. */
export function ChapterDefaultsSection({ scope, name }: { scope: DefaultsScope; name: string }) {
  const defaults = useQuery(chapterDefaultsQuery(scope))
  const client = useQueryClient()
  const headingId = useId()
  if (defaults.isPending) {
    return null
  }

  if (defaults.isError) {
    return <p className="muted">Chapter defaults aren't available: {defaults.error.message}</p>
  }

  const data = defaults.data
  if (data.fieldTypes.length === 0 && data.fields.length === 0) {
    // The den's packs define no chapter fields.
    return null
  }

  const where = `${data.kind} '${data.key}'`
  const target: Target = {
    universe: scope.universe,
    own: data.kind,
    where: 'the chapters below that set none of their own',
    clear: 'Clear',
    noneWords: 'none, for the chapters below that set none of their own',
    noneChoice: 'none here',
    save: (field, changes, expectedHash) => editChapterDefault({ ...scope, field, changes, expectedHash }),
    reload: async () => {
      const now = await client.fetchQuery({ ...chapterDefaultsQuery(scope), staleTime: 0 })
      return { hash: now.hash, values: now.fields }
    },
  }
  const reach =
    data.kind === 'book'
      ? `Each chapter of ${name} has these unless it sets its own.`
      : `Each chapter of the books ${data.kind === 'series' ? 'in' : 'of'} ${name} has these unless its book, ${data.kind === 'universe' ? 'its series, ' : ''}or the chapter itself, sets its own.`
  return (
    <section aria-labelledby={headingId} className="chapter-defaults">
      <h2 id={headingId}>Chapter defaults</h2>
      <p className="muted">
        {reach} They're the <code>chapter_defaults</code> in <code>{data.path}</code>.
      </p>
      <ChapterValues
        target={target}
        fieldTypes={data.fieldTypes}
        values={data.fields}
        hash={data.hash}
        locked={data.final ? "The book is final (frozen), so its defaults can't be edited until it's unfrozen: use Unfreeze… above." : null}
        label={`Chapter defaults of ${where}`}
      />
    </section>
  )
}

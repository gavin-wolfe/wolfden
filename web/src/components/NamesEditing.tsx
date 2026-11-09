import { Fragment, useEffect, useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { EntityPage, PositionView, ReaderMark } from '../api'
import { EditError, postEdit, sourceQuery, type EditPath } from '../editApi'
import { placeWords } from '../story'
import { LaterNameTag } from './AheadOfReaders'
import { DiscardPrompt, SaveError, useAfterSave, useEditing } from './Editing'
import { PositionInput } from './FieldEditing'

// A note's other names (#67): its plain aliases, which readers know wherever they
// know the note, and its `known_as` names, which readers learn at a `from`. The page
// lists them under the title; the Author's view marks a name readers there haven't
// learned. Other names, beside Edit file, edits them through the same writes an AI
// assistant's change_alias and update_fields make, so links follow a renamed name.

/** One of a note's other names, as the page lists it. */
export interface OtherName {
  value: string
  /** True for a `known_as` name, which readers learn at `from`. */
  knownAs: boolean
  from: string | null
  /** In the Author's view, until readers there know it. */
  mark?: ReaderMark
}

/** The note's other names: its aliases, then its `known_as` names, in file order. */
export function otherNames(entity: Pick<EntityPage, 'aliases' | 'knownAs'>): OtherName[] {
  return [
    ...(entity.aliases ?? []).map((value) => ({ value, knownAs: false, from: null })),
    ...(entity.knownAs ?? []).map((k) => ({ value: k.value, knownAs: true, from: k.from, mark: k.readerMark })),
  ]
}

/** Where readers learn a `known_as` name, for its badge: "from ch. 38", or that no from places it. */
export function learnedWords(name: OtherName, positions: readonly PositionView[]): string | null {
  if (!name.knownAs) {
    return null
  }

  return name.from === null ? 'no from' : `from ${placeWords(name.from, positions)}`
}

/**
 * The line under a note's title with its other names. In the writer's views each
 * `known_as` name has its from, and in the Author's view, a tag until readers there
 * learn it, as other later material has (#300). `quiet` is for a note that keeps its
 * own badge or tag.
 */
export function OtherNames({ entity, positions, quiet }: { entity: EntityPage; positions: readonly PositionView[]; quiet: boolean }) {
  const names = otherNames(entity)
  if (names.length === 0) {
    return null
  }

  return (
    <p className="aliases">
      Also:{' '}
      {names.map((n, i) => {
        const learned = learnedWords(n, positions)
        return (
          <Fragment key={`${n.knownAs ? 'k' : 'a'}:${n.value}:${i}`}>
            {i > 0 && ', '}
            <span className="other-name">{n.value}</span>
            {learned !== null && <span className="badge span">{learned}</span>}
            {n.mark && !quiet && <LaterNameTag mark={n.mark} positions={positions} on="name" />}
          </Fragment>
        )
      })}
    </p>
  )
}

/** What's open in Other names: one name's form, or the form for a new one. */
type Action = { kind: 'rename' | 'when' | 'remove'; index: number } | null

/**
 * Edits a note's other names, one change at a time, each saved at once: rename a
 * name, change where readers learn it, remove it, or add one. A rename rewrites the
 * links that use the old name in every note; changing where readers learn a name
 * changes no link, since links reach the note by it at every position.
 */
export function NamesEditor({ entity, positions }: { entity: EntityPage; positions: PositionView[] }) {
  const editing = useEditing()!
  const afterSave = useAfterSave()
  const source = useQuery(sourceQuery(editing.universe, editing.entityKey))
  const names = otherNames(entity)
  const [action, setAction] = useState<Action>(null)
  const [text, setText] = useState('')
  const [from, setFrom] = useState('')
  const [added, setAdded] = useState('')
  const [addedFrom, setAddedFrom] = useState('')
  // "Type a position" with nothing typed isn't "Always": a name readers learn later must say where (F-ENT-10).
  const [typing, setTyping] = useState(false)
  const [addTyping, setAddTyping] = useState(false)
  const [error, setError] = useState<Error | null>(null)
  const [saving, setSaving] = useState(false)
  const heading = useRef<HTMLHeadingElement>(null)
  const id = useId()
  const { setDirty } = editing
  const current = action === null ? null : names[action.index]
  const changed =
    added.trim() !== '' ||
    (current !== null && action?.kind === 'rename' && text.trim() !== current.value) ||
    (current !== null && action?.kind === 'when' && from !== (current.from ?? ''))
  useEffect(() => setDirty(changed), [changed, setDirty])
  useEffect(() => heading.current?.focus(), [])

  const open = (next: Action) => {
    setError(null)
    setAction(next)
    const name = next === null ? null : names[next.index]
    setText(name?.value ?? '')
    setFrom(name?.from ?? '')
    setTyping(false)
  }

  const send = async (path: EditPath, body: Record<string, unknown>) => {
    const hash = source.data?.hash
    if (!hash || saving) {
      return
    }

    setSaving(true)
    setError(null)
    try {
      const result = await postEdit(path, { entity: editing.entityKey, universe: editing.universe, expectedHash: hash, ...body })
      await afterSave()
      editing.close({ what: "the note's names", result })
    } catch (e) {
      if (e instanceof EditError && e.code === 'stale_file') {
        await source.refetch()
        setError(new Error('The note changed meanwhile, perhaps edited by an agent or in another editor. Reload the page to see its names as they are, then try again.'))
      } else {
        setError(e as Error)
      }
    } finally {
      setSaving(false)
    }
  }

  // An empty position is a plain alias: readers know it wherever they know the note.
  const makesPlain = current !== null && action?.kind === 'when' && current.knownAs && from.trim() === '' && !typing
  const submitAction = () => {
    if (current === null || action === null) {
      return
    }

    if (action.kind === 'rename') {
      void send('name', { name: current.value, newName: text })
    } else if (action.kind === 'remove') {
      void send('name', { name: current.value, remove: true })
    } else if (from.trim() === '' && typing) {
      setError(new Error('Say where readers learn the name, such as a book and chapter, or choose Always to make it an alias.'))
    } else if (from.trim() === '') {
      void send('name', { name: current.value, plain: true })
    } else {
      void send('name', { name: current.value, from: from.trim() })
    }
  }

  return (
    <section
      className="editor-panel names-editor"
      aria-labelledby={`${id}-heading`}
      onKeyDown={(e) => {
        if (e.key === 'Escape') {
          e.preventDefault()
          editing.cancel()
        }
      }}
    >
      <h3 id={`${id}-heading`} ref={heading} tabIndex={-1}>
        Other names
      </h3>
      <p className="muted">
        An alias is known wherever readers know the note. A name readers learn partway through the story, such as a secret identity, has the
        point where they learn it: before that, the Reader's view and AI assistants at a story position aren't told it.
      </p>
      {source.isError && <SaveError error={source.error} />}
      {names.length === 0 ? (
        <p className="muted">This note has no other names yet.</p>
      ) : (
        <ul className="names">
          {names.map((n, i) => {
            const learned = learnedWords(n, positions)
            const here = action?.index === i ? action : null
            return (
              <li key={`${n.knownAs ? 'k' : 'a'}:${n.value}:${i}`}>
                <span className="other-name">{n.value}</span> <span className="badge span">{learned ?? 'always known'}</span>{' '}
                <span className="piece-tools">
                  <button type="button" onClick={() => open({ kind: 'rename', index: i })} disabled={saving || here?.kind === 'rename'} aria-label={`Rename “${n.value}”`}>
                    Rename
                  </button>
                  <button
                    type="button"
                    onClick={() => open({ kind: 'when', index: i })}
                    disabled={saving || here?.kind === 'when'}
                    aria-label={`When readers learn “${n.value}”`}
                  >
                    When readers learn it
                  </button>
                  <button type="button" onClick={() => open({ kind: 'remove', index: i })} disabled={saving || here?.kind === 'remove'} aria-label={`Remove “${n.value}”`}>
                    Remove
                  </button>
                </span>
                {here && (
                  <form
                    className="inline-form"
                    aria-label={here.kind === 'rename' ? `Rename “${n.value}”` : here.kind === 'when' ? `When readers learn “${n.value}”` : `Remove “${n.value}”`}
                    onSubmit={(e) => {
                      e.preventDefault()
                      submitAction()
                    }}
                  >
                    {here.kind === 'rename' && (
                      <>
                        <label>
                          New name
                          <input value={text} onChange={(e) => setText(e.target.value)} required autoFocus />
                        </label>
                        <p className="muted">
                          Links that use “{n.value}” change to the new name, in every note.{n.knownAs ? ' Readers learn it at the same point.' : ''}
                        </p>
                      </>
                    )}
                    {here.kind === 'when' && (
                      <>
                        <PositionInput
                          id={`${id}-from-${i}`}
                          label="Readers learn it at"
                          none="Always: it's an alias"
                          value={from}
                          positions={positions}
                          onChange={setFrom}
                          onTyping={setTyping}
                        />
                        <p className="muted">No link changes: links reach the note by this name at every point in the story.</p>
                        {makesPlain && (
                          <p className="warning" role="note">
                            Readers will know “{n.value}” wherever they know the note, at every point in the story. If it's a secret, that gives it away.
                          </p>
                        )}
                      </>
                    )}
                    {here.kind === 'remove' && <p>Remove “{n.value}”? Links that use it change to the note's name, {entity.name}.</p>}
                    <p className="editor-actions">
                      <button type="submit" className={here.kind === 'remove' ? 'danger' : 'primary'} disabled={saving || !source.data}>
                        {saving ? 'Saving…' : here.kind === 'rename' ? 'Rename' : here.kind === 'remove' ? 'Remove name' : makesPlain ? 'Make it an alias' : 'Save'}
                      </button>
                      <button type="button" onClick={() => open(null)} disabled={saving}>
                        Cancel
                      </button>
                    </p>
                  </form>
                )}
              </li>
            )
          })}
        </ul>
      )}
      <form
        className="inline-form"
        aria-label="Add a name"
        onSubmit={(e) => {
          e.preventDefault()
          // Typing a position and leaving it blank sends no from, which the server refuses for a name readers learn later.
          const learnedAt = addedFrom.trim()
          void send('name/add', learnedAt === '' && !addTyping ? { name: added, knownAs: false } : { name: added, knownAs: true, from: learnedAt === '' ? null : learnedAt })
        }}
      >
        <label>
          Add a name
          <input value={added} onChange={(e) => setAdded(e.target.value)} required />
        </label>
        <PositionInput
          id={`${id}-new-from`}
          label="Readers learn it at"
          none="Always: it's an alias"
          value={addedFrom}
          positions={positions}
          onChange={setAddedFrom}
          onTyping={setAddTyping}
        />
        <p className="editor-actions">
          <button type="submit" className="primary" disabled={saving || !source.data || added.trim() === ''}>
            {saving ? 'Saving…' : 'Add name'}
          </button>
        </p>
      </form>
      {error && <SaveError error={error} />}
      <DiscardPrompt />
      <p className="editor-actions">
        <button type="button" onClick={editing.cancel} disabled={saving}>
          Done
        </button>
      </p>
    </section>
  )
}

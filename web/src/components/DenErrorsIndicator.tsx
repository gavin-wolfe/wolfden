import { useEffect, useId, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { count } from '../api'
import { useRefreshHeld } from '../formHold'
import { denErrorCountQuery, denErrorsQuery, fixElsewhere, leavesOut, type DenErrors, type DenErrorView } from '../denErrorsApi'
import { usePosition } from '../hooks'
import { useAccess } from '../access'
import { trapTab } from './CheckpointIndicator'
import '../denErrors.css'

/**
 * The Reader's view of the top bar's "⚠ 3 problems" (#300). The list names files and
 * quotes them, whatever readers know, so this asks only how many, and a press goes to
 * the Author's view, where the list is. It stays because what an error leaves out is
 * missing from this view too, and the writer checking it should know.
 */
export function ReadersErrorCount() {
  const { setReaders } = usePosition()
  const errors = useQuery(denErrorCountQuery)
  if (!errors.data || errors.data.count === 0) {
    return null
  }

  return (
    <button type="button" className="den-errors-indicator" title="What these leave out is missing here. See them in the Author's view." onClick={() => setReaders(false)}>
      <span aria-hidden="true">⚠ </span>
      {count(errors.data.count, 'problem', 'problems')}
    </button>
  )
}

/**
 * The top bar's "⚠ 3 problems" (#280): the den's errors, not its warnings. It
 * asks every few seconds while the page is showing, so it appears when a file
 * goes wrong outside Wolfden and clears when it's fixed, and it's hidden when
 * there are none. It opens the list: each error's file and line, what it says,
 * what it leaves out, and a way to fix it.
 *
 * It isn't affected by the story position in the Author's view: like editing,
 * it's for the writer, and shows everything. The Reader's view has {@link ReadersErrorCount}.
 */
export function DenErrorsIndicator({ universe }: { universe: string | undefined }) {
  const errors = useQuery(denErrorsQuery)
  const [open, setOpen] = useState(false)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const close = () => {
    setOpen(false)
    requestAnimationFrame(() => triggerRef.current?.focus())
  }

  const data = errors.data

  // A den that didn't load has just come back: what the pages said about it is out of date.
  const client = useQueryClient()
  const loaded = data?.denLoaded
  const wasLoaded = useRef(loaded)
  const catchUp = useRef(false)
  const held = useRefreshHeld()
  useEffect(() => {
    catchUp.current ||= wasLoaded.current === false && loaded === true
    wasLoaded.current = loaded

    // While an editor or form is open, it waits, as every refresh does (formHold.ts).
    if (catchUp.current && !held) {
      catchUp.current = false
      void client.invalidateQueries()
    }
  }, [loaded, held, client])

  // It can't ask now. Whatever it knew is old, so it says so rather than seem all clear.
  const stale = errors.isError
  if (data === undefined || data.count === 0) {
    return stale ? (
      <span className="den-errors-unknown" title={errors.error?.message}>
        Can’t check for problems
      </span>
    ) : null
  }

  return (
    <>
      <button
        type="button"
        ref={triggerRef}
        className="den-errors-indicator"
        aria-haspopup="dialog"
        title={stale ? errors.error?.message : undefined}
        onClick={() => setOpen(true)}
      >
        <span aria-hidden="true">⚠ </span>
        {count(data.count, 'problem', 'problems')}
        {stale && ' · can’t check now'}
      </button>
      {open && <DenErrorsDialog errors={data} universe={universe} onClose={close} onLeave={() => setOpen(false)} />}
    </>
  )
}

function DenErrorsDialog({ errors, universe, onClose, onLeave }: { errors: DenErrors; universe: string | undefined; onClose: () => void; onLeave: () => void }) {
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  useEffect(() => dialogRef.current?.querySelector<HTMLElement>('a[href], button')?.focus(), [])
  return (
    <div className="dialog-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        className="dialog den-errors-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        ref={dialogRef}
        tabIndex={-1}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            e.stopPropagation()
            onClose()
          } else if (e.key === 'Tab') {
            trapTab(e, dialogRef.current)
          }
        }}
      >
        <h2 id={titleId}>{count(errors.count, 'problem', 'problems')} in this den</h2>
        <p className="muted">Until they’re fixed, what these errors leave out is missing from your pages, and from what AI assistants read. Nothing is lost: the text stays in the files.</p>
        <ul className="den-errors-list">
          {errors.errors.map((error) => (
            <li key={`${error.path}:${error.line}:${error.rule}:${error.message}`}>
              <ErrorEntry error={error} universe={universe} onLeave={onLeave} />
            </li>
          ))}
        </ul>
        {errors.truncated && <p className="muted">And {errors.count - errors.errors.length} more. Run ‘wolfden check’ in the den’s folder to see them all.</p>}
        <div className="dialog-actions">
          <button type="button" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  )
}

function ErrorEntry({ error, universe, onLeave }: { error: DenErrorView; universe: string | undefined; onLeave: () => void }) {
  // A story position belongs to one universe, so it doesn't follow a link into another.
  const otherUniverse = error.universe !== null && error.universe !== universe
  // Edit file is a writer's, and fixing den.yaml the owner's (#315 §4.6); the list itself is anyone's.
  const { canWrite, isOwner } = useAccess()
  const allowed = error.kind === 'den' ? isOwner : canWrite
  return (
    <>
      <p className="den-errors-where">
        {error.name && <strong>{error.name} </strong>}
        <code>{error.path}</code>
        {error.line !== null && <span>, line {error.line}</span>}
      </p>
      <p>
        {error.message} <span className="muted">({error.rule})</span>
      </p>
      <p>
        <span className="den-errors-label">Left out:</span> {leavesOut(error)}
      </p>
      {error.canEdit && !allowed ? null : error.canEdit ? (
        <p>
          <Link to="/file" search={(s) => ({ ...s, path: error.path, at: otherUniverse ? undefined : s.at })} onClick={onLeave}>
            Edit file
          </Link>
        </p>
      ) : (
        <p className="muted">{fixElsewhere(error)}</p>
      )}
    </>
  )
}

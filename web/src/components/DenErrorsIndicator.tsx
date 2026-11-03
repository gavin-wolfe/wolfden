import { useEffect, useId, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { count } from '../api'
import { denErrorsQuery, fixElsewhere, leavesOut, type DenErrors, type DenErrorView } from '../denErrorsApi'
import { trapTab } from './CheckpointIndicator'
import '../denErrors.css'

/**
 * The top bar's "⚠ 3 problems" (#280): the den's errors, not its warnings. It
 * asks every few seconds while the page is showing, so it appears when a file
 * goes wrong outside Wolfden and clears when it's fixed, and it's hidden when
 * there are none. It opens the list: each error's file and line, what it says,
 * what it leaves out, and a way to fix it.
 *
 * It isn't affected by the story position: like editing, it's for the writer,
 * and shows everything.
 */
export function DenErrorsIndicator() {
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
  useEffect(() => {
    if (wasLoaded.current === false && loaded === true) {
      void client.invalidateQueries()
    }

    wasLoaded.current = loaded
  }, [loaded, client])

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
      {open && <DenErrorsDialog errors={data} onClose={close} onLeave={() => setOpen(false)} />}
    </>
  )
}

function DenErrorsDialog({ errors, onClose, onLeave }: { errors: DenErrors; onClose: () => void; onLeave: () => void }) {
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
              <ErrorEntry error={error} onLeave={onLeave} />
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

function ErrorEntry({ error, onLeave }: { error: DenErrorView; onLeave: () => void }) {
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
      {error.canEdit ? (
        <p>
          <Link to="/file" search={{ path: error.path }} onClick={onLeave}>
            Edit file
          </Link>
        </p>
      ) : (
        <p className="muted">{fixElsewhere(error)}</p>
      )}
    </>
  )
}

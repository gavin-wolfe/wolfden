import { Link } from '@tanstack/react-router'
import { ApiRequestError } from '../api'
import { usePosition } from '../hooks'

/** What a page shows while its data loads. */
export function Loading({ what }: { what: string }) {
  return (
    <p className="muted" role="status">
      Loading {what}…
    </p>
  )
}

/**
 * An error, in the API's words. A position the den doesn't have offers to
 * show everything instead.
 */
export function ErrorState({ error }: { error: Error }) {
  const { at, setAt } = usePosition()
  const badPosition = error instanceof ApiRequestError && (error.code === 'bad_position' || (error.code === 'bad_argument' && at !== undefined))
  return (
    <div className="error" role="alert">
      <p>{error.message}</p>
      {badPosition && at !== undefined && (
        <button type="button" onClick={() => setAt(undefined)}>
          Show everything
        </button>
      )}
    </div>
  )
}

export function NotFound() {
  return (
    <div className="page-body">
      <h1>Not found</h1>
      <p>
        There's no page here. <Link to="/">Go to the den's home page</Link>.
      </p>
    </div>
  )
}

/** A small label for a status other than canon, such as "draft" or "idea". */
export function StatusBadge({ status }: { status: string | null | undefined }) {
  if (!status || status === 'canon') {
    return null
  }

  return <span className={`badge status-${status}`}>{status}</span>
}

/** Marks an error in a save's list of what's in the file: a bad section comment the save kept, listed with the warnings. */
export function ErrorMark({ severity }: { severity: string }) {
  return severity === 'error' ? <strong className="kept-error">Error: </strong> : null
}

/** The heading of that list. */
export function fileProblemsHeading(problems: readonly { severity: string }[]): string {
  return problems.some((p) => p.severity === 'error') ? 'Errors and warnings in the file:' : 'Warnings in the file:'
}

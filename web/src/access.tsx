import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { queryOptions, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { ApiRequestError, getJson } from './api'
import './login.css'

/**
 * Who is logged in, from `/api/auth/status` (#315,
 * src/Wolfden.Api/AuthEndpoints.cs). With login off, `role` is `owner` and
 * `user` is null; with login on and nobody logged in, both are null.
 */
export interface AuthStatus {
  version: number
  login: boolean
  user: string | null
  role: Role | null
}

export type Role = 'owner' | 'writer' | 'viewer'

export const AUTH_KEY = ['auth'] as const

export const authQuery = queryOptions({
  queryKey: AUTH_KEY,
  queryFn: () => getJson<AuthStatus>('/api/auth/status'),
  // Asked once: a login, a logout, or any request refused for want of a login asks again (AuthGate).
  staleTime: Infinity,
})

/** What the page lets this person do. The server's 403 is what enforces it; the page only hides what would be refused. */
export interface Access {
  role: Role | null
  /** Owners and writers: edit notes, create, checkpoint, freeze. */
  canWrite: boolean
  /** Owners: unfreeze, turn history on, fix den.yaml. */
  isOwner: boolean
}

const OWNER: Access = { role: 'owner', canWrite: true, isOwner: true }
const NOBODY: Access = { role: null, canWrite: false, isOwner: false }

/**
 * The person's access, from their role (#315 D6): a viewer reads everything
 * and changes nothing. Until the status arrives, nothing is editable, so a
 * viewer never sees an edit control flash up. If the status can't be read,
 * the page acts as the owner and leaves refusing to the server.
 */
export function useAccess(): Access {
  const auth = useQuery(authQuery)
  if (auth.isError) {
    return OWNER
  }

  const role = auth.data?.role ?? null
  return role === null ? NOBODY : { role, canWrite: role === 'owner' || role === 'writer', isOwner: role === 'owner' }
}

/** True for the API's answer to a request with no login, when login is on. */
function isUnauthenticated(error: unknown): boolean {
  return error instanceof ApiRequestError && error.code === 'unauthenticated'
}

/**
 * Shows the login page while this installation asks for a login and nobody
 * is logged in, and the den's pages otherwise. When any request is refused
 * for want of a login (a session that ended, or a logout in another tab), it
 * asks again who is logged in, which brings the login page back.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const client = useQueryClient()
  const auth = useQuery(authQuery)

  useEffect(() => {
    const recheck = () => void client.invalidateQueries({ queryKey: AUTH_KEY })
    const queries = client.getQueryCache().subscribe((event) => {
      if (event.type === 'updated' && event.action.type === 'error' && isUnauthenticated(event.action.error)) {
        recheck()
      }
    })
    const mutations = client.getMutationCache().subscribe((event) => {
      if (event.type === 'updated' && event.action.type === 'error' && isUnauthenticated(event.action.error)) {
        recheck()
      }
    })
    return () => {
      queries()
      mutations()
    }
  }, [client])

  if (auth.data?.login && auth.data.role === null) {
    return <LoginPage />
  }

  return <>{children}</>
}

/** Starts the den's pages afresh as `status`: nothing cached from before the login or logout is kept. */
function startAs(client: QueryClient, status: AuthStatus) {
  client.removeQueries({ predicate: (query) => query.queryKey[0] !== AUTH_KEY[0] })
  client.setQueryData(AUTH_KEY, status)
}

/** Posts to `/api/auth/<action>`, returning the new status, or throwing the API's error. */
async function postAuth(action: 'login' | 'logout', body?: unknown): Promise<AuthStatus> {
  const response = await fetch(`/api/auth/${action}`, {
    method: 'POST',
    headers: body === undefined ? { Accept: 'application/json' } : { Accept: 'application/json', 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const answer: unknown = await response.json().catch(() => undefined)
  const error = typeof answer === 'object' && answer !== null ? (answer as { code?: unknown; message?: unknown }) : null
  if (!response.ok) {
    throw new ApiRequestError(
      typeof error?.code === 'string' ? error.code : 'http_error',
      typeof error?.message === 'string' ? error.message : `The server answered ${response.status}.`,
    )
  }

  return answer as AuthStatus
}

/**
 * The login page (#315): a username and a password. A wrong password and an
 * unknown name get the same answer from the server, and so the same words here.
 */
export function LoginPage() {
  const client = useQueryClient()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    document.title = 'Log in · Wolfden'
  }, [])

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      startAs(client, await postAuth('login', { username: username.trim(), password }))
    } catch (failure) {
      setPassword('')
      setError(failure instanceof Error ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="login-page">
      <form className="login-form" onSubmit={submit} aria-labelledby="login-heading">
        <h1 id="login-heading">Wolfden</h1>
        <p>This den asks for a login.</p>
        <label>
          Username
          <input name="username" autoComplete="username" required value={username} onChange={(e) => setUsername(e.target.value)} autoFocus />
        </label>
        <label>
          Password
          <input name="password" type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        {error && (
          <p className="login-error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" className="primary" disabled={busy}>
          {busy ? 'Logging in…' : 'Log in'}
        </button>
        <p className="muted login-help">
          Forgot your password? Whoever runs Wolfden on this computer can set a new one with <code>wolfden user reset-password &lt;name&gt;</code>.
        </p>
      </form>
    </main>
  )
}

/** The top bar's account: who is logged in, their role, and Log out. Nothing with login off. */
export function AccountMenu() {
  const client = useQueryClient()
  const auth = useQuery(authQuery)
  const [error, setError] = useState<string | null>(null)
  if (!auth.data?.login || auth.data.user === null) {
    return null
  }

  const logout = async () => {
    setError(null)
    try {
      startAs(client, await postAuth('logout'))
    } catch (failure) {
      if (isUnauthenticated(failure)) {
        // The session had already ended.
        startAs(client, { version: 1, login: true, user: null, role: null })
      } else {
        setError(failure instanceof Error ? failure.message : String(failure))
      }
    }
  }

  return (
    <span className="account-menu">
      <span className="account-name" title={roleWords(auth.data.role)}>
        {auth.data.user}
        {auth.data.role && <span className="account-role"> · {auth.data.role}</span>}
      </span>{' '}
      <button type="button" onClick={() => void logout()}>
        Log out
      </button>
      {error && (
        <span className="login-error" role="alert">
          {' '}
          {error}
        </span>
      )}
    </span>
  )
}

function roleWords(role: Role | null): string {
  switch (role) {
    case 'owner':
      return 'Owner: can read and change everything.'
    case 'writer':
      return 'Writer: can read and change notes, checkpoint, and freeze books.'
    case 'viewer':
      return 'Viewer: can read everything, spoilers included, and change nothing.'
    default:
      return ''
  }
}

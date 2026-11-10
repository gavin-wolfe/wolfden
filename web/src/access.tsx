import { createContext, useCallback, useContext, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { queryOptions, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { ApiRequestError, getJson } from './api'
import { isRefreshHeld } from './formHold'
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

/** Lets the top bar's Log out start the pages afresh, unmounting them (AuthGate). */
const LogOutContext = createContext<(status: AuthStatus) => void>(() => {})

/**
 * Shows the login page while this installation asks for a login and nobody
 * is logged in, and the den's pages otherwise. When any request is refused
 * for want of a login (a session that ended, or a logout in another tab), it
 * asks again who is logged in.
 *
 * A session that ends while the pages are open brings the login form back
 * *over* them, not in their place: the pages stay mounted, so an open
 * editor keeps its text (formHold.ts), and once logged in again the writer
 * can press Save, which is still checked against the file's hash. Only Log
 * out, which the person chose, takes the pages away; so does someone else
 * logging in at that form, after asking, since an open editor holds the
 * last person's unsaved changes (PR 5 will record who saves what, and a
 * draft must not be saved under another account).
 *
 * A server that must be restarted (login turned on after it started)
 * answers everything with `restart_needed`; any such answer shows a banner.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const client = useQueryClient()
  const auth = useQuery(authQuery)
  // True once the pages have been shown to someone logged in, until Log out.
  const [shown, setShown] = useState(false)
  // Bumped to mount the pages afresh, dropping open editors, when someone else logs in over them.
  const [generation, setGeneration] = useState(0)
  const [restart, setRestart] = useState<string | null>(null)
  // Who the pages were last shown to.
  const lastUser = useRef<string | null>(null)
  const loggedIn = auth.data !== undefined && (!auth.data.login || auth.data.role !== null)
  if (loggedIn && !shown) {
    setShown(true)
  }

  useEffect(() => {
    if (auth.data?.user) {
      lastUser.current = auth.data.user
    }
  }, [auth.data?.user])

  useEffect(() => {
    const recheck = () => void client.invalidateQueries({ queryKey: AUTH_KEY })
    const seen = (error: unknown) => {
      if (isUnauthenticated(error)) {
        recheck()
      } else if (error instanceof ApiRequestError && error.code === 'restart_needed') {
        setRestart(error.message)
      }
    }
    const queries = client.getQueryCache().subscribe((event) => {
      if (event.type === 'updated' && event.action.type === 'error') {
        seen(event.action.error)
      }
    })
    const mutations = client.getMutationCache().subscribe((event) => {
      if (event.type === 'updated' && event.action.type === 'error') {
        seen(event.action.error)
      }
    })
    return () => {
      queries()
      mutations()
    }
  }, [client])

  const logOut = useCallback(
    (status: AuthStatus) => {
      setShown(false)
      lastUser.current = null
      startAs(client, status)
    },
    [client],
  )

  /**
   * After a login at the form: the same person carries on with everything as
   * it was; someone else gets the pages afresh, after saying so if an editor
   * or form is open. Declining logs the new session out again. Returns false
   * then, so the form stays.
   */
  const loggedInAs = useCallback(
    async (status: AuthStatus): Promise<boolean> => {
      const before = lastUser.current
      const someoneElse = shown && before !== null && status.user !== null && before.toLowerCase() !== status.user.toLowerCase()
      if (!someoneElse) {
        resumeAs(client, status)
        return true
      }

      if (isRefreshHeld() && !window.confirm('Someone else was editing here. Discard their unsaved changes and continue?')) {
        await postAuth('logout').catch(() => undefined)
        return false
      }

      setGeneration((g) => g + 1)
      startAs(client, status)
      return true
    },
    [client, shown],
  )

  const shownRestart = restart ?? (auth.error instanceof ApiRequestError && auth.error.code === 'restart_needed' ? auth.error.message : null)
  const loginNeeded = auth.data?.login === true && auth.data.role === null
  if (loginNeeded && !shown) {
    return <LoginPage onLoggedIn={loggedInAs} />
  }

  return (
    <LogOutContext.Provider value={logOut}>
      {shownRestart && (
        <p className="restart-needed" role="alert">
          {shownRestart}
        </p>
      )}
      <div key={generation} className="auth-pages" inert={loginNeeded} aria-hidden={loginNeeded || undefined}>
        {children}
      </div>
      {loginNeeded && (
        <div className="login-overlay">
          <LoginPage again onLoggedIn={loggedInAs} />
        </div>
      )}
    </LogOutContext.Provider>
  )
}

/** Starts the den's pages afresh as `status`: nothing cached from before the logout is kept. */
function startAs(client: QueryClient, status: AuthStatus) {
  client.removeQueries({ predicate: (query) => query.queryKey[0] !== AUTH_KEY[0] })
  client.setQueryData(AUTH_KEY, status)
}

/**
 * Carries on as `status` after a login. Nothing is thrown away: while an
 * editor or form is open nothing is asked again either (formHold.ts), so its
 * text and the hash it was based on stay; otherwise every answer is asked
 * again, now with the login.
 */
function resumeAs(client: QueryClient, status: AuthStatus) {
  client.setQueryData(AUTH_KEY, status)
  if (!isRefreshHeld()) {
    void client.invalidateQueries({ predicate: (query) => query.queryKey[0] !== AUTH_KEY[0] })
  }
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
 * `again` is the form shown over the pages when a session ended while they
 * were open.
 */
export function LoginPage({ again = false, onLoggedIn }: { again?: boolean; onLoggedIn: (status: AuthStatus) => Promise<boolean> }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (again) {
      return
    }

    document.title = 'Log in · Wolfden'
  }, [again])

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      if (!(await onLoggedIn(await postAuth('login', { username: username.trim(), password })))) {
        setPassword('')
        setError('Nothing was discarded, and you’re not logged in. Whoever was editing can log in again to carry on.')
      }
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
        <p>{again ? 'Your session ended. Log in again to carry on: anything you were editing is still there, ready to save.' : 'This den asks for a login.'}</p>
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

/** Shows `children` (a Save button) only to an account that may change the den; an open editor's form says why otherwise. */
export function IfCanWrite({ children }: { children: ReactNode }) {
  const { canWrite } = useAccess()
  return canWrite ? <>{children}</> : <span className="muted">Your account can read this den but not change it.</span>
}

/** The top bar's account: who is logged in, their role, and Log out. Nothing with login off. */
export function AccountMenu() {
  const logOut = useContext(LogOutContext)
  const auth = useQuery(authQuery)
  const [error, setError] = useState<string | null>(null)
  if (!auth.data?.login || auth.data.user === null) {
    return null
  }

  const logout = async () => {
    setError(null)
    // Log out takes the pages away, so an open editor's text would go with them.
    if (isRefreshHeld() && !window.confirm('You have an editor or form open. Log out anyway, and lose what you haven’t saved?')) {
      return
    }

    try {
      logOut(await postAuth('logout'))
    } catch (failure) {
      if (isUnauthenticated(failure)) {
        // The session had already ended.
        logOut({ version: 1, login: true, user: null, role: null })
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

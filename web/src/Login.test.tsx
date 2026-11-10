// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { AuthStatus } from './access'
import { renderApp, stubApi } from './test/harness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const LOGGED_OUT: AuthStatus = { version: 1, login: true, user: null, role: null }

function as(user: string, role: AuthStatus['role']): AuthStatus {
  return { version: 1, login: true, user, role }
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const UNAUTHENTICATED = { code: 'unauthenticated', message: 'Log in to use Wolfden: this installation asks for a login.' }

/**
 * A server with login on: `/api/auth/status` answers `state.auth`; while
 * nobody is logged in, every den request is a 401, as the server answers.
 * A login with `password` logs `user` in.
 */
function stubLogin(start: AuthStatus, accounts: Record<string, AuthStatus> = {}) {
  const state = { auth: start }
  const posts: { path: string; body: unknown; contentType: string | null }[] = []
  const api = stubApi({
    override: (path, _params, init) => {
      if (path === '/api/auth/status') {
        return state.auth
      }

      if (path === '/api/auth/login') {
        const body = JSON.parse(String(init?.body)) as { username: string; password: string }
        posts.push({ path, body, contentType: new Headers(init?.headers).get('Content-Type') })
        const account = accounts[body.username]
        if (!account || body.password !== 'correct horse battery') {
          return json({ code: 'login_failed', message: "That username and password don't match an account that can log in now." }, 401)
        }

        state.auth = account
        return account
      }

      if (path === '/api/auth/logout') {
        posts.push({ path, body: init?.body ?? null, contentType: null })
        state.auth = LOGGED_OUT
        return LOGGED_OUT
      }

      return state.auth.role === null ? json(UNAUTHENTICATED, 401) : undefined
    },
  })
  return { ...api, state, posts }
}

async function logIn(username: string, password: string) {
  const form = await screen.findByRole('form', { name: 'Wolfden' })
  fireEvent.change(within(form).getByLabelText('Username'), { target: { value: username } })
  fireEvent.change(within(form).getByLabelText('Password'), { target: { value: password } })
  fireEvent.click(within(form).getByRole('button', { name: 'Log in' }))
}

describe('SC-ACCT-11: with login on, the page asks who I am', () => {
  it('shows the login page, and after a login, the den and who I am', async () => {
    const { posts } = stubLogin(LOGGED_OUT, { wren: as('wren', 'owner') })
    renderApp('/')

    expect(await screen.findByText('This den asks for a login.')).toBeTruthy()
    expect(screen.queryByText('Dracula')).toBeNull()

    await logIn(' wren ', 'correct horse battery')

    expect(await screen.findByRole('link', { name: 'Dracula' })).toBeTruthy()
    expect(posts).toEqual([{ path: '/api/auth/login', body: { username: 'wren', password: 'correct horse battery' }, contentType: 'application/json' }])
    expect(screen.getByText('wren')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Log out' })).toBeTruthy()
  })

  it('says a login failed in the server’s words, and clears the password', async () => {
    stubLogin(LOGGED_OUT, { wren: as('wren', 'owner') })
    renderApp('/')

    await logIn('wren', 'not the right one')

    expect((await screen.findByRole('alert')).textContent).toBe("That username and password don't match an account that can log in now.")
    expect((screen.getByLabelText('Password') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Username') as HTMLInputElement).value).toBe('wren')
  })

  it('logs out, back to the login page', async () => {
    const { posts } = stubLogin(as('wren', 'owner'))
    renderApp('/')

    fireEvent.click(await screen.findByRole('button', { name: 'Log out' }))

    expect(await screen.findByText('This den asks for a login.')).toBeTruthy()
    expect(posts.map((p) => p.path)).toEqual(['/api/auth/logout'])
  })

  it('comes back to the login page when the session ends', async () => {
    const { state } = stubLogin(as('wren', 'owner'))
    const { queryClient } = renderApp('/')
    expect(await screen.findByRole('link', { name: 'Dracula' })).toBeTruthy()

    // The session ends elsewhere (a new password, or a logout in another tab); the next request is a 401.
    state.auth = LOGGED_OUT
    await queryClient.refetchQueries({ queryKey: ['den'] })

    expect(await screen.findByText(/Your session ended\. Log in again to carry on/)).toBeTruthy()
  })

  it('shows no account and no login with login off', async () => {
    stubApi()
    renderApp('/')

    expect(await screen.findByRole('link', { name: 'Dracula' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Log out' })).toBeNull()
    expect(screen.queryByText('This den asks for a login.')).toBeNull()
  })
})

describe('SC-ACCT-12: a viewer sees no edit controls', () => {
  it('has no Edit buttons on a note, no New menu, and no checkpoint indicator', async () => {
    stubApi({
      auth: as('rook', 'viewer'),
      override: (path) =>
        path === '/api/checkpoint'
          ? { version: 1, backend: 'git', available: true, reason: null, enable: null, latest: null, inProgress: null, count: 2, changes: [], otherUniverses: 0, autoCheckpoint: { every: null, problem: null, runs: true } }
          : undefined,
    })
    renderApp('/u/dracula/e/E-MINA')

    expect(await screen.findByRole('heading', { name: 'Mina Harker', level: 1 })).toBeTruthy()
    expect(screen.getByText('· viewer')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^Edit file/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /^Other names/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'New' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Checkpoint…/ })).toBeNull()
  })

  it('has the same page as the owner, with Edit buttons, for a writer', async () => {
    stubApi({ auth: as('ash', 'writer') })
    renderApp('/u/dracula/e/E-MINA')

    expect(await screen.findByRole('button', { name: /^Edit file/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'New' })).toBeTruthy()
  })

  it('has no Freeze control or Add chapter on a book', async () => {
    stubApi({ auth: as('rook', 'viewer') })
    renderApp('/u/dracula/b/dracula')

    expect(await screen.findByRole('heading', { name: 'Dracula', level: 1 })).toBeTruthy()
    await waitFor(() => expect(screen.getAllByText("Jonathan Harker's Journal").length).toBeGreaterThan(0))
    expect(screen.queryByRole('button', { name: 'Freeze…' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Add chapter/ })).toBeNull()
  })
})

describe('SC-ACCT-16: only an owner unfreezes', () => {
  const final = {
    version: 1,
    universe: 'dracula',
    book: 'dracula',
    name: 'Dracula',
    path: 'universes/dracula/books/dracula/book.md',
    hash: 'h-final',
    status: 'final',
    backend: 'git',
    history: true,
    reason: null,
    nextTag: null,
    unfinished: false,
    otherChanges: 0,
  }

  it('offers a writer no Unfreeze, and says who can', async () => {
    stubApi({ auth: as('ash', 'writer'), override: (path) => (path === '/api/book/freeze' ? final : undefined) })
    renderApp('/u/dracula/b/dracula')

    expect(await screen.findByText(/Only an owner can unfreeze it\./)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Unfreeze…' })).toBeNull()
  })

  it('offers the owner Unfreeze', async () => {
    stubApi({ auth: as('wren', 'owner'), override: (path) => (path === '/api/book/freeze' ? final : undefined) })
    renderApp('/u/dracula/b/dracula')

    expect(await screen.findByRole('button', { name: 'Unfreeze…' })).toBeTruthy()
  })
})

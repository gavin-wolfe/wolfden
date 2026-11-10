// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { AuthStatus } from './access'
import type { EntityPageResult, PageSection } from './api'
import type { EntitySource } from './editApi'
import { editorViewIn } from './components/MarkdownEditor'
import { entities, page, renderApp, stubApi } from './test/harness'

// A session that ends while an editor is open (#315, round 1 of PR 4b's
// review): the login form comes back over the page, the editor keeps its
// text, and after logging in again, Save works, still checked by hash.

beforeAll(() => {
  // jsdom lays nothing out; CodeMirror measures text ranges.
  const rect = { x: 0, y: 0, width: 0, height: 0, top: 0, left: 0, right: 0, bottom: 0, toJSON: () => ({}) } as DOMRect
  Range.prototype.getBoundingClientRect = () => rect
  Range.prototype.getClientRects = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }) as unknown as DOMRectList
  document.execCommand ??= () => false
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const mina = entities.find((e) => e.key === 'E-MINA')!
const OWNER: AuthStatus = { version: 1, login: true, user: 'wren', role: 'owner' }
const LOGGED_OUT: AuthStatus = { version: 1, login: true, user: null, role: null }

const overview: PageSection = {
  name: 'Overview', level: 2, headingPath: ['Overview'], status: 'canon', pinned: false, from: null, until: null, line: 9, body: 'Old text.', bookkeeping: null, links: [],
}

function source(hash: string, body: string): EntitySource {
  return {
    version: 1,
    entity: mina,
    hash,
    text: `---\nname: Mina Harker\n---\n\n# Mina Harker\n\nA schoolmistress.\n\n## Overview\n${body}\n`,
    lineEndings: 'lf',
    byteOrderMark: false,
    frozen: null,
    lead: { line: 4, text: '# Mina Harker\n\nA schoolmistress.' },
    sections: [{ index: 0, name: 'Overview', level: 2, line: 9, comment: null, body, pinned: false, parent: null }],
    standardSections: ['Overview'],
    fields: [],
    fieldTypes: [],
  }
}

/** A server with login on, whose session ends when the test says; edits answer `onPost`. */
function stubSession(onPost: (hash: unknown) => Response) {
  const state = { auth: OWNER, file: source('h1', 'Old text.') }
  const posts: Record<string, unknown>[] = []
  const pages = (): EntityPageResult => ({
    version: 1, view: 'full', position: null, inView: true, note: null, entity: page(mina, { lead: '# Mina Harker\n\nA schoolmistress.', sections: [overview] }),
  })
  stubApi({
    entityPage: pages,
    override: (path, _params, init) => {
      if (path === '/api/auth/status') {
        return state.auth
      }

      if (path === '/api/auth/login') {
        state.auth = OWNER
        return OWNER
      }

      if (state.auth.role === null) {
        return Response.json({ code: 'unauthenticated', message: 'Log in to use Wolfden.' }, { status: 401 })
      }

      if (path === '/api/entity/source') {
        return state.file
      }

      if (path.startsWith('/api/edit/')) {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>
        posts.push(body)
        return onPost(body.expectedHash)
      }

      return undefined
    },
  })
  return { state, posts }
}

async function openOverviewAndType(text: string) {
  fireEvent.click(await screen.findByRole('button', { name: 'Edit “Overview”' }))
  const group = await screen.findByRole('group', { name: 'Markdown of the section “Overview”' })
  await waitFor(() => expect(editorViewIn(group)).not.toBeNull())
  const view = editorViewIn(group)!
  act(() => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } }))
  return { group, view }
}

/** Ends the session, lets a refresh find out, and logs in again at the form over the page. */
async function sessionEndsThenLogInAgain(state: { auth: AuthStatus }, refresh: () => Promise<unknown>) {
  state.auth = LOGGED_OUT
  await act(refresh)
  const form = await screen.findByRole('form', { name: 'Wolfden' })
  expect(within(form).getByText(/Your session ended/)).toBeTruthy()
  fireEvent.change(within(form).getByLabelText('Username'), { target: { value: 'wren' } })
  fireEvent.change(within(form).getByLabelText('Password'), { target: { value: 'correct horse battery' } })
  fireEvent.click(within(form).getByRole('button', { name: 'Log in' }))
  await waitFor(() => expect(screen.queryByRole('form', { name: 'Wolfden' })).toBeNull())
}

describe('SC-ACCT-18: my session ends while I’m editing', () => {
  it('keeps the editor’s text under the login form, and saves it after I log in again', async () => {
    const { state, posts } = stubSession(() =>
      Response.json({ version: 1, changed: true, path: mina.path, hash: 'h2', entity: mina, effects: [], pinnedCanon: [], warnings: [], files: null, idAdded: null }),
    )
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    const { view } = await openOverviewAndType('Written before the session ended.')

    await sessionEndsThenLogInAgain(state, () => queryClient.refetchQueries({ queryKey: ['den-files'] }))

    // Still the same editor, with the same text.
    expect(view.state.doc.toString()).toBe('Written before the session ended.')
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByText('Saved the section “Overview”.')
    expect(posts).toHaveLength(1)
    expect(posts[0]).toMatchObject({ expectedHash: 'h1', text: 'Written before the session ended.' })
  })

  it('still shows both versions if the note changed meanwhile', async () => {
    const { state } = stubSession(() =>
      Response.json({ code: 'stale_file', message: 'It changed.', current: source('h9', 'An agent wrote this.') }, { status: 409 }),
    )
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    const { view } = await openOverviewAndType('Mine.')

    state.file = source('h9', 'An agent wrote this.')
    await sessionEndsThenLogInAgain(state, () => queryClient.refetchQueries({ queryKey: ['den-files'] }))

    expect(view.state.doc.toString()).toBe('Mine.')
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    const conflict = await screen.findByRole('region', { name: /The note changed/ })
    expect(within(conflict).getByText('An agent wrote this.')).toBeTruthy()
  })
})

// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { focusManager, onlineManager, type QueryClient } from '@tanstack/react-query'
import type { EntityPageResult, PageSection } from './api'
import { editorViewIn } from './components/MarkdownEditor'
import type { EntitySource } from './editApi'
import { LIVE_REFRESH_MS, RETRY_FAILED_MS, denFilesQuery } from './liveRefresh'
import { bookChapterFields, entities, page, renderApp, stubApi } from './test/harness'

// Writes from outside the page (#285): an open page catches up, and an open
// editor is never disturbed.

beforeAll(() => {
  const rect = { x: 0, y: 0, width: 0, height: 0, top: 0, left: 0, right: 0, bottom: 0, toJSON: () => ({}) } as DOMRect
  Range.prototype.getBoundingClientRect = () => rect
  Range.prototype.getClientRects = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }) as unknown as DOMRectList
  document.execCommand ??= () => false
})

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.unstubAllGlobals()
  focusManager.setFocused(undefined)
  onlineManager.setOnline(true)
})

const mina = entities.find((e) => e.key === 'E-MINA')!

function section(name: string, line: number, body: string): PageSection {
  return { name, level: 2, headingPath: [name], status: 'canon', pinned: false, from: null, until: null, line, body, bookkeeping: null, links: [] }
}

function source(body: string): EntitySource {
  return {
    version: 1,
    entity: mina,
    hash: 'h1',
    text: `---\nname: Mina Harker\n---\n\n## Overview\n${body}\n`,
    lineEndings: 'lf',
    byteOrderMark: false,
    frozen: null,
    lead: { line: 4, text: '' },
    sections: [{ index: 0, name: 'Overview', level: 2, line: 5, comment: null, body, pinned: false, parent: null }],
    standardSections: ['Overview'],
    fields: [],
    fieldTypes: [],
  }
}

/** The den as the API reads it now: a test changes it to play a write from outside. */
function stubDen() {
  const now = {
    fingerprint: 'files-1' as string | Response,
    line: 5,
    body: 'Before the write.',
    entity: null as Response | null,
    source: null as Response | null,
    hierarchy: null as Response | null,
    /** Holds the answer about the files back until it's settled. */
    filesAnswer: null as Promise<void> | null,
    /** Holds the answer to /api/hierarchy back until it's settled. */
    hierarchyAnswer: null as Promise<void> | null,
  }
  const stub = stubApi({
    entityPage: (_key, at): EntityPageResult => ({
      version: 1,
      view: at ? 'canon_as_of' : 'full',
      position: at,
      inView: true,
      note: null,
      entity: page(mina, { sections: [section('Overview', now.line, now.body)] }),
    }),
    override: async (path) => {
      if (path === '/api/hierarchy') {
        await now.hierarchyAnswer
        return now.hierarchy?.clone()
      }

      if (path === '/api/entity/source' && now.source) {
        return now.source.clone()
      }

      if (path === '/api/den/files') {
        await now.filesAnswer
        return typeof now.fingerprint === 'string' ? { version: 1, fingerprint: now.fingerprint } : now.fingerprint.clone()
      }

      if (path === '/api/entity' && now.entity) {
        return now.entity.clone()
      }

      return path === '/api/entity/source' ? source(now.body) : undefined
    },
  })
  const count = (start: string) => stub.calls.filter((c) => c.startsWith(start)).length
  return { ...stub, now, count }
}

/** What the page's timer does: asks about the files again. */
async function askAgain(client: QueryClient) {
  await act(() => client.refetchQueries({ queryKey: denFilesQuery.queryKey }))
}

const FAILED = () => new Response(JSON.stringify({ code: 'den_unreadable', message: "Couldn't read the den." }), { status: 500 })

describe('SC-WEB-85: an open page shows what an assistant just wrote', () => {
  it('a change to the files refreshes the note without a reload', async () => {
    const { now } = stubDen()
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    expect(await screen.findByText('Before the write.')).toBeTruthy()
    await waitFor(() => expect(queryClient.getQueryData(denFilesQuery.queryKey)).toBeDefined())

    now.body = 'After the write.'
    now.fingerprint = 'files-2'
    await askAgain(queryClient)

    expect(await screen.findByText('After the write.')).toBeTruthy()
    expect(screen.queryByText('Before the write.')).toBeNull()
  })

  it('a den that hasn’t changed asks about its files and nothing else', async () => {
    const { calls, count } = stubDen()
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    await screen.findByText('Before the write.')
    await waitFor(() => expect(count('/api/den/files')).toBe(1))
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))
    const before = calls.length

    await askAgain(queryClient)
    await askAgain(queryClient)

    expect(calls.slice(before)).toEqual(['/api/den/files', '/api/den/files'])
  })

  it('asks at the interval while the page is visible, and not while it’s hidden', async () => {
    expect(LIVE_REFRESH_MS).toBeGreaterThanOrEqual(5_000)
    expect(LIVE_REFRESH_MS).toBeLessThanOrEqual(10_000)
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const { count } = stubDen()
    renderApp('/u/dracula/e/E-MINA')
    await screen.findByText('Before the write.')
    await waitFor(() => expect(count('/api/den/files')).toBe(1))

    await act(() => vi.advanceTimersByTimeAsync(LIVE_REFRESH_MS))
    expect(count('/api/den/files')).toBe(2)

    act(() => focusManager.setFocused(false))
    await act(() => vi.advanceTimersByTimeAsync(LIVE_REFRESH_MS * 3))
    expect(count('/api/den/files')).toBe(2)
  })

  it('if asking fails, the page stays as it is', async () => {
    const { now } = stubDen()
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    await screen.findByText('Before the write.')
    await waitFor(() => expect(queryClient.getQueryData(denFilesQuery.queryKey)).toBeDefined())

    now.fingerprint = FAILED()
    await askAgain(queryClient)

    expect(screen.getByText('Before the write.')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('as of a position, the refresh is still as of that position', async () => {
    const { now, calls } = stubDen()
    const { queryClient } = renderApp('/u/dracula/e/E-MINA?at=dracula:3')
    await screen.findByText('Before the write.')
    await waitFor(() => expect(queryClient.getQueryData(denFilesQuery.queryKey)).toBeDefined())

    now.body = 'After the write.'
    now.fingerprint = 'files-2'
    await askAgain(queryClient)

    await screen.findByText('After the write.')
    const asked = calls.filter((c) => c.startsWith('/api/entity?') && c.includes('E-MINA'))
    expect(asked.length).toBeGreaterThan(1)
    expect(asked.every((c) => c.includes('asOf=dracula%3A3'))).toBe(true)
  })
})

describe('SC-WEB-85: with no network, or the server stopped', () => {
  it('still asks and still refreshes when the browser says it’s offline, since the server is on this computer', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const { now, count } = stubDen()
    renderApp('/u/dracula/e/E-MINA')
    await screen.findByText('Before the write.')
    await waitFor(() => expect(count('/api/den/files')).toBe(1))

    act(() => onlineManager.setOnline(false))
    now.body = 'After the write.'
    now.fingerprint = 'files-2'
    await act(() => vi.advanceTimersByTimeAsync(LIVE_REFRESH_MS))

    expect(count('/api/den/files')).toBe(2)
    expect(await screen.findByText('After the write.')).toBeTruthy()
  })

  it('coming back to the tab with the server stopped leaves the page as it is', async () => {
    const { fetchMock } = stubDen()
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    await screen.findByText('Before the write.')
    await waitFor(() => expect(queryClient.getQueryData(denFilesQuery.queryKey)).toBeDefined())
    fetchMock.mockImplementation(async () => {
      throw new TypeError('Failed to fetch')
    })

    await act(async () => {
      focusManager.setFocused(false)
      focusManager.setFocused(true)
      await new Promise((resolve) => setTimeout(resolve, 50))
    })

    expect(screen.getByText('Before the write.')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('a refresh that failed is tried again, so the page comes back with the server healthy', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const { now } = stubDen()
    const { queryClient } = renderApp('/u/dracula/b/dracula')
    await screen.findByRole('heading', { level: 1, name: 'Dracula' })
    await waitFor(() => expect(queryClient.getQueryData(denFilesQuery.queryKey)).toBeDefined())

    now.hierarchy = FAILED()
    now.fingerprint = 'files-2'
    await askAgain(queryClient)
    expect(await within(screen.getByRole('main')).findByText("Couldn't read the den.")).toBeTruthy()

    now.hierarchy = null
    await act(() => vi.advanceTimersByTimeAsync(RETRY_FAILED_MS + LIVE_REFRESH_MS))
    expect(await screen.findByRole('heading', { level: 1, name: 'Dracula' })).toBeTruthy()
    expect(screen.queryByText("Couldn't read the den.")).toBeNull()
  })
})

describe('SC-WEB-86: an outside write never touches an open editor', () => {
  async function openAndType(client: QueryClient) {
    await waitFor(() => expect(client.getQueryData(denFilesQuery.queryKey)).toBeDefined())
    fireEvent.click(await screen.findByRole('button', { name: 'Edit “Overview”' }))
    const group = await screen.findByRole('group', { name: 'Markdown of the section “Overview”' })
    await waitFor(() => expect(editorViewIn(group)).not.toBeNull())
    const view = editorViewIn(group)!
    await waitFor(() => expect(view.state.doc.toString()).toBe('Before the write.'))
    act(() => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: 'The writer’s own words.' } }))
    return { group, view }
  }

  it('the editor keeps the writer’s text, and the page catches up when it closes', async () => {
    const { now, count } = stubDen()
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    const { group, view } = await openAndType(queryClient)
    const asked = count('/api/entity?')

    // Lines added above move the section, which is how the page finds its editor.
    now.line = 9
    now.body = 'After the write.'
    now.fingerprint = 'files-2'
    await askAgain(queryClient)
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))

    expect(count('/api/entity?')).toBe(asked)
    expect(screen.getByRole('group', { name: 'Markdown of the section “Overview”' })).toBe(group)
    expect(view.state.doc.toString()).toBe('The writer’s own words.')

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Discard changes' }))

    expect(await screen.findByText('After the write.')).toBeTruthy()
    expect(count('/api/entity?')).toBeGreaterThan(asked)
  })

  it('getting the window’s focus back refreshes nothing while the editor is open', async () => {
    const { count } = stubDen()
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    const { view } = await openAndType(queryClient)
    const entity = count('/api/entity')

    act(() => {
      focusManager.setFocused(false)
      focusManager.setFocused(true)
    })
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))

    expect(count('/api/entity')).toBe(entity)
    expect(view.state.doc.toString()).toBe('The writer’s own words.')
  })

  it('an answer that arrives anyway can’t move the section or fail the page out from under it', async () => {
    const { now } = stubDen()
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    const { group, view } = await openAndType(queryClient)

    now.line = 9
    await act(() => queryClient.refetchQueries({ queryKey: ['entity'] }))
    expect(screen.getByRole('group', { name: 'Markdown of the section “Overview”' })).toBe(group)

    now.entity = FAILED()
    await act(() => queryClient.refetchQueries({ queryKey: ['entity'] }))
    expect(screen.getByRole('group', { name: 'Markdown of the section “Overview”' })).toBe(group)
    expect(view.state.doc.toString()).toBe('The writer’s own words.')
  })

  it('the note deleted and the network changing: the text stays on screen, and Cancel and the hold still work', async () => {
    const { now, count } = stubDen()
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    const { view } = await openAndType(queryClient)
    const gone = () => new Response(JSON.stringify({ code: 'unknown_entity', message: 'There is no note E-MINA.' }), { status: 404 })
    now.entity = gone()
    now.source = gone()
    now.fingerprint = 'files-2'
    await askAgain(queryClient)
    const asked = count('/api/entity')

    act(() => {
      onlineManager.setOnline(false)
      onlineManager.setOnline(true)
    })
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))
    expect(count('/api/entity')).toBe(asked)
    expect(screen.getByRole('group', { name: 'Markdown of the section “Overview”' })).toBeTruthy()

    // Even if the editor's own question is asked again and fails.
    await act(() => queryClient.refetchQueries({ queryKey: ['source'] }))
    const group = screen.getByRole('group', { name: 'Markdown of the section “Overview”' })
    expect(editorViewIn(group)).toBe(view)
    expect(view.state.doc.toString()).toBe('The writer’s own words.')

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Discard changes' }))

    // The hold is off: the page says the note is gone.
    expect(await screen.findByText('There is no note E-MINA.')).toBeTruthy()
  })

  it('a form opened before the first answer about the files still catches up when it closes', async () => {
    const { now } = stubDen()
    let answer = () => {}
    now.filesAnswer = new Promise((resolve) => {
      answer = resolve
    })
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    fireEvent.click(await screen.findByRole('button', { name: 'Edit “Overview”' }))
    await screen.findByRole('group', { name: 'Markdown of the section “Overview”' })

    now.filesAnswer = null
    await act(async () => {
      answer()
      await new Promise((resolve) => setTimeout(resolve, 20))
    })
    now.body = 'After the write.'
    now.fingerprint = 'files-2'
    await askAgain(queryClient)

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(await screen.findByText('After the write.')).toBeTruthy()
  })

  /** The den's hierarchy starts answering slowly and then fails, with a refresh under way. */
  async function refreshThatWillFail(client: QueryClient, now: ReturnType<typeof stubDen>['now']) {
    await waitFor(() => expect(client.getQueryData(denFilesQuery.queryKey)).toBeDefined())
    let fail = () => {}
    now.hierarchyAnswer = new Promise((resolve) => {
      fail = resolve
    })
    now.hierarchy = FAILED()
    now.fingerprint = 'files-2'
    await askAgain(client)
    return fail
  }

  it('a failing refresh already under way leaves a chapter field’s form, and the error waits for it to close', async () => {
    const { now } = stubDen()
    const { queryClient } = renderApp('/u/dracula/b/dracula/c/2')
    await screen.findByRole('button', { name: 'Edit tense' })
    const fail = await refreshThatWillFail(queryClient, now)
    fireEvent.click(screen.getByRole('button', { name: 'Edit tense' }))
    const form = screen.getByRole('form', { name: 'Edit tense' })
    fireEvent.change(within(form).getByLabelText('Value'), { target: { value: 'present' } })

    await act(async () => {
      fail()
      await new Promise((resolve) => setTimeout(resolve, 50))
    })

    expect(screen.getByRole('form', { name: 'Edit tense' })).toBe(form)
    expect((within(form).getByLabelText('Value') as HTMLSelectElement).value).toBe('present')
    expect(screen.queryByText("Couldn't read the den.")).toBeNull()

    fireEvent.click(within(form).getByRole('button', { name: 'Cancel' }))
    const discard = await screen.findByRole('alertdialog').catch(() => null)
    if (discard) {
      fireEvent.click(within(discard).getByRole('button', { name: /Discard/ }))
    }

    expect(await within(screen.getByRole('main')).findByText("Couldn't read the den.")).toBeTruthy()
  })

  it('the same on a book page, for Add chapter', async () => {
    const { now } = stubDen()
    const { queryClient } = renderApp('/u/dracula/b/dracula')
    const open = await screen.findByRole('button', { name: /Add chapter/ })
    const fail = await refreshThatWillFail(queryClient, now)
    fireEvent.click(open)
    const title = (await screen.findByRole('textbox', { name: /title/i })) as HTMLInputElement
    fireEvent.change(title, { target: { value: 'The Quiet Hour' } })

    await act(async () => {
      fail()
      await new Promise((resolve) => setTimeout(resolve, 50))
    })

    expect(screen.getByRole('textbox', { name: /title/i })).toBe(title)
    expect(title.value).toBe('The Quiet Hour')
    expect(screen.queryByText("Couldn't read the den.")).toBeNull()
  })

  it('a chapter field’s form holds the refresh back too', async () => {
    const { now, count } = stubDen()
    const { queryClient } = renderApp('/u/dracula/b/dracula/c/2')
    await waitFor(() => expect(queryClient.getQueryData(denFilesQuery.queryKey)).toBeDefined())
    expect(bookChapterFields().chapters.length).toBeGreaterThan(1)
    fireEvent.click(await screen.findByRole('button', { name: 'Edit tense' }))
    const form = screen.getByRole('form', { name: 'Edit tense' })
    fireEvent.change(within(form).getByLabelText('Value'), { target: { value: 'present' } })
    const asked = count('/api/hierarchy')

    now.fingerprint = 'files-2'
    await askAgain(queryClient)
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))

    expect(count('/api/hierarchy')).toBe(asked)
    expect((within(form).getByLabelText('Value') as HTMLSelectElement).value).toBe('present')

    fireEvent.click(within(form).getByRole('button', { name: 'Cancel' }))
    const discard = await screen.findByRole('alertdialog').catch(() => null)
    if (discard) {
      fireEvent.click(within(discard).getByRole('button', { name: /Discard/ }))
    }

    await waitFor(() => expect(count('/api/hierarchy')).toBeGreaterThan(asked))
  })
})

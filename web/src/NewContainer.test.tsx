// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { BookNode, HierarchyResult, SeriesNode } from './api'
import type { ContainerCreateResult, WorksView } from './containerApi'
import { hierarchy, renderApp, stubApi } from './test/harness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const GUEST_PATH = 'universes/dracula/books/dracula-s-guest/book.md'

const guest: BookNode = {
  key: 'dracula-s-guest',
  name: "Dracula's Guest",
  id: 'B2',
  path: 'universes/dracula/books/dracula-s-guest',
  status: 'draft',
  position: 'dracula-s-guest',
  entities: { total: 0, byType: {} },
  chapters: [1, 2, 3].map((n) => ({ key: String(n), number: n, title: null, position: `dracula-s-guest:${n}` })),
}

const papers: SeriesNode = {
  key: 'the-harker-papers',
  name: 'The Harker Papers',
  id: 'S1',
  path: 'universes/dracula/series/the-harker-papers',
  entities: { total: 0, byType: {} },
  books: [],
}

function made(extra: Partial<ContainerCreateResult> = {}): ContainerCreateResult {
  return {
    version: 1,
    universe: 'dracula',
    kind: 'book',
    key: 'dracula-s-guest',
    name: "Dracula's Guest",
    series: null,
    path: GUEST_PATH,
    hash: 'h-new',
    id: 'B2',
    chapters: 3,
    effects: [
      `Created ${GUEST_PATH}: book 'Dracula's Guest', key 'dracula-s-guest', with id B2. It has 3 chapters, dracula-s-guest:1 to dracula-s-guest:3.`,
      'universes/dracula/universe.md had no order list, so one was written: [dracula, dracula-s-guest].',
    ],
    warnings: [],
    files: [
      { path: GUEST_PATH, hash: 'h-new', created: true },
      { path: 'universes/dracula/universe.md', hash: 'h-u2', created: false },
    ],
    idAdded: null,
    ...extra,
  }
}

/**
 * Answers /api/works and the creates; after a create, the hierarchy holds what was made, so its page opens.
 * Records each create's path and body.
 */
function stubCreate(post: (path: string, body: Record<string, unknown>) => Response | ContainerCreateResult = () => made(), start: HierarchyResult = hierarchy) {
  const posted: { path: string; body: Record<string, unknown> }[] = []
  let tree = start
  const api = stubApi({
    override: (path, params, init) => {
      if (path === '/api/series/create' || path === '/api/book/create') {
        expect(init?.method).toBe('POST')
        expect(new Headers(init?.headers).get('Content-Type')).toBe('application/json')
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>
        posted.push({ path, body })
        const answer = post(path, body)
        if (!(answer instanceof Response)) {
          const u = tree.universe
          tree =
            answer.kind === 'series'
              ? { ...tree, universe: { ...u, series: [...u.series, papers] } }
              : answer.series
                ? { ...tree, universe: { ...u, series: u.series.map((s) => (s.key === answer.series ? { ...s, books: [...s.books, guest] } : s)) } }
                : { ...tree, universe: { ...u, books: [...u.books, guest] } }
        }

        return answer
      }

      if (path === '/api/hierarchy') {
        return { ...tree, position: params.get('asOf') }
      }

      if (path === '/api/works') {
        const series = params.get('series')
        const u = tree.universe
        const works: WorksView = series
          ? {
              version: 1,
              universe: 'dracula',
              series,
              name: u.series.find((s) => s.key === series)!.name,
              path: `universes/dracula/series/${series}/series.md`,
              hash: 'h-s',
              works: u.series.find((s) => s.key === series)!.books.map((b) => ({ kind: 'book', key: b.key, name: b.name })),
            }
          : {
              version: 1,
              universe: 'dracula',
              series: null,
              name: 'Dracula',
              path: 'universes/dracula/universe.md',
              hash: 'h-u',
              works: [
                ...u.series.map((s) => ({ kind: 'series' as const, key: s.key, name: s.name })),
                ...u.books.map((b) => ({ kind: 'book' as const, key: b.key, name: b.name })),
              ],
            }
        return works
      }

      return undefined
    },
  })
  return { ...api, posted }
}

describe('SC-WEB-69: I add a book or a series from its universe or series', () => {
  it("the universe's New book makes a standalone book with its chapters, at the end, and opens it", async () => {
    const { posted, calls } = stubCreate()
    const { history } = renderApp('/u/dracula')
    await screen.findByRole('heading', { name: 'Dracula', level: 1 })

    const opener = screen.getByRole('button', { name: 'New book in Dracula' })
    opener.focus()
    fireEvent.click(opener)
    const form = screen.getByRole('dialog')
    expect(within(form).getByRole('heading', { name: 'New book in Dracula' })).toBeTruthy()
    const name = within(form).getByLabelText('Name') as HTMLInputElement
    await waitFor(() => expect(document.activeElement).toBe(name))

    // Place after: at the end by default, or after any work.
    const after = (await within(form).findByLabelText('Place after…')) as HTMLSelectElement
    expect(after.value).toBe('')
    expect([...after.options].map((o) => o.text)).toEqual(['At the end, after Dracula', 'Dracula (book)'])

    // A name is required, and chapters are a whole number from 1 to 1000: nothing is sent otherwise.
    fireEvent.click(within(form).getByRole('button', { name: 'Create' }))
    expect(within(form).getByRole('alert').textContent).toContain('Give the book a name')
    fireEvent.change(name, { target: { value: " Dracula's Guest " } })
    const chapters = within(form).getByLabelText('Number of chapters') as HTMLInputElement
    for (const bad of ['0', '2.5', '1001']) {
      fireEvent.change(chapters, { target: { value: bad } })
      fireEvent.click(within(form).getByRole('button', { name: 'Create' }))
      expect(within(form).getByRole('alert').textContent).toContain('whole number from 1 to 1000')
      expect(document.activeElement).toBe(chapters)
    }

    expect(posted).toEqual([])

    fireEvent.change(chapters, { target: { value: '3' } })
    const before = calls.filter((c) => c.startsWith('/api/hierarchy')).length
    fireEvent.click(within(form).getByRole('button', { name: 'Create' }))

    await waitFor(() => expect(history.location.pathname).toBe('/u/dracula/b/dracula-s-guest'))
    expect(posted).toEqual([{ path: '/api/book/create', body: { name: "Dracula's Guest", universe: 'dracula', chapters: 3, expectedHash: 'h-u' } }])
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(calls.filter((c) => c.startsWith('/api/hierarchy')).length).toBeGreaterThan(before)

    // The new book's page, with a notice naming its file and where it went, which takes the focus.
    expect(await screen.findByRole('heading', { name: "Dracula's Guest", level: 1 })).toBeTruthy()
    const notice = document.querySelector<HTMLElement>('.created-notice')!
    expect(notice.getAttribute('role')).toBe('status')
    expect(notice.textContent).toContain(`Created ${GUEST_PATH}.`)
    expect(notice.textContent).toContain('[dracula, dracula-s-guest]')
    await waitFor(() => expect(document.activeElement).toBe(notice))

    // Leaving the page ends the notice.
    history.push('/u/dracula')
    await waitFor(() => expect(document.querySelector('.created-notice')).toBeNull())
  })

  it("the universe's New series places it after the work I choose, and a series' New book goes in it", async () => {
    const { posted } = stubCreate((path, body) =>
      path === '/api/series/create'
        ? made({ kind: 'series', key: 'the-harker-papers', name: 'The Harker Papers', path: 'universes/dracula/series/the-harker-papers/series.md', chapters: 0 })
        : made({ series: String(body.series), path: 'universes/dracula/series/the-harker-papers/books/dracula-s-guest/book.md', chapters: 0 }),
    )
    const { history } = renderApp('/u/dracula')
    await screen.findByRole('heading', { name: 'Dracula', level: 1 })

    fireEvent.click(screen.getByRole('button', { name: 'New series in Dracula' }))
    let form = screen.getByRole('dialog')
    expect(within(form).queryByLabelText('Number of chapters')).toBeNull()
    fireEvent.change(within(form).getByLabelText('Name'), { target: { value: 'The Harker Papers' } })
    fireEvent.change(await within(form).findByLabelText('Place after…'), { target: { value: 'dracula' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Create' }))

    await waitFor(() => expect(history.location.pathname).toBe('/u/dracula/s/the-harker-papers'))
    expect(posted[0]).toEqual({ path: '/api/series/create', body: { name: 'The Harker Papers', universe: 'dracula', after: 'dracula', expectedHash: 'h-u' } })
    expect(await screen.findByRole('heading', { name: 'The Harker Papers', level: 1 })).toBeTruthy()

    // The series has no books, so there's nothing to place it after, and chapters are optional.
    fireEvent.click(screen.getByRole('button', { name: 'New book in The Harker Papers' }))
    form = screen.getByRole('dialog')
    expect(await within(form).findByText('The Harker Papers has no books yet, so this is the first.')).toBeTruthy()
    expect(within(form).queryByLabelText('Place after…')).toBeNull()
    fireEvent.change(within(form).getByLabelText('Name'), { target: { value: "Dracula's Guest" } })
    fireEvent.click(within(form).getByRole('button', { name: 'Create' }))

    await waitFor(() => expect(history.location.pathname).toBe('/u/dracula/b/dracula-s-guest'))
    expect(posted[1]).toEqual({
      path: '/api/book/create',
      body: { name: "Dracula's Guest", universe: 'dracula', series: 'the-harker-papers', expectedHash: 'h-s' },
    })
  })
})

describe('SC-WEB-70: New series and New book are guarded', () => {
  it('a refusal shows in the form, which keeps the name and asks for the story order again', async () => {
    let answer: Response | null = new Response(
      JSON.stringify({ code: 'stale_file', message: 'universes/dracula/universe.md has changed since it was read.' }),
      { status: 409 },
    )
    const { posted, calls } = stubCreate(() => answer ?? made())
    renderApp('/u/dracula')
    await screen.findByRole('heading', { name: 'Dracula', level: 1 })

    const opener = screen.getByRole('button', { name: 'New book in Dracula' })
    opener.focus()
    fireEvent.click(opener)
    const form = screen.getByRole('dialog')
    const name = within(form).getByLabelText('Name') as HTMLInputElement
    await within(form).findByLabelText('Place after…')
    fireEvent.change(name, { target: { value: "Dracula's Guest" } })
    const works = calls.filter((c) => c.startsWith('/api/works')).length
    fireEvent.click(within(form).getByRole('button', { name: 'Create' }))

    const alert = await within(form).findByRole('alert')
    expect(alert.textContent).toContain('The story order changed in another program since this form opened')
    expect(name.value).toBe("Dracula's Guest")
    await waitFor(() => expect(calls.filter((c) => c.startsWith('/api/works')).length).toBeGreaterThan(works))

    // Another refusal is shown as the server words it.
    answer = new Response(JSON.stringify({ code: 'already_exists', message: "Universe 'dracula' already has a book 'dracula-s-guest'." }), { status: 409 })
    fireEvent.click(within(form).getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(within(form).getByRole('alert').textContent).toContain("already has a book 'dracula-s-guest'"))
    expect(posted).toHaveLength(2)

    // Escape closes it, sends nothing more, and returns focus to the button.
    fireEvent.keyDown(form, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(opener))
    expect(posted).toHaveLength(2)
  })
})

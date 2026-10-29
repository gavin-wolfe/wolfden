// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { HierarchyResult } from './api'
import type { BookChapters, ChapterAddResult } from './containerApi'
import { hierarchy, renderApp, stubApi } from './test/harness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const BOOK = 'universes/dracula/books/dracula/book.md'

const chapters: BookChapters = {
  version: 1,
  universe: 'dracula',
  book: 'dracula',
  path: BOOK,
  hash: 'h-book',
  final: false,
  chapters: hierarchy.universe.books[0].chapters.map((c) => ({ key: c.key, number: c.number, title: c.title })),
}

function added(extra: Partial<ChapterAddResult> = {}): ChapterAddResult {
  return {
    version: 1,
    universe: 'dracula',
    book: 'dracula',
    path: BOOK,
    hash: 'h-after',
    key: '2b',
    position: 'dracula:2b',
    number: 3,
    title: 'A Letter',
    index: 2,
    renumbered: [{ key: '3', from: 3, to: 4 }],
    effects: [
      "Added chapter '2b' to book 'dracula', after '2', with number 3 and the title 'A Letter'. Its position is dracula:2b, and its key never changes.",
      "Renumbered the chapters after it, keys unchanged, so positions still name the same chapters: '3' 3 → 4.",
    ],
    warnings: [],
    files: [{ path: BOOK, hash: 'h-after', created: false }],
    idAdded: null,
    ...extra,
  }
}

/** Answers /api/book/chapters and the add, recording each add's body. */
function stubAdd(
  post: (body: Record<string, unknown>) => Response | ChapterAddResult = () => added(),
  tree: HierarchyResult = hierarchy,
  book: () => BookChapters = () => chapters,
) {
  const posted: Record<string, unknown>[] = []
  const api = stubApi({
    override: (path, params, init) => {
      if (path === '/api/chapter/create') {
        expect(init?.method).toBe('POST')
        expect(new Headers(init?.headers).get('Content-Type')).toBe('application/json')
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>
        posted.push(body)
        return post(body)
      }

      if (path === '/api/book/chapters') {
        expect(params.get('book')).toBe('dracula')
        return book()
      }

      if (path === '/api/hierarchy') {
        return { ...tree, position: params.get('asOf') }
      }

      return undefined
    },
  })
  return { ...api, posted }
}

/** The notice of what the add did (the page has other status regions). */
async function findNotice(): Promise<HTMLElement> {
  const line = await screen.findByText(/^Added chapter /, { selector: 'p' })
  return line.closest<HTMLElement>('[role="status"]')!
}

describe('SC-WEB-71: I add and insert chapters on a book page', () => {
  it('Add chapter puts one at the end, with the title, and says what it did', async () => {
    const { posted, calls } = stubAdd(() => added({ key: '6', position: 'dracula:6', number: 6, index: 6, renumbered: [], effects: ['Added chapter 6.'] }))
    renderApp('/u/dracula/b/dracula')
    await screen.findByRole('heading', { name: 'Dracula', level: 1 })

    const opener = screen.getByRole('button', { name: 'Add chapter to Dracula' })
    opener.focus()
    fireEvent.click(opener)
    const form = screen.getByRole('dialog')
    expect(within(form).getByRole('heading', { name: 'Add a chapter to Dracula' })).toBeTruthy()
    const title = within(form).getByLabelText('Title') as HTMLInputElement
    await waitFor(() => expect(document.activeElement).toBe(title))
    // With no error, the title points at its hint.
    expect(document.getElementById(title.getAttribute('aria-describedby')!)?.textContent).toContain('Optional; leave it empty for none.')

    // The place is the end by default, and the list names every chapter but the last, by number, title, and key.
    const place = (await within(form).findByLabelText('Place')) as HTMLSelectElement
    expect(place.value).toBe('')
    expect([...place.options].map((o) => o.textContent)).toEqual([
      'At the end, after Note (key note)',
      "After 1. Jonathan Harker's Journal (key 1)",
      "After 2. Jonathan Harker's Journal (key 2)",
      "After 3. Jonathan Harker's Journal (key 3)",
      "After 4. Jonathan Harker's Journal (key 4)",
      'After 5. Letters—Lucy and Mina (key 5)',
    ])

    fireEvent.change(title, { target: { value: '  Afterword  ' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Add' }))

    const notice = await findNotice()
    expect(notice.textContent).toContain(`Added chapter dracula:6 to ${BOOK}.`)
    expect(notice.textContent).toContain('Added chapter 6.')
    await waitFor(() => expect(document.activeElement).toBe(notice))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(posted).toEqual([{ universe: 'dracula', book: 'dracula', title: 'Afterword', expectedHash: 'h-book' }])
    // The page asks again for what changed.
    expect(calls.filter((c) => c.startsWith('/api/hierarchy')).length).toBeGreaterThan(1)

    fireEvent.click(within(notice).getByRole('button', { name: 'Dismiss' }))
    expect(screen.queryByText(/^Added chapter /)).toBeNull()
  })

  it('Insert chapter… starts at the first chapter, sends after, and leaves an empty title out', async () => {
    const { posted } = stubAdd()
    renderApp('/u/dracula/b/dracula')
    await screen.findByRole('heading', { name: 'Dracula', level: 1 })

    fireEvent.click(screen.getByRole('button', { name: 'Insert chapter in Dracula' }))
    const form = screen.getByRole('dialog')
    expect(within(form).getByRole('heading', { name: 'Insert a chapter in Dracula' })).toBeTruthy()
    const place = (await within(form).findByLabelText('Place')) as HTMLSelectElement
    await waitFor(() => expect(document.activeElement).toBe(place))
    expect(place.value).toBe('1')

    fireEvent.change(place, { target: { value: '2' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Add' }))

    const notice = await findNotice()
    expect(notice.textContent).toContain("'3' 3 → 4")
    expect(posted).toEqual([{ universe: 'dracula', book: 'dracula', after: '2', expectedHash: 'h-book' }])
  })

  it('a final book has no Add chapter, and a book with one chapter no Insert chapter…', async () => {
    const u = hierarchy.universe
    const final: HierarchyResult = { ...hierarchy, universe: { ...u, books: [{ ...u.books[0], status: 'final' }] } }
    stubAdd(undefined, final)
    renderApp('/u/dracula/b/dracula')
    await screen.findByText('Book, final')
    expect(screen.queryByRole('button', { name: 'Add chapter to Dracula' })).toBeNull()
    cleanup()
    vi.unstubAllGlobals()

    const one: HierarchyResult = { ...hierarchy, universe: { ...u, books: [{ ...u.books[0], chapters: u.books[0].chapters.slice(0, 1) }] } }
    stubAdd(undefined, one)
    renderApp('/u/dracula/b/dracula')
    await screen.findByRole('button', { name: 'Add chapter to Dracula' })
    expect(screen.queryByRole('button', { name: 'Insert chapter in Dracula' })).toBeNull()
  })
})

describe('SC-WEB-72: adding a chapter is guarded', () => {
  it('a title over 200 characters is refused before anything is sent', async () => {
    const { posted } = stubAdd()
    renderApp('/u/dracula/b/dracula')
    fireEvent.click(await screen.findByRole('button', { name: 'Add chapter to Dracula' }))
    const form = screen.getByRole('dialog')
    await within(form).findByLabelText('Place')
    const title = within(form).getByLabelText('Title')
    fireEvent.change(title, { target: { value: 'x'.repeat(201) } })
    fireEvent.click(within(form).getByRole('button', { name: 'Add' }))

    expect(within(form).getByRole('alert').textContent).toContain('at most 200 characters')
    expect(title.getAttribute('aria-invalid')).toBe('true')
    expect(posted).toEqual([])
  })

  it("a refusal is shown, a stale book.md says so in the form's words, and Escape returns focus", async () => {
    // The server's words for the web page (WriteEndpoints.Words, #209) say how to unfreeze it; the form shows them as they are.
    const frozen =
      "Book 'dracula' is final (frozen), so no chapter can be added to it until it's unfrozen (F-BOOK-1). Unfreeze… on the page of book 'dracula' opens it for editing again."
    let answer: Response = new Response(JSON.stringify({ code: 'frozen', message: frozen }), { status: 409 })
    const { posted, calls } = stubAdd(() => answer)
    renderApp('/u/dracula/b/dracula')
    const opener = await screen.findByRole('button', { name: 'Add chapter to Dracula' })
    opener.focus()
    fireEvent.click(opener)
    const form = screen.getByRole('dialog')
    await within(form).findByLabelText('Place')
    fireEvent.click(within(form).getByRole('button', { name: 'Add' }))

    expect((await within(form).findByRole('alert')).textContent).toBe(frozen)
    expect(posted).toHaveLength(1)

    // A stale file: the form asks for the chapters again.
    answer = new Response(JSON.stringify({ code: 'stale_file', message: 'book.md has changed since it was read' }), { status: 409 })
    const before = calls.filter((c) => c.startsWith('/api/book/chapters')).length
    fireEvent.click(within(form).getByRole('button', { name: 'Add' }))
    await waitFor(() => expect(within(form).getByRole('alert').textContent).toContain("The book's chapters changed in another program since this form opened"))
    await waitFor(() => expect(calls.filter((c) => c.startsWith('/api/book/chapters')).length).toBeGreaterThan(before))

    fireEvent.keyDown(form, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(opener))
  })

  it('a chosen chapter that is gone after a stale refetch falls back to the end, and the form says so', async () => {
    let book = chapters
    stubAdd(
      () => new Response(JSON.stringify({ code: 'stale_file', message: 'book.md has changed since it was read' }), { status: 409 }),
      hierarchy,
      () => book,
    )
    renderApp('/u/dracula/b/dracula')
    fireEvent.click(await screen.findByRole('button', { name: 'Insert chapter in Dracula' }))
    const form = screen.getByRole('dialog')
    const place = (await within(form).findByLabelText('Place')) as HTMLSelectElement
    fireEvent.change(place, { target: { value: '2' } })

    // Meanwhile chapter 2 was taken out of book.md in another program.
    book = { ...chapters, hash: 'h-later', chapters: chapters.chapters.filter((c) => c.key !== '2') }
    fireEvent.click(within(form).getByRole('button', { name: 'Add' }))

    await waitFor(() => expect(place.value).toBe(''))
    const note = within(form).getByText(/^Chapter 2 can't be chosen now/)
    expect(note.textContent).toContain('so the new chapter goes at the end')
    expect(place.getAttribute('aria-describedby')).toBe(note.id)

    // Choosing a place again clears it.
    fireEvent.change(place, { target: { value: '3' } })
    expect(within(form).queryByText(/^Chapter 2 can't be chosen now/)).toBeNull()
  })
})

// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { HierarchyResult } from './api'
import { hierarchy, renderApp, stubApi } from './test/harness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

// The headings the API would build from `chapter_heading: [pov, setting, when]`
// (#227): in full, and as of dracula:1, where chapter 2 onward show their
// titles alone and a link to a note the reader hasn't met shows as written.
const FULL = [
  "Jonathan Harker's Journal · Jonathan Harker — Bistritz & Castle Dracula, 3–5 May",
  "Jonathan Harker's Journal · Jonathan Harker — Castle Dracula, 5–8 May",
]
const AS_OF_1 = ["Jonathan Harker's Journal · Jonathan Harker — bistritz & Castle Dracula, 3–5 May", "Jonathan Harker's Journal"]

function withHeadings(headings: string[], at: string | null): HierarchyResult {
  const [book] = hierarchy.universe.books
  return {
    ...hierarchy,
    position: at,
    universe: {
      ...hierarchy.universe,
      books: [{ ...book, chapters: book.chapters.map((c, i) => ({ ...c, heading: headings[i] ?? c.heading })) }],
    },
  }
}

function stubHeadings() {
  return stubApi({
    override: (path, params) => {
      if (path === '/api/hierarchy') {
        const at = params.get('asOf')
        return at === 'dracula:1' ? withHeadings(AS_OF_1, at) : withHeadings(FULL, at)
      }

      return undefined
    },
  })
}

/**
 * The den after `new book "Tide" --chapters 4` and `new chapter --in tide --after 1`, in the test's one book:
 * none has a title, the key '1b' is the second, and the key '3' is the fourth. The last has no number.
 */
function untitled(): HierarchyResult {
  const [book] = hierarchy.universe.books
  const chapter = (key: string, number: number | null) => ({ key, number, title: null, position: `dracula:${key}`, heading: null })
  return {
    ...hierarchy,
    universe: {
      ...hierarchy.universe,
      books: [{ ...book, chapters: [chapter('1', 1), chapter('1b', 2), chapter('2', 3), chapter('3', 4), chapter('4', 5), chapter('coda', null)] }],
    },
  }
}

function stubUntitled() {
  return stubApi({ override: (path, params) => (path === '/api/hierarchy' ? { ...untitled(), position: params.get('asOf') } : undefined) })
}

describe('SC-WEB-83: untitled chapters read the same everywhere', () => {
  it('the book page and the contents tree call a chapter by its number, not its key', async () => {
    stubUntitled()
    renderApp('/u/dracula/b/dracula')

    const main = await screen.findByRole('main')
    await within(main).findByText('Chapter coda')
    const list = [...main.querySelectorAll('.chapter-title')].map((t) => t.textContent)
    expect(list).toEqual(['Chapter 1', 'Chapter 2', 'Chapter 3', 'Chapter 4', 'Chapter 5', 'Chapter coda'])

    const contents = await screen.findByRole('navigation', { name: 'Contents' })
    const tree = await within(contents).findAllByRole('link', { name: /Chapter/ })
    expect(tree.map((a) => a.textContent)).toEqual(['1. Chapter 1', '2. Chapter 2', '3. Chapter 3', '4. Chapter 4', '5. Chapter 5', 'Chapter coda'])
  })

  it('Add chapter’s Place list uses the number, and still gives the key', async () => {
    stubApi({
      override: (path, params) => {
        if (path === '/api/hierarchy') {
          return { ...untitled(), position: params.get('asOf') }
        }

        return path === '/api/book/chapters'
          ? { version: 1, universe: 'dracula', book: 'dracula', path: 'universes/dracula/books/dracula/book.md', hash: 'h', chapters: untitled().universe.books[0].chapters.map(({ key, number, title }) => ({ key, number, title })) }
          : undefined
      },
    })
    renderApp('/u/dracula/b/dracula')

    fireEvent.click(await screen.findByRole('button', { name: /Add chapter/ }))
    expect(await screen.findByRole('option', { name: 'After 4. Chapter 4 (key 3)' })).toBeTruthy()
  })

  it('the page and the tab agree, and prev and next have labels', async () => {
    stubUntitled()
    renderApp('/u/dracula/b/dracula/c/3')

    expect(await screen.findByRole('heading', { level: 1, name: 'Chapter 4' })).toBeTruthy()
    await waitFor(() => expect(document.title).toBe('Chapter 4 · Dracula · Wolfden'))
    const nav = screen.getByRole('navigation', { name: 'Chapters' })
    expect(within(nav).getAllByRole('link').map((a) => a.textContent)).toEqual(['‹ Chapter 3', 'Chapter 5 ›'])
  })

  it('a chapter with no number goes by its key, everywhere', async () => {
    stubUntitled()
    renderApp('/u/dracula/b/dracula/c/coda')

    expect(await screen.findByRole('heading', { level: 1, name: 'Chapter coda' })).toBeTruthy()
    await waitFor(() => expect(document.title).toBe('Chapter coda · Dracula · Wolfden'))
    expect(within(screen.getByRole('navigation', { name: 'Chapters' })).getByRole('link').textContent).toBe('‹ Chapter 5')
  })

  it('as of an earlier position, a later chapter’s heading names nothing the position hides', async () => {
    // As of dracula:1, chapter 2 has its title alone: prev and next and the tab show that, not the full heading.
    stubHeadings()
    renderApp('/u/dracula/b/dracula/c/1?at=dracula:1')

    const nav = await screen.findByRole('navigation', { name: 'Chapters' })
    await waitFor(() => expect(within(nav).getByRole('link', { name: `2. ${AS_OF_1[1]} ›` })).toBeTruthy())
    expect(document.body.textContent).not.toContain(FULL[1])
    expect(document.title).not.toContain(FULL[1])
  })
})

describe('SC-WEB-77: chapter headings built from chapter fields', () => {
  it('the book page lists each chapter by its heading', async () => {
    stubHeadings()
    renderApp('/u/dracula/b/dracula')

    expect(await screen.findByText(FULL[0])).toBeTruthy()
    expect(screen.getByText(FULL[1])).toBeTruthy()
    // A chapter whose heading is its title alone shows the title.
    expect(screen.getByText('Letters—Lucy and Mina')).toBeTruthy()
  })

  it('as of a position, the book page shows the headings the API gives as of it', async () => {
    const { calls } = stubHeadings()
    renderApp('/u/dracula/b/dracula?at=dracula:1')

    expect(await screen.findByText(AS_OF_1[0])).toBeTruthy()
    expect(screen.queryByText(FULL[0])).toBeNull()
    expect(screen.queryByText(FULL[1])).toBeNull()
    expect(calls).toContain('/api/hierarchy?universe=dracula&asOf=dracula%3A1')
  })

  it('the chapter page heads itself with its heading, and its neighbours with theirs', async () => {
    stubHeadings()
    renderApp('/u/dracula/b/dracula/c/1')

    expect((await screen.findByRole('heading', { level: 1, name: FULL[0] })).textContent).toBe(FULL[0])
    const nav = screen.getByRole('navigation', { name: 'Chapters' })
    expect(within(nav).getByRole('link', { name: `2. ${FULL[1]} ›` })).toBeTruthy()
    await waitFor(() => expect(document.title).toContain(FULL[0]))
  })

  it('while the headings as of a position are on their way, or if they fail, the chapter page shows titles', async () => {
    let release: () => void = () => {}
    const pending = new Promise<void>((resolve) => {
      release = resolve
    })
    stubApi({
      override: async (path, params) => {
        if (path === '/api/hierarchy') {
          if (params.get('asOf') === 'dracula:1') {
            await pending
            return new Response(JSON.stringify({ code: 'bad_position', message: 'No such position.' }), { status: 400 })
          }

          return withHeadings(FULL, null)
        }

        return undefined
      },
    })
    renderApp('/u/dracula/b/dracula/c/1?at=dracula:1')

    const title = "Jonathan Harker's Journal"
    expect((await screen.findByRole('heading', { level: 1 })).textContent).toBe(title)
    const nav = screen.getByRole('navigation', { name: 'Chapters' })
    expect(within(nav).getByRole('link', { name: `2. ${title} ›` })).toBeTruthy()
    expect(screen.queryByText(FULL[0])).toBeNull()

    release()
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe(title)
    expect(within(nav).getByRole('link', { name: `2. ${title} ›` })).toBeTruthy()
    expect(document.body.textContent).not.toContain('Bistritz')
  })

  it('as of a position, the chapter page names only what the reader has met', async () => {
    stubHeadings()
    renderApp('/u/dracula/b/dracula/c/1?at=dracula:1')

    expect(await screen.findByRole('heading', { level: 1, name: AS_OF_1[0] })).toBeTruthy()
    const nav = screen.getByRole('navigation', { name: 'Chapters' })
    expect(within(nav).getByRole('link', { name: `2. ${AS_OF_1[1]} ›` })).toBeTruthy()
    expect(screen.queryByText(FULL[0])).toBeNull()
    expect(document.title).not.toContain('Bistritz')
  })
})

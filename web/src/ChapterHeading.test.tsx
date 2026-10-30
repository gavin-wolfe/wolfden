// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
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

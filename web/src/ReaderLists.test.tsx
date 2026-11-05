// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { MarkedEntity } from './api'
import { entities, hierarchy, renderApp, stubApi } from './test/harness'
import { MARKS_LEGEND } from './readerWords'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

async function tree() {
  return screen.findByRole('navigation', { name: 'Contents' })
}

const readersIn = (history: { location: { search: string } }) => new URLSearchParams(history.location.search).get('readers')
const listCalls = (calls: string[]) => calls.filter((c) => c.startsWith('/api/entities?'))
const LATER_NAMES = ['Lucy Westenra', 'Quincey P. Morris', 'Quincey Harker', 'A scene at the inn']

describe('SC-WEB-104: in the Author’s view, a list has everything, what readers have met first', () => {
  it('lists later notes after the ones readers have met, tagged, with nothing to open', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula?at=dracula:3')
    const contents = await tree()
    const jonathan = await within(contents).findByRole('link', { name: 'Jonathan Harker' })

    const rows = [...jonathan.closest('ul')!.querySelectorAll('li')].map((li) => li.querySelector('a')!.textContent)
    expect(rows).toEqual(['Jonathan Harker', 'Mina Harker', 'Lucy Westenra', 'Quincey P. Morris', 'Quincey Harker'])

    const quincey = within(contents).getByRole('link', { name: 'Quincey P. Morris' }).closest('li')!
    expect(quincey.className).toContain('later')
    expect(quincey.textContent).toContain('enters at chapter 5')
    // A chapter without a number is named by its title.
    expect(within(contents).getByRole('link', { name: 'Quincey Harker' }).closest('li')!.textContent).toContain('enters at “Note”')
    expect(within(contents).queryByRole('button', { name: /appear|not met|not in view/ })).toBeNull()

    // One request has both, so the page can't mix two readings of the den.
    expect(listCalls(calls).every((c) => c.includes('marksAt=dracula%3A3'))).toBe(true)
  })

  it('draws the line once, at the first later note, and not in a list of only later notes', async () => {
    stubApi()
    renderApp('/u/dracula?at=dracula:3')
    const contents = await tree()
    await within(contents).findByRole('link', { name: 'Jonathan Harker' })

    const firsts = [...contents.querySelectorAll('li.later-first')].map((li) => li.querySelector('a')!.textContent)
    expect(firsts).toEqual(['Lucy Westenra'])
    expect(within(contents).getByRole('link', { name: 'A scene at the inn' }).closest('li')!.className).toBe('later')
  })

  it('makes a tag plain text with its tooltip, not a Tab stop, with one line that explains them all', async () => {
    stubApi()
    renderApp('/u/dracula?at=dracula:3')
    const contents = await tree()
    await within(contents).findByRole('link', { name: 'Jonathan Harker' })

    const tag = within(contents).getByRole('link', { name: 'Lucy Westenra' }).closest('li')!.querySelector('.ahead-of-readers')!
    expect(tag.tagName).toBe('SPAN')
    expect(tag.getAttribute('title')).toContain('They first meet it at Dracula chapter 5')
    expect(within(contents).queryAllByRole('button', { name: /enters at/ })).toEqual([])
    expect(within(contents).getAllByText(MARKS_LEGEND)).toHaveLength(1)
  })

  it('has no line when nothing in the list has a tag', async () => {
    stubApi()
    renderApp('/u/dracula?at=dracula:note')
    const contents = await tree()
    await within(contents).findByRole('link', { name: 'Quincey Harker' })

    expect(contents.textContent).not.toContain(MARKS_LEGEND)
  })

  it('counts what readers have met of each type, when that isn’t all of them', async () => {
    stubApi()
    renderApp('/u/dracula?at=dracula:3')
    const contents = await tree()
    await within(contents).findByRole('link', { name: 'Jonathan Harker' })

    expect(within(contents).getByText('2 of 5').getAttribute('title')).toBe('Readers have met 2 of these 5 by this point.')
    expect(within(contents).getByText('0 of 1')).toBeTruthy()
  })

  it('gives an idea its status rather than a tag, after what readers have met', async () => {
    stubApi()
    renderApp('/u/dracula?at=dracula:3')
    const contents = await tree()

    const row = (await within(contents).findByRole('link', { name: 'A scene at the inn' })).closest('li')!
    expect(row.className).toContain('later')
    expect(row.querySelector('.status-idea')).toBeTruthy()
    expect(row.querySelector('.ahead-of-readers')).toBeNull()
  })

  it('says a note no position places isn’t placed, and an unplaced note with no position written too', async () => {
    const unplaced = (key: string, name: string, at: string | null): MarkedEntity => ({
      ...entities[0], key, id: key, name, path: `${key}.md`, from: at, met: false, readerMark: { kind: 'unplaced', at },
    })
    const listed = [unplaced('E-DUSK', 'The dusk note', 'dusk:2'), unplaced('E-EMPTY', 'The empty series note', null)]
    stubApi({
      override: (path, params): unknown =>
        path === '/api/entities' && params.get('marksAt')
          ? { version: 1, count: 2, total: 2, truncated: false, entities: listed, marksAt: 'dracula:3' }
          : undefined,
    })
    renderApp('/u/dracula?at=dracula:3')
    const contents = await tree()

    const dusk = (await within(contents).findByRole('link', { name: 'The dusk note' })).closest('li')!
    expect(dusk.textContent).toContain('not placed in the story yet')
    expect(dusk.querySelector('.ahead-of-readers')!.getAttribute('title')).toContain("dusk:2 isn't a chapter or book")
    expect(within(contents).getByRole('link', { name: 'The empty series note' }).closest('li')!.textContent).toContain('not placed in the story yet')
  })

  it('says plainly what a long list leaves out, in each view and with no position', async () => {
    const cut = (marked: boolean) => ({ version: 1, count: 1, total: 5000, truncated: true, entities: [{ ...entities[0], ...(marked ? { met: true } : {}) }], ...(marked ? { marksAt: 'dracula:3' } : {}) })
    stubApi({ override: (path, params): unknown => (path === '/api/entities' ? cut(params.get('marksAt') !== null) : undefined) })
    renderApp('/u/dracula?at=dracula:3')
    expect(await within(await tree()).findByText("1 of 5,000 notes are listed; groups count only what's listed. What readers have met is listed first.")).toBeTruthy()
    cleanup()

    renderApp('/u/dracula?at=dracula:3&readers=1')
    expect(await within(await tree()).findByText("1 of 5,000 notes are listed; groups count only what's listed.")).toBeTruthy()
    cleanup()

    renderApp('/u/dracula')
    expect(await within(await tree()).findByText("1 of 5,000 notes are listed; groups count only what's listed.")).toBeTruthy()
  })

  it.each([
    ['/u/dracula/b/dracula/c/5', false],
    ['/u/dracula/b/dracula/c/5?at=dracula:3', true],
    ['/u/dracula/b/dracula/c/5?at=dracula:5&readers=1', false],
  ])('doesn’t say no note enters at a chapter when the list was cut: %s', async (url, marked) => {
    const only = { ...entities[0], ...(marked ? { met: true } : {}) }
    stubApi({ override: (path): unknown => (path === '/api/entities' ? { version: 1, count: 1, total: 1200, truncated: true, entities: [only], ...(marked ? { marksAt: 'dracula:3' } : {}) } : undefined) })
    renderApp(url)
    const main = await screen.findByRole('main')

    expect(await within(main).findByText('Only 1 of 1,200 notes could be checked, so more may enter the story here.')).toBeTruthy()
    expect(within(main).getByText('None of those enter the story here.')).toBeTruthy()
    expect(within(main).queryByText('No notes enter the story in this chapter.')).toBeNull()
    expect(within(main).queryByText('Readers have met no notes that enter the story here.')).toBeNull()
  })

  it('tags the notes that enter at a later chapter on its page', async () => {
    stubApi()
    renderApp('/u/dracula/b/dracula/c/5?at=dracula:3')
    const main = await screen.findByRole('main')

    const lucy = (await within(main).findByRole('link', { name: 'Lucy Westenra' })).closest('li')!
    expect(lucy.textContent).toContain('enters at chapter 5')
    expect(within(main).getByText(MARKS_LEGEND)).toBeTruthy()
    expect(within(main).queryByRole('button', { name: /appear/ })).toBeNull()
  })

  it('has no tags on a chapter’s page once readers have got there', async () => {
    stubApi()
    renderApp('/u/dracula/b/dracula/c/5?at=dracula:note')
    const main = await screen.findByRole('main')

    await within(main).findByRole('link', { name: 'Lucy Westenra' })
    expect(within(main).queryByText(MARKS_LEGEND)).toBeNull()
  })
})

describe('SC-WEB-105: in the Reader’s view, a list has only what readers know', () => {
  it('names nothing later, counts nothing later, and never asks for it', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula?at=dracula:3&readers=1')
    const contents = await tree()
    await within(contents).findByRole('link', { name: 'Jonathan Harker' })

    for (const name of LATER_NAMES) {
      expect(document.body.textContent).not.toContain(name)
    }

    // Only the two it's met, of the five: nothing says there are more.
    const characters = [...contents.querySelectorAll('summary')].find((s) => s.textContent!.startsWith('Characters'))!
    expect(characters.querySelector('.count')!.textContent).toBe('2')
    expect(contents.textContent).not.toContain('of 5')
    expect(contents.textContent).not.toContain('Events')
    expect(contents.textContent).not.toContain(MARKS_LEGEND)
    expect(contents.querySelector('.later')).toBeNull()
    expect(contents.querySelector('.ahead-of-readers')).toBeNull()

    expect(listCalls(calls).length).toBeGreaterThan(0)
    expect(listCalls(calls).every((c) => c.includes('asOf=dracula%3A3'))).toBe(true)
    expect(calls.some((c) => c.includes('marksAt='))).toBe(false)
  })

  it('has no New buttons in the tree, on the universe’s page, or in the top bar, as it’s read-only', async () => {
    stubApi()
    renderApp('/u/dracula?at=dracula:3&readers=1')
    const contents = await tree()
    await within(contents).findByRole('link', { name: 'Jonathan Harker' })

    expect(screen.queryByRole('button', { name: /^New/ })).toBeNull()
    expect(within(contents).queryByRole('button', { name: /New/ })).toBeNull()
    expect(within(screen.getByRole('main')).queryByText(/Chapter defaults/i)).toBeNull()
    cleanup()

    renderApp('/u/dracula?at=dracula:3')
    expect((await screen.findAllByRole('button', { name: /^New/ })).length).toBeGreaterThan(0)
  })

  it('lists only the chapters readers have got to', async () => {
    stubApi()
    renderApp('/u/dracula?at=dracula:3&readers=1')
    const contents = await tree()
    await within(contents).findByRole('link', { name: 'Jonathan Harker' })

    const chapters = [...contents.querySelectorAll('.chapter-list a')].map((a) => a.textContent)
    expect(chapters).toHaveLength(3)
    expect(contents.textContent).not.toContain('Letters—Lucy and Mina')
  })

  it('keeps readers=1 from the tree to a note, and back again', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula?at=dracula:3&readers=1')
    const contents = await tree()

    fireEvent.click(await within(contents).findByRole('link', { name: 'Mina Harker' }))
    await screen.findByRole('heading', { level: 1, name: 'Mina Harker' })
    expect(history.location.pathname).toBe('/u/dracula/e/E-MINA')
    expect(readersIn(history)).toBe('1')

    fireEvent.click(within(await tree()).getAllByRole('link', { name: 'Dracula' })[0])
    await waitFor(() => expect(history.location.pathname).toBe('/u/dracula'))
    expect(readersIn(history)).toBe('1')
    for (const name of LATER_NAMES) {
      expect(document.body.textContent).not.toContain(name)
    }
  })

  it('says only what readers have met enters at a chapter’s page, naming nothing later', async () => {
    stubApi()
    renderApp('/u/dracula/b/dracula/c/4?at=dracula:4&readers=1')
    const main = await screen.findByRole('main')

    expect(await within(main).findByText('Readers have met no notes that enter the story here.')).toBeTruthy()
    expect(document.body.textContent).not.toContain('Lucy Westenra')
  })

  it('lists a chapter’s notes readers have met', async () => {
    stubApi()
    renderApp('/u/dracula/b/dracula/c/5?at=dracula:5&readers=1')
    const main = await screen.findByRole('main')

    expect(await within(main).findByRole('link', { name: 'Lucy Westenra' })).toBeTruthy()
    expect(within(main).queryByText(MARKS_LEGEND)).toBeNull()
  })

  it('doesn’t say a later series has no books, or name what it’s kept for', async () => {
    const later = { key: 'later', name: 'The Later Saga', id: null, path: 'later', entities: { total: 1, byType: { note: 1 } }, books: [] }
    const answer = { ...hierarchy, universe: { ...hierarchy.universe, series: [later] } }
    stubApi({ override: (path): unknown => (path === '/api/hierarchy' ? answer : undefined) })
    renderApp('/u/dracula?at=dracula:3&readers=1')
    const main = await screen.findByRole('main')

    expect(await within(main).findByText("Readers haven't got to a book in this series.")).toBeTruthy()
    expect(document.body.textContent).not.toContain('Far Book')
    expect(within(main).queryByText('No books yet.')).toBeNull()
    cleanup()

    renderApp('/u/dracula/s/later?at=dracula:3&readers=1')
    const page = await screen.findByRole('main')
    expect(await within(page).findByText("Readers haven't got to a book in this series.")).toBeTruthy()
    expect(within(page).queryByText('This series has no books yet.')).toBeNull()
  })

  it('keeps the banner for a note readers haven’t met, asking for the list only then', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/e/E-QUINCEY?at=dracula:3&readers=1')
    const main = await screen.findByRole('main')

    expect((await within(main).findByText(/Readers first meet/)).textContent).toBe('Readers first meet Quincey P. Morris in Dracula chapter 5.')
    expect(calls).toContain('/api/entities?universe=dracula&limit=1000')
  })

  it('doesn’t ask for the whole list on a note readers have met', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/e/E-MINA?at=dracula:3&readers=1')
    await screen.findByRole('heading', { level: 1, name: 'Mina Harker' })

    expect(calls).not.toContain('/api/entities?universe=dracula&limit=1000')
  })
})

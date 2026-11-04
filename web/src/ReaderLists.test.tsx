// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { BookNode, ChapterNode, MarkedEntity, PositionView, UniverseNode } from './api'
import { entities, renderApp, stubApi } from './test/harness'
import { MARKS_LEGEND, READERS_VIEW_REACH } from './readerWords'
import { readersTree } from './story'

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

  it('says when a long list is cut off', async () => {
    stubApi({
      override: (path, params): unknown =>
        path === '/api/entities' && params.get('marksAt')
          ? { version: 1, count: 1, total: 5000, truncated: true, entities: [{ ...entities[0], met: true }], marksAt: 'dracula:3' }
          : undefined,
    })
    renderApp('/u/dracula?at=dracula:3')

    expect(await within(await tree()).findByText('Only the first 1 notes are listed.')).toBeTruthy()
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
    renderApp('/u/dracula/b/dracula/c/5?at=dracula:5')
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
    renderApp('/u/dracula/b/dracula/c/5?at=dracula:3&readers=1')
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

  it('says what it doesn’t reach on every page, once', async () => {
    stubApi()
    renderApp('/u/dracula?at=dracula:3&readers=1')
    expect(await screen.findAllByText(READERS_VIEW_REACH)).toHaveLength(1)
    cleanup()

    renderApp('/u/dracula/e/E-MINA?at=dracula:3&readers=1')
    await screen.findByRole('heading', { level: 1, name: 'Mina Harker' })
    expect(screen.getAllByText(READERS_VIEW_REACH)).toHaveLength(1)
    cleanup()

    renderApp('/u/dracula?at=dracula:3')
    await screen.findByRole('group', { name: 'Whose view' })
    expect(screen.queryByText(READERS_VIEW_REACH)).toBeNull()
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

describe('SC-WEB-105: the tree readers know', () => {
  const chapter = (book: string, key: number): ChapterNode => ({ key: String(key), number: key, title: `${book} ${key}`, position: `${book}:${key}`, heading: null })
  const book = (key: string, chapters: number, total = 0): BookNode => ({
    key,
    name: key,
    id: null,
    path: key,
    status: 'draft',
    position: key,
    entities: { total, byType: {} },
    chapters: Array.from({ length: chapters }, (_, i) => chapter(key, i + 1)),
  })
  const place = (b: string, c: number): PositionView => ({ position: `${b}:${c}`, book: b, bookName: b, series: 'long', chapter: String(c), number: c, title: null })
  const positions = [place('ember', 1), place('ember', 2), place('ash', 1), place('ash', 2)]
  const universe = (ash: BookNode, empty = 0): UniverseNode => ({
    key: 'u',
    name: 'U',
    id: null,
    path: 'u',
    entities: { total: 0, byType: {} },
    series: [
      { key: 'long', name: 'Long', id: null, path: 'long', entities: { total: 0, byType: {} }, books: [book('ember', 2), ash] },
      { key: 'empty', name: 'Empty', id: null, path: 'empty', entities: { total: empty, byType: {} }, books: [] },
    ],
    books: [],
  })

  it('leaves out a later book and its chapters, and a series with nothing yet', () => {
    const known = readersTree(universe(book('ash', 2)), 'ember:2', positions)

    expect(known.series.map((s) => s.key)).toEqual(['long'])
    expect(known.series[0].books.map((b) => b.key)).toEqual(['ember'])
    expect(known.series[0].books[0].chapters.map((c) => c.key)).toEqual(['1', '2'])
  })

  it('cuts a book’s chapters at the position, and a bare book reaches its first chapter', () => {
    expect(readersTree(universe(book('ash', 2)), 'ember:1', positions).series[0].books[0].chapters.map((c) => c.key)).toEqual(['1'])
    const bare = readersTree(universe(book('ash', 2)), 'ash', positions).series[0].books
    expect(bare.map((b) => b.key)).toEqual(['ember', 'ash'])
    expect(bare[1].chapters.map((c) => c.key)).toEqual(['1'])
  })

  it('keeps a later book or a series with no books if a met note is filed there, with no later chapters', () => {
    const known = readersTree(universe(book('ash', 2, 1), 1), 'ember:1', positions)

    expect(known.series.map((s) => s.key)).toEqual(['long', 'empty'])
    expect(known.series[0].books.map((b) => b.key)).toEqual(['ember', 'ash'])
    expect(known.series[0].books[1].chapters).toEqual([])
  })

  it('fails closed where the positions aren’t known', () => {
    expect(readersTree(universe(book('ash', 2)), 'ember:1', []).series).toEqual([])
    expect(readersTree(universe(book('ash', 2)), 'nowhere', positions).series).toEqual([])
  })
})

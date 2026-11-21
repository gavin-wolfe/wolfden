// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { EntityPageResult, LinkView } from './api'
import { atOf, entities, page, renderApp, stubApi } from './test/harness'
import { MARKS_LEGEND, NEXT_BOOK, NEXT_CHAPTER, PICK_IN_AUTHORS_VIEW } from './readerWords'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const readersIn = (history: { location: { search: string } }) => new URLSearchParams(history.location.search).get('readers')

/** What the test den holds past chapter 3: a later chapter's title, a note that enters then. */
const LATER = ['Letters', 'Lucy', 'Quincey']

/**
 * Every request the Reader's view may make, as a pattern. `ReadersBookPagesTests` checks these answers word by word, so
 * between them, a word readers don't know isn't in anything these pages ask for. Anything new has to go there too.
 */
const READERS_REQUESTS = [
  // Who is logged in, and their role (#315): nothing from the den.
  /^\/api\/auth\/status$/,
  /^\/api\/den$/,
  /^\/api\/den\/files$/,
  /^\/api\/den\/errors\?countOnly=true$/,
  /^\/api\/hierarchy\?universe=dracula&asOf=[^&]+&readers=true$/,
  /^\/api\/positions\?universe=dracula&asOf=[^&]+&readers=true$/,
  /^\/api\/book\/chapter-fields\?universe=dracula&book=dracula&asOf=[^&]+&readers=true$/,
  /^\/api\/entities\?universe=dracula&asOf=[^&]+&readers=true&limit=1000$/,
]

function expectOnlyReadersRequests(calls: readonly string[]) {
  expect(calls.filter((c) => !READERS_REQUESTS.some((r) => r.test(c)))).toEqual([])
}

const options = (select: HTMLElement) => [...(select as HTMLSelectElement).options].map((o) => o.textContent)

describe('SC-WEB-110: the book and chapter pages in the Reader’s view', () => {
  it('a book’s page has only the chapters reached, and nothing later anywhere on it, or in what it asks for', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/b/dracula?at=dracula:3&readers=1')
    const main = await screen.findByRole('main')
    await waitFor(() => expect(main.querySelectorAll('ol.chapters > li')).toHaveLength(3))
    await waitFor(() => expect(main.querySelector('.chapter-field-summary')).not.toBeNull())

    for (const word of LATER) {
      expect(document.documentElement.innerHTML).not.toContain(word)
    }

    expectOnlyReadersRequests(calls)
  })

  it('a book’s page has nothing to change it with', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/b/dracula?at=dracula:3&readers=1')
    const main = await screen.findByRole('main')
    await within(main).findByRole('heading', { level: 1, name: 'Dracula' })

    expect(within(main).queryByRole('heading', { name: 'Chapter defaults' })).toBeNull()
    expect(within(main).queryByRole('button', { name: /Add chapter/ })).toBeNull()
    expect(within(main).queryByRole('button', { name: /Freeze/ })).toBeNull()
    expect(within(main).queryByRole('button', { name: /New/ })).toBeNull()
    expect(calls.some((c) => c.startsWith('/api/chapter-defaults') || c.startsWith('/api/book/freeze') || c.startsWith('/api/book/chapters'))).toBe(false)
  })

  it('a chapter’s page has its fields to read, with no editor to offer a later name', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/b/dracula/c/1?at=dracula:3&readers=1')
    const main = await screen.findByRole('main')

    expect(await within(main).findByRole('link', { name: 'Jonathan Harker' })).toBeTruthy()
    expect(within(main).queryByRole('button', { name: /^Edit/ })).toBeNull()
    expect(within(main).queryByRole('button', { name: /Use the default/ })).toBeNull()
    expectOnlyReadersRequests(calls)
  })

  it('prev and next stay within what’s reached', async () => {
    stubApi()
    renderApp('/u/dracula/b/dracula/c/3?at=dracula:3&readers=1')
    const nav = await screen.findByRole('navigation', { name: 'Chapters' })

    expect(within(nav).getByRole('link', { name: /2\./ })).toBeTruthy()
    expect(within(nav).queryByRole('link', { name: /›/ })).toBeNull()
  })

  it('a later chapter’s page says only that readers haven’t got there', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/b/dracula/c/5?at=dracula:3&readers=1')
    const main = await screen.findByRole('main')

    expect(await within(main).findByText("Readers haven't got to this chapter by this point in the story.")).toBeTruthy()
    expect(document.documentElement.innerHTML).not.toContain('Letters')
    expect(document.title).toBe('Wolfden')
    expectOnlyReadersRequests(calls)
  })

  it('a later book’s page says only that readers haven’t got there', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/b/sequel?at=dracula:3&readers=1')
    const main = await screen.findByRole('main')

    expect(await within(main).findByText("Readers haven't got to this book by this point in the story.")).toBeTruthy()
    expect(calls.some((c) => c.includes('book=sequel'))).toBe(false)
    fireEvent.click(within(main).getByRole('button', { name: "Author's view" }))
    expect(await within(main).findByText(/no book called 'sequel'/)).toBeTruthy()
  })

  it('with no position, a book’s and chapter’s pages are as they were', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/b/dracula')
    const main = await screen.findByRole('main')
    await waitFor(() => expect(main.querySelectorAll('ol.chapters > li')).toHaveLength(6))

    expect(main.querySelector('.ahead-of-readers')).toBeNull()
    expect(within(main).queryByText(MARKS_LEGEND)).toBeNull()
    expect(await within(main).findByRole('heading', { name: 'Chapter defaults' })).toBeTruthy()
    expect(calls).toContain('/api/hierarchy?universe=dracula')
    expect(calls).toContain('/api/book/chapter-fields?universe=dracula&book=dracula')
    expect(calls.some((c) => c.includes('readers=') || c.includes('marksAt='))).toBe(false)
  })
})

describe('SC-WEB-109: the book and chapter pages in the Author’s view at a position', () => {
  it('a book’s page lists every chapter, later ones tagged in plain words, with one line to explain them', async () => {
    stubApi()
    renderApp('/u/dracula/b/dracula?at=dracula:3')
    const main = await screen.findByRole('main')
    await waitFor(() => expect(main.querySelectorAll('ol.chapters > li > .ahead-of-readers')).toHaveLength(3))

    const rows = [...main.querySelectorAll('ol.chapters > li')]
    expect(rows).toHaveLength(6)
    expect(rows.slice(0, 3).every((li) => li.querySelector(':scope > .ahead-of-readers') === null)).toBe(true)
    const tag = rows[3].querySelector(':scope > .ahead-of-readers')!
    expect(tag.tagName).toBe('SPAN')
    expect(tag.textContent).toBe('ahead of readers')
    expect(tag.getAttribute('title')).toContain('They reach it at Dracula chapter 4')
    expect(rows[3].className).toContain('later')
    expect(within(main).getAllByText(MARKS_LEGEND)).toHaveLength(1)
    expect(within(main).queryAllByRole('button', { name: 'ahead of readers' })).toEqual([])
  })

  it('a bare book’s first chapter isn’t later', async () => {
    stubApi()
    renderApp('/u/dracula/b/dracula?at=dracula')
    const main = await screen.findByRole('main')
    await waitFor(() => expect(main.querySelectorAll('ol.chapters > li > .ahead-of-readers')).toHaveLength(5))

    expect(main.querySelector('ol.chapters > li > .ahead-of-readers')!.closest('li')!.textContent).toContain("Jonathan Harker's Journal")
    expect(main.querySelector('ol.chapters > li')!.querySelector(':scope > .ahead-of-readers')).toBeNull()
  })

  it('a book’s page counts what readers have met of what’s filed in it', async () => {
    stubApi()
    renderApp('/u/dracula/b/dracula?at=dracula:3')
    const main = await screen.findByRole('main')
    const heading = await within(main).findByRole('heading', { name: 'Filed in this book' })

    await waitFor(() => expect(heading.closest('section')!.textContent).toContain('2 of 4'))
    expect(heading.closest('section')!.querySelector('[title]')!.getAttribute('title')).toBe('Readers have met 2 of these 4 by this point.')
  })

  it('a chapter’s page has it all, and tags a later chapter, here and in prev and next', async () => {
    stubApi()
    renderApp('/u/dracula/b/dracula/c/4?at=dracula:3')
    const main = await screen.findByRole('main')

    await waitFor(() => expect(main.querySelector('.eyebrow .ahead-of-readers')?.textContent).toBe('ahead of readers'))
    const nav = within(main).getByRole('navigation', { name: 'Chapters' })
    expect(within(nav).getByRole('link', { name: /Letters/ }).parentElement!.querySelector('.ahead-of-readers')).not.toBeNull()
    expect(within(nav).getByRole('link', { name: /3\./ }).parentElement!.querySelector('.ahead-of-readers')).toBeNull()
    expect(within(main).getAllByText(MARKS_LEGEND)).toHaveLength(1)
    expect(await within(main).findByRole('button', { name: 'Edit tense' })).toBeTruthy()
  })
})

describe('SC-WEB-111: the position bar in the Reader’s view', () => {
  it('its menus list only what’s reached, with no slider to say how far through the story this is', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula?at=dracula:3&readers=1')
    const chapter = await screen.findByRole('combobox', { name: 'Chapter' })

    expect(options(chapter)).toEqual(["chapter 1: Jonathan Harker's Journal", "chapter 2: Jonathan Harker's Journal", "chapter 3: Jonathan Harker's Journal", NEXT_CHAPTER, PICK_IN_AUTHORS_VIEW])
    expect(options(screen.getByRole('combobox', { name: 'Book' }))).toEqual(['Everything', 'Dracula', NEXT_BOOK, PICK_IN_AUTHORS_VIEW])
    expect(screen.queryByRole('slider')).toBeNull()
    expect(calls.some((c) => c === '/api/positions?universe=dracula')).toBe(false)
  })

  it('›, and Next chapter in the menu, go to the next chapter, asking only when pressed, and that’s then reached', async () => {
    const { calls } = stubApi()
    const { history } = renderApp('/u/dracula?at=dracula:3&readers=1')
    await screen.findByRole('combobox', { name: 'Chapter' })
    expect(calls.some((c) => c.startsWith('/api/positions/next'))).toBe(false)

    fireEvent.click(screen.getByRole('button', { name: NEXT_CHAPTER }))
    await waitFor(() => expect(atOf(history)).toBe('dracula:4'))
    expect(readersIn(history)).toBe('1')
    expect(calls).toContain('/api/positions/next?universe=dracula&after=dracula%3A3&step=chapter')

    fireEvent.change(await screen.findByRole('combobox', { name: 'Chapter' }), { target: { value: 'next' } })
    await waitFor(() => expect(atOf(history)).toBe('dracula:5'))
    expect(readersIn(history)).toBe('1')
    await waitFor(() => expect(options(screen.getByRole('combobox', { name: 'Chapter' }))).toContain('chapter 5: Letters—Lucy and Mina'))
  })

  it('says so at the end, only when asked, and stays', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula?at=dracula:note&readers=1')
    await screen.findByRole('combobox', { name: 'Chapter' })
    expect(screen.queryByText('No later chapter.')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: NEXT_CHAPTER }))

    expect(await screen.findByText('No later chapter.')).toBeTruthy()
    expect(atOf(history)).toBe('dracula:note')
    expect(readersIn(history)).toBe('1')

    fireEvent.change(screen.getByRole('combobox', { name: 'Book' }), { target: { value: 'next' } })
    expect(await screen.findByText('No later book.')).toBeTruthy()
    expect(atOf(history)).toBe('dracula:note')
  })

  it('Pick in the Author’s view goes there, at the same position', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula?at=dracula:3&readers=1')
    fireEvent.change(await screen.findByRole('combobox', { name: 'Chapter' }), { target: { value: 'pick' } })

    await waitFor(() => expect(readersIn(history)).toBeNull())
    expect(atOf(history)).toBe('dracula:3')
    expect(await screen.findByRole('slider', { name: 'Story position' })).toBeTruthy()
  })

  it.each([
    ['›', () => screen.getByRole('button', { name: NEXT_CHAPTER }), (el: HTMLElement) => fireEvent.click(el)],
    ['Next chapter in the menu', () => screen.getByRole('combobox', { name: 'Chapter' }), (el: HTMLElement) => fireEvent.change(el, { target: { value: 'next' } })],
  ])('%s keeps focus, and the bar stays while it loads', async (_, control, press) => {
    let release: () => void = () => {}
    const held = new Promise<void>((resolve) => {
      release = resolve
    })
    stubApi({
      override: async (path, params) => {
        if (path === '/api/positions' && params.get('asOf') === 'dracula:4') {
          await held
        }

        return undefined
      },
    })
    const { history } = renderApp('/u/dracula?at=dracula:3&readers=1')
    await screen.findByRole('combobox', { name: 'Chapter' })
    const el = control()
    el.focus()
    press(el)

    await waitFor(() => expect(atOf(history)).toBe('dracula:4'))
    expect(screen.queryByText(/Loading the story/)).toBeNull()
    expect(control()).toBe(el)
    expect(document.activeElement).toBe(el)
    release()

    await waitFor(() => expect(options(screen.getByRole('combobox', { name: 'Chapter' }))).toContain("chapter 4: Jonathan Harker's Journal"))
    expect(control()).toBe(el)
    expect(document.activeElement).toBe(el)
  })

  it('moving back leaves nothing from where it was', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula?at=dracula:5&readers=1')
    const chapter = await screen.findByRole('combobox', { name: 'Chapter' })
    await waitFor(() => expect(options(chapter)).toContain('chapter 5: Letters—Lucy and Mina'))

    fireEvent.click(screen.getByRole('button', { name: 'Previous chapter' }))
    await waitFor(() => expect(atOf(history)).toBe('dracula:4'))

    // At once, not only once the new answer is in.
    expect(document.documentElement.innerHTML).not.toContain('chapter 5: Letters')
  })

  it('a position that can’t be read keeps a way out', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula?at=&readers=1')
    const bar = await screen.findByRole('group', { name: 'Story position' })
    fireEvent.click(await within(bar).findByRole('button', { name: 'Show everything' }))
    await waitFor(() => expect(atOf(history)).toBeNull())
  })

  it('the Author’s view keeps the slider and every name, and every position', async () => {
    stubApi()
    renderApp('/u/dracula?at=dracula:3')
    expect(await screen.findByRole('slider', { name: 'Story position' })).toBeTruthy()
    expect(options(await screen.findByRole('combobox', { name: 'Chapter' }))).toContain('chapter 5: Letters—Lucy and Mina')
  })
})

describe('SC-WEB-103: the top bar in the Reader’s view', () => {
  it('has no checkpoint indicator, and doesn’t ask what’s changed', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula?at=dracula:3&readers=1')
    await screen.findByRole('combobox', { name: 'Chapter' })

    expect(calls.some((c) => c.startsWith('/api/checkpoint'))).toBe(false)
    expect(calls).not.toContain('/api/den/errors')
    expect(calls).toContain('/api/den/errors?countOnly=true')
  })
})

/** A section's links, as the API gives a Reader's page. */
function link(text: string, target: string, extra: Partial<LinkView> = {}): LinkView {
  return { text, target, heading: null, label: null, line: 1, column: 1, outcome: 'resolved', entity: null, section: null, matches: null, problem: null, inView: false, ...extra }
}

function noteWith(body: string, links: LinkView[], extra: Partial<Parameters<typeof page>[1]> = {}) {
  const summary = entities.find((e) => e.key === 'E-MINA')!
  return (): EntityPageResult => ({
    version: 1,
    view: 'canon_as_of',
    position: 'dracula:3',
    inView: true,
    note: null,
    entity: page(summary, {
      sections: [{ name: 'Part', level: 2, headingPath: ['Part'], status: 'canon', pinned: false, from: null, until: null, line: 5, body, bookkeeping: false, links }],
      ...extra,
    }),
  })
}

describe('SC-WEB-101: links on a note’s page in the Reader’s view show only what they show', () => {
  const later = 'a-later-key'

  async function shown(body: string, links: LinkView[], url = '/u/dracula/e/E-MINA?at=dracula:3&readers=1') {
    stubApi({ entityPage: noteWith(body, links) })
    renderApp(url)
    const main = await screen.findByRole('main')
    await within(main).findByRole('heading', { level: 1, name: 'Mina Harker' })
    return within(main).findByRole('heading', { level: 2, name: /Part/ }).then((h) => h.closest('section')!)
  }

  it('in a table cell', async () => {
    const section = await shown(`| [[${later}\\|the stranger]] | plain |`, [link(`[[${later}\\|the stranger]]`, later, { label: 'the stranger' })])
    expect(section.textContent).toContain('the stranger')
    expect(section.textContent).not.toContain(later)
    // It isn't a table, but its words are all there.
    expect(section.textContent).toContain('plain')
  })

  it('in a table cell with no position too, as a link', async () => {
    const met = { key: 'E-JONATHAN', id: 'E-JONATHAN', name: 'Jonathan Harker', type: 'character' }
    const section = await shown(`| [[jonathan\\|the clerk]] |`, [link('[[jonathan\\|the clerk]]', 'jonathan', { label: 'the clerk', entity: met, inView: null })], '/u/dracula/e/E-MINA')
    expect(within(section).getByRole('link', { name: 'the clerk' })).toBeTruthy()
  })

  it('in an image’s alt text', async () => {
    const section = await shown(`![[[${later}|the stranger]]](nothing.png)`, [link(`[[${later}|the stranger]]`, later, { label: 'the stranger' })])
    await waitFor(() => expect(section.innerHTML).toContain('the stranger'))
    expect(section.innerHTML).not.toContain(later)
  })

  it('split over two lines, which isn’t a link, so it has no record', async () => {
    const section = await shown(`See [[${later}\n|the stranger]] there, and [[${later}|the\nother one]] too.`, [])
    expect(section.textContent).toContain('See the stranger there')
    expect(section.textContent).toContain('the\nother one')
    expect(section.textContent).not.toContain(later)
  })

  it.each([
    ['italic', `[[${later}|*Emlabel*]]`, 'Emlabel', 'em'],
    ['bold', `[[${later}|**Boldlabel**]]`, 'Boldlabel', 'strong'],
    ['italic in the middle', `[[${later}|a *b* Multilabel]]`, 'a b Multilabel', 'em'],
    ['HTML', `[[${later}|<b>Htmllabel</b>]]`, 'Htmllabel', null],
    ['code', `[[${later}|\`Ticklabel\`]]`, 'Ticklabel', 'code'],
    ['a hard break', `[[${later}  \n|Hardsplit]]`, 'Hardsplit', null],
    ['a backslash break', `[[${later}\\\n|Bsplit]]`, 'Bsplit', null],
    ['a break and italics', `[[${later}\n|*Emsplit*]]`, 'Emsplit', 'em'],
  ])('with %s in it, a label shows and the target doesn’t', async (_, body, words, tag) => {
    const section = await shown(`See ${body} there.`, [])
    expect(section.textContent).toContain(`See ${words} there.`)
    expect(section.innerHTML).not.toContain(later)
    expect(section.textContent).not.toContain('[[')
    if (tag) {
      expect(section.querySelector(tag)).not.toBeNull()
    }
  })

  it('with a record, a formatted label is the link’s words', async () => {
    const met = { key: 'E-JONATHAN', id: 'E-JONATHAN', name: 'Jonathan Harker', type: 'character' }
    const section = await shown('See [[jonathan|the *clerk*]] there.', [link('[[jonathan|the *clerk*]]', 'jonathan', { label: 'the *clerk*', entity: met, inView: true })])
    const a = within(section).getByRole('link', { name: 'the clerk' })
    expect(a.querySelector('em')?.textContent).toBe('clerk')
  })

  it('in code, it stays as written, and a name with markdown in it reads as written', async () => {
    const met = { key: 'E-JONATHAN', id: 'E-JONATHAN', name: 'Jonathan *the* Harker', type: 'character' }
    const section = await shown('`[[code-key|x]]` and [[jonathan]].', [link('[[jonathan]]', 'jonathan', { entity: met, inView: true })])
    expect(section.querySelector('code')!.textContent).toBe('[[code-key|x]]')
    expect(within(section).getByRole('link', { name: 'Jonathan *the* Harker' })).toBeTruthy()
  })

  it('with no record, a labelled link is only its label', async () => {
    const section = await shown(`See [[${later}|the stranger]] there.`, [])
    expect(section.textContent).toContain('See the stranger there.')
  })

  it('inside the words of a link to a website', async () => {
    const section = await shown(`[A [[${later}|stranger]] here](https://example.invalid/)`, [])
    expect(section.textContent).toContain('A stranger here')
    expect(section.textContent).not.toContain(later)
  })

  it('in a mapping’s key', async () => {
    const entityPage = noteWith('', [], {
      fields: [{ name: 'mapped', value: { [`[[${later}|the stranger]]`]: 'a value' }, kind: 'plain', status: null, pinned: false, from: null, until: null, line: 3, entryIndex: null, live: null, links: [] }],
    })
    stubApi({ entityPage })
    renderApp('/u/dracula/e/E-MINA?at=dracula:3&readers=1')
    const main = await screen.findByRole('main')
    expect((await within(main).findByRole('term', { name: '' }).catch(() => main)).textContent).toContain('the stranger')
    expect(main.textContent).not.toContain(later)
  })

  it('with a heading readers don’t know, only the name, and no anchor in the address', async () => {
    const met = { key: 'E-JONATHAN', id: 'E-JONATHAN', name: 'Jonathan Harker', type: 'character' }
    // The API gives no heading or section for one readers don't know.
    const section = await shown('See [[Jonathan#Later heading]].', [link('[[Jonathan#Later heading]]', 'Jonathan', { entity: met, inView: true })])
    const a = within(section).getByRole('link', { name: 'Jonathan Harker' })
    expect(a.getAttribute('href')).not.toContain('later')
    expect(section.textContent).not.toContain('Later heading')
  })

  it('in the Author’s view, a formatted label stays as it was', async () => {
    const section = await shown(`See [[${later}|*Emlabel*]] there.`, [], '/u/dracula/e/E-MINA?at=dracula:3')
    expect(section.textContent).toContain(later)
    expect(section.textContent).toContain('[[')
  })

  it('in the Author’s view, a split link and an alt text stay as written', async () => {
    const section = await shown(`See [[${later}\n|the stranger]] there.`, [], '/u/dracula/e/E-MINA?at=dracula:3')
    expect(section.textContent).toContain(later)
  })
})

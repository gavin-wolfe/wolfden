// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { BacklinkView, EntityPageResult, PageField, PageSection, PositionView, ReaderMark } from './api'
import { atOf, entities, page, positions, renderApp, stubApi } from './test/harness'
import { placeWords } from './story'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const MINA = entities.find((e) => e.key === 'E-MINA')!
const readersIn = (history: { location: { search: string } }) => new URLSearchParams(history.location.search).get('readers')
const entityCalls = (calls: string[]) => calls.filter((c) => c.startsWith('/api/entity?'))

function section(name: string, extra: Partial<PageSection> = {}): PageSection {
  return { name, level: 2, headingPath: [name], status: 'canon', pinned: false, from: null, until: null, line: 10, body: 'Its text.', bookkeeping: false, links: [], ...extra }
}

function field(name: string, value: unknown, extra: Partial<PageField> = {}): PageField {
  return { name, value, kind: 'plain', status: 'canon', pinned: false, from: null, until: null, line: 5, entryIndex: null, live: null, links: [], ...extra }
}

async function open(extra: Parameters<typeof page>[1], at = 'dracula:3') {
  stubApi({
    override: (path): EntityPageResult | undefined =>
      path === '/api/entity' ? { version: 1, view: 'full', position: null, inView: true, note: null, entity: page(MINA, extra), marksAt: at } : undefined,
  })
  renderApp(`/u/dracula/e/E-MINA?at=${at}`)
  const main = await screen.findByRole('main')
  await within(main).findByRole('heading', { level: 1, name: 'Mina Harker' })
  return main
}

const switchOf = () => screen.queryByRole('group', { name: 'Whose view' })

describe('SC-WEB-103: the Author’s view and the Reader’s view', () => {
  it('has no control with no position, and only the full view', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/e/E-MINA')
    await screen.findByRole('heading', { level: 1, name: 'Mina Harker' })

    expect(switchOf()).toBeNull()
    expect(entityCalls(calls)).toEqual(['/api/entity?entity=E-MINA&universe=dracula'])
  })

  it('appears once a position is picked, opening the Author’s view, and the current one is always there', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/e/E-MINA')
    await screen.findByRole('heading', { level: 1, name: 'Mina Harker' })

    fireEvent.keyDown(screen.getByRole('slider', { name: 'Story position' }), { key: 'Home' })

    const control = await screen.findByRole('group', { name: 'Whose view' })
    expect(within(control).getByRole('button', { name: "Author's view" }).getAttribute('aria-pressed')).toBe('true')
    expect(within(control).getByRole('button', { name: "Reader's view" }).getAttribute('aria-pressed')).toBe('false')
    await waitFor(() => expect(calls).toContain('/api/entity?entity=E-MINA&universe=dracula&marksAt=dracula%3A1'))
  })

  it('keeps the Reader’s view in the URL, so a reload stays in it, and a fresh start opens the Author’s', async () => {
    const { calls } = stubApi()
    const first = renderApp('/u/dracula/e/E-MINA?at=dracula:3')
    fireEvent.click(await screen.findByRole('button', { name: "Reader's view" }))

    await waitFor(() => expect(readersIn(first.history)).toBe('1'))
    await waitFor(() => expect(calls).toContain('/api/entity?entity=E-MINA&universe=dracula&asOf=dracula%3A3&readers=true'))
    expect(screen.getByRole('button', { name: "Reader's view" }).getAttribute('aria-pressed')).toBe('true')
    expect(first.history.location.search).toContain('readers=1')
    const url = first.history.location.pathname + first.history.location.search
    first.unmount()

    // A reload is the same URL.
    const again = renderApp(url)
    expect((await screen.findByRole('button', { name: "Reader's view" })).getAttribute('aria-pressed')).toBe('true')
    again.unmount()

    // A fresh start doesn't remember it.
    renderApp('/u/dracula/e/E-MINA?at=dracula:3')
    expect((await screen.findByRole('button', { name: "Author's view" })).getAttribute('aria-pressed')).toBe('true')
  })

  it('says what it doesn’t cover yet, beside read-only, on both tabs', async () => {
    stubApi()
    const note = renderApp('/u/dracula/e/E-MINA?at=dracula:3&readers=1')
    expect(await screen.findByText(/The sidebar, search, and other pages aren't part of the Reader's view yet/)).toBeTruthy()
    note.unmount()

    renderApp('/u/dracula/e/E-MINA?view=history&at=dracula:3&readers=1')
    expect(await screen.findByText(/The sidebar, search, and other pages aren't part of the Reader's view yet/)).toBeTruthy()
  })

  it('doesn’t say so in the Author’s view', async () => {
    stubApi()
    renderApp('/u/dracula/e/E-MINA?at=dracula:3')
    await screen.findByRole('heading', { level: 1, name: 'Mina Harker' })

    expect(screen.queryByText(/aren't part of the Reader's view yet/)).toBeNull()
  })

  it('switches back, leaving the position alone', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula/e/E-MINA?at=dracula:3&readers=1')

    fireEvent.click(await screen.findByRole('button', { name: "Author's view" }))

    await waitFor(() => expect(readersIn(history)).toBeNull())
    expect(atOf(history)).toBe('dracula:3')
  })

  it('carries across a link to another note and to the next position', async () => {
    stubApi({
      entityPage: (key) => ({
        version: 1, view: 'canon_as_of', position: 'dracula:3', inView: true, note: null,
        entity: page(entities.find((e) => e.key === key)!, {
          backlinks: key === 'E-MINA' ? [{ source: { key: 'E-JONATHAN', id: 'E-JONATHAN', name: 'Jonathan Harker', type: 'character' }, container: null, site: 'entity_lead', path: 'j.md', line: 1, column: 1, text: '[[Mina]]', field: null, section: null, chapter: null, outlineRow: null, status: 'canon', bookkeeping: false }] : [],
        }),
      }),
    })
    const { history } = renderApp('/u/dracula/e/E-MINA?at=dracula:3&readers=1')
    const main = await screen.findByRole('main')

    fireEvent.click(await within(main).findByRole('link', { name: 'Jonathan Harker' }))

    await within(main).findByRole('heading', { level: 1, name: 'Jonathan Harker' })
    expect(readersIn(history)).toBe('1')
    expect(atOf(history)).toBe('dracula:3')

    fireEvent.keyDown(screen.getByRole('slider', { name: 'Story position' }), { key: 'ArrowRight' })
    await waitFor(() => expect(atOf(history)).toBe('dracula:4'))
    expect(readersIn(history)).toBe('1')
  })

  it('goes with its position: Show everything ends it, and it means nothing with no position', async () => {
    const { calls } = stubApi()
    const first = renderApp('/u/dracula/e/E-MINA?at=dracula:3&readers=1')
    const bar = await screen.findByRole('group', { name: 'Story position' })

    fireEvent.click(await within(bar).findByRole('button', { name: 'Show everything' }))

    await waitFor(() => expect(atOf(first.history)).toBeNull())
    expect(readersIn(first.history)).toBeNull()
    expect(switchOf()).toBeNull()
    first.unmount()

    calls.length = 0
    renderApp('/u/dracula/e/E-MINA?readers=1')
    await screen.findByRole('heading', { level: 1, name: 'Mina Harker' })
    expect(switchOf()).toBeNull()
    expect(entityCalls(calls)).toEqual(['/api/entity?entity=E-MINA&universe=dracula'])
  })

  it('takes only readers=1 as the Reader’s view', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula/e/E-MINA?at=dracula:3&readers=0')
    await screen.findByRole('heading', { level: 1, name: 'Mina Harker' })

    expect(screen.getByRole('button', { name: "Author's view" }).getAttribute('aria-pressed')).toBe('true')
    expect(entityCalls(calls)).toEqual(['/api/entity?entity=E-MINA&universe=dracula&marksAt=dracula%3A3'])
  })

  it('isn’t on a page that has one view until later steps', async () => {
    stubApi()
    renderApp('/u/dracula?at=dracula:3')
    await screen.findByRole('group', { name: 'Story position' })
    await screen.findByRole('main')

    expect(switchOf()).toBeNull()
  })

  it('shows a blank position’s error in either view, never everything quietly', async () => {
    stubApi()
    renderApp('/u/dracula/e/E-MINA?at=')

    expect((await within(await screen.findByRole('main')).findByRole('alert')).textContent).toContain("'marksAt' is empty")
    expect(screen.queryByRole('heading', { level: 1, name: 'Mina Harker' })).toBeNull()
  })

  it('says a note no position places isn’t placed, in the Reader’s view, rather than blaming its status', async () => {
    const dusk = { ...MINA, key: 'E-DUSK', id: 'E-DUSK', name: 'The dusk note', from: 'dusk:2' }
    stubApi({
      override: (path): unknown =>
        path === '/api/entities' ? { version: 1, count: 1, total: 1, truncated: false, entities: [dusk] }
        : path === '/api/entity' ? { version: 1, view: 'canon_as_of', position: 'dracula:3', inView: false, note: 'Not in canon.', entity: null }
        : undefined,
    })
    renderApp('/u/dracula/e/E-DUSK?at=dracula:3&readers=1')

    const banner = await within(await screen.findByRole('main')).findByText(/isn't placed in the story yet/)
    expect(banner.textContent).toBe("The dusk note isn't placed in the story yet, so readers don't meet it at any chapter.")
  })

  it('shows a type’s default in the Reader’s view, badged, and no tags', async () => {
    stubApi({
      entityPage: (_, at) => ({
        version: 1, view: 'canon_as_of', position: at, inView: true, note: null,
        entity: page(MINA, { fields: [field('allegiance', 'none', { kind: 'default' })], sections: [section('Overview')] }),
      }),
    })
    renderApp('/u/dracula/e/E-MINA?at=dracula:3&readers=1')
    const main = await screen.findByRole('main')

    await within(main).findByText('type default')
    expect(main.querySelector('.ahead-of-readers')).toBeNull()
  })
})

describe('SC-WEB-102: what readers don’t know is tagged in the Author’s view', () => {
  const headingOf = (main: HTMLElement, name: RegExp) => within(main).findByRole('heading', { name })

  it.each<[ReaderMark['kind'], string | null, string, RegExp]>([
    ['ahead', 'dracula:5', 'ahead of readers', /don't know this yet\. They reach it at Dracula chapter 5\./],
    ['ahead', null, 'ahead of readers', /^Readers at this point in the story don't know this yet\.$/],
    ['ended', 'dracula:2', 'no longer true here', /stopped being true at Dracula chapter 2\./],
    ['ended', null, 'no longer true here', /stopped being true before this point/],
    ['unplaced', 'dusk:9', 'not placed in the story yet', /dusk:9 isn't a chapter or book/],
    ['unplaced', null, 'not placed in the story yet', /Nothing places this/],
    ['never', 'dracula:5', "never true (its dates don't overlap)", /Check its dates/],
  ])('a section marked %s at %s says "%s"', async (kind, at, words, why) => {
    const main = await open({ sections: [section('Terms', { readerMark: { kind, at } })] })

    const heading = await headingOf(main, /Terms/)
    expect(within(heading).getByRole('button', { name: words }).getAttribute('title')).toMatch(why)
  })

  it('tags a field’s entry the same way', async () => {
    const main = await open({ fields: [field('rank', 'Captain', { readerMark: { kind: 'ended', at: 'dracula:2' }, until: 'dracula:2' })] })

    const value = (await within(main).findByText(/Captain/)).closest('dd')!
    expect(within(value).getByRole('button', { name: 'no longer true here' })).toBeTruthy()
    expect(value.textContent).toContain('until ch. 2')
  })

  it('gives a fact one tag: its mark, even if its until is later too', async () => {
    const main = await open({ sections: [section('Terms', { from: 'dracula:4', until: 'dracula:5', readerMark: { kind: 'ahead', at: 'dracula:4' } })] })

    const heading = await headingOf(main, /Terms/)
    expect(heading.querySelectorAll('.ahead-of-readers')).toHaveLength(1)
  })

  it('tags a fact in effect with a later until by the page’s rule, since it has no mark', async () => {
    const main = await open({ sections: [section('Terms', { until: 'dracula:5' })] })

    expect(within(await headingOf(main, /Terms/)).getByRole('button', { name: 'ahead of readers' })).toBeTruthy()
  })

  it('tags each value of a position field that’s later, or that can’t be compared', async () => {
    const main = await open({ fields: [field('planted', ['dracula:1', 'dracula:5', 'dusk:9'], { aheadValues: ['dracula:5'], unplacedValues: ['dusk:9'] })] })

    const items = within((await within(main).findByText('dracula:1')).closest('ul')!).getAllByRole('listitem')
    expect(items[0].querySelector('.ahead-of-readers')).toBeNull()
    expect(within(items[1]).getByRole('button', { name: 'ahead of readers' })).toBeTruthy()
    const unplaced = within(items[2]).getByRole('button', { name: 'not placed in the story yet' })
    expect(unplaced.getAttribute('title')).toContain('dusk:9')
  })

  it('gives a value no tag of its own in a fact that has a mark', async () => {
    const main = await open({ fields: [field('paid_off', 'dracula:5', { aheadValues: ['dracula:5'], readerMark: { kind: 'ahead', at: 'dracula:4' }, from: 'dracula:4' })] })

    const value = (await within(main).findByText(/dracula:5/)).closest('dd')!
    expect(value.querySelectorAll('.ahead-of-readers')).toHaveLength(1)
  })

  it('says where a later note enters, in the note’s header', async () => {
    const main = await open({ readerMark: { kind: 'ahead', at: 'dracula:5' } })

    const tag = within(main.querySelector<HTMLElement>('.eyebrow')!).getByRole('button', { name: 'enters at chapter 5' })
    expect(tag.getAttribute('title')).toContain('They first meet it at Dracula chapter 5.')
  })

  it('says a note enters later when no position names where', async () => {
    const main = await open({ readerMark: { kind: 'ahead', at: null } })

    expect(within(main).getByRole('button', { name: 'enters later in the story' })).toBeTruthy()
  })

  it('opens the sentence for a keyboard or a touch', async () => {
    const main = await open({ sections: [section('Terms', { readerMark: { kind: 'ended', at: 'dracula:2' } })] })

    const tag = await within(main).findByRole('button', { name: 'no longer true here' })
    expect(tag.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(tag)
    expect(tag.getAttribute('aria-expanded')).toBe('true')
    expect(within(main).getByText(/stopped being true at Dracula chapter 2\./)).toBeTruthy()
  })

  it.each<[string, Partial<PageSection>]>([
    ['an idea', { status: 'idea' }],
    ['a retconned section', { status: 'retconned' }],
    ['a bookkeeping section', { bookkeeping: true }],
    ['a section left out', { leftOut: [{ line: 3, rule: 'F-SEC-10', message: 'A bad comment.' }] as PageSection['leftOut'] }],
  ])('leaves %s with its own badge, even with a later until', async (_, extra) => {
    const main = await open({ sections: [section('Terms', { until: 'dracula:5', ...extra })] })

    await headingOf(main, /Terms/)
    expect(main.querySelector('.ahead-of-readers')).toBeNull()
  })

  it('gives an idea’s note no reader tags, even with a later until', async () => {
    const main = await open({ status: 'idea', sections: [section('Terms', { until: 'dracula:5' })] })

    await headingOf(main, /Terms/)
    expect(main.querySelector('.ahead-of-readers')).toBeNull()
  })

  it('gives a note no position places the one tag, and none to what’s in it', async () => {
    const main = await open({
      readerMark: { kind: 'unplaced', at: 'dusk:1' },
      sections: [section('Terms', { until: 'dracula:5' })],
      fields: [field('paid_off', 'dracula:5', { aheadValues: ['dracula:5'] })],
    })

    await headingOf(main, /Terms/)
    expect(main.querySelectorAll('.ahead-of-readers')).toHaveLength(1)
    expect(within(main).getByRole('button', { name: 'not placed in the story yet' })).toBeTruthy()
  })

  it('has no tags with no position', async () => {
    stubApi({ entityPage: () => ({ version: 1, view: 'full', position: null, inView: true, note: null, entity: page(MINA, { sections: [section('Terms', { until: 'dracula:5' })] }) }) })
    renderApp('/u/dracula/e/E-MINA')
    const main = await screen.findByRole('main')

    await headingOf(main, /Terms/)
    expect(main.querySelector('.ahead-of-readers')).toBeNull()
  })

  describe('Linked from', () => {
    const backlink = (extra: Partial<BacklinkView>): BacklinkView => ({
      source: { key: 'E-JONATHAN', id: 'E-JONATHAN', name: 'Jonathan Harker', type: 'character' }, container: null, site: 'entity_section', path: 'j.md', line: 8, column: 1, text: '[[Mina]]', field: null, section: 'Letters', chapter: null, outlineRow: null, status: 'canon', bookkeeping: false, ...extra,
    })

    it('marks later material in plain words, with no button in the list, and one line to explain them', async () => {
      const main = await open({ backlinks: [backlink({ readerMark: { kind: 'ahead', at: 'dracula:5' } }), backlink({ section: 'Journal', line: 2 })] })

      const places = await within(main).findByText(/in “Letters”/)
      const tag = within(places).getByText('ahead of readers')
      expect(tag.tagName).toBe('SPAN')
      expect(tag.getAttribute('title')).toContain('Dracula chapter 5')
      expect(within(main).getByText(/in “Journal”/).querySelector('.ahead-of-readers')).toBeNull()
      expect(within(main).getByText(/Tags say what readers/).textContent).not.toMatch(/hover/i)
      expect(main.querySelector('.backlinks button')).toBeNull()
    })

    it('gives a link in a section left out the left-out badge, and no reader tag', async () => {
      const main = await open({ backlinks: [backlink({ leftOut: true })] })

      const places = await within(main).findByText(/in “Letters”/)
      expect(within(places).getByText('left out').className).toContain('problem')
      expect(places.querySelector('.ahead-of-readers')).toBeNull()
    })

    it('has no explaining line when nothing is marked', async () => {
      const main = await open({ backlinks: [backlink({})] })

      await within(main).findByText(/in “Letters”/)
      expect(within(main).queryByText(/Tags say what readers/)).toBeNull()
    })
  })
})

describe('placeWords', () => {
  const twoBooks: PositionView[] = [...positions, { position: 'ash:1', book: 'ash', bookName: 'Ash', series: null, chapter: '1', number: 1, title: null }]

  it('names a chapter, with its book only where there are several', () => {
    expect(placeWords('dracula:5', positions)).toBe('chapter 5')
    expect(placeWords('dracula:5', twoBooks)).toBe('Dracula chapter 5')
    expect(placeWords('ash:1', twoBooks)).toBe('Ash chapter 1')
  })

  it('names a bare book by its start, and gives one it doesn’t know as written', () => {
    expect(placeWords('ash', twoBooks)).toBe('the start of Ash')
    expect(placeWords('dusk:9', twoBooks)).toBe('dusk:9')
  })
})

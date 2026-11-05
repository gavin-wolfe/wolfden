// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { SearchHit, SearchResult } from './api'
import { entities, hierarchy, renderApp, stubApi } from './test/harness'
import { MARKS_LEGEND } from './readerWords'
import { keepAtSamePosition } from './pages/SearchPage'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const byKey = (key: string) => entities.find((e) => e.key === key)!

function hit(key: string, section: string | null, text: string, extra: Partial<SearchHit> = {}): SearchHit {
  const entity = byKey(key)
  return { entity, section, line: section ? 10 : null, snippet: text, text, matches: [], status: entity.status, inCanon: true, inComment: false, ...extra }
}

const later = { kind: 'ahead' as const, at: 'dracula:11' }

/** As the API sorts them: what a reader can find, then the rest. */
const markedHits: SearchHit[] = [
  hit('E-MINA', 'Letters', 'She writes of blood.', { met: true }),
  hit('E-MINA', 'After the castle', 'Blood at the castle.', { met: false, readerMark: later }),
  hit('E-MINA', null, 'No more blood.', { met: false, readerMark: { kind: 'ended', at: 'dracula:2' } }),
  hit('E-LUCY', null, 'A plan.', { met: false, field: 'plot', aheadValues: ['dracula:11'] }),
  hit('E-JONATHAN', null, 'An idea → The Count', { met: false, field: 'ally', laterLink: { status: 'canon', readerMark: later } }),
  hit('E-QUINCEY', 'Texas', 'Blood in Texas.', { met: false, noteMark: later, readerMark: later }),
]

function stubSearch(hits: SearchHit[] = markedHits) {
  const searches: URLSearchParams[] = []
  const stub = stubApi({
    override: (path, params) => {
      if (path !== '/api/search') {
        return undefined
      }

      searches.push(params)
      return { version: 1, query: params.get('query')!, count: hits.length, truncated: false, hits, marksAt: params.get('marksAt') ?? undefined } satisfies SearchResult
    },
  })
  return { ...stub, searches }
}

describe("SC-WEB-107: search in the Author's view", () => {
  it('asks for everything, tagged, with one legend, and a line before what readers can\'t find at all', async () => {
    const { searches } = stubSearch()
    renderApp('/u/dracula/search?q=blood&at=dracula:3')
    const main = await screen.findByRole('main')
    await within(main).findByText('She writes of blood.')

    expect(searches[0].get('marksAt')).toBe('dracula:3')
    expect(within(main).getByText(/Searching everything, at/).textContent).toContain('Dracula chapter 3')
    expect(within(main).getAllByText(MARKS_LEGEND)).toHaveLength(1)

    // A note with something readers can find comes first, its later hits tagged.
    const groups = [...main.querySelectorAll('.hit-groups > li')] as HTMLElement[]
    expect(groups).toHaveLength(4)
    expect(groups[0].classList.contains('later-first')).toBe(false)
    expect(within(groups[0]).getByText('ahead of readers')).toBeTruthy()
    expect(within(groups[0]).getByText('no longer true here')).toBeTruthy()
    expect(groups[1].classList.contains('later-first')).toBe(true)
    expect(within(groups[1]).getByText('ahead of readers')).toBeTruthy()
    expect(within(groups[2]).getByText(/enters at/)).toBeTruthy()

    // The note's own tag is on the note, not again on its hit.
    expect(within(groups[3]).getAllByText(/enters at/)).toHaveLength(1)
    expect(groups[3].querySelector('.hit-entity .ahead-of-readers')).toBeTruthy()

    // Plain text: no Tab stop, no button inside a link.
    for (const tag of main.querySelectorAll('.ahead-of-readers')) {
      expect(tag.tagName).toBe('SPAN')
      expect(tag.closest('a')).toBeNull()
      expect(tag.getAttribute('title')).toBeTruthy()
    }

    expect(main.querySelector('.hit-groups button')).toBeNull()
  })

  it('has no line or legend when everything found is something readers can find', async () => {
    stubSearch([hit('E-MINA', 'Letters', 'She writes of blood.', { met: true })])
    renderApp('/u/dracula/search?q=blood&at=dracula:3')
    const main = await screen.findByRole('main')
    await within(main).findByText('She writes of blood.')

    expect(within(main).queryByText(MARKS_LEGEND)).toBeNull()
    expect(main.querySelector('.later-first')).toBeNull()
  })

  it('has no line when nothing found is something readers can find', async () => {
    stubSearch([hit('E-QUINCEY', 'Texas', 'Blood in Texas.', { met: false, noteMark: later, readerMark: later })])
    renderApp('/u/dracula/search?q=blood&at=dracula:3')
    const main = await screen.findByRole('main')
    await within(main).findByText('Blood in Texas.')

    expect(main.querySelector('.later-first')).toBeNull()
    expect(within(main).getByText(MARKS_LEGEND)).toBeTruthy()
  })

  it('shows a linked note out of canon with its status, not a tag', async () => {
    stubSearch([hit('E-MINA', null, 'x → The dream', { met: false, field: 'ally', laterLink: { status: 'idea' } })])
    renderApp('/u/dracula/search?q=dream&at=dracula:3')
    const main = await screen.findByRole('main')
    await within(main).findByText('x → The dream')

    expect(within(main.querySelector('.hit-place') as HTMLElement).getByText('idea')).toBeTruthy()
    expect(main.querySelector('.ahead-of-readers')).toBeNull()
  })

  it('offers every type and field, as it finds them all', async () => {
    const { calls } = stubSearch()
    renderApp('/u/dracula/search?q=blood&at=dracula:3')
    const field = (await screen.findByRole('combobox', { name: 'Field' })) as HTMLSelectElement

    await waitFor(() => expect([...field.options].map((o) => o.value)).toEqual(['', 'home', 'occupation']))
    expect(calls.filter((c) => c.startsWith('/api/fields'))).toEqual(['/api/fields?universe=dracula'])
  })
})

describe("SC-WEB-108: search in the Reader's view", () => {
  it('shows no tags, line, or legend, and each hit keeps the view', async () => {
    const { searches } = stubSearch([hit('E-MINA', 'Letters', 'She writes of blood.')])
    const { history } = renderApp('/u/dracula/search?q=blood&at=dracula:3&readers=1')
    const main = await screen.findByRole('main')
    await within(main).findByText('She writes of blood.')

    expect(searches[0].get('asOf')).toBe('dracula:3')
    expect(searches[0].get('readers')).toBe('true')
    expect(main.querySelector('.ahead-of-readers')).toBeNull()
    expect(main.querySelector('.later-first')).toBeNull()
    expect(within(main).queryByText(MARKS_LEGEND)).toBeNull()
    expect(screen.getByRole('button', { name: "Reader's view" }).getAttribute('aria-pressed')).toBe('true')

    fireEvent.click(within(main).getByRole('link', { name: 'Letters' }))
    await waitFor(() => expect(history.location.pathname).toContain('/e/'))
    const url = new URLSearchParams(history.location.search)
    expect([url.get('at'), url.get('readers')]).toEqual(['dracula:3', '1'])
  })

  it('keeps the view from the top box, on a page and on the search page', async () => {
    stubSearch([])
    const { history } = renderApp('/u/dracula?at=dracula:3&readers=1')
    fireEvent.change(await screen.findByRole('searchbox', { name: 'Search notes' }), { target: { value: 'blood' } })
    fireEvent.submit(screen.getByRole('search'))

    await waitFor(() => expect(history.location.pathname).toBe('/u/dracula/search'))
    let url = new URLSearchParams(history.location.search)
    expect([url.get('q'), url.get('at'), url.get('readers')]).toEqual(['blood', 'dracula:3', '1'])

    fireEvent.change(screen.getByRole('searchbox', { name: 'Search notes' }), { target: { value: 'bat' } })
    fireEvent.submit(screen.getByRole('search'))
    await waitFor(() => expect(new URLSearchParams(history.location.search).get('q')).toBe('bat'))
    url = new URLSearchParams(history.location.search)
    expect([url.get('at'), url.get('readers')]).toEqual(['dracula:3', '1'])
  })

  it('lists only the books readers have reached in the Scope filter, keeping one already chosen', async () => {
    const u = hierarchy.universe
    const second = { ...u.books[0], key: 'sequel', name: 'The Sequel', position: 'sequel', chapters: [], entities: { total: 0, byType: {} } }
    // The harness' one book is reached; a later one with nothing met isn't named.
    stubApi({
      override: (path) =>
        path === '/api/hierarchy'
          ? { ...hierarchy, universe: { ...u, books: [...u.books, second] } }
          : path === '/api/search'
            ? ({ version: 1, query: 'blood', count: 0, truncated: false, hits: [] } satisfies SearchResult)
            : undefined,
    })
    renderApp('/u/dracula/search?q=blood&at=dracula:3&readers=1')
    const scope = (await screen.findByRole('combobox', { name: 'Scope' })) as HTMLSelectElement
    await waitFor(() => expect([...scope.options].map((o) => o.textContent)).toContain('Dracula'))
    expect([...scope.options].map((o) => o.textContent)).not.toContain('The Sequel')
    cleanup()

    renderApp('/u/dracula/search?q=blood&at=dracula:3&readers=1&book=sequel')
    const chosen = (await screen.findByRole('combobox', { name: 'Scope' })) as HTMLSelectElement
    await waitFor(() => expect(chosen.value).toBe('book:sequel'))
  })

  it("never shows the Author's results while the Reader's load", () => {
    const authors = { queryKey: ['search', { at: 'dracula:3', readers: false }] }
    const readers = { queryKey: ['search', { at: 'dracula:3', readers: true }] }
    const answer = { version: 1, query: 'blood', count: 0, truncated: false, hits: [] } satisfies SearchResult

    expect(keepAtSamePosition('dracula:3', true)(answer, authors)).toBeUndefined()
    expect(keepAtSamePosition('dracula:3', false)(answer, readers)).toBeUndefined()
    expect(keepAtSamePosition('dracula:3', true)(answer, readers)).toBe(answer)
    expect(keepAtSamePosition('dracula:3', false)(answer, authors)).toBe(answer)
  })
})

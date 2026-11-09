// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { EntityPage, EntityPageResult, LinkView, SearchHit, SearchResult } from './api'
import type { EntitySource, WriteResult } from './editApi'
import { matchLabel, type LookupHit } from './lookupApi'
import { entities, page, renderApp, stubApi } from './test/harness'

// Names readers learn partway through the story (`known_as`, #67) on the pages: the
// Author's view marks them, and links and search hits by them; the Reader's view lists
// only the names readers know; Other names edits them; and the link picker says which
// name matched.

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const lucy = entities.find((e) => e.key === 'E-LUCY')!
const mina = entities.find((e) => e.key === 'E-MINA')!
const later = { kind: 'ahead' as const, at: 'dracula:5' }

/** Mina's page, with a later name, as the view asks for it. */
function minaPage(at: string | null, readers: boolean): EntityPage {
  if (readers) {
    // The Reader's view lists only the names readers know there, as plain aliases.
    return page(mina, { aliases: at === 'dracula:5' ? ['Madam Mina', 'Mrs. Harker'] : ['Madam Mina'] })
  }

  return page(mina, {
    aliases: ['Madam Mina'],
    knownAs: [{ value: 'Mrs. Harker', from: 'dracula:5', ...(at !== null && at !== 'dracula:5' ? { readerMark: later } : {}) }],
  })
}

function link(extra: Partial<LinkView>): LinkView {
  return {
    text: '[[Mrs. Harker]]',
    target: 'Mrs. Harker',
    heading: null,
    label: null,
    line: 5,
    column: 1,
    outcome: 'resolved',
    entity: { key: mina.key, id: mina.id, name: mina.name, type: mina.type },
    section: null,
    matches: null,
    problem: null,
    inView: null,
    ...extra,
  }
}

function stubPages(lucyPage?: (at: string | null, readers: boolean) => EntityPage) {
  return stubApi({
    override: (path, params) => {
      if (path !== '/api/entity') {
        return undefined
      }

      const key = params.get('entity')!
      const readers = params.get('readers') === 'true'
      const at = params.get('asOf') ?? params.get('marksAt')
      const entity = key === mina.key ? minaPage(at, readers) : lucyPage ? lucyPage(at, readers) : page(entities.find((e) => e.key === key)!)
      return { version: 1, view: 'full', position: readers ? at : null, inView: true, note: null, entity, marksAt: readers ? undefined : (at ?? undefined) } satisfies EntityPageResult
    },
  })
}

async function open(url: string, name: string) {
  renderApp(url)
  const main = await screen.findByRole('main')
  await within(main).findByRole('heading', { name, level: 1 })
  return main
}

describe('SC-WEB-112: the Author sees the name, marked until readers learn it', () => {
  it('lists every name, with a later one tagged with where readers learn it', async () => {
    stubPages()
    const main = await open('/u/dracula/e/E-MINA?at=dracula:3', 'Mina Harker')

    const names = main.querySelector('.aliases')!
    expect(names.textContent).toContain('Madam Mina')
    expect(names.textContent).toContain('Mrs. Harker')
    expect(names.textContent).toContain('from chapter 5')
    const tag = within(names as HTMLElement).getByRole('button', { name: 'readers learn this at chapter 5' })
    expect(tag.getAttribute('title')).toBe("Readers at this point in the story don't know this name yet. They learn it at Dracula chapter 5.")
  })

  it('has no tag once readers know it, and none with no position', async () => {
    stubPages()
    let main = await open('/u/dracula/e/E-MINA?at=dracula:5', 'Mina Harker')
    expect(main.querySelector('.aliases')!.textContent).toContain('from chapter 5')
    expect(within(main).queryByText(/readers learn this/)).toBeNull()
    cleanup()

    stubPages()
    main = await open('/u/dracula/e/E-MINA', 'Mina Harker')
    expect(main.querySelector('.aliases')!.textContent).toContain('Mrs. Harker')
    expect(main.querySelector('.aliases')!.textContent).toContain('from chapter 5')
    expect(within(main).queryByText(/readers learn this/)).toBeNull()
  })

  it('in the Reader’s view lists only the names readers know, with nothing about when', async () => {
    stubPages()
    const main = await open('/u/dracula/e/E-MINA?at=dracula:3&readers=1', 'Mina Harker')

    expect(main.querySelector('.aliases')!.textContent).toBe('Also: Madam Mina')
    expect(main.textContent).not.toContain('Mrs. Harker')
  })

  it('shows a link by a later name as written, tagged, and only in the Author’s view', async () => {
    const lead = '# Lucy Westenra\n\nShe writes to [[Mrs. Harker]].'
    stubPages((at, readers) =>
      page(lucy, {
        lead: readers ? '# Lucy Westenra\n\nShe writes to [[Mina Harker]].' : lead,
        leadLinks: [readers ? link({ text: '[[Mina Harker]]', target: 'Mina Harker' }) : link({ byKnownName: true, ...(at === 'dracula:3' ? { nameMark: later } : {}) })],
      }),
    )
    let main = await open('/u/dracula/e/E-LUCY?at=dracula:3', 'Lucy Westenra')
    expect(within(main).getByRole('link', { name: 'Mrs. Harker' })).toBeTruthy()
    const tag = within(main).getByRole('button', { name: 'a name readers learn at chapter 5' })
    expect(tag.getAttribute('title')).toContain("Until then, the Reader's view shows the note's name for this link.")
    cleanup()

    stubPages((_, readers) =>
      page(lucy, {
        lead: readers ? '# Lucy Westenra\n\nShe writes to [[Mina Harker]].' : lead,
        leadLinks: [readers ? link({ text: '[[Mina Harker]]', target: 'Mina Harker' }) : link({ byKnownName: true })],
      }),
    )
    main = await open('/u/dracula/e/E-LUCY?at=dracula:3&readers=1', 'Lucy Westenra')
    expect(within(main).getByRole('link', { name: 'Mina Harker' })).toBeTruthy()
    expect(main.textContent).not.toContain('Mrs. Harker')
    expect(main.querySelector('.ahead-of-readers')).toBeNull()
  })

  it('tags a backlink written by a later name in Linked from', async () => {
    stubApi({
      override: (path) =>
        path === '/api/entity'
          ? ({
              version: 1,
              view: 'full',
              position: null,
              inView: true,
              note: null,
              marksAt: 'dracula:3',
              entity: page(mina, {
                backlinks: [
                  {
                    source: { key: lucy.key, id: lucy.id, name: lucy.name, type: lucy.type },
                    container: null,
                    site: 'entity_lead',
                    path: lucy.path,
                    line: 5,
                    column: 1,
                    text: '[[Mrs. Harker]]',
                    field: null,
                    section: null,
                    chapter: null,
                    outlineRow: null,
                    status: 'canon',
                    bookkeeping: false,
                    nameMark: later,
                  },
                ],
              }),
            } satisfies EntityPageResult)
          : undefined,
    })
    const main = await open('/u/dracula/e/E-MINA?at=dracula:3', 'Mina Harker')

    const backlinks = main.querySelector('.backlinks') as HTMLElement
    expect(within(backlinks).getByText('a name readers learn at chapter 5').tagName).toBe('SPAN')
  })

  it('tags a search hit only in a later name with where readers learn it', async () => {
    const hits: SearchHit[] = [
      { entity: mina, section: null, line: null, snippet: 'Mina Harker Mrs. Harker', text: 'Mina Harker Mrs. Harker', matches: [], status: 'canon', inCanon: true, inComment: false, met: false, hiddenIn: 'laterName', nameMark: later },
    ]
    stubApi({
      override: (path, params) =>
        path === '/api/search' ? ({ version: 1, query: params.get('query')!, count: 1, truncated: false, hits, marksAt: 'dracula:3' } satisfies SearchResult) : undefined,
    })
    renderApp('/u/dracula/search?q=mrs&at=dracula:3')
    const main = await screen.findByRole('main')

    const tag = await within(main).findByText('only in a name readers learn at chapter 5')
    expect(tag.getAttribute('title')).toContain("A search in the Reader's view here doesn't find it.")
    expect(within(main).queryByText('only in an alias')).toBeNull()
  })
})

describe('SC-WEB-113: a link by a name readers know shows as written', () => {
  it('shows the name it was written with, not the note’s, in the Reader’s view', async () => {
    stubPages(() => page(lucy, { lead: '# Lucy Westenra\n\nShe writes to [[Mrs. Harker]].', leadLinks: [link({ byKnownName: true })] }))
    const main = await open('/u/dracula/e/E-LUCY?at=dracula:5&readers=1', 'Lucy Westenra')

    expect(within(main).getByRole('link', { name: 'Mrs. Harker' })).toBeTruthy()
    expect(within(main).queryByRole('link', { name: 'Mina Harker' })).toBeNull()
  })
})

function source(hash = 'h1'): EntitySource {
  return {
    version: 1,
    entity: mina,
    hash,
    text: '---\nname: Mina Harker\n---\n',
    lineEndings: 'lf',
    byteOrderMark: false,
    frozen: null,
    lead: { line: 4, text: '# Mina Harker' },
    sections: [],
    standardSections: [],
    fields: [],
    fieldTypes: [],
  }
}

function written(extra: Partial<WriteResult> = {}): WriteResult {
  return { version: 1, changed: true, path: mina.path, hash: 'h2', entity: mina, effects: ['Changed the alias.'], pinnedCanon: [], warnings: [], files: null, idAdded: null, ...extra }
}

/** The pages, with the source and every edit posted, answered by `onPost`. */
function stubEditing(onPost: (path: string, body: Record<string, unknown>) => Response | WriteResult = () => written()) {
  const stub = stubPages()
  const base = stub.fetchMock.getMockImplementation()!
  const posts: { path: string; body: Record<string, unknown> }[] = []
  stub.fetchMock.mockImplementation((async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://127.0.0.1')
    if (url.pathname === '/api/entity/source') {
      return Response.json(source())
    }

    if (url.pathname.startsWith('/api/edit/')) {
      const post = { path: url.pathname.slice('/api/edit/'.length), body: JSON.parse(String(init?.body)) as Record<string, unknown> }
      posts.push(post)
      const answer = onPost(post.path, post.body)
      return answer instanceof Response ? answer : Response.json(answer)
    }

    return base(input, init)
  }) as typeof fetch)
  return posts
}

async function openNames(url = '/u/dracula/e/E-MINA?at=dracula:3', main?: HTMLElement) {
  main ??= await open(url, 'Mina Harker')
  fireEvent.click(within(main).getByRole('button', { name: /^Other names/ }))
  return screen.findByRole('region', { name: 'Other names' })
}

describe('SC-WEB-115: editing a note’s other names', () => {
  it('lists each name with when readers learn it, and renames one through the same write as an assistant', async () => {
    const posts = stubEditing()
    const panel = await openNames()

    const rows = [...panel.querySelectorAll('ul.names > li')].map((li) => li.querySelector('.other-name')!.textContent + ' | ' + li.querySelector('.badge')!.textContent)
    expect(rows).toEqual(['Madam Mina | always known', 'Mrs. Harker | from chapter 5'])

    fireEvent.click(within(panel).getByRole('button', { name: 'Rename “Mrs. Harker”' }))
    const form = within(panel).getByRole('form', { name: 'Rename “Mrs. Harker”' })
    fireEvent.change(within(form).getByRole('textbox', { name: 'New name' }), { target: { value: 'Mrs. Mina Harker' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Rename' }))

    await screen.findByText(/Saved the note's names/)
    expect(posts).toEqual([
      { path: 'name', body: { entity: 'E-MINA', universe: 'dracula', expectedHash: 'h1', name: 'Mrs. Harker', newName: 'Mrs. Mina Harker' } },
    ])
  })

  it('changes where readers learn a name, and warns before making a later name an alias', async () => {
    const posts = stubEditing()
    const panel = await openNames()

    fireEvent.click(within(panel).getByRole('button', { name: 'When readers learn “Mrs. Harker”' }))
    const form = within(panel).getByRole('form', { name: 'When readers learn “Mrs. Harker”' })
    fireEvent.change(within(form).getByRole('combobox', { name: 'Book' }), { target: { value: '' } })
    expect(within(form).getByRole('note').textContent).toContain("If it's a secret, that gives it away.")
    fireEvent.click(within(form).getByRole('button', { name: 'Make it an alias' }))

    await screen.findByText(/Saved the note's names/)
    expect(posts[0]).toEqual({ path: 'name', body: { entity: 'E-MINA', universe: 'dracula', expectedHash: 'h1', name: 'Mrs. Harker', plain: true } })
  })

  it('makes an alias a later name from a chapter, with no link changed', async () => {
    const posts = stubEditing()
    const panel = await openNames()

    fireEvent.click(within(panel).getByRole('button', { name: 'When readers learn “Madam Mina”' }))
    const form = within(panel).getByRole('form', { name: 'When readers learn “Madam Mina”' })
    fireEvent.change(within(form).getByRole('combobox', { name: 'Book' }), { target: { value: 'dracula' } })
    fireEvent.change(within(form).getByRole('combobox', { name: 'Chapter' }), { target: { value: 'dracula:4' } })
    expect(within(form).queryByRole('note')).toBeNull()
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))

    await screen.findByText(/Saved the note's names/)
    expect(posts[0].body).toMatchObject({ name: 'Madam Mina', from: 'dracula:4' })
  })

  it('adds a plain alias, or a later name with where readers learn it, and refuses a position left blank', async () => {
    const posts = stubEditing((path, body) =>
      path === 'name/add' && body.knownAs === true && body.from === null
        ? Response.json({ code: 'bad_argument', message: 'Say where readers learn the name, such as ember:38.' }, { status: 400 })
        : written(),
    )
    const main = await open('/u/dracula/e/E-MINA?at=dracula:3', 'Mina Harker')
    let panel = await openNames(undefined, main)
    let form = within(panel).getByRole('form', { name: 'Add a name' })
    fireEvent.change(within(form).getByRole('textbox', { name: 'Add a name' }), { target: { value: 'Mina Murray' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Add name' }))
    await screen.findByText(/Saved the note's names/)
    expect(posts[0]).toEqual({ path: 'name/add', body: { entity: 'E-MINA', universe: 'dracula', expectedHash: 'h1', name: 'Mina Murray', knownAs: false } })

    panel = await openNames(undefined, main)
    form = within(panel).getByRole('form', { name: 'Add a name' })
    fireEvent.change(within(form).getByRole('textbox', { name: 'Add a name' }), { target: { value: 'the Countess' } })
    fireEvent.change(within(form).getByRole('combobox', { name: 'Book' }), { target: { value: 'dracula' } })
    fireEvent.change(within(form).getByRole('combobox', { name: 'Chapter' }), { target: { value: 'dracula:5' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Add name' }))
    await waitFor(() => expect(posts).toHaveLength(2))
    expect(posts[1].body).toMatchObject({ name: 'the Countess', knownAs: true, from: 'dracula:5' })
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Other names' })).toBeNull())

    // "Type a position" left blank isn't "Always": it's a later name with no from, which is refused, so nothing leaks as an alias.
    panel = await openNames(undefined, main)
    form = within(panel).getByRole('form', { name: 'Add a name' })
    fireEvent.change(within(form).getByRole('textbox', { name: 'Add a name' }), { target: { value: 'Wilhelmina' } })
    fireEvent.change(within(form).getByRole('combobox', { name: 'Book' }), { target: { value: '*' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Add name' }))
    expect((await within(panel).findByRole('alert')).textContent).toContain('Say where readers learn the name')
    expect(posts[2].body).toMatchObject({ name: 'Wilhelmina', knownAs: true, from: null })
  })

  it('removes a name after asking, and says its links go to the note’s name', async () => {
    const posts = stubEditing()
    const panel = await openNames()

    fireEvent.click(within(panel).getByRole('button', { name: 'Remove “Madam Mina”' }))
    const form = within(panel).getByRole('form', { name: 'Remove “Madam Mina”' })
    expect(form.textContent).toContain('Links that use it change to the note\'s name, Mina Harker.')
    fireEvent.click(within(form).getByRole('button', { name: 'Remove name' }))

    await screen.findByText(/Saved the note's names/)
    expect(posts[0].body).toMatchObject({ name: 'Madam Mina', remove: true })
  })
})

describe('SC-WEB-116: the link picker says which name matched, and when readers learn it', () => {
  const hit = (extra: Partial<LookupHit>): LookupHit => ({ entity: mina, matchedBy: 'alias', alias: 'Mrs. Harker', linkText: 'Mrs. Harker', ...extra })

  it('marks a later name with its from, and a plain alias with nothing', () => {
    expect(matchLabel(hit({ knownAs: true, from: 'dracula:5' }))).toEqual({ name: 'Mina Harker', alias: 'Mrs. Harker', learned: 'readers learn it at dracula:5' })
    expect(matchLabel(hit({ knownAs: true, from: null })).learned).toBe('readers never learn it: no from')
    expect(matchLabel(hit({ alias: 'Madam Mina' })).learned).toBeNull()
    expect(matchLabel(hit({ matchedBy: 'name', alias: null })).learned).toBeNull()
  })
})

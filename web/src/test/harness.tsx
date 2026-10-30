import { vi } from 'vitest'
import { render } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { createMemoryHistory } from '@tanstack/react-router'
import { App } from '../App'
import type {
  DenSummary,
  EntityListResult,
  EntityPage,
  EntityPageResult,
  EntitySummary,
  FieldNamesResult,
  HierarchyResult,
  PositionView,
  PositionsResult,
} from '../api'
import type { TypeListResult, TypeView } from '../createApi'
import type { BookChapterFields, ChapterDefaultsSource, ChapterValueView } from '../chapterFieldsApi'
import type { SourceFieldType } from '../editApi'

// A small den for the UI's tests, from Dracula (public domain): one book of
// five chapters and a closing note, and a few of its notes.

export const den: DenSummary = {
  version: 1,
  name: 'Dracula',
  pinnedUniverse: null,
  universes: [{ key: 'dracula', name: 'Dracula', id: 'U1', series: 0, books: 1, entities: 7 }],
  remoteImages: false,
}

const titles = ["Jonathan Harker's Journal", "Jonathan Harker's Journal", "Jonathan Harker's Journal", "Jonathan Harker's Journal", 'Letters—Lucy and Mina']

export const positions: PositionView[] = [
  ...titles.map((title, i) => ({
    position: `dracula:${i + 1}`,
    book: 'dracula',
    bookName: 'Dracula',
    series: null,
    chapter: String(i + 1),
    number: i + 1,
    title,
  })),
  { position: 'dracula:note', book: 'dracula', bookName: 'Dracula', series: null, chapter: 'note', number: null, title: 'Note' },
]

function entity(key: string, name: string, type: string, scopeKind: EntitySummary['scopeKind'], from: string | null, status = 'canon'): EntitySummary {
  return {
    key,
    id: key,
    name,
    type,
    status,
    universe: 'dracula',
    scopeKind,
    scopeKey: 'dracula',
    path: `universes/dracula/${name.toLowerCase().replace(/\W+/g, '-')}.md`,
    from,
  }
}

export const YEAR_PATH = 'universes/dracula/notes/the year #1893.md'

export const entities: EntitySummary[] = [
  entity('E-CASTLE', 'Castle Dracula', 'location', 'universe', 'dracula:1'),
  entity('E-DATES', 'Dates and the year', 'note', 'universe', null),
  entity('E-JONATHAN', 'Jonathan Harker', 'character', 'book', 'dracula:1'),
  entity('E-MINA', 'Mina Harker', 'character', 'book', 'dracula:1'),
  entity('E-LUCY', 'Lucy Westenra', 'character', 'book', 'dracula:5'),
  entity('E-QUINCEY', 'Quincey P. Morris', 'character', 'book', 'dracula:5'),
  entity('E-SCENE', 'A scene at the inn', 'event', 'book', null, 'idea'),
  entity('E-QUINCEY-H', 'Quincey Harker', 'character', 'book', 'dracula:note'),
  // A note without an id is named by its path, slashes, spaces and all.
  { ...entity(YEAR_PATH, 'The year #1893', 'note', 'universe', null), id: null, path: YEAR_PATH },
]

/** Which entities are in the canon view at a position: draft or canon, and entered by then. */
export function inViewAt(at: string): EntitySummary[] {
  const ordinal = (p: string) => (p === 'dracula' ? -0.5 : positions.findIndex((x) => x.position === p))
  return entities.filter((e) => ['canon', 'draft'].includes(e.status) && ordinal(e.from ?? 'dracula') <= ordinal(at))
}

/** The den's types: some of the core pack's, by display name. */
export const types: TypeView[] = [
  { key: 'character', name: 'Character', plural: 'characters', description: 'A person in the story.', pack: 'core', builtIn: true },
  { key: 'event', name: 'Event', plural: 'events', description: null, pack: 'core', builtIn: true },
  { key: 'location', name: 'Location', plural: 'locations', description: null, pack: 'core', builtIn: true },
  { key: 'note', name: 'Note', plural: 'notes', description: null, pack: 'core', builtIn: true },
]

export const hierarchy: HierarchyResult = {
  version: 1,
  position: null,
  universe: {
    key: 'dracula',
    name: 'Dracula',
    id: 'U1',
    path: 'universes/dracula',
    entities: { total: 2, byType: { location: 1, note: 1 } },
    series: [],
    books: [
      {
        key: 'dracula',
        name: 'Dracula',
        id: 'B1',
        path: 'universes/dracula/books/dracula',
        status: 'draft',
        position: 'dracula',
        entities: { total: 5, byType: { character: 4, event: 1 } },
        chapters: positions.map((p) => ({ key: p.chapter!, number: p.number, title: p.title, position: p.position, heading: p.title })),
      },
    ],
  },
}

/** An entity page with nothing on it but its name, to build on. */
export function page(summary: EntitySummary, extra: Partial<EntityPage> = {}): EntityPage {
  return {
    ...summary,
    aliases: null,
    lead: `# ${summary.name}`,
    fields: [],
    sections: [],
    scope: [
      { kind: 'universe', key: 'dracula', name: 'Dracula', id: 'U1', path: 'universes/dracula' },
      { kind: 'book', key: 'dracula', name: 'Dracula', id: 'B1', path: 'universes/dracula/books/dracula' },
    ],
    entry: { position: summary.from, inherited: summary.from === null, description: '' },
    leadLinks: [],
    backlinks: [],
    ...extra,
  }
}

/** The core pack's chapter fields (types.md §9.2), as the API gives them. */
export const chapterFieldTypes: SourceFieldType[] = [
  { name: 'pov', type: 'ref', list: true, temporal: false, required: false, values: [], to: ['character'], description: 'The point-of-view character, or characters, of the chapter.', default: null },
  { name: 'narration', type: 'enum', list: false, temporal: false, required: false, values: ['first', 'second', 'third-limited', 'third-omniscient', 'third-objective'], to: null, description: null, default: null },
  { name: 'tense', type: 'enum', list: false, temporal: false, required: false, values: ['past', 'present', 'future'], to: null, description: 'The narrative tense.', default: null },
  { name: 'when', type: 'date', list: false, temporal: false, required: false, values: [], to: null, description: null, default: null },
  { name: 'setting', type: 'ref', list: true, temporal: false, required: false, values: [], to: ['location'], description: 'Where the chapter takes place.', default: null },
]

/** A chapter field nothing sets, to build on. */
export function unsetValue(name: string, extra: Partial<ChapterValueView> = {}): ChapterValueView {
  return { name, set: false, own: null, ignored: false, ignoredReason: null, ignoredItems: [], value: null, from: null, fromKey: null, none: false, default: null, defaultFrom: null, defaultFromKey: null, links: [], text: '', ...extra }
}

/** A value the book's chapter_defaults give, as a chapter inherits it. */
function fromBook(name: string, value: string): ChapterValueView {
  return unsetValue(name, { value, from: 'book', fromKey: 'dracula', default: value, defaultFrom: 'book', defaultFromKey: 'dracula' })
}

const JONATHAN_LINK = { text: '[[jonathan-harker]]', target: 'jonathan-harker', label: null, outcome: 'resolved', entity: 'E-JONATHAN', name: 'Jonathan Harker', type: 'character', problem: null } as const

/**
 * The book's chapters with their fields, as in the Dracula sample: the book
 * is told in the first person, in the past tense (its chapter_defaults), and
 * chapter 1 is Jonathan Harker's, in early May.
 */
export function bookChapterFields(extra: Partial<BookChapterFields> = {}): BookChapterFields {
  return {
    version: 1,
    universe: 'dracula',
    book: 'dracula',
    path: 'universes/dracula/books/dracula/book.md',
    hash: 'h-book',
    final: false,
    fieldTypes: chapterFieldTypes,
    chapters: positions.map((p) => ({
      key: p.chapter!,
      position: p.position,
      number: p.number,
      title: p.title,
      bare: false,
      fields: [
        p.chapter === '1'
          ? unsetValue('pov', { set: true, own: ['[[jonathan-harker]]'], value: ['[[jonathan-harker]]'], from: 'chapter', links: [JONATHAN_LINK] })
          : unsetValue('pov'),
        fromBook('narration', 'first'),
        fromBook('tense', 'past'),
        p.chapter === '1' ? unsetValue('when', { set: true, own: { text: '3–5 May', sort: [{ $number: '0' }, { $number: '5' }, { $number: '3' }] }, value: { text: '3–5 May', sort: [{ $number: '0' }, { $number: '5' }, { $number: '3' }] }, from: 'chapter' }) : unsetValue('when'),
        unsetValue('setting'),
      ],
    })),
    ...extra,
  }
}

/** The book's chapter_defaults (narration first, tense past), or a universe's or series' (none). */
export function chapterDefaults(kind: ChapterDefaultsSource['kind'] = 'book', extra: Partial<ChapterDefaultsSource> = {}): ChapterDefaultsSource {
  const book = kind === 'book'
  const own = (name: string, value: string) => unsetValue(name, { set: true, own: value, value, from: 'book', fromKey: 'dracula' })
  return {
    version: 1,
    universe: 'dracula',
    kind,
    key: 'dracula',
    path: book ? 'universes/dracula/books/dracula/book.md' : 'universes/dracula/universe.md',
    hash: book ? 'h-book' : 'h-universe',
    final: false,
    fieldTypes: chapterFieldTypes,
    fields: [unsetValue('pov'), book ? own('narration', 'first') : unsetValue('narration'), book ? own('tense', 'past') : unsetValue('tense'), unsetValue('when'), unsetValue('setting')],
    books: [{ key: 'dracula', final: false, chapters: positions.length, path: 'universes/dracula/books/dracula/book.md' }],
    ...extra,
  }
}

export type Responder = (path: string, params: URLSearchParams, init?: RequestInit) => unknown | Promise<unknown>

/**
 * Stubs fetch with the test den's API. `entityPage` answers /api/entity;
 * by default, the entity's bare page, or not in view as of a position
 * before it enters. `override` answers anything first, if it returns a value.
 */
export function stubApi(options: { entityPage?: (key: string, at: string | null) => EntityPageResult; override?: Responder; den?: DenSummary } = {}) {
  const calls: string[] = []
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://127.0.0.1')
    calls.push(`${url.pathname}${url.search}`)
    const params = url.searchParams
    const at = params.get('asOf')
    const body = (await options.override?.(url.pathname, params, init)) ?? answer(url.pathname, params, at, options)
    if (body instanceof Response) {
      return body
    }

    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  })
  vi.stubGlobal('fetch', fetchMock)
  return { calls, fetchMock }
}

function answer(path: string, params: URLSearchParams, at: string | null, options: Parameters<typeof stubApi>[0] = {}): unknown {
  if (at === '') {
    return new Response(JSON.stringify({ code: 'bad_argument', message: "'asOf' is empty. Leave it out for the full view." }), { status: 400 })
  }

  switch (path) {
    case '/api/den':
      return options.den ?? den
    case '/api/checkpoint':
      // Nothing changed since the last checkpoint, so the indicator is hidden.
      return {
        version: 1,
        backend: 'git',
        available: true,
        reason: null,
        enable: null,
        latest: null,
        inProgress: null,
        count: 0,
        changes: [],
        otherUniverses: 0,
        autoCheckpoint: { every: null, problem: null, runs: true },
      }
    case '/api/book/chapter-fields':
      return bookChapterFields()
    case '/api/chapter-defaults':
      return chapterDefaults(params.get('book') ? 'book' : params.get('series') ? 'series' : 'universe')
    case '/api/book/freeze':
      // The book is a draft, in a den with history.
      return {
        version: 1,
        universe: 'dracula',
        book: 'dracula',
        name: 'Dracula',
        path: 'universes/dracula/books/dracula/book.md',
        hash: 'h-draft',
        status: 'draft',
        backend: 'git',
        history: true,
        reason: null,
        nextTag: 'book/dracula/dracula/final',
        unfinished: false,
        otherChanges: 0,
      }
    case '/api/positions':
      return { version: 1, universe: 'dracula', count: positions.length, positions } satisfies PositionsResult
    case '/api/types':
      return { version: 1, types } satisfies TypeListResult
    case '/api/hierarchy':
      return { ...hierarchy, position: at }
    case '/api/fields': {
      // As of a position, only the fields of values the reader has met.
      const fields = at ? ['occupation'] : ['home', 'occupation']
      return { version: 1, position: at, count: fields.length, fields } satisfies FieldNamesResult
    }
    case '/api/entities': {
      const list = at ? inViewAt(at) : entities
      return { version: 1, count: list.length, total: list.length, truncated: false, entities: list } satisfies EntityListResult
    }
    case '/api/entity': {
      const key = params.get('entity')!
      if (options.entityPage) {
        return options.entityPage(key, at)
      }

      const summary = entities.find((e) => e.key === key)!
      const inView = !at || inViewAt(at).includes(summary)
      return {
        version: 1,
        view: at ? 'canon_as_of' : 'full',
        position: at,
        inView,
        note: inView ? null : `'${key}' isn't in canon as of ${at}.`,
        entity: inView ? page(summary) : null,
      } satisfies EntityPageResult
    }
    default:
      return new Response(JSON.stringify({ code: 'not_found', message: `No ${path}.` }), { status: 404 })
  }
}

/** Renders the whole app at `url`, with a memory history the test can read. */
export function renderApp(url: string) {
  // jsdom has no scrolling; the router restores scroll positions.
  window.scrollTo = () => {}
  const history = createMemoryHistory({ initialEntries: [url] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const result = render(<App queryClient={queryClient} history={history} />)
  return { ...result, history, queryClient }
}

/** The `at` in the history's current URL, or null. */
export function atOf(history: { location: { search: string } }): string | null {
  return new URLSearchParams(history.location.search).get('at')
}

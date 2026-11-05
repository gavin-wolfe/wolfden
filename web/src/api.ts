import { QueryClient, queryOptions } from '@tanstack/react-query'
import { refreshDefaults, writeDefaults } from './formHold'
import type { TextMatch } from './snippet'

// The shapes of Wolfden's API (src/Wolfden.Api/DenEndpoints.cs,
// ReadEndpoints.cs, ReadResults.cs, and Wolfden.Index's read results), in
// camelCase. Only the members the pages use are listed.

export interface UniverseSummary {
  key: string
  name: string
  id: string | null
  series: number
  books: number
  entities: number
}

export interface DenSummary {
  version: number
  name: string
  pinnedUniverse: string | null
  universes: UniverseSummary[]
  /** True if den.yaml has `remote_images: true`, so notes' images from websites load (#180). */
  remoteImages: boolean
}

export interface EntityCounts {
  total: number
  byType: Record<string, number>
}

export interface ChapterNode {
  key: string
  number: number | null
  title: string | null
  position: string
  /**
   * The heading to show (format.md F-CTR-6, #227): the title, then the chapter
   * fields `chapter_heading` lists, such as "Ardent — Vesta Yards,
   * 2350-04-12"; the title alone where no `chapter_heading` holds; null when
   * there's neither. As of a position (the hierarchy's `asOf`), a chapter
   * after it has its title alone, and none names a note the reader hasn't met.
   */
  heading: string | null
}

export interface BookNode {
  key: string
  name: string
  id: string | null
  path: string
  status: string
  position: string
  entities: EntityCounts
  chapters: ChapterNode[]
}

export interface SeriesNode {
  key: string
  name: string
  id: string | null
  path: string
  entities: EntityCounts
  books: BookNode[]
}

export interface UniverseNode {
  key: string
  name: string
  id: string | null
  path: string
  entities: EntityCounts
  series: SeriesNode[]
  books: BookNode[]
}

export interface HierarchyResult {
  version: number
  position: string | null
  universe: UniverseNode
}

export interface PositionView {
  position: string
  book: string
  bookName: string
  series: string | null
  chapter: string | null
  number: number | null
  title: string | null
}

export interface PositionsResult {
  version: number
  universe: string
  count: number
  positions: PositionView[]
}

export type ScopeKind = 'universe' | 'series' | 'book'

export interface EntitySummary {
  key: string
  id: string | null
  name: string
  type: string
  status: string
  universe: string
  scopeKind: ScopeKind
  scopeKey: string
  path: string
  from: string | null
}

export interface EntityListResult {
  version: number
  count: number
  total: number
  truncated: boolean
  entities: EntitySummary[]
}

/**
 * A note in a list at a position, for the Author's view (`marksAt`, #300). `met`
 * says readers there know it: an idea has no mark, but they don't.
 */
export interface MarkedEntity extends EntitySummary {
  met: boolean
  readerMark?: ReaderMark
}

export interface MarkedEntityListResult {
  version: number
  count: number
  total: number
  truncated: boolean
  /** What readers have met, then the rest, each in the list's usual order. */
  entities: MarkedEntity[]
  marksAt: string
}

export interface EntityRef {
  key: string
  id: string | null
  name: string
  type: string
}

export interface ContainerRef {
  kind: ScopeKind
  key: string
  name: string
  id: string | null
  path: string
}

export interface LinkView {
  text: string
  target: string
  heading: string | null
  label: string | null
  line: number
  column: number
  outcome: 'resolved' | 'ambiguous' | 'broken'
  entity: EntityRef | null
  section: string | null
  matches: EntityRef[] | null
  problem: string | null
  inView: boolean | null
}

export interface PageField {
  name: string
  value: unknown
  kind: 'plain' | 'entry' | 'temporal' | 'default'
  status: string | null
  pinned: boolean
  from: string | null
  until: string | null
  line: number
  entryIndex: number | null
  live: boolean | null
  links: LinkView[]
  /** In the Author's view, what readers at the position don't know of this value or entry. */
  readerMark?: ReaderMark
  /** In the Author's view, a position field's values later than the position, as written. */
  aheadValues?: string[]
  /** In the Author's view, a position field's values that can't be compared, as written. */
  unplacedValues?: string[]
}

/**
 * Why readers at the position don't see a note or fact the writer is shown (#300).
 * `at` is the position that makes it so, and can be null where no position names
 * that point. Only the Author's view has these: the words are in readerWords.ts.
 */
export interface ReaderMark {
  kind: 'ahead' | 'ended' | 'unplaced' | 'never'
  at: string | null
}

export interface PageSection {
  name: string
  level: number
  headingPath: string[]
  status: string
  pinned: boolean
  from: string | null
  until: string | null
  line: number
  body: string
  bookkeeping: boolean | null
  links: LinkView[]
  /** Why an error leaves this section out of every story view, in the full view only; otherwise null or missing. */
  leftOut?: SectionProblem[] | null
  /** In the Author's view, what readers at the position don't know of this section. */
  readerMark?: ReaderMark
}

/** An error that leaves a section out: a bad value in its comment, or in the comment of a section holding it. */
export interface SectionProblem {
  line: number | null
  rule: string
  message: string
}

export interface EntryView {
  position: string | null
  inherited: boolean
  description: string
}

export interface BacklinkView {
  source: EntityRef | null
  container: ContainerRef | null
  site: string
  path: string
  line: number
  column: number
  text: string
  field: string | null
  section: string | null
  chapter: string | null
  outlineRow: string | null
  status: string | null
  bookkeeping: boolean
  /** In the Author's view, what readers at the position don't know of what holds this link. */
  readerMark?: ReaderMark
  /** True for a link in a section an error leaves out, which only the writer's views list. */
  leftOut?: boolean
}

export interface EntityPage {
  key: string
  id: string | null
  name: string
  type: string
  status: string
  universe: string
  scopeKind: ScopeKind
  scopeKey: string
  path: string
  from: string | null
  aliases: string[] | null
  lead: string | null
  fields: PageField[]
  sections: PageSection[]
  scope: ContainerRef[]
  entry: EntryView
  leadLinks: LinkView[]
  backlinks: BacklinkView[]
  /** Why the note can't be edited, since it's in a final book (#28), or null (or left out) if it can be. */
  frozen?: string | null
  /** In the Author's view, for a note readers meet after the position, or that no position places. */
  readerMark?: ReaderMark
}

export interface EntityPageResult {
  version: number
  view: string
  position: string | null
  inView: boolean
  note: string | null
  entity: EntityPage | null
  /** In the Author's view, the position its marks are at. */
  marksAt?: string
}

export interface ApiError {
  code: string
  message: string
}

/** An error the API answered with, carrying its stable code. */
export class ApiRequestError extends Error {
  readonly code: string
  /** For a note an error leaves out of the den: what's known of it, with nothing in it as of a story position (#280). */
  readonly leftOut: LeftOutNote | null

  constructor(code: string, message: string, leftOut: LeftOutNote | null = null) {
    super(message)
    this.code = code
    this.leftOut = leftOut
  }
}

export interface LeftOutNote {
  /** The file with the error, which can be its book's or series' file. */
  path: string | null
  line: number | null
  rule: string | null
  message: string | null
}

/** Fetches JSON from the API, turning an error answer into an ApiRequestError. */
export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path, { headers: { Accept: 'application/json' } })
  const body: unknown = await response.json().catch(() => undefined)
  if (!response.ok) {
    const error = isObject(body) ? (body as Partial<ApiError>) : null
    throw new ApiRequestError(
      typeof error?.code === 'string' ? error.code : 'http_error',
      typeof error?.message === 'string' ? error.message : `The server answered ${response.status}.`,
      isObject(body) ? ((body as { leftOut?: LeftOutNote }).leftOut ?? null) : null,
    )
  }

  if (!isObject(body)) {
    throw new ApiRequestError('bad_response', `The server's answer to ${path} wasn't the JSON the page expects.`)
  }

  return body as T
}

function isObject(value: unknown): value is object {
  return typeof value === 'object' && value !== null
}

/**
 * An API path with its query parameters; those left undefined are left out.
 * An empty value is sent as it is, so the API refuses it (an empty `asOf` is
 * `bad_argument`) rather than the page quietly showing everything.
 */
export function apiPath(path: string, params: Record<string, string | number | undefined>): string {
  const query = new URLSearchParams()
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined) {
      query.set(name, String(value))
    }
  }

  const text = query.toString()
  return text ? `${path}?${text}` : path
}

/**
 * The page's query client. An error the API answered with (an ApiRequestError,
 * such as den_not_loaded) comes back the same until the writer acts, so it's
 * shown at once rather than tried again; a failed connection is tried again.
 */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: (failures, error) => !(error instanceof ApiRequestError) && failures < 3,
        ...refreshDefaults,
      },
      mutations: writeDefaults,
    },
  })
}

/**
 * The den's `remoteImages` in its first answer, for each query client (one
 * per page load). The page's Content-Security-Policy was fixed when the page
 * loaded, so turning the setting on lets images load only after a reload.
 */
const remoteImagesAtFirstAnswer = new WeakMap<QueryClient, boolean>()

export const denQuery = queryOptions({
  queryKey: ['den'],
  queryFn: async ({ client }) => {
    const den = await getJson<DenSummary>('/api/den')
    if (!remoteImagesAtFirstAnswer.has(client)) {
      remoteImagesAtFirstAnswer.set(client, den.remoteImages === true)
    }

    return den
  },
})

/** The den's `remoteImages` as first answered for `client`'s page, or undefined before the first answer. */
export function remoteImagesAtLoad(client: QueryClient): boolean | undefined {
  return remoteImagesAtFirstAnswer.get(client)
}

// Every query that depends on the story position has the position in its key
// (null for the full view), so each position is cached on its own.

export function hierarchyQuery(universe: string, at: string | undefined) {
  return queryOptions({
    queryKey: ['hierarchy', universe, at ?? null],
    queryFn: () => getJson<HierarchyResult>(apiPath('/api/hierarchy', { universe, asOf: at })),
  })
}

export function positionsQuery(universe: string) {
  return queryOptions({
    queryKey: ['positions', universe],
    queryFn: () => getJson<PositionsResult>(apiPath('/api/positions', { universe })),
  })
}

/** The most entities one list asks for: the API's own limit. */
export const ENTITY_LIMIT = 1000

export function entitiesQuery(universe: string, at: string | undefined) {
  return queryOptions({
    queryKey: ['entities', universe, at ?? null],
    queryFn: () => getJson<EntityListResult>(apiPath('/api/entities', { universe, asOf: at, limit: ENTITY_LIMIT })),
  })
}

/** Every note, for the Author's view at a position: what readers have met first, and the rest marked (#300). */
export function markedEntitiesQuery(universe: string, at: string) {
  return queryOptions({
    queryKey: ['entities', universe, at, 'marked'],
    queryFn: () => getJson<MarkedEntityListResult>(apiPath('/api/entities', { universe, marksAt: at, limit: ENTITY_LIMIT })),
  })
}

/**
 * A note's page. With no position, its full view. At a position, the Author's
 * view: all of it, with what readers there don't know marked. With `readers`,
 * the Reader's view: only what they know, as an AI assistant reads it (#300).
 */
export function entityQuery(universe: string, entity: string, at: string | undefined, readers = false) {
  const asked = at === undefined ? {} : readers ? { asOf: at, readers: 'true' } : { marksAt: at }
  return queryOptions({
    queryKey: ['entity', universe, entity, at ?? null, readers && at !== undefined],
    queryFn: () => getJson<EntityPageResult>(apiPath('/api/entity', { entity, universe, ...asked })),
  })
}

/** "1 book", "2 books". */
export function count(n: number, one: string, many: string): string {
  return `${n} ${n === 1 ? one : many}`
}

export interface SearchHit {
  entity: EntitySummary
  /** The section the match is in, or null for the name, aliases, lead, or a field. */
  section: string | null
  line: number | null
  /** The field the match is in (#179), or null (or left out) for the name, aliases, lead, or a section. */
  field?: string | null
  /** True for a match in a section an error leaves out of every story view. */
  leftOut?: boolean | null
  /** An excerpt with each match in square brackets, as the MCP tool gives it. The page marks matches from `text` and `matches`. */
  snippet: string
  /** The excerpt without marks. The API always gives it (#179); the page falls back to `snippet` unmarked. */
  text?: string
  /** Where the matches are in `text`; none marked if it's missing. */
  matches?: TextMatch[]
  status: string
  inCanon: boolean
  inComment: boolean
  /** With `marksAt`: what readers there don't know of the text it found, the note's own mark if that's why. */
  readerMark?: ReaderMark
  /** With `marksAt`: the note's own mark, if readers meet it after the position or no position places it. */
  noteMark?: ReaderMark
  /** With `marksAt`: which of a position field's values are later, or can't be compared. */
  aheadValues?: string[]
  unplacedValues?: string[]
  /** With `marksAt`: the snippet names a linked note readers there don't know, so they couldn't have found it by that name. */
  laterLink?: { status: string; readerMark?: ReaderMark }
  /** With `marksAt`: true if a search in the Reader's view there finds this. They come first. */
  met?: boolean
}

/** The field names a search can be narrowed to (/api/fields): as of a position, only those of values the reader has met. */
export interface FieldNamesResult {
  version: number
  position: string | null
  count: number
  fields: string[]
}

export interface SearchResult {
  version: number
  query: string
  count: number
  truncated: boolean
  hits: SearchHit[]
  marksAt?: string
}

/** What a search asks for besides its words: the filters /api/search takes. */
export interface SearchFilters {
  /** Search only this field's values. */
  field?: string
  type?: string
  status?: string
  series?: string
  book?: string
  canonOnly?: boolean
}

/** The statuses /api/search's `status` takes (DenReader.Statuses). */
export const STATUSES = ['idea', 'undecided', 'candidate', 'draft', 'canon', 'retconned'] as const

/** How many hits a search asks for at first, then on each "Show more", up to the API's own limit. */
export const SEARCH_LIMITS = [20, 50, 100, 200] as const

/** What a search's cache key holds: everything it was asked with, the position as `at` (null for everything). */
export interface SearchKey {
  universe: string
  query: string
  field: string | null
  type: string | null
  status: string | null
  series: string | null
  book: string | null
  canonOnly: boolean
  at: string | null
  /** True for the Reader's view at `at`. */
  readers: boolean
  limit: number
}

/**
 * A search (#25, #300). With no position, everything. In the Author's view, everything,
 * with what readers at `at` can find first and marks on the rest. In the Reader's view,
 * only what they can find. A blank `at` is sent as it is, and fails closed.
 */
export function searchQuery(universe: string, query: string, filters: SearchFilters, at: string | undefined, limit: number, readers = false) {
  const { field, type, status, series, book, canonOnly } = filters
  const key: SearchKey = {
    universe,
    query,
    field: field ?? null,
    type: type ?? null,
    status: status ?? null,
    series: series ?? null,
    book: book ?? null,
    canonOnly: canonOnly ?? false,
    at: at ?? null,
    readers: readers && at !== undefined,
    limit,
  }
  const position = at === undefined ? {} : readers ? { asOf: at, readers: 'true' } : { marksAt: at }
  return queryOptions({
    queryKey: ['search', key] as const,
    queryFn: () =>
      getJson<SearchResult>(
        apiPath('/api/search', { query, universe, field, type, status, series, book, canonOnly: canonOnly ? 'true' : undefined, ...position, limit }),
      ),
  })
}

/** The fields a search can be narrowed to: every one, or in the Reader's view at `at`, only those with a value readers there can see. */
export function fieldsQuery(universe: string, at?: string) {
  return queryOptions({
    queryKey: ['fields', universe, at ?? null],
    queryFn: () => getJson<FieldNamesResult>(apiPath('/api/fields', at === undefined ? { universe } : { universe, asOf: at, readers: 'true' })),
  })
}

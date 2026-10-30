import { queryOptions } from '@tanstack/react-query'
import { ApiRequestError, apiPath, getJson, type ApiError } from './api'
import type { DiagnosticView } from './editApi'
import type { Json } from './fieldModel'

// Creating series and books (#213; src/Wolfden.Api/ContainerEndpoints.cs),
// in camelCase: the works a new one can be placed after, and the create.

/** A series or book a new work can be placed after. */
export interface WorkView {
  kind: 'series' | 'book'
  key: string
  name: string
}

/** The works of a universe, or of one series, in story order (GET /api/works). */
export interface WorksView {
  version: number
  universe: string
  series: string | null
  name: string
  /** The universe.md or series.md that lists them. */
  path: string
  /** That file's hash, to send back with the create. */
  hash: string
  works: WorkView[]
}

/** What a create made (POST /api/series/create, /api/book/create). */
export interface ContainerCreateResult {
  version: number
  universe: string
  kind: 'series' | 'book'
  key: string
  name: string
  series: string | null
  path: string
  hash: string
  id: string
  chapters: number
  effects: string[]
  warnings: DiagnosticView[]
  /** Every file written: the new one, and the universe.md or series.md whose list changed. */
  files: { path: string; hash: string; created: boolean }[]
  idAdded: string | null
}

export function worksQuery(universe: string, series: string | undefined) {
  return queryOptions({
    queryKey: ['works', universe, series ?? null],
    queryFn: () => getJson<WorksView>(apiPath('/api/works', { universe, series })),
    // A create always starts from the file as it is.
    staleTime: 0,
  })
}

/** What the New series and New book forms send. */
export interface ContainerRequest {
  name: string
  universe: string
  /** For a book in a series; left out, a standalone book. */
  series?: string
  /** The key of the work it comes after; left out, at the end (or with first, at the start). */
  after?: string
  /** True to put it at the start of the story order, before every work; not with after. */
  first?: boolean
  /** A book's chapters, 1 to N; left out, none. */
  chapters?: number
  expectedHash: string
}

/** Creates a series or book, sent as JSON (the API takes nothing else), and returns what was made, or throws an ApiRequestError. */
export function createContainer(kind: 'series' | 'book', body: ContainerRequest): Promise<ContainerCreateResult> {
  return post<ContainerCreateResult>(`/api/${kind}/create`, body)
}

/** POSTs a create as JSON and returns the answer, which names a key, or throws an ApiRequestError. */
async function post<T extends { key: string }>(path: string, body: object): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  const answer: unknown = await response.json().catch(() => undefined)
  const json = typeof answer === 'object' && answer !== null ? (answer as Partial<ApiError>) : null
  if (!response.ok) {
    throw new ApiRequestError(
      typeof json?.code === 'string' ? json.code : 'http_error',
      typeof json?.message === 'string' ? json.message : `The server answered ${response.status}.`,
    )
  }

  const result = json as unknown as T | null
  if (!result?.key) {
    throw new ApiRequestError('bad_response', "The server's answer wasn't the JSON the page expects.")
  }

  return result
}

// Adding a chapter (#215): a book's chapters with the hash of its book.md,
// and the add.

/** A book's chapters, with the hash of its book.md (GET /api/book/chapters). */
export interface BookChapters {
  version: number
  universe: string
  book: string
  path: string
  /** book.md's hash, to send back with the add. */
  hash: string
  final: boolean
  chapters: { key: string; number: number | null; title: string | null }[]
}

/** What adding a chapter did (POST /api/chapter/create). */
export interface ChapterAddResult {
  version: number
  universe: string
  book: string
  path: string
  hash: string
  /** The new chapter's key, which never changes. */
  key: string
  position: string
  number: number | null
  title: string | null
  index: number
  /** The chapters after it whose display numbers changed; their keys didn't. */
  renumbered: { key: string; from: number; to: number }[]
  effects: string[]
  warnings: DiagnosticView[]
  files: { path: string; hash: string; created: boolean }[]
  idAdded: string | null
}

export function bookChaptersQuery(universe: string, book: string) {
  return queryOptions({
    queryKey: ['bookChapters', universe, book],
    queryFn: () => getJson<BookChapters>(apiPath('/api/book/chapters', { universe, book })),
    // An add always starts from the file as it is.
    staleTime: 0,
  })
}

/** What the Add chapter form sends. */
export interface ChapterRequest {
  universe: string
  book: string
  /** Left out, no title. */
  title?: string
  /** The key of the chapter it comes after; left out, at the end. */
  after?: string
  /** Its own chapter fields, by name (#225); a field left out is inherited from chapter_defaults. */
  fields?: Record<string, Json>
  expectedHash: string
}

/** Adds a chapter, sent as JSON (the API takes nothing else), and returns what was added, or throws an ApiRequestError. */
export function addChapter(body: ChapterRequest): Promise<ChapterAddResult> {
  return post<ChapterAddResult>('/api/chapter/create', body)
}

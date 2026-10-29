import { queryOptions } from '@tanstack/react-query'
import { ApiRequestError, apiPath, getJson, type ApiError } from './api'
import type { DiagnosticView } from './editApi'

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
  /** The key of the work it comes after; left out, at the end. */
  after?: string
  /** A book's chapters, 1 to N; left out, none. */
  chapters?: number
  expectedHash: string
}

/** Creates a series or book, sent as JSON (the API takes nothing else), and returns what was made, or throws an ApiRequestError. */
export async function createContainer(kind: 'series' | 'book', body: ContainerRequest): Promise<ContainerCreateResult> {
  const response = await fetch(`/api/${kind}/create`, {
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

  const result = json as unknown as ContainerCreateResult | null
  if (!result?.key) {
    throw new ApiRequestError('bad_response', "The server's answer wasn't the JSON the page expects.")
  }

  return result
}

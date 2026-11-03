import { queryOptions } from '@tanstack/react-query'
import { apiPath, getJson, type EntitySummary } from './api'
import { EditError, type WriteResult } from './editApi'

// The den's errors and fixing them (#280; src/Wolfden.Api/FileEndpoints.cs).

export type ErrorReach = 'section' | 'file' | 'container' | 'den'

export interface DenErrorView {
  path: string
  /** The universe it's in, or null for a den-level file. */
  universe: string | null
  /** The note's name, if the den could read it. */
  name: string | null
  kind: 'note' | 'outline' | 'book' | 'series' | 'universe' | 'den' | 'pack' | 'folder' | 'other'
  line: number | null
  rule: string
  message: string
  reach: ErrorReach
  section: string | null
  sectionLine: number | null
  /** True if Edit file can open it here. */
  canEdit: boolean
  /** For a folder said to have no file, or a pack said not to be there, what's there but can't be read. */
  heldFile: string | null
}

export interface DenErrors {
  version: number
  count: number
  truncated: boolean
  denLoaded: boolean
  errors: DenErrorView[]
}

/** How often the indicator asks while the page is open: the answer costs a look at the files' sizes and times unless one changed. */
export const DEN_ERRORS_POLL_MS = 5000

export const denErrorsQuery = queryOptions({
  queryKey: ['den-errors'],
  queryFn: () => getJson<DenErrors>('/api/den/errors'),
  refetchInterval: DEN_ERRORS_POLL_MS,
  refetchOnWindowFocus: true,
  staleTime: 0,
})

/** What the error leaves out, in words. */
export function leavesOut(error: Pick<DenErrorView, 'reach' | 'kind' | 'section'>): string {
  switch (error.reach) {
    case 'section':
      return `The section “${error.section}” and its subsections.`
    case 'den':
      return 'The whole den: nothing shows until this is fixed.'
    case 'container':
      switch (error.kind) {
        case 'book':
          return 'The whole book and everything filed in it.'
        case 'series':
          return 'The whole series and everything in it.'
        case 'universe':
          return 'The whole universe and everything in it.'
        default:
          return 'This folder and everything in it.'
      }

    default:
      return error.kind === 'note' ? 'The whole note.' : error.kind === 'outline' ? "The book's outline." : error.kind === 'pack' ? "This file of the pack, and what it defines." : 'This file.'
  }
}

/** Why Edit file can't open it, and where to fix it instead. */
export function fixElsewhere(error: Pick<DenErrorView, 'kind' | 'path' | 'rule' | 'heldFile'>): string {
  if (error.rule === 'F-GEN-5') {
    return `This isn’t about what’s in it, so there’s nothing to edit here. It clears by itself within a minute of Wolfden being able to ${error.kind === 'folder' ? 'look in it' : 'read it'}.`
  }

  if (error.heldFile) {
    return `Nothing is missing, so don’t add anything: ${error.heldFile.split('/').pop()} is there, but Wolfden can’t read it right now. It’s in this list too, and this clears with it.`
  }

  switch (error.kind) {
    case 'den':
      return 'Wolfden can’t edit den.yaml here: it’s missing, or this server is pinned to one universe and every universe shares it. Fix it in your own editor, or restore it.'
    case 'folder':
      return 'This folder is missing its file. Add it or move the folder in your file manager.'
    default:
      return 'Wolfden doesn’t edit this file. Fix it in your own editor.'
  }
}

export interface FileError {
  line: number | null
  rule: string
  message: string
  reach: ErrorReach
  section: string | null
  sectionLine: number | null
}

/** A file as Edit file needs it, by its path (GET /api/file/source). */
export interface FileSource {
  version: number
  path: string
  kind: DenErrorView['kind']
  universe: string | null
  hash: string
  /** The whole file, with LF line breaks, or null if it isn't valid UTF-8. */
  text: string | null
  lineEndings: 'lf' | 'crlf' | 'mixed'
  byteOrderMark: boolean
  /** Why it can't be edited (a final book), or null. */
  frozen: string | null
  errors: FileError[]
  /** The note, if the den reads it. */
  entity: EntitySummary | null
}

export function fileSourceQuery(path: string) {
  return queryOptions({
    queryKey: ['file-source', path],
    queryFn: () => getJson<FileSource>(apiPath('/api/file/source', { path })),
    // An editor starts from the file as it is.
    staleTime: 0,
    refetchOnWindowFocus: false,
  })
}

/** A save the API refused. For `stale_file`, `currentFile` is the file as it is now, if it can still be read. */
export class FileEditError extends EditError {
  readonly currentFile: FileSource | null

  constructor(code: string, message: string, currentFile: FileSource | null) {
    super(code, message, null)
    this.currentFile = currentFile
  }
}

/** Saves a file by its path, and returns the result, or throws a FileEditError. */
export async function saveFile(path: string, text: string, expectedHash: string): Promise<WriteResult> {
  const response = await fetch('/api/file/edit', {
    method: 'POST',
    headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
    body: JSON.stringify({ path, text, expectedHash }),
  })
  const answer: unknown = await response.json().catch(() => undefined)
  const json = typeof answer === 'object' && answer !== null ? (answer as Record<string, unknown>) : null
  if (!response.ok) {
    throw new FileEditError(
      typeof json?.code === 'string' ? json.code : 'http_error',
      typeof json?.message === 'string' ? json.message : `The server answered ${response.status}.`,
      (json?.currentFile as FileSource | undefined) ?? null,
    )
  }

  if (!json) {
    throw new FileEditError('bad_response', "The server's answer to the save wasn't the JSON the page expects.", null)
  }

  return json as unknown as WriteResult
}

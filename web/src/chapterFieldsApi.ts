import { queryOptions } from '@tanstack/react-query'
import { ApiRequestError, apiPath, getJson, type ApiError, type LinkView } from './api'
import type { SourceFieldType, WriteResult } from './editApi'
import type { ChapterFieldChange, Json } from './fieldModel'

// Chapter fields (#225; src/Wolfden.Api/ContainerEndpoints.cs), in
// camelCase: each chapter's fields and each container's chapter_defaults,
// as written and as they hold (types.md §8), with the den's chapter field
// definitions, and edits of one field at a time, made by the MCP tools'
// update_chapter_fields and update_chapter_defaults.

/** Where a value comes from: a chapter's own, or a container's chapter_defaults. */
export type ValueFrom = 'chapter' | 'book' | 'series' | 'universe'

/** A link in a chapter field's value, resolved. */
export interface ChapterLinkView {
  text: string
  target: string
  label: string | null
  outcome: 'resolved' | 'ambiguous' | 'broken'
  /** The entity it reaches, by key, or null. */
  entity: string | null
  name: string | null
  type: string | null
  problem: string | null
}

/** One chapter field of a chapter, or of a container's chapter_defaults. Values are as written. */
export interface ChapterValueView {
  name: string
  /** True if the chapter (or the container) writes the field, even with a value that's ignored. */
  set: boolean
  own: Json | null
  /** Written, but null or not a value of the field, so it's ignored and the value is inherited (F-CHAP-3). */
  ignored: boolean
  /** The value that holds, or null if nothing sets it. */
  value: Json | null
  from: ValueFrom | null
  fromKey: string | null
  /** The value that holds is [], "none here". */
  none: boolean
  /** What would hold without the field written here, or null. */
  default: Json | null
  defaultFrom: Exclude<ValueFrom, 'chapter'> | null
  defaultFromKey: string | null
  links: ChapterLinkView[]
  /** The lines of the file an edit of the field lands in, as written: a stale save is sent again only if they're unchanged. */
  text: string
}

export interface ChapterFieldsView {
  key: string
  position: string
  number: number | null
  title: string | null
  /** Written as a bare key in book.md, which has no place for fields. */
  bare: boolean
  fields: ChapterValueView[]
}

/** GET /api/book/chapter-fields. */
export interface BookChapterFields {
  version: number
  universe: string
  book: string
  path: string
  hash: string
  final: boolean
  fieldTypes: SourceFieldType[]
  chapters: ChapterFieldsView[]
}

/** GET /api/chapter-defaults. */
export interface ChapterDefaultsSource {
  version: number
  universe: string
  kind: 'universe' | 'series' | 'book'
  key: string
  path: string
  hash: string
  final: boolean
  fieldTypes: SourceFieldType[]
  fields: ChapterValueView[]
  books: { key: string; final: boolean; chapters: number; path: string }[]
}

/** A container whose chapter_defaults are shown: a universe, or a series or book in it. */
export interface DefaultsScope {
  universe: string
  series?: string
  book?: string
}

export function bookChapterFieldsQuery(universe: string, book: string) {
  return queryOptions({
    queryKey: ['bookChapterFields', universe, book],
    queryFn: () => getJson<BookChapterFields>(apiPath('/api/book/chapter-fields', { universe, book })),
  })
}

export function chapterDefaultsQuery(scope: DefaultsScope) {
  return queryOptions({
    queryKey: ['chapterDefaults', scope.universe, scope.series ?? null, scope.book ?? null],
    queryFn: () => getJson<ChapterDefaultsSource>(apiPath('/api/chapter-defaults', { universe: scope.universe, series: scope.series, book: scope.book })),
  })
}

type ChapterChange = ChapterFieldChange

/** Changes one field of a chapter. */
export function editChapterField(body: { universe: string; book: string; chapter: string; field: string; changes: ChapterChange[]; expectedHash: string }): Promise<WriteResult> {
  return post('/api/chapter/edit', body)
}

/** Changes one field of a container's chapter_defaults. */
export function editChapterDefault(body: DefaultsScope & { field: string; changes: ChapterChange[]; expectedHash: string }): Promise<WriteResult> {
  return post('/api/chapter-defaults/edit', body)
}

async function post(path: string, body: object): Promise<WriteResult> {
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

  if (!json) {
    throw new ApiRequestError('bad_response', "The server's answer to the edit wasn't the JSON the page expects.")
  }

  return json as unknown as WriteResult
}

/**
 * A value's links as the page's link views, so a value renders as a note's
 * field does. With `met` (as of a story position, the notes the reader has
 * met), a link to any other note doesn't say what it reaches (#26).
 */
export function linkViews(links: ChapterLinkView[], met?: ReadonlySet<string>): LinkView[] {
  return links.map((l) => ({
    text: l.text,
    target: l.target,
    heading: null,
    label: l.label,
    line: 0,
    column: 0,
    outcome: l.outcome,
    entity: l.entity && (!met || met.has(l.entity)) ? { key: l.entity, id: null, name: l.name ?? l.target, type: l.type ?? '' } : null,
    section: null,
    matches: null,
    problem: met && l.outcome !== 'resolved' ? null : l.problem,
    inView: met && l.entity ? met.has(l.entity) : null,
  }))
}

/** "book 'ember' defaults": where an inherited value comes from, in words. */
export function defaultsWords(from: Exclude<ValueFrom, 'chapter'>, key: string | null): string {
  return `${from}${key ? ` '${key}'` : ''} defaults`
}

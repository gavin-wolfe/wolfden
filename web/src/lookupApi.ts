import { apiPath, getJson, type EntitySummary } from './api'

/** A note a lookup found (#198), and what matched: its name, slug, id, or one of its aliases. */
export interface LookupHit {
  entity: EntitySummary
  matchedBy: 'name' | 'slug' | 'id' | 'alias'
  /** The alias that matched, for an alias match. */
  alias: string | null
  /**
   * The text that matched (name, alias, file name, or id), where `[[text]]`
   * reaches this note and no other as links resolve; null where it wouldn't.
   */
  linkText: string | null
}

export interface LookupResult {
  version: number
  universe: string
  query: string
  position: string | null
  count: number
  truncated: boolean
  matches: LookupHit[]
}

/** True if what's typed ends in a letter or digit, so a lookup is worth asking: not after a space or `-`. */
export function lookupable(words: string): boolean {
  return /[\p{L}\p{N}]$/u.test(words)
}

/**
 * The notes of a universe whose name, alias, file name, or id has a word
 * starting with each word typed, best first (/api/entities/lookup). `types`
 * narrows them to a ref field's `to` types. The editors never pass `at`: an
 * editing surface shows everything (#28); with it, only notes the reader
 * has met by then are looked at (#26).
 */
export function lookupEntities(
  universe: string,
  query: string,
  { types, at, limit = 8 }: { types?: readonly string[] | null; at?: string; limit?: number } = {},
): Promise<LookupResult> {
  return getJson<LookupResult>(
    apiPath('/api/entities/lookup', { query, universe, type: types && types.length > 0 ? types.join(',') : undefined, asOf: at, limit }),
  )
}

/** A match as a picker shows it: "Mina Harker", and for an alias match the alias it matched by. */
export function matchLabel(hit: LookupHit): { name: string; alias: string | null } {
  return { name: hit.entity.name, alias: hit.matchedBy === 'alias' ? hit.alias : null }
}

/** Why a pick isn't linked: no link is sure to reach the note (`linkFor` gives none). */
export function noSureLink(name: string): string {
  return `“${name}” has no id, and no link by its file name or name is sure to reach it. Add an id with Edit file on that note, then pick it again.`
}

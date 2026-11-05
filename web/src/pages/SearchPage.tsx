import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import {
  ApiRequestError,
  SEARCH_LIMITS,
  STATUSES,
  count,
  fieldsQuery,
  hierarchyQuery,
  positionsQuery,
  searchQuery,
  type HierarchyResult,
  type SearchFilters,
  type SearchHit,
  type SearchKey,
  type SearchResult,
  type EntitySummary,
  type PositionView,
} from '../api'
import { MarkList } from '../components/AheadOfReaders'
import { EntityLink, sectionAnchor } from '../components/Links'
import { ErrorState, Loading, StatusBadge } from '../components/States'
import { useDocumentTitle } from '../hooks'
import { typesQuery, type TypeView } from '../createApi'
import { snippetParts, type TextMatch } from '../snippet'
import {
  AHEAD_OF_READERS,
  MARKS_LEGEND,
  NOT_PLACED,
  ONLY_IN_ALIAS,
  ONLY_IN_ALIAS_WHY,
  ONLY_IN_LINK_TARGET,
  ONLY_IN_LINK_TARGET_WHY,
  aheadValuesWhy,
  notPlacedWhy,
} from '../readerWords'
import { describePosition, readersTree } from '../story'
import type { SearchPageSearch } from '../router'

const route = getRouteApi('/u/$universe/search')

/**
 * The search page (#25): full-text search of a universe's notes and their
 * field values, filtered by type, scope, and status, or to canon text only,
 * or narrowed to one field's values (#179). At a position, the Author's view
 * finds everything, what readers can find first and the rest tagged, and the
 * Reader's view finds only what readers can (#300). The page says which.
 */
export function SearchPage() {
  const { universe } = route.useParams()
  const search = route.useSearch() as SearchPageSearch
  const navigate = route.useNavigate()
  // A blank `at` is '' (it fails closed, as on every page), never everything.
  const at = search.at
  const readers = at !== undefined && search.readers === '1'
  const hierarchy = useQuery(hierarchyQuery(universe, undefined))
  // In the Reader's view, types, fields, and scopes are only what readers there have
  // reached, so a filter doesn't name what's later. Otherwise, every one.
  const metHierarchy = useQuery({ ...hierarchyQuery(universe, readers ? at : undefined), enabled: at !== '' })
  const fields = useQuery({ ...fieldsQuery(universe, readers ? at : undefined), enabled: at !== '' })
  const positions = useQuery(positionsQuery(universe))
  const q = search.q
  const filters: SearchFilters = {
    field: search.field,
    type: search.type,
    status: search.status,
    series: search.series,
    book: search.book,
    canonOnly: search.canon,
  }
  const filtered = Object.values(filters).some((v) => v !== undefined && v !== false)

  const setFilters = (patch: Partial<SearchPageSearch>) =>
    void navigate({ search: (prev: SearchPageSearch) => ({ ...prev, ...patch }), replace: true })

  const where = at === undefined || at === '' ? undefined : describePosition(at, positions.data?.positions ?? [])

  useDocumentTitle(q ? `Search: ${q}` : 'Search')

  return (
    <div className="page-body search-page">
      <p className="eyebrow">Search</p>
      <h1>{q ? <>Matches for “{q}”</> : 'Search'}</h1>
      <Filters
        hierarchy={readers ? readersScopes(metHierarchy.data, at, positions.data?.positions) : hierarchy.data}
        metHierarchy={metHierarchy.data}
        fields={fields.data?.fields}
        search={search}
        onChange={setFilters}
      />
      {at === '' && !q && <ErrorState error={new ApiRequestError('bad_argument', "The story position in the address is empty, so there's nothing to search as of.")} />}
      {where !== undefined &&
        (readers ? (
          <p className="search-position">
            Searching as of <strong>{where}</strong>: only what a reader can find by then.{' '}
            <Link to="." search={(prev: SearchPageSearch) => ({ ...prev, readers: undefined })}>
              Author's view
            </Link>
          </p>
        ) : (
          <p className="search-position">
            Searching everything, at <strong>{where}</strong>: what a reader can find by then comes first.
          </p>
        ))}
      {q ? (
        <Results
          universe={universe}
          q={q}
          filters={filters}
          filtered={filtered}
          at={at}
          readers={readers}
          where={where}
          hierarchy={hierarchy.data}
          positions={positions.data?.positions ?? []}
        />
      ) : (
        <p className="muted">Type in the search box at the top to search this universe's notes. Press / to go to it from anywhere.</p>
      )}
    </div>
  )
}

/** The hierarchy with only the books and series readers at `at` have reached, for the Scope filter. Nothing until it can be worked out. */
function readersScopes(
  hierarchy: HierarchyResult | undefined,
  at: string | undefined,
  positions: readonly PositionView[] | undefined,
): HierarchyResult | undefined {
  return hierarchy && at && positions ? { ...hierarchy, universe: readersTree(hierarchy.universe, at, positions) } : undefined
}

function Filters({
  hierarchy,
  metHierarchy,
  fields,
  search,
  onChange,
}: {
  hierarchy: HierarchyResult | undefined
  metHierarchy: HierarchyResult | undefined
  fields: string[] | undefined
  search: SearchPageSearch
  onChange: (patch: Partial<SearchPageSearch>) => void
}) {
  const u = hierarchy?.universe
  const books = u ? [...u.series.flatMap((s) => s.books), ...u.books] : []
  const denTypes = useQuery(typesQuery)
  const types = withParents(typesIn(metHierarchy), denTypes.data?.types)
  // A filter the lists don't have yet (still loading, or typed into the URL) stays chosen.
  const withCurrent = (list: string[], current: string | undefined) => (current && !list.includes(current) ? [...list, current] : list)
  const scope = search.book ? `book:${search.book}` : search.series ? `series:${search.series}` : ''

  return (
    <div className="search-filters" role="group" aria-label="Filters">
      <label>
        <span>Field</span>
        <select value={search.field ?? ''} onChange={(e) => onChange({ field: e.currentTarget.value || undefined })}>
          <option value="">Anywhere</option>
          {withCurrent(fields ?? [], search.field).map((f) => (
            <option key={f} value={f}>
              {f}
            </option>
          ))}
        </select>
      </label>
      <label>
        <span>Type</span>
        <select value={search.type ?? ''} onChange={(e) => onChange({ type: e.currentTarget.value || undefined })}>
          <option value="">Any type</option>
          {withCurrent(types, search.type).map((t) => (
            <option key={t} value={t}>
              {t}
            </option>
          ))}
        </select>
      </label>
      <label>
        <span>Scope</span>
        <select
          value={scope}
          onChange={(e) => {
            const [kind, key] = e.currentTarget.value.split(/:(.*)/s)
            onChange({ series: kind === 'series' ? key : undefined, book: kind === 'book' ? key : undefined })
          }}
        >
          <option value="">Whole universe</option>
          {u && u.series.length > 0 && (
            <optgroup label="Series">
              {u.series.map((s) => (
                <option key={s.key} value={`series:${s.key}`}>
                  {s.name}
                </option>
              ))}
            </optgroup>
          )}
          <optgroup label="Books">
            {books.map((b) => (
              <option key={b.key} value={`book:${b.key}`}>
                {b.name}
              </option>
            ))}
            {scope && !books.some((b) => `book:${b.key}` === scope) && !u?.series.some((s) => `series:${s.key}` === scope) && (
              <option value={scope}>{search.book ?? search.series}</option>
            )}
          </optgroup>
        </select>
      </label>
      <label>
        <span>Status</span>
        <select value={search.status ?? ''} onChange={(e) => onChange({ status: e.currentTarget.value || undefined })}>
          <option value="">Any status</option>
          {withCurrent([...STATUSES], search.status).map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>
      </label>
      <label className="check">
        <input type="checkbox" checked={search.canon === true} onChange={(e) => onChange({ canon: e.currentTarget.checked || undefined })} />
        <span>Canon text only</span>
      </label>
    </div>
  )
}

/**
 * The types to offer: those of the notes, and what they're kinds of, since
 * filtering by a type includes its kinds (types.md F-TYPE-13). A den of worlds
 * and ships can ask for every location without a note of type location.
 */
function withParents(met: string[], den: TypeView[] | undefined): string[] {
  const parents = met.flatMap((key) => den?.find((t) => t.key === key)?.kindOf ?? [])
  return [...new Set([...met, ...parents])].sort()
}

/** The types of the notes a hierarchy counts: as of a position, only those the reader has met. */
function typesIn(hierarchy: HierarchyResult | undefined): string[] {
  const u = hierarchy?.universe
  if (!u) {
    return []
  }

  const books = [...u.series.flatMap((s) => s.books), ...u.books]
  const counts = [u.entities, ...u.series.map((s) => s.entities), ...books.map((b) => b.entities)]
  return [...new Set(counts.flatMap((c) => Object.entries(c.byType).filter(([, n]) => n > 0).map(([type]) => type)))].sort()
}

/**
 * Keeps the last answer while the next one loads, but only at the same position
 * and in the same view: one from a later position, or the Author's view, could
 * show what the reader hasn't met.
 */
export function keepAtSamePosition(at: string | undefined, readers = false) {
  return (previous: SearchResult | undefined, previousQuery: { queryKey: readonly unknown[] } | undefined): SearchResult | undefined => {
    const key = previousQuery?.queryKey[1] as SearchKey | undefined
    return key && key.at === (at ?? null) && key.readers === readers ? previous : undefined
  }
}

function Results({
  universe,
  q,
  filters,
  filtered,
  at,
  readers,
  where,
  hierarchy,
  positions,
}: {
  universe: string
  q: string
  filters: SearchFilters
  filtered: boolean
  at: string | undefined
  readers: boolean
  where: string | undefined
  hierarchy: HierarchyResult | undefined
  positions: readonly PositionView[]
}) {
  // "Show more" asks again for more hits; a new search starts from the first step.
  const searchKey = JSON.stringify([q, filters, at, readers])
  const [more, setMore] = useState({ key: searchKey, step: 0 })
  const step = more.key === searchKey ? more.step : 0
  const result = useQuery({
    ...searchQuery(universe, q, filters, at, SEARCH_LIMITS[step], readers),
    placeholderData: keepAtSamePosition(at, readers),
  })
  // After "Show more", when the button goes (every match is shown, or the
  // API's limit is reached), focus moves to the line that says so.
  const askedForMore = useRef(false)
  const end = useRef<HTMLParagraphElement>(null)
  const showsButton = result.data?.truncated === true && step < SEARCH_LIMITS.length - 1
  const settled = result.isSuccess && !result.isPlaceholderData
  useEffect(() => {
    if (askedForMore.current && settled && !showsButton) {
      askedForMore.current = false
      end.current?.focus()
    }
  }, [settled, showsButton])

  if (result.isPending) {
    return <Loading what="matches" />
  }

  if (result.isError) {
    return <ErrorState error={result.error} />
  }

  // The answer shown may be the last search's while the next loads; its hits mark their own matches.
  const { hits, truncated } = result.data
  if (hits.length === 0) {
    return (
      <p className="muted" role="status">
        {where !== undefined && readers ? `No matches as of ${where}.` : `No matches for “${q}”.`}
        {filtered && ' Try without some of the filters.'}
      </p>
    )
  }

  const groups = groupByEntity(hits)
  const last = step === SEARCH_LIMITS.length - 1
  // In the Author's view, what no hit lets a reader find follows a line, as a list's later notes do.
  const firstLater = at !== undefined && !readers ? groups.findIndex((g) => !g.hits.some((h) => h.met)) : -1
  const tagged = hits.some(hasTag)
  return (
    <div aria-busy={result.isPlaceholderData} className={result.isPlaceholderData ? 'search-results stale' : 'search-results'}>
      <p className="muted" role="status">
        {truncated ? `The first ${count(hits.length, 'match', 'matches')}` : count(hits.length, 'match', 'matches')}, in{' '}
        {count(groups.length, 'note', 'notes')}.
      </p>
      {tagged && <p className="muted list-legend">{MARKS_LEGEND}</p>}
      <ol className="hit-groups">
        {groups.map(({ entity, hits: own }, i) => (
          <li key={entity.key} className={i === firstLater && i > 0 ? 'later-first' : undefined}>
            <p className="hit-entity">
              <EntityLink universe={universe} entityKey={entity.key}>
                {entity.name}
              </EntityLink>{' '}
              <StatusBadge status={entity.status} /> <span className="muted">{entity.type}, in {scopeName(entity, hierarchy)}</span>
              {own[0].noteMark && <MarkList mark={own[0].noteMark} positions={positions} note />}
            </p>
            <ul className="hits">
              {own.map((hit, j) => (
                <Hit key={j} universe={universe} hit={hit} positions={positions} />
              ))}
            </ul>
          </li>
        ))}
      </ol>
      {truncated && !last ? (
        <button
          type="button"
          onClick={() => {
            askedForMore.current = true
            setMore({ key: searchKey, step: step + 1 })
          }}
          disabled={result.isFetching}
        >
          Show more matches
        </button>
      ) : truncated ? (
        <p className="muted" ref={end} tabIndex={-1}>
          Only the first {hits.length} matches are shown. Add words or a filter to narrow the search.
        </p>
      ) : (
        step > 0 && (
          <p className="muted" ref={end} tabIndex={-1}>
            That's every match.
          </p>
        )
      )}
    </div>
  )
}

/** A hit's own tag or note's tag, so the list needs its one line to explain them. */
function hasTag(hit: SearchHit): boolean {
  return !!(hit.noteMark || hit.readerMark || hit.aheadValues || hit.unplacedValues || hit.laterLink?.readerMark || hit.hiddenIn)
}

/** True if two marks say the same, so a hit whose mark is its note's doesn't repeat it. */
function sameMark(a: SearchHit['readerMark'], b: SearchHit['readerMark']): boolean {
  return a?.kind === b?.kind && a?.at === b?.at
}

/**
 * What readers at the position don't know of a hit, in one plain tag, as nothing has
 * two (#300): what it's in, then a value it names, then a linked note it names.
 * A linked note out of canon has its own badge instead.
 */
function HitTag({ hit, positions }: { hit: SearchHit; positions: readonly PositionView[] }) {
  if (hit.readerMark) {
    return sameMark(hit.readerMark, hit.noteMark) ? null : <MarkList mark={hit.readerMark} positions={positions} />
  }

  if (hit.aheadValues) {
    return (
      <span className="ahead-of-readers" title={aheadValuesWhy(hit.aheadValues)}>
        {AHEAD_OF_READERS}
      </span>
    )
  }

  if (hit.unplacedValues) {
    return (
      <span className="ahead-of-readers" title={notPlacedWhy(hit.unplacedValues[0])}>
        {NOT_PLACED}
      </span>
    )
  }

  if (hit.laterLink) {
    return hit.laterLink.readerMark ? <MarkList mark={hit.laterLink.readerMark} positions={positions} note /> : <StatusBadge status={hit.laterLink.status} />
  }

  if (hit.hiddenIn) {
    return (
      <span className="ahead-of-readers" title={hit.hiddenIn === 'alias' ? ONLY_IN_ALIAS_WHY : ONLY_IN_LINK_TARGET_WHY}>
        {hit.hiddenIn === 'alias' ? ONLY_IN_ALIAS : ONLY_IN_LINK_TARGET}
      </span>
    )
  }

  return null
}

function Hit({ universe, hit, positions }: { universe: string; hit: SearchHit; positions: readonly PositionView[] }) {
  const bookkeeping = !hit.inCanon && !hit.inComment && !hit.field && !hit.leftOut && (hit.status === 'canon' || hit.status === 'draft')
  return (
    <li>
      <p className="hit-place">
        <Link
          to="/u/$universe/e/$"
          params={{ universe, _splat: hit.entity.key }}
          hash={hit.section ? sectionAnchor(hit.section) : undefined}
        >
          {hit.field ? `Field: ${hit.field}` : hit.section === null ? 'Name or opening' : hit.section || 'A section with no heading'}
        </Link>
        {hit.status !== hit.entity.status && <StatusBadge status={hit.status} />}
        {bookkeeping && <span className="badge">bookkeeping</span>}
        {hit.leftOut && <span className="badge problem">left out</span>}
        {hit.inComment && <span className="badge">in a comment</span>}
        <HitTag hit={hit} positions={positions} />
      </p>
      <p className="snippet">
        <Snippet text={hit.text ?? hit.snippet} matches={hit.matches ?? []} />
      </p>
    </li>
  )
}

/** A snippet with its matches marked, rendered as text. */
export function Snippet({ text, matches }: { text: string; matches: readonly TextMatch[] }) {
  return (
    <>
      {snippetParts(text, matches).map((part, i) => (part.match ? <mark key={i}>{part.text}</mark> : part.text))}
    </>
  )
}

function groupByEntity(hits: readonly SearchHit[]): { entity: EntitySummary; hits: SearchHit[] }[] {
  const groups = new Map<string, { entity: EntitySummary; hits: SearchHit[] }>()
  for (const hit of hits) {
    const group = groups.get(hit.entity.key) ?? { entity: hit.entity, hits: [] }
    group.hits.push(hit)
    groups.set(hit.entity.key, group)
  }

  return [...groups.values()]
}

/** The name of the universe, series, or book an entity is filed in. */
function scopeName(entity: EntitySummary, hierarchy: HierarchyResult | undefined): string {
  const u = hierarchy?.universe
  if (!u) {
    return entity.scopeKey
  }

  switch (entity.scopeKind) {
    case 'universe':
      return u.name
    case 'series':
      return u.series.find((s) => s.key === entity.scopeKey)?.name ?? entity.scopeKey
    default:
      return [...u.series.flatMap((s) => s.books), ...u.books].find((b) => b.key === entity.scopeKey)?.name ?? entity.scopeKey
  }
}

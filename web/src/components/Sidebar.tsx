import { useEffect, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { denQuery, hierarchyQuery, type BookNode, type PositionView, type ScopeKind } from '../api'
import { useShownError } from '../formHold'
import { useEntitiesInView, type ListedEntity } from '../hooks'
import { MARKS_LEGEND, listCutWords } from '../readerWords'
import { chapterLabel } from '../story'
import { EntityList } from './EntityList'
import { NewTypeButton } from './NewEntity'
import { typePlural } from './Links'
import { ErrorState, Loading } from './States'

/**
 * The hierarchy tree (#24): universe → series in story order → books →
 * chapters and entity types → entities. Universe and series notes sit under
 * their own scope. Each type group has a New <type> button that makes another
 * of that type there (#29). At a position, the Author's view lists every note,
 * what the reader has met first, and one line says what the tags on the rest
 * are. The Reader's view has only what the reader knows: no later notes, books,
 * or chapters, no count of them, and no New buttons, as it's read-only (#300).
 */
export function Sidebar({ universe }: { universe: string | undefined }) {
  const den = useQuery(denQuery)
  const navigate = useNavigate()
  const universes = den.data?.universes ?? []

  return (
    <div className="tree">
      {universes.length > 1 && (
        <label className="switcher">
          <span>Universe</span>
          <select
            value={universe ?? ''}
            onChange={(e) =>
              // Positions belong to one universe, so a switch starts at everything.
              void navigate({ to: '/u/$universe', params: { universe: e.currentTarget.value }, search: { at: undefined, readers: undefined } })
            }
          >
            {!universe && <option value="">Choose…</option>}
            {universes.map((u) => (
              <option key={u.key} value={u.key}>
                {u.name}
              </option>
            ))}
          </select>
        </label>
      )}
      {universe ? <UniverseTree universe={universe} /> : <p className="muted">Choose a universe to browse it.</p>}
    </div>
  )
}

function UniverseTree({ universe }: { universe: string }) {
  const view = useEntitiesInView(universe)
  const hierarchy = useQuery(hierarchyQuery(universe, view.readers ? view.at : undefined, view.readers))
  const failedHierarchy = useShownError(hierarchy)
  const params = useParams({ strict: false }) as { book?: string; chapter?: string; _splat?: string }

  if (hierarchy.isPending || view.isPending) {
    return <Loading what="the contents" />
  }

  const error = failedHierarchy ?? view.error
  if (error) {
    return <ErrorState error={error} />
  }

  const positions = view.positions!
  const u = hierarchy.data!.universe
  const all = view.entities!
  const current = all.find((e) => e.key === params._splat || e.id === params._splat || e.path === params._splat)
  const byScope = groupByScope(all)
  const scopeGroups = (kind: ScopeKind, key: string, name: string, final = false) => (
    <TypeGroups
      universe={universe}
      scope={{ kind, key }}
      scopeName={name}
      entities={byScope.get(`${kind}:${key}`) ?? []}
      positions={positions}
      readers={view.readers}
      current={current}
      final={final}
    />
  )
  const book = (b: BookNode) => (
    <TreeNode
      key={b.key}
      label={<Link to="/u/$universe/b/$book" params={{ universe, book: b.key }}>{b.name}</Link>}
      open={params.book === b.key || current?.scopeKey === b.key || u.books.length + u.series.reduce((n, s) => n + s.books.length, 0) === 1}
    >
      {b.chapters.length > 0 && (
        <TreeNode label="Chapters" open={params.book === b.key && params.chapter !== undefined}>
          <ol className="chapter-list">
            {b.chapters.map((c) => (
              <li key={c.key}>
                <Link to="/u/$universe/b/$book/c/$chapter" params={{ universe, book: b.key, chapter: c.key }}>
                  {c.number !== null && `${c.number}. `}
                  {c.title ?? chapterLabel(c)}
                </Link>
              </li>
            ))}
          </ol>
        </TreeNode>
      )}
      {scopeGroups('book', b.key, b.name, b.status === 'final')}
    </TreeNode>
  )

  return (
    <nav aria-label="Contents">
      <p className="tree-root">
        <Link to="/u/$universe" params={{ universe }} activeOptions={{ exact: true }}>
          {u.name}
        </Link>
      </p>
      {view.truncated && <p className="muted list-legend">{listCutWords(all.length, view.total, view.at !== undefined && !view.readers)}</p>}
      {all.some((e) => e.readerMark) && <p className="muted list-legend">{MARKS_LEGEND}</p>}
      {scopeGroups('universe', u.key, u.name)}
      {u.series.map((s) => (
        <TreeNode key={s.key} label={<Link to="/u/$universe/s/$series" params={{ universe, series: s.key }}>{s.name}</Link>} open>
          {scopeGroups('series', s.key, s.name)}
          {s.books.map(book)}
        </TreeNode>
      ))}
      {u.books.map(book)}
    </nav>
  )
}

function groupByScope(entities: readonly ListedEntity[]): Map<string, ListedEntity[]> {
  const groups = new Map<string, ListedEntity[]>()
  for (const e of entities) {
    const key = `${e.scopeKind}:${e.scopeKey}`
    const group = groups.get(key)
    if (group) {
      group.push(e)
    } else {
      groups.set(key, [e])
    }
  }

  return groups
}

function TypeGroups({
  universe,
  scope,
  scopeName,
  entities,
  positions,
  readers,
  current,
  final,
}: {
  universe: string
  scope: { kind: ScopeKind; key: string }
  scopeName: string
  entities: readonly ListedEntity[]
  positions: readonly PositionView[]
  readers: boolean
  current: ListedEntity | undefined
  /** A final book takes nothing new, so its lists have no New <type> (#285), as its page has no New note (#203). */
  final: boolean
}) {
  const types = [...new Set(entities.map((e) => e.type))].sort()
  return (
    <>
      {types.map((type) => {
        const ofType = entities.filter((e) => e.type === type)
        const met = ofType.filter((e) => e.met).length
        return (
          <TreeNode
            key={type}
            label={
              <>
                {typePlural(type)}{' '}
                <span className="count" title={met < ofType.length ? `Readers have met ${met} of these ${ofType.length} by this point.` : undefined}>
                  {met < ofType.length ? `${met} of ${ofType.length}` : ofType.length}
                </span>
              </>
            }
            open={current !== undefined && current.type === type && entities.includes(current)}
          >
            <EntityList universe={universe} entities={ofType} positions={positions} current={current?.key} />
            {!final && !readers && <NewTypeButton type={type} scope={scope} scopeName={scopeName} />}
          </TreeNode>
        )
      })}
    </>
  )
}

/** A node of the tree that opens and closes. */
function TreeNode({ label, open, children }: { label: ReactNode; open?: boolean; children: ReactNode }) {
  const [isOpen, setOpen] = useState(open ?? false)
  // Opens when the page moves to something inside it; the writer closes it.
  useEffect(() => {
    if (open) {
      setOpen(true)
    }
  }, [open])
  return (
    <details className="tree-node" open={isOpen} onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary>{label}</summary>
      <div className="tree-children">{children}</div>
    </details>
  )
}

import { Fragment, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, getRouteApi, useRouter, useRouterState } from '@tanstack/react-router'
import { wasHistoryStep } from '../router'
import {
  ApiRequestError,
  entityQuery,
  positionsQuery,
  type LeftOutNote,
  type BacklinkView,
  type ContainerRef,
  type EntityPage,
  type EntitySummary,
  type LinkView,
  type PageField,
  type PageSection,
  type PositionView,
  type ReaderMark,
} from '../api'
import { AheadOfReaders, MarkList, MarkTag, ReaderTag } from '../components/AheadOfReaders'
import { EntityTabs } from '../components/EntityTabs'
import { EntityLink, LinkedText, createLinkMatcher, sectionAnchor } from '../components/Links'
import { Markdown } from '../components/Markdown'
import {
  AddSection,
  EditButton,
  EditingProvider,
  EditingStatus,
  FrozenNotice,
  LeftOutFlag,
  PieceEditor,
  RemoveSection,
  RenameSection,
  useEditing,
  type EditTarget,
} from '../components/Editing'
import { AddField, FieldEditor, FieldTools } from '../components/FieldEditing'
import { denErrorCountQuery, denErrorsQuery } from '../denErrorsApi'
import { sourceQuery, type EntitySource } from '../editApi'
import { ErrorState, Loading, StatusBadge } from '../components/States'
import { useDocumentTitle, useNoteSummary, usePosition, usePositionsInView } from '../hooks'
import { chapterWords, describePosition, entryOf, isAheadOf, ordinalOf } from '../story'
import { NOT_PLACED, MARKS_LEGEND, notPlacedWhy } from '../readerWords'

const route = getRouteApi('/u/$universe/e/$')

/**
 * An entity's page (#24): its name, type, status, scope, and where it enters
 * the story; its fields and sections, with links in-app; and what links to
 * it. At a position, by default it's the Author's view (#300): all of the note,
 * tagged with what readers there don't know, and it can be edited. The Reader's
 * view shows only what they know, and is read-only, and a note the reader hasn't
 * met shows a banner saying where they first meet it, and nothing else of it.
 */
export function EntityPageView() {
  const { universe, _splat: key = '' } = route.useParams()
  const { at, readers } = usePosition()
  const [target, setTarget] = useState<EditTarget | null>(null)
  const live = useQuery(entityQuery(universe, key, at, readers))
  // While something's being edited the page stays as the editor opened on it. An answer already on its way when
  // the editor opened could move a section's line, or fail, and either would take the editor and its text away.
  const opened = useRef<typeof live.data>(undefined)
  if (target === null) {
    opened.current = undefined
  } else if (opened.current === undefined) {
    opened.current = live.data
  }
  const data = opened.current ?? live.data
  const positions = usePositionsInView(universe).data?.positions ?? []
  const summary = useNoteSummary(universe, key, data !== undefined && data.entity === null)
  // Only the API's page names the note, and it's null when the reader hasn't met it.
  useDocumentTitle(data ? (data.entity?.name ?? 'Not met yet') : undefined)

  if (opened.current === undefined && live.isError) {
    return live.error instanceof ApiRequestError && live.error.leftOut ? (
      <LeftOutPage message={live.error.message} note={live.error.leftOut} at={at} onErrorsChanged={() => void live.refetch()} />
    ) : (
      <ErrorState error={live.error} />
    )
  }

  if (!data) {
    return <Loading what="the note" />
  }

  const { entity } = data
  if (!entity) {
    return <NotInView summary={summary} note={data.note} universe={universe} at={at} />
  }

  if (readers) {
    return <EntityArticle universe={universe} entity={entity} positions={positions} readersView />
  }

  return (
    <EditingProvider universe={universe} entityKey={key} frozen={entity.frozen ?? null} target={target} setTarget={setTarget}>
      <EntityArticle universe={universe} entity={entity} positions={positions} />
    </EditingProvider>
  )
}

/**
 * The page of a note an error leaves out of the den (#280): it says so and
 * names the line, with a way to fix it. As of a story position it says only
 * that it's left out, since the file's name and the error could say what the
 * reader hasn't met. It looks again whenever the den's errors change, so it
 * becomes the note once that's fixed, wherever it was fixed.
 */
function LeftOutPage({ message, note, at, onErrorsChanged }: { message: string; note: LeftOutNote; at: string | undefined; onErrorsChanged: () => void }) {
  const { setAt, readers } = usePosition()
  const errors = useQuery(readers ? denErrorCountQuery : denErrorsQuery)
  const signature = errors.data ? `${errors.data.count}:${errors.data.errors.map((e) => `${e.path}:${e.line}:${e.rule}`).join('|')}` : null
  const [seen, setSeen] = useState(signature)
  useEffect(() => {
    if (signature !== seen) {
      setSeen(signature)
      if (seen !== null) {
        onErrorsChanged()
      }
    }
  }, [signature, seen, onErrorsChanged])
  return (
    <div className="page-body">
      <h1>This note is left out</h1>
      <div className="left-out-flag" role="alert">
        <p>{message}</p>
      </div>
      {note.path ? (
        <p>
          <Link to="/file" search={{ path: note.path }}>
            Edit file
          </Link>
        </p>
      ) : (
        at !== undefined && (
          <p>
            <button type="button" onClick={() => setAt(undefined)}>
              Show everything
            </button>
          </p>
        )
      )}
      <p className="muted">
        Nothing is lost: the text stays in the file. {readers ? "The ⚠ in the top bar counts the den's problems, and the Author's view lists them." : 'The ⚠ in the top bar lists every problem in the den.'}
      </p>
    </div>
  )
}

/**
 * The banner of the Reader's view for an entity the reader hasn't met at the
 * position. The Author's view is the way to see it from here (#300). This is the one
 * place in that view that names what's ahead, since the writer came here for this
 * note, so it's the one that asks for the whole story's positions (SC-WEB-13).
 */
export function NotInView({
  summary,
  note,
  universe,
  at,
  className = 'page-body',
}: {
  summary: EntitySummary | undefined
  note: string | null
  universe: string
  at: string | undefined
  className?: string
}) {
  const { setAt, setReaders } = usePosition()
  const positions = useQuery(positionsQuery(universe)).data?.positions ?? []
  const entry = summary ? entryOf(summary, positions) : null
  const entryOrdinal = entry === null ? -1 : ordinalOf(entry, positions)
  const here = at === undefined ? null : ordinalOf(at, positions)
  const later = summary !== undefined && entry !== null && here !== null && entryOrdinal !== null && entryOrdinal > here
  const where = entry === null ? '' : describePosition(entry, positions)

  return (
    <div className={className}>
      <div className="banner" role="status">
        {later ? (
          <p>
            Readers first meet <strong>{summary!.name}</strong> {where.startsWith('the start of') ? 'at' : 'in'} {where}.
          </p>
        ) : summary && reachesReaders(summary.status) ? (
          <p>
            <strong>{summary.name}</strong> isn't placed in the story yet, so readers don't meet it at any chapter.
          </p>
        ) : summary ? (
          <p>
            <strong>{summary.name}</strong> isn't in canon at this point in the story: its status is {summary.status}.
          </p>
        ) : (
          <p>{note ?? "The reader hasn't met this note by this point in the story."}</p>
        )}
        <p className="banner-actions">
          {later && entry && (
            <button type="button" onClick={() => setAt(entry)}>
              Go to {where}
            </button>
          )}
          <button type="button" onClick={() => setAt(undefined)}>
            Show everything
          </button>
          <button type="button" onClick={() => setReaders(false)}>
            Author's view
          </button>
        </p>
      </div>
    </div>
  )
}

/** True if a status is one readers can meet: the others keep their badge, and get no reader tags. */
function reachesReaders(status: string | null): boolean {
  return status === null || status === 'draft' || status === 'canon'
}

function EntityArticle({ universe, entity, positions, readersView = false }: { universe: string; entity: EntityPage; positions: PositionView[]; readersView?: boolean }) {
  // An idea has no reader tags, nor does anything in a note no position places, which has the one tag.
  const quiet = !reachesReaders(entity.status) || entity.readerMark?.kind === 'unplaced'
  const lead = stripTitle(entity.lead, entity.name)
  const editing = useEditing()
  const target = editing?.target ?? null
  // The fields' editors need the file's values as written and the type's fields, which only the source has.
  const source = useQuery({ ...sourceQuery(universe, editing?.entityKey ?? entity.key), enabled: editing !== null })
  // A link to a section (#section-...) arrives before the page has loaded,
  // so the router can't scroll to it; scroll once the section is here.
  // Back and Forward keep the scroll position the router restores.
  const router = useRouter()
  const hash = useRouterState({ select: (s) => s.location.hash })
  useEffect(() => {
    if (hash && !wasHistoryStep(router.history)) {
      document.getElementById(hash)?.scrollIntoView?.()
    }
  }, [hash, entity.key, router])
  return (
    <article className="page-body entity">
      <Breadcrumb universe={universe} scope={entity.scope} />
      <EntityTabs universe={universe} entityKey={entity.key} current="note" />
      <header>
        <p className="eyebrow">
          {entity.type} <StatusBadge status={entity.status} />
          {entity.readerMark && <MarkTag mark={entity.readerMark} positions={positions} note />}
        </p>
        <h1>{entity.name}</h1>
        <p className="entry">{entryWords(entity, positions)}</p>
        {entity.aliases && entity.aliases.length > 0 && <p className="aliases">Also: {entity.aliases.join(', ')}</p>}
        <p className="edit-bar">
          <EditButton target={{ kind: 'file' }} label="Edit file: the note's whole source, frontmatter included">
            Edit file
          </EditButton>
        </p>
        <FrozenNotice />
        {readersView && (
          <p className="muted">The Reader's view is read-only. Switch to the Author's view to edit.</p>
        )}
      </header>
      <EditingStatus />
      {target?.kind === 'file' ? (
        <PieceEditor target={target} what="the whole file" file={entity.path} />
      ) : (
        <>
          {target?.kind === 'lead' ? (
            <PieceEditor target={target} what="the opening" file={entity.path} />
          ) : (
            <div className="lead">
              {lead && <Markdown text={lead} links={entity.leadLinks} universe={universe} file={entity.path} readers={readersView} />}
              <p className="piece-tools">
                <EditButton target={{ kind: 'lead' }} label="Edit the opening, the text before the sections">
                  {lead ? 'Edit opening' : 'Write an opening'}
                </EditButton>
              </p>
            </div>
          )}
          {(entity.fields.length > 0 || editing !== null) && (
            <Fields universe={universe} fields={entity.fields} positions={positions} target={target} source={editing ? source.data : undefined} quiet={quiet} readers={readersView} />
          )}
          {entity.sections.map((s, i) => (
            <Section
              key={s.line}
              universe={universe}
              section={s}
              subsections={subsectionCount(entity.sections, i)}
              positions={positions}
              file={entity.path}
              target={target}
              quiet={quiet}
              readers={readersView}
            />
          ))}
          {target?.kind === 'add' ? (
            <AddSection file={entity.path} />
          ) : (
            <p className="piece-tools">
              <EditButton target={{ kind: 'add' }} label="Add a section">
                Add section
              </EditButton>
            </p>
          )}
        </>
      )}
      <Backlinks universe={universe} backlinks={entity.backlinks} positions={positions} />
    </article>
  )
}

/** How many subsections the section at `index` holds: the sections after it, up to the next of its level or higher. */
function subsectionCount(sections: PageSection[], index: number): number {
  let n = 0
  for (let i = index + 1; i < sections.length && sections[i].level > sections[index].level; i++) {
    n++
  }

  return n
}

/** The lead without a first `# Name` heading, which the page shows already. */
export function stripTitle(lead: string | null, name: string): string {
  if (!lead) {
    return ''
  }

  const lines = lead.split('\n')
  if (lines[0].trim() === `# ${name}`) {
    lines.shift()
  }

  return lines.join('\n').trim()
}

export function Breadcrumb({ universe, scope }: { universe: string; scope: ContainerRef[] }) {
  return (
    <nav className="breadcrumb" aria-label="Where this is filed">
      {scope.map((c, i) => (
        <Fragment key={`${c.kind}:${c.key}`}>
          {i > 0 && ' › '}
          <ContainerLink universe={universe} container={c} />
        </Fragment>
      ))}
    </nav>
  )
}

function ContainerLink({ universe, container }: { universe: string; container: ContainerRef }) {
  switch (container.kind) {
    case 'series':
      return (
        <Link to="/u/$universe/s/$series" params={{ universe, series: container.key }}>
          {container.name}
        </Link>
      )
    case 'book':
      return (
        <Link to="/u/$universe/b/$book" params={{ universe, book: container.key }}>
          {container.name}
        </Link>
      )
    default:
      return (
        <Link to="/u/$universe" params={{ universe }}>
          {container.name}
        </Link>
      )
  }
}

/** "Enters at Dracula chapter 5", or "From the start of Dracula", or "From the start of the story". */
export function entryWords(entity: Pick<EntityPage, 'entry' | 'scope' | 'scopeKind'>, positions: PositionView[]): string {
  const { entry } = entity
  if (!entry.inherited && entry.position) {
    return `Enters at ${describePosition(entry.position, positions)}`
  }

  const scope = entity.scope[entity.scope.length - 1]
  return entity.scopeKind === 'universe' || !scope ? 'From the start of the story' : `From the start of ${scope.name}`
}

/** "Dracula ch. 18", or "ch. 18" when the universe has one book, for a field entry's span. */
function shortPosition(position: string, positions: PositionView[]): string {
  const p = positions.find((x) => x.position === position)
  if (!p || p.chapter === null) {
    return describePosition(position, positions)
  }

  const oneBook = positions.every((x) => x.book === p.book)
  return `${oneBook ? '' : `${p.bookName} `}${p.number !== null ? `ch. ${p.number}` : chapterWords(p)}`
}

/**
 * Where a fact starts and stops holding, and what readers at the position don't
 * know of it: its mark, or else an `until` they haven't reached, which has no
 * mark (design.md §9). So nothing has two tags. Something `quiet` has no tag for
 * its `until`, as it keeps its own badge (#300).
 */
function Span({ from, until, positions, mark, quiet = false }: { from: string | null; until: string | null; positions: PositionView[]; mark?: ReaderMark; quiet?: boolean }) {
  const { at } = usePosition()
  const text = from && until ? `${shortPosition(from, positions)} to ${shortPosition(until, positions)}` : from ? `from ${shortPosition(from, positions)}` : until ? `until ${shortPosition(until, positions)}` : null
  return (
    <>
      {text && <span className="badge span">{text}</span>}
      {mark ? <MarkTag mark={mark} positions={positions} /> : !quiet && until && isAheadOf(until, at, positions) && <AheadOfReaders />}
    </>
  )
}

function Fields({
  universe,
  fields,
  positions,
  target,
  source,
  quiet,
  readers,
}: {
  universe: string
  fields: PageField[]
  positions: PositionView[]
  target: EditTarget | null
  /** The note's source, when it can be edited: which fields have their own editor, and which the type has. */
  source: EntitySource | undefined
  /** The note keeps its own badge, or its own tag, so its facts have no reader tags. */
  quiet: boolean
  /** The Reader's view: every link in a value shows only what it shows. */
  readers: boolean
}) {
  // A field's entries are listed one after another; show them under one name.
  const groups: PageField[][] = []
  for (const f of fields) {
    const last = groups[groups.length - 1]
    if (last && last[0].name === f.name) {
      last.push(f)
    } else {
      groups.push([f])
    }
  }

  const editingField = target?.kind === 'field' ? target.name : null
  const unset = source?.fieldTypes.some((t) => source.fields.find((f) => f.name === t.name)?.value == null) ?? false
  return (
    <>
      {groups.length > 0 && (
        <dl className="fields">
          {groups.map((entries) => (
            <div key={`${entries[0].name}:${entries[0].line}`} className="field">
              <dt>{entries[0].name}</dt>
              {editingField === entries[0].name ? (
                <dd className="field-editing">
                  <FieldEditor name={editingField} positions={positions} />
                </dd>
              ) : (
                <>
                  <FieldEntries universe={universe} entries={entries} positions={positions} quiet={quiet} readers={readers} />
                  {source && (
                    <dd className="field-tools">
                      <FieldTools name={entries[0].name} source={source} />
                    </dd>
                  )}
                </>
              )}
            </div>
          ))}
        </dl>
      )}
      {editingField !== null && !groups.some((g) => g[0].name === editingField) && (
        <div className="field-new">
          <FieldEditor name={editingField} positions={positions} />
        </div>
      )}
      {target?.kind === 'add-field' ? (
        <AddField source={source} />
      ) : (
        source &&
        unset && (
          <p className="piece-tools">
            <EditButton target={{ kind: 'add-field' }} label="Add a field">
              Add field
            </EditButton>
          </p>
        )
      )}
    </>
  )
}

function FieldEntries({ universe, entries, positions, quiet, readers }: { universe: string; entries: PageField[]; positions: PositionView[]; quiet: boolean; readers: boolean }) {
  // One value of a position field can be later than the position, or not place at all. A fact with its own mark needs no more.
  const valueTag = (f: PageField) => (value: string) =>
    quiet || f.readerMark ? null
    : f.aheadValues?.includes(value) ? <AheadOfReaders />
    : f.unplacedValues?.includes(value) ? <ReaderTag words={NOT_PLACED} why={notPlacedWhy(value)} />
    : null
  return (
    <>
      {entries.map((f, i) => (
        <dd key={`${f.line}:${f.entryIndex ?? i}`} className={f.live === false ? 'not-live' : undefined} title={f.live === false ? 'No view of the story shows this entry.' : undefined}>
          <FieldValue value={f.value} links={f.links} universe={universe} tagValue={valueTag(f)} readers={readers} />{' '}
          {f.kind === 'default' && <span className="badge">type default</span>}
          <Span from={f.from} until={f.until} positions={positions} mark={quiet ? undefined : f.readerMark} quiet={quiet || !reachesReaders(f.status)} />
          {f.pinned && <span className="badge">pinned</span>}
          <StatusBadge status={f.status} />
        </dd>
      ))}
    </>
  )
}

/**
 * A field's value: text with its links, a list, or a mapping (such as a date's text and sort key). `tagValue` can add a tag after a piece of text.
 * With `readers`, for the Reader's view, a mapping's key is read for links too, and nothing written as a labelled link shows more than its label.
 */
export function FieldValue({
  value,
  links,
  universe,
  tagValue,
  readers = false,
}: {
  value: unknown
  links: LinkView[]
  universe: string
  tagValue?: (text: string) => ReactNode
  readers?: boolean
}) {
  const match = useMemo(() => createLinkMatcher(links), [links])
  const render = (v: unknown): ReactNode => {
    if (v === null || v === undefined || v === '') {
      return <span className="muted">empty</span>
    }

    if (typeof v === 'string') {
      return (
        <>
          <LinkedText text={v} match={match} universe={universe} readers={readers} />
          {tagValue?.(v)}
        </>
      )
    }

    if (Array.isArray(v)) {
      return (
        <ul className="inline-list">
          {v.map((item, i) => (
            <li key={i}>{render(item)}</li>
          ))}
        </ul>
      )
    }

    if (typeof v === 'object') {
      const o = v as Record<string, unknown>
      if (typeof o.text === 'string') {
        return render(o.text)
      }

      return (
        <dl className="mapping">
          {Object.entries(o).map(([k, item]) => (
            <div key={k}>
              <dt>{readers ? <LinkedText text={k} match={match} universe={universe} readers /> : k}</dt>
              <dd>{render(item)}</dd>
            </div>
          ))}
        </dl>
      )
    }

    return String(v)
  }

  return <>{render(value)}</>
}

function Section({
  universe,
  section,
  subsections,
  positions,
  file,
  target,
  quiet,
  readers,
}: {
  universe: string
  section: PageSection
  subsections: number
  positions: PositionView[]
  file: string
  target: EditTarget | null
  /** The note keeps its own badge, or its own tag, so its sections have no reader tags. */
  quiet: boolean
  /** The Reader's view: every link in the text shows only what it shows. */
  readers: boolean
}) {
  const Heading = `h${Math.min(Math.max(section.level, 2), 6)}` as 'h2'
  const here = target !== null && 'line' in target && target.line === section.line && target.name === section.name ? target : null
  const { line, name } = section
  return (
    <section className={`section level-${section.level}${section.bookkeeping ? ' bookkeeping' : ''}${section.leftOut ? ' left-out' : ''}`}>
      {here?.kind === 'rename' ? (
        <RenameSection target={here} />
      ) : (
        <Heading id={sectionAnchor(section.name)}>
          {section.name} <StatusBadge status={section.status} />
          <Span
            from={section.from}
            until={section.until}
            positions={positions}
            mark={quiet ? undefined : section.readerMark}
            quiet={quiet || !reachesReaders(section.status) || Boolean(section.bookkeeping) || Boolean(section.leftOut)}
          />
          {section.pinned && <span className="badge">pinned</span>}
          {section.bookkeeping && <span className="badge">bookkeeping</span>}
          {section.leftOut && <span className="badge problem">left out</span>}
        </Heading>
      )}
      {section.leftOut && <LeftOutFlag problems={section.leftOut} hint="The text is safe in the file. Change the comment with Edit file." />}
      {here?.kind !== 'rename' && (
        <p className="piece-tools section-tools">
          <EditButton target={{ kind: 'section', line, name }} label={`Edit “${name}”`}>
            Edit
          </EditButton>
          <EditButton target={{ kind: 'rename', line, name }} label={`Rename “${name}”`}>
            Rename
          </EditButton>
          <EditButton target={{ kind: 'remove', line, name }} label={`Remove “${name}”`}>
            Remove
          </EditButton>
        </p>
      )}
      {here?.kind === 'remove' && <RemoveSection target={here} subsections={subsections} />}
      {here?.kind === 'section' ? (
        <PieceEditor target={here} what={`the section “${name}”`} file={file} />
      ) : (
        section.body.trim() && <Markdown text={section.body} links={section.links} universe={universe} file={file} readers={readers} />
      )}
    </section>
  )
}

/** Where a backlink is written, in words. */
export function backlinkPlace(b: BacklinkView, positions: PositionView[]): string {
  switch (b.site) {
    case 'entity_lead':
      return 'in its opening'
    case 'entity_section':
      return `in “${b.section}”`
    case 'entity_field':
    case 'entity_field_entry':
      return `in ${b.field}`
    case 'chapter_field':
      return `${b.field} of ${b.chapter ? describePosition(b.chapter, positions) : 'a chapter'}`
    case 'chapter_default':
      return `chapter default ${b.field}`
    case 'outline_row':
      return `outline row ${b.outlineRow}${b.chapter ? `, ${describePosition(b.chapter, positions)}` : ''}`
    case 'outline_notes':
      return 'in the outline notes'
    case 'container_body':
      return 'in its notes'
    default:
      return b.site
  }
}

/** "Linked from": the links that reach this entity, grouped by the note or book they're in. */
function Backlinks({ universe, backlinks, positions }: { universe: string; backlinks: BacklinkView[]; positions: PositionView[] }) {
  const groups = new Map<string, BacklinkView[]>()
  for (const b of backlinks) {
    const key = b.source ? `e:${b.source.key}` : `c:${b.container?.kind}:${b.container?.key}:${b.path}`
    groups.set(key, [...(groups.get(key) ?? []), b])
  }

  return (
    <section className="backlinks">
      <h2>Linked from</h2>
      {backlinks.some((b) => b.readerMark) && <p className="muted">{MARKS_LEGEND}</p>}
      {groups.size === 0 ? (
        <p className="muted">Nothing links here{backlinks.length === 0 ? ' yet' : ''}.</p>
      ) : (
        <ul>
          {[...groups.values()].map((items) => {
            const first = items[0]
            return (
              <li key={`${first.path}:${first.line}`}>
                {first.source ? (
                  <>
                    <EntityLink universe={universe} entityKey={first.source.key}>
                      {first.source.name}
                    </EntityLink>{' '}
                    <span className="muted">{first.source.type}</span>
                  </>
                ) : first.container ? (
                  <>
                    <ContainerLink universe={universe} container={first.container} />
                    {first.site.startsWith('outline_') && <span className="muted"> outline</span>}
                  </>
                ) : (
                  first.path
                )}
                <ul className="places">
                  {items.map((b) => (
                    <li key={`${b.line}:${b.column}`}>
                      {backlinkPlace(b, positions)}
                      {b.bookkeeping && <span className="badge">bookkeeping</span>} <StatusBadge status={b.status} />
                      {b.leftOut && <span className="badge problem">left out</span>}
                      {b.readerMark && <MarkList mark={b.readerMark} positions={positions} />}
                    </li>
                  ))}
                </ul>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

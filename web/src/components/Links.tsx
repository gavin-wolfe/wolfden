import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import type { LinkView, ReaderMark } from '../api'
import { usePositionsInView } from '../hooks'
import { LaterNameTag } from './AheadOfReaders'

/** A link to an entity's page, by its key (its id, or its path while it has none). */
export function EntityLink({ universe, entityKey, className, children }: { universe: string; entityKey: string; className?: string; children: ReactNode }) {
  return (
    <Link to="/u/$universe/e/$" params={{ universe, _splat: entityKey }} className={className}>
      {children}
    </Link>
  )
}

/** "character" → "Characters", for a group of entities of one type. "species" is its own plural. */
export function typePlural(type: string): string {
  const word = type.replace(/[-_]/g, ' ')
  const plural = /species$/.test(word) ? word : /(s|x|z|ch|sh)$/.test(word) ? `${word}es` : /[^aeiou]y$/.test(word) ? `${word.slice(0, -1)}ies` : `${word}s`
  return plural.charAt(0).toUpperCase() + plural.slice(1)
}

/** The anchor of a section on an entity page. */
export function sectionAnchor(name: string): string {
  return `section-${name.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '')}`
}

/** A `[[...]]` link as written in markdown or a field value. */
export const WIKI_LINK = /\[\[[^[\]\n]+?\]\]/g

/**
 * What reads as a labelled link with a line break in it, which isn't a link
 * (F-LINK-1): the server's `SplitLinkPattern` in `WikiLink.cs`. The label is group 1.
 */
const SPLIT_LINK = /\[\[(?:[^[\]|\r\n]*[\r\n][^[\]|]*\\?\||[^[\]|\r\n]*\\?\|(?=[^[\]\r\n]*[\r\n]))([^[\]]+)\]\]/g

/**
 * A link's text as compared with the API's: a `\|`, written so a table cell doesn't
 * split, is the same as `|` (F-OUT-12), and markdown has already read it as `|`.
 */
function linkKey(text: string): string {
  return text.replace(/\\\|/g, '|')
}

/**
 * Finds each `[[...]]` link's resolution in the API's `links`, by its text,
 * taking them in order, so two links with the same text get one each.
 */
export function createLinkMatcher(links: readonly LinkView[]): (text: string) => LinkView | undefined {
  const used = new Set<number>()
  return (written) => {
    const text = linkKey(written)
    let index = links.findIndex((l, i) => linkKey(l.text) === text && !used.has(i))
    if (index < 0) {
      index = links.findIndex((l) => linkKey(l.text) === text)
    }

    if (index < 0) {
      return undefined
    }

    used.add(index)
    return links[index]
  }
}

/**
 * What a link shows: its label, else the entity it reaches, else its target as written.
 * A link by a `known_as` name readers know (`byKnownName`, #67) shows that name as written,
 * so text after the reveal that says `[[Corin Hask]]` doesn't go back to the note's name.
 */
export function linkText(link: LinkView): string {
  const name = link.label ?? (link.byKnownName && link.entity ? link.target.trim() : link.entity?.name) ?? link.target
  return link.heading && !link.label ? `${name}: ${link.heading}` : name
}

/**
 * A `[[...]]` link, as the index resolved it: a link to the entity it
 * reaches; a broken link, marked so, with why; an ambiguous one, with the
 * entities it matches in its tooltip; or, as of a position, plain text for a
 * link to an entity the reader hasn't met yet, which doesn't say what it
 * reaches. `children`, for the Reader's view of a note's text, is its words as markdown read them.
 */
export function WikiLink({ link, universe, children }: { link: LinkView; universe: string; children?: ReactNode }) {
  // In the Author's view, a link written by a name readers don't know yet is shown as written, with a tag after it (#67).
  if (link.nameMark) {
    return (
      <>
        <WikiLinkOnly link={link} universe={universe}>
          {children}
        </WikiLinkOnly>{' '}
        <LinkNameMark mark={link.nameMark} universe={universe} />
      </>
    )
  }

  return (
    <WikiLinkOnly link={link} universe={universe}>
      {children}
    </WikiLinkOnly>
  )
}

/** The tag after a link written by a name readers at the position haven't learned. */
function LinkNameMark({ mark, universe }: { mark: ReaderMark; universe: string }) {
  const positions = usePositionsInView(universe).data?.positions ?? []
  return <LaterNameTag mark={mark} positions={positions} on="link" />
}

function WikiLinkOnly({ link, universe, children }: { link: LinkView; universe: string; children?: ReactNode }) {
  const text = children ?? linkText(link)
  if (link.outcome === 'resolved' && link.entity) {
    return (
      <Link
        to="/u/$universe/e/$"
        params={{ universe, _splat: link.entity.key }}
        hash={link.section ? sectionAnchor(link.section) : undefined}
        className="wikilink"
      >
        {text}
      </Link>
    )
  }

  if (link.outcome === 'resolved') {
    return (
      <span className="wikilink wikilink-later" title="The reader hasn't met this yet at this point in the story.">
        {text}
      </span>
    )
  }

  if (link.outcome === 'ambiguous') {
    const matches = link.matches ?? []
    const title = matches.length
      ? `Ambiguous link: it matches ${matches.map((m) => `${m.name} (${m.type})`).join(', ')}.`
      : `Ambiguous link${link.problem ? `: ${link.problem}` : '.'}`
    return (
      <span className="wikilink wikilink-ambiguous" title={title} data-outcome="ambiguous">
        {text}
      </span>
    )
  }

  return (
    <span className="wikilink wikilink-broken" title={`Broken link: ${link.problem ?? `nothing is called '${link.target}'.`}`} data-outcome="broken">
      {text}
    </span>
  )
}

/** The label of what's written as a labelled link, for one the API has no record of, or null. The server's `WithLinksShown` does the same. */
export function labelOf(written: string): string | null {
  const bar = written.indexOf('|')
  return bar < 0 ? null : written.slice(bar + 1, -2).trim() || null
}

/** The server's `LinkPattern` in `WikiLink.cs`: what's a link, whatever's in its label. The target is group 1. */
const SOURCE_LINK = /\[\[((?:[^[\]|#\r\n\\]|\\(?!\|))*)(?:#(?:[^[\]|#\r\n\\]|\\(?!\|))+)?(?:\\?\|[^[\]\r\n]+)?\]\]/g

/** A range of a note's source, by offset. */
export interface SourceRange {
  start: number
  end: number
}

const within = (ranges: readonly SourceRange[], at: number) => ranges.some((r) => r.start <= at && at < r.end)

function escapedAt(text: string, index: number): boolean {
  let count = 0
  for (let i = index - 1; i >= 0 && text[i] === '\\'; i--) {
    count++
  }

  return count % 2 === 1
}

/** A name as markdown source, so it reads as written. A label is left as it is, since its formatting is the writer's. */
function asMarkdown(words: string): string {
  return words.replace(/[\\`*_[\]<&]/g, '\\$&')
}

/**
 * A note's markdown source as the Reader's view reads it (#300), with each link replaced
 * before markdown parses it, so formatting in a label can't split it from its target.
 * It's the server's `WithLinksShown`: a link in code stays (`code`), as does a `\[[` or a
 * `![[`, and so does an unlabelled one with no record. `link` gives what becomes of one with a
 * record, in a place that can hold a link. In `plain`, such as an image's alt text or the words
 * of a `[text](url)`, it's only the words it shows.
 */
export function readersSource(
  source: string,
  code: readonly SourceRange[],
  plain: readonly SourceRange[],
  match: (text: string) => LinkView | undefined,
  linkTo: (link: LinkView, words: string) => string,
): string {
  const replaced: { start: number; end: number; text: string }[] = []
  for (const m of source.matchAll(SOURCE_LINK)) {
    if (m[1].trim() === '' || escapedAt(source, m.index) || (source[m.index - 1] === '!' && !escapedAt(source, m.index - 1)) || within(code, m.index)) {
      continue
    }

    const link = match(m[0])
    const words = link ? (link.label ?? asMarkdown(linkText(link))) : labelOf(m[0])
    if (words !== null) {
      replaced.push({ start: m.index, end: m.index + m[0].length, text: link && !within(plain, m.index) ? linkTo(link, words) : words })
    }
  }

  for (const m of source.matchAll(SPLIT_LINK)) {
    if (!within(code, m.index)) {
      replaced.push({ start: m.index, end: m.index + m[0].length, text: m[1].trim() })
    }
  }

  let shown = ''
  let at = 0
  for (const r of replaced.sort((a, b) => a.start - b.start)) {
    shown += source.slice(at, r.start) + r.text
    at = r.end
  }

  return shown + source.slice(at)
}

/** What reads as a labelled link split over two lines, as its label: the Reader's view's rule for it. */
export function withoutSplitLinks(text: string): string {
  return text.replace(SPLIT_LINK, (_, label: string) => label.trim())
}

/**
 * Plain text with its `[[...]]` links rendered as links. With `readers`, for the
 * Reader's view, what's written as a labelled link but has no link record shows only its label.
 */
export function LinkedText({ text: written, match, universe, readers = false }: { text: string; match: (text: string) => LinkView | undefined; universe: string; readers?: boolean }) {
  const text = readers ? withoutSplitLinks(written) : written
  const parts: ReactNode[] = []
  let last = 0
  for (const m of text.matchAll(WIKI_LINK)) {
    const link = match(m[0])
    const label = !link && readers ? labelOf(m[0]) : null
    if (!link && label === null) {
      continue
    }

    parts.push(text.slice(last, m.index))
    parts.push(link ? <WikiLink key={m.index} link={link} universe={universe} /> : label)
    last = m.index + m[0].length
  }

  parts.push(text.slice(last))
  return <>{parts}</>
}

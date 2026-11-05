import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import type { LinkView } from '../api'

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

/** What a link shows: its label, else the entity it reaches, else its target as written. */
export function linkText(link: LinkView): string {
  const name = link.label ?? link.entity?.name ?? link.target
  return link.heading && !link.label ? `${name}: ${link.heading}` : name
}

/**
 * A `[[...]]` link, as the index resolved it: a link to the entity it
 * reaches; a broken link, marked so, with why; an ambiguous one, with the
 * entities it matches in its tooltip; or, as of a position, plain text for a
 * link to an entity the reader hasn't met yet, which doesn't say what it
 * reaches.
 */
export function WikiLink({ link, universe }: { link: LinkView; universe: string }) {
  const text = linkText(link)
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

/** Text as the Reader's view has it where it can't hold a link, such as an image's alt text: each link is the words it shows. */
export function linkWords(text: string, match: (text: string) => LinkView | undefined): string {
  return text.replace(SPLIT_LINK, (_, label: string) => label.trim()).replace(WIKI_LINK, (written) => {
    const link = match(written)
    return link ? linkText(link) : (labelOf(written) ?? written)
  })
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

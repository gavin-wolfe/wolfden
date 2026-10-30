// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { EditorState } from '@codemirror/state'
import { CompletionContext, type Completion } from '@codemirror/autocomplete'
import { noteLinks } from './components/MarkdownEditor'
import type { LookupHit, LookupResult } from './lookupApi'
import { entities, stubApi } from './test/harness'

// `[[` autocomplete in the section editor (#198): notes by name or alias from
// the lookup, linked as the field pickers link them.

afterEach(() => {
  vi.unstubAllGlobals()
})

const mina = entities.find((e) => e.key === 'E-MINA')!
const jonathan = entities.find((e) => e.key === 'E-JONATHAN')!

function stubLookup(matches: LookupHit[]) {
  return stubApi({
    override: (path, params) =>
      path === '/api/entities/lookup'
        ? ({ version: 1, universe: 'dracula', query: params.get('query')!, position: null, count: matches.length, truncated: false, matches } satisfies LookupResult)
        : undefined,
  })
}

/** Completes `doc` at its `|`, or at its end. */
async function complete(doc: string, others: { path: string; name?: string }[] | null = entities) {
  const pos = doc.includes('|') ? doc.indexOf('|') : doc.length
  const state = EditorState.create({ doc: doc.replace('|', '') })
  return noteLinks('dracula', () => others)(new CompletionContext(state, pos, false))
}

const apply = (o: Completion) => o.apply as string

describe('SC-WEB-78: [[ autocomplete looks notes up by name and alias', () => {
  it('links a pick by the name or alias it matched, so the prose reads as written', async () => {
    const { calls } = stubLookup([
      { entity: mina, matchedBy: 'alias', alias: 'Madam Mina', linkText: 'Madam Mina' },
      { entity: jonathan, matchedBy: 'name', alias: null, linkText: 'Jonathan Harker' },
    ])

    const result = await complete('She met [[madam')
    expect(result?.from).toBe('She met [['.length)
    expect(result?.options.map((o) => [o.label, o.detail, apply(o)])).toEqual([
      ['Mina Harker', 'as Madam Mina · character', 'Madam Mina]]'],
      ['Jonathan Harker', 'character', 'Jonathan Harker]]'],
    ])
    // Whatever the position: the lookup never sends one from an editor (#28).
    expect(calls).toEqual(['/api/entities/lookup?query=madam&universe=dracula&limit=20'])
  })

  it('falls back to the slug, or the id, where the text matched would reach another note too', async () => {
    // "Harker" is an alias of Jonathan and of Quincey Harker: the lookup says it doesn't reach Jonathan alone.
    stubLookup([{ entity: jonathan, matchedBy: 'alias', alias: 'Harker', linkText: null }])

    expect((await complete('[[harker'))?.options.map(apply)).toEqual(['jonathan-harker]]'])
    // A link already closed isn't closed again.
    expect((await complete('[[har|]] went'))?.options.map(apply)).toEqual(['jonathan-harker'])
    // Without the universe's notes, a slug might be shadowed: the id.
    expect((await complete('[[harker', null))?.options.map(apply)).toEqual(['E-JONATHAN]]'])
  })

  it('writes no link for a note no link is sure to reach, and says to add an id', async () => {
    // No id, and another note has the same file name: [[its name]] could reach that one (review of #239).
    const twin = { ...jonathan, key: 'x/renfield.md', id: null, name: 'Renfield', path: 'x/renfield.md' }
    stubLookup([{ entity: twin, matchedBy: 'name', alias: null, linkText: null }])

    const option = (await complete('[[renf', [...entities, { path: 'y/Renfield.md', name: 'R. M. Renfield' }]))!.options[0]
    expect(option.detail).toBe('character · no sure link')
    expect(option.info).toContain('Add an id with Edit file')
    expect(typeof option.apply).toBe('function')
  })

  it('asks nothing until a letter or digit is typed after [[', async () => {
    const { calls } = stubLookup([])
    expect(await complete('[[')).toBeNull()
    expect(await complete('[[mina-')).toBeNull()
    expect(await complete('mina')).toBeNull()
    expect(calls).toEqual([])
  })
})

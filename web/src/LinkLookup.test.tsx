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
  it('links a pick by its slug, and keeps an alias it was found by as the label', async () => {
    const { calls } = stubLookup([
      { entity: mina, matchedBy: 'alias', alias: 'Madam Mina' },
      { entity: jonathan, matchedBy: 'name', alias: null },
    ])

    const result = await complete('She met [[madam')
    expect(result?.from).toBe('She met [['.length)
    expect(result?.options.map((o) => [o.label, o.detail, apply(o)])).toEqual([
      ['Mina Harker', 'as Madam Mina · character', 'mina-harker|Madam Mina]]'],
      ['Jonathan Harker', 'character', 'jonathan-harker]]'],
    ])
    // Whatever the position: the lookup never sends one from an editor (#28).
    expect(calls).toEqual(['/api/entities/lookup?query=madam&universe=dracula&limit=20'])
  })

  it('doesn’t close a link that’s already closed, and links by id when the notes aren’t known', async () => {
    stubLookup([{ entity: jonathan, matchedBy: 'name', alias: null }])

    const closed = await complete('[[jon|]] went', entities)
    expect(closed?.options.map(apply)).toEqual(['jonathan-harker'])
    const unknown = await complete('[[jon', null)
    expect(unknown?.options.map(apply)).toEqual(['E-JONATHAN]]'])
  })

  it('asks nothing until a letter or digit is typed after [[', async () => {
    const { calls } = stubLookup([])
    expect(await complete('[[')).toBeNull()
    expect(await complete('[[mina-')).toBeNull()
    expect(await complete('mina')).toBeNull()
    expect(calls).toEqual([])
  })
})

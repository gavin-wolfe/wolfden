// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, within } from '@testing-library/react'
import type { EntityPageResult, PageField, PageSection } from './api'
import { entities, page, positions, renderApp, stubApi } from './test/harness'
import { isAheadOf } from './story'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const TOOLTIP = "Readers at this point in the story don't know this yet."

function section(until: string | null): PageSection {
  return { name: 'History', level: 2, headingPath: ['History'], status: 'canon', pinned: false, from: null, until, line: 10, body: 'What everyone says.', bookkeeping: false, links: [] }
}

function field(until: string | null): PageField {
  return { name: 'rank', value: 'Captain', kind: 'temporal', status: 'canon', pinned: false, from: null, until, line: 5, entryIndex: 0, live: true, links: [] }
}

async function openAt(at: string | null, extra: Parameters<typeof page>[1]) {
  stubApi({
    entityPage: (key): EntityPageResult => ({
      version: 1,
      view: at === null ? 'full' : 'canon_as_of',
      position: at,
      inView: true,
      note: null,
      entity: page(entities.find((e) => e.key === key)!, extra),
    }),
  })
  renderApp(`/u/dracula/e/E-JONATHAN${at === null ? '' : `?at=${at}`}`)
  const main = await screen.findByRole('main')
  await within(main).findByRole('heading', { name: 'Jonathan Harker' })
  return main
}

describe('SC-WEB-97: a date ahead of the position is kept and marked', () => {
  it('tags a section’s until later than the position, in words, with the tooltip', async () => {
    const main = await openAt('dracula:3', { sections: [section('dracula:5')] })

    const heading = await within(main).findByRole('heading', { name: /History/ })
    expect(heading.textContent).toContain('until ch. 5')
    const tag = within(heading).getByText('ahead of readers')
    expect(tag.getAttribute('title')).toBe(TOOLTIP)
  })

  it('tags a field entry’s until later than the position', async () => {
    const main = await openAt('dracula:3', { fields: [field('dracula:5')] })

    const value = (await within(main).findByText(/Captain/)).closest('dd')!
    expect(value.textContent).toContain('until ch. 5')
    expect(within(value).getByText('ahead of readers').getAttribute('title')).toBe(TOOLTIP)
  })

  it('has no tag with no position', async () => {
    const main = await openAt(null, { sections: [section('dracula:5')] })

    expect((await within(main).findByRole('heading', { name: /History/ })).textContent).toContain('until ch. 5')
    expect(within(main).queryByText('ahead of readers')).toBeNull()
  })

  it.each(['dracula:5', 'dracula:7'])('has no tag at %s, which has reached an until of chapter 5', async (at) => {
    const main = await openAt(at, { sections: [section('dracula:5')] })

    expect((await within(main).findByRole('heading', { name: /History/ })).textContent).toContain('until ch. 5')
    expect(within(main).queryByText('ahead of readers')).toBeNull()
  })

  it('has no tag for a date that isn’t a position the universe has, or on a fact with no until', async () => {
    const main = await openAt('dracula:3', { sections: [section('dracula:999')], fields: [field(null)] })

    await within(main).findByRole('heading', { name: /History/ })
    expect(within(main).queryByText('ahead of readers')).toBeNull()
  })
})

describe('isAheadOf', () => {
  it('compares by story order, counting a bare book as the start of the book', () => {
    expect(isAheadOf('dracula:2', 'dracula:1', positions)).toBe(true)
    expect(isAheadOf('dracula:2', 'dracula:2', positions)).toBe(false)
    expect(isAheadOf('dracula:1', 'dracula:2', positions)).toBe(false)
    expect(isAheadOf('dracula:1', 'dracula', positions)).toBe(true)
    expect(isAheadOf('dracula', 'dracula:1', positions)).toBe(false)
  })

  it('is false with no position, or one the list doesn’t have', () => {
    expect(isAheadOf('dracula:2', undefined, positions)).toBe(false)
    expect(isAheadOf('nowhere:2', 'dracula:1', positions)).toBe(false)
    expect(isAheadOf('dracula:2', 'nowhere:1', positions)).toBe(false)
  })
})

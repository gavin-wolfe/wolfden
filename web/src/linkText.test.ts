import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import type { LinkView } from './api'
import { linkText } from './components/Links'

interface Case {
  about: string
  label: string | null
  name: string | null
  target: string
  heading: string | null
  /** A link by a `known_as` name readers know there (#67): the API's `byKnownName`. */
  asWritten?: boolean
  shown: string
}

// The same cases test the server's copy of this rule (ReadersLinks.Shown), which a Reader's search goes by.
const cases = JSON.parse(readFileSync(new URL('../../tests/shared/link-text-cases.json', import.meta.url), 'utf8')) as Case[]

describe('SC-WEB-106, SC-WEB-113: what a link shows', () => {
  it.each(cases)('$about', ({ label, name, target, heading, asWritten, shown }) => {
    const link = { label, target, heading, entity: name === null ? null : { name }, byKnownName: asWritten } as unknown as LinkView
    expect(linkText(link)).toBe(shown)
  })
})

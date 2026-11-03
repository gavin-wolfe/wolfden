import { describe, expect, it } from 'vitest'
import { typePlural } from './components/Links'

describe('SC-WEB-82: a list of notes of one type is headed by the type, in the plural', () => {
  it.each([
    ['character', 'Characters'],
    ['star-system', 'Star systems'],
    ['world', 'Worlds'],
    ['settlement', 'Settlements'],
    ['species', 'Species'],
    ['polity', 'Polities'],
    ['ship-class', 'Ship classes'],
    ['ship', 'Ships'],
    ['technology', 'Technologies'],
    ['culture', 'Cultures'],
    ['realm', 'Realms'],
    ['magic-system', 'Magic systems'],
    ['creature', 'Creatures'],
    ['deity', 'Deities'],
    ['artifact', 'Artifacts'],
    ['order', 'Orders'],
  ])('%s is headed %s', (type, heading) => {
    expect(typePlural(type)).toBe(heading)
  })
})

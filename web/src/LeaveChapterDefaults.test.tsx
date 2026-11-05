// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { atOf, renderApp, stubApi } from './test/harness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const readersIn = (history: { location: { search: string } }) => new URLSearchParams(history.location.search).get('readers')

/** Opens the `tense` field's form, and changes it. */
async function editADefault(section: HTMLElement | null) {
  const scope = section ?? (await screen.findByRole('main'))
  fireEvent.click(await within(scope).findByRole('button', { name: 'Edit tense' }))
  const form = await within(scope).findByRole('form', { name: 'Edit tense' })
  fireEvent.change(within(form).getByLabelText('Value'), { target: { value: 'future' } })
  return form
}

const prompt = () => screen.findByRole('alertdialog')

describe('SC-WEB-103: an open chapter default asks before it’s lost', () => {
  it('on a universe’s page, switching to the Reader’s view asks first, and keeping it stays', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula?at=dracula:3')
    const main = await screen.findByRole('main')
    const heading = await within(main).findByRole('heading', { name: 'Chapter defaults' })
    const form = await editADefault(heading.closest('section'))

    fireEvent.click(await screen.findByRole('button', { name: "Reader's view" }))

    const asked = await prompt()
    expect(asked.textContent).toContain("You have changes that aren't saved")
    expect(readersIn(history)).toBeNull()

    fireEvent.click(within(asked).getByRole('button', { name: 'Keep editing' }))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect(readersIn(history)).toBeNull()
    expect(form.isConnected).toBe(true)
  })

  it('leaves when told to', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula?at=dracula:3')
    const main = await screen.findByRole('main')
    await editADefault((await within(main).findByRole('heading', { name: 'Chapter defaults' })).closest('section'))

    fireEvent.click(await screen.findByRole('button', { name: "Reader's view" }))
    fireEvent.click(within(await prompt()).getByRole('button', { name: 'Leave without saving' }))

    await waitFor(() => expect(readersIn(history)).toBe('1'))
    await waitFor(() => expect(within(screen.getByRole('main')).queryByRole('heading', { name: 'Chapter defaults' })).toBeNull())
  })

  it('moving the position asks first too', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula?at=dracula:3')
    const main = await screen.findByRole('main')
    await editADefault((await within(main).findByRole('heading', { name: 'Chapter defaults' })).closest('section'))

    fireEvent.keyDown(screen.getByRole('slider', { name: 'Story position' }), { key: 'ArrowRight' })

    await prompt()
    expect(atOf(history)).toBe('dracula:3')
  })

  it('on a book’s page, the form stays through a view it’s in too, and a new position asks', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula/b/dracula?at=dracula:3')
    const main = await screen.findByRole('main')
    const form = await editADefault((await within(main).findByRole('heading', { name: 'Chapter defaults' })).closest('section'))

    fireEvent.click(await screen.findByRole('button', { name: "Reader's view" }))
    await waitFor(() => expect(readersIn(history)).toBe('1'))
    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(form.isConnected).toBe(true)

    fireEvent.keyDown(screen.getByRole('slider', { name: 'Story position' }), { key: 'ArrowRight' })
    await prompt()
    expect(atOf(history)).toBe('dracula:3')
  })

  it('following a link asks first too', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula?at=dracula:3')
    const main = await screen.findByRole('main')
    await editADefault((await within(main).findByRole('heading', { name: 'Chapter defaults' })).closest('section'))

    fireEvent.click(within(screen.getByRole('navigation', { name: 'Contents' })).getByRole('link', { name: 'Mina Harker' }))

    await prompt()
    expect(history.location.pathname).toBe('/u/dracula')
  })

  it('doesn’t ask with nothing changed', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula?at=dracula:3')
    const main = await screen.findByRole('main')
    const section = (await within(main).findByRole('heading', { name: 'Chapter defaults' })).closest('section')!
    fireEvent.click(await within(section).findByRole('button', { name: 'Edit tense' }))
    await within(section).findByRole('form', { name: 'Edit tense' })

    fireEvent.click(await screen.findByRole('button', { name: "Reader's view" }))

    await waitFor(() => expect(readersIn(history)).toBe('1'))
    expect(screen.queryByRole('alertdialog')).toBeNull()
  })

  it('on a chapter’s page, the form stays through a new position or view, and only another page asks', async () => {
    stubApi()
    const { history } = renderApp('/u/dracula/b/dracula/c/5?at=dracula:3')
    const form = await editADefault(null)

    fireEvent.click(await screen.findByRole('button', { name: "Reader's view" }))
    await waitFor(() => expect(readersIn(history)).toBe('1'))
    fireEvent.keyDown(screen.getByRole('slider', { name: 'Story position' }), { key: 'ArrowRight' })
    await waitFor(() => expect(atOf(history)).toBe('dracula:4'))
    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(form.isConnected).toBe(true)

    fireEvent.click(within(screen.getByRole('main')).getByRole('link', { name: /Note ›/ }))
    await prompt()
    expect(history.location.pathname).toBe('/u/dracula/b/dracula/c/5')
  })
})

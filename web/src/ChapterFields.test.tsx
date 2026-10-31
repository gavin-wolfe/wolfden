// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { BookChapterFields, ChapterDefaultsSource } from './chapterFieldsApi'
import type { WriteResult } from './editApi'
import { chapterFieldChanges, fieldDraftOf, type FieldDraft } from './fieldModel'
import { bookChapterFields, chapterDefaults, chapterFieldTypes, renderApp, stubApi, unsetValue } from './test/harness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const BOOK = 'universes/dracula/books/dracula/book.md'

function result(extra: Partial<WriteResult> = {}): WriteResult {
  return {
    version: 1,
    changed: true,
    path: BOOK,
    hash: 'h-after',
    entity: null,
    effects: ['Chapter dracula:2 (Jonathan Harker’s Journal)’s tense is now present, its own value, in place of past from book ’dracula’ chapter_defaults.'],
    pinnedCanon: [],
    warnings: [],
    files: null,
    idAdded: null,
    ...extra,
  }
}

/** Answers the chapter field endpoints, recording each edit's path and body. */
function stubFields(options: {
  post?: (path: string, body: Record<string, unknown>) => Response | WriteResult
  book?: () => BookChapterFields
  defaults?: (kind: ChapterDefaultsSource['kind']) => ChapterDefaultsSource
} = {}) {
  const posted: { path: string; body: Record<string, unknown> }[] = []
  const api = stubApi({
    override: (path, params, init) => {
      if (path === '/api/chapter/edit' || path === '/api/chapter-defaults/edit' || path === '/api/chapter/create') {
        expect(init?.method).toBe('POST')
        expect(new Headers(init?.headers).get('Content-Type')).toBe('application/json')
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>
        posted.push({ path, body })
        return options.post?.(path, body) ?? result()
      }

      if (path === '/api/book/chapter-fields' && options.book) {
        return options.book()
      }

      if (path === '/api/chapter-defaults' && options.defaults) {
        return options.defaults(params.get('book') ? 'book' : params.get('series') ? 'series' : 'universe')
      }

      if (path === '/api/book/chapters') {
        const fields = bookChapterFields()
        return { version: 1, universe: 'dracula', book: 'dracula', path: BOOK, hash: 'h-book', final: false, chapters: fields.chapters.map((c) => ({ key: c.key, number: c.number, title: c.title })) }
      }

      return undefined
    },
  })
  return { ...api, posted }
}

/** The field's row on the page: its name, then what holds and its tools. */
function row(name: string, list?: HTMLElement): HTMLElement {
  const where = list ?? screen.getByRole('group', { name: /^Fields of / })
  const dt = within(where)
    .getAllByRole('term')
    .find((t) => t.textContent === name)!
  return dt.parentElement!
}

describe('SC-WEB-73: I see each chapter’s fields, and which are inherited', () => {
  it('the book page lists each chapter’s fields under its title, inherited ones marked', async () => {
    stubFields()
    renderApp('/u/dracula/b/dracula')
    await screen.findByRole('heading', { name: 'Dracula', level: 1 })

    await waitFor(() => expect(document.querySelector('ol.chapters li .chapter-field-summary')).not.toBeNull())
    const first = document.querySelector<HTMLElement>('ol.chapters li')!
    const summary = first.querySelector<HTMLElement>('.chapter-field-summary')!
    // pov and when are the chapter's own; narration and tense come from the book's defaults.
    expect(within(summary).getByRole('link', { name: 'Jonathan Harker' }).getAttribute('href')).toBe('/u/dracula/e/E-JONATHAN')
    expect(summary.textContent).toContain('when3–5 May')
    expect(summary.textContent).toContain('narrationfirst book default')
    expect(summary.querySelectorAll('.inherited-value')).toHaveLength(2)

    // The book's own chapter defaults have their own section.
    const defaults = screen.getByRole('group', { name: "Chapter defaults of book 'dracula'" })
    expect(row('tense', defaults).textContent).toContain('past')
    expect(within(row('pov', defaults)).getByText('not set')).toBeTruthy()
  })

  it('the chapter page shows each field the den defines, where it comes from, and a field a den pack adds', async () => {
    const withWeather = () => {
      const b = bookChapterFields()
      const weather = { name: 'weather', type: 'string' as const, list: false, temporal: false, required: false, values: [], to: null, description: 'The weather.', default: null }
      return { ...b, fieldTypes: [...b.fieldTypes, weather], chapters: b.chapters.map((c) => ({ ...c, fields: [...c.fields, unsetValue('weather')] })) }
    }
    stubFields({ book: withWeather })
    renderApp('/u/dracula/b/dracula/c/2')
    await screen.findByRole('heading', { name: "Jonathan Harker's Journal", level: 1 })

    const list = await screen.findByRole('group', { name: 'Fields of this chapter' })
    expect(within(list).getAllByRole('term').map((t) => t.textContent)).toEqual(['pov', 'narration', 'tense', 'when', 'setting', 'weather'])
    expect(row('tense').textContent).toContain("past inherited from book 'dracula' defaults")
    expect(within(row('pov')).getByText('not set')).toBeTruthy()
    // Every field has Edit, but only a value set here can go back to the default.
    expect(within(row('weather')).getByRole('button', { name: 'Edit weather' })).toBeTruthy()
    expect(within(list).queryByRole('button', { name: /^Use the default/ })).toBeNull()
  })

  it('a value written but ignored is shown as written, with why, before Edit replaces it (#233)', async () => {
    const reason = `setting is a link to one entity, such as "[[whitby]]", in quotes, with nothing else in the string, not 'Whitby harbour'.`
    const withPlainSetting = () => {
      const b = bookChapterFields()
      const chapters = b.chapters.map((c) =>
        c.key !== '2' ? c : { ...c, fields: c.fields.map((f) => (f.name === 'setting' ? unsetValue('setting', { set: true, own: 'Whitby harbour', ignored: true, ignoredReason: reason }) : f)) },
      )
      return { ...b, chapters }
    }
    const { posted } = stubFields({ book: withPlainSetting })
    renderApp('/u/dracula/b/dracula/c/2')
    await screen.findByRole('heading', { name: "Jonathan Harker's Journal", level: 1 })

    await waitFor(() => expect(row('setting').textContent).toContain(`(written here as "Whitby harbour", which is ignored: ${reason})`))
    fireEvent.click(within(row('setting')).getByRole('button', { name: 'Edit setting' }))
    const form = screen.getByRole('form', { name: 'Edit setting' })
    const note = within(form).getByRole('note')
    expect(note.textContent).toBe(`Written here as "Whitby harbour", which is ignored: ${reason} Saving replaces it.`)
    expect(form.getAttribute('aria-describedby')).toBe(note.id)
    expect(posted).toEqual([])
  })

  it('a list item that doesn’t fit is shown as left out, with why; a value written as null isn’t a fault (#233)', async () => {
    const reason = `setting is a link to one entity, such as "[[borgo-pass]]", in quotes, with nothing else in the string, not 'Borgo Pass'.`
    const withItem = () => {
      const b = bookChapterFields()
      const chapters = b.chapters.map((c) =>
        c.key !== '2'
          ? c
          : {
              ...c,
              fields: c.fields.map((f) =>
                f.name === 'setting'
                  ? unsetValue('setting', { set: true, own: ['[[castle-dracula]]', 'Borgo Pass'], value: ['[[castle-dracula]]', 'Borgo Pass'], from: 'chapter', ignoredItems: [{ item: 1, asWritten: 'Borgo Pass', reason }] })
                  : f.name === 'when'
                    ? unsetValue('when', { set: true, own: null, ignored: true })
                    : f,
              ),
            },
      )
      return { ...b, chapters }
    }
    stubFields({ book: withItem })
    renderApp('/u/dracula/b/dracula/c/2')
    await screen.findByRole('heading', { name: "Jonathan Harker's Journal", level: 1 })

    await waitFor(() => expect(row('setting').textContent).toContain(`(item 2, "Borgo Pass", is left out: ${reason})`))
    expect(row('when').textContent).not.toContain('ignored')
    fireEvent.click(within(row('setting')).getByRole('button', { name: 'Edit setting' }))
    const form = screen.getByRole('form', { name: 'Edit setting' })
    expect(within(form).getByRole('note').textContent).toBe(`Item 2, "Borgo Pass", is left out: ${reason} Change or remove it here, or it stays as written.`)
  })
})

describe('SC-WEB-74: I edit a chapter’s fields, and go back to the default', () => {
  it('Edit starts from the inherited value, and Save sets the chapter’s own', async () => {
    const { posted } = stubFields()
    renderApp('/u/dracula/b/dracula/c/2')
    fireEvent.click(await screen.findByRole('button', { name: 'Edit tense' }))

    const form = screen.getByRole('form', { name: 'Edit tense' })
    const value = within(form).getByLabelText('Value') as HTMLSelectElement
    await waitFor(() => expect(document.activeElement).toBe(value))
    expect(value.value).toBe('past')
    expect(form.textContent).toContain("This starts from the value inherited now, from book 'dracula' defaults.")
    // Nothing changed yet, so nothing to save.
    expect((within(form).getByRole('button', { name: 'Save' }) as HTMLButtonElement).disabled).toBe(true)

    fireEvent.change(value, { target: { value: 'present' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))

    const notice = await screen.findByText('Saved tense.')
    await waitFor(() => expect(document.activeElement).toBe(notice.closest('[role="status"]')))
    expect(notice.closest('[role="status"]')!.textContent).toContain('its own value')
    expect(posted).toEqual([
      { path: '/api/chapter/edit', body: { universe: 'dracula', book: 'dracula', chapter: '2', field: 'tense', changes: [{ op: 'set', value: 'present' }], expectedHash: 'h-book' } },
    ])
  })

  it('removing the last POV saves [], "none in this chapter", and Use the default is its own action', async () => {
    const { posted } = stubFields()
    renderApp('/u/dracula/b/dracula/c/1')
    fireEvent.click(await screen.findByRole('button', { name: 'Edit pov' }))
    const form = screen.getByRole('form', { name: 'Edit pov' })
    fireEvent.click(within(form).getByRole('button', { name: 'Remove item 1' }))
    expect(within(form).getByRole('status').textContent).toBe('No items: saving sets pov: [], none in this chapter.')
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))
    await screen.findByText('Saved pov.')
    expect(posted[0].body.changes).toEqual([{ op: 'remove', item: 0 }])

    // Use the default says first what the chapter will have, then clears the field.
    fireEvent.click(screen.getByRole('button', { name: 'Use the default: pov' }))
    const prompt = screen.getByRole('group', { name: /^Use the default for pov\?/ })
    expect(prompt.textContent).toContain('Nothing above sets pov, so this chapter will have none.')
    fireEvent.click(within(prompt).getByRole('button', { name: 'Use the default' }))
    await waitFor(() => expect(posted).toHaveLength(2))
    expect(posted[1].body).toEqual({ universe: 'dracula', book: 'dracula', chapter: '1', field: 'pov', changes: [{ op: 'clear' }], expectedHash: 'h-book' })
  })

  it('removing the last POV where the book has one shows the outcome inline, with the default one click away (#138)', async () => {
    const withDefault = () => {
      const b = bookChapterFields()
      const chapters = b.chapters.map((c) =>
        c.key !== '1'
          ? c
          : { ...c, fields: c.fields.map((f) => (f.name === 'pov' ? { ...f, default: ['[[mina-murray]]'], defaultFrom: 'book' as const, defaultFromKey: 'dracula' } : f)) },
      )
      return { ...b, chapters }
    }
    const { posted } = stubFields({ book: withDefault })
    renderApp('/u/dracula/b/dracula/c/1')
    fireEvent.click(await screen.findByRole('button', { name: 'Edit pov' }))
    const form = screen.getByRole('form', { name: 'Edit pov' })
    fireEvent.click(within(form).getByRole('button', { name: 'Remove item 1' }))

    // The safe choice, [] "none here", applies by default; the effect shows before saving.
    expect(within(form).getByRole('status').textContent).toBe('pov: [[jonathan-harker]] → none in this chapter. Saving sets pov: [].')
    expect(within(form).getByRole('status').querySelector('del')!.textContent).toBe('[[jonathan-harker]]')
    fireEvent.click(within(form).getByRole('button', { name: 'use book default ([[mina-murray]]) instead' }))
    expect(within(form).getByRole('status').textContent).toBe('pov: [[jonathan-harker]] → [[mina-murray]] (book default). Saving takes pov out, so it inherits.')
    // And back again.
    fireEvent.click(within(form).getByRole('button', { name: 'none in this chapter instead' }))
    expect(within(form).getByRole('status').textContent).toContain('→ none in this chapter')
    fireEvent.click(within(form).getByRole('button', { name: 'use book default ([[mina-murray]]) instead' }))

    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))
    await screen.findByText('Saved pov.')
    expect(posted[0].body.changes).toEqual([{ op: 'clear' }])
    expect(screen.queryByRole('group', { name: /^Use the default for pov\?/ })).toBeNull()
  })

  it('a chapter whose own pov is [] can choose the default, and that counts as a change (#138)', async () => {
    const ownNone = () => {
      const b = bookChapterFields()
      const pov = { set: true, own: [], value: [], from: 'chapter' as const, none: true, links: [], default: ['[[mina-murray]]'], defaultFrom: 'book' as const, defaultFromKey: 'dracula' }
      return { ...b, chapters: b.chapters.map((c) => (c.key !== '1' ? c : { ...c, fields: c.fields.map((f) => (f.name === 'pov' ? { ...f, ...pov } : f)) })) }
    }
    const { posted } = stubFields({ book: ownNone })
    renderApp('/u/dracula/b/dracula/c/1')
    fireEvent.click(await screen.findByRole('button', { name: 'Edit pov' }))
    const form = screen.getByRole('form', { name: 'Edit pov' })
    const save = within(form).getByRole('button', { name: 'Save' }) as HTMLButtonElement
    expect(within(form).getByRole('status').textContent).toBe('pov: none in this chapter. Saving sets pov: [].')
    expect(save.disabled).toBe(true)
    // Ctrl+Enter follows the Save button: nothing to send yet.
    fireEvent.keyDown(form, { key: 'Enter', ctrlKey: true })
    expect(posted).toHaveLength(0)

    fireEvent.click(within(form).getByRole('button', { name: 'use book default ([[mina-murray]]) instead' }))
    expect(save.disabled).toBe(false)
    fireEvent.keyDown(form, { key: 'Enter', ctrlKey: true })
    await screen.findByText('Saved pov.')
    expect(posted[0].body.changes).toEqual([{ op: 'clear' }])
  })

  it('a field the chapter only inherits is greyed on its page', async () => {
    stubFields()
    renderApp('/u/dracula/b/dracula/c/2')
    await screen.findByRole('group', { name: 'Fields of this chapter' })
    await waitFor(() => expect(row('tense').querySelector('dd.inherited-value')).not.toBeNull())
    expect(row('pov').querySelector('dd.inherited-value')).toBeNull()
  })

  it('a changed date is set as a whole, as the form writes it', async () => {
    const { posted } = stubFields()
    renderApp('/u/dracula/b/dracula/c/1')
    fireEvent.click(await screen.findByRole('button', { name: 'Edit when' }))
    const form = screen.getByRole('form', { name: 'Edit when' })
    fireEvent.change(within(form).getByLabelText('Date'), { target: { value: '3–6 May' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))
    await screen.findByText('Saved when.')
    expect(posted[0].body.changes).toEqual([{ op: 'set', value: { text: '3–6 May', sort: [{ $plain: '0' }, { $plain: '5' }, { $plain: '3' }] } }])
  })
})

describe('SC-WEB-75: I edit chapter defaults, and give a new chapter its fields', () => {
  it('the universe page edits its chapter defaults', async () => {
    const { posted } = stubFields()
    renderApp('/u/dracula')
    const list = await screen.findByRole('group', { name: "Chapter defaults of universe 'dracula'" })
    fireEvent.click(within(list).getByRole('button', { name: 'Edit narration' }))
    const form = screen.getByRole('form', { name: 'Edit narration' })
    fireEvent.change(within(form).getByLabelText('Value'), { target: { value: 'third-limited' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))
    await screen.findByText('Saved narration.')
    expect(posted).toEqual([
      {
        path: '/api/chapter-defaults/edit',
        body: { universe: 'dracula', field: 'narration', changes: [{ op: 'set', value: 'third-limited' }], expectedHash: 'h-universe' },
      },
    ])
  })

  it('Add chapter sends the fields filled in, and leaves the empty ones to be inherited', async () => {
    const { posted } = stubFields({
      post: () => ({ ...result(), key: '6', position: 'dracula:6', number: 6, title: null, index: 6, renumbered: [], effects: ['Added chapter 6.'] }) as unknown as WriteResult,
    })
    renderApp('/u/dracula/b/dracula')
    fireEvent.click(await screen.findByRole('button', { name: 'Add chapter to Dracula' }))
    const dialog = screen.getByRole('dialog')
    const tense = await within(dialog).findByRole('group', { name: 'tense' })
    expect(tense.textContent).toContain("Left empty: past, from book 'dracula' defaults.")
    expect(within(dialog).getByRole('group', { name: 'pov' }).textContent).toContain('Left empty: none.')
    fireEvent.change(within(tense).getByLabelText('Value'), { target: { value: 'present' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add' }))

    await screen.findByText(/^Added chapter /, { selector: 'p' })
    expect(posted).toEqual([{ path: '/api/chapter/create', body: { universe: 'dracula', book: 'dracula', fields: { tense: 'present' }, expectedHash: 'h-book' } }])
  })
})

describe('SC-WEB-76: chapter field edits are guarded', () => {
  it('a final book has no Edit, and says how to unfreeze it', async () => {
    stubFields({ book: () => bookChapterFields({ final: true }), defaults: (kind) => chapterDefaults(kind, { final: kind === 'book' }) })
    renderApp('/u/dracula/b/dracula/c/1')
    const list = await screen.findByRole('group', { name: 'Fields of this chapter' })
    expect(within(list).queryByRole('button')).toBeNull()
    expect(screen.getByText(/^The book is final \(frozen\)/).textContent).toContain('Unfreeze…')
  })

  it('a refusal shows the server’s words, which say how to unfreeze', async () => {
    const frozen =
      "The edit would change what chapters of a final (frozen) book have (F-BOOK-1): book 'dracula', narration of chapters 1–note. Unfreeze… on the page of book 'dracula' opens it for editing again."
    stubFields({ post: () => new Response(JSON.stringify({ code: 'frozen', message: frozen }), { status: 409 }) })
    renderApp('/u/dracula')
    const list = await screen.findByRole('group', { name: "Chapter defaults of universe 'dracula'" })
    fireEvent.click(within(list).getByRole('button', { name: 'Edit narration' }))
    const form = screen.getByRole('form', { name: 'Edit narration' })
    fireEvent.change(within(form).getByLabelText('Value'), { target: { value: 'first' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))
    expect((await within(form).findByRole('alert')).textContent).toBe(`It can't be saved: ${frozen}`)
  })

  it('a stale book.md is sent again only for added or removed items on unchanged lines; otherwise both versions show', async () => {
    const STALE = () => new Response(JSON.stringify({ code: 'stale_file', message: 'book.md has changed since it was read' }), { status: 409 })
    const lines = (b: BookChapterFields, text: string) => ({ ...b, chapters: b.chapters.map((c) => ({ ...c, fields: c.fields.map((f) => ({ ...f, text })) })) })
    let fresh = lines(bookChapterFields({ hash: 'h-book-2' }), '  - { key: "1" }')
    let answers: (Response | WriteResult)[] = [STALE(), result()]
    let reads = 0
    const { posted } = stubFields({
      post: () => answers.shift()!,
      book: () => (reads++ === 0 ? lines(bookChapterFields(), '  - { key: "1" }') : fresh),
    })
    renderApp('/u/dracula/b/dracula/c/1')

    // Another part of book.md changed, and chapter 1's lines are byte for byte as they were: an added POV is sent again.
    fireEvent.click(await screen.findByRole('button', { name: 'Edit pov' }))
    let form = screen.getByRole('form', { name: 'Edit pov' })
    fireEvent.click(within(form).getByRole('button', { name: 'Add item' }))
    fireEvent.change(within(form).getByLabelText('Item 2'), { target: { value: '[[mina-harker]]' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))
    await screen.findByText('Saved pov.')
    expect(posted.map((p) => p.body.expectedHash)).toEqual(['h-book', 'h-book-2'])
    expect(posted[1].body.changes).toEqual([{ op: 'add', value: '[[mina-harker]]' }])

    // A change that sets the field whole is never sent again unseen: both versions, even though tense itself didn't change.
    posted.length = 0
    fresh = lines(bookChapterFields({ hash: 'h-book-3' }), '  - { key: "1" }')
    answers = [STALE(), result()]
    fireEvent.click(screen.getByRole('button', { name: 'Edit tense' }))
    form = screen.getByRole('form', { name: 'Edit tense' })
    fireEvent.change(within(form).getByLabelText('Value'), { target: { value: 'present' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))
    const conflict = await screen.findByRole('region', { name: 'The file changed while you were editing tense' })
    expect(conflict.textContent).toContain('"present"')
    fireEvent.click(within(conflict).getByRole('button', { name: 'Keep mine' }))
    await waitFor(() => expect(posted).toHaveLength(2))
    expect(posted.map((p) => p.body.expectedHash)).toEqual(['h-book-2', 'h-book-3'])
    expect(posted[1].body).toMatchObject({ changes: [{ op: 'set', value: 'present' }] })
  })

  it('a refetch while the form is open doesn’t move it onto the other writer’s file', async () => {
    let current = bookChapterFields()
    const answers: (Response | WriteResult)[] = [new Response(JSON.stringify({ code: 'stale_file', message: 'book.md has changed since it was read' }), { status: 409 })]
    const { posted } = stubFields({ post: () => answers.shift() ?? result(), book: () => current })
    const { queryClient } = renderApp('/u/dracula/b/dracula/c/2')
    fireEvent.click(await screen.findByRole('button', { name: 'Edit tense' }))
    const form = screen.getByRole('form', { name: 'Edit tense' })
    fireEvent.change(within(form).getByLabelText('Value'), { target: { value: 'present' } })

    // Meanwhile another program set chapter 2's tense, and the page refetched (say, on window focus).
    current = bookChapterFields({ hash: 'h-other' })
    current.chapters[1].fields[2] = unsetValue('tense', { set: true, own: 'future', value: 'future', from: 'chapter', text: '  - { key: "2", fields: { tense: future } }' })
    await queryClient.invalidateQueries()
    await waitFor(() => expect(screen.getAllByText(/future/).length).toBeGreaterThan(0))

    fireEvent.click(within(form).getByRole('button', { name: 'Save' }))
    const conflict = await screen.findByRole('region', { name: 'The file changed while you were editing tense' })
    expect(posted[0].body.expectedHash).toBe('h-book')
    expect(conflict.textContent).toContain('"future"')
  })

  it('Use the default on a stale file shows what the file has now, or says it was already cleared', async () => {
    const STALE = () => new Response(JSON.stringify({ code: 'stale_file', message: 'book.md has changed since it was read' }), { status: 409 })
    let fresh = bookChapterFields({ hash: 'h-book-2' })
    fresh.chapters[0].fields[0] = unsetValue('pov', { set: true, own: ['[[mina-harker]]'], value: ['[[mina-harker]]'], from: 'chapter' })
    let answers: (Response | WriteResult)[] = [STALE(), result()]
    let reads = 0
    const { posted } = stubFields({ post: () => answers.shift()!, book: () => (reads++ === 0 ? bookChapterFields() : fresh) })
    renderApp('/u/dracula/b/dracula/c/1')

    // Another program set a new POV meanwhile: it isn't cleared unseen.
    fireEvent.click(await screen.findByRole('button', { name: 'Use the default: pov' }))
    fireEvent.click(within(screen.getByRole('group', { name: /^Use the default for pov\?/ })).getByRole('button', { name: 'Use the default' }))
    const conflict = await screen.findByRole('region', { name: 'The file changed before pov was cleared' })
    expect(conflict.textContent).toContain('[[mina-harker]]')
    expect(screen.queryByText('Saved pov.')).toBeNull()
    fireEvent.click(within(conflict).getByRole('button', { name: 'Keep mine' }))
    await screen.findByText('Saved pov.')
    expect(posted.map((p) => p.body)).toEqual([
      { universe: 'dracula', book: 'dracula', chapter: '1', field: 'pov', changes: [{ op: 'clear' }], expectedHash: 'h-book' },
      { universe: 'dracula', book: 'dracula', chapter: '1', field: 'pov', changes: [{ op: 'clear' }], expectedHash: 'h-book-2' },
    ])

    // This time the field was taken out meanwhile: the page says so.
    fresh = bookChapterFields({ hash: 'h-book-3' })
    fresh.chapters[0].fields[0] = unsetValue('pov')
    answers = [STALE()]
    reads = 0
    cleanup()
    vi.unstubAllGlobals()
    stubFields({ post: () => answers.shift()!, book: () => (reads++ === 0 ? bookChapterFields() : fresh) })
    renderApp('/u/dracula/b/dracula/c/1')
    fireEvent.click(await screen.findByRole('button', { name: 'Use the default: pov' }))
    fireEvent.click(within(screen.getByRole('group', { name: /^Use the default for pov\?/ })).getByRole('button', { name: 'Use the default' }))
    expect((await screen.findByText(/was already taken out of the file/)).textContent).toContain('pov was already taken out of the file by something else meanwhile')
  })

  it('as of a position, later chapters keep their fields to themselves, and links to notes not met yet don’t say what they reach', async () => {
    const quincey = { text: '[[quincey-harker]]', target: 'quincey-harker', label: null, outcome: 'resolved' as const, entity: 'E-QUINCEY-H', name: 'Quincey Harker', type: 'character', problem: null }
    const withQuincey = () => {
      const b = bookChapterFields()
      b.chapters[0].fields[0] = { ...b.chapters[0].fields[0], value: ['[[jonathan-harker]]', '[[quincey-harker]]'], links: [...b.chapters[0].fields[0].links, quincey] }
      return b
    }
    stubFields({ book: withQuincey })
    renderApp('/u/dracula/b/dracula?at=dracula:1')
    await waitFor(() => expect(document.querySelector('ol.chapters li .chapter-field-summary')).not.toBeNull())
    const items = [...document.querySelectorAll<HTMLElement>('ol.chapters li')]
    const first = items[0].querySelector<HTMLElement>('.chapter-field-summary')!
    expect(within(first).getByRole('link', { name: 'Jonathan Harker' })).toBeTruthy()
    expect(within(first).queryByRole('link', { name: /Quincey/ })).toBeNull()
    expect(first.textContent).not.toContain('Quincey Harker')
    // Chapters after the position show no fields.
    expect(items.slice(1).every((li) => li.querySelector('.chapter-field-summary') === null)).toBe(true)
  })

  it('as of a position, no chapter shows its fields while the story’s positions are unknown', async () => {
    const api = stubApi({
      override: (path) =>
        path === '/api/positions' ? new Response(JSON.stringify({ code: 'internal', message: 'The positions failed.' }), { status: 500 }) : undefined,
    })
    renderApp('/u/dracula/b/dracula?at=dracula:1')
    await screen.findByRole('heading', { name: 'Dracula', level: 1 })
    // The fields and what the reader has met have arrived; only the positions are missing.
    await waitFor(() => expect(api.calls.some((c) => c.startsWith('/api/book/chapter-fields'))).toBe(true))
    await waitFor(() => expect(api.calls.some((c) => c.startsWith('/api/entities') && c.includes('asOf='))).toBe(true))
    await waitFor(() => expect(api.calls.some((c) => c.startsWith('/api/positions'))).toBe(true))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(document.querySelectorAll('ol.chapters li').length).toBeGreaterThan(0)
    expect(document.querySelector('.chapter-field-summary')).toBeNull()
  })

  it('a chapter written as a bare key says how to give it fields', async () => {
    stubFields({ book: () => ({ ...bookChapterFields(), chapters: bookChapterFields().chapters.map((c) => ({ ...c, bare: true })) }) })
    renderApp('/u/dracula/b/dracula/c/2')
    expect((await screen.findByText(/bare key/)).textContent).toContain('write it as { key: 2 } in book.md first')
    expect(within(screen.getByRole('group', { name: 'Fields of this chapter' })).queryByRole('button')).toBeNull()
  })
})

describe('chapterFieldChanges', () => {
  const pov = chapterFieldTypes[0]
  const when = chapterFieldTypes[3]
  const draft = (raw: Parameters<typeof fieldDraftOf>[1]) => fieldDraftOf(pov, raw) as FieldDraft

  it('removes, adds, moves, and replaces list items by number, keeping a moved or changed item in place (#197)', () => {
    const d = draft(['[[a]]', '[[b]]', '[[c]]'])
    const items = d.entries[0].value.kind === 'list' ? d.entries[0].value.items : []
    const changed: FieldDraft = {
      ...d,
      entries: [{ ...d.entries[0], value: { kind: 'list', raw: ['[[a]]', '[[b]]', '[[c]]'], items: [items[0], { ...items[1], draft: { ...items[1].draft, text: '[[x]]' } }] } }],
    }
    expect(chapterFieldChanges(pov, ['[[a]]', '[[b]]', '[[c]]'], changed)).toEqual([
      { op: 'remove', item: 2 },
      { op: 'replace', item: 1, value: '[[x]]' },
    ])

    const reordered: FieldDraft = {
      ...d,
      entries: [{ ...d.entries[0], value: { kind: 'list', raw: ['[[a]]', '[[b]]', '[[c]]'], items: [items[2], items[0], items[1]] } }],
    }
    expect(chapterFieldChanges(pov, ['[[a]]', '[[b]]', '[[c]]'], reordered)).toEqual([{ op: 'move', item: 2, index: 0 }])
  })

  it('an emptied list is its items removed, which leaves [], and nothing set is set whole', () => {
    const d = draft(['[[a]]'])
    const empty: FieldDraft = { ...d, entries: [{ ...d.entries[0], value: { kind: 'list', raw: ['[[a]]'], items: [] } }] }
    expect(chapterFieldChanges(pov, ['[[a]]'], empty)).toEqual([{ op: 'remove', item: 0 }])
    expect(chapterFieldChanges(pov, undefined, { ...empty, shape: 'none' })).toEqual([{ op: 'set', value: [] }])
    const date = fieldDraftOf(when, '1893') as FieldDraft
    const v = date.entries[0].value
    if (v.kind === 'one') {
      const next: FieldDraft = { ...date, entries: [{ ...date.entries[0], value: { kind: 'one', draft: { ...v.draft, sort: '1893' } } }] }
      expect(chapterFieldChanges(when, '1893', next)).toEqual([{ op: 'set', value: { text: '1893', sort: { $plain: '1893' } } }])
    }
  })
})

// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { editorViewIn } from './components/MarkdownEditor'
import { denErrorsQuery, DEN_ERRORS_POLL_MS, leavesOut, type DenErrors, type DenErrorView, type FileSource } from './denErrorsApi'
import { renderApp, stubApi } from './test/harness'

beforeAll(() => {
  // jsdom lays nothing out; CodeMirror measures text ranges.
  const rect = { x: 0, y: 0, width: 0, height: 0, top: 0, left: 0, right: 0, bottom: 0, toJSON: () => ({}) } as DOMRect
  Range.prototype.getBoundingClientRect = () => rect
  Range.prototype.getClientRects = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }) as unknown as DOMRectList
  document.execCommand ??= () => false
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const VAULT = 'universes/dracula/entities/notes/ember-vault.md'

function error(extra: Partial<DenErrorView> = {}): DenErrorView {
  return {
    path: VAULT,
    universe: 'dracula',
    name: null,
    kind: 'note',
    line: 5,
    rule: 'F-ENT-5',
    message: "'maybe' isn't a status.",
    reach: 'file',
    section: null,
    sectionLine: null,
    canEdit: true,
    ...extra,
  }
}

function list(...errors: DenErrorView[]): DenErrors {
  return { version: 1, count: errors.length, truncated: false, denLoaded: true, errors }
}

function source(extra: Partial<FileSource> = {}): FileSource {
  return {
    version: 1,
    path: VAULT,
    kind: 'note',
    universe: 'dracula',
    hash: 'h1',
    text: '---\nstatus: maybe\n---\n',
    lineEndings: 'lf',
    byteOrderMark: false,
    frozen: null,
    errors: [{ line: 5, rule: 'F-ENT-5', message: "'maybe' isn't a status.", reach: 'file', section: null, sectionLine: null }],
    entity: null,
    ...extra,
  }
}

/** Types into the file's editor, once it's open. */
async function typeInFile(text: string) {
  const group = await screen.findByRole('group', { name: `Text of ${VAULT}` })
  await waitFor(() => expect(editorViewIn(group)).not.toBeNull())
  const view = editorViewIn(group)!
  act(() => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } }))
  return view
}

function json(body: unknown, status: number) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('the top bar’s problems', () => {
  it('SC-WEB-90: shows nothing while the den has no errors', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula')

    await waitFor(() => expect(calls).toContain('/api/den/errors'))
    await screen.findAllByText('Dracula')
    expect(screen.queryByRole('button', { name: /problem/ })).toBeNull()
  })

  it('SC-WEB-90: appears when an answer has errors and clears when they’re gone, by polling', async () => {
    let answer = list()
    stubApi({ override: (path) => (path === '/api/den/errors' ? answer : undefined) })
    const { queryClient } = renderApp('/u/dracula')
    await screen.findAllByText('Dracula')

    answer = list(error(), error({ path: 'universes/dracula/entities/notes/other.md' }))
    await queryClient.refetchQueries({ queryKey: denErrorsQuery.queryKey })
    expect(await screen.findByRole('button', { name: /2 problems/ })).toBeTruthy()

    answer = list()
    await queryClient.refetchQueries({ queryKey: denErrorsQuery.queryKey })
    await waitFor(() => expect(screen.queryByRole('button', { name: /problem/ })).toBeNull())

    // It asks by itself, only while the page is showing (the query's default).
    expect(denErrorsQuery.refetchInterval).toBe(DEN_ERRORS_POLL_MS)
    expect(denErrorsQuery.refetchIntervalInBackground).toBeUndefined()
  })

  it('SC-WEB-90: says it can’t check, quietly, rather than seem all clear', async () => {
    stubApi({ override: (path) => (path === '/api/den/errors' ? json({ code: 'den_unreadable', message: 'The index is locked.' }, 503) : undefined) })
    renderApp('/u/dracula')

    expect(await screen.findByText('Can’t check for problems')).toBeTruthy()
  })

  it('SC-WEB-90: lists each error’s file, line, message, and what it leaves out, at any position', async () => {
    const { calls } = stubApi({
      override: (path) =>
        path === '/api/den/errors'
          ? list(
              error(),
              error({ path: 'universes/dracula/books/dracula/entities/characters/mina-harker.md', name: 'Mina Harker', reach: 'section', section: 'A rumor', sectionLine: 20, line: 21 }),
              error({ path: 'universes/dracula/books/dracula/book.md', kind: 'book', reach: 'container', line: 3 }),
              error({ path: 'packs/mine/pack.yaml', universe: null, kind: 'pack', canEdit: false, line: null }),
            )
          : undefined,
    })
    renderApp('/u/dracula?at=dracula:1')

    fireEvent.click(await screen.findByRole('button', { name: /4 problems/ }))

    const dialog = await screen.findByRole('dialog', { name: '4 problems in this den' })
    const items = within(dialog).getAllByRole('listitem')
    expect(items[0].textContent).toContain(VAULT)
    expect(items[0].textContent).toContain(', line 5')
    expect(items[0].textContent).toContain("'maybe' isn't a status.")
    expect(items[0].textContent).toContain('The whole note.')
    expect(items[1].textContent).toContain('Mina Harker')
    expect(items[1].textContent).toContain('The section “A rumor” and its subsections.')
    expect(items[2].textContent).toContain('The whole book and everything filed in it.')
    expect(within(items[3]).queryByRole('link')).toBeNull()
    expect(items[3].textContent).toContain('Fix it in your own editor.')
    expect(within(items[0]).getByRole('link', { name: 'Edit file' }).getAttribute('href')).toContain(`path=${encodeURIComponent(VAULT)}`)
    // The list takes no position: it's the writer's.
    expect(calls.filter((c) => c.startsWith('/api/den/errors')).every((c) => c === '/api/den/errors')).toBe(true)
  })

  it('SC-WEB-90: says what each reach leaves out', () => {
    expect(leavesOut({ reach: 'den', kind: 'den', section: null })).toContain('whole den')
    expect(leavesOut({ reach: 'container', kind: 'series', section: null })).toContain('whole series')
    expect(leavesOut({ reach: 'container', kind: 'universe', section: null })).toContain('whole universe')
    expect(leavesOut({ reach: 'container', kind: 'folder', section: null })).toContain('folder')
    expect(leavesOut({ reach: 'file', kind: 'outline', section: null })).toContain('outline')
  })
})

describe('Edit file by path', () => {
  it('SC-WEB-91: opens a file left out, shows its errors, and saves with its hash', async () => {
    let posted: Record<string, unknown> | null = null
    let current = source()
    stubApi({
      override: (path, _params, init) => {
        if (path === '/api/den/errors') {
          return list(error())
        }

        if (path === '/api/file/source') {
          return current
        }

        if (path === '/api/file/edit') {
          posted = JSON.parse(String(init?.body)) as Record<string, unknown>
          current = source({ hash: 'h2', text: '---\nstatus: canon\n---\n', errors: [] })
          return { version: 1, changed: true, path: VAULT, hash: 'h2', entity: null, effects: ["Saved the whole file. It's back in the den."], pinnedCanon: [], warnings: [], files: null, idAdded: null }
        }

        return undefined
      },
    })
    renderApp(`/file?path=${encodeURIComponent(VAULT)}`)

    expect(await screen.findByRole('heading', { name: 'Edit file' })).toBeTruthy()
    expect(await screen.findByText(VAULT)).toBeTruthy()
    expect(await screen.findByText(/'maybe' isn't a status/)).toBeTruthy()
    await typeInFile('---\nstatus: canon\n---\n')

    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText("Saved the whole file. It's back in the den.")).toBeTruthy()
    expect(posted).toEqual({ path: VAULT, text: '---\nstatus: canon\n---\n', expectedHash: 'h1' })
    expect(await screen.findByText('This file has no errors now.')).toBeTruthy()
  })

  it('SC-WEB-91: a save that would add an error stays in the editor and says why', async () => {
    stubApi({
      override: (path) => {
        if (path === '/api/file/source') {
          return source()
        }

        return path === '/api/file/edit' ? json({ code: 'invalid_edit', message: 'The edit would add errors, so nothing was written.' }, 422) : undefined
      },
    })
    renderApp(`/file?path=${encodeURIComponent(VAULT)}`)
    const view = await typeInFile('worse')

    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect((await screen.findByRole('alert')).textContent).toContain('would add errors')
    expect(view.state.doc.toString()).toBe('worse')
  })

  it('SC-WEB-91: a file changed outside offers both versions, and keeping mine saves on the new hash', async () => {
    const hashes: unknown[] = []
    stubApi({
      override: (path, _params, init) => {
        if (path === '/api/file/source') {
          return hashes.length < 2 ? source() : source({ hash: 'h3', text: 'mine', errors: [] })
        }

        if (path === '/api/file/edit') {
          hashes.push((JSON.parse(String(init?.body)) as { expectedHash: string }).expectedHash)
          return hashes.length === 1
            ? json({ code: 'stale_file', message: 'It changed.', currentFile: source({ hash: 'h2', text: 'theirs' }) }, 409)
            : { version: 1, changed: true, path: VAULT, hash: 'h3', entity: null, effects: ['Saved.'], pinnedCanon: [], warnings: [], files: null, idAdded: null }
        }

        return undefined
      },
    })
    renderApp(`/file?path=${encodeURIComponent(VAULT)}`)
    await typeInFile('mine')
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('heading', { name: 'The file changed while you were editing it' })).toBeTruthy()
    expect(screen.getByText('theirs')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Keep mine' }))

    expect(await screen.findByText('Saved.')).toBeTruthy()
    expect(hashes).toEqual(['h1', 'h2'])
  })

  it('SC-WEB-91: a file in a final book shows, and can’t be edited here', async () => {
    stubApi({ override: (path) => (path === '/api/file/source' ? source({ frozen: 'It is in book dracula, which is final.' }) : undefined) })
    renderApp(`/file?path=${encodeURIComponent(VAULT)}`)

    expect((await screen.findByText('It is in book dracula, which is final.')).getAttribute('role')).toBe('alert')
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull()
  })

  it('SC-WEB-92: a path it can’t open says so', async () => {
    stubApi({ override: (path) => (path === '/api/file/source' ? json({ code: 'unknown_file', message: "Wolfden has no file to edit at '../x.md'." }, 404) : undefined) })
    renderApp('/file?path=..%2Fx.md')

    expect((await screen.findByRole('alert')).textContent).toContain('no file to edit')
  })
})

describe('the page of a note left out', () => {
  it('SC-WEB-94: says so, names the line, and offers Edit file', async () => {
    stubApi({
      override: (path) =>
        path === '/api/entity'
          ? json({ code: 'unknown_entity', message: `'Ember Vault' is left out of the den by an error in ${VAULT}, line 5.`, leftOut: { path: VAULT, line: 5, rule: 'F-ENT-5', message: 'bad' } }, 404)
          : undefined,
    })
    renderApp('/u/dracula/e/Ember%20Vault')

    expect(await screen.findByRole('heading', { name: 'This note is left out' })).toBeTruthy()
    expect(screen.getByRole('alert').textContent).toContain('line 5')
    expect(screen.getByRole('link', { name: 'Edit file' }).getAttribute('href')).toContain(encodeURIComponent(VAULT))
    expect(document.body.textContent).not.toContain('/api/')
  })

  it('SC-WEB-95: as of a position, names no file, and offers to show everything', async () => {
    stubApi({
      override: (path, params) => {
        if (path !== '/api/entity') {
          return undefined
        }

        return params.get('asOf')
          ? json({ code: 'unknown_entity', message: "'E-1' can't be shown as of dracula:1: an error leaves it out of the den at every story position.", leftOut: { path: null, line: null, rule: null, message: null } }, 404)
          : json({ code: 'unknown_entity', message: 'It has an error at line 5.', leftOut: { path: VAULT, line: 5, rule: 'F-ENT-5', message: 'bad' } }, 404)
      },
    })
    renderApp('/u/dracula/e/E-1?at=dracula:1')

    expect(await screen.findByRole('heading', { name: 'This note is left out' })).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Edit file' })).toBeNull()
    expect(document.querySelector('.page-body')!.textContent).not.toContain(VAULT)

    fireEvent.click(within(document.querySelector<HTMLElement>('.page-body')!).getByRole('button', { name: 'Show everything' }))

    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('line 5'))
    expect(await screen.findByRole('link', { name: 'Edit file' })).toBeTruthy()
  })

  it('SC-WEB-94: becomes the note when the error’s fixed', async () => {
    let fixed = false
    let errors = list(error())
    stubApi({
      override: (path) => {
        if (path === '/api/den/errors') {
          return errors
        }

        if (path === '/api/entity' && !fixed) {
          return json({ code: 'unknown_entity', message: 'Left out at line 5.', leftOut: { path: VAULT, line: 5, rule: 'F-ENT-5', message: 'bad' } }, 404)
        }

        return undefined
      },
    })
    const { queryClient } = renderApp('/u/dracula/e/E-MINA')
    expect(await screen.findByRole('heading', { name: 'This note is left out' })).toBeTruthy()
    await queryClient.refetchQueries({ queryKey: denErrorsQuery.queryKey })

    fixed = true
    errors = list()
    await queryClient.refetchQueries({ queryKey: denErrorsQuery.queryKey })

    expect(await screen.findByRole('heading', { name: 'Mina Harker' })).toBeTruthy()
  })
})

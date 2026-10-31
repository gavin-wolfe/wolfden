// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, screen, waitFor, configure } from '@testing-library/react'
import type { EntityPageResult } from './api'
import type { EntitySource } from './editApi'
import type { AddedImage } from './imageApi'
import { imageMarkdown, imageProblem, looksLikeImage } from './imageApi'
import { editorViewIn } from './components/MarkdownEditor'
import { entities, page, renderApp, stubApi } from './test/harness'

// Adding an image to a note from the editor (#187): Add image…, a paste,
// and a drop send the file to /api/entity/image and put `![alt](path)` in
// at the cursor.

configure({ asyncUtilTimeout: 5000 })
vi.setConfig({ testTimeout: 15_000 })

beforeAll(() => {
  const rect = { x: 0, y: 0, width: 0, height: 0, top: 0, left: 0, right: 0, bottom: 0, toJSON: () => ({}) } as DOMRect
  Range.prototype.getBoundingClientRect = () => rect
  Range.prototype.getClientRects = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }) as unknown as DOMRectList
  document.execCommand ??= () => false
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const mina = entities.find((e) => e.key === 'E-MINA')!

function source(): EntitySource {
  return {
    version: 1,
    entity: mina,
    hash: 'h1',
    text: '---\nname: Mina Harker\n---\n\n# Mina Harker\n\nA schoolmistress.\n\n## Overview\nOld text.\n',
    lineEndings: 'lf',
    byteOrderMark: false,
    frozen: null,
    lead: { line: 4, text: '# Mina Harker\n\nA schoolmistress.' },
    sections: [{ index: 0, name: 'Overview', level: 2, line: 9, comment: null, body: 'Old text.', pinned: false, parent: null }],
    standardSections: ['Overview'],
    fields: [],
    fieldTypes: [],
  }
}

interface Upload {
  query: Record<string, string>
  contentType: string | null
  body: unknown
}

function stubImages(answer: (upload: Upload, n: number) => Response | AddedImage = (u) => added(u.query.name)) {
  const pages = (key: string, at: string | null): EntityPageResult => {
    const summary = entities.find((e) => e.key === key)!
    return {
      version: 1, view: at ? 'canon_as_of' : 'full', position: at, inView: true, note: null,
      entity: page(summary, { lead: `# ${summary.name}\n\nA schoolmistress.`, frozen: null }),
    }
  }

  const stub = stubApi({ entityPage: pages })
  const base = stub.fetchMock.getMockImplementation()!
  const uploads: Upload[] = []
  stub.fetchMock.mockImplementation((async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://127.0.0.1')
    if (url.pathname === '/api/entity/source') {
      return Response.json(source())
    }

    if (url.pathname === '/api/entity/image') {
      expect(init?.method).toBe('POST')
      const upload = { query: Object.fromEntries(url.searchParams), contentType: new Headers(init?.headers).get('Content-Type'), body: init?.body }
      uploads.push(upload)
      const result = answer(upload, uploads.length)
      return result instanceof Response ? result : Response.json(result, { status: 201 })
    }

    return base(input, init)
  }) as typeof fetch)
  return { ...stub, uploads }
}

function added(name: string, extra: Partial<AddedImage> = {}): AddedImage {
  const slug = name.toLowerCase().replace(/\.[^.]*$/, '').replace(/[^a-z0-9]+/g, '-')
  const ext = name.slice(name.lastIndexOf('.')).toLowerCase()
  return {
    version: 1,
    path: `universes/dracula/entities/characters/images/${slug}${ext}`,
    link: `images/${slug}${ext}`,
    alt: name.slice(0, name.lastIndexOf('.')).replace(/[-_]+/g, ' '),
    bytes: 4,
    effects: [],
    ...extra,
  }
}

function png(name = 'Castle Map.png', size = 8): File {
  return new File([new Uint8Array(size)], name, { type: 'image/png' })
}

async function openLeadEditor() {
  fireEvent.click(await screen.findByRole('button', { name: 'Edit the opening, the text before the sections' }))
  const group = await screen.findByRole('group', { name: 'Markdown of the opening' })
  await waitFor(() => expect(editorViewIn(group)).not.toBeNull())
  return editorViewIn(group)!
}

function pick(files: File[]) {
  const input = screen.getByLabelText('Image to add') as HTMLInputElement
  Object.defineProperty(input, 'files', { value: files, configurable: true })
  fireEvent.change(input)
}

describe('SC-WEB-80: I add a picture to a note while editing it', () => {
  it('Add image… sends the file and puts ![alt](path) in at the cursor', async () => {
    const { uploads } = stubImages()
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()
    act(() => view.dispatch({ selection: { anchor: '# Mina Harker\n\n'.length } }))

    expect(screen.getByRole('button', { name: 'Add image…' })).toBeTruthy()
    pick([png()])

    await screen.findByText(/Saved images\/castle-map\.png in the den/)
    expect(uploads).toHaveLength(1)
    expect(uploads[0].query).toEqual({ entity: 'E-MINA', universe: 'dracula', name: 'Castle Map.png' })
    expect(uploads[0].contentType).toBe('application/octet-stream')
    expect(uploads[0].body).toBeInstanceOf(File)
    expect(view.state.doc.toString()).toBe('# Mina Harker\n\n![Castle Map](images/castle-map.png)A schoolmistress.')
    expect(screen.getByText(/Saved images\/castle-map\.png/).textContent).toContain('It stays in the den even if you cancel.')

    // The note now differs, so Save is on: the image is in the note once it's saved.
    expect((screen.getByRole('button', { name: 'Save' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('puts several images on their own lines and says when a name was taken', async () => {
    stubImages((u, n) => (n === 2 ? added(u.query.name, { link: 'images/coach-2.jpg', effects: ["The note's images folder already has coach.jpg, so the image was saved as coach-2.jpg."] }) : added(u.query.name)))
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()
    act(() => view.dispatch({ selection: { anchor: view.state.doc.length } }))

    pick([png('seal.png'), new File([new Uint8Array(4)], 'coach.jpg', { type: 'image/jpeg' })])

    await screen.findByText(/already has coach\.jpg/)
    expect(view.state.doc.toString()).toBe('# Mina Harker\n\nA schoolmistress.![seal](images/seal.png)\n![coach](images/coach-2.jpg)')
  })

  it('adds a pasted image, and leaves pasted text to the editor', async () => {
    const { uploads } = stubImages()
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()
    act(() => view.dispatch({ selection: { anchor: 0 } }))

    const paste = new Event('paste', { bubbles: true, cancelable: true })
    Object.defineProperty(paste, 'clipboardData', { value: { files: [png('image.png')], getData: () => '', types: ['Files'] } })
    act(() => {
      view.contentDOM.dispatchEvent(paste)
    })

    await screen.findByText(/Saved images\/image\.png/)
    expect(paste.defaultPrevented).toBe(true)
    expect(uploads).toHaveLength(1)
    expect(view.state.doc.toString().startsWith('![image](images/image.png)# Mina Harker')).toBe(true)

    // Text only: not ours.
    const text = new Event('paste', { bubbles: true, cancelable: true })
    Object.defineProperty(text, 'clipboardData', { value: { files: [], getData: () => 'words', types: ['text/plain'] } })
    act(() => {
      view.contentDOM.dispatchEvent(text)
    })
    expect(uploads).toHaveLength(1)
  })

  it('adds a dropped image', async () => {
    const { uploads } = stubImages()
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()

    const drop = new Event('drop', { bubbles: true, cancelable: true })
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [png('ship.webp')], types: ['Files'] } })
    act(() => {
      view.contentDOM.dispatchEvent(drop)
    })

    await screen.findByText(/Saved images\/ship\.webp/)
    expect(drop.defaultPrevented).toBe(true)
    expect(uploads.map((u) => u.query.name)).toEqual(['ship.webp'])
    expect(view.state.doc.toString()).toContain('![ship](images/ship.webp)')
  })
})

describe('SC-WEB-81: adding an image is guarded', () => {
  it('refuses a file that isn’t an image type, or is too large, without sending it', async () => {
    const { uploads } = stubImages()
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()
    const before = view.state.doc.toString()

    pick([new File(['x'], 'notes.txt', { type: 'text/plain' }), png('huge.png', 10 * 1024 * 1024 + 1)])

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain("These images weren't added")
    expect(alert.textContent).toContain('notes.txt: Wolfden adds .png, .jpg, .jpeg, .gif, .webp, .avif, .svg files, not .txt.')
    expect(alert.textContent).toContain("huge.png: It's 10.0 MB, and Wolfden adds images of up to 10 MB.")
    expect(uploads).toEqual([])
    expect(view.state.doc.toString()).toBe(before)
  })

  it('shows the server’s refusal, such as a frozen book, and changes nothing', async () => {
    stubImages(() => Response.json(
      { code: 'frozen', message: "Book 'dracula' is final (frozen). Unfreeze… on the page of book 'dracula' opens it for editing again." },
      { status: 409 },
    ))
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()
    const before = view.state.doc.toString()

    pick([png()])

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain("This image wasn't added")
    expect(alert.textContent).toContain('Castle Map.png: Book \'dracula\' is final (frozen). Unfreeze…')
    expect(view.state.doc.toString()).toBe(before)
  })
})

describe('imageApi', () => {
  it('builds the Markdown, and tells images from other files', () => {
    expect(imageMarkdown({ alt: 'Map of the castle', link: 'images/castle-map.png' })).toBe('![Map of the castle](images/castle-map.png)')
    expect(looksLikeImage(new File([], 'MAP.PNG'))).toBe(true)
    expect(looksLikeImage(new File([], 'scan', { type: 'image/png' }))).toBe(true)
    expect(looksLikeImage(new File([], 'chapter.md', { type: 'text/markdown' }))).toBe(false)
    expect(imageProblem(new File([], 'MAP.PNG'))).toBeNull()
    expect(imageProblem(new File([], 'scan'))).toContain('Wolfden adds')
  })
})

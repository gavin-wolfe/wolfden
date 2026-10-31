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

/** Holds the first upload until `hold`'s resolve is called; the rest go straight through. */
function holdFirstUpload(hold: (resolve: () => void) => void) {
  let held = false
  const fetchMock = vi.mocked(fetch)
  const answer = fetchMock.getMockImplementation()!
  fetchMock.mockImplementation((async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://127.0.0.1')
    if (url.pathname === '/api/entity/image' && !held) {
      held = true
      await new Promise<void>((resolve) => hold(resolve))
    }

    return answer(input, init)
  }) as typeof fetch)
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

  it('pastes the text, not its picture, when the clipboard has both (as Word, Excel, and OneNote put there)', async () => {
    const { uploads } = stubImages()
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()
    act(() => view.dispatch({ selection: { anchor: view.state.doc.length } }))

    const paste = new Event('paste', { bubbles: true, cancelable: true })
    const data: Record<string, string> = { 'text/plain': ' Two paragraphs from the manuscript.', 'text/html': '<p>Two paragraphs from the manuscript.</p>' }
    Object.defineProperty(paste, 'clipboardData', {
      value: { files: [png('image.png')], getData: (type: string) => data[type] ?? '', types: ['text/plain', 'text/html', 'Files'] },
    })
    act(() => {
      view.contentDOM.dispatchEvent(paste)
    })

    await waitFor(() => expect(view.state.doc.toString()).toContain('Two paragraphs from the manuscript.'))
    expect(uploads).toEqual([])
    expect(view.state.doc.toString()).not.toContain('![')
  })

  it('refuses more images dropped or pasted while one is being added, as the button does', async () => {
    let finish: (() => void) | null = null
    const { uploads } = stubImages()
    const fetchMock = vi.mocked(fetch)
    const answer = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation((async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://127.0.0.1')
      if (url.pathname === '/api/entity/image' && finish === null) {
        await new Promise<void>((resolve) => (finish = resolve))
      }

      return answer(input, init)
    }) as typeof fetch)
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()

    pick([png('first.png')])
    await screen.findByText('Adding first.png…')
    expect((screen.getByRole('button', { name: 'Add image…' }) as HTMLButtonElement).disabled).toBe(true)

    const paste = new Event('paste', { bubbles: true, cancelable: true })
    Object.defineProperty(paste, 'clipboardData', { value: { files: [png('second.png')], getData: () => '', types: ['Files'] } })
    act(() => {
      view.contentDOM.dispatchEvent(paste)
    })
    expect(paste.defaultPrevented).toBe(true)
    await screen.findByText(/One batch of images at a time/)

    act(() => finish!())
    await screen.findByText(/Saved images\/first\.png/)
    expect(uploads.map((u) => u.query.name)).toEqual(['first.png'])
    expect(view.state.doc.toString()).not.toContain('second')
  })

  it('leaves the cursor where it was when a drop is refused, so the batch in progress goes in there', async () => {
    let finish: (() => void) | null = null
    const { uploads } = stubImages()
    holdFirstUpload((resolve) => (finish = resolve))
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()
    // jsdom has no layout, so say where a drop lands.
    view.posAtCoords = () => 0
    act(() => view.dispatch({ selection: { anchor: view.state.doc.length } }))

    pick([png('first.png')])
    await screen.findByText('Adding first.png…')

    const drop = new Event('drop', { bubbles: true, cancelable: true })
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [png('second.png')], types: ['Files'] } })
    act(() => {
      view.contentDOM.dispatchEvent(drop)
    })
    expect(drop.defaultPrevented).toBe(true)
    await screen.findByText(/One batch of images at a time/)
    expect(view.state.selection.main.head).toBe(view.state.doc.length)

    act(() => finish!())
    await screen.findByText(/Saved images\/first\.png/)
    expect(uploads.map((u) => u.query.name)).toEqual(['first.png'])
    expect(view.state.doc.toString()).toBe('# Mina Harker\n\nA schoolmistress.![first](images/first.png)')

    // Once it's in, a drop goes where it's dropped.
    const again = new Event('drop', { bubbles: true, cancelable: true })
    Object.defineProperty(again, 'dataTransfer', { value: { files: [png('second.png')], types: ['Files'] } })
    act(() => {
      view.contentDOM.dispatchEvent(again)
    })
    await screen.findByText(/Saved images\/second\.png/)
    expect(view.state.doc.toString().startsWith('![second](images/second.png)# Mina Harker')).toBe(true)
  })

  it('turns Save and Cancel off while an image is being added, and Ctrl+Enter and Escape wait', async () => {
    let finish: (() => void) | null = null
    stubImages()
    holdFirstUpload((resolve) => (finish = resolve))
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()
    act(() => view.dispatch({ changes: { from: view.state.doc.length, insert: ' Edited.' } }))
    const save = screen.getByRole('button', { name: 'Save' }) as HTMLButtonElement
    const cancel = screen.getByRole('button', { name: 'Cancel' }) as HTMLButtonElement
    expect(save.disabled).toBe(false)

    pick([png('first.png')])
    await screen.findByText('Adding first.png…')
    expect(save.disabled).toBe(true)
    expect(cancel.disabled).toBe(true)
    expect(screen.getByText('Save and Cancel come back once the image is added.')).toBeTruthy()

    act(() => {
      fireEvent.keyDown(view.contentDOM, { key: 'Escape' })
    })
    await screen.findByText(/Save and Cancel wait until the image is added\./)
    expect(screen.getByRole('group', { name: 'Markdown of the opening' })).toBeTruthy()

    act(() => finish!())
    await screen.findByText(/Saved images\/first\.png/)
    expect(save.disabled).toBe(false)
    expect(cancel.disabled).toBe(false)
    expect(view.state.doc.toString()).toBe('![first](images/first.png)# Mina Harker\n\nA schoolmistress. Edited.')
    expect(screen.getByText('Ctrl+Enter (⌘+Enter) saves, Escape cancels.')).toBeTruthy()
  })

  it('says which dropped files it left out because they aren’t images', async () => {
    const { uploads } = stubImages()
    renderApp('/u/dracula/e/E-MINA')
    const view = await openLeadEditor()

    const drop = new Event('drop', { bubbles: true, cancelable: true })
    Object.defineProperty(drop, 'dataTransfer', {
      value: { files: [png('map.png'), new File(['x'], 'chapter-notes.txt', { type: 'text/plain' })], types: ['Files'] },
    })
    act(() => {
      view.contentDOM.dispatchEvent(drop)
    })

    const status = await screen.findByText(/Saved images\/map\.png/)
    expect(status.textContent).toContain("Not added, since it isn't an image: chapter-notes.txt.")
    expect(uploads.map((u) => u.query.name)).toEqual(['map.png'])
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

import { useEffect, useRef, useState } from 'react'
import { EditorState, Prec } from '@codemirror/state'
import { EditorView, keymap, placeholder as placeholderText } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { defaultHighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { autocompletion, type Completion, type CompletionContext, type CompletionResult } from '@codemirror/autocomplete'
import { useQuery } from '@tanstack/react-query'
import { entitiesQuery } from '../api'
import { linkFor } from '../fieldModel'
import { lookupable, lookupEntities, matchLabel, noSureLink, type LookupResult } from '../lookupApi'
import { imageExtensions, imageMarkdown, imageProblem, looksLikeImage, type AddedImage } from '../imageApi'

/**
 * A Markdown source editor (#28): CodeMirror 6, with Markdown highlighting
 * and `[[` autocomplete of notes by name or alias (`noteLinks`). Ctrl+Enter (⌘+Enter
 * on a Mac) saves and Escape cancels. Tab is left to move focus, so the
 * editor is never a keyboard trap; Ctrl+] and Ctrl+[ (⌘ on a Mac) indent.
 *
 * It's mounted in a shadow root of its own. CodeMirror styles itself with
 * a style sheet it adds at run time: in a page that would be an inline
 * `<style>`, which the page's Content-Security-Policy (`style-src 'self'`,
 * #180) refuses, but in a shadow root it's a constructed style sheet, which
 * the policy doesn't govern. The page's colours reach it as CSS variables.
 *
 * With `addImage` (#187), an image picked with Add image…, dropped on the
 * editor, or pasted into it is sent to be saved in the den, and
 * `![alt](path)` goes in at the cursor (or where it was dropped). Dropped or
 * pasted files that aren't images are left to CodeMirror. While images are
 * being added, Ctrl+Enter and Escape wait (saying so), and `onAddingImages`
 * tells the page, so it can turn its own Save and Cancel off: a save then
 * would leave the image out of the text.
 */
export function MarkdownEditor({
  value,
  onChange,
  onSave,
  onCancel,
  label,
  universe,
  autoFocus = false,
  minLines = 4,
  addImage,
  onAddingImages,
}: {
  value: string
  onChange: (text: string) => void
  onSave: () => void
  onCancel: () => void
  label: string
  universe: string
  autoFocus?: boolean
  minLines?: number
  /** Saves an image in the den for this note; without it, the editor doesn't add images. */
  addImage?: (file: File) => Promise<AddedImage>
  /** Called with true when images start being added, and false once they're in (or failed). */
  onAddingImages?: (adding: boolean) => void
}) {
  const host = useRef<HTMLDivElement>(null)
  const view = useRef<EditorView | null>(null)
  // The latest callbacks, so the editor made once always calls them.
  const handlers = useRef({ onChange, onSave, onCancel, addImage, onAddingImages })
  handlers.current = { onChange, onSave, onCancel, addImage, onAddingImages }
  const [imageStatus, setImageStatus] = useState<ImageStatus | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  // True while images are being added. More dropped or pasted then are refused, as the button is off.
  const busy = useRef(false)

  // Says why something waits until the images in progress are added.
  const wait = (why: string) =>
    setImageStatus((now) => ({ ...(now ?? { text: '' }), busy: true, waiting: why }))

  // Ctrl+Enter and Escape while images are being added: they wait, so the image isn't left out of the text.
  const unlessAdding = (action: () => void) => () => {
    if (busy.current) {
      wait('Save and Cancel wait until the image is added.')
    } else {
      action()
    }

    return true
  }

  // Adds the files one at a time, each at the cursor, and says what happened.
  // `skipped` names dropped files that aren't images, which aren't added.
  const addFiles = useRef(async (files: File[], skipped: string[] = []) => {
    const add = handlers.current.addImage
    const editor = view.current
    if (!add || !editor || (files.length === 0 && skipped.length === 0)) {
      return
    }

    if (busy.current) {
      wait('One batch of images at a time: wait until this one is added, then add the others again.')
      return
    }

    busy.current = true
    handlers.current.onAddingImages?.(true)
    const saved: string[] = []
    const failed: string[] = []
    const notes: string[] = []
    try {
      for (const file of files) {
        const problem = imageProblem(file)
        if (problem) {
          failed.push(`${file.name}: ${problem}`)
          continue
        }

        setImageStatus({ busy: true, text: `Adding ${file.name}…` })
        try {
          const image = await add(file)
          const current = view.current
          if (current) {
            // Each on its own line after the first, so several don't run together.
            const text = (saved.length > 0 ? '\n' : '') + imageMarkdown(image)
            current.dispatch(current.state.replaceSelection(text))
          }

          saved.push(image.link)
          notes.push(...image.effects)
        } catch (e) {
          failed.push(`${file.name}: ${(e as Error).message}`)
        }
      }
    } finally {
      busy.current = false
      handlers.current.onAddingImages?.(false)
    }

    const parts: string[] = []
    if (saved.length > 0) {
      parts.push(`Saved ${saved.join(', ')} in the den and added ${saved.length === 1 ? 'it' : 'them'} to the text. ${saved.length === 1 ? 'It stays' : 'They stay'} in the den even if you cancel.`)
    }

    if (skipped.length > 0) {
      parts.push(`Not added, since ${skipped.length === 1 ? "it isn't an image" : "they aren't images"}: ${skipped.join(', ')}.`)
    }

    setImageStatus({ busy: false, text: parts.join(' '), notes, failed })
    view.current?.focus()
  }).current
  // Every note of the universe, to tell whether a slug reaches only the note picked.
  const all = useQuery(entitiesQuery(universe, undefined))
  const others = useRef<readonly { path: string; name?: string }[] | null>(null)
  others.current = all.data && !all.data.truncated ? all.data.entities : null

  useEffect(() => {
    const element = host.current!
    const root = element.shadowRoot ?? element.attachShadow({ mode: 'open' })
    const editor = new EditorView({
      root,
      parent: root,
      state: EditorState.create({
        doc: value,
        extensions: [
          history(),
          markdown(),
          syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
          autocompletion({ override: [noteLinks(universe, () => others.current)], icons: false }),
          EditorView.lineWrapping,
          placeholderText('Write in Markdown…'),
          keymap.of([
            { key: 'Mod-Enter', run: unlessAdding(() => handlers.current.onSave()) },
            { key: 'Escape', run: unlessAdding(() => handlers.current.onCancel()) },
            ...historyKeymap,
            ...defaultKeymap.filter((k) => k.key !== 'Mod-Enter'),
          ]),
          EditorView.contentAttributes.of({ 'aria-label': label, 'aria-multiline': 'true' }),
          EditorView.updateListener.of((update) => {
            if (update.docChanged) {
              handlers.current.onChange(update.state.doc.toString())
            }
          }),
          editorTheme(minLines),
          Prec.highest(EditorView.domEventHandlers({
            drop: (event, editor) => {
              const dropped = [...(event.dataTransfer?.files ?? [])]
              const files = dropped.filter(looksLikeImage)
              if (!handlers.current.addImage || files.length === 0) {
                return false
              }

              event.preventDefault()
              // Where it was dropped, if the editor can tell (it measures the layout); else at the cursor.
              // Not while a batch is being added: this drop is refused, and the cursor
              // stays where the rest of that batch is going in.
              let at: number | null = null
              try {
                at = busy.current ? null : editor.posAtCoords({ x: event.clientX, y: event.clientY })
              } catch {
                at = null
              }

              if (at !== null) {
                editor.dispatch({ selection: { anchor: at } })
              }

              void addFiles(files, dropped.filter((f) => !looksLikeImage(f)).map((f) => f.name))
              return true
            },
            paste: (event) => {
              const clipboard = event.clipboardData
              const files = imagesIn(clipboard?.files)
              // Text copied from Word, Excel, or OneNote comes with a picture of it as
              // well: when there's text, it's the text that's pasted. A screenshot has none.
              if (!handlers.current.addImage || files.length === 0 || (clipboard?.getData('text/plain') ?? '').trim() !== '') {
                return false
              }

              event.preventDefault()
              void addFiles(files)
              return true
            },
          })),
        ],
      }),
    })
    view.current = editor
    if (autoFocus) {
      editor.focus()
    }

    return () => {
      editor.destroy()
      view.current = null
    }
    // The editor is made once; a new `value` changes it below.
  }, [])

  // A new value from outside (such as the current file after a conflict) replaces the text.
  useEffect(() => {
    const editor = view.current
    if (editor && editor.state.doc.toString() !== value) {
      editor.dispatch({ changes: { from: 0, to: editor.state.doc.length, insert: value } })
    }
  }, [value])

  const editorElement = <div ref={host} className="markdown-editor" role="group" aria-label={label} data-testid="markdown-editor" />
  if (!addImage) {
    return editorElement
  }

  return (
    <>
      {editorElement}
      <div className="editor-images">
        <button type="button" onClick={() => fileInput.current?.click()} disabled={imageStatus?.busy === true}>
          Add image…
        </button>
        <input
          ref={fileInput}
          type="file"
          accept={imageExtensions.join(',')}
          multiple
          hidden
          aria-label="Image to add"
          onChange={(e) => {
            const files = [...(e.target.files ?? [])]
            e.target.value = ''
            void addFiles(files)
          }}
        />
        <span className="muted">or drop or paste one into the text. It's saved in an images folder beside the note.</span>
      </div>
      {imageStatus && (imageStatus.busy || imageStatus.text) && (
        <p className="editor-image-status" role="status">
          {imageStatus.waiting ? `${imageStatus.waiting} ` : ''}
          {imageStatus.text}
          {imageStatus.notes && imageStatus.notes.length > 0 ? ` ${imageStatus.notes.join(' ')}` : ''}
        </p>
      )}
      {imageStatus && !imageStatus.busy && imageStatus.failed && imageStatus.failed.length > 0 && (
        <div className="error" role="alert">
          <p>{imageStatus.failed.length === 1 ? "This image wasn't added:" : "These images weren't added:"}</p>
          <ul>
            {imageStatus.failed.map((f) => (
              <li key={f}>{f}</li>
            ))}
          </ul>
        </div>
      )}
    </>
  )
}

/** What adding images is doing, or did. */
interface ImageStatus {
  busy: boolean
  text: string
  /** Why more images dropped or pasted meanwhile weren't added. */
  waiting?: string
  notes?: string[]
  failed?: string[]
}

/** The files in a drop or paste that look like images. */
function imagesIn(files: FileList | null | undefined): File[] {
  return files ? [...files].filter(looksLikeImage) : []
}

/** The EditorView in an editor's element, for tests and for moving focus into it. */
export function editorViewIn(element: Element | null): EditorView | null {
  const dom = element?.shadowRoot?.querySelector('.cm-editor')
  return dom instanceof HTMLElement ? EditorView.findFromDOM(dom) : null
}

/**
 * `[[` autocomplete: after `[[` and at least one letter, the notes whose
 * name, alias, or file name has a word starting with each word typed, from
 * the lookup (#198), whatever the story position (#28). Prose stays
 * readable: a pick is linked by the text it matched, `[[Mina Harker]]` for a
 * name and `[[Madam Mina]]` for an alias (shown "as Madam Mina"), where the
 * lookup says that reaches only this note (`linkText`; for an alias that
 * doesn't, the name if that does). Otherwise it's linked as the field
 * pickers link it (`linkFor`): `[[slug]]`, or `[[id]]`. A note no link is
 * sure to reach is offered with the field picker's hint (add an id) and
 * inserts nothing, since a link by its name could quietly reach another note.
 * `others` gives the universe's notes when they're known, null if not.
 */

export function noteLinks(universe: string, others: () => readonly { path: string; name?: string }[] | null) {
  return async (context: CompletionContext): Promise<CompletionResult | null> => {
    const before = context.matchBefore(/\[\[[^[\]|#\n]*$/)
    if (!before) {
      return null
    }

    const words = before.text.slice(2).trim()
    if (!lookupable(words)) {
      return null
    }

    let result: LookupResult
    try {
      result = await lookupEntities(universe, words, { limit: 20 })
    } catch {
      return null
    }

    if (context.aborted) {
      return null
    }

    const closed = context.state.sliceDoc(context.pos, context.pos + 2) === ']]'
    const notes = others()
    const options = result.matches.map((hit): Completion => {
      const { name, alias } = matchLabel(hit)
      const detail = alias !== null ? `as ${alias} · ${hit.entity.type}` : hit.entity.type
      const text = hit.linkText ?? linkFor(hit.entity, notes)?.slice(2, -2) ?? null
      if (text === null) {
        // No link is sure to reach it: never write one that may reach another note. Picking it leaves the text as typed.
        return { label: name, detail: `${detail} · no sure link`, info: noSureLink(name), apply: () => {} }
      }

      return { label: name, detail, apply: closed ? text : `${text}]]` }
    })
    return { from: before.from + 2, options, filter: false }
  }
}

/** The editor's look, from the page's own colours and fonts. */
function editorTheme(minLines: number) {
  return EditorView.theme({
    '&': {
      color: 'var(--text)',
      backgroundColor: 'var(--bg-raised)',
      border: '1px solid var(--border)',
      borderRadius: '4px',
      fontSize: '0.95rem',
    },
    '&.cm-focused': { outline: '2px solid var(--focus)', outlineOffset: '1px' },
    '.cm-content': {
      fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
      minHeight: `${minLines * 1.5}em`,
      caretColor: 'var(--text)',
      padding: '0.5rem 0',
    },
    '.cm-line': { padding: '0 0.6rem' },
    '.cm-cursor': { borderLeftColor: 'var(--text)' },
    '.cm-placeholder': { color: 'var(--text-muted)' },
    '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': { backgroundColor: 'var(--mark-bg)' },
    '.cm-tooltip': { backgroundColor: 'var(--bg-raised)', border: '1px solid var(--border)', color: 'var(--text)' },
    '.cm-tooltip-autocomplete > ul > li[aria-selected]': { backgroundColor: 'var(--accent)', color: 'var(--accent-text)' },
  })
}

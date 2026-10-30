import { useEffect, useRef } from 'react'
import { EditorState } from '@codemirror/state'
import { EditorView, keymap, placeholder as placeholderText } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { defaultHighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { autocompletion, type Completion, type CompletionContext, type CompletionResult } from '@codemirror/autocomplete'
import { useQuery } from '@tanstack/react-query'
import { entitiesQuery } from '../api'
import { linkFor } from '../fieldModel'
import { lookupable, lookupEntities, matchLabel, noSureLink, type LookupResult } from '../lookupApi'

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
}: {
  value: string
  onChange: (text: string) => void
  onSave: () => void
  onCancel: () => void
  label: string
  universe: string
  autoFocus?: boolean
  minLines?: number
}) {
  const host = useRef<HTMLDivElement>(null)
  const view = useRef<EditorView | null>(null)
  // The latest callbacks, so the editor made once always calls them.
  const handlers = useRef({ onChange, onSave, onCancel })
  handlers.current = { onChange, onSave, onCancel }
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
            { key: 'Mod-Enter', run: () => (handlers.current.onSave(), true) },
            { key: 'Escape', run: () => (handlers.current.onCancel(), true) },
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

  return <div ref={host} className="markdown-editor" role="group" aria-label={label} data-testid="markdown-editor" />
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

import { useCallback, useEffect, useId, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, getRouteApi, useBlocker, useNavigate } from '@tanstack/react-router'
import { Conflict, LeftOutFlag, MarkdownEditor, SaveError, SaveResult, UnsavedPrompt, useAfterSave } from '../components/Editing'
import { ErrorState, Loading } from '../components/States'
import { FileEditError, fileSourceQuery, saveFile, type FileSource } from '../denErrorsApi'
import { useHoldRefresh } from '../formHold'
import { useDocumentTitle } from '../hooks'
import { IfCanWrite, useAccess } from '../access'
import type { WriteResult } from '../editApi'
import '../denErrors.css'

const route = getRouteApi('/file')

/**
 * Edit file by its path (#280): where the list of problems sends the writer,
 * for any file with an error, even one the den leaves out, which has no page.
 * It shows everything, whatever the story position. A save is checked as the
 * den would read the file, and carries the hash it was based on.
 */
export function FilePage() {
  const { path = '' } = route.useSearch()
  useDocumentTitle('Edit file')
  // Edit file is a writer's, and den.yaml the owner's (#315 §4.6); the server refuses the rest.
  const { role, canWrite, isOwner } = useAccess()
  if (role !== null && !(path === 'den.yaml' ? isOwner : canWrite)) {
    return (
      <div className="page-body file-page">
        <h1>Edit file</h1>
        <p role="note">
          {canWrite ? 'Only an owner can change den.yaml.' : 'Your account is a viewer: it can read this den but not change it.'}
        </p>
      </div>
    )
  }

  return <FileEditor key={path} path={path} />
}

function FileEditor({ path }: { path: string }) {
  const source = useQuery({ ...fileSourceQuery(path), refetchOnMount: 'always' })
  const fresh = source.isFetchedAfterMount && !source.isFetching
  const [base, setBase] = useState<FileSource | null>(null)
  const [text, setText] = useState('')
  useEffect(() => {
    if (fresh && source.data && base === null) {
      setBase(source.data)
      setText(source.data.text ?? '')
    }
  }, [fresh, source.data, base])

  // While the editor is open the page doesn't refresh itself, as with the other
  // editors; a read-only page does. And nothing a refetch answers can replace an open
  // editor: a stale hash at Save is how the writer learns the file changed.
  useHoldRefresh(base !== null && !base.frozen && base.text !== null)

  if (base === null) {
    return source.isError ? (
      <div className="page-body file-page">
        <h1>Edit file</h1>
        <ErrorState error={source.error} />
      </div>
    ) : (
      <Loading what="the file" />
    )
  }

  return <Editor path={path} base={base} setBase={setBase} text={text} setText={setText} />
}

function Editor({
  path,
  base,
  setBase,
  text,
  setText,
}: {
  path: string
  base: FileSource
  setBase: (source: FileSource) => void
  text: string
  setText: (text: string) => void
}) {
  const client = useQueryClient()
  const afterSave = useAfterSave()
  const navigate = useNavigate()
  const labelId = useId()
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<Error | null>(null)
  const [conflict, setConflict] = useState<FileSource | null>(null)
  const [saved, setSaved] = useState<WriteResult | null>(null)
  const dirty = base.text !== null && text !== base.text

  const shouldBlockFn = useCallback(() => dirty, [dirty])
  const blocker = useBlocker({ shouldBlockFn, enableBeforeUnload: false, withResolver: true })
  useEffect(() => {
    if (!dirty) {
      return
    }

    const warn = (e: BeforeUnloadEvent) => {
      e.preventDefault()
      e.returnValue = ''
    }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const reload = async () => {
    const now = await client.fetchQuery(fileSourceQuery(path))
    setBase(now)
    setText(now.text ?? '')
  }

  const save = async (hash: string = base.hash) => {
    if (saving || (!dirty && hash === base.hash)) {
      return
    }

    setSaving(true)
    setError(null)
    setSaved(null)
    try {
      const result = await saveFile(path, text, hash)
      setConflict(null)
      setSaved(result)
      await afterSave()
      try {
        await reload()
      } catch {
        // It's saved, but can't be read back now: what the save said stands, so the next save is based on it.
        setBase({
          ...base,
          hash: result.hash,
          text,
          errors: result.warnings.filter((w) => w.severity === 'error').map((w) => ({ line: w.line, rule: w.rule, message: w.message, reach: 'file', section: null, sectionLine: null })),
        })
      }
    } catch (e) {
      if (e instanceof FileEditError && e.code === 'stale_file' && e.currentFile) {
        setConflict(e.currentFile)
      } else {
        setError(e as Error)
      }
    } finally {
      setSaving(false)
    }
  }

  const goBack = () => {
    const entity = saved?.entity ?? base.entity
    void (entity
      ? navigate({ to: '/u/$universe/e/$', params: { universe: entity.universe, _splat: entity.key } })
      : navigate({ to: '/' }))
  }

  const leavePrompt = blocker.status === 'blocked' && (
    <UnsavedPrompt
      question="You have changes that aren't saved. Leave without saving them?"
      discard="Leave without saving"
      onKeep={() => blocker.reset()}
      onDiscard={() => blocker.proceed()}
    />
  )

  if (conflict) {
    return (
      <div className="page-body file-page">
        <h1>Edit file</h1>
        <p className="file-page-path">
          <code>{path}</code>
        </p>
        {leavePrompt}
        <Conflict
          title="The file changed while you were editing it"
          what="the file"
          mine={text}
          theirs={conflict.text}
          saving={saving}
          onKeepMine={() => {
            setBase(conflict)
            void save(conflict.hash)
          }}
          onTakeTheirs={() => {
            setBase(conflict)
            setText(conflict.text ?? '')
            setConflict(null)
          }}
        />
      </div>
    )
  }

  return (
    <div className="page-body file-page">
      <h1>Edit file</h1>
      <p className="file-page-path">
        <code>{path}</code>
        {base.entity && (
          <>
            {' · '}
            <Link to="/u/$universe/e/$" params={{ universe: base.entity.universe, _splat: base.entity.key }}>
              Open the note
            </Link>
          </>
        )}
      </p>
      {leavePrompt}
      {saved && <SaveResult saved={{ what: 'the file', result: saved }} onDismiss={() => setSaved(null)} />}
      {base.errors.length > 0 ? (
        <LeftOutFlag problems={base.errors} hint={base.frozen || base.text === null ? '' : 'Fix it here and save. You can fix one error at a time.'} />
      ) : (
        <p role="status">This file has no errors now.</p>
      )}
      {base.text === null ? (
        <p className="error" role="alert">
          This file isn’t valid UTF-8 text, so it can’t be edited here. Open it in your own editor and save it as UTF-8.
        </p>
      ) : base.frozen ? (
        <>
          <p className="error" role="alert">
            {base.frozen}
          </p>
          <pre className="file-page-text">{base.text}</pre>
        </>
      ) : (
        <div className="editor-panel">
          <div className="editor-toolbar">
            <span id={labelId} className="editor-label">
              Editing the whole file, frontmatter included
            </span>
          </div>
          <MarkdownEditor value={text} onChange={setText} onSave={() => void save()} onCancel={goBack} label={`Text of ${path}`} universe={base.universe ?? ''} autoFocus minLines={20} />
          {error && <SaveError error={error} />}
          <p className="editor-actions">
            <IfCanWrite>
            <button type="button" className="primary" onClick={() => void save()} disabled={saving || !dirty}>
              {saving ? 'Saving…' : 'Save'}
            </button>
            </IfCanWrite>
            <button type="button" onClick={goBack} disabled={saving}>
              Close
            </button>
            <span className="muted">Ctrl+Enter (⌘+Enter) saves.</span>
          </p>
        </div>
      )}
    </div>
  )
}

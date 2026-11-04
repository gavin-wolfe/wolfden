// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import {
  CHECKPOINT_STATUS_KEY,
  groupChanges,
  invalidateCheckpointStatus,
  type CheckpointMade,
  type CheckpointStatus,
  type HistoryTurnedOn,
  type PendingChange,
  type TurnOnRequest,
  type TurnOnView,
} from './checkpointApi'
import { renderApp, stubApi } from './test/harness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const MINA = 'universes/dracula/books/dracula/entities/characters/mina-harker.md'
const LUCY = 'universes/dracula/books/dracula/entities/characters/lucy-westenra.md'
const KRONE = 'universes/dracula/entities/locations/golden-krone.md'

function change(path: string, kind: PendingChange['kind'] = 'modified', savedHere = false): PendingChange {
  return { kind, path, oldPath: null, universe: path.startsWith('universes/') ? path.split('/')[1] : null, savedHere }
}

function status(extra: Partial<CheckpointStatus> = {}): CheckpointStatus {
  const changes = extra.changes ?? [change(MINA, 'modified', true), change(LUCY), change(KRONE, 'added')]
  return {
    version: 1,
    backend: 'git',
    available: true,
    reason: null,
    enable: null,
    latest: { id: 'c1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', when: '2026-10-26T20:00:00Z' },
    inProgress: null,
    count: changes.length + (extra.otherUniverses ?? 0),
    changes,
    otherUniverses: 0,
    autoCheckpoint: { every: null, problem: null, runs: true },
    ...extra,
  }
}

const NO_HISTORY: CheckpointStatus = {
  ...status({ changes: [] }),
  backend: 'none',
  available: false,
  reason: "This den has no history, so a checkpoint isn't available. Your edits are still saved in the files.",
  enable: "To keep history, turn it on: with Turn on history in the checkpoint dialog, or by running 'wolfden init --git' in the den's folder.",
  latest: null,
  count: 0,
}

/** Answers /api/checkpoint with `current` for GET, and `post` for POST; records POST bodies. */
function stubCheckpoints(current: () => CheckpointStatus, post?: (body: { message: string }) => Response | CheckpointMade) {
  const posted: { message: string }[] = []
  const api = stubApi({
    override: (path, _params, init) => {
      if (path !== '/api/checkpoint') {
        return undefined
      }

      if (init?.method === 'POST') {
        expect(new Headers(init.headers).get('Content-Type')).toBe('application/json')
        const body = JSON.parse(String(init.body)) as { message: string }
        posted.push(body)
        return post?.(body)
      }

      return current()
    },
  })
  return { ...api, posted }
}

describe('SC-WEB-50: the top bar says what changed since the last checkpoint', () => {
  it('is hidden when nothing has changed', async () => {
    const { calls } = stubApi()
    renderApp('/u/dracula')
    await screen.findByRole('heading', { name: 'Dracula', level: 1 })
    await waitFor(() => expect(calls).toContain('/api/checkpoint'))
    expect(screen.queryByRole('button', { name: /checkpoint/i })).toBeNull()
  })

  it('counts the changed notes and opens a dialog that groups them for review', async () => {
    stubCheckpoints(() => status())
    renderApp('/u/dracula')

    const indicator = await screen.findByRole('button', { name: /3 notes changed since the last checkpoint · Checkpoint…/ })
    expect(indicator.getAttribute('aria-haspopup')).toBe('dialog')
    fireEvent.click(indicator)

    const dialog = await screen.findByRole('dialog', { name: 'Make a checkpoint' })
    expect(dialog.getAttribute('aria-modal')).toBe('true')

    // Grouped by universe, then by the book the notes are filed under, paths shortened within the group.
    const groups = within(dialog).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)
    expect(groups).toEqual(['Universe dracula', 'Universe dracula · Book dracula'])
    expect(within(dialog).getByText('entities/locations/golden-krone.md')).toBeTruthy()
    expect(within(dialog).getByText('entities/characters/mina-harker.md')).toBeTruthy()

    // A note saved through Wolfden is marked; the others changed another way.
    const mina = within(dialog).getByText('entities/characters/mina-harker.md').closest('li')!
    expect(within(mina).getByText('saved in Wolfden')).toBeTruthy()
    const lucy = within(dialog).getByText('entities/characters/lucy-westenra.md').closest('li')!
    expect(within(lucy).queryByText('saved in Wolfden')).toBeNull()
    expect(within(dialog).getByText(/changed another way/)).toBeTruthy()

    // The message box has focus, and no message is made up for the writer.
    const box = within(dialog).getByLabelText('Name this checkpoint') as HTMLTextAreaElement
    await waitFor(() => expect(document.activeElement).toBe(box))
    expect(box.value).toBe('')
  })

  it('says how many notes are new before the first checkpoint', async () => {
    stubCheckpoints(() => status({ latest: null }))
    renderApp('/u/dracula')
    expect(await screen.findByRole('button', { name: /3 notes not checkpointed yet/ })).toBeTruthy()
  })

  it('counts, but doesn’t name, changes in universes a pinned server doesn’t show', async () => {
    stubCheckpoints(() => status({ changes: [change(MINA)], otherUniverses: 2 }))
    renderApp('/u/dracula')
    fireEvent.click(await screen.findByRole('button', { name: /3 notes changed/ }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/And 2 files in other universes/)).toBeTruthy()
  })

  it('looks again when the editor says it saved, and when the window gets focus back', async () => {
    let current = status({ changes: [] })
    const { calls } = stubCheckpoints(() => current)
    const { queryClient } = renderApp('/u/dracula')
    await waitFor(() => expect(calls.filter((c) => c === '/api/checkpoint')).toHaveLength(1))
    expect(screen.queryByRole('button', { name: /changed since/ })).toBeNull()

    current = status({ changes: [change(MINA)] })
    await invalidateCheckpointStatus(queryClient)
    expect(await screen.findByRole('button', { name: /1 note changed since the last checkpoint/ })).toBeTruthy()
    expect(queryClient.getQueryState(CHECKPOINT_STATUS_KEY)?.data).toBeDefined()
  })
})

describe('SC-WEB-52: I name a checkpoint after a notes pass', () => {
  it('needs a name, then makes the checkpoint and says so', async () => {
    let current = status()
    const { posted, calls } = stubCheckpoints(
      () => current,
      (body) => {
        const made = { id: 'c2bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', when: '2026-10-27T09:00:00Z', author: 'Test Writer', message: body.message, files: 3 }
        current = status({ changes: [], latest: { id: made.id, when: made.when } })
        return {
          version: 1,
          backend: 'git',
          recorded: true,
          checkpoint: made,
          inProgress: null,
          changes: [change(MINA), change(LUCY), change(KRONE, 'added')],
          otherUniverses: 0,
        }
      },
    )
    renderApp('/u/dracula')
    fireEvent.click(await screen.findByRole('button', { name: /3 notes changed/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Make a checkpoint' })

    // An empty name is refused here, before anything is sent.
    fireEvent.click(within(dialog).getByRole('button', { name: 'Checkpoint' }))
    expect(within(dialog).getByRole('alert').textContent).toMatch(/Give the checkpoint a name/)
    const box = within(dialog).getByLabelText('Name this checkpoint')
    expect(box.getAttribute('aria-invalid')).toBe('true')
    expect(posted).toEqual([])

    fireEvent.change(box, { target: { value: '  Notes pass after drafting ch 12 ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Checkpoint' }))

    const done = await screen.findByRole('dialog', { name: 'Checkpoint made' })
    expect(posted).toEqual([{ message: 'Notes pass after drafting ch 12' }])
    expect(within(done).getByRole('status').textContent).toMatch(/Recorded “Notes pass after drafting ch 12”, with 3 notes/)
    await waitFor(() => expect(document.activeElement).toBe(within(done).getByRole('button', { name: 'Close' })))

    // The indicator asked again, and is hidden now that nothing has changed.
    await waitFor(() => expect(calls.filter((c) => c === '/api/checkpoint').length).toBeGreaterThanOrEqual(3))
    fireEvent.click(within(done).getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(screen.queryByRole('button', { name: /changed since/ })).toBeNull()
  })

  it('shows why git refused, and keeps the name to try again', async () => {
    stubCheckpoints(
      () => status(),
      () =>
        new Response(
          JSON.stringify({
            code: 'history_failed',
            message: 'Git refused the checkpoint, so nothing was recorded (spell check failed). A pre-commit or commit-msg hook can do this.',
          }),
          { status: 500, headers: { 'Content-Type': 'application/json' } },
        ),
    )
    renderApp('/u/dracula')
    fireEvent.click(await screen.findByRole('button', { name: /3 notes changed/ }))
    const dialog = await screen.findByRole('dialog')
    const box = within(dialog).getByLabelText('Name this checkpoint') as HTMLTextAreaElement
    fireEvent.change(box, { target: { value: 'Planning session: harbour subplot' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Checkpoint' }))

    const alert = await within(dialog).findByRole('alert')
    expect(alert.textContent).toMatch(/Git refused the checkpoint.*hook/)
    expect(box.value).toBe('Planning session: harbour subplot')
    expect(within(dialog).getByRole('button', { name: 'Checkpoint' })).toBeTruthy()
  })

  it('says when there was nothing to record', async () => {
    stubCheckpoints(
      () => status(),
      () => ({ version: 1, backend: 'git', recorded: false, checkpoint: null, inProgress: null, changes: [], otherUniverses: 0 }),
    )
    renderApp('/u/dracula')
    fireEvent.click(await screen.findByRole('button', { name: /3 notes changed/ }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText('Name this checkpoint'), { target: { value: 'Again' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Checkpoint' }))
    expect((await within(await screen.findByRole('dialog', { name: 'Nothing to checkpoint' })).findByRole('status')).textContent).toMatch(/Nothing had changed since the last checkpoint/)
  })

  it('closes on Escape and gives focus back to the indicator', async () => {
    stubCheckpoints(() => status())
    renderApp('/u/dracula')
    const indicator = await screen.findByRole('button', { name: /3 notes changed/ })
    fireEvent.click(indicator)
    const dialog = await screen.findByRole('dialog')
    fireEvent.keyDown(dialog, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(document.activeElement).toBe(indicator))
  })

  it('keeps focus in the dialog while the checkpoint is made, and gives it back to the box after an error', async () => {
    let answer: (r: Response) => void = () => {}
    stubCheckpoints(
      () => status(),
      () => new Promise<Response>((resolve) => (answer = resolve)) as unknown as Response,
    )
    renderApp('/u/dracula')
    fireEvent.click(await screen.findByRole('button', { name: /3 notes changed/ }))
    const dialog = await screen.findByRole('dialog')
    const box = within(dialog).getByLabelText('Name this checkpoint')
    fireEvent.change(box, { target: { value: 'Notes pass after drafting ch 12' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Checkpoint' }))

    await within(dialog).findByRole('button', { name: 'Making the checkpoint…' })
    await waitFor(() => expect(document.activeElement).toBe(dialog))
    answer(new Response(JSON.stringify({ code: 'history_failed', message: 'Git hasn’t finished the checkpoint after 2 minutes.' }), { status: 500 }))

    await within(dialog).findByRole('alert')
    await waitFor(() => expect(document.activeElement).toBe(box))
  })

  it('asks before a click outside drops a typed name, and closes at once without one', async () => {
    stubCheckpoints(() => status())
    renderApp('/u/dracula')
    fireEvent.click(await screen.findByRole('button', { name: /3 notes changed/ }))
    let dialog = await screen.findByRole('dialog')
    const backdrop = dialog.parentElement!
    fireEvent.change(within(dialog).getByLabelText('Name this checkpoint'), { target: { value: 'Planning session: harbour subplot' } })

    fireEvent.mouseDown(backdrop)
    const ask = within(dialog).getByRole('alertdialog')
    expect(ask.textContent).toMatch(/Close without making the checkpoint/)
    fireEvent.click(within(ask).getByRole('button', { name: 'Keep it open' }))
    expect((within(dialog).getByLabelText('Name this checkpoint') as HTMLTextAreaElement).value).toBe('Planning session: harbour subplot')

    fireEvent.mouseDown(backdrop)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close without a checkpoint' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())

    fireEvent.click(screen.getByRole('button', { name: /3 notes changed/ }))
    dialog = await screen.findByRole('dialog')
    fireEvent.mouseDown(dialog.parentElement!)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('warns when git is in the middle of something', async () => {
    stubCheckpoints(() => status({ inProgress: 'a rebase' }))
    renderApp('/u/dracula')
    fireEvent.click(await screen.findByRole('button', { name: /3 notes changed/ }))
    expect((await screen.findByRole('dialog')).textContent).toMatch(/Git is in the middle of a rebase/)
  })

  it('mentions automatic checkpoints when they’re on', async () => {
    stubCheckpoints(() => status({ autoCheckpoint: { every: '30m', problem: null, runs: true } }))
    renderApp('/u/dracula')
    fireEvent.click(await screen.findByRole('button', { name: /3 notes changed/ }))
    expect((await screen.findByRole('dialog')).textContent).toMatch(/Automatic checkpoints are on: Wolfden makes one after 30m without changes/)
  })
})

describe('SC-WEB-51: without history, the indicator says how to turn it on', () => {
  it('on a server that can’t turn it on, says how to from the command line instead of offering a checkpoint', async () => {
    const { posted } = stubCheckpoints(() => NO_HISTORY)
    renderApp('/u/dracula')
    fireEvent.click(await screen.findByRole('button', { name: /No history · Turn on…/ }))

    const dialog = await screen.findByRole('dialog', { name: 'History is off' })
    expect(dialog.textContent).toMatch(/wolfden init --git/)
    expect(within(dialog).queryByRole('button', { name: 'Turn on history' })).toBeNull()
    expect(dialog.textContent).toMatch(/still saved in the files/)
    expect(within(dialog).queryByLabelText('Name this checkpoint')).toBeNull()
    expect(within(dialog).queryByRole('button', { name: 'Checkpoint' })).toBeNull()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(posted).toEqual([])
  })
})

const TURNED_ON: HistoryTurnedOn = {
  version: 1,
  backend: 'git',
  steps: [
    "Made the den's folder a git repository.",
    ".gitignore already leaves out .wolfden/.",
    'Made the first checkpoint, "Start of history", of 42 files.',
  ],
  repositoryMade: true,
  gitIgnore: 'present',
  identitySaved: [],
  checkpoint: { id: 'c0ffee0000000000000000000000000000000000', when: '2026-10-28T09:00:00Z', author: 'Test Writer', message: 'Start of history', files: 42 },
}

/** A den without history on a server that can turn it on; `turnOn` overrides what git still needs. */
function noHistory(turnOn: Partial<TurnOnView> = {}): CheckpointStatus {
  return { ...NO_HISTORY, turnOn: { possible: true, reason: null, needsName: false, needsEmail: false, identityCertain: true, ...turnOn } }
}

/** Answers /api/checkpoint with `current`, and /api/history/turn-on with `answer`; records what was sent to turn it on. */
function stubTurnOn(
  current: () => CheckpointStatus,
  answer: (body: TurnOnRequest) => Response | HistoryTurnedOn | Promise<Response | HistoryTurnedOn> = () => TURNED_ON,
) {
  const sent: TurnOnRequest[] = []
  const api = stubApi({
    override: (path, _params, init) => {
      if (path === '/api/history/turn-on') {
        expect(init?.method).toBe('POST')
        expect(new Headers(init?.headers).get('Content-Type')).toBe('application/json')
        const body = JSON.parse(String(init?.body)) as TurnOnRequest
        sent.push(body)
        return answer(body)
      }

      return path === '/api/checkpoint' ? current() : undefined
    },
  })
  return { ...api, sent }
}

async function openTurnOn() {
  renderApp('/u/dracula')
  fireEvent.click(await screen.findByRole('button', { name: /No history · Turn on…/ }))
  return screen.findByRole('dialog', { name: 'History is off' })
}

describe('SC-WEB-55: I turn on history from the checkpoint dialog', () => {
  it('turns it on with a first checkpoint, says what it did, and the indicator catches up', async () => {
    let current = noHistory()
    const { sent, calls } = stubTurnOn(
      () => current,
      () => {
        current = status({ changes: [], latest: { id: TURNED_ON.checkpoint!.id, when: TURNED_ON.checkpoint!.when } })
        return TURNED_ON
      },
    )
    const dialog = await openTurnOn()

    expect(dialog.textContent).toMatch(/synced folder/)
    expect(within(dialog).queryByLabelText('Name')).toBeNull()
    expect(within(dialog).queryByLabelText('Email')).toBeNull()
    const first = within(dialog).getByLabelText(/Make a first checkpoint, “Start of history”/) as HTMLInputElement
    expect(first.checked).toBe(true)
    const statusCalls = calls.filter((c) => c === '/api/checkpoint').length
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))

    const done = await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent).toEqual([{ firstCheckpoint: true }])
    expect(within(done).getByText('Made the first checkpoint, "Start of history", of 42 files.')).toBeTruthy()
    await waitFor(() => expect(calls.filter((c) => c === '/api/checkpoint').length).toBeGreaterThan(statusCalls))
    fireEvent.click(within(done).getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(screen.queryByRole('button', { name: /No history/ })).toBeNull())
  })

  it('leaves out the first checkpoint when it’s unticked', async () => {
    const { sent } = stubTurnOn(() => noHistory())
    const dialog = await openTurnOn()

    fireEvent.click(within(dialog).getByLabelText(/Make a first checkpoint/))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))

    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent).toEqual([{ firstCheckpoint: false }])
  })

  it('asks for a name and email when git has none, and sends only those', async () => {
    const { sent } = stubTurnOn(() => noHistory({ needsName: true, needsEmail: true }))
    const dialog = await openTurnOn()

    expect(within(dialog).getByText(/a pen name included/)).toBeTruthy()
    expect(within(dialog).getByText(/can be a placeholder/)).toBeTruthy()
    const name = within(dialog).getByLabelText('Name') as HTMLInputElement
    const email = within(dialog).getByLabelText('Email') as HTMLInputElement
    await waitFor(() => expect(document.activeElement).toBe(name))

    // Nothing given: asked again, and nothing is sent.
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))
    expect((await within(dialog).findByRole('alert')).textContent).toMatch(/Give a name/)
    expect(name.getAttribute('aria-invalid')).toBe('true')
    expect(sent).toEqual([])

    fireEvent.change(name, { target: { value: '  Ash Calloway ' } })
    fireEvent.change(email, { target: { value: 'ash@example.invalid' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))

    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent).toEqual([{ firstCheckpoint: true, authorName: 'Ash Calloway', authorEmail: 'ash@example.invalid' }])
  })

  it('before the den is a repository, offers the fields without requiring them, and sends nothing if they’re left empty', async () => {
    const { sent } = stubTurnOn(() => noHistory({ needsName: true, needsEmail: true, identityCertain: false }))
    const dialog = await openTurnOn()

    expect(dialog.textContent).toMatch(/doesn't seem to have them/)
    const name = within(dialog).getByLabelText('Name') as HTMLInputElement
    expect(name.required).toBe(false)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))

    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent).toEqual([{ firstCheckpoint: true }])
  })

  it('when git has only an email or only a name, asks for both, and requires both', async () => {
    // The server's status says both whichever one git has, since neither is recorded beside the other (#286).
    const { sent } = stubTurnOn(() => noHistory({ needsName: true, needsEmail: true }))
    const dialog = await openTurnOn()

    expect(dialog.textContent).toMatch(/doesn't have both/)
    const name = within(dialog).getByLabelText('Name') as HTMLInputElement
    const email = within(dialog).getByLabelText('Email') as HTMLInputElement
    expect(name.required).toBe(true)
    expect(email.required).toBe(true)

    fireEvent.change(email, { target: { value: 'ash@example.invalid' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))
    expect(await within(dialog).findByText(/Give a name/)).toBeTruthy()
    expect(sent).toEqual([])
  })

  it('after an email alone is refused because git has a name, asks for the name and keeps the email', async () => {
    // Before the den is a repository the fields are optional; once it is, an includeIf gives git a name.
    const { sent } = stubTurnOn(
      () => noHistory({ needsName: true, needsEmail: true, identityCertain: false }),
      (body) =>
        body.authorName === undefined
          ? new Response(
              JSON.stringify({ code: 'identity_needed', message: 'An email was given without a name.', needsName: true, needsEmail: false }),
              { status: 400 },
            )
          : TURNED_ON,
    )
    const dialog = await openTurnOn()
    fireEvent.change(within(dialog).getByLabelText('Email'), { target: { value: 'ash@example.invalid' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))

    expect((await within(dialog).findByRole('alert')).textContent).toMatch(/without a name/)
    const name = within(dialog).getByLabelText('Name') as HTMLInputElement
    expect(name.required).toBe(true)
    expect((within(dialog).getByLabelText('Email') as HTMLInputElement).value).toBe('ash@example.invalid')

    fireEvent.change(name, { target: { value: 'Ash Calloway' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))

    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent[1]).toEqual({ firstCheckpoint: true, authorName: 'Ash Calloway', authorEmail: 'ash@example.invalid' })
  })

  it('asks only for what’s missing', async () => {
    stubTurnOn(() => noHistory({ needsEmail: true }))
    const dialog = await openTurnOn()
    expect(within(dialog).queryByLabelText('Name')).toBeNull()
    expect(within(dialog).getByLabelText('Email')).toBeTruthy()
  })

  it('shows the server’s refusal, and asks for a name and email when git turns out to need them', async () => {
    const { sent } = stubTurnOn(
      () => noHistory(),
      () =>
        new Response(
          JSON.stringify({ code: 'identity_needed', message: 'Git records who made each checkpoint, and it has no name or email for this den.' }),
          { status: 400 },
        ),
    )
    const dialog = await openTurnOn()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))

    expect((await within(dialog).findByRole('alert')).textContent).toMatch(/has no name or email for this den/)
    expect(within(dialog).getByLabelText('Name')).toBeTruthy()
    expect(within(dialog).getByLabelText('Email')).toBeTruthy()
    expect(sent).toHaveLength(1)
  })

  it('after git turns out to need only an email, asks only for that, keeps the name typed, and holds the fields while it tries again', async () => {
    // Before the den is a repository, git seemed to lack both; once it is, an includeIf gives it a name, but no email.
    let retry: (answer: Response | HistoryTurnedOn) => void = () => {}
    const { sent } = stubTurnOn(
      () => noHistory({ needsName: true, needsEmail: true, identityCertain: false }),
      (body) =>
        body.authorEmail === undefined
          ? new Response(
              JSON.stringify({ code: 'identity_needed', message: 'Git records who made each checkpoint, and it has no email for this den.', needsName: false, needsEmail: true }),
              { status: 400 },
            )
          : new Promise((resolve) => (retry = resolve)),
    )
    const dialog = await openTurnOn()
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: 'Ilse Tarrow' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))

    expect((await within(dialog).findByRole('alert')).textContent).toMatch(/has no email for this den/)
    const email = within(dialog).getByLabelText('Email') as HTMLInputElement
    expect(email.required).toBe(true)
    expect((within(dialog).getByLabelText('Name') as HTMLInputElement).value).toBe('Ilse Tarrow')

    // The email is required now.
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))
    expect(await within(dialog).findByText(/Give an email/)).toBeTruthy()
    expect(sent).toHaveLength(1)

    fireEvent.change(email, { target: { value: 'ilse@example.invalid' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))
    await waitFor(() => expect(sent).toHaveLength(2))
    expect(sent[1]).toEqual({ firstCheckpoint: true, authorName: 'Ilse Tarrow', authorEmail: 'ilse@example.invalid' })

    // While it's pending, the fields stay where they are, and the email stays required.
    expect(within(dialog).getByRole('button', { name: 'Turning on history…' })).toBeTruthy()
    expect(within(dialog).getByLabelText('Name')).toBeTruthy()
    expect((within(dialog).getByLabelText('Email') as HTMLInputElement).required).toBe(true)

    retry(TURNED_ON)
    await screen.findByRole('dialog', { name: 'History is on' })
  })

  it('after git turns out to need only an email that wasn’t offered, shows only the email, and keeps it while it tries again', async () => {
    // The status said git needs nothing, so only the refusal says to ask: the fields mustn't go while the retry is pending.
    let retry: (answer: Response | HistoryTurnedOn) => void = () => {}
    const { sent } = stubTurnOn(
      () => noHistory(),
      (body) =>
        body.authorEmail === undefined
          ? new Response(JSON.stringify({ code: 'identity_needed', message: 'It has no email for this den.', needsName: false, needsEmail: true }), {
              status: 400,
            })
          : new Promise((resolve) => (retry = resolve)),
    )
    const dialog = await openTurnOn()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))

    const email = (await within(dialog).findByLabelText('Email')) as HTMLInputElement
    expect(within(dialog).queryByLabelText('Name')).toBeNull()
    expect(sent).toHaveLength(1)

    fireEvent.change(email, { target: { value: 'ash@example.invalid' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))
    await waitFor(() => expect(sent).toHaveLength(2))
    expect(sent[1]).toEqual({ firstCheckpoint: true, authorEmail: 'ash@example.invalid' })

    // Pending: the Email field is still there, still required, and still holds what was typed.
    expect(within(dialog).getByRole('button', { name: 'Turning on history…' })).toBeTruthy()
    const pending = within(dialog).getByLabelText('Email') as HTMLInputElement
    expect(pending.required).toBe(true)
    expect(pending.value).toBe('ash@example.invalid')
    expect(within(dialog).queryByLabelText('Name')).toBeNull()

    retry(TURNED_ON)
    await screen.findByRole('dialog', { name: 'History is on' })
  })

  it('says why when it can’t be turned on here, and offers no button', async () => {
    stubTurnOn(() =>
      noHistory({ possible: false, reason: "Git isn't installed, or Wolfden can't find it on the PATH, so history can't be turned on." }),
    )
    const dialog = await openTurnOn()

    expect(dialog.textContent).toMatch(/Git isn't installed/)
    expect(within(dialog).queryByRole('button', { name: 'Turn on history' })).toBeNull()
  })
})

describe('SC-WEB-99: I use a different name for this den when I turn on history', () => {
  const DIFFERENT = 'Use a different name for this den'
  const SIGNING_STEP = "Turned signing off for this den's repository only: git is set to sign, and Wolfden can't tell whose key that would use."

  const openDifferent = (dialog: HTMLElement) => fireEvent.click(within(dialog).getByRole('button', { name: DIFFERENT }))
  const fill = (dialog: HTMLElement, name: string, email: string) => {
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: name } })
    fireEvent.change(within(dialog).getByLabelText('Email'), { target: { value: email } })
  }
  const submit = (dialog: HTMLElement) => fireEvent.click(within(dialog).getByRole('button', { name: 'Turn on history' }))
  const refusal = (code: string, message: string, status: number) => () => new Response(JSON.stringify({ code, message }), { status })

  it('is closed at first, keeps focus where it was, and sends no name', async () => {
    const { sent } = stubTurnOn(() => noHistory())
    const dialog = await openTurnOn()

    const toggle = within(dialog).getByRole('button', { name: DIFFERENT })
    expect(toggle.getAttribute('type')).toBe('button')
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(toggle.getAttribute('aria-controls')).toBeNull()
    expect(within(dialog).queryByLabelText('Name')).toBeNull()
    expect(within(dialog).queryByLabelText('Email')).toBeNull()
    expect(document.activeElement).toBe(within(dialog).getByLabelText(/Make a first checkpoint/))

    submit(dialog)
    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent).toEqual([{ firstCheckpoint: true }])
  })

  it('opens both fields and what it does to signing, and sends both, tidied, with the first checkpoint choice', async () => {
    const { sent } = stubTurnOn(() => noHistory())
    const dialog = await openTurnOn()
    openDifferent(dialog)

    const toggle = within(dialog).getByRole('button', { name: DIFFERENT })
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    const fields = document.getElementById(toggle.getAttribute('aria-controls')!)!
    expect(within(fields).getByLabelText('Name').getAttribute('aria-required')).toBe('true')
    expect(within(fields).getByLabelText('Email').getAttribute('aria-required')).toBe('true')
    expect(fields.textContent).toMatch(/Signing goes off for this den/)
    expect(fields.textContent).toMatch(/how to sign here with a key of this name's/)
    expect(document.activeElement).not.toBe(within(fields).getByLabelText('Name'))

    fill(dialog, '  Ash Calloway ', ' ash@example.invalid ')
    fireEvent.click(within(dialog).getByLabelText(/Make a first checkpoint/))
    submit(dialog)

    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent).toEqual([{ firstCheckpoint: false, authorName: 'Ash Calloway', authorEmail: 'ash@example.invalid' }])
  })

  it('offers it when the den isn’t a repository yet and git seems to have a name, and still requires both', async () => {
    const { sent } = stubTurnOn(() => noHistory({ identityCertain: false }))
    const dialog = await openTurnOn()
    openDifferent(dialog)
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: 'Ash Calloway' } })
    submit(dialog)

    expect((await within(dialog).findByRole('alert')).textContent).toMatch(/Give an email/)
    expect(sent).toEqual([])
  })

  it('isn’t offered when git needs a name or email: the fields are asked for already', async () => {
    stubTurnOn(() => noHistory({ needsName: true, needsEmail: true }))
    const dialog = await openTurnOn()

    expect(within(dialog).queryByRole('button', { name: DIFFERENT })).toBeNull()
    expect(within(dialog).getByLabelText('Name')).toBeTruthy()
  })

  it.each([
    ['name', '', 'ash@example.invalid', /Give a name/, 'Name'],
    ['name', '   ', 'ash@example.invalid', /Give a name/, 'Name'],
    ['email', 'Ash Calloway', '', /Give an email/, 'Email'],
    ['email', 'Ash Calloway', '   ', /Give an email/, 'Email'],
  ])('refuses in the page without a %s (%j, %j), keeps what’s typed, and goes to the field', async (_what, name, email, says, label) => {
    const { sent } = stubTurnOn(() => noHistory())
    const dialog = await openTurnOn()
    openDifferent(dialog)
    fill(dialog, name, email)
    submit(dialog)

    expect((await within(dialog).findByRole('alert')).textContent).toMatch(says)
    expect(sent).toEqual([])
    expect((within(dialog).getByLabelText('Name') as HTMLInputElement).value).toBe(name)
    expect((within(dialog).getByLabelText('Email') as HTMLInputElement).value).toBe(email)
    expect(document.activeElement).toBe(within(dialog).getByLabelText(label))
    expect(within(dialog).getByLabelText(label).getAttribute('aria-invalid')).toBe('true')
  })

  it('doesn’t send what’s typed once it’s closed, says so, and has it back when it’s opened again', async () => {
    const { sent } = stubTurnOn(() => noHistory())
    const dialog = await openTurnOn()
    expect(dialog.textContent).not.toMatch(/What you typed/)
    openDifferent(dialog)
    fill(dialog, 'Ash Calloway', 'ash@example.invalid')
    openDifferent(dialog)

    expect(within(dialog).queryByLabelText('Name')).toBeNull()
    expect(dialog.textContent).toMatch(/What you typed isn't used while this is closed/)

    openDifferent(dialog)
    expect((within(dialog).getByLabelText('Name') as HTMLInputElement).value).toBe('Ash Calloway')
    expect(dialog.textContent).not.toMatch(/What you typed/)
    openDifferent(dialog)

    submit(dialog)
    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent).toEqual([{ firstCheckpoint: true }])
  })

  it('clears a complaint about a missing field when it’s closed, and sends nothing hidden if only one was typed', async () => {
    const { sent } = stubTurnOn(() => noHistory())
    const dialog = await openTurnOn()
    openDifferent(dialog)
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: 'Ash Calloway' } })
    submit(dialog)
    await within(dialog).findByRole('alert')
    openDifferent(dialog)

    expect(within(dialog).queryByRole('alert')).toBeNull()
    submit(dialog)
    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent).toEqual([{ firstCheckpoint: true }])
  })

  it.each([
    ['a den with checkpoints', 'has_repository', 409, "The den's folder is already a git repository with checkpoints, so its history is on. The name and email given weren't applied."],
    ['a den inside a larger repository', 'in_repository', 409, "The den is inside the git repository at 'C:/kestrel'. The name and email given weren't applied."],
    ['a value git won’t record', 'bad_argument', 400, 'The email given is one git would record as empty.'],
  ])('shows the refusal for %s, keeps what’s typed, and can try again', async (_what, code, status, message) => {
    let answer: () => Response | HistoryTurnedOn = refusal(code, message, status)
    const { sent } = stubTurnOn(
      () => noHistory(),
      () => answer(),
    )
    const dialog = await openTurnOn()
    openDifferent(dialog)
    fill(dialog, 'Ash Calloway', 'ash@example.invalid')
    submit(dialog)

    expect((await within(dialog).findByRole('alert')).textContent).toBe(message)
    await waitFor(() => expect((within(dialog).getByRole('button', { name: 'Turn on history' }) as HTMLButtonElement).disabled).toBe(false))
    expect((within(dialog).getByLabelText('Name') as HTMLInputElement).value).toBe('Ash Calloway')
    expect((within(dialog).getByLabelText('Email') as HTMLInputElement).value).toBe('ash@example.invalid')
    expect(within(dialog).getByRole('button', { name: DIFFERENT }).getAttribute('aria-expanded')).toBe('true')

    answer = () => TURNED_ON
    submit(dialog)
    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent).toHaveLength(2)
    expect(sent[1]).toEqual(sent[0])
  })

  it('brings a refusal into view, since the open fields can push it below the dialog’s fold', async () => {
    const scrolled = vi.fn()
    const had = Element.prototype.scrollIntoView
    Element.prototype.scrollIntoView = function (this: Element) {
      scrolled(this.getAttribute('role'))
    }
    try {
      stubTurnOn(() => noHistory(), refusal('has_repository', 'Its history is on.', 409))
      const dialog = await openTurnOn()
      openDifferent(dialog)
      fill(dialog, 'Ash Calloway', 'ash@example.invalid')
      submit(dialog)

      await within(dialog).findByRole('alert')
      await waitFor(() => expect(scrolled).toHaveBeenCalledWith('alert'))
    } finally {
      Element.prototype.scrollIntoView = had
    }
  })

  it('when the server still asks for one of them, keeps it open with both typed, and sends both again', async () => {
    let answer: () => Response | HistoryTurnedOn = () =>
      new Response(JSON.stringify({ code: 'identity_needed', message: 'A name was given without an email.', needsName: false, needsEmail: true }), { status: 400 })
    const { sent } = stubTurnOn(
      () => noHistory(),
      () => answer(),
    )
    const dialog = await openTurnOn()
    openDifferent(dialog)
    fill(dialog, 'Ash Calloway', 'ash@example.invalid')
    submit(dialog)

    expect((await within(dialog).findByRole('alert')).textContent).toMatch(/without an email/)
    expect(within(dialog).getByRole('button', { name: DIFFERENT }).getAttribute('aria-expanded')).toBe('true')
    expect((within(dialog).getByLabelText('Name') as HTMLInputElement).value).toBe('Ash Calloway')

    answer = () => TURNED_ON
    submit(dialog)
    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent[1]).toEqual({ firstCheckpoint: true, authorName: 'Ash Calloway', authorEmail: 'ash@example.invalid' })
  })

  it('when it stopped part way, says what was done and sends the name again on a retry', async () => {
    let answer: () => Response | HistoryTurnedOn = refusal(
      'history_failed',
      "Made the den's folder a git repository. Saved your name and email for this den's repository only. Couldn't make the first checkpoint: a hook refused it.",
      500,
    )
    const { sent } = stubTurnOn(
      () => noHistory(),
      () => answer(),
    )
    const dialog = await openTurnOn()
    openDifferent(dialog)
    fill(dialog, 'Ash Calloway', 'ash@example.invalid')
    submit(dialog)

    expect((await within(dialog).findByRole('alert')).textContent).toMatch(/a hook refused it/)
    answer = () => TURNED_ON
    await waitFor(() => expect((within(dialog).getByRole('button', { name: 'Turn on history' }) as HTMLButtonElement).disabled).toBe(false))
    submit(dialog)

    await screen.findByRole('dialog', { name: 'History is on' })
    expect(sent[1]).toEqual({ firstCheckpoint: true, authorName: 'Ash Calloway', authorEmail: 'ash@example.invalid' })
  })

  it('if git turns out to need them with it closed, shows what was typed in the fields it asks for, so nothing is sent unseen', async () => {
    const { sent } = stubTurnOn(
      () => noHistory({ identityCertain: false }),
      () => new Response(JSON.stringify({ code: 'identity_needed', message: 'Git has no name or email for this den.', needsName: true, needsEmail: true }), { status: 400 }),
    )
    const dialog = await openTurnOn()
    openDifferent(dialog)
    fill(dialog, 'Ash Calloway', 'ash@example.invalid')
    openDifferent(dialog)
    submit(dialog)

    await within(dialog).findByRole('alert')
    expect(sent).toEqual([{ firstCheckpoint: true }])
    expect(within(dialog).queryByRole('button', { name: DIFFERENT })).toBeNull()
    expect((within(dialog).getByLabelText('Name') as HTMLInputElement).value).toBe('Ash Calloway')
    expect((within(dialog).getByLabelText('Email') as HTMLInputElement).value).toBe('ash@example.invalid')
  })

  it('can’t be changed while it’s turning history on', async () => {
    let finish: (answer: HistoryTurnedOn) => void = () => {}
    stubTurnOn(
      () => noHistory(),
      () => new Promise((resolve) => (finish = resolve)),
    )
    const dialog = await openTurnOn()
    openDifferent(dialog)
    fill(dialog, 'Ash Calloway', 'ash@example.invalid')
    submit(dialog)

    await waitFor(() => expect((within(dialog).getByRole('button', { name: DIFFERENT }) as HTMLButtonElement).disabled).toBe(true))
    expect((within(dialog).getByLabelText('Name') as HTMLInputElement).disabled).toBe(true)
    finish(TURNED_ON)
    await screen.findByRole('dialog', { name: 'History is on' })
  })

  it('stays inside the dialog’s Tab order, and Tab from the last control comes round', async () => {
    stubTurnOn(() => noHistory())
    const dialog = await openTurnOn()
    openDifferent(dialog)
    const cancel = within(dialog).getByRole('button', { name: 'Cancel' })
    cancel.focus()
    fireEvent.keyDown(cancel, { key: 'Tab' })
    expect(document.activeElement).toBe(within(dialog).getByLabelText(/Make a first checkpoint/))
    const controls = [...dialog.querySelectorAll<HTMLElement>('button, input')]
    expect(controls.slice(1, 4)).toEqual([
      within(dialog).getByRole('button', { name: DIFFERENT }),
      within(dialog).getByLabelText('Name'),
      within(dialog).getByLabelText('Email'),
    ])
  })

  it('never shows what git has, and a browser’s saved name isn’t offered for the different one', async () => {
    const turned: HistoryTurnedOn = {
      ...TURNED_ON,
      steps: [...TURNED_ON.steps, SIGNING_STEP],
      checkpoint: { ...TURNED_ON.checkpoint!, author: 'Marek Oswin' },
    }
    stubTurnOn(
      () => noHistory(),
      () => turned,
    )
    const dialog = await openTurnOn()
    openDifferent(dialog)

    expect(within(dialog).getByLabelText('Name').getAttribute('autocomplete')).toBe('off')
    expect(within(dialog).getByLabelText('Email').getAttribute('autocomplete')).toBe('off')
    fill(dialog, 'Ash Calloway', 'ash@example.invalid')
    submit(dialog)

    const done = await screen.findByRole('dialog', { name: 'History is on' })
    expect(within(done).getByText(SIGNING_STEP)).toBeTruthy()
    expect(done.textContent).not.toMatch(/Marek/)
  })
})

describe('SC-WEB-50: when the status can’t be read', () => {
  it('shows a quiet "History unavailable" with the reason, rather than hiding', async () => {
    stubCheckpoints(
      () =>
        new Response(JSON.stringify({ code: 'history_failed', message: 'git diff failed: fatal: bad object' }), { status: 500 }) as unknown as CheckpointStatus,
    )
    renderApp('/u/dracula')
    fireEvent.click(await screen.findByRole('button', { name: 'History unavailable' }))
    const dialog = await screen.findByRole('dialog', { name: 'History unavailable' })
    expect(dialog.textContent).toMatch(/fatal: bad object/)
    fireEvent.keyDown(dialog, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })
})

describe('grouping changes for review', () => {
  it('puts den-level files first, then each universe’s scopes in path order', () => {
    const groups = groupChanges([
      change('universes/starfall/series/long-dark/books/ember/outline.md'),
      change('universes/starfall/series/long-dark/entities/characters/ilse-tarrow.md'),
      change('den.yaml'),
      change('universes/starfall/books/a-quiet-moon/book.md'),
      change('universes/starfall/universe.md'),
    ])
    expect(groups.map((g) => [g.title, g.changes.map((c) => c.shown)])).toEqual([
      ['Den settings and packs', ['den.yaml']],
      ['Universe starfall', ['universe.md']],
      ['Universe starfall · Book a-quiet-moon', ['book.md']],
      ['Universe starfall · Series long-dark', ['entities/characters/ilse-tarrow.md']],
      ['Universe starfall · Series long-dark · Book ember', ['outline.md']],
    ])
  })
})

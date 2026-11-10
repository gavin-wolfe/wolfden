# Security policy

## Reporting a vulnerability

Please report security problems privately, not in a public issue.

Use GitHub's private vulnerability reporting: open the repository's
**Security** tab and choose **Report a vulnerability**, or go straight to
<https://github.com/gavin-wolfe/wolfden/security/advisories/new>. Only the
maintainer, and anyone they add to the report, can see it. The maintainer
will reply there. This is a one-person project, so a reply may take a few
days. You can then agree on a fix and a disclosure date before anything is
made public.

If you're not sure whether something is a security problem, report it
privately anyway.

## Supported versions

Wolfden hasn't had a release yet. Fixes go to the `main` branch only. Once
releases begin, this section will say which ones get security fixes.

## What's in scope

Wolfden runs on your own computer. Your notes are plain files in a folder
you control, and `wolfden serve` listens on `127.0.0.1` only. Reports we
especially want:

- A website open in your browser, or another computer or device, that can
  read or change a den through `wolfden serve` (cross-site requests, host
  or origin checks, DNS rebinding).
- A den you received from someone else (a clone, a zip, a collaborator's
  changes) whose content can run script in the web UI, or load something
  the content security policy should block.
- Reading or writing files outside the den through the web UI, the API or
  the MCP server (path traversal, symbolic links, junctions).
- With login on: getting past it, or an account doing what its role
  doesn't allow (a viewer making changes, or a writer unfreezing a book).
- Once the commit-identity check
  ([#311](https://github.com/gavin-wolfe/wolfden/issues/311)) exists: a
  commit recorded under a name or email the den doesn't declare. Until then, the known limits are
  listed under "Turning on history" in
  [the usage guide](docs/using-wolfden.md#turning-on-history) and in that issue.

## What's out of scope

- Anything that needs someone to already have access to your den folder,
  Wolfden's installation folder or your computer account. Notes are
  ordinary files: anyone or any program that can open the folder can read
  and change them, login or not. Wolfden doesn't encrypt notes. (A den you
  received from someone else is different: see above.)
- Other programs or accounts on the same computer reaching `wolfden serve`
  while login is off. The server's help text says so. Turning login on
  (`wolfden login on`) is the fix.
- Story-position reads that show too much. A position narrows what a read
  returns, but it isn't a lock: an AI assistant can leave the position
  out, and one that can open the den's files can read everything. If a read at a position returns text from
  past that position, that's a bug, so please open a normal issue.
- Problems in a den's own git remote, sync service or backup program.

## Keeping your notes private on a shared computer

Give each person their own computer account, and keep the den inside your
own user folder (such as Documents), where, on most systems, other
accounts can't read it (an administrator account still can).
Disk encryption protects the notes if the computer is lost or stolen.

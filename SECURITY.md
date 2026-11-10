# Security policy

## Reporting a vulnerability

Please report security problems privately, not in a public issue.

Use GitHub's private vulnerability reporting: open the repository's
**Security** tab and choose **Report a vulnerability**, or go straight to
<https://github.com/gavin-wolfe/wolfden/security/advisories/new>. Only the
maintainer sees the report. You'll get a reply there, and we can work out a
fix and a disclosure date together before anything is made public.

If you're not sure whether something is a security problem, report it
privately anyway.

## Supported versions

Wolfden hasn't had a release yet. Fixes go to the `main` branch only. Once
releases begin, this section will say which ones get security fixes.

## What's in scope

Wolfden runs on your own computer. Your notes are plain files in a folder
you control, and `wolfden serve` listens on `127.0.0.1` only. Reports we
especially want:

- Another website, or another program reaching `wolfden serve` over the
  network, that can read or change a den (cross-site requests, host or
  origin checks, DNS rebinding).
- Den content that can run script in the web UI, or load something the
  content security policy should block.
- Reading or writing files outside the den through the web UI, the API or
  the MCP server (path traversal, symbolic links, junctions).
- An AI assistant connected through `wolfden mcp` seeing notes or text that
  a read at a story position should hide from it.
- Commits recorded under a name or email the writer didn't choose. This
  matters to writers who use a pen name.
- Once login exists: getting past it, or a viewer account making changes.

## What's out of scope

- Anything that needs access to the den folder or Wolfden's installation
  folder already. Notes are ordinary files: anyone or any program that can
  open the folder can read and change them, login or not. Wolfden doesn't
  encrypt notes. Give each person their own computer account and use disk
  encryption.
- Other programs or accounts on the same computer reaching
  `wolfden serve` while login is off. The server's help text says so, and
  login (coming soon) is the fix.
- Problems in a den's own git remote, sync service or backup program.

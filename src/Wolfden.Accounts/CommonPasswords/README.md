# Common passwords

`common-passwords.txt` is the list `PasswordRules` refuses passwords from
(#354; NIST SP 800-63B rev. 4 §3.1.1.2 asks for a blocklist). It's built
into `Wolfden.Accounts.dll` as a resource and read once, the first time a
password is checked. Wolfden never fetches it or anything else over the
network.

## Where it comes from

- **Source:** "Bad Passwords" by Ryan Parman, `raw.txt`: the top 10,000
  most-used passwords from hacked password lists, one per line, lowercase,
  sorted.
- **Repository:** <https://github.com/skyzyx/bad-passwords>
- **Download URL** (pinned to commit `cfd1a95`, the latest, 2017-09-16):
  <https://raw.githubusercontent.com/skyzyx/bad-passwords/cfd1a9564b43e43c18c06ff0460c3617dc147d01/raw.txt>
- **Copied unchanged:** 10,000 lines, 73,020 bytes, SHA-256
  `fad3a4d9174c11750634b80e1b4d2fb5f1623599b6b46da558b953f797b665bf`.

## Why it's public domain

The author dedicates it to the public domain, twice over. The project's
README, under "Authors, Copyright & Licensing", says:

> My intention is to release all rights to this documentation and make it
> available under the Public Domain. [...] So, I am dual-licensing this work
> under CC0 and the Unlicense.

with the CC0 statement "To the extent possible under law, Ryan Parman has
waived all copyright and related or neighboring rights to 'Bad Passwords'".
The full texts are in the repository:

- CC0 1.0: <https://github.com/skyzyx/bad-passwords/blob/cfd1a9564b43e43c18c06ff0460c3617dc147d01/LICENSE-CC0>
- The Unlicense: <https://github.com/skyzyx/bad-passwords/blob/cfd1a9564b43e43c18c06ff0460c3617dc147d01/LICENSE-UNLICENSE>
- README: <https://github.com/skyzyx/bad-passwords/blob/cfd1a9564b43e43c18c06ff0460c3617dc147d01/README.md>

Neither dedication asks for attribution; this note records the source
anyway.

## How it's used

Only 1 of the 10,000 entries has 15 characters or more, and shorter
passwords are refused for length already. So the whole list is kept, and a
password is refused when, compared ignoring case, spaces and punctuation,
it is a list entry said once or several times in a row, optionally followed
by digits that are no longer than the part before them
(`passwordpassword`, `qwertyuiop12345`). See `PasswordRules.cs`.

The list is what people really typed into compromised sites, so it
includes crude words. Replace it only with another list that has an
explicit public-domain dedication (maintainer decision, 2026-11-10), and
update this note.

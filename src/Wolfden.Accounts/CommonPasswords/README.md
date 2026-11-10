# Common passwords

`common-passwords.txt` is the list `PasswordRules` refuses passwords from
(#354; NIST SP 800-63B rev. 4 §3.1.1.2 asks for a blocklist). It's built
into `Wolfden.Accounts.dll` as a resource and read once, the first time a
password is checked. Wolfden never fetches it or anything else over the
network.

## Where it comes from

- **Copied from:** "Bad Passwords" by Ryan Parman, `raw.txt`, "the top
  10,000 most-used passwords from hacked password lists", one per line,
  lowercase. <https://github.com/skyzyx/bad-passwords>
- **Download URL** (pinned to commit `cfd1a95`, the latest, 2017-09-16):
  <https://raw.githubusercontent.com/skyzyx/bad-passwords/cfd1a9564b43e43c18c06ff0460c3617dc147d01/raw.txt>
- **Upstream file:** 10,000 lines, 73,020 bytes, SHA-256
  `fad3a4d9174c11750634b80e1b4d2fb5f1623599b6b46da558b953f797b665bf`.
- **Origin of the entries:** 9,995 of the 10,000 also appear in SecLists'
  `10k-most-common.txt`, which is commonly credited to Mark Burnett's 2011
  "10,000 Top Passwords". Parman redistributed the list; he didn't compile
  it.

## Why it's public domain

Parman, as the redistributor, dedicates it to the public domain under
**CC0 1.0** and **the Unlicense**. His README says: "My intention is to
release all rights to this documentation and make it available under the
Public Domain. [...] So, I am dual-licensing this work under CC0 and the
Unlicense." See the
[README](https://github.com/skyzyx/bad-passwords/blob/cfd1a9564b43e43c18c06ff0460c3617dc147d01/README.md),
[LICENSE-CC0](https://github.com/skyzyx/bad-passwords/blob/cfd1a9564b43e43c18c06ff0460c3617dc147d01/LICENSE-CC0)
and
[LICENSE-UNLICENSE](https://github.com/skyzyx/bad-passwords/blob/cfd1a9564b43e43c18c06ff0460c3617dc147d01/LICENSE-UNLICENSE).

That dedication is accepted (maintainer decision, 2026-11-10) for two
reasons:

- A frequency-ranked list of passwords people used is factual data, with
  no creative selection or arrangement to hold a copyright (in the US,
  *Feist v. Rural*, 1991).
- The distributor's explicit dedication is the best evidence available.

## What was changed

8 lines were removed: unambiguous slurs against a group of people, and a
Nazi leader's name. Ordinary crude words stay. The words aren't listed
here.

- **Result:** 9,992 lines, 72,964 bytes, SHA-256
  `e4307250b0679c96768927af1ae76f659d49798ed283fe667a5d7818a08079ac`.

## How it's used

Only 1 entry has 15 characters or more, and shorter passwords are refused
for length already. So the whole list is kept. Both the password and the
entries are compared as a skeleton: NFKC, then only letters, marks and
digits, lowercased. A password is refused when its skeleton is an entry
said once or several times in a row, optionally followed by digits. Those
digits can be no longer than the whole part before them (examples:
`passwordpassword`, `qwertyuiop12345`). See `PasswordRules.cs`.

Replace the list only with one that has an explicit public-domain
dedication (maintainer decision, 2026-11-10), and update this note.

#!/usr/bin/env node
// Builds the GitHub wiki pages from docs/ (#244).
//
// docs/ is the source of truth. This script copies the writer-facing docs
// into wiki pages, rewrites their links for the wiki, and can check that
// every link on every page resolves. .github/workflows/wiki.yml runs it on
// each push to main and pushes the result to the wiki repo.
//
//   node scripts/wiki/wiki.mjs --out <folder> [--check] [--repo owner/name] [--ref main]
//
// --out    folder to write the pages to (created if missing; existing
//          .md files in it are left alone, so use an empty folder)
// --check  after building, check every link and exit 1 if any is broken
// --repo   repository for links to files that aren't published
//          (default: $GITHUB_REPOSITORY, else gavin-wolfe/wolfden)
// --ref    branch those links point at (default: main)
//
// No dependencies beyond Node itself. Tests: node --test scripts/wiki/wiki.test.mjs

import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { parseArgs } from 'node:util';

// Published pages: repo source file -> wiki page name. A doc that isn't
// listed here isn't published, and links to it point at the repo instead.
// To publish another doc, add it here and link it from docs/wiki/_Sidebar.md.
export const PAGES = [
  { source: 'docs/wiki/Home.md', page: 'Home', keepTitle: true },
  { source: 'docs/wiki/_Sidebar.md', page: '_Sidebar', note: false },
  { source: 'docs/using-wolfden.md', page: 'Using-Wolfden' },
  { source: 'docs/where-to-file.md', page: 'Where-to-File-Things' },
  { source: 'docs/mcp-clients.md', page: 'Connecting-MCP-Clients' },
  { source: 'docs/types.md', page: 'Types-and-Packs' },
  { source: 'docs/format.md', page: 'Format-Specification' },
];

const IMAGE_EXT = /\.(png|jpe?g|gif|svg|webp|avif|bmp|ico)$/i;
const SCHEME = /^[a-z][a-z0-9+.-]*:/i;

// ---------------------------------------------------------------------------
// Markdown scanning
//
// Not a full Markdown parser: just enough to find link targets outside code.
// Fenced code blocks and inline code spans are passed through untouched.

/** Splits markdown into { code: boolean, text } pieces at fenced code blocks. */
export function splitFences(md) {
  const lines = md.split('\n');
  const parts = [];
  let buf = [];
  let fence = null; // { ch, len }
  const flush = (code) => {
    if (buf.length) parts.push({ code, text: buf.join('\n') });
    buf = [];
  };
  for (const line of lines) {
    if (!fence) {
      const m = /^\s*(`{3,}|~{3,})/.exec(line);
      if (m && !(m[1][0] === '`' && line.slice(m.index + m[0].length).includes('`'))) {
        flush(false);
        fence = { ch: m[1][0], len: m[1].length };
        buf.push(line);
        continue;
      }
      buf.push(line);
    } else {
      buf.push(line);
      const m = /^\s*(`{3,}|~{3,})\s*$/.exec(line);
      if (m && m[1][0] === fence.ch && m[1].length >= fence.len) {
        flush(true);
        fence = null;
      }
    }
  }
  flush(fence !== null); // an unclosed fence runs to the end, as in CommonMark
  // Re-join with the newlines the split removed.
  return parts.map((p, i) => (i < parts.length - 1 ? { ...p, text: p.text + '\n' } : p));
}

const BLANK_LINE = /\n[ \t]*(\n|$)/y;

/** Splits text (outside fences) into { code: boolean, text } pieces at inline code spans. */
export function splitCodeSpans(text) {
  const parts = [];
  let i = 0;
  let start = 0;
  while (i < text.length) {
    if (text[i] === '\\') { i += 2; continue; }
    if (text[i] !== '`') { i++; continue; }
    let n = 0;
    while (text[i + n] === '`') n++;
    // Find the next run of exactly n backticks, before a blank line.
    let j = i + n;
    let close = -1;
    while (j < text.length) {
      if (text[j] === '\n') {
        BLANK_LINE.lastIndex = j;
        if (BLANK_LINE.test(text)) break; // a code span can't cross a paragraph break
      }
      if (text[j] === '`') {
        let k = 0;
        while (text[j + k] === '`') k++;
        if (k === n) { close = j; break; }
        j += k;
        continue;
      }
      j++;
    }
    if (close < 0) { i += n; continue; }
    if (i > start) parts.push({ code: false, text: text.slice(start, i) });
    parts.push({ code: true, text: text.slice(i, close + n) });
    i = close + n;
    start = i;
  }
  if (start < text.length) parts.push({ code: false, text: text.slice(start) });
  return parts;
}

// Inline link or image target: ](target) or ](target "title"), and
// reference definitions: [label]: target
const INLINE_LINK = /\]\(\s*(<[^<>\n]*>|[^\s()<>]+(?:\([^\s()]*\)[^\s()<>]*)*)((?:\s+(?:"[^"\n]*"|'[^'\n]*'|\([^)\n]*\)))?\s*\))/g;
const REF_DEF = /^( {0,3}\[[^\]\n]+\]:[ \t]*)(<[^<>\n]*>|\S+)/;

/** Applies fn to every piece of text outside fenced code blocks and code spans. */
export function mapProse(md, fn) {
  return splitFences(md)
    .map((block) =>
      block.code
        ? block.text
        : splitCodeSpans(block.text)
            .map((span) => (span.code ? span.text : fn(span.text)))
            .join(''),
    )
    .join('');
}

/** Calls fn(target) for every link target outside code and replaces it with the result. */
export function mapLinks(md, fn) {
  const inline = mapProse(md, (text) =>
    text.replace(INLINE_LINK, (all, target, rest) => `](${wrap(target, fn(unwrap(target)))}${rest}`),
  );
  // A reference definition can't interrupt a paragraph, so it starts after a
  // blank line, a heading, or another definition. "[warning]: ..." in the
  // middle of a paragraph is just text.
  return splitFences(inline)
    .map((block) => {
      if (block.code) return block.text;
      let canStart = true;
      return block.text
        .split('\n')
        .map((line) => {
          const m = canStart ? REF_DEF.exec(line) : null;
          canStart = m !== null || /^\s*$/.test(line) || /^ {0,3}#/.test(line);
          return m ? m[1] + wrap(m[2], fn(unwrap(m[2]))) + line.slice(m[0].length) : line;
        })
        .join('\n');
    })
    .join('');
}

/**
 * The wiki reads [[Name]] as a link to a wiki page, but in these docs it's
 * Wolfden's own link syntax, shown as an example. Outside code, write the
 * brackets as &#91; so they show as typed. (Inside code they can't be
 * escaped, and don't need to be.)
 */
export function escapeWikiLinks(md) {
  return mapProse(md, (text) => text.replace(/\[\[([^[\]\n]*)\]\]/g, '&#91;&#91;$1]]'));
}

/** Returns every link target outside code. */
export function findLinks(md) {
  const found = [];
  mapLinks(md, (t) => { found.push(t); return t; });
  return found;
}

const unwrap = (t) => (t.startsWith('<') && t.endsWith('>') ? t.slice(1, -1) : t);
const wrap = (original, value) =>
  original.startsWith('<') && /\s/.test(value) ? `<${value}>` : value.replace(/ /g, '%20');

// ---------------------------------------------------------------------------
// Headings and anchors

/** GitHub's heading anchor for a heading's text (github-slugger rules). */
export function slug(text) {
  const plain = text
    .replace(/!?\[([^\]]*)\]\([^)]*\)/g, '$1') // links and images keep their text
    .replace(/`/g, '')
    .replace(/<[^>]+>/g, '')
    .replace(/(\*\*|__|\*|~~)/g, '');
  return plain
    .trim()
    .toLowerCase()
    .replace(/[^\p{L}\p{M}\p{N}\p{Pc} -]/gu, '')
    .replace(/ /g, '-');
}

/** The set of anchors a page's headings produce, with GitHub's -1, -2 suffixes for repeats. */
export function anchors(md) {
  const seen = new Map();
  const out = new Set();
  for (const block of splitFences(md)) {
    if (block.code) continue;
    for (const line of block.text.split('\n')) {
      const m = /^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$/.exec(line);
      if (!m) continue;
      const base = slug(m[2]);
      const n = seen.get(base) ?? 0;
      seen.set(base, n + 1);
      out.add(n === 0 ? base : `${base}-${n}`);
    }
    for (const m of block.text.matchAll(/<a\s[^>]*(?:id|name)="([^"]+)"/g)) out.add(m[1]);
  }
  return out;
}

// ---------------------------------------------------------------------------
// Link rewriting

/**
 * Rewrites one link target from a page whose source is `source` (a repo path
 * such as docs/types.md) for the wiki.
 *
 * - External links and in-page anchors (#x) are unchanged.
 * - Links to a published doc become its wiki page: format.md#x -> Format-Specification#x.
 * - Images become raw file links in the repo, so they display.
 * - Anything else in the repo (design.md, code, samples/) becomes a link to
 *   it on GitHub, at `ref`.
 *
 * `isDir(repoPath)` says whether a repo path is a folder (tree/ rather than blob/).
 */
export function rewriteTarget(target, { source, repo, ref = 'main', pages = PAGES, isDir = () => false }) {
  if (!target || target.startsWith('#') || SCHEME.test(target) || target.startsWith('//')) return target;
  const hashAt = target.indexOf('#');
  const pathPart = hashAt < 0 ? target : target.slice(0, hashAt);
  const hash = hashAt < 0 ? '' : target.slice(hashAt);
  const queryAt = pathPart.indexOf('?');
  const rawPath = queryAt < 0 ? pathPart : pathPart.slice(0, queryAt);
  let decoded;
  try { decoded = decodeURIComponent(rawPath); } catch { decoded = rawPath; }

  const resolved = decoded.startsWith('/')
    ? path.posix.normalize(decoded.slice(1))
    : path.posix.normalize(path.posix.join(path.posix.dirname(source), decoded));
  if (resolved === '..' || resolved.startsWith('../')) {
    throw new Error(`${source}: link ${target} points outside the repository`);
  }
  const repoPath = resolved.replace(/\/$/, '').replace(/^\.$/, '');

  const page = pages.find((p) => p.source === repoPath);
  if (page) return page.page + hash;

  const encoded = repoPath.split('/').map(encodeURIComponent).join('/');
  const kind = IMAGE_EXT.test(repoPath) ? 'raw' : decoded.endsWith('/') || isDir(repoPath) ? 'tree' : 'blob';
  return `https://github.com/${repo}/${kind}/${ref}${encoded ? '/' + encoded : ''}${hash}`;
}

/** The header note at the top of each generated page. */
export function headerNote(source, repo, ref = 'main') {
  return `> Generated from [${source}](https://github.com/${repo}/blob/${ref}/${source}). Edit it there, not here.\n\n`;
}

/**
 * Builds one wiki page from its source markdown. The wiki shows the page
 * name as its title, so a leading # title line is dropped unless keepTitle.
 */
export function buildPage(md, { source, repo, ref = 'main', pages = PAGES, isDir, note = true, keepTitle = false }) {
  let body = md.replace(/\r\n?/g, '\n').replace(/^﻿/, '');
  if (!keepTitle) body = body.replace(/^# [^\n]*\n+/, '');
  body = mapLinks(body, (t) => rewriteTarget(t, { source, repo, ref, pages, isDir }));
  body = escapeWikiLinks(body);
  return (note ? headerNote(source, repo, ref) : '') + body;
}

/** Builds every page into outDir. Returns the list of files written. */
export function buildAll({ root, outDir, repo, ref = 'main', pages = PAGES }) {
  mkdirSync(outDir, { recursive: true });
  const isDir = (p) => {
    const full = path.join(root, p);
    return existsSync(full) && statSync(full).isDirectory();
  };
  const written = [];
  for (const p of pages) {
    const md = readFileSync(path.join(root, p.source), 'utf8');
    const out = buildPage(md, { source: p.source, repo, ref, pages, isDir, note: p.note !== false, keepTitle: p.keepTitle === true });
    const file = path.join(outDir, `${p.page}.md`);
    writeFileSync(file, out);
    written.push(file);
  }
  return written;
}

// ---------------------------------------------------------------------------
// Checking

/**
 * Checks every link on every built page. Returns a list of problems, each
 * "Page: target: why". Checks wiki page links and their anchors, in-page
 * anchors, and links into this repo on GitHub (the file exists; a .md
 * file's anchor exists). Other external links aren't fetched.
 */
export function checkAll({ root, outDir, repo, ref = 'main', pages = PAGES }) {
  const problems = [];
  const pageText = new Map(pages.map((p) => [p.page, readFileSync(path.join(outDir, `${p.page}.md`), 'utf8')]));
  const pageAnchors = new Map([...pageText].map(([k, v]) => [k, anchors(v)]));
  const repoAnchors = new Map();
  const repoPrefix = `https://github.com/${repo}/`;

  for (const [page, text] of pageText) {
    for (const target of findLinks(text)) {
      const fail = (why) => problems.push(`${page}: ${target}: ${why}`);
      const hashAt = target.indexOf('#');
      const base = hashAt < 0 ? target : target.slice(0, hashAt);
      const frag = hashAt < 0 ? '' : decodeURIComponent(target.slice(hashAt + 1));

      if (base === '') {
        if (page.startsWith('_')) fail('an in-page anchor on a sidebar or footer points at whichever page is showing');
        else if (!pageAnchors.get(page).has(frag)) fail('no such heading on this page');
      } else if (target.startsWith(repoPrefix)) {
        const m = /^(blob|tree|raw)\/([^/]+)(?:\/(.*))?$/.exec(base.slice(repoPrefix.length));
        if (!m) continue; // issues, wiki, and other GitHub pages
        if (m[2] !== ref) { fail(`points at ${m[2]}, not ${ref}`); continue; }
        const repoPath = decodeURIComponent(m[3] ?? '');
        const full = path.join(root, repoPath);
        if (!existsSync(full)) { fail('no such file in the repository'); continue; }
        const dir = statSync(full).isDirectory();
        if (m[1] === 'tree' && !dir) fail('tree/ link to a file');
        if (m[1] !== 'tree' && dir) fail(`${m[1]}/ link to a folder`);
        if (frag && repoPath.endsWith('.md')) {
          if (!repoAnchors.has(repoPath)) repoAnchors.set(repoPath, anchors(readFileSync(full, 'utf8').replace(/\r\n?/g, '\n')));
          if (!repoAnchors.get(repoPath).has(frag)) fail(`no such heading in ${repoPath}`);
        }
      } else if (SCHEME.test(target) || target.startsWith('//')) {
        // External; not fetched.
      } else if (!base.includes('/') && pageText.has(base)) {
        if (frag && !pageAnchors.get(base).has(frag)) fail(`no such heading on ${base}`);
      } else {
        fail('not a wiki page, a repository link, or an external link');
      }
    }
  }
  return problems;
}

// ---------------------------------------------------------------------------
// Command line

function main() {
  const { values } = parseArgs({
    options: {
      out: { type: 'string' },
      check: { type: 'boolean', default: false },
      repo: { type: 'string', default: process.env.GITHUB_REPOSITORY || 'gavin-wolfe/wolfden' },
      ref: { type: 'string', default: 'main' },
    },
  });
  if (!values.out) {
    console.error('usage: node scripts/wiki/wiki.mjs --out <folder> [--check] [--repo owner/name] [--ref main]');
    process.exit(2);
  }
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
  const opts = { root, outDir: path.resolve(values.out), repo: values.repo, ref: values.ref };
  const written = buildAll(opts);
  console.log(`Built ${written.length} wiki pages in ${opts.outDir}`);
  if (values.check) {
    const problems = checkAll(opts);
    for (const p of problems) console.error(`broken link: ${p}`);
    if (problems.length) {
      console.error(`${problems.length} broken link(s).`);
      process.exit(1);
    }
    console.log('Every link resolves.');
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) main();

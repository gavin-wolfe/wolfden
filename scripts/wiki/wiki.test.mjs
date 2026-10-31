// Tests for the wiki page builder. Run: node --test scripts/wiki/wiki.test.mjs
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { after, describe, test } from 'node:test';
import { anchors, buildAll, buildPage, checkAll, escapeWikiLinks, findLinks, mapLinks, rewriteTarget, slug } from './wiki.mjs';

const repo = 'owner/repo';
const pages = [
  { source: 'docs/wiki/Home.md', page: 'Home', keepTitle: true },
  { source: 'docs/wiki/_Sidebar.md', page: '_Sidebar', note: false },
  { source: 'docs/guide.md', page: 'Guide' },
  { source: 'docs/format.md', page: 'Format-Specification' },
];
const from = (source, extra = {}) => (t) => rewriteTarget(t, { source, repo, pages, ...extra });

describe('rewriteTarget', () => {
  const docs = from('docs/guide.md', { isDir: (p) => p === 'samples/dracula' || p === 'packs/core' });

  test('a published doc becomes its wiki page, keeping the anchor', () => {
    assert.equal(docs('format.md'), 'Format-Specification');
    assert.equal(docs('format.md#64-bookmd'), 'Format-Specification#64-bookmd');
    assert.equal(docs('./format.md#x'), 'Format-Specification#x');
  });

  test('links from docs/wiki/ resolve from that folder', () => {
    const home = from('docs/wiki/Home.md');
    assert.equal(home('../guide.md#getting-started'), 'Guide#getting-started');
    assert.equal(home('Home.md'), 'Home');
    assert.equal(home('_Sidebar.md'), '_Sidebar');
  });

  test('an unpublished doc points at the repo on GitHub', () => {
    assert.equal(docs('design.md'), 'https://github.com/owner/repo/blob/main/docs/design.md');
    assert.equal(docs('design.md#scope-and-inheritance'), 'https://github.com/owner/repo/blob/main/docs/design.md#scope-and-inheritance');
    assert.equal(docs('../CONTRIBUTING.md'), 'https://github.com/owner/repo/blob/main/CONTRIBUTING.md');
  });

  test('code files point at blob/, folders at tree/', () => {
    assert.equal(docs('../src/Wolfden.Cli/Program.cs'), 'https://github.com/owner/repo/blob/main/src/Wolfden.Cli/Program.cs');
    assert.equal(docs('../samples/dracula/'), 'https://github.com/owner/repo/tree/main/samples/dracula');
    assert.equal(docs('../packs/core'), 'https://github.com/owner/repo/tree/main/packs/core');
  });

  test('images point at raw/, so they display', () => {
    assert.equal(docs('images/map.png'), 'https://github.com/owner/repo/raw/main/docs/images/map.png');
    assert.equal(docs('/images/Seal.JPG'), 'https://github.com/owner/repo/raw/main/images/Seal.JPG');
  });

  test('spaces and percent-encoding survive', () => {
    assert.equal(docs('images/castle%20map.png'), 'https://github.com/owner/repo/raw/main/docs/images/castle%20map.png');
    assert.equal(docs('images/castle map.png'), 'https://github.com/owner/repo/raw/main/docs/images/castle%20map.png');
  });

  test('a leading / is the repo root', () => {
    assert.equal(docs('/README.md'), 'https://github.com/owner/repo/blob/main/README.md');
    assert.equal(docs('/docs/format.md#x'), 'Format-Specification#x');
  });

  test('external links, anchors, and mailto are unchanged', () => {
    for (const t of ['https://obsidian.md', 'http://x.y/z.md', 'mailto:a@b.c', '#gotchas', '//cdn.example/x.png']) {
      assert.equal(docs(t), t);
    }
  });

  test('the ref is configurable', () => {
    assert.equal(from('docs/guide.md', { ref: 'dev' })('design.md'), 'https://github.com/owner/repo/blob/dev/docs/design.md');
  });

  test('a link out of the repo is an error', () => {
    assert.throws(() => docs('../../outside.md'), /outside the repository/);
  });
});

describe('mapLinks', () => {
  const rewrite = (md) => mapLinks(md, from('docs/guide.md'));

  test('rewrites inline links, images, and titles', () => {
    assert.equal(rewrite('See [the spec](format.md#x).'), 'See [the spec](Format-Specification#x).');
    assert.equal(rewrite('![Map](images/m.png "A map")'), '![Map](https://github.com/owner/repo/raw/main/docs/images/m.png "A map")');
    assert.equal(rewrite('[`format.md`](format.md)'), '[`format.md`](Format-Specification)');
  });

  test('rewrites angle-bracket targets', () => {
    assert.equal(rewrite('[a](<format.md>)'), '[a](Format-Specification)');
    assert.equal(rewrite('![a](<images/castle map.png>)'), '![a](https://github.com/owner/repo/raw/main/docs/images/castle%20map.png)');
  });

  test('rewrites link text that wraps across lines', () => {
    assert.equal(rewrite('[add series and\nbooks](format.md#y)'), '[add series and\nbooks](Format-Specification#y)');
  });

  test('rewrites reference definitions that start a paragraph', () => {
    assert.equal(rewrite('Text.\n\n[spec]: format.md#x\n[d]: design.md\n'),
      'Text.\n\n[spec]: Format-Specification#x\n[d]: https://github.com/owner/repo/blob/main/docs/design.md\n');
  });

  test('leaves "[label]: text" in the middle of a paragraph alone', () => {
    const md = 'Some problems drop only the item, with a\n[warning]: one field definition.\n';
    assert.equal(rewrite(md), md);
  });

  test('leaves inline code alone, including spans across lines', () => {
    const md = 'Write `![Map](images/m.png)` or ``a `[x](format.md)` b``, and `[y](format.md)\nstill code`.';
    assert.equal(rewrite(md), md);
  });

  test('leaves fenced code alone, including indented and tilde fences', () => {
    const md = '```md\n[a](format.md)\n```\n\n   ~~~~\n   [b](format.md)\n   ~~~~\n\n[c](format.md)\n';
    assert.equal(rewrite(md), md.replace('[c](format.md)', '[c](Format-Specification)'));
  });

  test('an unclosed backtick is literal, not code', () => {
    assert.equal(rewrite('A ` here, then [a](format.md).'), 'A ` here, then [a](Format-Specification).');
  });

  test('a backtick run doesn\'t pair across a blank line', () => {
    assert.equal(rewrite('One `tick.\n\nThen [a](format.md) and ` more.'), 'One `tick.\n\nThen [a](Format-Specification) and ` more.');
  });

  test('findLinks lists targets outside code only', () => {
    assert.deepEqual(findLinks('[a](x.md) `[b](y.md)`\n```\n[c](z.md)\n```\n[d]: w.md\n'), ['x.md', 'w.md']);
  });
});

describe('escapeWikiLinks', () => {
  test('escapes [[...]] in prose so the wiki doesn\'t make it a page link', () => {
    assert.equal(escapeWikiLinks('Turn on **Use [[Wikilinks]]**.'), 'Turn on **Use &#91;&#91;Wikilinks]]**.');
  });

  test('writes an inline code span with [[ as <code>, with its punctuation as entities', () => {
    assert.equal(escapeWikiLinks('Type `[[Mina Harker]]`.'), 'Type <code>&#91;&#91;Mina Harker&#93;&#93;</code>.');
    assert.equal(escapeWikiLinks('`pov: "[[a|B]]" <x> & *y*`'),
      '<code>pov&#58; &#34;&#91;&#91;a&#124;B&#93;&#93;&#34; &#60;x&#62; &#38; &#42;y&#42;</code>');
  });

  test('a converted span keeps CommonMark\'s spacing, and links in it stay text', () => {
    assert.equal(escapeWikiLinks('`` `[[a]]` ``'), '<code>&#96;&#91;&#91;a&#93;&#93;&#96;</code>');
    assert.equal(escapeWikiLinks('`[[a]]\n[b](c.md)`'), '<code>&#91;&#91;a&#93;&#93; &#91;b&#93;&#40;c&#46;md&#41;</code>');
    assert.deepEqual(findLinks(escapeWikiLinks('`[[a]] [b](c.md)`')), []);
  });

  test('leaves code spans without [[ and fenced blocks alone', () => {
    const md = 'Type `[x](y.md)`.\n```yaml\npov: "[[mara-voss]]"\n```\n';
    assert.equal(escapeWikiLinks(md), md);
  });
});

describe('slug and anchors', () => {
  test('match GitHub\'s heading anchors', () => {
    assert.equal(slug('6.4 `book.md`'), '64-bookmd');
    assert.equal(slug('1. Build Wolfden'), '1-build-wolfden');
    assert.equal(slug('Chapter fields: POV, when, and the rest'), 'chapter-fields-pov-when-and-the-rest');
    assert.equal(slug('The habit: one checkpoint per notes pass'), 'the-habit-one-checkpoint-per-notes-pass');
    assert.equal(slug('Entities don\'t span universes'), 'entities-dont-span-universes');
    assert.equal(slug('8. AI integration (MCP)'), '8-ai-integration-mcp');
  });

  test('repeats get -1, -2, and headings in code don\'t count', () => {
    const a = anchors('## Overview\n```md\n# Overview\n```\n## Overview\n### Overview\n');
    assert.deepEqual([...a].sort(), ['overview', 'overview-1', 'overview-2']);
  });

  test('a repeat skips a suffixed slug that\'s already taken, as github-slugger does', () => {
    assert.deepEqual([...anchors('## A\n## A\n## A-1\n')], ['a', 'a-1', 'a-1-1']);
    assert.deepEqual([...anchors('## A-1\n## A\n## A\n')], ['a-1', 'a', 'a-2']);
  });

  test('a heading the build escaped keeps the anchor it has in docs/', () => {
    const src = '## Linking with [[names]]\n\n## The `[[name|alias]]` form\n\n## A & B\n';
    const out = buildPage(src, { source: 'docs/guide.md', repo, pages, note: false });
    assert.notEqual(out, src);
    assert.deepEqual([...anchors(out)], [...anchors(src)]);
    assert.deepEqual([...anchors(src)], ['linking-with-names', 'the-namealias-form', 'a--b']);
  });
});

describe('buildPage', () => {
  test('adds the header note and drops the title line', () => {
    const out = buildPage('# Guide\n\nSee [spec](format.md).\n', { source: 'docs/guide.md', repo, pages });
    assert.equal(out,
      '> Generated from [docs/guide.md](https://github.com/owner/repo/blob/main/docs/guide.md). Edit it there, not here.\n\n' +
      'See [spec](Format-Specification).\n');
  });

  test('keeps the title when asked, and can skip the note', () => {
    const out = buildPage('# Wolfden\n\nHi.\n', { source: 'docs/wiki/_Sidebar.md', repo, pages, note: false, keepTitle: true });
    assert.equal(out, '# Wolfden\n\nHi.\n');
  });

  test('reads CRLF sources', () => {
    const out = buildPage('# T\r\n\r\n[a](format.md)\r\n', { source: 'docs/guide.md', repo, pages, note: false });
    assert.equal(out, '[a](Format-Specification)\n');
  });
});

describe('checkAll', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'wiki-test-'));
  after(() => rmSync(root, { recursive: true, force: true }));
  const write = (p, text) => {
    mkdirSync(path.dirname(path.join(root, p)), { recursive: true });
    writeFileSync(path.join(root, p), text);
  };

  test('passes when every link resolves, and reports each broken one', () => {
    write('docs/wiki/Home.md', '# Wolfden\n\n[Start](../guide.md#start) · [Contributing](../../CONTRIBUTING.md)\n');
    write('docs/wiki/_Sidebar.md', '[Home](Home.md)\n[Spec](../format.md#keys)\n');
    write('docs/guide.md', '# Guide\n\n## Start\n\n[Keys](format.md#keys) · [Design](design.md#why) · [Up](#start) · [Web](https://example.com)\n');
    write('docs/format.md', '# Format\n\n## Keys\n');
    write('docs/design.md', '# Design\n\n## Why\n');
    write('CONTRIBUTING.md', '# Contributing\n');
    const outDir = path.join(root, 'out');
    buildAll({ root, outDir, repo, pages });
    assert.deepEqual(checkAll({ root, outDir, repo, pages }), []);

    write('docs/guide.md', '# Guide\n\n## Start\n\n[a](format.md#nope) [b](design.md#nope) [c](#nope) [d](missing.md) [e](../samples/)\n');
    write('docs/wiki/_Sidebar.md', '[Home](Home.md)\n[Here](#start)\n');
    buildAll({ root, outDir, repo, pages });
    const problems = checkAll({ root, outDir, repo, pages });
    assert.equal(problems.length, 6, problems.join('\n'));
    assert.match(problems.join('\n'), /Guide: Format-Specification#nope: no such heading on Format-Specification/);
    assert.match(problems.join('\n'), /Guide: .*design\.md#nope: no such heading in docs\/design\.md/);
    assert.match(problems.join('\n'), /Guide: #nope: no such heading on this page/);
    assert.match(problems.join('\n'), /Guide: .*docs\/missing\.md: no such file/);
    assert.match(problems.join('\n'), /Guide: .*tree\/main\/samples: no such file/);
    assert.match(problems.join('\n'), /_Sidebar: #start: an in-page anchor on a sidebar/);
  });

  test('a malformed % escape is a broken link, not a crash', () => {
    write('docs/guide.md', '# Guide\n\n## Start\n\n[a](format.md#bad%zz) [b](#bad%E0%A4%A) [c](../bad%zz.md)\n');
    write('docs/wiki/_Sidebar.md', '[Home](Home.md)\n');
    const outDir = path.join(root, 'out');
    buildAll({ root, outDir, repo, pages });
    const problems = checkAll({ root, outDir, repo, pages });
    assert.equal(problems.length, 3, problems.join('\n'));
    assert.match(problems.join('\n'), /Guide: Format-Specification#bad%zz: malformed % escape/);
    assert.match(problems.join('\n'), /Guide: #bad%E0%A4%A: malformed % escape/);
    assert.match(problems.join('\n'), /Guide: .*bad%25zz\.md: no such file/);
  });
});

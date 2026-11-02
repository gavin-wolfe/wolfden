#!/usr/bin/env node
// Replays the Starfall sample's history into a fresh git repository (#34),
// so the version history features have real data: a retcon as its own
// checkpoint, and a book frozen partway through.
//
//   node scripts/starfall-history/replay.mjs <new-folder>
//
// <new-folder> must not exist, or must be empty, and must not be inside a git
// repository such as this clone. The script never overwrites anything. It
// runs only local git commands, in that folder, with git's system and global
// settings turned off, so it signs nothing and leaves this repository alone.
// When it finishes, the folder is a den: `wolfden serve <new-folder>`.
//
// How the history is stored:
//
//   checkpoints.json   the checkpoints in order. `final` lists the files (or
//                      folders, ending in /) that take their current text
//                      from samples/starfall at that checkpoint. `remove`
//                      lists files that stop existing. `tag` adds a tag.
//   versions/NN/       older text. A file here is how that path read as of
//                      checkpoint NN, until a later checkpoint changes it.
//
// So only text that has since changed is stored twice. Any file of the sample
// that no checkpoint names arrives at the last checkpoint, which means a new
// note in the sample needs no change here. The script checks that the end
// state matches samples/starfall byte for byte, and fails if it doesn't.
//
// No dependencies beyond Node and git. Tests: StarfallReplayTests, in
// tests/Wolfden.Mcp.Tests.

import { execFileSync } from 'node:child_process';
import { copyFileSync, existsSync, lstatSync, mkdirSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const sampleRoot = path.join(repoRoot, 'samples', 'starfall');
const author = { name: 'Wren Calder', email: 'wren.calder@example.invalid' };

/** A problem to report in a sentence, not a stack trace. */
class Refusal extends Error {}

/** Every file under a folder, as sorted '/' paths, skipping local state. */
function filesUnder(root, folder = '') {
  const found = [];
  for (const entry of readdirSync(path.join(root, folder), { withFileTypes: true })) {
    const relative = folder ? `${folder}/${entry.name}` : entry.name;
    if (entry.isDirectory()) {
      if (entry.name !== '.git' && entry.name !== '.wolfden') {
        found.push(...filesUnder(root, relative));
      }
    } else {
      found.push(relative);
    }
  }
  return found.sort();
}

/** The nearest folder at or above this one that holds a .git, or null. */
function enclosingRepository(folder) {
  for (let at = folder; ; at = path.dirname(at)) {
    if (existsSync(path.join(at, '.git'))) {
      return at;
    }
    if (path.dirname(at) === at) {
      return null;
    }
  }
}

function isInside(folder, parent) {
  const relative = path.relative(parent, folder);
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

/** Refuses anything but a new or empty folder outside every repository. */
function checkTarget(target) {
  let stat = null;
  try {
    stat = lstatSync(target);
  } catch (error) {
    if (error.code !== 'ENOENT') {
      throw error;
    }
  }
  if (stat && (!stat.isDirectory() || readdirSync(target).length > 0)) {
    throw new Refusal(`${target} already exists and isn't an empty folder. Nothing was changed. Name a new folder.`);
  }
  const repository = enclosingRepository(target);
  if (repository) {
    throw new Refusal(`${target} is inside the git repository at ${repository}. Nothing was changed. Name a folder outside it.`);
  }
  if (isInside(target, repoRoot)) {
    throw new Refusal(`${target} is inside this copy of Wolfden. Nothing was changed. Name a folder outside it.`);
  }
}

/** Git's environment here: no settings from this machine, and no pointers at another repository. */
function gitEnvironment(date) {
  const env = Object.fromEntries(Object.entries(process.env).filter(([name]) => !/^GIT_/i.test(name)));
  return {
    ...env,
    GIT_CONFIG_NOSYSTEM: '1',
    GIT_CONFIG_GLOBAL: path.join(here, 'no-settings.gitconfig'),
    GIT_AUTHOR_NAME: author.name,
    GIT_AUTHOR_EMAIL: author.email,
    GIT_COMMITTER_NAME: author.name,
    GIT_COMMITTER_EMAIL: author.email,
    ...(date ? { GIT_AUTHOR_DATE: date, GIT_COMMITTER_DATE: date } : {}),
  };
}

function git(cwd, date, ...args) {
  return execFileSync('git', args, {
    cwd,
    env: gitEnvironment(date),
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
  });
}

function copyInto(fromRoot, relative, toRoot) {
  const destination = path.join(toRoot, relative);
  mkdirSync(path.dirname(destination), { recursive: true });
  copyFileSync(path.join(fromRoot, relative), destination);
}

/** The sample's files each checkpoint gives their final text; the last one takes whatever is left. */
function planFinals(checkpoints, sampleFiles) {
  const left = new Set(sampleFiles);
  const plan = checkpoints.map((checkpoint) => {
    const files = [];
    for (const name of checkpoint.final ?? []) {
      const matches = name.endsWith('/') ? sampleFiles.filter((file) => file.startsWith(name)) : sampleFiles.filter((file) => file === name);
      if (matches.length === 0) {
        throw new Refusal(`checkpoints.json names '${name}', which isn't in samples/starfall. Nothing was changed.`);
      }
      files.push(...matches);
      matches.forEach((file) => left.delete(file));
    }
    return files;
  });
  plan[plan.length - 1].push(...left);
  return plan;
}

/** Fails if the end state isn't the sample, byte for byte. */
function verify(target, sampleFiles) {
  const written = filesUnder(target);
  const differs = [
    ...sampleFiles.filter((file) => !written.includes(file)),
    ...written.filter((file) => !sampleFiles.includes(file)),
    ...sampleFiles.filter((file) => written.includes(file)
      && !readFileSync(path.join(target, file)).equals(readFileSync(path.join(sampleRoot, file)))),
  ];
  if (differs.length > 0) {
    throw new Refusal(`The end state doesn't match samples/starfall: ${differs.join(', ')}. Delete ${target} once you've looked at it.`);
  }
}

function replay(target) {
  const checkpoints = JSON.parse(readFileSync(path.join(here, 'checkpoints.json'), 'utf8'));
  const sampleFiles = filesUnder(sampleRoot);
  const finals = planFinals(checkpoints, sampleFiles);
  checkTarget(target);
  git(os.tmpdir(), null, '--version');

  mkdirSync(target, { recursive: true });
  git(target, null, 'init', '--quiet', '--initial-branch=main', '--template=');
  checkpoints.forEach((checkpoint, index) => {
    const number = String(index + 1).padStart(2, '0');
    for (const file of checkpoint.remove ?? []) {
      rmSync(path.join(target, file));
      const folder = path.dirname(path.join(target, file));
      if (readdirSync(folder).length === 0) {
        rmSync(folder, { recursive: true });
      }
    }
    for (const file of finals[index]) {
      copyInto(sampleRoot, file, target);
    }
    const versions = path.join(here, 'versions', number);
    if (existsSync(versions)) {
      for (const file of filesUnder(versions)) {
        copyInto(versions, file, target);
      }
    }
    git(target, checkpoint.date, 'add', '--all');
    git(target, checkpoint.date, 'commit', '--quiet', '-m', checkpoint.message);
    if (checkpoint.tag) {
      git(target, checkpoint.date, 'tag', '-m', checkpoint.tagMessage, checkpoint.tag);
    }
    console.log(`${number}. ${checkpoint.message}${checkpoint.tag ? ` (tagged ${checkpoint.tag})` : ''}`);
  });
  verify(target, sampleFiles);
  console.log(`\nDone: ${checkpoints.length} checkpoints in ${target}. Try: wolfden serve "${target}"`);
}

function main(args) {
  if (args.length !== 1 || args[0].startsWith('-')) {
    console.error('Usage: node scripts/starfall-history/replay.mjs <new-folder>');
    return 2;
  }
  try {
    replay(path.resolve(args[0]));
    return 0;
  } catch (error) {
    if (error instanceof Refusal) {
      console.error(error.message);
      return 1;
    }
    console.error(`The replay failed: ${error.message}\nDelete ${args[0]} if it was started, then try again.`);
    return 1;
  }
}

process.exitCode = main(process.argv.slice(2));

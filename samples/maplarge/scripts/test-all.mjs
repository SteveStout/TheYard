/**
 * Every test The Shed has, run as one command with a short summary: `npm run test:all`.
 *
 * Each step runs in order with its output captured, and one line per step says what it found:
 * the counts each tool prints in its own summary, and the time it took. A green run prints the
 * summary and nothing else. A red step stops the run, prints everything that step printed, then
 * the summary, and exits with the step's code, so nothing is hidden and nothing after it runs.
 * `npm run test:all:full` is the same three commands with their whole output.
 *
 * No dependencies: Node's own child_process runs each command through the shell, which is how
 * npm itself runs scripts on Windows and Linux alike.
 */
import { spawn } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const TITLE = 'The Shed - all tests';
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/** The steps, in order. A step with a reader reports tests; one without reports a clean build. */
const STEPS = [
  { label: 'C# tests (xUnit)', command: 'dotnet test', read: dotnetCounts },
  { label: 'TypeScript build', command: 'npm run build' },
  { label: 'TypeScript tests', command: 'npm test', read: nodeTestCounts },
];

const LABEL_WIDTH = 21;
const COUNT_WIDTH = 3;

const tty = process.stdout.isTTY === true && !process.env.NO_COLOR;
const paint = (code, text) => (tty ? `\x1b[${code}m${text}\x1b[0m` : text);
const green = (text) => paint('32', text);
const red = (text) => paint('31', text);
const write = (text) => process.stdout.write(text);

/** Colour codes some tools print even into a pipe; the counts are read from the plain text. */
const ESC = String.fromCharCode(27);
const COLOUR = new RegExp(`${ESC}\\[[0-9;?]*[A-Za-z]`, 'g');
const plain = (text) => text.replace(COLOUR, '');

/**
 * dotnet test's counts. The terminal logger says "Test summary: total: N, failed: N,
 * succeeded: N"; with its output captured dotnet falls back to one "Passed!" or "Failed!" line
 * per test assembly ("Failed: N, Passed: N, Skipped: N, Total: N"). Either is summed.
 */
function dotnetCounts(output) {
  const text = plain(output);
  let passed = 0;
  let failed = 0;
  let found = false;
  for (const m of text.matchAll(/Test summary: total: (\d+), failed: (\d+), succeeded: (\d+)/g)) {
    failed += Number(m[2]);
    passed += Number(m[3]);
    found = true;
  }
  if (!found) {
    for (const m of text.matchAll(
      /Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+), Total:\s+(\d+)/g
    )) {
      failed += Number(m[1]);
      passed += Number(m[2]);
      found = true;
    }
  }
  return found ? { passed, failed } : null;
}

/** node --test's counts: "# pass N" and "# fail N" in its TAP output, or the spec reporter's own. */
function nodeTestCounts(output) {
  const text = plain(output);
  const pass = text.match(/^(?:#|ℹ)\s*pass (\d+)\s*$/m);
  const fail = text.match(/^(?:#|ℹ)\s*fail (\d+)\s*$/m);
  return pass && fail ? { passed: Number(pass[1]), failed: Number(fail[1]) } : null;
}

/** Runs one command in the project folder, output captured, and resolves with its code and text. */
function run(command) {
  return new Promise((resolve) => {
    const child = spawn(command, { cwd: ROOT, shell: true, env: process.env });
    let output = '';
    child.stdout.on('data', (chunk) => (output += chunk));
    child.stderr.on('data', (chunk) => (output += chunk));
    child.on('error', (error) => resolve({ code: 1, output: output + String(error) }));
    child.on('close', (code) => resolve({ code: code ?? 1, output }));
  });
}

const seconds = (ms) => `${(ms / 1000).toFixed(1).padStart(6)} s`;
const count = (n) => String(n).padStart(COUNT_WIDTH);

/** One summary line, from the step and what it found. */
function line(row) {
  const label = row.label.padEnd(LABEL_WIDTH);
  if (row.counts) {
    const failed = `${count(row.counts.failed)} failed`;
    return `${label}${count(row.counts.passed)} passed ${row.counts.failed > 0 ? red(failed) : failed} ${seconds(row.ms)}`;
  }
  const word = row.ok ? 'clean' : row.read ? 'failed' : 'errors';
  return `${label}${row.ok ? word : red(word)}${' '.repeat(22 - word.length)}${seconds(row.ms)}`;
}

const rows = [];
write(`${TITLE}\n`);

for (const step of STEPS) {
  if (tty) write(`${step.label.padEnd(LABEL_WIDTH)}running...`);
  const started = performance.now();
  const { code, output } = await run(step.command);
  const ms = performance.now() - started;
  const counts = step.read ? step.read(output) : null;
  const unread = step.read !== undefined && counts === null;
  const ok = code === 0 && !unread && (counts === null || counts.failed === 0);
  const row = { label: step.label, read: step.read, counts, ok, ms };
  rows.push(row);
  if (tty) write('\r\x1b[2K');

  if (!ok) {
    write(`\n----- ${step.label}: everything \`${step.command}\` printed -----\n`);
    write(output.endsWith('\n') ? output : `${output}\n`);
    write(`----- end of ${step.label} -----\n\n`);
    write(`${TITLE}\n`);
    for (const done of rows) write(`${line(done)}\n`);
    const why =
      code === 0 ? 'it exited 0 but its test counts could not be read' : `exit code ${code}`;
    write(`${red(`RED - ${step.label} failed, ${why}`)}\n`);
    // exitCode rather than exit(): a write to a Windows terminal is asynchronous, and exit()
    // could cut off the output a reader needs most.
    process.exitCode = code === 0 ? 1 : code;
    break;
  }
  write(`${line(row)}\n`);
}

if (process.exitCode === undefined) {
  const total = rows.reduce((sum, row) => sum + (row.counts ? row.counts.passed : 0), 0);
  write(`${green(`ALL GREEN - ${total} tests passed`)}\n`);
}

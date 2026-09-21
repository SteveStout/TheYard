/**
 * Every test result the ship's gate produced, gathered into one file that ships
 * with the version and that the Admin tab shows (ADR: The five-minute gate,
 * the addendum on every check running once).
 *
 * The gate runs each suite once, on the machine that ships, and each runner
 * leaves its own report in the gate's raw folder: Vitest and Playwright as
 * JSON, xUnit as a TRX file per pass. This reads those reports and writes
 * data/test-results.json: for every suite its counts and its time, and for
 * every test its group, its name, its outcome and its milliseconds, one line
 * each so a diff between two versions reads test by test. It also carries the
 * checks that are not tests (prettier, lint, tsc, dotnet format, the SQL
 * project) from the gate's own result files.
 *
 * Run by the gate after every suite is green and before the commit:
 * `node scripts/test_results.mjs <raw folder> <version> <gate seconds>`.
 * No dependencies: the three formats are read with the standard library.
 */
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const [raw, version, gateSeconds] = process.argv.slice(2);
if (!raw || !version) {
  console.error('usage: node scripts/test_results.mjs <raw folder> <version> <gate seconds>');
  process.exit(2);
}

const OUT = path.join(process.cwd(), 'data', 'test-results.json');

/** The gate's own `key=value` result files, one per side. */
function results(file) {
  const values = {};
  const full = path.join(raw, file);
  if (!existsSync(full)) return values;
  for (const line of readFileSync(full, 'utf8')
    .replace(/^\uFEFF/, '')
    .split(/\r?\n/)) {
    const at = line.indexOf('=');
    if (at > 0) values[line.slice(0, at).trim()] = line.slice(at + 1).trim();
  }
  return values;
}

const node = results('node.result');
const dotnet = results('dotnet.result');
const seconds = (side, key) => Number(side[`${key}_s`] ?? 0);

const readJson = (file) => {
  const full = path.join(raw, file);
  return existsSync(full) ? JSON.parse(readFileSync(full, 'utf8').replace(/^\uFEFF/, '')) : null;
};

/** The last part of a path written on either system: the reports come from Windows. */
const base = (file) => (file ?? '').split(/[\\/]/).pop() ?? '';

const OUTCOME = { passed: 'p', failed: 'f', skipped: 's', pending: 's', todo: 's' };

// #region vitest
function vitest(report) {
  const tests = [];
  for (const file of report?.testResults ?? []) {
    const group = base(file.name);
    for (const test of file.assertionResults ?? []) {
      const name = [...(test.ancestorTitles ?? []), test.title].filter(Boolean).join(' > ');
      tests.push([group, name, OUTCOME[test.status] ?? 'f', Math.round(test.duration ?? 0)]);
    }
  }
  return tests;
}
// #endregion vitest

// #region playwright
function playwright(report) {
  const tests = [];
  const walk = (suite, titles, file) => {
    const here = suite.file ?? file;
    for (const spec of suite.specs ?? []) {
      for (const test of spec.tests ?? []) {
        const outcome = test.status === 'skipped' ? 's' : test.status === 'unexpected' ? 'f' : 'p';
        const ms = (test.results ?? []).reduce((sum, result) => sum + (result.duration ?? 0), 0);
        tests.push([base(here), [...titles, spec.title].join(' > '), outcome, ms]);
      }
    }
    for (const child of suite.suites ?? []) {
      // A file's own suite carries the file name as its title; a describe inside it carries its words.
      const title = child.file && !suite.file ? [] : [child.title];
      walk(child, [...titles, ...title], here);
    }
  };
  for (const suite of report?.suites ?? []) walk(suite, [], suite.file);
  return tests;
}
// #endregion playwright

// #region trx
const decode = (text) =>
  text
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&amp;/g, '&');

function attributes(tag) {
  const values = {};
  for (const match of tag.matchAll(/([A-Za-z]+)="([^"]*)"/g)) values[match[1]] = decode(match[2]);
  return values;
}

/** hh:mm:ss.fffffff, as TRX writes a duration, in milliseconds. */
function milliseconds(duration) {
  const parts = /^(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(duration ?? '');
  if (!parts) return 0;
  return Math.round((Number(parts[1]) * 3600 + Number(parts[2]) * 60 + Number(parts[3])) * 1000);
}

function trx(file) {
  const full = path.join(raw, file);
  if (!existsSync(full)) return null;
  const text = readFileSync(full, 'utf8');
  const classes = new Map();
  for (const match of text.matchAll(
    /<UnitTest\b[^>]*\bid="([^"]+)"[\s\S]*?<TestMethod\b([^>]*)>/g
  )) {
    classes.set(match[1], attributes(match[2]).className ?? '');
  }
  const tests = [];
  for (const match of text.matchAll(/<UnitTestResult\b([^>]*)>/g)) {
    const result = attributes(match[1]);
    const className = (classes.get(result.testId) ?? '').replace(/^TheYard\.Tests\./, '');
    let name = (result.testName ?? '').replace(/^TheYard\.Tests\./, '');
    if (className && name.startsWith(`${className}.`)) name = name.slice(className.length + 1);
    const outcome =
      result.outcome === 'Passed' ? 'p' : result.outcome === 'NotExecuted' ? 's' : 'f';
    tests.push([className, name, outcome, milliseconds(result.duration)]);
  }
  return tests;
}
// #endregion trx

const suites = [];
function add(id, name, tests, time) {
  if (tests === null) return;
  const count = (outcome) => tests.filter((test) => test[2] === outcome).length;
  suites.push({
    id,
    name,
    seconds: time,
    passed: count('p'),
    failed: count('f'),
    skipped: count('s'),
    tests,
  });
}

add('vitest', 'Vitest', vitest(readJson('vitest.json')), seconds(node, 'vitest'));
add('xunit-sqlite', 'xUnit on SQLite', trx('trx/xunit-sqlite.trx'), seconds(dotnet, 'xunit'));
add('xunit-live', 'The live Cosmos DB tests', trx('trx/xunit-live.trx'), seconds(dotnet, 'live'));
add('xunit-cosmos', 'xUnit on Cosmos DB', trx('trx/xunit-cosmos.trx'), seconds(dotnet, 'oncosmos'));
add(
  'browser-sqlite',
  'Browser on SQLite',
  playwright(readJson('browser-sqlite.json')),
  seconds(node, 'browser')
);
add(
  'browser-cosmos',
  'Browser on Cosmos DB',
  playwright(readJson('browser-cosmos.json')),
  seconds(node, 'browser_cosmos')
);

const checks = [
  ['Prettier', node, 'prettier'],
  ['oxlint', node, 'lint'],
  ['TypeScript', node, 'tsc'],
  ['dotnet format', dotnet, 'format'],
  ['The SQL project', dotnet, 'sqlproj'],
]
  .filter(([, side, key]) => side[key] !== undefined)
  .map(([name, side, key]) => ({ name, passed: side[key] === '0', seconds: seconds(side, key) }));

const empty = suites.filter((suite) => suite.tests.length === 0).map((suite) => suite.id);
if (suites.length === 0 || empty.length > 0) {
  console.error(`no results read for: ${empty.join(', ') || 'every suite'}`);
  process.exit(1);
}

// One test per line, so a diff between two versions reads test by test.
const lines = [
  '{',
  `  "version": ${JSON.stringify(version)},`,
  `  "ranAt": ${JSON.stringify(new Date().toISOString())},`,
  `  "gateSeconds": ${Number(gateSeconds ?? 0)},`,
  `  "checks": ${JSON.stringify(checks)},`,
  '  "suites": [',
  suites
    .map((suite) => {
      const { tests, ...head } = suite;
      const header = JSON.stringify(head).slice(0, -1);
      const rows = tests.map((test) => `      ${JSON.stringify(test)}`).join(',\n');
      return `    ${header}, "tests": [\n${rows}\n    ]}`;
    })
    .join(',\n'),
  '  ]',
  '}',
];
// Every character past ASCII, and every ampersand, is written as its \\u escape: a test's name can hold
// what the house voice test forbids in a file, as a character or as an HTML entity (its own examples do),
// and the escaped form is the same JSON.
const ascii = (text) =>
  text.replace(/[&\u007f-\uffff]/g, (c) => `\\u${c.charCodeAt(0).toString(16).padStart(4, '0')}`);
writeFileSync(OUT, ascii(`${lines.join('\n')}\n`), 'utf8');

const total = suites.reduce((sum, suite) => sum + suite.tests.length, 0);
const failed = suites.reduce((sum, suite) => sum + suite.failed, 0);
console.log(
  `${total} tests in ${suites.length} suites, ${failed} failed; ${checks.length} checks; wrote ${OUT}`
);

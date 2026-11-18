/**
 * Does:      Keeps data/gate-reds.json, every gate run that stopped a version from shipping, newest first. Folds in
 *            the gate feed's reds (scripts/gate_feed.sh) that ended in failure, each once by its run id, with the jobs
 *            that failed less the gate's own verdict job, which fails whenever any of them does.
 * Does not:  Read gh or fail a promote: a feed it cannot read, or one with no reds, leaves the file as it is. A
 *            cancelled run is not a red the gate caught, so it is never written.
 * Used by:   scripts/promote.sh, after gate_feed.sh and before the commit; GateRedsScriptTests.
 *            `node scripts/gate_reds.mjs <feed.json>`
 */
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const [feedFile] = process.argv.slice(2);
if (!feedFile) {
  console.error('usage: node scripts/gate_reds.mjs <feed.json>');
  process.exit(2);
}

const OUT = path.join(process.cwd(), 'data', 'gate-reds.json');

const read = (file) => {
  try {
    return existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : null;
  } catch {
    return null;
  }
};

const kept = read(OUT);
const reds = read(feedFile)?.reds;
if (!kept || !Array.isArray(reds)) {
  console.log('gate reds: nothing to fold');
  process.exit(0);
}

const known = new Set(kept.rows.map((row) => row.run));
const added = reds
  .filter((red) => red.came === 'failure' && !known.has(red.run))
  .map((red) => ({
    run: red.run,
    version: red.version,
    lane: red.lane ?? null,
    sha: red.sha,
    at: red.at,
    failed: (red.failed ?? []).filter((job) => job !== 'gate'),
  }));
const rows = [...added, ...kept.rows].sort((a, b) => (a.at < b.at ? 1 : a.at > b.at ? -1 : 0));

// One row a line, so a diff between two versions reads red by red.
writeFileSync(
  OUT,
  `{\n  "about": ${JSON.stringify(kept.about)},\n  "rows": [\n${rows.map((row) => `    ${JSON.stringify(row)}`).join(',\n')}\n  ]\n}\n`
);
console.log(`gate reds: ${added.length} added, ${rows.length} kept`);

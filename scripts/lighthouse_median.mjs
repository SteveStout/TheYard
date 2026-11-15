// lighthouse_median.mjs
//
// Does:      Reads the Lighthouse runs in a folder (run-*.json) and prints one JSON object: how many runs finished,
//            and the median of the performance score, the largest contentful paint, total blocking time, the layout
//            shift and the speed index across them (ladder 8b, ADR-133).
// Does not:  Run Lighthouse, judge anything or fail: the gate's lighthouse job reports and never blocks.
// Used by:   .github/workflows/gate.yml, the lighthouse job.
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const folder = process.argv[2];
const runs = readdirSync(folder)
  .filter((name) => /^run-\d+\.json$/.test(name))
  .map((name) => JSON.parse(readFileSync(join(folder, name), 'utf8')));
const median = (values) => {
  const sorted = values.filter((value) => typeof value === 'number').sort((a, b) => a - b);
  return sorted.length === 0 ? null : sorted[Math.floor(sorted.length / 2)];
};
const audit = (key) => median(runs.map((run) => run.audits?.[key]?.numericValue));
console.log(
  JSON.stringify(
    {
      runs: runs.length,
      score: median(
        runs.map((run) => Math.round((run.categories?.performance?.score ?? NaN) * 100))
      ),
      lcp_ms: audit('largest-contentful-paint'),
      tbt_ms: audit('total-blocking-time'),
      cls: audit('cumulative-layout-shift'),
      speed_index_ms: audit('speed-index'),
    },
    null,
    2
  )
);

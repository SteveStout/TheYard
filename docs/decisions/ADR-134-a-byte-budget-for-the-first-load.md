# ADR: A byte budget for the first load

Status: accepted, 2026-11-16, at 1.0.5.63. What a reader's first load costs is held to the bytes it cost when the budget was set: the drawn index.html, the entry script, the entry stylesheet and, for a document, the markdown chunk each have a number in `.github/byte-budgets.json`, and a version that spends more fails the gate's node job until it raises the number in its own commit and says why.

## Reading list

1. [ADR: Quality you can watch](https://theyard.stevenstout.biz/?doc=adr-quality-you-can-watch): the ratchet whose rule this follows, that a number moves only on purpose.
2. [ADR: A hook on every page](https://theyard.stevenstout.biz/?doc=adr-a-hook-on-every-page): the first screen these bytes draw.
3. [ADR: The gate runs where the memory is](https://theyard.stevenstout.biz/?doc=adr-the-gate-runs-where-the-memory-is): the gate whose node job runs the check.

## In plain words

Every page a reader is sent to arrives as a few files. This record says how big those files may be, and a test fails the build when one grows past its number. Growing on purpose is allowed: the number is raised in the same change, with the reason in the changelog. Growing by accident is not.

What that is worth: a visitor's first load cannot quietly get slower version after version, a developer sees the cost of a change the day it is made, and the organization gets a speed that is held rather than hoped for.

## Context

The gate already measured the whole built script, `dist_js_bytes` in `data/test-results.json`, and reported it without holding it. That number counts the Admin tab's chunks too, which no reader of a public page downloads, so a change to Admin moved it and a change to the first load hid inside it. The public pages all draw from the entry bundle; the markdown chunk loads when a document opens, and Admin's chunks load only on Admin.

## Decision

- Four parts, each with its own number: `index_html` (the page as built, with the landing drawn into it), `entry_js` and `entry_css` (the two files index.html names), `markdown_js` (what a document adds).
- The numbers were the bytes measured when the budget was set, on this build, uncompressed: 176,223, 579,915, 121,934 and 149,135.
- `tests/budgets/byteBudgets.test.ts` reads the built site in the gate's node job, which builds before Vitest runs, and fails a part over its number. On a machine that has not built it is skipped and says so; on the gate it requires the build.
- A number is raised only in the commit that needs it, never to fit after the fact, and the changelog line says why.

## What holds it

`tests/budgets/byteBudgets.test.ts`, seen red against a budget of zero for every part before the numbers were set.

## Consequences

- A version that adds to the first load raises a number on purpose, in view.
- The two pages the API draws in C# (/about and the resume door) are not in the build's output and are not counted here.
- The Lighthouse report (ladder 8b) is the reading of the whole page in a browser; this is the cheap check that runs on every version.

## Where it sits

Outside the rings: a test over the build's output, beside the quality ratchet's file.

## Files

- [`.github/byte-budgets.json`](https://github.com/SteveStout/TheYard/blob/main/.github/byte-budgets.json) - the four numbers
- [`tests/budgets/byteBudgets.test.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/budgets/byteBudgets.test.ts) - the check

## Addendum, 2026-11-16 (1.0.5.64): the build is asked for only on the gate, and the history may grow

What stopped being true: that every GitHub Actions run of Vitest has a built site to read. The gate's node job builds before Vitest, but ci.yml's frontend job runs npm test before npm run build, so after 1.0.5.63 CI on main was red on the check that a build exists. The check now holds only on a gate branch (GITHUB_REF under refs/heads/gate/); elsewhere a missing build skips the budget, as on a machine that has not built. And the first gate after that, 1.0.5.64's own, was red on 18 bytes: the landing draws every reading in data/quality-history.json (the hook's chart, ADR-132), and the gate's promote adds a row after each version, so main itself had grown past a number measured one row earlier, the drawn index.html by 18 bytes and the entry script by 1,032, with no code change. A number fixed to the bytes would fail every lane's next version. So .github/byte-budgets.json also names the history's rows when it was measured (141) and an allowance per row past them for the two parts that carry the history: 64 bytes for index.html and 1,200 for the entry script (a row is about 1,100 bytes). The stylesheet and the markdown chunk get none, and any growth beyond a row's allowance still fails.

## Addendum, 2026-11-16 (1.0.5.72): the Lighthouse reading beside the budget

What stopped being true: that the gate says nothing about how the page performs in a browser. A job of its own, `lighthouse` in gate.yml, builds the site, serves it inside the job and reads the landing with Lighthouse 12.8.2 five times, never the live site, and keeps the median score, largest contentful paint, total blocking time, layout shift and speed index as the `lighthouse-raw` artifact (scripts/lighthouse_median.mjs). It reports and never blocks: nothing needs it, so promote and the verdict do not wait for it, it is `continue-on-error`, and it is capped at 25 minutes, because the gate's concurrency group holds the next gate until every job of a run has ended. GateWorkflowTests holds all of that. Ten of these readings set the blocking line in a later version, as the owner answered the ladder's question 4. This version changes a workflow, so it is promoted by hand (the repository has no PROMOTE_TOKEN), and a hand promote skips after-roll, whose steps are then done by hand.

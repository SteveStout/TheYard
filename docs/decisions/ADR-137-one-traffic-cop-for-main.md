# ADR: One traffic cop for main

Status: accepted, 2026-11-19, at 1.0.5.99. Lanes stop picking version numbers. A lane pushes `ready/<lane>-<sha8>`, and the Gate run's first job, the traffic cop, gives it its number when it lands. gate/* closes once main carries the cop.

## Reading list

1. [ADR: The version comes from the changelog](https://theyard.stevenstout.biz/?doc=adr-version-source): one version, one commit on main, one changelog line, and the addendum on the fill at the claim that this record moves into the gate.
2. [ADR: The gate runs where the memory is](https://theyard.stevenstout.biz/?doc=adr-the-gate-runs-where-the-memory-is): the gate the cop now leads, its promote and its rule that green code sends no failure email.
3. [ADR: CodeRabbit, one rung at a time](https://theyard.stevenstout.biz/?doc=adr-coderabbit-one-rung-at-a-time): the review the gate waits for, now of the lane's own commit.
4. [ADR: A pull request of AI-written code is sized to the gate](https://theyard.stevenstout.biz/?doc=adr-a-pull-request-of-ai-written-code-is-sized-to-the-gate): what one gate costs, and so what a lost gate costs.

## In plain words

Several AI coding sessions build this site at once, and each finished piece of work has to pass one gate before it goes live. Every version gets a number, one above the last. Until today each session picked its own number on its own machine before it reached the gate. On 18 November two sessions finished about the same time and both picked 1.0.5.93. The gate told each of them "taken" only after the fact, so for over an hour they kept trying again with the same number. Five gate runs were spent and none of them shipped. To Steve, watching, the waiting sessions looked as if they were asking him something.

The fix works like a traffic cop at a single-lane bridge. Sessions no longer pick a number. A finished session parks its work on a branch named `ready/` and leaves the number blank. The gate takes branches one at a time, and only then does the cop look at what is live, write in the next number, and run every check. A version that passes goes live. A version that fails is set aside, and its session can read why on its own branch, so the next one in line never waits behind it. Two sessions that finish in the same second still get two numbers, one after the other, because only the cop hands numbers out.

What that is worth: no gate run is spent on a number fight, nobody has to referee, and the order things went live in is written down by the one process that decided it.

[![One traffic cop for main: three lanes each push a ready branch; the ready queue holds them, oldest first; the traffic cop takes one and assigns main plus one; the full gate runs build, tests, quality and CodeRabbit; a pass ships to main and the version goes live, a fail is logged and skipped and the lane sees why on its branch; either way the cop takes the next ready branch](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/traffic-cop.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/traffic-cop)

*A preview. [Open the traffic cop in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/traffic-cop) to zoom in and follow it.*

## Context

The race, measured from the Gate runs of 18 November (times CST). Main was on 1.0.5.92. Two lanes each picked 1.0.5.93 on their own machines and pushed:

| Pushed | Branch | Gate run | Ended |
| --- | --- | --- | --- |
| 13:00 | gate/1.0.5.93-code1118b-395e780b | 44375787267 | 13:15, red |
| 13:20 | gate/1.0.5.93-adminfix1118-352343c4 | 44379423201 | 13:39, red |
| 13:42 | gate/1.0.5.93-code1118b-cc6a6719 | 44383075318 | 13:59, red |
| 14:03 | gate/1.0.5.93-adminfix1118-1d28f0b0 | 44386900736 | 14:16, red |
| 14:20 | gate/1.0.5.93-adminfix1118-3c1e47b5 | 44390065861 | 14:41, red |
| 14:44 | gate/1.0.5.93-zfix1118-983f125a | 44394248691 | 15:22, green |

Five runs on one number across an hour and forty minutes, and in the end a third lane shipped 1.0.5.93. Three pull requests from adminfix1118's branches, #115, #117 and #118, stayed open and red. The lock and the gate's number job could only say "taken" once a lane had already picked, because the pick happened on the laptop before the gate.

The queue. Since 1.0.5.87 gate.yml has set `concurrency: group: gate, queue: max`. GitHub's workflow syntax documents `queue` with two values: `single`, the default, which keeps one pending run, and `max`, which keeps up to a hundred. Runs in a group go first in, first out by the time each started waiting, and the page adds that the order is not guaranteed because start times vary. On 18 November it was never put to the test: each of the six runs above started the second it was created (`run_started_at` equal to `created_at`), because each push came after the run before it had ended, so the queue never held two. The collision was in the number, not in the queue. The first ready pushes of 19 November are the first reading of the order, and are recorded in this record's addendum.

Steve, 19 November, 06:45: "ADRs going to be created for the rules we set". So every rule the cop sets is one numbered line below, and each has a row in the rules table with the test that holds it.

## Decision

The cop is gate.yml extended, not a second workflow: one writer to main. Its rules:

1. **A lane never picks a version or a record number.** The lane's one commit leaves three placeholders where the numbers go: an at sign, VER, an at sign for the version (the changelog's top line and the subject); the same with DATE for the date; the same with ADR for a new record's number (its file name and its text). The cop fills all three at landing with `scripts/fill_version.sh`. A branch whose changelog top line is already a number is skipped.
2. **One commit per ready branch.** The cop rebases exactly one commit onto main. A branch of two commits is skipped, with the reason.
3. **A lane rebases onto main before it pushes, and proves its run exists.** A push runs the gate.yml inside the pushed commit. A branch cut before main's last gate.yml change would run an old gate, so the cop skips a branch whose gate.yml differs from main's unless changing gate.yml is that commit's own work. A branch cut before the cop existed starts no run at all, so a lane confirms that a Gate run names its branch.
4. **The number is assigned inside the gate's group, one run at a time.** The cop lifts the lane's changelog line out, gives the changelog, the quality ratchet and the version card main's side, rebases the rest onto main, puts the line back on top of main's newest and fills. A conflict anywhere else is the lane's. Every later job tests that filled commit, handed on as a git bundle in the run's `filled` artifact. That works for every version, workflow changes included, and needs no write in the first job.
5. **A lane watches its branch to landed or skipped.** The cop posts on the lane's own commit, as the status named `cop`: "landed as <version>, main <sha8>", "green as <version>, needs a hand promote", "red gate: <checks>; <first failing line>", or "skipped: <reason>", each linked to its run. On anything but landed the lane fixes, rebases and pushes a new ready branch itself. Nobody retries for it. The after-roll deletes a ready branch only once landed is there to read. A ready branch names no version, so the next promote's line-stop takes an after-roll named for one as proof of main only when its reads ran, and a red one joins the site's list of what the gate caught under its branch's name.
6. **Order in the queue is GitHub's and is not promised.** No sweep reorders it, because a run cannot reorder GitHub's queue anyway. Two pushes seconds apart that start swapped still land as N and N+1 with no collision.
7. **Red means a check failed.** A skip tested nothing, so its run ends green with a notice and the failing status on the lane's commit. Steve gets a red Gate email only for a red gate.
8. **No lane takes the gate lock.** The number is the cop's, so `scripts/gate_lock.sh` and `theyard-gate.lock` guard nothing for a ready branch. The lock is kept only for a hand promote, where it stops two promotes from running at once.
9. **A version that changes a workflow is pushed with PROMOTE_TOKEN.** The secret is a fine-grained personal token, "theyard-promote", scoped to SteveStout/TheYard only, with Contents and Workflows read and write. It expires on 19 November 2027. The promote job checks out with it whenever it exists, so every promote is that token's push, and the push itself starts Deploy, Deploy Render and CI on main. promote.sh sets by_push for every version the token pushes, so the run never calls the deploys a second time and each commit rolls once. When the secret is missing or expired, a version that changes a workflow still runs the whole gate, ends green, and its commit reads "needs a hand promote". The hand steps are in CLAUDE.md, Shipping.
10. **gate/* is closed once main carries the cop.** The cop refuses a gate/* push red, pointing at ready/*, once origin/main's gate.yml has the ready/** trigger. Until then gate/* took the old path. That is how this version shipped, as the last gate/* claim.

## Consequences

- Lanes no longer wait on each other's numbers, and a red version never holds the line behind it.
- What stops being true: the claim, the number picked on the laptop, the lock taken by every lane, and the number job.
- What stays true: one commit on main per gate, every check on every version, and main moving only by promote.sh's fast-forward.
- The cost: every ready branch is rebased by a machine. A conflict outside the changelog comes back to the lane as a skip, never as a guess.
- A rotated or expired PROMOTE_TOKEN turns workflow versions into hand promotes until it is replaced. Every other version still lands on its own.

## Where it sits

Outside the rings: the ship's path to main. It is a job of the gate, a script beside the fill, and a status on each lane's commit.

## Files

- [`scripts/cop.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/cop.sh) - the cop: the lift, the rebase, the fill, the skips and the bundle
- [`scripts/cop_status.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/cop_status.sh) - the status on the lane's commit
- [`.github/workflows/gate.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/gate.yml) - the cop job, the filled commit in every job, the said job
- [`.github/actions/filled/action.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/actions/filled/action.yml) - the filled commit, checked out from its bundle
- [`scripts/promote.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/promote.sh) - the tested commit from gate-raw's filled.sha, and one roll per commit
- [`scripts/fill_version.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/fill_version.sh) - the fill the cop runs
- [`scripts/preflight.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/preflight.sh) - the ready shape and the ready push
- [`scripts/gate_pr.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/gate_pr.sh), [`scripts/coderabbit_review.py`](https://github.com/SteveStout/TheYard/blob/main/scripts/coderabbit_review.py), [`.github/workflows/after-roll.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/after-roll.yml) - the three readers that knew only gate/*
- [`docs/images/traffic-cop.svg`](https://github.com/SteveStout/TheYard/blob/main/docs/images/traffic-cop.svg) - the diagram
- [`api/TheYard.Tests/CopScriptTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CopScriptTests.cs) - the cop's cases, on throwaway repositories

## Addendum, 2026-11-20 (1.0.5.122): the queue's first reading, the race and the red main

**The queue, read.** On 19 November eleven ready runs went through the gate's group, and every one started in the order it was pushed, first in, first out, each holding the group alone. The start that counts is each run's cop job, not the run's own `run_started_at`: a pending run's `run_started_at` equals its `created_at`, so it says nothing about when the run left the queue. Pushed and cop started, UTC: the fail proof 14:33:09 and 14:33:12; cop1118-e4b902df 14:33:11 and 14:42:27; jhfix1119 14:35:05 and 14:51:40; green1119 14:41:52 and 14:59:32; code1118e-2a93b6c5 14:42:12 and 15:09:05; commentline1119 14:46:18 and 15:19:08; then code1118e-3923f20b 14:50:06, hunter1119 14:50:11 and cop1118-222191f0 15:20:06, in that order behind them. No swap was seen, so no sweep was built (rule 6 stands).

**The race, proven.** The first two ready branches pushed within a minute of each other on a green main, both cut from c807f400 (1.0.5.101): ready/code1118e-127f68d8 at 15:52:34Z and ready/crosspost1119-5cce6c07 at 15:53:06Z, 32 seconds apart. The first run's cop started 15:52:38Z and landed 1.0.5.102 at 058fec49 by 16:03:52Z; the second's cop started 16:03:54Z, two seconds after the first run ended, and landed 1.0.5.103 at 96474e4f by 16:22:49Z. Its line-stop took the after-roll named for ready/code1118e-127f68d8 as the proof of 058fec49. N and N+1, in push order, with no human step and no collision. The pair before it (green1119-dbc62a40 and code1118e-2a93b6c5, 20 seconds apart) also started in push order but went red on the bug below, so it proves the order and not the landing.

**The red main.** 1.0.5.100, the first ready landing, folded the fail proof's red run into data/gate-reds.json under its branch's name, ready/cop1118-failproof-4de7ed19, where every other row holds a version. GateRedsTests holds each row to four numbers, so CI on main went red at 17701ea1, the after-roll went red, and every ready run behind it failed that one test. jhfix1119, a waiting lane, reported it. 1.0.5.101 (c807f400, `Fixes-main: 17701ea1`) fixed it: a red ready run is kept under the version its cop job gave it, read back from the cop's own status, a red run with no such status is left out, and the status names the failing test on its first line. Rule 5's last clause is replaced by that: a red ready run joins the list under the version the cop gave it, never under its branch's name.

**The correction.** The Context above gives "(`run_started_at` equal to `created_at`)" as the evidence that each 18 November run started the second it was created. That parenthetical is not evidence, for the reason in the first paragraph. The claim stands on the times themselves: each of the six pushes came after the run before it had ended, so the queue never held two.

## Addendum, 2026-11-20 (1.0.5.122): the counts are the fill's

S3's index column was skipped twice on 19 November, both times "the rebase onto main conflicts in README.md docs/built-with-ai/BUILT-WITH-AI.md": ready/code1118e-3923f20b at 15:26Z on main 17701ea1 (1.0.5.100) and ready/code1118e-31f196df at 16:55Z on main 3a756ec0 (1.0.5.105). Every landing rewrites README.md's suite line and Built with AI's gate line with its xUnit and Vitest figures, and the lane had written its new Playwright count on the same two lines, so any version that landed between the push and its turn made a real conflict. A version that adds a record meets the same wall on the record count in words, which README.md states four times and How this was built and Built with AI once each: two such lanes in one queue, and the second is skipped.

So the counts the public face states are written at landing, by the fill, on the tree it numbers: `scripts/fill_version.sh` writes the record count in words from the records the commit carries, and the Playwright count from the specs' `test(` lines, both read the way PublicFaceTests reads them, which keeps holding them. A lane leaves those words as main has them. Its unfilled commit reads one record or one browser test short in PublicFaceTests until the cop fills it, the same as the version card, and nothing it edits sits on a line the next landing rewrites. CLAUDE.md, Shipping, tells lanes so, and FillVersionScriptTests holds the fill.

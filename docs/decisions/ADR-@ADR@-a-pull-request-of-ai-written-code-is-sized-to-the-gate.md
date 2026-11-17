# ADR: A pull request of AI-written code is sized to the gate

Status: accepted, on trial, @DATE@, at @VER@. Steve re-reads it after the run of 17 to 18 November and may change either number. A version changes at least 100 lines and at most 1,000, added plus deleted; one under the floor names why it ships alone, and one over the ceiling names why it is one version. The two numbers live in `.github/pr-size.json` and nowhere else.

## Reading list

1. [ADR: The gate runs where the memory is](https://theyard.stevenstout.biz/?doc=adr-the-gate-runs-where-the-memory-is): the gate whose cost this record weighs.
2. [ADR: The version comes from the changelog](https://theyard.stevenstout.biz/?doc=adr-version-source): one version, one commit on main, one changelog line.
3. [ADR: Quality you can watch](https://theyard.stevenstout.biz/?doc=adr-quality-you-can-watch): the ratchet, the other number a version is held to before it ships.

## In plain words

Nearly all of this site's code is written by AI coding sessions, and three or four of them ship through one gate, one version at a time. The gate runs every check on every version, about half an hour each, and while one version is in it the others wait. So a version that changes six lines costs the same half hour of everyone's queue as one that changes six hundred. This record says how big a version should be: big enough to be worth the gate, small enough that a reviewer, human or CodeRabbit, can still hold it in their head.

What that is worth: the gate's time goes to changes worth testing, a red gate is easy to place because a version is one whole item, and the trade is written in the commit where anyone can read it rather than left to each session's habit.

## Context

Measured at main 1.0.5.75 with `git log --first-parent --numstat`, the files the promote and the fill write left out: over the 76 versions 1.0.5.0 to 1.0.5.75 the median version changed 209 lines; 21 changed under 100, the smallest 6; 6 changed over 1,000, the largest 1,616. A gate held about 32 minutes of every lane's queue that day, so those 21 slivers took about 11 hours of it. Steve, 17 November: "I want each push to be beefy enough to be worth the test", and then: "this will be a ADR on LLM generated Code Pull Request size, to maxamize QA gate efficently let's try it and we can modify after this run".

An LLM writes a hundred lines as easily as ten, so the cost that matters is not the writing; it is the gate's half hour and the reviewer's attention. A human team sizes pull requests for the reviewer; here the gate is the scarcer of the two.

## Decision

1. A version changes at least 100 lines and at most 1,000, added plus deleted, counted on its diff against main. The files the promote and the fill write are not counted (data/, README.md, docs/CHANGELOG.md, docs/images/og.svg, public/og.png, docs/built-with-ai/BUILT-WITH-AI.md, package-lock.json), and binary files are not counted.
2. A version under the floor ships alone only when its commit message carries `Ships-alone: <reason>`: it changes a workflow, it changes how lanes claim, it fixes a red main (`Fixes-main: <sha>` counts), or a reason the lane writes out. Otherwise it rides in its lane's next version, as one squashed commit whose changelog line names each part.
3. A version over the ceiling carries `Large: <reason>`.
4. The two numbers and the list of files not counted live in `.github/pr-size.json`, beside `quality-ratchet.json` and `byte-budgets.json`. The script and the test read them from there; neither is typed anywhere else.
5. The floor exists because of what a gate costs. When the push-to-live time the promote now records shows the gate getting faster, the floor is read again.

## What holds it

`scripts/pr_size.sh`, run by `scripts/preflight.sh` before every claim: it prints the count, the band and the verdict, and refuses a version outside the band with no trailer. On trial it runs in preflight only, not in the gate. `PrSizeScriptTests` plants a sliver, a sliver with its trailer, a version inside the band and a large one with no trailer, and holds the script's answer for each.

## Consequences

- A lane batches its small items into one version, inside one area, so a red is still easy to place.
- What stops being true: one gate per finding, and any lane's habit of shipping a sliver because it was ready.
- What stays true: one commit on main per gate, every check on every version, and the lane's own judgment, now written where a reader can see it.
- The evidence for the re-read is the list of every version shipped under the record, its size, and whether it carried a trailer and why (GATEFLOW1117's notes), against the gate's time per version.

## Where it sits

Outside the rings: a check over the version's diff, run before the claim, beside the quality ratchet and the byte budget.

## Files

- [`.github/pr-size.json`](https://github.com/SteveStout/TheYard/blob/main/.github/pr-size.json) - the floor, the ceiling and the files not counted
- [`scripts/pr_size.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/pr_size.sh) - the count and the verdict, run by preflight
- [`api/TheYard.Tests/PrSizeScriptTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PrSizeScriptTests.cs) - the four cases

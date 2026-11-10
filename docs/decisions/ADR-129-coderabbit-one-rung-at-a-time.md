# ADR: CodeRabbit, one rung at a time

Status: accepted, 2026-11-10, at 1.0.4.176. CodeRabbit reviews every ship's pull request inside the gate and the review is read before main moves. It starts soft, at Level 0, where no finding holds a ship, and climbs one level at a time on the findings log's evidence and the owner's yes.

Current level: 0

## Reading list

1. [ADR: The rules a change has to pass](https://theyard.stevenstout.biz/?doc=adr-rules): every rule a machine checks, beside its test. CodeRabbit is told only rules already in it.
2. [ADR: The gate runs where the memory is](https://theyard.stevenstout.biz/?doc=adr-the-gate-runs-where-the-memory-is): the gate this review joins, and the hand promotion a version that changes a workflow needs.
3. [ADR: The numbers that may only fall](https://theyard.stevenstout.biz/?doc=adr-the-numbers-that-may-only-fall): the other gate that only ever gets stricter, one measured step at a time.

## In plain words

Every version of this site already passes its own tests and quality checks before it goes live. Now a second reviewer, CodeRabbit, an AI review bot, reads each change too, the way a colleague would read a pull request. It starts gentle: it comments, and nothing it says stops a release yet. Each step up in strictness is my decision, made on a log of what it found, what was fixed and what it got wrong, so the bot earns its authority instead of being handed it.

What that is worth: a team gets a reviewer that never tires, without a new source of false alarms on the day it arrives, and every rule the bot enforces points at the decision that made it.

## Context

Steve, 9 November: "once our current coding is done, I'd like to introduce code rabbit as our final QA gate", then "I want this to be run as part of our QA gates and have it set softly and you and I gradually increase it's intensity so every rule has a ADR". His standing view is that AI makes mistakes the way a person does, so a team adds peer review or a review bot on top of its own gates.

A ship is a push of gate/<version>; gate.yml runs every check and fast-forwards main when green. There was no pull request anywhere in that path, and CodeRabbit reviews pull requests only. Three facts read before building, each from CodeRabbit's own pages or schema on 9 and 10 November:

- A public repository under ten stars is reviewed only when asked, by a comment `@coderabbitai review` (this one has none). The open-source allowance is one to ten reviews an hour by star count; a limited review posts a check that passes.
- The configuration key the brief named for "request changes", `request_changes_workflow`, is now a legacy alias of `reviews.auto_approvals.enabled` in CodeRabbit's schema (schema.v2.json, read 08:05 CST 10 November). The ladder uses the current key.
- A committed `.coderabbit.yaml` outranks the repository's settings in CodeRabbit's web app. Nothing is set there, so the file is the one place the level lives.

The App was installed on this repository alone on 10 November, and a throwaway pull request (#3) had its CodeRabbit check complete 87 seconds after the comment that asked for it.

## Decision

1. **The review sits inside the gate.** Right after its push of gate/<version>, the machine that ships runs `scripts/gate_pr.sh open <version>`: a pull request from that branch into main, titled with the commit's subject, and the comment that asks CodeRabbit for a review. It runs as the owner through gh, so the repository needs no setting letting Actions open pull requests and no new token. gate.yml's `coderabbit` job runs beside the gate's checks and waits for CodeRabbit's check on the commit to complete, a skipped one counted as not yet, with a ceiling of 30 minutes. The brief said to start at 20; the dry run below took 18 minutes for its review, too close to 20, and the gate's own checks take 31 to 38 minutes, so 30 costs a ship nothing. The promote job needs it, so the review always lands before main moves.
2. **Level 0 never holds a ship.** Any conclusion counts as complete. A review that does not come inside the ceiling, or a read that fails, ships anyway with a notice in the run, and its row in the log says which.
3. **The request closes on main's commit.** promote.sh amends the gate's commit with the run's results before it fast-forwards main, so GitHub never marks the request merged. The after-roll job closes it with a comment naming main's commit, before the gate branch goes; a version promoted by hand closes it with the same script.
4. **The findings log is the evidence.** The coderabbit job writes one row per ship (version, pull request, level, findings by severity, how many CodeRabbit called actionable, the minutes the job waited and the review's own minutes, and whether it was a full review, a summary only, rate limited, or not reviewed). promote.sh folds it into `data/coderabbit-findings.json` in the same commit as `data/test-results.json`, the pattern that file set: the site already serves what the gate writes into `data/`, so the Admin tab or a later lane reads it without a new store.
5. **Every rule cites its record.** Each path instruction ends with `(ADR-NNN)`. A rule with no record is not told to CodeRabbit; it waits for one.
6. **The owner moves the level, on evidence.** A level moves only when Steve says yes to a Claude session that brings him the log over at least five ships: findings per ship, how many were fixed, how many were answered as wrong, and the minutes added per ship. The level ships with its addendum here, and the line above changes in the same commit as the configuration.

### The ladder

| Level | profile | auto_approvals.enabled | fail_commit_status | What holds a ship |
| --- | --- | --- | --- | --- |
| 0 | chill | false | false | Nothing: the review is read, never waited on past the ceiling |
| 1 | chill | false | false | Nothing; more path instructions, each with its own record or addendum |
| 2 | chill | true | false | "Changes requested" on a rule marked blocking holds the promote; the rest stay advisory |
| 3 | assertive | true | true | Level 2, plus a ruleset on main requiring the CodeRabbit check |

### The rules at Level 0

Each was already decided and already held by a test; CodeRabbit is a second reader of the same rule, not its author.

| Rule told to CodeRabbit | Record | Test that already holds it |
| --- | --- | --- |
| Every async method takes a CancellationToken | ADR-122 | CancellationTests |
| No em dash anywhere | ADR-028 | HouseVoiceTests |
| A comment says what, how or why, never history | ADR-074 | ProductionCommentTests |
| Dependencies point inward | ADR-088 | OnionTests |
| A slice never uses another slice | ADR-107 | SliceTests |
| A request is a sealed query or command with one handler | ADR-115 | CqrsTests |
| 300 lines a file and a type | ADR-075 | FileShapeTests |

Level 1 candidates, each waiting for a record: sealed by default (held by SealedByDefaultTests, but decided on the Best Practices page, not in a record), and no secret, token or key in the tree (no record and no test yet).

The level and the rules, read from the configuration this build carries:

```live path=.coderabbit.yaml region=the-level
```

```live path=.coderabbit.yaml region=path-instructions
```

## The dry run

On 10 November a throwaway pull request (#4, never merged) carried this version's files plus one planted file: an async method that took no CancellationToken and called `Task.Delay` without one, under a comment holding an em dash. `scripts/coderabbit_review.py` ran against it on the box exactly as the gate's job runs it, and wrote this row (08:34 CST):

```
{"version": "coderabbit1109/dryrun", "pr": 4, "sha": "d28866e6", "level": 0, "waited_seconds": 1093,
 "review_seconds": 1074, "conclusion": "success", "reviewed": "full", "actionable": 6,
 "findings": {"critical": 0, "major": 1, "minor": 5, "trivial": 0, "nitpick": 0}}
```

- **Both planted defects were found**, each as a Minor finding on its own line, and each quoted the rule from this configuration: "The rule 'No em dash (U+2014) anywhere in code, comments, documents or data' applies here", and "This async method breaks the rule that every async method takes a CancellationToken". So CodeRabbit reads `.coderabbit.yaml` from the pull request's own branch, which is what lets a level ship in the commit that changes it.
- **The other four findings were on this version's own scripts, and all four were right**: a reply from gh that is not JSON would have stopped the read without a row; a failed read of the findings would have been logged as zero findings; a read that failed before writing its row would have left the ship out of the log; and a gate branch gh could not read gave a misleading hint. Each is fixed in this version (the empty row, the "completed, findings not read" row, the promote's row for a read that wrote none, and the hint).
- **The review took 17 minutes 54 seconds** (1074 s) from its start to its check completing, against 87 seconds for the one-file probe on #3. Measured on one pull request of 25 files; the log's minutes column will say what a normal ship costs.
- The job ended green with findings outstanding, which is Level 0: a ship is never held.

No screenshot is attached: the lane that built this ran with no browser. The pull request's comments, read through GitHub's API, are the record, and #4 stays readable.

## Consequences

- Every ship opens and closes one pull request, and CI's pull request run starts on it beside the gate. It costs runner minutes on a public repository and no money.
- The job waits beside the gate's own checks, which take 31 to 38 minutes, so a review that takes up to half an hour adds nothing to a ship; the log records the minutes either way.
- Under ten stars the allowance is small. A rate-limited review is a completed check at Level 0, and the log says "rate limited", so the evidence is never read across two allowances without saying so.
- The new account started a 14-day trial of a paid plan. CodeRabbit's plans page says open-source projects get Team features with no subscription, and also that an organization that does not subscribe after the trial reverts to summaries only; it does not say which applies here. The log's "reviewed" column will show it, and the first summary-only ship after the trial is the owner's call.

## What holds it

`CodeRabbitRuleTests`: every path instruction cites a record and every cited record exists in docs/decisions; the profile and the two switches are the ones this record's ladder gives for its current level; the current level line here exists and the configuration agrees with it; the generated and binary files stay filtered. `GateWorkflowTests` and `PromoteWorkflowTests`: the coderabbit job waits on the review with its ceiling and never writes, promote needs it, promote.sh refuses a run whose coderabbit job is not green, the row is folded into the findings log, and the request closes before the branch goes.

## Where it sits

Outside the rings: the configuration at the repository's root, the job in gate.yml, the two scripts under scripts/, and the log in data/ beside the results file. Nothing in the six projects changes.

## Files

- [`.coderabbit.yaml`](https://github.com/SteveStout/TheYard/blob/main/.coderabbit.yaml) - the level and the rules, each with its record
- [`.github/workflows/gate.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/gate.yml) - the coderabbit job, the promote job's wait and the request's close
- [`scripts/gate_pr.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/gate_pr.sh) - opens the request and asks for the review; closes it on main's commit
- [`scripts/coderabbit_review.py`](https://github.com/SteveStout/TheYard/blob/main/scripts/coderabbit_review.py) - waits for the review and writes the row; folds it into the log
- [`api/TheYard.Tests/CodeRabbitRuleTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CodeRabbitRuleTests.cs) - the rules and the level held to their records

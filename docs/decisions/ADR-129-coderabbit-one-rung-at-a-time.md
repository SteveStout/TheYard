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

## Addendum, 2026-11-10 (1.0.4.177): the first finding, fixed

CodeRabbit reviewed 1.0.4.176 on its own gate's pull request (#5): one Minor finding, 777 seconds of review, and the job done 3 minutes before the gate's own checks, so the ship waited for nothing. The finding was right. The log counted a finding's severity by searching its opening text for any of the four words, so a Minor finding that said "major" in its title would have been logged as Major. The count now reads the label in the finding's header alone (`LABEL` in `scripts/coderabbit_review.py`), in the current header and the older one, and a remark with no label is a nitpick. CodeRabbitRuleTests holds it with four headers: a Minor that mentions "major", a Major that mentions "minor", the older Critical header, and no label at all. It is the log's first row with a finding fixed, the kind of row the ladder's evidence is made of.

## Addendum, 2026-11-10 (1.0.4.178): a cold review of the first two versions

A reviewer with no part in building this read 1.0.4.176 and 1.0.4.177 from three seats (QA, a hiring manager, a staff engineer). Three of its findings were defects and are fixed here:

- **Nothing told a shipper to open the request.** The Shipping section of CLAUDE.md and the preflight's last line now say `bash scripts/gate_pr.sh open <version>` after the push. A ship without a request is still a Level 0 ship, but the job stops looking after five minutes and says so as a warning, where it used to wait out the ceiling behind a notice.
- **The log counted the request, not the commit.** A second push to the same gate branch would have counted the first commit's findings again, and a reply in a finding's thread counted as a nitpick. The count now takes only the gated commit's review and only comments that open a thread.
- **The hand promotion's command stopped on a missing row.** It fetched the review's artifact with `&&`, so a read that crashed before writing its row stopped the promotion that exists to write the row for it. The fetch is now allowed to find nothing.

Also changed: the label is read from the header line alone, never the remark under it (the one Minor finding CodeRabbit raised on 1.0.4.177's request, #6); a rate-limited review whose text says "skipped" counts as complete, and the job's timeout is 45 minutes, so the 30-minute ceiling plus its slowest reads cannot time the job out and hold a ship.

Owed before Level 1, from the same review: the log has no column for a finding fixed or answered as wrong, which Decision 6's evidence needs (the session that brings the table derives them from the request's threads until the log carries them); Level 2's "a rule marked blocking" needs a way to tie a finding to the rule it cites before that level is written; and five ships is a small sample, so the table says how many ships it covers.

## Addendum, 2026-11-10 (1.0.5.1): the second Level 1 candidate has a record

What stopped being true: "no secret, token or key in the tree (no record and no test yet)" under The rules at Level 0. The record exists, ADR-072 (The code is public and the secrets are not). What it lacks is a test: ADR-075 has no row for it. It stays a Level 1 candidate until a secret scan in the gate holds it.

## Addendum, 2026-11-10 (1.0.5.5): the instructions match their records

What stopped being true: the seven path instructions said their rules in words of their own and over paths of their own, and only one, the em dash rule, came close to what its record and its test say, naming one of the three spellings its test refuses. The cancellation rule, the comment rule and the 300-line rule reached every file of their kind, tests included, where their records and tests read production code only: 727 async test methods (at 1.0.5.4, in the three test projects) with no token would have been flagged under a rule that exempts them. The onion, slice and request rules each said part of what their tests hold and left out the job hunt Function's half.

**Written from the rule table.** `scripts/coderabbit_rules.py` writes the path-instructions region of `.coderabbit.yaml`. Each instruction is clauses quoted word for word from the ADR-075 rows that name its record and its test, then the scope that test reads (its folders, and what it leaves out, such as test projects, migrations, generated files and the Experiment tool), then the record. Run with no argument it checks; `CodeRabbitRuleTests` runs it, so a row that changes, a clause that no longer appears in its row and an instruction edited by hand are each red until the configuration is written again. A second test reads each test's own scope (the ratchet's C# folders, FileShapeTests' folders and kinds, ProductionCommentTests' two areas, HouseVoiceTests' three spellings) and holds the instruction's path to it. The three rules whose tests read compiled assemblies (the onion, the slices, the requests) name their projects in the script and are held by the clause check.

The mechanism differs from the one the lane's prompt recommended, generating each instruction whole from ADR-075's rows, in one way: an instruction quotes clauses of a row rather than the whole row, because several rows carry two rules or a version's history ("From 1.0.4.70 the file rule is one body"), and telling CodeRabbit those would tell it more than the seven rules. The clause check is what keeps a quoted clause from drifting.

**Seven rules, eight instructions.** The comment rule is asked at two strengths, the site's and the job hunt Function's, so it is told as two instructions with two paths. The rule table above stays seven rows, one per rule; `Every_rule_the_ladder_lists_is_one_the_configuration_tells_and_the_other_way_round` now compares the distinct records and holds the table at seven.

**Paths name what is in.** CodeRabbit documents negation for path filters only, so each path names the folders its test reads and each instruction's text says what is out of scope.

**Proved on a throwaway request** (#14, closed unmerged): with these instructions, CodeRabbit flagged the three cases the records forbid (a production async method with no token, a production file over 300 lines, a production comment leaning on a date and a named review) and said nothing on the three they allow (an async test method with no token, a test file over 300 lines, a comment showing a wire format with a date in backticks), though all six files were in the files it reviewed. CodeRabbit reviewed #14 on its own, with no comment asking it to.

**A commit's review is counted on every request that carried it.** When 1.0.5.3's gate branch was pushed again, its first request (#11) closed holding the review's two Minor findings, the second (#12) got no review of its own, and the row said "full, no findings". `scripts/coderabbit_review.py` now reads every request from the branch whose head is the commit, closed ones included, counts each on its own and keeps the one holding the review, so a summary mirrored onto both is not counted twice; the row names every request as `prs`. `recount` corrects a row after the fact, and 1.0.5.3's row now reads two Minor findings on #11. `CodeRabbitRuleTests` runs it against a stand-in gh that answers as GitHub did.

The level stays 0, the profile and both switches are unchanged, and no rule was added. The guideline-file question (CodeRabbit reading ADR-075 as a guideline file) is the walkthrough's to put to Steve and was not answered when this shipped, so nothing was added under `knowledge_base`.

- [`scripts/coderabbit_rules.py`](https://github.com/SteveStout/TheYard/blob/main/scripts/coderabbit_rules.py) - the instructions, written from ADR-075's rows and checked against them
- [`api/TheYard.Tests/CodeRabbitRuleTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CodeRabbitRuleTests.cs) - the check run, each path held to its test's scope, the seven rows, the recount

## Addendum, 2026-11-10 (1.0.5.6): what a review body carries is counted, and two clauses are told

What stopped being true: the log counted the findings CodeRabbit posts on a line, and a review body's nitpicks only in the form "Nitpick comments (N)". A review body also carries the findings GitHub cannot post on a line outside the diff, each with its own header label, and CodeRabbit's newer bodies write "Nitpick comments: N". Both are now counted (`outside` and the nitpick pattern in `scripts/coderabbit_review.py`, held by `CodeRabbitRuleTests` on a planted body), CodeRabbit's own finding on #7. Every row was counted again with `recount`: 1.0.4.178 gains its outside-diff Major (keep the downloaded reports outside the worktree, on #7) and 1.0.5.0 its two nitpicks (on #8); the other rows are unchanged.

Also from the addendum of 1.0.5.5: two clauses its tests hold were not yet told, ADR-075's OnionTests row on what an endpoint class is, the host's own database reads and a handler's settings, and the request rule's "nothing in the Application ring's root or slices is named *Service". Both are now quoted, so all seven rules say what their record and test say. The hand promotion downloads into a fresh folder outside the worktree, and a missing review artifact removes any coderabbit.json left behind (#7).

## Addendum, 2026-11-10 (1.0.5.7): what became of each finding, and what the clock says

**The log says what became of a finding.** Decision 6 decides each rung on findings per ship, how many were fixed and how many were answered as wrong, and the addendum of 1.0.4.178 owed the log a column for each. Every row now carries `fixed` and `answered_as_wrong`, read from the request's threads and the versions after it (`coderabbit_review.py dispose`, which refuses a count larger than the row's findings); null is a count not read yet, never a guess, and a version re-run keeps what was read. A finding in neither column is still open. The Quality page shows both columns beside the findings. Over the ten ships 1.0.4.176 to 1.0.5.6 the log holds 16 findings: 12 fixed, 2 answered as wrong (on #10 and #11, "Limit now" and a history row's limit, both right only for a build between two versions; ADR-130, the addendum of 1.0.5.6), and 2 open: on #8, the chart pieces the Quality page imports from the Admin section, carried to a version of its own, and on #16, a Trivial note that the outside-diff count reads CodeRabbit's current header format, kept as it is because the section's own count is the fallback, so a change of format can lose a finding's severity and never the finding.

**What the clock says.** Decision 1 and Consequences say the gate's own checks take 31 to 38 minutes, and that a ceiling of 30 minutes therefore costs a ship nothing. Measured from each run's jobs (`gh run view --json jobs`): over the ten ships 1.0.4.173 to 1.0.5.3, the checks ended a median 886 seconds into the run (727 to 1,162), and the whole run, from the push to the after-roll's green, took a median 2,124 seconds, 35.4 minutes (1,842 to 2,338, seven ships). The 31 to 38 minutes was the whole run. Since the checks run side by side (ADR-098, the addendum of 1.0.5.4) they end seven to ten minutes in: 416 seconds at 1.0.5.4, 410 at 1.0.5.5 and 564 at 1.0.5.6, three ships. CodeRabbit's job took 884, 646 and 597 seconds on them, so promote waited 473, 240 and 37 seconds on the review, and gateSeconds, which runs to the promote step, read 900, 657 and 612.

So the ceiling's basis, restated: the review no longer waits beside a longer gate. When it is slower than the checks, which it now usually is, the ship waits on it, up to the 30-minute ceiling, which is still where it stands. Whether promote keeps waiting, and whether the ceiling moves, stay Steve's (Decision 1).


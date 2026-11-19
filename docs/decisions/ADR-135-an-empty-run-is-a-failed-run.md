# ADR: An empty run is a failed run

Status: accepted, 2026-11-17, at 1.0.5.77. A job hunt process that ran and did nothing it was there to do does not come to success. A lookup that keeps coming back empty stops itself and says so on WALLS.md, a run under its floor comes to blocked, a model lane that had work due and did none two runs in a row leads the 07:30 card, and a run line that says failed or blocked is what the dead-Function alert reads.

## Reading list

1. [ADR: The Nightly Function on the Admin tab](https://theyard.stevenstout.biz/?doc=adr-the-nightly-function-on-the-admin-tab): the run line every process writes, and the six outcome words this record keeps.
2. [ADR: The campaign sends, the model writes rows](https://theyard.stevenstout.biz/?doc=adr-the-campaign-sends-the-model-writes-rows): the finder and its monthly cap, whose counting rule this record changes.

## In plain words

For a day and more, parts of the job hunt ran every half hour and every run said it finished. The inbox pass failed on Gmail's quota eleven times in a row and its record said success; the address finder spent twenty-one lookups on an account that refused them and recorded twenty-one people with no address; the morning invites sent none of twelve and closed like any other morning. Now an empty run is treated as a failed one: it says blocked or failed in its own record, the lookup that keeps finding nothing stops and puts one line on the list of things only Steve can clear, the morning card opens with any lane that keeps running empty, and the alert that already emails a dead Function reads those records too.

What that is worth: a break shows up the day it starts, as a wall, a card line or an email, instead of as a silent zero found by asking.

## Context

Steve, on 12 November: "an empty run counts as failed", "if a lookup step returns nothing across a set number of tries, stop and send a message", and "it ran for days and every run still looked finished". Read that morning: the campaign part of the inbox pass had answered 403 on 13 to 25 of each day's 48 passes since 27 October, and from 04:20 Central on 12 November the other parts did too; HunterFinder answered null on any refusal, so HTTP 429 `restricted_account` read as "no address found"; the clock recorded `dispatched=0` for a dispatch GitHub did not take and still came to success; and the alert read only the host's failed requests, which a pass whose parts failed never writes.

## Decision

1. **Three answers from a lookup.** Found, no answer, refused. A refusal (401, 403, 429 `restricted_account`) is a wall on WALLS.md with a 24-hour stop for every caller, and is not counted against the month.
2. **Every process has a floor or a reason.** One table (`RunFloors`) names, for every process that writes a run line, the count that proves it worked and the least it must reach over how many tries, or the words for why empty is normal for it. A test fails when a process has no row.
3. **Under the floor is blocked.** A run that comes to success under its floor comes to `blocked` with `zero_results`; a dispatch GitHub did not take comes to `blocked` with `dispatch_refused`. No seventh outcome word.
4. **A failed part fails the pass.** The inbox pass reads all six of its parts, the campaign and the finder included.
5. **The run line emails.** The dead-Function alert also reads a run line that came to failed or blocked, one alert a process, so a standing hold cannot hide a new break; it is deployed only once a week's replay shows no ordinary day with more than one email.
6. **The model lanes too.** On the share, `check_empty_runs.py` reads each lane's close row once a Central day and writes `EMPTY-RUNS.md`: one line for a lane that had work due and did none two runs in a row, with the top three reasons its own notes gave. A send of none with work left out counts as empty, and so does a hold the lane put on itself; a hold that names another live lane does not, and "0 of 0" is quiet. The 07:30 card prints each `EMPTY RUN:` line whole at its head, under the hiring signal, every day; a file that says none, or no file, prints nothing.

## What holds it

`RunFloorsTests` (a process with no row fails), `FinderWallTests` and `AddressSearchTests` (the three answers, and the wall), `TodayEmptyRunsTests` (the card prints each line and prints nothing for none), and on the share `test_check_empty_runs.py`.

## Consequences

- The alert's first replay (11/5 to 11/12) would have sent 17 emails, one to three a day, every one a real break. The replay read on 17 November found two a day from 11/10 to 11/12 and at most one from 11/13 to 11/16, after the campaign's 403 stopped. It goes live once a replay with 11/12 out of its window shows no day over one.
- The first `EMPTY-RUNS.md`, written 17 November, names two lanes: the 6 AM apply lane (eight runs) and the 6 AM messages lane (seven), each with the reasons it gave. Before this record both closed every morning as finished.
- A wall costs Steve one look a day at most: a bullet stays until the thing it names works again, and the stop keeps the calls from piling up behind it.
- A process added later must arrive with its row, or its tests are red.

## Where it sits

The application ring, in the run line every process writes and the floors table beside it (RunFloors), with the finder's three answers in the infrastructure ring's Hunter adapter and the card's lines in the domain's TodayCard. It serves the single responsibility of the run line: one place says what a run came to, and the alert, the card and the Admin tab read it rather than each guessing. It cost one row a process and one daily read on the share; a process whose empty run is normal says so in its row instead of being exempt in code.

## Files

- [`functions/JobHunt.Application/Shared/Runs/RunFloors.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Application/Shared/Runs/RunFloors.cs) - every process's floor or its reason
- [`functions/JobHunt.Application/Shared/Runs/RunLine.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Application/Shared/Runs/RunLine.cs) - the run line and its outcome words
- [`functions/JobHunt.Infrastructure/Features/Campaign/HunterFinder.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Infrastructure/Features/Campaign/HunterFinder.cs) - found, no answer, refused
- [`functions/JobHunt.Domain/Features/Card/TodayCard.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Domain/Features/Card/TodayCard.cs) - the card's EMPTY RUN lines
- [`infra/jobhunt-alerts.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/jobhunt-alerts.bicep) - the widened alert, not yet deployed

## Addendum, 2026-11-19 (1.0.5.107): an empty run goes to a fix proposal, never to Steve's card or walls

What stopped being true: the Decision's line that a model lane with work due and none done two runs in a row leads the 07:30 card, and `check_empty_runs.py` putting a bullet on WALLS.md. RULE 77 (set 18 November) keeps his card and WALLS.md for what only he can clear, and an EMPTY RUN's control was "read that lane's newest DONE note", which is the system's work. RULE 76's fourth approved fix moves it.

From this version `check_empty_runs.py` (on the share, `dispatch/`) still writes `dispatch/EMPTY-RUNS.md`, takes every EMPTY RUN bullet off WALLS.md each run, and writes one fix proposal a flagged lane, `dispatch/proposed/emptyrun-<lane>.md`, once, left as written for the triage pass and his one "queue it". The card no longer reads EMPTY-RUNS.md: `TodayInputs.EmptyRuns` and its path are gone. On the box, the apply worklist takes the postings the tracker already shows submitted off before any gate (`ApplyPostings.Unapplied`, with `PersonGate.Applied` now public), as one count line, so "already applied", 65 of the apply lane's skips from 11 to 18 November, no longer fills the skip list or the reasons this record counts. `test_check_empty_runs.py`, `TodayUnblockTests` and `WorklistTests` hold it.

The card's first lines are now the other half of RULE 76's fixes: `TodayUnblock` reads the newest JOBS file's NEEDS STEVE rows and prints, five at most and highest band first, the ones whose published band tops at $200,000 or more and whose control is his alone (a RULE 50 swap, an account, a sign-in, a form only he can fill), less any the tracker already shows applied and any in a live process. The mail's subject carries the count ("TODAY, Thu 11/19, 3 high-value roles need one click each"), so the notice reaches his phone by the mail he already gets, and nothing new is bought or installed. The 18 November file's 1Password VP and Senior Engineering Manager print as RULE 50 swaps; Beemo, submitted 16 November, does not.

## Addendum, 2026-11-19 (1.0.5.114): three empty runs hold the lane, and the morning reads the hold

What changed beside this record, on the share: from 19 November `dispatch/check_empty_runs.py` also runs after every dispatcher close, and a lane with three empty or failed runs in a row gets a hold file, `dispatch/held/<lane>.hold`, and one heal item in a fixed text written by code (the breaker of ADR: The system watches its scheduled tasks and heals what it may). A run whose every reason a PLAYBOOK.md row calls "not a failure" (no accepted invitee at the company, a blacklist hit) counts as quiet, never empty, so it neither starts nor extends a streak. Two runs still write only the EMPTY-RUNS.md line and the fix proposal above.

What changes in this repository: the 6 AM program reads `dispatch/held/server6am-<lane>.hold` before each of its six lanes and skips a held lane with the hold's reason ("held by the breaker (...): 8 empty runs in a row since 11/12") while the others run; a hold only ever stops a lane, and one that will not read holds it too. The skipped lane's DONE note and close row say held by the breaker, which `check_empty_runs.py` counts as another lane's turn: it neither counts nor breaks the streak. And `run --canary --lane <lane>` is a dry run of one lane on one row by construction, four minutes at most, that answers 0 when the lane ran, answered, was not cut short and wrote no DEFECT line, 3 when it did not, and 4 when it was not started (from 05:28 to 07:00 Central, the morning's quiet). `dispatch/canary.py` runs it for apply, invites and messages at 05:15 (jobhunt-canary.timer), with one board read and one resume check beside them; a lane that fails twice is held for the 06:00 run and its heal opens. A canary reads no hold, because proving a held lane works again is its job.

The box: jobhunt-restart.timer runs `dispatch/box_restart.py` at 04:55 for Steve's daily 05:00 restart (11/12: the box and the web apps on plan-theyard-ss, compute only), which restarts nothing while an item runs, the gate lock is held, the 6 AM run is active or a lane is LIVE on the box, and gives up for the day at 05:15; jobhunt-boot.service writes one line after each boot of what came back. The box agent now reports the dispatcher, the leaders, cross-post, canary and restart timers beside the units it already watched.

Held by `CanaryTests` (the held lane skipped with its reason and the rest run, the canary's options, settings, pass, failure and quiet) and, on the share, `test_box.py` and `test_check_empty_runs.py`.


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

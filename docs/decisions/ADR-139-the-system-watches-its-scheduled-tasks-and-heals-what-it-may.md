# ADR: The system watches its scheduled tasks and heals what it may

Status: accepted, 2026-11-19, at 1.0.5.115. Every scheduled task the job hunt and this site run is watched, and a failure goes up a fixed ladder (a known fix, a retry, a pause, a heal) before anything reaches Steve. This version ships the two guards the ladder leans on first: a gate on the Function's one mail sender for the days no email may go out, and a merger that never takes a header line or an empty row as a tracker row. The ladder's dispatcher half lives on the job-hunt share, not in this repository, and went live the same day; the watch itself, its tests and the last road to Steve follow in later versions as addenda here.

## Reading list

1. [ADR: The tracker merger runs in Azure](https://theyard.stevenstout.biz/?doc=adr-the-tracker-merger-runs-in-azure): the one writer of the tracker, whose row merge this record narrows.
2. [ADR: The network log drafts, Steve sends](https://theyard.stevenstout.biz/?doc=adr-the-network-log-drafts-steve-sends): the reconnects, the second caller of the mail port.
3. [ADR: An empty run is a failed run](https://theyard.stevenstout.biz/?doc=adr-an-empty-run-is-a-failed-run): the run lines and floors a held send writes into, and the check this record's ladder grows out of.

## In plain words

The job hunt runs about thirty scheduled tasks: timers in an Azure Function, a program on the home server at six every morning, a dispatcher that starts model lanes, and the site's own background services. When one of them broke, the way it got noticed was Steve reading a card and asking why. His words on 19 November: "This should apply to all scheduled task and it should run without any questions from me". So the system now watches each task, fixes what it is allowed to fix, pauses what keeps failing, and tells him in one line only when it could not.

Two of the first things it needed are small and live here. On Thanksgiving and over the holidays no email may go out under his name, and two senders (the campaign at 8 AM and the network log's reconnects at 6 AM) mail on their own from the Function. A line in a lane's instructions would not hold them, so the hold is one gate in front of the one sender. And on 19 November two header lines in a lane's spool files became tracker rows 3789 and 3795, which then held the Sheet's mirror and needed a hand moveout. The merger now drops such a line before it can become a row.

## Context

- The Function has one sending class, `GmailSender` (ADR-118), built in four places: the campaign's timer, the network log's timer, the 07:30 card's timer and the campaign's test-send route. The year-end plan lists the no-email days in `J:\dispatch\NO-EMAIL-DAYS.txt`, one date or one range a line, written by the job-hunt lane yearend1120.
- `RowMerge` stripped a first line only when it began with Count or Person. The two replies1119 files of 19 November began `Stamp,Lane,Person,Company,...`, so each header merged as a row (the merge log, 08:27 and 08:53 CST). The rows were moved out by hand at 13:52 CST.
- A run line has six outcome words and no "held". A run that came to blocked starts the Azure rule that mails Steve, so a holiday must not read as blocked.

## Decision

1. **One gate on the sender.** `NoEmailDayGate` wraps every `GmailSender` the timers build. On a Central day the file lists, it lets through only mail whose every recipient is Steve's own address (the card, a NOT WORKING mail) and throws `MailHeldException` for anything else before Gmail is called. A missing or unreadable file holds nothing; a line that reads as neither a date nor a range lists nothing.
2. **The callers hold, write nothing and count nothing.** The campaign's send loop and the reconnects' loop stop at a held send: no email record, no row marked sent, nothing against the day's cap, so the next open day sends as if the held day had not happened. The campaign's run line comes to `unchanged` with the word `no_email_day` and the count `no_email_day`; the network log's run line carries the count. Neither reads as blocked.
3. **No header line or empty row becomes a tracker row.** A row with three or more cells that are header words (Count, Stamp, Lane, Person, Company, Communication, Channel, Date, Phone/Email, Notes, Subject, RefCode, Verified), or with Person, Company, Communication and Date all empty, is dropped before the Counts are given. A file left with no row moves to `rejected-logged/` beside its spool with a row in that folder's README, so it never waits on the card's rejected count; before this a header-only file went to `rejected/` and a lane moved it by hand.

## What holds it

`NoEmailDayGateTests` holds the file's dates, ranges and typos, the held mail and the passed card, the open day, the missing and the unreadable file, and a campaign pass on Thanksgiving that sends nothing, writes nothing and names `no_email_day` on its line. `TrackerMergerTests` holds the two spool files of 19 November as fixtures: their header lines never merge, the rows behind them take the next Counts, and a file of header lines and empty rows goes to `rejected-logged/` with its README row. `NetworkSqlTests` still holds that only the reconnects reach the mail port.

## Consequences

- What becomes true: a listed day sends nothing to anyone but Steve, whatever a switch says; a lane's header line can no longer hold the Sheet's mirror.
- What stops being true: a header-only spool file waiting in `rejected/` for a lane to move it.
- What stays true: every send rule, cap and template of the campaign and the reconnects, and every switch. The gate only ever stops a send.

## Where it sits

The gate is an adapter beside `GmailSender` in the Function's Infrastructure ring, around the port it implements; the held exception sits beside the port in the Application ring, where the two callers catch it. The date file's reader is the Domain's `NoEmailDays`. The merger's rule is in the Application ring's `RowMerge`.

## Files

- [`functions/JobHunt.Infrastructure/Features/Campaign/NoEmailDayGate.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Infrastructure/Features/Campaign/NoEmailDayGate.cs) - the gate and the file's reader
- [`functions/JobHunt.Domain/Shared/Mail/NoEmailDays.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Domain/Shared/Mail/NoEmailDays.cs) - the dates and ranges
- [`functions/JobHunt.Application/Shared/Mail/MailPorts.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Application/Shared/Mail/MailPorts.cs) - `MailHeldException` beside the port
- [`functions/JobHunt.Application/Features/Merge/RowMerge.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Application/Features/Merge/RowMerge.cs) - the header-line and empty-row reject
- [`functions/jobhunt.Tests/NoEmailDayGateTests.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt.Tests/NoEmailDayGateTests.cs) and [`functions/jobhunt.Tests/TrackerMergerTests.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt.Tests/TrackerMergerTests.cs) - what holds it

## Addendum, 2026-11-20 (1.0.5.125): the watch, in shadow

The watch is one timer on the job-hunt Function, `Watch`, at 13, 28, 43 and 58 past, clear of every other timer's minute, with a hand run at POST /api/watch/hand-run for the daytime tests. WATCH picks the mode the way SHEET_MIRROR does: shadow reads every test and writes `J:\dispatch\WATCH.md` whole, healing nothing; on adds the heal ladder (a later version); anything else is off. The template ships it in shadow, and a test holds that, so a redeploy never turns heals on.

Each tick judges what its real source answered, never a lane's own report: the pounce's mode and every switch and secret by name against `dispatch/SWITCHES.md` (W2, W13; a secret's value never leaves the process, only whether it is set); the canaries' last tries in `dispatch/canary.log` (W8); the site on main's commit with CI green, read from both names' /api/version and GitHub's public API, two calls a tick and no token (W9); the review route's second actions (W11); every scheduled task's heartbeat (W12): the nightly card's firing and merger rows, each Function process's newest listed run, and each unit the box card lists; what reached him on the card (W14); the night's supply (W15); the no-email day (W16); the watchers themselves, the dispatcher's HEARTBEAT, the last tick, the box card's snapshot and the dispatcher's pause (W17); the heal budget in HEALED.md (W18); and the four fixed slots from their own records (W6). A source with no reader yet is "not read", never a pass. `dispatch/watch/<date>.tsv` gets a line only when a test's word changes.

The nightly card now lists every process that writes a run line, nineteen with the watch, so W12 can read one row each: the board scan, the fresh pass, the pounce and its day line, the pounce follow-up, the reply drafts, the watch, and the two hand runs, whose next run reads "on request". The card's schedule reader takes the step and range forms the newer timers use (`5/15`, `*/15`, `2-59/5`), and the Function's health route reports CAMPAIGN_SQL, FRESH, POUNCE, POUNCE_RUN and WATCH by word beside the others.

Held by `WatchTests`, `WatchPassTests` and `WatchBeatsTests` (each test with the failing reading it exists for, on fakes and a throwaway folder), `TemplateTests` (WATCH in shadow), `ProcessRunsTests` and `ProcessReadersTests` (nineteen processes, the fourteen timers' crons, the step forms) and `cloud.spec` (nineteen sections). A smoke tick on 19 November, the same code over the live share and the public web from the box, read 58 lines: 46 passed, 2 failed (the review route at 3 of 52, a January item; that morning's card, written before 1.0.5.107 took its EMPTY RUN lines out) and 10 were not read.


## Addendum, 2026-11-20 (1.0.5.127): the heal ladder

With WATCH on, a tick climbs the ladder of the year-end plan's 7b for every failing reading, in the dispatcher's own terms, so a heal the watch opens and one `check_empty_runs.py` opens are one kind of item (`J:\dispatch\dispatch_heal.py`). The day's change log dates when each test began failing. The first failing tick writes one HEALED.md line: step 1, the PLAYBOOK.md row whose pattern the reading names, or step 2, a retry at the next tick. The third failing tick in a row, 45 minutes on and so past Gmail's 30-minute backoff, opens step 4: one heal item, `30-heal-<task>-<date>.md`, in the fixed text with `Heal: true`, `Now: true`, the standing approval and `Entered_by: watch`, beside its fixture in `dispatch/heal-fixtures/`, into `dispatch/ready` when policy.json's heal_mode is on and `dispatch/heal-shadow` otherwise. Each is written to a temp name and replaced, so the dispatcher never reads half an item; the dispatcher's own `is_heal` accepts it.

The budget is the dispatcher's: at most two code heals a Central day across every writer, one open item a failure, two attempts a failure, then a line in `dispatch/NOT-WORKING.md` instead. A failure whose only fix is outside self-heal is refused and named, never built: the pounce's mode and a switch (a send switch, e), a secret (his red line, d, whose line names the one control) and a no-email day's breach (a send, a). So is a heal lane's own failure. A "not read" never climbs. Step 3, the breaker, stays with the runs it stops (`check_empty_runs.py`, `canary.py`, the 6 AM program's hold read): a hold on a Function process would stop nothing, so the watch writes none.

The gate holds the outside list's paths: `data/heal-outside.json` names the campaign's templates, words and rules, the pay floor, the run floors, the LinkedIn ceilings, TextCheck, the infra templates, the ratchet, the byte budgets, the workflows and the list itself, and `scripts/heal_outside.py` turns a `ready/heal-*` branch red in the node job's verdict and in preflight's catch list when it touches one of them or deletes any file. Any other branch is never held.

Held by `HealLadderTests` (each step by its tick, the budget, the outside letters, a heal lane, the writer over a throwaway folder), `HealOutsideTests` (a fixture heal that touches a ceiling, an infra file and a template and deletes a test is red, an inside one and a planning lane's are not, and both runners call the script) and `GateVerdictTests` (its red reaches the annotations).

## Addendum, @DATE@ (@VER@): what reaches him

A failure the ladder could not fix reaches Steve one way, as information and never a question (RULE 77 check 4; the year-end plan's 7c): one line in `J:\dispatch\NOT-WORKING.md`, "NOT WORKING: <what> since <when>. Tried: <fixes>. Needs you: <one control, or nothing>". The tick that writes a new line puts it under TODAY.md's title and sends one mail to his own address through the card's own sender behind the no-email-day gate, subject the line's "NOT WORKING: <what>", at most once a failure a Central day (`dispatch/watch/not-working-mailed.tsv`); a mail that does not go fails nothing, because the line is on the card either way. The 07:30 card prints the open lines first, under "NOT WORKING", above the roles only he can unblock. A watch line leaves when its test has passed on two ticks: the tick moves it to `NOT-WORKING-cleared.md` and writes a "5 cleared" line in HEALED.md. The pounce's mode, a secret and a no-email day's breach write their line on the first failing tick, because no retry or heal can fix them.

The other roads close. The card's lanes print the proposals as one count that asks nothing, "Waiting for a planning session: N proposals (dispatch/STATUS.md); none needs you.", and the "Proposed: ... Queue it?" lines are gone with the helper that found them; W14 holds the card to that. The alert template keeps one mail of its own, a stopped Function (no Merger run in 30 minutes), because a dead Function cannot write its own NOT WORKING line, and the share's transactions alert, which is money. The failed-or-blocked run mail and the silent campaign and silent network rules are deleted: W12 reads those processes every quarter hour and the ladder heals them or names them.

Held by `NotWorkingTests` (the card's first lines, the clearing after two passing ticks, the line under TODAY.md's title once, one mail a failure a day and a failed send that fails nothing), `TodayPartsTests` and `TodayCardDayTests` (one count, no queue-it line, W14 passes the lanes) and `TemplateTests` (three resources, none of the removed queries).

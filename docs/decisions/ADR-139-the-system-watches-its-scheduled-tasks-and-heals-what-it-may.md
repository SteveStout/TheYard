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

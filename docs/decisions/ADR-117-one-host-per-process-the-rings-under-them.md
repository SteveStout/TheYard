# ADR: One host per process, the rings under them

Status: accepted in part, 2026-10-23, at 1.0.4.44. The job hunt's Function is cut into the same rings as the site: `JobHunt.Domain` holds the rules, `JobHunt.Application` holds the use cases and their ports, `JobHunt.Infrastructure` holds the adapters, and the host sits over them. `RingTests` holds the direction. One host per process and one Function App per host are the next step. They wait on Steve's call on the hosting plan, which is written below.

## Reading list

1. [ADR: The tracker merger runs in Azure](https://theyard.stevenstout.biz/?doc=adr-the-tracker-merger-runs-in-azure): the Function this record cuts, and every process it runs.
2. [ADR: The Nightly Function on the Admin tab](https://theyard.stevenstout.biz/?doc=adr-the-nightly-function-on-the-admin-tab): the card that watches it.
3. [ADR: The onion cut into vertical slices](https://theyard.stevenstout.biz/?doc=adr-the-onion-cut-into-vertical-slices): the rules the site holds, which this record starts to hold for the Function.

## In plain words

The job hunt's Function grew one feature at a time into one project of 43 files. The tracker merger, the inbox pass, the Sheet mirror, the daily card and the nightly clock all sat side by side with the Gmail, Sheets and file-share code they use. A rule about which job-board mail is an alert sat in the same project as the code that talks to Google.

From this version the rules are in one project that talks to nothing, the processes are in a second that talks only through interfaces, and the Google, GitHub and share code is in a third. The project that Azure runs is now only the clock and the wiring. The site was built this way from the start. Now the Function is too, and a test fails the build if a project reaches the wrong way.

## Context

Measured at 1.0.4.43: `functions/jobhunt` held 43 `.cs` files. There were six triggers (`AtsSweep`, `DailyCard`, `Merger`, `MergerHealth`, `Nightly`, `SheetMirror`) and the slice folders `Ats/`, `Card/`, `Leads/`, `Merge/` and `Sheet/`, each mixing rules, use cases and adapters. There were three ports (`IInbox`, `IMergeStore`, `ISheetGrid`) and no architecture test. Steve's rulings: each process is its own Function on the App Service plan (10/23), every Function has its own Admin tab card (10/22), and one host project per process is fine (11:37, 10/23).

## Decision

1. **Three rings.** `functions/JobHunt.Domain` holds the rules with no I/O: the ATS rules, the mail address rules, the leads filter and the application cap, every board's alert reader, the tracker's CSV text, the Sheet plan and the tracker gate, the card's text, the Central clock and the slot table, which is linked from TheYard's Domain as before. `functions/JobHunt.Application` holds the use cases and their ports: the inbox pass's parts (the ATS sweep, the leads harvest, human mail, triage), the merger and its health, and the Sheet mirror, with `IInbox`, `IMergeStore` and `ISheetGrid`. `functions/JobHunt.Infrastructure` holds the adapters: Gmail, Google Sheets, Google Calendar, the share, the GitHub dispatch and the share snapshot. `functions/jobhunt` is the host: the six triggers and `Program.cs`.
2. **Slices inside each ring.** A ring's file sits under `Features/<Slice>/` (`Ats`, `Leads`, `Merge`, `Sheet`, `Card`) or at the ring's root, and declares `JobHunt.<Ring>.Features.<Slice>` or `JobHunt.<Ring>`. Each project imports the namespaces it may use once, in its project file.
3. **The direction.** Domain references nothing of the job hunt's. Application references Domain only. Infrastructure references Application and Domain and never the host. Domain and Application reference neither the Azure SDK nor an HTTP client, and nothing in them reads the environment.

## What holds it

`RingTests`, new: each ring leans only inward, only Infrastructure reaches the share or the network, nothing in Domain or Application reads the environment, and a file under a ring's slice declares that slice's namespace. `TemplateTests` now reads every setting across the host and the three rings, not the host's folder alone.

## Not done in this version, and why

- **One host per process** (`JobHunt.Host.AtsSweep`, `DailyCard`, `Merger`, `Nightly`, `SheetMirror`), each with one trigger, its settings bound once into a record, and one Command dispatched. A host with no Function App to run in is code nobody runs, so the hosts are built with their apps.
- **One Function App per host.** The prompt names the existing plan in RG-JOBHUNT-SS, which is a Flex Consumption plan (FC1) holding one app. The off-the-laptop prompt names TheYard's B2 App Service plan. Moving the job hunt's processes onto the B2 is a hosting plan change for those apps, which this lane may not make on its own (no SKU, tier or plan change), and the cutover runs two copies of the merger until the old app's switches are turned off. The call is mine, and the lane asked it once: run the five processes as five apps on the B2 plan TheYard runs on (shared CPU and memory with the site, timers every minute with no cold start), or stay on Flex Consumption with one plan per app.
- **A query or command per process step, and CqrsTests for the Function.** These come with the hosts, because each host's trigger dispatches exactly one command.
- **Five cards under Cloud infrastructure.** These come with the apps they read.
- **The nightly card's process sections, at stage B.** Each section keeps its place and gains a "which app" line read from its request's cloud role, and the runs reader's role filter (`InsightsProcessRuns`, one role today) becomes the list of the five apps' roles.

## Consequences

Every Function file moved once and changed only its namespace line. The tests pass by namespace change alone, through one import per namespace in the test project. The deploy still publishes `functions/jobhunt`, which now carries the three rings as references, so the running app is unchanged.

## Where it sits

Every ring of the job hunt's Function, and none of the site's: the Function shares this repository and its Domain's slot table, and nothing else. Dependency inversion is the principle it serves, the processes owning the interfaces and the adapters implementing them, and single responsibility for the host, which wires and schedules and decides nothing. It cost a namespace line in every file and three project files. What would change it: a process that needs a library no ring may hold, which would be an adapter in Infrastructure behind a port the process declares, never a reference from an inner ring.

## Files

- [`functions/JobHunt.Domain/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Domain) - the rules
- [`functions/JobHunt.Application/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Application) - the use cases and the ports
- [`functions/JobHunt.Infrastructure/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Infrastructure) - the adapters
- [`functions/jobhunt/`](https://github.com/SteveStout/TheYard/tree/main/functions/jobhunt) - the host: the triggers and Program.cs
- [`functions/jobhunt.Tests/RingTests.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt.Tests/RingTests.cs) - holds this record

## Addendum, 2026-10-25 (1.0.4.69): the mail search is data, and the Gmail adapter writes it

The Domain wrote Gmail's search syntax: the tracking systems' sweep (`in:inbox from:(...)`), each board's alert search (`from:...`, `subject:"..."`) and the delivery failures' search, and the Application ring wrote four more (the triage window, the human mail check, the harvest's inbox prefix and the campaign's reply search). A search language is an adapter's concern. From this version the Domain keeps what a board is, its senders and the subject phrases its alerts carry, as data; the Application ring carries a search as a `MailSearch` record (senders, subject phrases, inbox only, newer than, not from me), and `IInbox.FindAsync` takes one; the Gmail adapter's `GmailQuery.Compose` is the one place the syntax is written.

The composer is [`functions/JobHunt.Infrastructure/Features/Ats/GmailQuery.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Infrastructure/Features/Ats/GmailQuery.cs), and the search it reads is [`functions/JobHunt.Application/Shared/Mail/MailSearch.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Application/Shared/Mail/MailSearch.cs).

`RingTests.Nothing_in_Domain_or_Application_writes_a_mail_search` holds it: no code line under Domain or Application writes `from:`, `subject:`, `in:inbox` or `newer_than:`. It was red on 1.0.4.68 with only its file applied, naming ten lines across eight files. `GmailQueryTests` holds every search the Function sends, nine of them, to the string it sent at 1.0.4.66 character for character, so Gmail sees nothing new. The test fakes answer the search object, and the inbox pass's fake reads the string the adapter would send.

## Addendum, 2026-11-09 (1.0.4.166): the machine lanes take the same rings

A third process joined the two, `server/JobHunt.Server`, on the same three rings and the same direction: `JobHunt.Server.Domain` references nothing of its own process, `JobHunt.Server.Application` references Domain only, `JobHunt.Server.Infrastructure` references both and never the host. Nothing in Domain or Application reads the disk, the network, the clock or the environment. `RingTests` in `server/JobHunt.Server.Tests` holds it, and ADR-119's addendum of the same version says why the process exists and what crosses into it.

## Addendum, 2026-11-09 (1.0.4.168): the reply lane gets a second dispatch at 5 AM

On 8 November the 4 AM dispatch reached GitHub and the runner died 31 seconds later, before the lane began: the action's own setup fetched its runtime, got a 504, and the next step found no `bun`. The leads run at 3:50 and the triage run at 4:20 died the same way. The others come round again within hours; the reply lane comes round once a day, and the 6 AM messages lane then read the day before's queue. The Function's hourly clock now asks one more question in the 5 o'clock hour in Central: did any reply run of the day write `linkedin-queue.md`? `Bookend.ReplyRunsAsync` lists `nightly/<date>/` on the share for folders named `*-reply-*` (a `-dry` folder never counts) and `ReplyRetry.Needed` answers in the Domain; when no run holds the queue, the hour dispatches the reply lane once more and its run line reads `reply_retry`. A day whose queue exists finds the hour as it always was, not a slot. The hour is the clock's own work, like the picker's, so it is not a row in `NightlySlots`. `ReplyRetryTests` holds the hour on both sides of the clock change and the decision on real folder names.

## Addendum, 2026-11-09 (1.0.4.171): one card at 7:30, written whole and mailed to its reader

The job hunt left its reader five places to look each morning (the hourly CARD.md, the night's JOBS file, the blockers card, the dispatcher's status and every closed lane's notes), so he assembled his own day. The Function now writes one card, `TODAY.md` at the share's root, in the 7 o'clock hour in Central (`CentralClock.IsTodayHour`, a timer at 30 past every hour), replaces the file whole, and mails it to his own address from his own Gmail with the token the campaign sends with (`GmailSender`, built in the host, so the Card slice uses no Campaign type). No model runs. The order is fixed: the CRITICAL sends with their full text, the essays the 6 AM run submitted, the replies with each draft in full, the calls in the next 72 hours with their must-answers line from the company's NOTES file, the dispatcher's lanes (running, paused, held with its reason, each proposed item as "queue it?"), what lanes that closed in the last day left for him, the walls (Mondays, or where a human has replied), one decision, and the rejection reads carried forward from the file it replaces. `TodayParts`, `TodayCard`, `TodayReads` and `TodayMail` in the Domain take text and answer text; `TodayCardSource` in Infrastructure does every read; the reply queue is read by format 2's grammar against the reply lane's own fixture. Anything named in `dispatch/ANSWERED.md`, and any draft with a DO-NOT-SEND note beside it, never reaches the card. Past 15 minutes at 200 words a minute the held replies fold into one line. `TODAY_CARD` unset or `on` writes and mails, `write` only writes, `off` does neither: unset is on, so a template redeploy that drops the setting cannot silently stop the card. A day whose log line already says mailed is left alone, and `POST /api/today/hand-run` runs the same path at any hour. `CalendarWeek.ReadAsync` is the calendar read the hourly card and this one share. `TodayCardTests` holds the order, the answered and DO-NOT-SEND drops, the carry-forward, the cap, the hour on both sides of the clock change and the one address the mail goes to.

# ADR: The nightly picker, code gates and the model ranks

Status: accepted, 2026-11-05, at 1.0.4.143, after the two versions before it (1.0.4.141 and 1.0.4.142) that cleaned the alert leads and kept every posting the board scan reads. The job hunt's Function picks the night's roles in the 2 o'clock hour Central with every rule code can decide, and a Claude lane on a GitHub runner ranks what is left and writes the morning's file.

## Reading list

1. [ADR: The campaign sends, the model writes rows](https://theyard.stevenstout.biz/?doc=adr-the-campaign-sends-the-model-writes-rows): the same split, code holding the rules and the model doing the judgment, and the 1 AM scan this reads.
2. [ADR: One host per process, the rings under them](https://theyard.stevenstout.biz/?doc=adr-one-host-per-process-the-rings-under-them): the rings the Jobs slice is cut into.
3. [ADR: The modular monolith](https://theyard.stevenstout.biz/?doc=adr-the-modular-monolith): why the level rule and the pay lines are shared concerns and not the campaign's.
4. [ADR: The tracker merger runs in Azure](https://theyard.stevenstout.biz/?doc=adr-the-tracker-merger-runs-in-azure): the alert leads and the tracker's SQL copy the picker reads.

## In plain words

Every morning should start with a list of real jobs that were checked overnight, so the day goes to applying and not to searching. Rules that never change, such as remote only, posted in the last three weeks, the right level, the right stack, the pay floor, not applied before, and no more than three applications to one company in two months, are code with tests. The parts that need reading and weighing, such as whether the posting is still open on the employer's own site, how it ranks, and whether its hiring process is a wall of automated tests, are done by a model that writes a file. Nothing in the night submits, sends or signs in anywhere.

What that is worth: a reviewer can see exactly which roles were thrown away and why, because every role a gate refused is written with the gate's name, and the model can only choose among roles code already let through.

## Context

On 5 November there was no list waiting. Three night pieces existed and none produced one: the inbox pass harvested alert leads and threw away real ones (fixed in 1.0.4.141), the board scan ran at 8 AM after Steve was awake and kept one date a company (moved to 1 AM and made to keep every posting in 1.0.4.142), and the postings history had been planned and not built. Steve's ask, 5 November: ten to twenty roles a night, ready to read in for blockers.

## Decision

1. **The gates are code in the Function.** `JobsGates` (Domain, Jobs slice) holds every deterministic rule with named cases: not a staffing firm placing an unnamed client and not a former employer; remote, or St. Louis hybrid at one day a week at most, with a St. Louis hybrid of unknown days passed on to Steve's table; posted or first seen 21 days ago or less; Principal, Staff, Lead, Architect, an engineering Manager or Director, and a plain Senior only with a band topping $180,000; a band whose top is under $170,000 skipped with its number; a stack that touches C#, .NET or Azure or an AI-native lead role; not the same role applied to; and under RULE 50's three applications a company in 60 days. The first failure is written with its gate.
2. **One pay line, in one place.** `PayFloor` (Domain, Shared/Pay) holds RULE 72's $180,000 pass and $170,000 flag. The picker and the campaign's `Strength` read it, so the band points of the email campaign moved with it (Strength's formula version 2).
3. **The picker runs in the clock's own hour, not a new slot.** `Nightly` asks `CentralClock.IsJobsHour` (the 2 o'clock hour Central, asked as a local hour) and runs `JobsPick` through `JobsHour` before it dispatches `jobs` on the nightly switch. NightlySlots is not touched, because the Admin card judges a month of firings against that table.
4. **The picker reads, and writes one file.** `JobsPick` (Application) reads the postings the 1 AM scan saw open tonight with their company's name, a description only for a posting the other gates passed, today's and yesterday's alert leads that passed the inbox pass, the tracker's SQL copy and the do-not-apply list, and writes `nightly/jobs-candidates-<date>.tsv`: the supply line, one row a candidate, and every refused role under `# FAILED` with its gate. An alert lead whose company and title the scan read tonight is dropped for the posting, which is confirmed. Without the postings tables it writes from the alert leads alone and says so; with no tracker copy it reads the share's CSV and says so.
5. **The model ranks and writes the morning's file.** `nightly.yml` gains the `jobs` mode on Opus with the web, 80 turns and 40 minutes, reading `nightly/JOBS-LANE.md` on the share: confirm each alert lead on the employer's own board, read wage filings where no band was published, rank, scan for expectations, evergreen postings and blind-test funnels, and write `JOBS-<date>.md` with its READY TO APPLY table in the columns the 6 AM apply lane parses, its NEEDS STEVE, UNCONFIRMED, FLAG and SKIPPED tables and its supply line, then append the ready rows to PRIORITY-BOARD.md. The lane submits nothing, sends nothing and signs in nowhere.

## What holds it

`JobsTests` (every gate, the pay lines, the alert rows, the candidates file's shape, the drop of an alert the scan confirmed, the two fall-backs, the hour, the statements' columns and the jobs mode in nightly.yml), `CampaignTextTests` (Strength on the new lines), `PostingsTests` and `PostingsSeedTests` (the postings the picker reads).

## Not done in this version, and why

- **The picker's card.** Every job-hunt process gets its own card under Cloud infrastructure; the Admin slice belongs to another lane this week, which reads the picker's facts (its run name, its hour, its file and its counts) from that lane's DONE note.
- **Confirmation from the cloud for every board.** Ashby and some Workday tenants serve JavaScript only, so the lane cannot read them from a runner; those rows are written as unconfirmed for the daytime lane.

## Files

- [`functions/JobHunt.Domain/Features/Jobs/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Domain/Features/Jobs) - the gates and the files
- [`functions/JobHunt.Domain/Shared/Pay/PayFloor.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Domain/Shared/Pay/PayFloor.cs) - RULE 72's two lines
- [`functions/JobHunt.Application/Features/Jobs/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Application/Features/Jobs) - the picker and its ports
- [`functions/JobHunt.Infrastructure/Features/Jobs/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Infrastructure/Features/Jobs) - the SQL reads
- [`functions/jobhunt/JobsHour.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt/JobsHour.cs) - the clock's 2 o'clock work
- [`.github/workflows/nightly.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/nightly.yml) - the jobs mode

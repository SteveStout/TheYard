# ADR: Old folders are zipped, not kept

Status: accepted, @DATE@, at @VER@. The archive job, `JobHunt.Server archive`, and its 03:40 timer on WickedAwsome ship at @VER@; its first passes run after the publish.

## Reading list

1. [ADR: The modular monolith](https://theyard.stevenstout.biz/?doc=adr-the-modular-monolith): the third process, `server/`, that the job is a verb of, and the ring and slice rules it follows.
2. [ADR: The tracker merger runs in Azure](https://theyard.stevenstout.biz/?doc=adr-the-tracker-merger-runs-in-azure): the job hunt's share and what reads it on a timer.
3. [ADR: Automated tests never touch a real database or permanent storage](https://theyard.stevenstout.biz/?doc=adr-automated-tests-never-touch-a-real-database-or-permanent-storage): why every test of the job runs on a fake share or a temp folder.

## In plain words

The job hunt keeps its working files on one Azure file share. Every night adds forty to a hundred run folders to it, and nothing ever took one away. A share is billed for looking: each folder costs a little every time something lists the share, and a lane that walks it pays for every folder it passes. So once a folder is old enough that nothing reads it any more, it is packed into one zip, the zip is opened again and checked file by file against the folder, a line is written saying where it went, and only then is the folder moved aside by one rename. Nothing is deleted: the zip is the second copy, and the moved folder waits until a person empties the place it went.

What that is worth: the share stops growing a folder at a time, any day's files can still be read from a single zip at the same paths, and a file is never lost to a copy that did not finish, because the move only happens after the check.

## Context

Counted on 23 November, one level each, by name:

| Folder | What was there |
| --- | --- |
| `nightly\<day>\` | 35 day folders, about 44 run folders a day; 32 of 44 written by GitHub runners, the rest by the box |
| `dispatch\done\<day>\` | 15 day folders of closed dispatcher runs |
| `dispatch\locks\` | 180 released and 11 dead lock files, a README, no live lock |
| `inbox\link-tokens\merged\` | 557 rows already folded into `link-tokens.csv` |
| `mentor\logs\` | 1,099 files and 170 subfolders holding 3,110 more, about 104 MB, nothing written since 27 October |

A listing or a write on this share is $0.015 for 10,000 (the share card's own price, `ShareCard.cs`). The whole share cost about $0.24 on 22 November. So this is hygiene with a ceiling on growth, worth under a dollar a month, and it is described that way. The bigger saving, emptying `J:\_to_delete\2026-10-24`, is a person's decision and is not taken here.

Every reader of a day folder looks back a short way: the topic import three days, the last card seven, the morning cards today only. One reader looks back forever: the lane's crash hold (`LaneRunner.CrashedAsync`) stops a lane while any day holds a run that died part way, and that hold "never runs out on its own".

## Decision

[![The archive job: check that no cost1123 row is open, pick what is old enough, refuse a held day, then zip, reopen and check, log, and move by one rename; a zip that does not match is renamed .failed and the source stays; the daily line names what it could not do](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/archive-job.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/archive-job)

*A preview. [Open the archive job in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/archive-job) to zoom in and follow it.*

1. **One verb on the box, one timer.** `JobHunt.Server archive [--dry-run] [--unit <path>]`, a slice of its own in `server/` (`Features/Archive`), a port for zip, verify and move (`IArchiver`), and its adapter over the mount (`MountArchiver`). `jobhunt-archive.timer` runs it at 03:40 Central every night, `Persistent=true`, installed by `server/publish-to-wickedawsome.sh`. No new script on the share.
2. **What a unit is, and when it is old enough.** A nightly day folder or a dispatcher done day more than fourteen Central days before today; the released and dead lock files more than fourteen days past the stamp in their name, one zip a month, never a live `.lock` and never the README; the folded link-token files last written more than thirty days ago, one zip a month; and `mentor\logs`, once, by name, with a `MOVED.md` left where it was, because public decision records cite its files by path.

```live path=server/JobHunt.Server.Application/Features/Archive/ArchiveUnits.cs region=archive-ages
```

3. **What it refuses.** A nightly day with any run that is not settled, by the same two tests the crash hold applies (now one rule, `RunSettlement`, that both call), and any day a live document names, read from `nightly\*.md` that are not backups and `START-HERE.md`. Archiving either would lift a hold or move a folder a lane still reads.

```live path=server/JobHunt.Server.Application/Features/Archive/ArchivePlan.cs region=archive-refusal
```

4. **The order, for every unit.** Zip it with every path from the share's root kept. Reopen the zip from disk and read each entry to its end: the entry count must equal the files, and each entry's length and CRC-32 must equal a second read of its source. Write one line to `J:\_archive\datamove1123\ARCHIVE-LOG.tsv` (UTC time, unit, files, bytes, the zip's SHA-256, where the original went). Only then move the original by one rename into `J:\_to_delete\datamove1123\<kind>\`, which the mount sends to Azure as one server-side move. A name already taken gets -2, -3. A zip that does not match is renamed `.failed` and its source stays where it was.

```live path=server/JobHunt.Server.Application/Features/Archive/ArchiveRun.cs region=archive-one-unit
```

5. **Small and polite.** At most five units of a kind a run, so the backlog clears over a week rather than in one busy hour, and a run starts no unit that would take it past 3,000 calls to the share. It lists only its five folders, one level, and inside a chosen unit only that unit. While a cost1123 row is open on `state.md` it does nothing and says so, because that lane's diagnostic log is reading the share; a row older than 48 hours with no close does not hold it.
6. **Where it may write.** The program's write policy gains two folders, `_archive/datamove1123/` and `_to_delete/datamove1123/`, and the one note `mentor/logs/MOVED.md`. Everything else stays refused before the disk is touched, and the adapter also refuses a source outside the job's five folders.
7. **It says what it could not do.** The run's last line counts what it archived, failed, refused and left, and names each refused day with its reason and any moved folder that has come back. When it names anything, the same line goes to the morning's state rows as a NOTE.

## What holds it

- `ArchiveTests`: the job against the share in memory and an archiver that records each unit. Five of a kind oldest first and the rest waiting; a day with a defect line, a HELD note, a progress file with no DONE note, or a name in a live document refused and named; a dry run that reads names only; nothing touched while a cost1123 row is open, and a two-day-old row holding nothing; a zip that does not match set aside with its source left; `mentor\logs` once with its note, and named when it comes back; the 3,000-call stop.
- `ArchiveAdapterTests`: the adapter on a temp folder. A day folder zipped and reopened to the same lengths and CRC-32s, CRC-32 held to its check value, the whole job end to end with the log and the rename, -2 for a taken name, `.failed`, and a source or target outside the job's folders refused.
- `SlotTests`: the two new folders and the one note, and nothing beside them.

## Consequences

- The share's folder count stops growing with every night. It only falls once `J:\_to_delete\datamove1123\` is emptied, which is Steve's call; until then the originals and the zips are two copies.
- A day's files are found at the same path inside its zip, so a record or a note that names `nightly\<day>\...` can still be followed.
- A day with an unsettled run stays on the share, and the job names it every night until a person settles it.

## What is not done here

The dispatcher's queue in SQL, the link codes in SQL and the pictures in blob storage were looked at and held, each for a reason: the dispatcher and the code minter run as Python on the share with no path to SQL, and coding lanes name their mockup pictures by their J: paths. Each needs a planning session of its own first.

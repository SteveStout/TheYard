# ADR: The tracker merger runs in Azure

Status: accepted, 2026-10-22. The job hunt's tracker merger moved from the laptop to the job hunt's Function, `func-jobhunt-ss`, on 22 October between 09:45 and 09:47 Central. It is now the only writer of `Job_Hunt_Tracker.csv` and `Job_Hunt_Tracker_MOVED_OUT.csv`, it runs every minute while the `MERGER` setting is `on`, and the Admin tab's nightly card asks it once a minute whether it is keeping up.

## Reading list

1. [ADR: The Nightly Function on the Admin tab](https://theyard.stevenstout.biz/?doc=adr-the-nightly-function-on-the-admin-tab), the Function this runs in and the card that watches it.
2. [ADR: The onion cut into vertical slices](https://theyard.stevenstout.biz/?doc=adr-the-onion-cut-into-vertical-slices), the port-and-adapter shape the merger keeps in a project of its own.

## In plain words

Every lane of the job hunt, on the laptop or on GitHub's runners, records what it did as a row in one tracker file. Lanes never write the tracker. Each drops a small file into a spool folder, and one merger appends those files to the tracker in order and files them away. Until 22 October that merger was a PowerShell loop on Steve's laptop, so a row written while the laptop slept waited for it to wake, and the cloud lanes' rows crossed the sync twice before they landed.

Now the merger is a timer in the Function that already runs the nightly lanes. It reads the share directly, writes the tracker on the share, and the laptop gets a copy on the way down.

What that is worth: one writer per file, wherever the writer runs, and the laptop off the path a row takes to the record.

## Context

- **One writer per file is the rule the job hunt lives by.** Two lanes writing one file have overwritten each other before; the spool and the single merger exist so that never happens to the tracker. Moving the merger only works if it moves whole: for a minute, two mergers on two copies of the tracker would be two writers.
- **The laptop was on the critical path.** The nightly lanes run on GitHub Actions against the share. Their rows reached the tracker only after the laptop's sync brought them down, the laptop's merger appended them, and the sync took the tracker back up. A laptop asleep at 4:00 AM held every row of the night until morning.
- **Measured before the move** (22 October, the job hunt's own notes): the share's sync had failed one file on every pass for two days, `merge.log`, because the laptop merger appended to it while the copy ran. The share's copy of the log was 23.6 MB against 31.2 MB on the laptop, so a cloud lane that read it read a stale log.

## Decision

1. **The merger is a timer function in the job hunt's Function app.** `Merger` runs at second 0 of every minute and does nothing unless the app setting `MERGER` is `on`, so the hand-over is a setting, not a deploy. It lists the two spool folders, `tracker-inbox/` (rows the laptop's lanes write, carried up by the sync) and `inbox/tracker-inbox/` (rows the cloud lanes write on the share), and runs new rows, then fixes, then approved moveouts, the order the PowerShell merger always ran them.
2. **The rules talk to a port.** `IMergeStore` is seven calls over paths relative to the job-hunt folder: exists, read, list, ensure a folder, write, rename over, move, append. `ShareMergeStore` is the Azure Files adapter over the Function's existing `ShareClient`; the tests run the same rules over a plain folder on disk (`FolderMergeStore`). Nothing in the rules knows the share exists.
3. **A tracker is never half written.** Every change writes the whole new file to `<file>.merger-tmp`, reads it back and compares it byte for byte, and only then renames it over the target. A failure anywhere before the rename leaves the tracker as it was and the spool file where it was, for the next pass. A row is copied to the moved-out file before it is removed from the tracker, so a failure between the two can duplicate a row and never lose one.
4. **The split is by file, not by job.** The tracker and the moved-out file had only ever been written by the merger, so they moved cleanly. The `.md` files the laptop merger also appended to (`state.md` and the rest) are still edited directly by laptop lanes, so a cloud writer there would be a second writer. Those appends stay on the laptop: its `Tracker Merger` task runs `merge-tracker.ps1 -Loop -AppendsOnly`, and `merge-tracker.ps1` carries `$TrackerRowsInAzure = $true`, which makes a plain run leave the tracker alone.
5. **Parity was proven before the hand-over, byte for byte.** A test runs the PowerShell merger and the new one on two copies of the real tracker and moved-out file with awkward rows (commas, quotes, line breaks, a header-only file, a fix and an approved moveout) and requires identical bytes: master 1,743,354 B and moved-out 79,903 B identical on the laptop, and the same five spool files gave the same SHA-256 on Azure in a scratch folder. One difference is on purpose: a row file whose `[TID:]` is already in the moved-out file is filed as already present instead of merged a second time, which the PowerShell merger, reading only the tracker, did not check.
6. **The template keeps it on.** `infra/jobhunt.bicep` sets the Function's app settings as one list, and a deploy of the template replaces every setting the app holds with that list. A switch that is on in Azure and missing from the template is off after the next deploy. So the template carries `MERGER` `on`, and both dispatches `on` as they run, and a test in `functions/jobhunt.Tests` fails if `MERGER` is missing or not `on`, if either dispatch is not `on`, or if the Function reads a setting the template does not name (three are named as set out of band: the scratch root, the GitHub token, and the hand test of the clock).

```live path=infra/jobhunt.bicep region=jobhunt-settings
```

7. **CI runs the Function's tests.** The api job runs `dotnet test api`, which never built the Function. A job of its own runs `dotnet test functions/jobhunt.Tests` on every push, with the .NET 9 runtime the Flex Consumption app runs installed beside the SDK. The parity test needs Windows and a copy of the real tracker, so on GitHub's Linux runner it reports that it did not run, by design; every other test must pass.

```live path=.github/workflows/ci.yml region=functions-job
```

8. **The card asks whether it is keeping up, and sees counts and ages only.** The site's identity holds Reader and Monitoring Reader on the job hunt's group, which read the Function's record and metrics and nothing inside the share, and it is given no account key. So the Function answers the question itself: `GET /api/merger/health` lists the two spool folders, reads `merge.log`'s write time from the same listing, opens no file, and answers five numbers: seconds since `merge.log` was last written, files waiting, files waiting past five minutes, past ten, and the oldest wait. The route is anonymous because those five numbers are exactly what the card shows the public; the site would otherwise need a key to the app in its settings. The nightly card reads it once a minute with the Function's metrics, beside them on the same kept minute, from the host name the hour reads off the Function's record, and keeps only the five numbers under names of its own: any other field in the answer is dropped unread. The row "Is the tracker merger keeping up?" is good while nothing has waited past five minutes, information while a file has, and a warning once any file has waited past ten (`CloudThresholds.MergerWait`), because a merger that runs every minute and leaves a file through ten passes is stuck, not busy.

```live path=api/TheYard.Tests/NightlyCardTests.Merger.cs region=nightly-merger-tests
```

The age of a waiting file is counted from its own write time, which for a laptop row is when the lane wrote it on the laptop, not when the sync carried it up. A laptop that comes back online after more than ten minutes therefore shows its held rows as waiting past ten for the minute or two the merger needs: the card is reporting the row's whole trip, sync included, which is the question the operator asks.

## A row file's path

![A laptop lane's row goes up by the share sync and a cloud lane's is written to the share; the Merger appends it through a temp file it reads back and renames over, files it under merged and logs it; the sync brings the tracker down; the health route counts what waits for the Admin tab](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/merger-row-path.svg)

## The roll back

Three switches, in this order, each one undoing one part:

1. `MERGER` to `off` on `func-jobhunt-ss` (`az functionapp config appsettings set ... --settings MERGER=off`). The timer keeps firing and does nothing. A row file left in the share's spools stays there.
2. In the job-hunt folder, `$TrackerRowsInAzure = $false` in `merge-tracker.ps1`, so a run writes the tracker again.
3. Restart the laptop's `Tracker Merger` task without `-AppendsOnly`, and turn the sync back to carrying the tracker up. The laptop is the one writer again.

Template and setting must agree after a roll back: a template deploy with `MERGER` `on` would turn the cloud merger back on beside the laptop's, two writers. The template's test holds `on` because that is what runs; a roll back that lasts longer than an afternoon changes the template and the test together.

## What was considered

- **Moving the `.md` appends too.** Rejected for now: laptop lanes edit those files directly, so a cloud appender would be a second writer. They move when the laptop lanes stop editing them in place.
- **Mounting the share on the laptop and keeping the PowerShell merger.** The laptop stays on the path; a sleeping laptop still holds every cloud row.
- **The site reading the share for the card.** It would need the account key or a data role in the site's settings for five numbers the Function can say on its own. Rejected; the card keeps its rule that no file in the share is opened by the site.
- **A key on the health route.** It would put a second secret in the site's settings to protect numbers the site publishes anyway.

## Consequences

- A row reaches the share's tracker within a minute of reaching a spool; on the laptop it shows about three minutes after a lane writes it (sync up, a merger pass, sync down).
- The Function runs one more execution a minute, about 43,200 a month at 512 MB, plus one health read a minute from the site: inside the Flex Consumption free grant the nightly card already measures.
- A redeploy of `infra/jobhunt.bicep` keeps the merger on. A setting added in Azure by hand and not in the template is caught by the test the next time the Function reads it.

## Where it sits

The merger is not in the site's onion: it is the job hunt's Function, `functions/jobhunt`, a project of its own that shares this repository and nothing in the site's image. Its rules (`Merge/RowMerge`, `FixMerge`, `MoveOutMerge`, `MergeContext`, `MergerHealth`) talk only to the `IMergeStore` port; `ShareMergeStore` is the adapter over Azure Files; `Merger.cs` and `MergerWatch.cs` are the timer and the route. On the site's side the reading is the host's, beside the nightly card it feeds: `Cloud/MergerReading.cs` reads the route and keeps the five numbers, `Cloud/NightlySource.cs` reads it with the Function's metrics, and `Cloud/NightlyRows.cs` draws the row. Nothing in Domain or Application knows the merger exists.

## Files

- [`functions/jobhunt/Merger.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt/Merger.cs) - the timer, and the `MERGER` switch
- [`functions/jobhunt/Merge/`](https://github.com/SteveStout/TheYard/tree/main/functions/jobhunt/Merge) - the port, the share adapter, the rows, fixes and moveouts, the merge context and the health reading
- [`functions/jobhunt/MergerWatch.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt/MergerWatch.cs) - the health route
- [`functions/jobhunt.Tests/`](https://github.com/SteveStout/TheYard/tree/main/functions/jobhunt.Tests) - the rules, the parity test, the health reading and the template's settings
- [`infra/jobhunt.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/jobhunt.bicep) - the Function's settings, `MERGER` among them
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml) - the functions job
- [`api/TheYard.Api/Cloud/MergerReading.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Cloud/MergerReading.cs) - the card's read of the route and the five readings
- [`api/TheYard.Api/Cloud/NightlyRows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Cloud/NightlyRows.cs) - the merger row
- [`api/TheYard.Tests/NightlyCardTests.Merger.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/NightlyCardTests.Merger.cs) - the row's tones and the wire check, through the catalogue

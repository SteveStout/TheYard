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

## Addendum, 2026-10-22 (1.0.4.29): the .md appends merge in Azure too

The split in rule 4 lasted three hours. On Steve's word at 11:40 ("move the .md appends into the Azure merger, the same pattern as the tracker rows") the appends moved at 12:32 Central, in the quiet window after the noon records run.

- **The port.** `Merge/TextAppendMerge.cs` is `merge-tracker.ps1`'s `Merge-TextAppends` behind the same `IMergeStore` port, which gained a folder listing: the fragments in `append-inbox/<target>/` (the laptop's, sent up by the sync) and `inbox/append-inbox/<target>/` (the cloud lanes', on the share), the same nine targets, the fragment appended verbatim and stamped `<!-- AID:<stem> -->`, read back, filed under `merged/`, the 24-hour self-heal, nothing deleted. The `Merger` runs it after the rows when `APPENDS` is `on`; the template carries `on` and its test holds it there.
- **Parity.** `TextAppendMergeTests` runs `merge-tracker.ps1 -AppendsOnly` and the port on copies of all nine real targets with a fragment that has no final line break, one with only LF line ends and trailing blank lines, one with a byte order mark and characters past ASCII, one whose stamp is already in its target, an empty one, and one for a folder that is not a target: every target identical, byte for byte.
- **A bug found on the way, fixed in both.** From 11:20 to 11:53 the laptop merger re-appended 332 blocks. The sync brought stale top-level copies of already merged fragments back down from the share; the merger filed each as already present under a name `merged/` already held, which renames it `-dupHHMMSS`; the next pass looked for that new name's stamp, did not find it, and appended the block again. The self-heal now counts the original name's stamp for a `-dup` name, a dot file (`azcopy`'s `.azDownload-*`) is never a fragment, and the sync never brings `append-inbox/` down. The duplicated blocks already appended (standard-answers.md 33, CONVENTIONS.md 11, writing-samples.md 3, failure-log.md 1) are left for a dedupe the owner approves, because removing them deletes text.
- **The cutover** (`staging\mergercode1022\cutover-appends.ps1` in the job-hunt folder): the sync paused; the laptop merger idle (`$AppendsInAzure = $true`); the laptop's nine targets up and proven equal on the share by SHA-256; the 44 stale fragment copies at the top of the share's spools set aside in `pre-cutover/` folders, not deleted; `APPENDS=on`; the new sync in and resumed; one fragment from the laptop spool merged on the share and back in the laptop's `state.md` in four minutes.
- **The laptop copy is a copy.** The sync brings the nine targets down every pass. Text a lane appended to the laptop copy directly is found against the last copy brought down and turned into a fragment (logged `ADOPTED`), so it is merged rather than lost; any other edit is set aside whole and logged `CONFLICT`. A sweep or rotation of a target is a rewrite, not an append, so it is no lane's job until the merger can do it.
- **Roll back**: `APPENDS=off`, both `$AppendsInAzure` switches false, the share's copies down once, the laptop merger appending again (`rollback-appends.ps1`).

## Addendum, 2026-10-22 (1.0.4.30): the merger row is read from the first minute after a roll

Rule 8 said the card reads the route "from the host name the hour reads off the Function's record". That stopped being true at 1.0.4.30. The hour runs once an hour, and a roll starts a new process with no hour read yet, so for about two minutes after every deploy the row read "not read: no merger reading kept in the last hour" (seen on both names at 13:26 Central after 1.0.4.29, cleared by 13:28).

- **The minute reads the host itself.** When `NightlySource.MinutesAsync` finds the Function and no host is known, it reads the Function's record once for `properties.defaultHostName`, the same read and API version the hour uses, and keeps the answer. Every later minute and hour uses the kept host; the hour still refreshes it.
- **A failed read costs one line.** A 401, 403 or 404, a timeout or an answer of the wrong shape leaves the host unknown and the merger reading out of that minute; the Function's metrics on the same minute are kept, and the next minute asks again.
- **Tests** (`NightlyCardTests.Merger.cs`): the first two minutes after a roll, with no hour read, both carry the merger's reading and read the record once between them; a record that answers 403 leaves the minute's metrics read, asks the route nothing, and the row says it is not read.

## Addendum, 2026-10-22 (1.0.4.30): the Sheet mirror, in shadow

The four-hour records run spent most of its turns on a job with one right answer: copy the master rows the tracker's Google Sheet lacks onto the end of the Sheet. On Steve's word at 11:54 ("we can write code that writes to the CSV and Google sheet", "azure is cheaper than AI") that job moves into the same Function, as the first increment of the job hunt's automation plan.

- **The rules are the records run's, as code** (`functions/jobhunt/Sheet/SheetPlan.cs`). Sheet row N+1 holds the master's Nth data row in Count order and column A is the display number 1..N, so the rows to add are the master's rows past the Sheet's last, numbered on from it. Before anything is added the Sheet's numbering must run 1..N without a gap and its last five rows must match the master's Person, Company and Date at the same positions; a Sheet ahead of the master, a gap or a drifted tail adds nothing and says which. A row the tracker gate bans (`TrackerGate.cs`, `check_tracker.py`'s BANNED verdict ported line for line) or whose Communication is not a channel name stops the append at that row, and every later row waits behind it, because the Sheet is in Count order.
- **Append only, as a test.** The grid port (`ISheetGrid`) has two methods, read and append, and a test holds it to those two; another refuses any code under `Sheet/` that could edit, clear or delete a cell or reach Gmail. The adapter (`GoogleSheetGrid.cs`) makes two calls: `spreadsheets.values.get` on A:G and `spreadsheets.values.append` with RAW input and INSERT_ROWS. After an append the mirror reads the grid back and checks the row count, the numbering and every added cell, and logs VERIFIED or MISMATCH.
- **Shadow first.** `SheetMirror` runs every fifteen minutes (7, 22, 37 and 52 past). `SHEET_MIRROR=shadow`, which the template carries, writes the pass's plan to `sheet-mirror/last-plan.tsv` on the share and one line to `sheet-mirror/mirror.log`, and adds nothing, so its plan can be set beside the records run's `sheet-plan.tsv` for at least two runs before the switch moves to `on` and the records run stops writing the Sheet. One writer a file stays the law: the day the mirror is on, the records run's Sheet step goes.
- **Credentials.** The mirror reads the Sheet with the Google client and refresh token the nightly lane already holds (ADR-JOBHUNT-001 in the job-hunt folder) and the Sheet's id, set on the Function out of band as `GMAIL_CLIENT_ID`, `GMAIL_CLIENT_SECRET`, `GMAIL_REFRESH_TOKEN` and `TRACKER_SHEET_ID`; none is in this repository, and the Sheet's id is left out because the Sheet holds the job hunt's private rows. Until they are set the timer logs that it is blocked and does nothing.
- **Roll back:** `SHEET_MIRROR=off`.

## Addendum, 2026-10-22 (1.0.4.30): the ATS sweep, in shadow

The second increment of the same plan: the records run's ATS sweep (its step 3), the job that costs most of its turns after the Sheet, as code in the same Function (`functions/jobhunt/Ats/`).

- **What a message is** (`AtsRules.cs`). Mail still in the inbox from a known tracking system's domain (Greenhouse, Lever, Ashby, Workday, iCIMS, Jobvite, SmartRecruiters, Taleo, SuccessFactors, BambooHR, Workable) is read as a receipt, a generic rejection, a rejection that gives a reason (a location, a cap, sponsorship), or unknown. A receipt that asks for anything (an assessment, availability, a reply) is unknown.
- **What the sweep does with it.** A person anywhere in the thread keeps it in the inbox. Mail from before 2026 is archived with no row. The company is the one company with an application on the master named in the sender, the subject or the text; none, or two, keeps it in the inbox. A receipt rests on an application row within two days, else gets a row; a generic rejection rests on a rejection row within two days, else gets a row naming the applications on file; either is then archived. A rejection with a reason gets its row and stays for Steve. A message a row already names by its Gmail id rests on that row. Rows go to `inbox/tracker-inbox/` for the merger, never into the tracker directly.
- **The red lines as tests.** The inbox port has four methods (find, read, a thread's senders, archive), and a test refuses any code under `Ats/` that could send, draft, trash, delete or add a label. Archive removes INBOX and reads the labels back.
- **Shadow first.** `AtsSweep` runs at 50 past the hour before each records run, so both read the same inbox. `ATS_SWEEP=shadow`, which the template carries, writes `ats-sweep/last-plan.tsv` and a line to `ats-sweep/sweep.log` on the share and archives nothing; after two runs agree with the records run's DONE table, `on` turns the records run's step 3 off. Roll back: `ATS_SWEEP=off`.

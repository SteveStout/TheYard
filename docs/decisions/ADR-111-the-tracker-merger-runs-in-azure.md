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

The merger is not in the site's onion: it is the job hunt's Function, `functions/jobhunt`, a project of its own that shares this repository and nothing in the site's image. Its rules (`Merge/RowMerge`, `FixMerge`, `MoveOutMerge`, `MergeContext`, `MergerHealth`) talk only to the `IMergeStore` port; `ShareMergeStore` is the adapter over Azure Files; `Merger.cs` and `MergerWatch.cs` are the timer and the route. On the site's side the reading is the host's, beside the nightly card it feeds: `Features/Admin/Cloud/MergerReading.cs` reads the route and keeps the five numbers, `Features/Admin/Cloud/NightlySource.cs` reads it with the Function's metrics, and `Features/Admin/Cloud/NightlyRows.cs` draws the row. Nothing in Domain or Application knows the merger exists.

## Files

- [`functions/jobhunt/Merger.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt/Merger.cs) - the timer, and the `MERGER` switch
- [`functions/JobHunt.Application/Features/Merge/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Application/Features/Merge) and [`functions/JobHunt.Infrastructure/Features/Merge/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Infrastructure/Features/Merge) - the port, the share adapter, the rows, fixes and moveouts, the merge context and the health reading
- [`functions/jobhunt/MergerWatch.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt/MergerWatch.cs) - the health route
- [`functions/jobhunt.Tests/`](https://github.com/SteveStout/TheYard/tree/main/functions/jobhunt.Tests) - the rules, the parity test, the health reading and the template's settings
- [`infra/jobhunt.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/jobhunt.bicep) - the Function's settings, `MERGER` among them
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml) - the functions job
- [`api/TheYard.Api/Features/Admin/Cloud/MergerReading.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/MergerReading.cs) - the card's read of the route and the five readings
- [`api/TheYard.Api/Features/Admin/Cloud/NightlyRows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/NightlyRows.cs) - the merger row
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

- **The rules are the records run's, as code** (`functions/JobHunt.Domain/Features/Sheet/SheetPlan.cs`). Sheet row N+1 holds the master's Nth data row in Count order and column A is the display number 1..N, so the rows to add are the master's rows past the Sheet's last, numbered on from it. Before anything is added the Sheet's numbering must run 1..N without a gap and its last five rows must match the master's Person, Company and Date at the same positions; a Sheet ahead of the master, a gap or a drifted tail adds nothing and says which. A row the tracker gate bans (`TrackerGate.cs`, `check_tracker.py`'s BANNED verdict ported line for line) or whose Communication is not a channel name stops the append at that row, and every later row waits behind it, because the Sheet is in Count order.
- **Append only, as a test.** The grid port (`ISheetGrid`) has two methods, read and append, and a test holds it to those two; another refuses any code under `Sheet/` that could edit, clear or delete a cell or reach Gmail. The adapter (`GoogleSheetGrid.cs`) makes two calls: `spreadsheets.values.get` on A:G and `spreadsheets.values.append` with RAW input and INSERT_ROWS. After an append the mirror reads the grid back and checks the row count, the numbering and every added cell, and logs VERIFIED or MISMATCH.
- **Shadow first.** `SheetMirror` runs every fifteen minutes (7, 22, 37 and 52 past). `SHEET_MIRROR=shadow`, which the template carries, writes the pass's plan to `sheet-mirror/last-plan.tsv` on the share and one line to `sheet-mirror/mirror.log`, and adds nothing, so its plan can be set beside the records run's `sheet-plan.tsv` for at least two runs before the switch moves to `on` and the records run stops writing the Sheet. One writer a file stays the law: the day the mirror is on, the records run's Sheet step goes.
- **Credentials.** The mirror reads the Sheet with the Google client and refresh token the nightly lane already holds (ADR-JOBHUNT-001 in the job-hunt folder) and the Sheet's id, set on the Function out of band as `GMAIL_CLIENT_ID`, `GMAIL_CLIENT_SECRET`, `GMAIL_REFRESH_TOKEN` and `TRACKER_SHEET_ID`; none is in this repository, and the Sheet's id is left out because the Sheet holds the job hunt's private rows. Until they are set the timer logs that it is blocked and does nothing.
- **Roll back:** `SHEET_MIRROR=off`.

## Addendum, 2026-10-22 (1.0.4.30): the ATS sweep, in shadow

The second increment of the same plan: the records run's ATS sweep (its step 3), the job that costs most of its turns after the Sheet, as code in the same Function (`functions/JobHunt.Application/Features/Ats/` since 1.0.4.44).

- **What a message is** (`AtsRules.cs`). Mail still in the inbox from a known tracking system's domain (Greenhouse, Lever, Ashby, Workday, iCIMS, Jobvite, SmartRecruiters, Taleo, SuccessFactors, BambooHR, Workable) is read as a receipt, a generic rejection, a rejection that gives a reason (a location, a cap, sponsorship), or unknown. A receipt that asks for anything (an assessment, availability, a reply) is unknown.
- **What the sweep does with it.** A person anywhere in the thread keeps it in the inbox. Mail from before 2026 is archived with no row. The company is the one company with an application on the master named in the sender, the subject or the text; none, or two, keeps it in the inbox. A receipt rests on an application row within two days, else gets a row; a generic rejection rests on a rejection row within two days, else gets a row naming the applications on file; either is then archived. A rejection with a reason gets its row and stays for Steve. A message a row already names by its Gmail id rests on that row. Rows go to `inbox/tracker-inbox/` for the merger, never into the tracker directly.
- **The red lines as tests.** The inbox port has four methods (find, read, a thread's senders, archive), and a test refuses any code under `Ats/` that could send, draft, trash, delete or add a label. Archive removes INBOX and reads the labels back.
- **Shadow first.** `AtsSweep` runs at 50 past the hour before each records run, so both read the same inbox. `ATS_SWEEP=shadow`, which the template carries, writes `ats-sweep/last-plan.tsv` and a line to `ats-sweep/sweep.log` on the share and archives nothing; after two runs agree with the records run's DONE table, `on` turns the records run's step 3 off. Roll back: `ATS_SWEEP=off`.

## Addendum, 2026-10-22 (1.0.4.31): the daily card

The third increment: one page Steve reads, `nightly/CARD.md` on the share, written by the same Function with no model (`functions/JobHunt.Domain/Features/Card/` and `functions/JobHunt.Infrastructure/Features/Card/` since 1.0.4.44). It says what waits on him (the newest APPLY-BLOCKERS file's numbered items, a Sheet held behind a row, rejections that give a reason left in the inbox), the next seven days (each interview or call on his calendar with how he joins it, read with calendar.readonly once the token holds that scope; a call with no join link and no number starts with STOP and is listed under what waits on him too; until the scope is granted the line says it is not read), the pipeline (the tracker's rows and last Count, the merger's and the appends' newest log lines with their age, rejected spool files, the Sheet mirror's and the ATS sweep's last passes) and TheYard (the newest CI run on main and both names' live version). Every line names where it was read, and a read that fails says so in words. Under what waits on him the card also names every company at RULE 50's cap (three applications in a rolling 60 days) or on the do-not-apply list, with the day the next application may go in: `Leads/ApplicationCap.cs` is that rule as code, the do-not-apply table read out of `systemRules.md` and a company's variant spellings counted as the company, and it is the one cap check the leads filter will use too. `DailyCard` runs at 5 past each hour and rewrites the card only when what it says changed, and at 6 AM Central every day. Roll back: `DAILY_CARD=off`.

## Addendum, 2026-10-22 (1.0.4.31): the leads harvest, in shadow

The fourth increment, started: the LinkedIn job alerts still in the inbox, each requisition read out of the alert's text (`Leads/LinkedInAlert.cs`: the title, the company and the location above "View job:", the link cut to its job id so no tracking reaches a file), and judged by `Leads/LeadsFilter.cs` before any model reads it: remote only, not already applied to (the company with the title in an application row's note), and clear of RULE 50 through the same `ApplicationCap` the card uses. It runs in the ATS sweep's slot with `LEADS=shadow`, writes `leads-shadow/last-pass.tsv` and one line to `leads-shadow/leads.log` on the share, and archives nothing, so its list can be set beside the records run's `leads-<date>.md`. The other boards' alerts and the posting age from the Greenhouse, Lever and Ashby APIs come next, each in shadow first. Roll back: `LEADS=off`.

## Addendum, 2026-10-22 (1.0.4.34): processed means archived

Steve set the rule at 18:26 Central: once a lane or a run processes a mail, it is archived in the same pass. The ATS sweep followed the records run's older rule and left a rejection that gives a reason in the inbox for him. It now logs that rejection with the reason in the row's notes and archives it, and archives it with no new row when a rejection row already holds it (`AtsRules.Decide`, held by `AtsSweepTests`). The row, not the inbox, is where he reads the reason. The daily card's "left in the inbox" line went with it, and its RULE 50 line now names how many companies are held and the five that open soonest, not every one. Mail that asks something of Steve, and any thread with a person in it, still stays in the inbox. The first parity read against a records run (17:45) found one more: an interview or scheduling mail a row already names by its Gmail id was planned for the archive, where the run leaves it; it now stays.

The leads harvest's first live pass, at 19:50, read 13 LinkedIn alerts and no job: the body it was handed was the one the ATS rules read, every line joined into one, so an alert had no blocks to split. A message now also carries its plain part with the lines kept (`InboxMessage.Lines`), the harvest reads that, and an alert's own header line, which carries a link mid-line, no longer reads as the first job (`AtsSweepTests` holds both on a multipart alert shaped like the real one).

## Addendum, 2026-10-23 (1.0.4.35): every function's telemetry in Application Insights

Until this addendum the Function app had no Application Insights: a timer that threw or a pass that stalled showed only in the share's own logs. `appi-jobhunt-ss` (RG-JOBHUNT-SS, workspace-based on TheYard's `log-theyard-ss`, so one workspace holds both) now receives every function's invocations, failures and host logs, from the one app setting `APPLICATIONINSIGHTS_CONNECTION_STRING` that the template carries from the component's own connection string. No code reads it; the Functions host does. `TemplateTests` holds the setting and the component in the template, so a redeploy cannot cut the app off.

## Addendum, 2026-10-23 (1.0.4.35): the 30-minute inbox pass, acting

Steve's alpha goal for 2026-10-23: a pass every 30 minutes, so he never sees a job-board alert or a rejection again, and every human message, interview, scheduling and account mail stays in the inbox. Until this addendum the ATS sweep and the leads harvest ran in shadow once before each records run, and the records run left same-day alerts in the inbox. `AtsSweep` now runs at 20 and 50 past every hour as the inbox pass, three parts in order, each logged and each allowed to fail without stopping the next:

- **The ATS sweep, on.** The same rules as in shadow (`AtsRules.Decide`); its plans since 1.0.4.34 matched the records runs and archived nothing a run left. The row goes to the spool before the archive.
- **The leads harvest, on.** Harvest before archive: the requisitions of every LinkedIn alert that read whole are appended to `nightly/leads-<date>.md` (the records run's file, never rewritten) with the filter's verdict beside each, and only then is the alert archived and its labels read back. An alert with a block it could not read, with no job in it, or with anybody but the alert sender in its thread stays in the inbox.
- **The model, only for what code could not judge.** With `TRIAGE=on` the pass lists the inbox's mail from the last two days that `triage/seen.tsv` does not name and, when there is any, fires `triage` at nightly.yml: Sonnet at low effort, 40 turns, a 10-minute step, reading `nightly/TRIAGE-LANE.md` on the share. It harvests the other boards' alerts, logs the rejections and receipts, archives them, leaves everything else, and writes one line a judged message to `triage/seen.tsv`, so nothing is judged twice and the model is never started for nothing. It shares the records run's concurrency group, so the two never act on the inbox at once.

One line a pass goes to `inbox-pass/pass.log` with its wall time. The template carries `ATS_SWEEP=on`, `LEADS=on` and `TRIAGE=on`; `InboxPassTests` holds the schedule, the harvest-before-archive order and what is left, and the triage step's seen list. Roll back: any of the three set to `shadow` or `off`, each on its own.

## Addendum, 2026-10-23 (1.0.4.37): the triage floor is the inbox, and a refused token is named

Two gaps the first day of the inbox pass showed. The 09:20 and 09:50 passes each wrote `FAILED: HttpRequestException: ... 400 (Bad Request)` three times and fired no triage: Steve's password change at 08:12 revoked the Gmail refresh token, and every Gmail call of the pass answered 400 until the consent click at 09:57 (read from `inbox-pass/pass.log` and state.md). The token trade now throws `GoogleTokenRefusedException`, the pass writes one `BLOCKED:` line naming the click that restores it, and the parts after it are skipped instead of asking Google again. And the triage window was the last two days, so an alert older than that (the Ladders alert of 10/19) was never judged by any run; the pass now hands the model whatever is still in the inbox that `triage/seen.tsv` does not name, a hundred a pass, and `nightly/triage_gmail.py` lists the same. `InboxPassTests` holds both.

## Addendum, 2026-10-23 (1.0.4.40): every board's alert is harvested, and the leads list has its own run

The inbox pass harvested LinkedIn's digest only, so the other boards' alerts waited for the triage run, which judges by model what code can read. Read in the inbox on 23 October: a SuccessFactors agent (Olympus, `olympuscorp1-jobnotification@noreply.jobs2web.com`, HTML only), two Ladders alerts (`jobs@my.theladders.com`), and two LinkedIn nudges from `jobs-noreply@linkedin.com` ("CSI Companies is hiring for a API role", "apply now to 'Senior Staff Software Engineer at ServiceTitan'"). From 1.0.4.40 the harvest asks every reader in `BoardAlerts.All`: LinkedIn's digest as before, LinkedIn's nudges by their two subjects and the first job link, Ladders by "title, band, company, place" once the markup is gone (a company named in two parts keeps both; the link written is a Ladders search for the title and company, because Ladders' own links are tracking redirects with no job id), and a SuccessFactors agent by its job links in the HTML part, cut at the query string. The same sender's "Your application was viewed" and "application was sent" mails are not alerts and are left for the sweep and triage. Each reader names its own sender, so a thread with anybody else in it stays. `LeadsFilter` adds the band floor: a printed band that tops under $155K fails with the figure named; no band printed passes, as before.

The list itself: `LEADS_LIST` fires nightly.yml's new `leads` mode (Sonnet, low effort, 40 turns, 10 minutes, `nightly/LEADS-LANE.md` on the share) when a pass harvested a lead that passed the filter and that `leads/scored.tsv` does not hold. That run scores fit against targeting-criteria.md and candidate-profile.md and rewrites `nightly/LEADS.md` ranked best first; a lead drops off when a tracker row names it applied, rejected or skipped. The template carries `LEADS_LIST=shadow`: the pass's line says what it would fire for two passes, then the setting moves to `on`. `BoardAlertsTests` holds each reader on the shape the board sent; `TemplateTests` holds the setting and its gate. Roll back: `LEADS_LIST` to anything but `on`.

## Addendum, 2026-10-23 (1.0.4.42): human mail never waits for 4 AM, and a dead Function emails me

A person who writes in the morning waited for the 4 AM reply run. From 1.0.4.42 the inbox pass asks `HumanMail` whether a person, not Steve, not a machine and not a relay that speaks for one (an InMail notice, a digest, an alert, a newsletter), has written in the last two days to a thread that holds no draft. When one has, the pass fires the reply run (the nightly mode, Opus, drafts only) at most once every four hours, between 7 AM and 9 PM Central, and keeps the time of the fire in `inbox-pass/reply-fired.txt`. Nothing sends: every email still waits for Steve's word. `REPLY_ON_HUMAN` carries it, `shadow` in the template first, so the pass's line says who is waiting and what it would fire. `HumanMailTests` holds who is a person on the senders in the inbox on 23 October and the hours, the four hours and the draft rule.

A Function that stops told nobody. `infra/jobhunt-alerts.bicep` is its own template, so its deploy touches no app setting: one action group that emails me (the address passed at deploy time, never written in the repository) and one log alert over `appi-jobhunt-ss`, every ten minutes, that fires on any failed invocation in thirty minutes or no `Merger` invocation in ten. `TemplateTests` holds the parameter, the windows and the query.

The card's next seven days needed no change: the token Steve restored at 09:57 on 23 October was issued by `get_google_token_j.py`, which asks `calendar.readonly`, and CARD.md at 14:05 listed Tuesday's recruiter screen from Google Calendar.

## Addendum, 2026-10-24 (1.0.4.50): an answered thread does not fire, a full harvest asks no more boards, and the share is billed less

Three fixes to the inbox pass, the first a condition for REPLY_ON_HUMAN leaving shadow. A thread whose newest message, not counting a draft, is mine is answered: after I reply, the person's mail stays in the inbox for two days, and HumanMail read the thread as waiting because it held no draft, so it would have fired the reply run every four hours until the mail aged out. The inbox port reads a thread's newest sender (`ThreadNewestSenderAsync`, Gmail lists a thread oldest first), and HumanMailTests holds the answered case. LeadsHarvest stops asking boards once its cap of 150 is met, where it asked each later board for zero, which Gmail answers with its default page or a 400.

The share the Function writes to bills every transaction. From 1.0.4.49 the merger remembers for an hour each folder it has made sure of, which took about 2,200 creates an hour that found the folder already there off the bill. From this version the share is Transaction optimized rather than Hot (infra/jobhunt.bicep), an alert emails when the share passes 20,000 transactions in an hour, about four times a quiet hour (infra/jobhunt-alerts.bicep), and a $50 monthly budget on the subscription emails at 80 % and 100 % of the spend and at 100 % of the forecast (infra/jobhunt-budget.bicep, subscription scope, the address passed at deploy time). TemplateTests holds the tier, the alert and the budget.


## Addendum, 2026-10-25 (1.0.4.71): the Function and its CI job on .NET 10

Point 7 above installed the .NET 9 runtime beside the SDK, because the Function ran on .NET 9. From 1.0.4.71 the Function targets .NET 10 and its Flex Consumption app runs dotnet-isolated 10.0 (ADR-089, the addendum of that version), so the CI job installs .NET 10 alone, the same as every other job.

## Addendum, 2026-10-25 (1.0.4.73): the tracker gets a SQL copy, and the CSV stays the master

Job_Hunt_Tracker.csv has twelve readers and one writer, this Merger. PROMPT-JOBHUNT-TRACKER-SQL moves it into the job hunt's schema one reader at a time, and this version is its first rung: the copy and the proof that the copy is the file.

- **The tables.** `jobhunt.Tracker` holds one row a record: the seven cells as the file holds them (no date parsing, no trimming, no length limit), the record's text with its quotes and inner line breaks, and the record's SHA-256. `jobhunt.TrackerHeader` holds the header record, which carries byte order marks inside its quoted Count cell, whether the file opens with a mark and ends with a break, the row count and the whole file's hash. Both live in the site's database beside the campaign (ADR-118, the addendum of 1.0.4.72).
- **The cut.** `TrackerFile` cuts the file at every CRLF outside quotes, so a line break inside a field, CRLF or LF alone, stays in its record, and puts the records back byte for byte. Measured on the live file the morning this shipped, by csv.reader and the same cut: 1,816,499 bytes, 3,006 records (the header and 3,005 rows), the last Count 3135, every record one row of seven cells, and the file put back identical.
- **The import.** `TRACKER_SQL=import` makes the Merger copy the file once, after its pass, when the copy is empty, then rebuild the file from the copy and compare it with the CSV byte for byte. Its run line carries the rows, the rows written and the parity. Nothing writes the CSV but the merge it already did.

`TrackerSqlTests` holds the cut and the rebuild over a checked-in sample of the real file's shapes with the names replaced, the mirror writing only changed rows, and the copy following a row taken off the end. `.gitattributes` keeps the sample's CRLF breaks as they are.

## Addendum, 2026-10-25 (1.0.4.74): both, every pass

The second rung of the tracker's move. With `TRACKER_SQL=on` the Merger copies the tracker into SQL after every pass that changed it, the CSV first and the copy second. A pass whose file hash the copy already holds writes nothing, and a copy that fails is logged on the run line (`tracker_sql_failed`) for the next pass to retry, because the CSV stays the master. The first pass of each Central day rebuilds the file from the copy and compares it with the CSV byte for byte; the run line carries `tracker_sql_parity` (1 when the two are the same), so the nightly card's Merger section shows it. Seven straight days of parity come before any reader moves to the copy (the prompt's rung 3).

## Addendum, 2026-10-26 (1.0.4.91): the copy's counts are drawn on the card

The addendum above says the nightly card's Merger section shows `tracker_sql_parity`. It did not. The Merger's sentence on the card was built from `merged` alone, its history listed only the passes that merged something, and the health route's switch list had no `TRACKER_SQL`, so a parity check that found the copy different, or a copy that kept failing, would have been in the run line and nowhere a person looks. The copy itself was in step when this was found: the SQL frame's hash equalled the CSV's SHA-256, 3,005 rows.

From this version the card draws what this record said it did:

- **The sentence** names the copy beside the merge: "the SQL copy in step" or "N rows copied" (`tracker_sql_written`), "parity same" or "PARITY DIFFERENT" (`tracker_sql_parity`, the first pass of a Central day), and "copy failed, retried next pass" (`tracker_sql_failed`).
- **The history** lists a pass that copied rows, checked parity or failed its copy, beside the ones that merged something; a quiet minute is still counted and not listed.
- **The section turns amber** when the newest parity check in the window found the copy different, or when the newest pass failed its copy. Every other tone is still its first switch's.
- **The health route names `TRACKER_SQL`** among the switches, and the Merger section shows it as a chip beside `MERGER` and `APPENDS`.

`ProcessRunsTests` holds the words and the tone, `ProcessReadersTests` the listing rule and the parity row, and `RunLineTests` the switch name.

## Addendum, 2026-10-26 (1.0.4.92): the copy reads the tracker only when it moved

The copy pass read the whole tracker from the share every minute (1.8 MB on 25 October) and hashed it, to learn that the copy already held it nearly every time. The share bills that as one read operation a minute, inside its free line, so this is not about the bill: it is the host's time and the trace volume of fourteen hundred needless reads a day.

From this version the pass first asks the share for the tracker's size and last-write time, one properties request that moves no content. When both equal what the last pass saw, and the copy's frame still holds the hash that pass read, the pass writes nothing and does not read the file. Any write to the tracker, the Merger's own included, moves its last-write time, so the next pass reads it whole. What the last pass saw lives in the Function's memory only, so the first pass after a restart reads the file as before. The daily parity check is unchanged and still reads the whole file.

`TrackerSqlTests.An_unmoved_tracker_is_not_read_again` holds it.

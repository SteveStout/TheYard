# ADR: The Nightly Function on the Admin tab

Status: accepted, 2026-10-20. The Azure Function that starts the nightly workflow gets the seventh resource card under Cloud infrastructure, `nightly`, named The Nightly Function: whether its slots fired on time, what its runs on GitHub came to, what it costs line by line, and what it is allowed to do. It is one more definition in the one frame (ADR: One frame for every resource), not a recorder, keep or route of its own, and it never reads anything from the file share the lane works on.

## In plain words

A job that runs at four in the morning fails where nobody is looking. The Function wakes every hour, decides whether the hour is one of its six slots in Central time, and asks GitHub to start a run; the run does the work. Until now, finding out whether last night happened meant opening the portal, then GitHub, then the bill. This card puts the three on one page in the same shape as every other resource: two dials, a chart, four questions and a readout.

What that is worth: the dev side and the infrastructure side of a scheduled job looked at in one place, the same way the site's own resources are.

## Context

The Function `func-jobhunt-ss` runs on Flex Consumption in its own resource group, `RG-JOBHUNT-SS`, with the plan `plan-jobhunt-ss` and the storage account `stjobhuntss` (`infra/jobhunt.bicep`). Flex Consumption on Linux does not honour a time zone setting (learn.microsoft.com/azure/azure-functions/functions-bindings-timer, read 2026-10-20), so its timer fires every hour on the hour in UTC and `CentralClock` decides what the hour is: 4:00 AM Central is the reply lane, and midnight, 8 AM, noon, 4 PM and 8 PM are the records run. Each fires `repository_dispatch` at this repository, which runs `.github/workflows/nightly.yml`.

Three sources answer the card's questions. Azure Monitor counts the Function's on-demand executions and execution units on `Microsoft.Web/sites` as `OnDemandFunctionExecutionCount` and `OnDemandFunctionExecutionUnits`, the latter in megabyte-milliseconds, which is what the free grant is counted in; a gigabyte-second is 1,024,000 of them (learn.microsoft.com/azure/azure-functions/functions-consumption-costs, read 2026-10-20). GitHub's public API lists the workflow's runs without a token. The bill names the Function, its plan and its storage account as three lines.

## Decision

**One slot table, compiled twice.** The slots live in `TheYard.Domain/NightlySlots.cs`: the slot an hour is, the slots due in a window, the next one, and how a slot is judged from five-minute firing counts (on time when a bucket in its first five minutes counted one, late when the first that did is later in the hour, missed otherwise). The Function compiles the same file through a link in its project, and its `CentralClock` asks the table rather than keeping hours of its own, so the card's idea of a slot can never be the Function's minus a daylight-saving change. `NightlySlotsTests` holds the link, the table on both daylight-saving days, and the judging.

```live path=api/TheYard.Domain/NightlySlots.cs region=nightly-slots
```

**The two dials are the two that run out.** Runs on time over thirty days, a floor: amber under 95 per cent and red under 90, read on the misses the way the edge's cache dial is. And the month's execution units against the Flex Consumption free grant of 100,000 GB-s a month per subscription (with 250,000 executions), amber at 80 per cent and red at 95 (Azure Functions pricing page, read 2026-10-20). The grant is typed with its source on the constant, because no record Azure returns states it.

**A resource group of its own, a directory of its own.** The card finds the Function by type and the configured name in a second `ResourceDirectory` over the Function's group, built beside the first in the module. The names come from configuration (`Nightly` in appsettings.json, `Shared/NightlyResources.cs`), and `LiteralScanTests` now refuses the Function's suffix in the code too.

**No role yet, and the card says so.** The site's identity holds no role on `RG-JOBHUNT-SS`. Until it does, Resource Manager and Monitor answer 403, and the card treats that as an answer: the hour writes `role = missing`, the minute reads nothing, and every line that needs Azure reads "not read: no role on RG-JOBHUNT-SS", the group named from configuration. The run log is GitHub's and still reads. The step is Steve's: Monitoring Reader and Reader for the site's identity on the group, which lets the card read the metrics, the record and the Activity Log and nothing on the share.

**The runs are kept a year, one document each.** The hour reads the last thirty days of runs, and upserts each into the `nightlyruns` container under GitHub's own run id, so a run read on thirty hours is one document; it also writes one document per judged slot when Monitor has the counts. The container is partitioned on the day and keeps a year. The card's view is synchronous, so the hour reads the month back into a small in-process cache the view draws from, newest first. `.github/workflows/nightly.yml` gained a `run-name`, so every run's title names its lane and whether it was a dry run; a run from before it reads "not named" rather than a guess.

**What it is allowed to do, in words.** One role on one share (Storage File Data SMB Share Contributor, proven by `functions/jobhunt/role-check.ps1`), and a token that may send `repository_dispatch` to this one repository. The card says so, and says that nothing from the share is read here. How many share snapshots are kept is not read either: that needs a data role on the storage account, and the account key never goes in the site's settings; the readout prints the Function's own limit, fourteen, held to `ShareSnapshot.Keep` by a test that reads the file.

**Three lines of the bill.** `ResourceCard` takes an optional list of bill names and `ResourceCardInputs` an optional dictionary of their month to date, which `CloudReader` fills from the same cost function; every other card passes neither and is unchanged.

## Consequences

- The rail's Cloud infrastructure group ends with The Nightly Function, after Memory against traffic, with its tone dot read before it is opened.
- The card reads GitHub once an hour, one of the sixty unsigned reads GitHub allows an address an hour.
- Two containers on the existing account, inside the free tier: `nightly` for the minutes and hours like every card, and `nightlyruns` for the year of runs. Both are a person's `az` command from their definitions, as every container is.
- The chart shades every Monday and Friday in Central days, the two days the schedule keeps free, through the chart's existing bands.
- Until the role is granted, the dials read "not read" and the card's tone is unread; that is the truth, not a fault.

## Where it sits

Domain gains the slot table, its first rule shared with a project outside the six, which is why it reads no clock and has no I/O. Data gains the two run records. Application gains the run log's port and its memory adapter beside the other memory keeps, Infrastructure.Cosmos its document adapter, and the host the card's view, rows, reader, GitHub reader and cache under `Cloud/`, wired in the cloud slice's module. Single responsibility: the slots are one file both readers compile. What would change it: a second Function, at which point the names become a list and the card a pair.

## Files

- [`api/TheYard.Domain/NightlySlots.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/NightlySlots.cs) - the slot table, compiled into the Function too
- [`functions/jobhunt/CentralClock.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt/CentralClock.cs) and [`functions/jobhunt/JobHunt.Functions.csproj`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt/JobHunt.Functions.csproj) - the clock that asks it, and the link
- [`api/TheYard.Api/Cloud/NightlyCard.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Cloud/NightlyCard.cs) and [`NightlyRows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Cloud/NightlyRows.cs) - the view
- [`api/TheYard.Api/Cloud/NightlySource.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Cloud/NightlySource.cs), [`GitHubRuns.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Cloud/GitHubRuns.cs), [`NightlyMetrics.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Cloud/NightlyMetrics.cs) and [`NightlyRunCache.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Cloud/NightlyRunCache.cs) - what it reads
- [`api/TheYard.Api/Shared/NightlyResources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/NightlyResources.cs) - the names, from configuration
- [`api/TheYard.Application/NightlyRunLog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/NightlyRunLog.cs) and [`api/TheYard.Infrastructure.Cosmos/CosmosNightlyRunLog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosNightlyRunLog.cs) - the run log
- [`infra/cosmos/nightly.json`](https://github.com/SteveStout/TheYard/blob/main/infra/cosmos/nightly.json) and [`infra/cosmos/nightlyruns.json`](https://github.com/SteveStout/TheYard/blob/main/infra/cosmos/nightlyruns.json) - the two containers
- [`.github/workflows/nightly.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/nightly.yml) - the run name
- [`api/TheYard.Tests/NightlySlotsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/NightlySlotsTests.cs) and [`api/TheYard.Tests/NightlyCardTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/NightlyCardTests.cs) - the table and the card, held

## Addendum, 2026-10-21 (1.0.4.10): GitHub first, the log a durable copy

1.0.4.9 shipped the card against a run log that was always there in its tests and never there on the live site. Neither container exists yet (`nightly`, `nightlyruns`), and two things followed. The one recorder returned before any card's hour when the card's own keep was missing, so the GitHub read never ran; and even when it ran, the card's cache was filled only by reading the log back, which needs `nightlyruns`. Read live on both names on the morning of 21 October, the card said "no runs read yet" and "nightly off · bookkeeping off" while `nightly.yml` listed ten runs, that morning's 04:00 reply run among them. The sentence above, "the run log is GitHub's and still reads", was not true in code.

From 1.0.4.10:

- **GitHub is the source of the card, the log its durable copy.** The hour fills the cache from GitHub's answer whatever the log says. When `nightlyruns` is there, the runs are also kept in it and the older runs it holds are folded in beside GitHub's, which is what lets the card show a run GitHub no longer lists. When GitHub refuses, the cache is read back from the log if there is one, and the hour says "not read: GitHub answered" and the status.
- **An hourly card is read with no keep.** `CloudRecorder` still reads an hourly card's hour once in its hour when the card's keep is missing, holds it in memory (`LiveHour`), and appends nothing; the minutes are not read at all, since nothing could keep them. `CloudReader` draws such a card from that held hour and asks the recorder once the held hour is older than the card's staleness. The card's note says so: "the nightly container is not there yet; the card reads live until it is."
- **Empty is unread, not off.** The Gates line says "on" or "off" only of a list that was read; with no runs it reads "not read yet" and why.
- **Every "not read" says why**: no hour kept yet, the keep's own reason, no role on the group, or GitHub's answer. `ResourceView.LiveRead` says why no minute is kept on every card.
- **An optional token.** `Nightly:GitHubToken`, when set as a web app setting, is sent as a bearer token, so the read draws on its own allowance rather than the sixty unsigned reads an hour App Service's shared outbound addresses split. None is kept in the repository, and with none set the read is unsigned as before.
- **Words.** The readout is "The nightly job", the slots are "the reply run" and "the records run", and the run table's column is "Run"; "lane" is no word of the site's.

Before the two containers exist the card shows GitHub's runs and gates, the hour's role sentence, and "not read" with its reason on every Azure line; it keeps no history and its chart is empty. Creating them is still a person's `az` command from their definitions. `NightlyCardTests` holds all of it: a run log that is not there beside three listed runs, the gates on an empty list, the reasons, and the recorder with an hourly card and no keep.

## Addendum, 2026-10-21 (1.0.4.12): the hour without a keep, for a minute card too

1.0.4.10 shipped the rule above for an hourly card, and the nightly card is read by the minute, so on the live site the recorder still returned before its hour: read on both names at 14:41Z, about eight minutes after the roll, Gates said "not read yet: no hour kept yet" and the run log was empty. From 1.0.4.12 any card whose keep is missing has its hour read once an hour and held in memory, minute card or hourly, and the reader asks for it once the held hour is past the hour. `NightlyCardTests` runs the recorder case with both shapes. The lesson is in the job-hunt failure log: a test of a card's wiring builds the card the way the catalogue builds it.


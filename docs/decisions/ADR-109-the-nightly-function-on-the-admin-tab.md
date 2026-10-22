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
- `infra/cosmos/nightly.json` and `infra/cosmos/nightlyruns.json` - the two containers, never created and removed by the addendum of 1.0.4.25: the documents live in the plan container ([`infra/cosmos/plan.json`](https://github.com/SteveStout/TheYard/blob/main/infra/cosmos/plan.json))
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


## Addendum, 2026-10-21 (1.0.4.15): the run reader, and the Function's clock held by running it

`NightlySource` read Azure Monitor, Resource Manager, the Activity Log and GitHub, wrote the run log and filled the cache, which is why the GitHub path could not be tested apart from Azure and why 1.0.4.9 shipped it broken. From 1.0.4.15 the runs are `NightlyRunReader`'s alone (`Cloud/NightlyRunReader.cs`: GitHub's answer into the cache, the log as the durable copy, the log's older runs folded in, the log read back when GitHub refuses), and `NightlySource` keeps the Azure reads and asks the reader first each hour. `NightlyCardTests` exercises the reader with a GitHub answer and a log and nothing of Azure, the bearer token among it.

The test that held `CentralClock` and the table together read the Function's two files as text. From 1.0.4.15 the test project compiles `functions/jobhunt/CentralClock.cs` by a link, the way the Function compiles `NightlySlots.cs`; the clock needs Domain and nothing of the Functions SDK, so no second test project was needed. `NightlySlotsTests` calls the Function's clock and the table for every hour of both daylight-saving days of 2026 and an ordinary day and asserts the same slot, the same 4 AM and the same Central hour, one 4 AM a UTC day. The Function's own project is unchanged, so nothing rolls to Azure for it.

## Addendum, 2026-10-21 (1.0.4.18): what the group listed, when the Function is not in it

Read live on 1.0.4.14 and after, the card's Last fired said "not read: no Microsoft.Web/sites named func-jobhunt-ss in RG-JOBHUNT-SS". The group's listing answered without refusing, so the role is not plainly missing, and the sentence could not say whether the name was wrong or the listing showed the site nothing. From 1.0.4.18 `NightlySource.Unfound` says which: "RG-JOBHUNT-SS lists nothing to the site's identity" when the listing is empty, or the count of resources and the names of the web and Function apps it did list. `NightlyCardTests.A_function_not_found_says_what_the_group_listed` holds the three sentences.

## Addendum, 2026-10-21 (1.0.4.22): a GitHub hiccup is the run log's alone, and an empty group is resolved

Two things the review of 1.0.4.11 to 1.0.4.18 found. `NightlyRunReader` caught only `HttpRequestException`, so GitHub's client timing out (a `TaskCanceledException`) or a body of the wrong shape escaped, and because `NightlySource.HourAsync` asked the reader outside its own `try`, the whole nightly hour failed, the Azure facts with it. And the 1.0.4.18 sentence for an empty listing said "the group is empty, or it holds no role that reads it": the honest union of what one list call knows, left unresolved.

From 1.0.4.22 the reader catches every failure but the host stopping and says which ("not read: GitHub did not answer (TaskCanceledException)"), falling back to the log as before, and the hour asks it inside a `try` of its own, so the Azure facts are kept whatever GitHub did. An empty listing is resolved by one read of the Function's own path (`FunctionPath`, from configuration): a 403 says "the site's identity holds no role on {group}" and marks the role missing, a 404 says "no Function app named {name} in {group}", and a 200 reads the Function as found and carries on. A 404 on the group's list is counted in `Refused` beside 401 and 403, so it no longer takes the hour down. The spec line now reads "FLEX CONSUMPTION · NOT READ" and no more; the reason is a readout line of its own, "Spec", in a sentence and never in capitals. `NightlySource.cs` loses its doubled summary (`Refused` has its own again), and the stale hand-written sentence in `NightlyCardTests.Unkept.cs` is built from `NightlySource.Unfound`. `NightlyCardTests.Reader.cs` holds a GitHub timeout leaving the Azure facts in the hour and the three answers through `HourAsync`.

## Addendum, 2026-10-22 (1.0.4.25): the roles granted, the containers that could not be, and the snapshots read

On the owner's word (22 October, 04:16 CDT: "I grant full permissions for that prompt"), lane rolegrant made the two grants this record has asked for since 1.0.4.9, through the runner, and read them back:

- Reader on `RG-JOBHUNT-SS` for the site's identity `id-theyard-ss` (principal `dd34b0a6-4df7-4381-b6af-d1262a98f9ef`), so the group lists the Function and its ARM record, plan and storage read.
- Monitoring Reader on `RG-JOBHUNT-SS` for the same identity, so the Function's two metrics and the five-minute firing counts read.

`az role assignment list --scope <RG-JOBHUNT-SS>` answers exactly those two for the site, beside the job hunt identity's one share role, and `functions/jobhunt/role-check.ps1` now fails on anything else the site's identity holds on the group or on any resource in it.

![A summary of the role assignments on RG-JOBHUNT-SS, read back on 22 October, and Cosmos DB's refusal of a twenty-sixth container](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/nightly-roles.svg)

**The two containers could not be created, and are not needed.** `az cosmosdb sql container create` for `nightly` answered "Collection create forbidden as collection count in database offer has exceeded 25": the `theyard` database shares its thousand RU/s across its containers, a shared database holds at most twenty-five, and it holds twenty-five, seventeen the site defines and eight copies the live tests use under `tests-`. The two definitions were never going to apply; the card read live for two days on a create that could not succeed. From 1.0.4.25 the card's minutes and hours and its run log live in the `plan` container, which is partitioned on the day and indexes the kind, the day, the moment and the bucket keys, exactly what the two definitions asked for. Their kinds and ids open with `nightly-` (`Containers.NightlyPrefix`; `CosmosResourceHistory` takes the prefix, `CosmosNightlyRunLog` writes it), so the plan's own minutes and the nightly card's never meet in a query, and each run document carries its own time to live of a year, which the plan container's month does not override. `infra/cosmos/nightly.json` and `nightlyruns.json` are removed. `CosmosDefinitionTests.The_shared_database_never_needs_a_twenty_sixth_container` fails a definition that would need one, so a later card shares a container the same way instead of waiting on a create.

**The snapshot count is read with Reader.** The card said the share's snapshot count needed a data role on the storage account. It does not: Resource Manager lists a storage account's shares with their snapshots (`fileServices/default/shares?$expand=snapshots`), which Reader on the group may read, with no data role and no account key, and without opening anything in the share. The hour now reads it (`NightlySource.SnapshotCount`), and the readout's "Snapshots kept" line and the cost row say how many the account holds against the fourteen the Function keeps.

**Nothing that needed a role can park again.** The owner's ship script, which lives in his own workspace outside this repository, now reads every Cloud card on both names before it applies a patch and stops a ship whose card says "no role on", printing the `az` command that clears it. A read-only grant on the owner's own group to the site's own identity is pre-approved for every change that needs one, on his word of 22 October.

## Addendum, 2026-10-22 (1.0.4.26): the dial reads the night before the keep holds it

Read live on both names after 1.0.4.25 rolled, the card showed the Function's spec, Last fired (22 October, 04:00 Central), the firings and units sparklines, the free grant and three share snapshots, and one dial still said "no slot kept yet": the slots are judged against the kept five-minute minutes, the keep began at 10:5x UTC, and the next slot was 13:00 UTC. The hour already judges the day before from Azure Monitor's own five-minute counts, which Monitor keeps whether or not the site did, and writes those judgements to the run log. From 1.0.4.26 the hour also writes the day's tally as a fact (`NightlySource.SlotsFact`), and until the kept minutes hold a whole slot the dial reads that tally instead (`NightlyCard.Tally`, `HourTally`). Once a slot comes due inside the keep, the thirty days' count takes over as before.

## Addendum, 2026-10-22 (1.0.4.27): the merger's counts and ages, from the Function

The card's rule was that nothing in the share is read here. From 1.0.4.27 that sentence stopped being true in one narrow way, and the allowed row says so: the tracker merger moved into the Function (ADR: The tracker merger runs in Azure), and the card has a fifth question, "Is the tracker merger keeping up?", answered by five numbers the Function's own health route gives, read once a minute beside the metrics and kept on the same minute. No file in the share is opened by the site, no key to the share or the app is in its settings, and only the five numbers are kept; any other field in the Function's answer is dropped. The row is good while nothing has waited past five minutes, information while something has, and a warning past ten. `NightlyCardTests.Merger.cs` builds the card through `ResourceCatalog.Cards`, routes the hour and the minute to a recorded group and health answer, and holds that no file name, row or log line from that answer reaches the card's JSON.

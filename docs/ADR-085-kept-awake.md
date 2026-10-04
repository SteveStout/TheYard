# ADR: Kept awake

Status: accepted, 2026-09-28. Steve's goal for it, in his words: "no visitor ever meets a cold read, on either site, at any hour." Shipped as 1.0.3.35.

## In plain words

This page decides that each running copy of the site sends itself every public read a visitor can make once every four minutes. That keeps the databases and their caches from going cold between visitors (a keep-warm loop). It runs inside the app on both sites, and the health card shows when it last ran.

What that is worth: a developer can trust that the first visitor after a quiet spell gets a warm read instead of waiting two seconds, and the organization pays nothing extra for it on the plans it already has.

## Context

Read from Missouri on the Cosmos DB site on the morning of 28 September, every read that had been quiet for a while was slow the first time and fast after it:

| The read | First after a quiet spell | Warm |
| --- | --- | --- |
| `GET /api/admin/activity` | 2.0 to 2.2 s | about 260 ms |
| `GET /api/health` | 736 ms | about 190 ms |
| The activity card's four chunks through the edge | about 700 ms on a miss | 33 ms on a hit |

The chunks are the edge's (ADR: Cache headers, the addendum of the same day). The reads are the store's: a query's connections and its plan go cold when nobody asks for a while, and so do the caches in front of the stores. The first visitor after the quiet pays for waking them. The activity report had a second cost of its own: counting every visitor row of its window. That count is now kept and rebuilt behind the reader (ADR: Site activity, and the line an address does not cross, the addendum of the same day). What was left was simply that nobody had asked in a while.

## Decision

**The container keeps its own reads warm.** `KeepWarm` in `api/TheYard.Api/KeepWarm.cs` is a loop inside the process that, every four minutes for as long as the container runs, sends every public read a visitor can open through the app's own HTTP pipeline, once for each store (the `X-Yard-Store` header): the listing, the filter values, one vehicle from that listing, the health report, the stores, the version, the tests summary, the activity report for each of its three windows, the machines, the page sweep, the metrics, the SQL and store logs, the log, Azure's view of the container, the peer, the tests, the proof, the experiment, the telemetry and the errors. Each read carries the site's own mark on its agent, `TheYard-SelfRead/1 (keep-warm)`, so the activity card counts it as the site reading itself and never as a person. It also carries a header of its own, which keeps it out of the request ring and the kept log: two hundred slots of the container reading itself every four minutes would be the speed tile timing the loop, not a visitor.

- **In the process, not on a schedule outside it.** A scheduled workflow starts when a runner is free, not when it was asked (Steve: "scheduled runners start late and are not dependable"). A loop inside the container runs exactly as long as there is something to keep warm, including the minutes after a roll.
- **Staggered.** The first pass waits a random part of a minute, so the two sites' containers do not fire together on the one plan.
- **Never overlapping, never throwing.** A pass is awaited before the next is timed, so a pass that runs long pushes the next back rather than running beside it. A read that fails or answers anything but 200 is counted and logged as a warning, and the pass goes on; a pass that throws is logged and the next one still comes.
- **On where it is deployed, off everywhere else.** It runs where App Service runs the site (`WEBSITE_SITE_NAME` is App Service's own setting) and nowhere else, the test host included. `KeepWarm:Enabled` turns it off on a site, or on anywhere, as an app setting.
- **Visible.** The health card says when the last pass ran and how many reads it sent, with the slowest of them: "Kept warm: last pass 12:04, 46 reads, slowest 312 ms".

## What it costs

Every read goes to the container's own loopback address, so none of it passes the edge or counts against the edge's allowance. What a pass spends is store work, priced before shipping against what Azure itself said on 28 September (`az` reads, 12:47 CDT):

- **Azure Cosmos DB.** The account `cosmos-theyard-ss` is on the free tier, and its database is provisioned at 1,000 request units a second; it is not serverless and not autoscaled. On provisioned throughput the bill is the rate and not the use, and the free tier covers the first 1,000 a second, so reads under the rate add $0. What a pass uses: one read of the activity report for each of its three windows cost 312 request units when measured that morning, and a pass sets off at most one rebuild of each window's report per container; the health check's two point reads and one vehicle add a few more, as do the store's own reads. Two containers, one pass each every four minutes, is 720 passes a day: about 225,000 request units a day, about 2.6 a second on average against the 1,000 provisioned, a quarter of one per cent. A pass is a burst of about 312 over a few seconds, which is under the rate. The SDK retries a throttled read on its own. The figure per pass is read off the live site once the loop runs.
- **Azure SQL Database.** The sites point at `sqldb-theyard-ss-basic`: Basic and five DTUs, online. It has no auto-pause delay and no minimum capacity, because a Basic database has neither. It is priced by the day whatever it does ($4.90 a month, ADR: One plan, two sites), so the loop adds $0, and it cannot pause. The serverless database beside it, `sqldb-theyard-ss`, is paused and no site points at it.
- **App Service.** `PLAN-THEYARD-SS` is Basic B1: one worker for two sites. It has no autoscale setting and no elastic scaling. Both web apps are running with Always On set, and a health-check path of `/healthz`. A Basic plan is priced by the hour whatever it runs ($12.41 a month, ADR: One plan, two sites) and never scales to zero, so the loop runs inside a processor already paid for and adds $0. Always On is what keeps the process itself alive between visitors; the loop keeps what is inside it warm.
- **Nothing else can go to sleep.** The two container groups the sites ran on before the plan, `aci-theyard-ss` and `aci-theyard-cosmos-ss`, are still in the resource group and serve nothing; the edge's rules no longer point at them.

## What it does not do

Steve's plan also asked each pass to fetch the build's hashed files through the public address, so the edge never lets a chunk go between visitors. That part is not built, for two reasons read before shipping; it waits on his call. The edge keeps a proxied file at the node that served it, and a read from the container in West US 3 warms the node nearest West US 3, not the one a visitor in Missouri reaches (ADR: Cache headers, the addendum of 28 September). And the edge charges for it: about forty files a pass from both containers every four minutes is close to a million requests and five gigabytes a month, about 300 of the edge's credits a month at its April 2026 prices (2 credits per 10,000 requests, 20 per gigabyte), the whole of the free allowance, to keep warm a node that serves nobody in particular. The deploy's warm-up, and the push from Missouri, cover the first visitor after a roll instead.

## Turning it off

Set `KeepWarm__Enabled` to `false` in a site's app settings; the site restarts without the loop and the health card says "Kept warm: off on this container". Nothing else depends on it: the reads it sends are reads any visitor can send.

## What holds it

- `KeepWarmTests`: with a clock the test moves by hand: the first pass waits the stagger and then one pass runs every four minutes; a pass that runs past the interval delays the next and two never run at once; a pass that throws does not stop the next; inside a pass a read that fails is counted, and every other read is still sent, on both stores, with the site's own mark.
- `keepWarm.test.ts`: the health card's line, when the loop has run, has not yet, and is off.

## Where it sits

KeepWarm and KeepWarmState live in the host Api, with one line on the health card in the front end, since sending reads to the app's own pipeline is a hosting concern that no inner ring should know about. Single responsibility, one reason to change, separates the schedule in KeepWarm from the reads in KeepWarmReads, and the loop takes its clock as a TimeProvider through the constructor, the dependency inversion habit that lets KeepWarmTests move time by hand. It cost a pass of reads every four minutes per container, inside capacity the plans already pay for. A plan billed per request or able to scale to zero would make the loop cost money and reopen the choice.

## Files

- [`api/TheYard.Api/KeepWarm.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/KeepWarm.cs): the loop, the reads and the pass.
- [`api/TheYard.Api/Composition/Startup.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/Startup.cs): the wiring (region keep-warm-wiring).
- [`api/TheYard.Api/Composition/ObservabilityRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/ObservabilityRegistration.cs): the request hook that leaves its reads out of the ring and the kept log.
- [`api/TheYard.Api/Endpoints/HealthEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Endpoints/HealthEndpoints.cs): the health report that carries its last pass.
- [`api/TheYard.Tests/KeepWarmTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/KeepWarmTests.cs): the schedule, the overlap and the failures, on a clock the test moves.
- [`src/lib/keepWarm.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/keepWarm.ts) and [`src/components/admin/HealthCard/HealthCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/HealthCard/HealthCard.tsx): the health card's line.

```live path=api/TheYard.Api/KeepWarm.cs region=keep-warm
```

# ADR: Live dials where the source allows

Status: accepted, 2026-11-07, at 1.0.4.159. Every card under Cloud infrastructure has a live route, GET /api/admin/cloud/{key}/live, and a live line under its dials. A card whose source reads faster than a minute (the plan's /proc, Azure SQL's own view, this process's Cosmos DB charges) shows that reading at the source's own grain while somebody looks; every other card says it has no live source, once, and stops asking.

## Reading list

1. [ADR: The box reports itself](https://theyard.stevenstout.biz/?doc=adr-the-box-reports-itself): the first live ring and the poll this one generalises.
2. [ADR: One frame for every resource](https://theyard.stevenstout.biz/?doc=adr-one-frame-for-every-resource): the frame the live line sits in, and the kept minutes the tones keep coming from.
3. [ADR: Telemetry](https://theyard.stevenstout.biz/?doc=adr-telemetry): the daily cap the live routes stay out of.

## In plain words

The cards under Cloud infrastructure read their resources once a minute, some once an hour. A few of those resources can say more often what they are doing: the database keeps its own view every fifteen seconds, and the container can read its own memory and processor every two. Those cards now show that faster number while somebody has the card open, and nothing runs when nobody does. The colour of a card never moves with the live number: it still comes from fifteen held minutes, so a spike does not flash red.

## Context

Steve asked on 4 November for monitors that move "like on linux resources", and the WickedAwsome card did that for the box with a ring held in memory and a poll every two seconds (ADR: The box reports itself). The lane that made the Admin tab stable (adminstable1105) planned the same pattern for every card where the source allows, with Steve's grains: the plan every 2 s if its /proc fork passes, Azure SQL every 15 s, Cosmos DB every 5 s.

## Decision

1. **One ring type.** `LiveRing<T>` in the Application's Shared/Live holds every live reading: a bounded queue under one lock, the oldest dropped on every push, read back by age. The box's ring moved onto it in the same version.
2. **A source, a lease, one sampler.** A card's live source (`ILiveSource`) names its key, its grain, its source line, and why it cannot read in this process when it cannot. `LiveLease` leases a card for 60 seconds after each read of its live route; `LiveSampler`, one background loop a second, asks each leased source for one sample when its grain has passed, and holds it in that card's ring (`LiveRings`, 240 samples a card). A source nobody is looking at never runs.
3. **One route for every card.** GET /api/admin/cloud/{key}/live answers `{ available, note, grain_seconds, source, as_of_ms, samples }` for the last ten minutes, anonymous like the card. A card with no live source answers 200 with available false and one sentence, never 404. The box keeps its own route, whose literal path wins.
4. **Polling, not streaming.** The page asks at the source's grain, never faster than every 2 s, only while the card is mounted and the tab is in view, and stops for good once the card says it has no live source. The edge buffers a streamed answer (ADR-100's rejected option), so SignalR and Server-Sent Events were rejected; so was Azure Arc, for the box.
5. **Out of the telemetry.** Every live route, and every request carrying the site's self-read tag (the keep-warm loop's and the nightly self-read's), is left out of Application Insights' request telemetry; the live routes are GETs under /api/admin/, so the request ring already leaves them out.
6. **The tone does not move.** The live number is a line under the dials; the dials, the rows and the card's tone come from the kept minutes and the fifteen-minute hold, unchanged.

This version adds the pattern and no source: every card says it has no live source until the versions that add the plan's, Azure SQL's and Cosmos DB's.

## What holds it

`LiveTests`: the ring and its bound, the lease's minute, the read that leases and answers, the sampler at each source's grain and past one that throws, the telemetry filter over every live route and the self-read tag, and the route answering 200 for a card with no source; `live.test.ts`: the poll's grain, when it stops, and the line's words.

## Where it sits

The ring, the source port, the lease and the rings by card in the Application's Shared/Live, used by more than one feature; the read in the Application's Admin Cloud feature beside the box's handlers; the sampler and the sources in the host's Cloud slice; the page's read in src/lib, the poll in src/hooks and the line in its own component folder.

## Files

- [`api/TheYard.Application/Shared/Live/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Application/Shared/Live) - the ring, the source, the lease and the read
- [`api/TheYard.Api/Features/Admin/Cloud/LiveSampler.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/LiveSampler.cs) - the one loop
- [`src/lib/live.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/live.ts), [`src/hooks/useLive.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useLive.ts) and [`src/components/admin/LivePanel/`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/LivePanel) - the page's read and line

## Addendum, 2026-11-07 (1.0.4.160): the plan's /proc fork

The plan card's live source reads this instance's own /proc every 2 s while the card is open: memory is what /proc/meminfo says is not available of MemTotal, the processor the busy share of every core between two reads (`ProcLiveSource`). Its line names its source, "this instance, /proc, 2 s". Whether it tracks Azure Monitor's minutes for the plan is measured after this version rolls: thirty minutes of the live route every 2 s beside the plan's MemoryPercentage and CpuPercentage minutes, written to J:\staging\adminstable1105\proc-compare.jsonl, and the verdict (MemTotal within 2 % of the plan's 3.78 GB, both series within 5 points of the minutes) in the next version's addendum. Pass or fail, the dials keep their kept minutes; on a fail the line says it is this container's own view. ProcLiveSourceTests holds the reading over a fixture /proc and on the box's own.

## Addendum, 2026-11-07 (1.0.4.161): Azure SQL's line

The Azure SQL card's live source reads the database's own view of itself, `sys.dm_db_resource_stats`, which every tier keeps at fifteen-second intervals for an hour and which costs nothing to read, every 15 s while the card is open (`SqlLiveSource`, through the same ResourceStats read the Machines card uses, 40 rows). Its line says "DTU" as the greatest of the newest interval's processor, data IO and log write shares, Microsoft's example formula, beside the three shares; Azure Monitor's own dtu_consumption_percent stays on the dial, read a minute at a time, so the two can be compared on the card. A read that finds no newer interval adds no sample. A process with no relational store says "this site keeps no relational store". SqlLiveSourceTests holds the formula with each share leading, the newest interval, and the absent cases.

The same version matches the nightly picker's card to the Function's line of the bill, which it had matched under its title and so read "not on the bill yet".

## Addendum, 2026-11-07 (1.0.4.162): Cosmos DB's line

The Cosmos DB card's live source sums this process's own requests to the account every 5 s while the card is open, from the operations ring the Machines card already keeps (`CosmosChargeSource`): request units a second over the last five seconds, how many requests made them, and how many were throttled. Cosmos DB has no fifteen-second view of the whole account, so the dial stays Azure Monitor's minute for the account and the line says only what this process asked for, "this process's requests, live". A process with no document store says so. CosmosChargeSourceTests holds the window's edges, the throttled count and the quiet case.

## Addendum, 2026-11-07 (1.0.4.162): the /proc fork's verdict, fail

The plan's /proc source was compared with Azure Monitor's minutes for the plan for thirty minutes after 1.0.4.160 rolled (20:34 to 21:05 CST, the live route read every 2 s beside MemoryPercentage and CpuPercentage, 802 lines in J:\staging\adminstable1105\proc-compare.jsonl, 30 minutes compared). MemTotal read 3.78 GB, the plan's own size, within the 2 % the rule asks. Memory tracked: the gap between the minute's mean of the 2 s samples and Azure's minute had a median of 1.3 points, a 90th percentile of 5.7 and a mean of +0.5, its worst 10.7 in the minute of the roll. The processor did not: a median gap of 3.9 points, a 90th percentile of 15.5 and a worst of 69.7, because a 2 s sample of a bursty processor and a minute's average are different numbers, most of all in the minutes after a roll. The rule asks both series within 5 points over the thirty minutes, so the fork fails. What follows, as decided before the measurement: the plan's dials keep Azure Monitor's minutes, and the live line names itself "this container's own /proc, 2 s, not the plan's minute". At more than one instance the line is one instance's view.

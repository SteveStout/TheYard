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

## Files

- [`api/TheYard.Application/Shared/Live/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Application/Shared/Live) - the ring, the source, the lease and the read
- [`api/TheYard.Api/Features/Admin/Cloud/LiveSampler.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/LiveSampler.cs) - the one loop
- [`src/lib/live.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/live.ts), [`src/hooks/useLive.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useLive.ts) and [`src/components/admin/LivePanel/`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/LivePanel) - the page's read and line

# ADR: Memory follows traffic

Status: accepted, 2026-10-18. An Admin card, Memory against traffic, plots the process's processor and memory against requests a minute over the tab's window, and reads red when memory keeps climbing while traffic is flat: a leak or a cache with no bound. Run over the readings the site kept, it reads red the week the hundred thousand vehicles moved into each process, ten days before the plan card found them.

## Reading list

For Steve, when he is fresh, in this order:

1. The card itself, [Memory against traffic](https://theyard.stevenstout.biz/?view=admin&card=follows), on the 30-day window.
2. [ADR: The store does the work](https://theyard.stevenstout.biz/?doc=adr-the-store-does-the-work): the incident this card would have caught, and what fixed it.
3. [Memory, held by the build](https://theyard.stevenstout.biz/?doc=memory), rule b: every cache bounded, because an unbounded one is the shape this card looks for.
4. Microsoft Learn, [Fundamentals of garbage collection](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals): why a working set rises and falls under load and a leak only rises.

## In plain words

A web server's processor should be busier when more people use it and quieter when fewer do. Its memory should mostly stay put: a server that needs more memory every hour, whether or not anyone is visiting, is keeping something it should have let go. This card draws both against the traffic, so the healthy shape and the broken one can be told apart at a glance, and it says so in words when the broken one shows.

What that is worth: the catalogue held in memory passed every test for weeks, because tests check what code was asked to do, not what it keeps. This card checks what it keeps.

## Context

Steve asked on 18 October for a card plotting processor and memory against requests a minute, an alert when memory keeps climbing while traffic is flat, and the note that it would have flagged the catalogue issue. The readings already existed: every site folds each finished minute into the machines history, with its working set, its processor and the requests it answered (ADR: What the machines are doing), kept 31 days in Cosmos DB and read back a day, a week or a month at a time. Nothing new is measured for this card.

The kept readings show both shapes. From 24 to 26 September a load test lifted the traffic forty times; the working set went from about 110 MB to about 340 and came back to about 140 when it stopped, and the processor followed the requests with a correlation of 0.75. From 6 to 11 October the working set went from about 400 MB to about 650 on the 8th and stayed there, while the traffic fell back to a few hundred requests in four hours. That second shape is the catalogue in memory.

## Decision

**The rule, in the server.** `MemoryTraffic.Judge` in Application splits the window in two by time. Memory is climbing when the second half's middle working set is at least 50 MB, or 5 per cent of the memory limit, whichever is more, above the first half's, and the least-squares line through every point slopes up. Traffic is flat when the second half's middle requests a minute is no more than a quarter above the first half's, plus one. Both at once is red. Middles rather than averages, so one busy minute or one collection does not decide a window. Memory that rose as traffic rose is green and says it is worth watching. Fewer than six readings is not judged.

```live path=api/TheYard.Application/Features/Admin/Operations/MemoryTraffic.cs region=rule
```

**Deploys are not leaks.** The last hour is this process's own, a minute at a time, and leaves out its first fifteen minutes, when the runtime, the caches and a fill grow memory for reasons that are not a leak. Steve's rule for the plan's alerts holds here too: a deployment moves the numbers, so a reading has to hold before it means anything. A wider window is the kept buckets, judged on middles, so a roll's dip and climb does not decide it.

**One route, one card.** `GET /api/admin/follows?window=` answers the window's points and the verdict; the card under Cloud infrastructure, after the machines, draws the two scatter plots and the straight line through each, with the finding in one sentence on the tone every Cloud card uses. The alert is the card's red for now; the email the observability lane adds reads the same rule.

**Held to the real readings.** `MemoryTrafficTests` carries both weeks above as the site kept them and holds the rule to red on the catalogue week and green on the busy one, with the processor following the traffic.

```live path=api/TheYard.Tests/MemoryTrafficTests.cs region=catalogue-week
```

## Consequences

- The catalogue incident is now a test: a change that brought back a process-held copy of the store would read red on this card within the window it climbed in.
- The rule reads the process's working set, which the runtime grows ahead of need and gives back late; a climb of under 50 MB is not called a climb, so ordinary collector behaviour is not an alarm.
- A slow leak inside a busy day, where traffic also rose, reads green and "worth watching". That is the cost of not calling every busy afternoon a leak; the 30-day window, where the busy days average out, is the one to read for a slow one.

## Where it sits

The rule in `TheYard.Application`, pure arithmetic over points with no clock and no store. The route in the host's admin endpoints, reading the machines sampler, the request ring and the kept history the machines card already reads. The card in `src/components/admin/FollowsCard`, its placing of points in `src/lib/follows.ts`.

## Files

- [`api/TheYard.Application/Features/Admin/Operations/MemoryTraffic.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Admin/Operations/MemoryTraffic.cs) - the rule
- [`api/TheYard.Api/Features/Admin/Operations/AdminFollowsRoutes.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Operations/AdminFollowsRoutes.cs) - the route and its wire
- [`src/components/admin/FollowsCard/FollowsCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/FollowsCard/FollowsCard.tsx) - the card
- [`src/lib/follows.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/follows.ts) - the dots and the trend line
- [`api/TheYard.Tests/MemoryTrafficTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/MemoryTrafficTests.cs) - the rule held to the kept readings

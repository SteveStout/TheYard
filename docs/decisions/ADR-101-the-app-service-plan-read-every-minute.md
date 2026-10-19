# ADR: The App Service plan, read every minute

Status: accepted, 2026-10-18. The Admin tab gains a Cloud infrastructure section, and its first card reads the App Service plan the three web apps run on: the plan's processor and memory as live dials, thirty days of both with every deploy and every Azure platform event drawn on the same line, the apps the plan carries, and the plan's own events. One document a minute is kept in Cosmos DB for thirty-one days and the card reads the kept copy; Azure Monitor is asked only when the newest kept minute is older than a minute.

## In plain words

The portal shows what the machine is doing. The deploy pipeline shows what the code did. Neither page knows about the other, so when something goes wrong the first twenty minutes of any review go to lining up timestamps from two tools.

This card puts both on one line. Memory that steps up at a deploy marker changed because the code changed, which is a decision. Memory that climbs between markers is a leak. Memory that drops at a red line was Azure restarting the worker. One line, three readings, and nobody has to ask the other team.

What that is worth: the people who write the code and the people who run it look at the same screen and see the same information.

## Context

Since 1.0.0.156 both sites run as web apps on one Linux App Service plan (ADR: One plan, two sites), and since the rendering service (ADR: A rendering service beside the API) a third app, the Node renderer, sits on the same plan. The plan moved from B1 to B2 on 8 October when its memory read 91 per cent at the median, so it is two virtual processors and 3.5 GB, and three apps share it. Two of them hold a 100,000-vehicle catalogue in memory each, so memory is the resource that runs out first, and the plan's dashboard in the portal is the only place that said so.

The site already keeps what its own machines are doing, one document a minute (ADR: What the machines are doing), and what Azure charges, one read an hour (ADR: What Azure charges). Both read Azure with the container's managed identity and keep the answer in Cosmos DB so the Admin tab never waits on Azure. The plan's metrics are the missing third source, and they follow the same shape.

## Decision

**One recorder, one document a minute, written by one site.** `PlanRecorder` asks Azure Monitor once a minute for the plan's metrics at one-minute grain, and for each of the three apps' own metrics, and writes one document to the `plan` container partitioned on the UTC day. Only the site whose default store is the relational one records, the rule the machines recorder already follows, so two sites never write the same minute twice. The Activity Log and the three apps' state, always-on setting and last deploy are read once an hour, because they change at most that often.

```live path=api/TheYard.Api/PlanRecorder.cs region=plan-recorder
```

**Kept thirty-one days, served from the kept copy.** The container's own time to live is thirty-one days, one more than the longest window the tab offers, so the container is its own retention policy. `GET /api/admin/plan?window=24h|7d|30d` answers from Cosmos DB alone. A live read happens only when the newest kept minute ended more than sixty seconds ago, which is the case for the first minute after a cold start and never otherwise. Azure Monitor metric reads are free; the writes are about 43,000 a month at roughly 1.3 request units each, inside the free tier the account already runs on.

```live path=infra/cosmos/plan.json region=*
```

**Two dials for the two numbers that run out.** Processor and memory, as a ring each, read from the newest kept minute; the tab asks again every thirty seconds while it is open and the server caches its answer a minute, so the dials move once a minute. The ring carries the thresholds as marks on its inner edge, a gold dot at the thirty-day ninety-fifth percentile and a red dot at the thirty-day peak, so the reading has its history beside it. The dials answer the question the plan exists for: how much of the machine is left.

**Deploys and platform events on the memory line.** Every kept minute carries the build version the site was running, so the thirty-day chart draws a gold marker wherever the version changed, with no second source and no clock to reconcile. Azure's own events, read from the Activity Log, draw as red dashed lines on the same axis. This is the decision the whole card rests on: the two things that move memory are drawn where memory is drawn.

```live path=src/components/admin/CloudChart/CloudChart.tsx region=plan-markers
```

**Thresholds held by a test, sustained before they speak.** Processor amber at 60 and red at 80 per cent, memory amber at 70 and red at 85, the HTTP queue amber at 5 and red at 20 waiting, outbound sockets amber at 60 per cent of the plan's 1,920. Each must hold for fifteen minutes before the card changes colour, so a deploy's two-minute warm-up spike reads as a spike and not as an alarm. Any app stopped, or any unplanned restart in the window, is red at once. `PlanThresholdTests` holds every number.

```live path=api/TheYard.Application/PlanThresholds.cs region=plan-thresholds
```

**Absent is a sentence, never a zero.** No Reader role on the plan, no minute kept, a window with a gap: each is said in words on the card. A dial at zero would claim the machine is idle, and a flat line through a gap would claim nothing happened. The cost cards set this rule (ADR: What Azure charges) and the plan card keeps it.

**Every resource name is a link to its portal blade.** The plan and each app are drawn as links to `portal.azure.com/#@{tenant}/resource/{resourceId}`, with the tenant and the resource id taken from the Azure read, never typed. The link opens with the viewer's own Azure sign-in, which only the owner has, and the card says so beside it. A reader who cannot open the blade still sees the name, which is the point of naming it.

## What this cost

A recorder, a reader, a container definition, and one Admin card in six parts. The containers' identity needs Reader on the plan and the three sites, which covers the metrics and the Activity Log, at no charge; it already held Reader on the resource group, which the plan and every site inherit, so no assignment was made. That was read back through the runner, and the screenshot is below with the container's. No new Azure resource and no tier change.

## Alternatives

**Read Azure Monitor from the page.** Rejected: every visitor to the tab would cost a metrics call, the tab would wait on Azure, and a throttled read would leave the dials blank. The kept copy costs one write a minute whoever is looking.

**Draw deploys from the deploy workflow's history.** Rejected: that is a second source with its own clock, and the whole point of the chart is one clock. The version the site was running when the minute was kept is already true and already there.

**Put the plan card under "Is it costing anything?"** Rejected on the owner's ruling: the plan is infrastructure, and the cost of it is already on the cost card. A new section, Cloud infrastructure, holds this card and the two cards about the container that were under the cost question before.

## Where it sits

`PlanRecorder` and `PlanReader` are in `TheYard.Api` beside the machines recorder, and they share its credential and its once-per-process rule. The port `IPlanHistory` is in `TheYard.Application`; `CosmosPlanHistory` in `TheYard.Infrastructure.Cosmos` is its only implementation. The card is `src/components/admin/PlanCard`, which chooses the route, drawn by parts split by job, each in a folder of its own as every component is (ADR: One folder per component): `CloudDial`, `CloudChart`, `CloudRows`, `CloudReadout`, framed by `CloudCard`. The thresholds sit in `TheYard.Application` beside the port, because they are a rule about the readings and not about Azure. Nothing in Domain knows the plan exists.

What would change it: a second resource read the same way, at which point the recorder and the container shape become one shared shape with the resource as its parameter. The card's parts were built shared from the start, so the frame is already the one the next record names.

## Files

- [`api/TheYard.Api/PlanRecorder.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/PlanRecorder.cs) - the minute's read and the hourly read
- [`api/TheYard.Api/PlanReader.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/PlanReader.cs) - the reads from Azure Monitor and Resource Manager
- [`api/TheYard.Api/PlanView.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/PlanView.cs) and [`api/TheYard.Api/PlanRows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/PlanRows.cs) - the windows, the percentiles, the markers, the rows
- [`api/TheYard.Application/PlanThresholds.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/PlanThresholds.cs) - every threshold and its hold time
- [`api/TheYard.Api/CloudWire.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CloudWire.cs) - the records the wire carries
- [`api/TheYard.Application/PlanHistory.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/PlanHistory.cs) - the port
- [`api/TheYard.Infrastructure.Cosmos/CosmosPlanHistory.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosPlanHistory.cs) - the keep
- [`infra/cosmos/plan.json`](https://github.com/SteveStout/TheYard/blob/main/infra/cosmos/plan.json) - the container, its key, its time to live, its index
- [`api/TheYard.Tests/PlanViewTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PlanViewTests.cs) - fixed minutes in, fixed report out
- [`api/TheYard.Tests/PlanThresholdTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PlanThresholdTests.cs) - the numbers above, held
- [`src/components/admin/PlanCard/`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/PlanCard) - the card; its parts are [`CloudCard`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/CloudCard), [`CloudDial`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/CloudDial), [`CloudChart`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/CloudChart), [`CloudRows`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/CloudRows) and [`CloudReadout`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/CloudReadout)
- [`src/lib/bench.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/bench.ts) - the Cloud infrastructure section
- [`tests/e2e/cloud.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/cloud.spec.ts) - the dials, the three apps and the section, on the page

Configuration, read back through the runner on 18 October: the identity's Reader on the resource group, which the plan and its apps inherit, and the `plan` container on the account.

![The role assignments id-theyard-ss holds, read back: Reader on RG-THEYARD-SS, which the plan and its three apps inherit](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/plan-reader-role.svg)

![The plan container on cosmos-theyard-ss, read back: partitioned on /day, thirty-one days to live](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/plan-container.svg)

The minute's path, Azure Monitor to recorder to Cosmos DB to card:

[![The minute's path: Azure Monitor read by PlanRecorder on the SQL site, kept in the plan container, read by GET /api/admin/plan and drawn by the plan card](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/plan-minute.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/plan-minute)

## Addendum, 2026-10-18: a deploy is a span, and a spike inside one is a blip

The gold marker above says where the version changed; from 1.0.3.177 the chart also shades the whole roll. Every deploy writes each site's settings and then its image, and Azure's Activity Log, already read hourly, records both as configuration writes on the app; writes within ten minutes of each other are one roll, and the span runs from the first to three minutes after the last, while App Service starts the new container beside the old one and the plan runs both. The owner asked for it after the plan card's CPU sparkline showed two spikes: "I'd like to pull that data in so it's obvious it's just a blip." The "What did Azure do to it?" row now lists each roll in a table as a planned deploy with how long it took, and any spike over the amber line inside one is named in a sentence as the roll it was. Nothing about the fifteen-minute hold changes: a roll lasts about two minutes, so it could never turn a dial amber, and now the reason is on the chart.

```live path=api/TheYard.Api/PlanDeploys.cs region=plan-deploys
```

## Addendum, 2026-10-19: what each app holds and burns

Steve asked on 19 October for the four apps' RAM and CPU beside the plan's dial. The plan's kept minutes already carried each app's working set and its seconds of processor a minute, so nothing new is read: the table under "What does it power?" gains a Memory now and a CPU now column, the CPU as the app's seconds of processor over the sixty seconds each of the plan's cores had, and a last row for the plan's memory no app's working set accounts for, the operating system, the platform's containers beside each site and the file cache. Read the night it shipped, that last row was the largest thing on the plan: about 1.5 of the 2.45 GB in use, with the four apps' working sets at about 920 MB between them. `PlanChildrenTests` holds the arithmetic.

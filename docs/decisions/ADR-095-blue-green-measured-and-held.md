# ADR: Blue-green on a $13 plan, measured and held

Status: accepted, 2026-10-07 (1.0.3.96), and what it accepts is holding. The rolls were measured first, the cheapest fix shipped on its own in 1.0.3.89 (the container answers before it loads the catalogue), and the second copy of each site that blue-green needs was not created: on Steve's word, "1 for now", and on the plan's memory, which has no room for it. This record keeps what was measured, what each option costs, and what would bring it back.

## In plain words

Every deploy of this site takes it offline for a few minutes. The usual cure is blue-green deployment: two copies of the site, the new version started on the idle copy, and visitors moved across only once it answers. On Azure that is usually deployment slots, which need the Standard tier at about $56 a month more than this site's plan. This record measured where the minutes go before buying anything, shipped the one fix that costs nothing, and found that the rest of the minutes belong to the platform swapping the container on its single instance, which only a second copy running beside the first removes. That second copy does not fit in the plan's memory, so it waits.

What that is worth: a visitor's slow minutes after a deploy shrank from as much as two minutes to a few seconds, a developer can see why the offline stretch is still there and what removing it would cost, and the organization was not sold a tier to fix a stretch it could measure first.

## Context

Steve, 7 October: "When a deployment happens all of the api end points are frozen for 30 sec." Both sites run on one Linux B1 plan, one core and 1.75 GB, about $13 a month (ADR: One plan, two sites).

**Before any code** (read only). The three rolls Application Insights held in full, 1.0.3.84, 1.0.3.85 and 1.0.3.87, read from 20 minutes before each roll to 20 after; times are seconds from the start of the deploy's roll step.

| Phase of a roll | 1.0.3.84 | 1.0.3.85 | 1.0.3.87 |
| --- | --- | --- | --- |
| Settings, then the image, written | +6 to +19 | +6 to +19 | +6 to +19 |
| Old container answers normally | to about +110 | to about +150 | to about +150 |
| Old container slows: reads of 1 to 26 s, some never answered | from about +120 | from +155 | from +162 |
| Last request the old container answers | about +200 | +225 | +268 |
| New containers log "Now listening", both sites within 2 s | +412 | +437 | +437 |
| Nothing answers at the origin | 1 to 3.5 minutes | about 3.5 minutes | about 3 minutes |
| New containers' first 150 s: median read, worst read | 172 ms, 18 s | 342 ms, 112 s | 246 ms, 64 s |

Three things that reading settled. Writing the app settings did not restart the live site: they were written at +6 s and the old container served normally for two minutes after. The roll does not overlap the containers: on one instance the old container stopped answering minutes before the new one listened. And the new container listened late because it loaded a hundred thousand vehicles first, on the core both sites share.

## Decision

**Listen first, load after (1.0.3.89, $0).** The host answers at once and loads the catalogue beside the server; every API read waits for the load without holding a thread, and `/readyz` says not ready until the catalogue is in (ADR: The ports learn to wait).

```live path=api/TheYard.Api/Composition/Startup.cs region=listen-first
```

**Readiness on every store, for whatever swaps next.** `/readyz?stores=all` answers 200 only once every store the host runs has its catalogue in (ADR: The order of the request pipeline). It is the read a roll waits on before it moves a visitor, whether that roll is a second copy, a slot, or a second instance.

**No second copy yet.** The green apps, the color-aware roll and the edge function that flips between colors were written and not merged.

## Every roll since, read the same way

A probe on Steve's machine reads each site's origin (`/`, `/healthz`, `/api/version`, `/api/facets`) and domain (`/api/version`) every two seconds through each roll (`scripts/probe-roll.py`). Its 30 seconds is a limit on each wait on the socket, not on the read, so a read whose answer arrives in pieces can run past it. The roll of 1.0.3.95 logged good answers of 41.8 to 45.8 s; three other rolls logged failed reads of 40 to 57 s. Application Insights stops recording at its daily cap in the afternoon, so these numbers come from the probe, a different instrument from the table above.

Two columns, read the same way on every row. The first is the longest stretch in which an address got no good answer, from the shortest of the ten addresses to the longest. The second is the slowest good read that was sent after that site's API had answered under the new version, for the Azure SQL site then the Azure Cosmos DB site: the reads sent before it are queued through the swap and answered together the moment the container listens, so each of them reads as the 26 to 30 s it waited and says nothing about the new container. The logs are `greenlane-probe-103NN.log`.

| Roll | What changed | No good answer, longest | Slowest good read after the API answered |
| --- | --- | --- | --- |
| 1.0.3.89 | listening first | 158 to 186 s | 7.1 s and 7.1 s |
| 1.0.3.92 | the request pipeline reordered | 153 to 168 s | 4.5 s and 3.6 s |
| 1.0.3.93 | the runtime reading | 131 to 157 s | 4.1 s and 6.0 s |
| 1.0.3.94 | compiled ahead of time | 148 to 185 s | 6.8 s and 5.8 s |
| 1.0.3.95 | the records index read from the records | 119 to 156 s | 23.4 s and 2.4 s |
| 1.0.3.96 | this record, and the probe | 271 to 314 s | 10.1 s and 5.1 s |
| 1.0.3.97 | the template keeps the telemetry link | 151 to 227 s | 8.0 s and 8.2 s |

The stretch with no answer has held at two to five minutes through every change inside the container, `/healthz` included, which answers the moment the process listens and reads no store. Those minutes are App Service stopping the old container, pulling the new image and starting it on its one instance. The 23.4 s on the Azure SQL site at 1.0.3.95 is one stall eight and a half minutes after the start: every address of that site, `/healthz` included, answered nothing for 23 s and then answered together, while the Azure Cosmos DB container beside it answered throughout; what paused it was not measured.

## What it costs, against what it replaces

| Option | Added a month | Stretch with no answer | Why it is or is not taken |
| --- | --- | --- | --- |
| In-place roll, listening first (since 1.0.3.89) | $0 | two to five minutes | taken |
| A second copy of each site on this plan, stopped until a roll | $0 | none, if it fits | the plan reads 92 per cent of its memory used at the median over ten days; two more containers do not fit |
| The plan moved to B2, two cores and 3.5 GB, with the second copy | about $13 | none | room for both copies; held until Steve asks for it |
| A second B1 plan for the copy during rolls | about $13 | none | the same cost, and the new copy's load stays off the live core |
| Standard tier with deployment slots | about $56 | none | four times the added cost for the same overlap |

## What would bring it back

Steve asking for zero-downtime deploys at about $13 a month, or the plan's memory falling far enough that two more containers fit. The code that was written is kept on its own branch and is not described here as if it shipped; this record links only what is on the main branch.

## Addendum, 2026-10-08 (1.0.3.98): the table read again against its logs

Every number in this record was read again against the probe logs. Two things were wrong and are corrected above rather than left standing. The probe was described as giving up on a read at 30 seconds; its 30 seconds is a socket timeout on each wait, and the logs hold good answers of 41.8 to 45.8 s. The slowest-answer column had mixed two readings: the 1.0.3.89 row gave the slowest read sent after the new container's API had answered, while the rows after it gave the probe's own summary figure, which is the longest a read queued through the swap waited and comes out near 30 s on every roll. The column now gives the first reading on every row, and the rolls of 1.0.3.95 to 1.0.3.97 are added. The stretch with no answer stays the platform's, and the rolls since 1.0.3.94 are read in ADR: Compiled before it ships, the addendum on the four rolls.

## Where it sits

Outside the rings: the deploy and the plan. One change reached the host, listening before the catalogue is loaded, in the API ring's startup, and it changed no port. Single responsibility: the host says when it is ready, and the roll, whichever one ships, decides when to move a visitor.

## Files

- [`api/TheYard.Api/Composition/Startup.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/Startup.cs): listening first, in the region `listen-first`.
- [`api/TheYard.Api/Endpoints/HealthEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Endpoints/HealthEndpoints.cs): `/readyz` and readiness on every store.
- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep): the one plan and the two sites on it.
- [`scripts/probe-roll.py`](https://github.com/SteveStout/TheYard/blob/main/scripts/probe-roll.py): the probe every roll above was read with.

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

A probe on Steve's machine reads each site's origin (`/`, `/healthz`, `/api/version`, `/api/facets`) and domain (`/api/version`) every two seconds through each roll, and gives up on a read at 30 seconds (`scripts/probe-roll.py`). Application Insights stops recording at its daily cap in the afternoon, so these numbers come from the probe, a different instrument from the table above.

| Roll | What changed | Longest stretch with no good answer | Slowest good answer |
| --- | --- | --- | --- |
| 1.0.3.89 | listening first | about 3 minutes | 2 to 7 s, one first read of 29 s |
| 1.0.3.92 | the request pipeline reordered | 153 to 168 s | 27 to 30 s |
| 1.0.3.93 | the runtime reading | 131 to 157 s | 27 to 30 s |
| 1.0.3.94 | compiled ahead of time | 148 to 185 s | 28 to 30 s |

The stretch with no answer has held at two to three minutes through every change inside the container, `/healthz` included, which answers the moment the process listens and reads no store. Those minutes are App Service stopping the old container, pulling the new image and starting it on its one instance. A 30-second slowest answer is the probe's own limit: the edge answers 504 after 28 seconds and the probe stops waiting at 30.

## What it costs, against what it replaces

| Option | Added a month | Stretch with no answer | Why it is or is not taken |
| --- | --- | --- | --- |
| In-place roll, listening first (since 1.0.3.89) | $0 | about 3 minutes | taken |
| A second copy of each site on this plan, stopped until a roll | $0 | none, if it fits | the plan reads 92 per cent of its memory used at the median over ten days; two more containers do not fit |
| The plan moved to B2, two cores and 3.5 GB, with the second copy | about $13 | none | room for both copies; held until Steve asks for it |
| A second B1 plan for the copy during rolls | about $13 | none | the same cost, and the new copy's load stays off the live core |
| Standard tier with deployment slots | about $56 | none | four times the added cost for the same overlap |

## What would bring it back

Steve asking for zero-downtime deploys at about $13 a month, or the plan's memory falling far enough that two more containers fit. The code that was written is kept on its own branch and is not described here as if it shipped; this record links only what is on the main branch.

## Where it sits

Outside the rings: the deploy and the plan. One change reached the host, listening before the catalogue is loaded, in the API ring's startup, and it changed no port. Single responsibility: the host says when it is ready, and the roll, whichever one ships, decides when to move a visitor.

## Files

- [`api/TheYard.Api/Composition/Startup.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/Startup.cs): listening first, in the region `listen-first`.
- [`api/TheYard.Api/Endpoints/HealthEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Endpoints/HealthEndpoints.cs): `/readyz` and readiness on every store.
- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep): the one plan and the two sites on it.
- [`scripts/probe-roll.py`](https://github.com/SteveStout/TheYard/blob/main/scripts/probe-roll.py): the probe every roll above was read with.

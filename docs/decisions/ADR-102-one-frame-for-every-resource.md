# ADR: One frame for every resource

Status: accepted, 2026-10-18. Every Azure resource the site runs on gets the same card in the Cloud infrastructure section: two dials for the two numbers that run out first, thirty days of both with deploys and platform events marked, four question rows, and a readout. One recorder shape, one container per resource at thirty-one days, one card frame. The plan card (ADR: The App Service plan, read every minute) is the first instance; this record makes it the pattern and adds six more.

## In plain words

Each Azure resource has a page in the portal with a different layout, a different set of charts and a different idea of what matters. Reading seven of them is seven lessons. This section reads all seven the same way: the two numbers that would run out first are the dials, the month is the line, the questions are the same four, and the readout is twelve readings in the same order.

What that is worth: a reader who has understood one card has understood all of them, and the eighth resource costs an afternoon rather than a project.

## Context

The plan card proved the shape: read Azure once a minute with the containers' identity, keep one document a minute in Cosmos DB, serve from the kept copy, draw the two scarce numbers as dials and the month as a line with the deploys on it. Six more resources carry the site: the relational database, the document store, the registry that holds the images, the telemetry workspace, the email sender, and the edge in front of it all. Each had either no card or a card that read it live on every visit.

The owner's ruling, 2026-10-18: every Azure resource gets a dashboard like the plan's, above all those with compute under them, because keeping the dev side and the infrastructure side looking at the same picture is the part of the job he says is his.

## Decision

**One frame, parameterised by the resource.** `CloudCard` is one component in six parts: the identity line with its portal link, two dials, the chart, the question rows, the readout, and the absent state. A resource supplies a data shape, `CloudReport`, and the frame draws it. There are no seven copies of the layout, and a change to the frame changes every card.

```live path=src/components/admin/CloudCard/CloudCard.tsx region=cloud-frame
```

**One recorder shape, one container each.** `CloudRecorder` is one recorder that walks every resource's definition: a minute read, an hourly read (once in its hour for a source that moves no faster), the once-per-process rule from the machines recorder, and a container named for the resource from `infra/cosmos/<resource>.json`, each partitioned on the day with a thirty-one day time to live. A resource is a definition in `Cloud/ResourceCatalog.cs` (which resource, which metrics, which view), not a recorder of its own, so one card's failure is a gap on that card and the others keep reading. Seven containers rather than one, because each resource's documents have their own shape and their own index, and a window query over one resource should never scan another's minutes.

```live path=api/TheYard.Api/CloudRecorder.cs region=cloud-recorder
```

**The two dials are the two numbers that run out.** Not the two most interesting numbers and not the portal's defaults: for each resource, the two readings that would stop the site first.

- The App Service plan: processor and memory of the one B2.
- Azure SQL Database: DTU of the five, and data space of the 2 GB.
- Cosmos DB: request units a second of the 1,000 shared, and storage of the free 25 GB.
- Container Registry: storage of the 10 GB included, and pulls this minute.
- Application Insights: today's ingestion against the daily cap, and the sampling rate in effect.
- Communication Services: sends this hour, and the bounce rate.
- The Netlify edge: credits of the 300 this cycle, and the cache hit rate.

**Thresholds per resource, held by a test, sustained before they speak.** Each card's thresholds are its own numbers in `CloudThresholds`, held fifteen minutes by the plan's rule (`PlanThresholds.ToneOf`), and `CloudThresholdTests` holds all of them. A capped hour on the telemetry workspace is red at once rather than after a hold, because a capped hour is data already lost; the workspace's Usage table moves hourly, so the telemetry card and the edge keep one document an hour.

```live path=api/TheYard.Api/CloudThresholds.cs region=cloud-thresholds
```

**Cost on the readout comes from the cost card, never typed.** Each readout's cost line reads the kept cost days for that resource (ADR: What Azure charges). A figure typed into a card goes stale the next time the bill changes.

**Every resource name links to its portal blade.** The tenant and the resource id come from the Azure read. The Netlify card links to the project page the same way. The link opens with the viewer's own sign-in, which the card says beside it.

**The Netlify card names its own retirement.** The edge retires at the registrar transfer, so its fourth question is "Is it still needed?" and the answer is a date. A card for a resource that is going away should say so rather than look permanent.

**A secret the card needs is a GitHub secret, never in the repo.** The Netlify read needs a personal access token; it is `NETLIFY_TOKEN` on the repository, carried into the apps as a setting the way the operator's key is (ADR: The code is public and the secrets are not). With no token set, the card says so in a sentence.

## What this cost

Six containers on the existing account, inside the free tier; one shared frame; one recorder walking six definitions beside the plan's own; seven threshold sets; and one role assignment. The containers' identity already held Reader on the resource group, which covers every resource's metrics and Activity Log, and AcrPull on the registry, which covers its catalogue; the one it lacked was Log Analytics Reader on `log-theyard-ss`, because Reader cannot run the query the telemetry card reads the Usage table with. That was assigned on the owner's word at 12:13 CDT on 18 October, at no charge, and every assignment and container was read back; the screenshots are below. No new Azure resource. The six cards shipped as one deploy after the plan card had been reviewed live.

## Alternatives

**Seven cards, seven layouts, each tuned to its resource.** Rejected: the portal already does that, and the reader has to learn seven pages. The value of the section is that the cards are the same.

**One container for all seven.** Rejected: one index policy cannot serve seven document shapes well, and a window query over the plan would read the registry's minutes to skip them.

**Read the portal's defaults as the dials.** Rejected: the portal's first chart for a SQL database is DTU and for a registry is storage, but for Cosmos DB it is total requests, which never runs out. The dial is the number with a ceiling.

## Where it sits

`CloudRecorder`, `CloudReader` and `CloudThresholds` in `TheYard.Api`, beside the plan's own files, which are now the first instance of each, and each resource's reads and view in `TheYard.Api/Cloud/`. One port in `TheYard.Application`, `IResourceHistory`, because the six shapes are one shape (a minute of named readings, an hour of named facts), and its Cosmos implementation in `TheYard.Infrastructure.Cosmos`. The frame in `src/components/admin/CloudCard` and its parts beside it; the seven data shapes are built on the server and arrive as one `CloudReport`, because the server owns every derived fact, and `src/lib/cloud.ts` holds only the frame's geometry. Domain knows nothing of any of it.

What would change it: a resource whose two scarce numbers are not a pair of percentages, at which point the dial takes a unit and a ceiling rather than a fraction. The frame already draws the absolute under the reading for that reason.

## Files

- [`src/components/admin/CloudCard/`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/CloudCard) - the frame; its parts in `CloudDial`, `CloudChart`, `CloudRows` and `CloudReadout` beside it, and [`CloudResourceCard`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/CloudResourceCard) for the six
- [`src/lib/cloud.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/cloud.ts) - the frame's geometry and the words a tone prints in
- [`api/TheYard.Api/Cloud/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Cloud) - the six data shapes: each resource's reads and its view
- [`api/TheYard.Api/CloudCards.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CloudCards.cs) - what a resource card is
- [`api/TheYard.Api/CloudRecorder.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CloudRecorder.cs) - the recorder shape
- [`api/TheYard.Api/CloudReader.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CloudReader.cs) and [`api/TheYard.Api/ResourceView.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/ResourceView.cs) - the windows and the markers, once
- [`api/TheYard.Api/CloudThresholds.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CloudThresholds.cs) - every card's numbers
- [`api/TheYard.Api/Composition/CloudRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/CloudRegistration.cs) - the six definitions, registered beside the plan's
- [`infra/cosmos/`](https://github.com/SteveStout/TheYard/tree/main/infra/cosmos) - `sqldb.json`, `cosmosdb.json`, `registry.json`, `insights.json`, `mail.json`, `edge.json` beside `plan.json`, each named for the card's address
- [`api/TheYard.Tests/CloudThresholdTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CloudThresholdTests.cs) - the numbers above, held
- [`api/TheYard.Tests/CloudViewTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CloudViewTests.cs) - fixed minutes in, fixed report out, per resource
- [`tests/e2e/cloud.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/cloud.spec.ts) - seven cards render in the one frame; that every portal link carries the id the read returned is held in `CloudViewTests`, because a browser run has no Azure read to compare against

Configuration, read back through the runner on 18 October: every role assignment the identity holds, and the six containers.

![The role assignments id-theyard-ss holds, read back: Reader on the resource group, AcrPull on the registry, Log Analytics Reader on log-theyard-ss, among others](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/cloud-roles.svg)

![The six resource containers on cosmos-theyard-ss, read back: each partitioned on /day with thirty-one days to live](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/cloud-containers.svg)

The section: seven resources to seven containers to one frame.

[![Cloud infrastructure: seven resources read by two recorders on the SQL site, kept in seven containers, served by two routes and drawn in one frame](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/cloud-infrastructure.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/cloud-infrastructure)

## Addendum, 2026-10-18: an hourly card speaks on its newest reading

The fifteen-minute hold above stopped being true for two cards in 1.0.3.175. Application Insights and the Netlify edge keep one document an hour, because their sources move no faster, so fifteen minutes of readings over a line could never be kept and both cards could never turn amber or red. The first live hour showed the cost: the telemetry workspace had ingested 0.098 GB of its 0.1 GB daily cap, and the dial said information. An hourly card now takes its tone from its newest reading alone, which already stands for an hour; every minute-by-minute card keeps the hold. `CloudThresholdTests` holds the case as it was read.

```live path=api/TheYard.Api/ResourceView.cs region=hourly-tone
```

## Addendum, 2026-10-18: the edge builds from its own branch

The edge card reads Netlify's deploy log, and on the first day it read as a column of failures. None was a failure: every push to main started a Netlify build, `netlify.toml`'s ignore rule found no change under `edge/` and cancelled it, and Netlify files a cancelled build as an error, "Canceled build due to no content change". From 1.0.3.175 two things hold. The card names each deploy by Netlify's own reason, live, skipped or failed, so a skipped build is never shown as failed (`NetlifyEdge.Outcome`, held by `CloudViewTests`). And Netlify builds from an `edge` branch that `.github/workflows/edge-branch.yml` moves to main only when `edge/` or `netlify.toml` changes, on the owner's word on 18 October. Once the project's production branch is `edge` in Netlify's settings, a change made in the Netlify account that owns the project, a push that does not touch the edge starts no build at all; until then, and after, the ignore rule stays as the second guard.


## Addendum, 2026-10-18: a list is a table, and each resource says what it is

From 1.0.3.176 a row that answers with a list draws it as a table under the answer, through the Admin tab's one table component (ADR-083), and every table names each resource's type. The owner's reading of the plan card was the reason: "4 apps, running" followed by ".NET 10 Azure SQL" on one line read as though the database ran on the plan. It never has; the plan carries four web apps, and the SQL database and the Cosmos DB account are resources of their own with cards of their own. The plan's table says so in a column, Talks to, and the other cards' tables say Cosmos DB container, image repository, web app or SQL database beside each name. A row carries its table as `CloudRow.Table`, empty and never null when it has none.

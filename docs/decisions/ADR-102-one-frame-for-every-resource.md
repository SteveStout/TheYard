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

```live path=src/components/admin/CardShell/CardShell.tsx region=card-shell
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

`CloudRecorder`, `CloudReader` and `CloudThresholds` in `TheYard.Api`, beside the plan's own files, which are now the first instance of each, and each resource's reads and view in `TheYard.Api/Cloud/`. One port in `TheYard.Application`, `IResourceHistory`, because the six shapes are one shape (a minute of named readings, an hour of named facts), and its Cosmos implementation in `TheYard.Infrastructure.Cosmos`. The frame in `src/components/admin/CloudCard` (`CardShell` from 1.0.4.0, the addendum below) and its parts beside it; the seven data shapes are built on the server and arrive as one `CloudReport`, because the server owns every derived fact, and `src/lib/cloud.ts` holds only the frame's geometry. Domain knows nothing of any of it.

What would change it: a resource whose two scarce numbers are not a pair of percentages, at which point the dial takes a unit and a ceiling rather than a fraction. The frame already draws the absolute under the reading for that reason.

## Files

- [`src/components/admin/CardShell/`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/CardShell) - the frame; its parts in `CloudDial`, `CloudChart`, `CloudRows` and `CloudReadout` beside it, and [`CloudResourceCard`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/CloudResourceCard) for the six
- [`src/lib/cloud.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/cloud.ts) - the frame's geometry and the words a tone prints in
- [`api/TheYard.Api/Cloud/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Cloud) - the six data shapes: each resource's reads and its view
- [`api/TheYard.Api/CloudCards.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CloudCards.cs) - what a resource card is
- [`api/TheYard.Api/CloudRecorder.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CloudRecorder.cs) - the recorder shape
- [`api/TheYard.Api/CloudReader.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CloudReader.cs) and [`api/TheYard.Api/ResourceView.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/ResourceView.cs) - the windows and the markers, once
- [`api/TheYard.Api/CloudThresholds.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CloudThresholds.cs) - every card's numbers
- [`api/TheYard.Api/Features/Admin/Cloud/Module.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/Module.cs) - the six definitions, registered beside the plan's
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

Addendum, 19 October 2026 (1.0.3.186): the plan's table no longer describes an app from its name. Its Type, Runs and Answers to columns are Azure's own read of each site, taken in the same hourly read as its state and Always On: the site's kind, its linuxFxVersion (the image and tag) and its hostNames. Talks to is gone with the guess it was built on; which store a request reaches is the Hosting page's and the store cards' to say. An hour kept before the reader read these fields says "not read" rather than a guess. The owner asked for it on 19 October: the card "should always pull from real data from the api".

## Addendum, 2026-10-18: the readers are tested, and a good deploy is green

From 1.0.3.173 to 1.0.3.176 every push to main deployed green and ran CI red, and GitHub mailed the owner a failed run on each one. No test failed. The coverage step failed: 86.8 per cent of lines against its floor of 90 and 66.7 per cent of branches against 75, because the new readers behind these cards talk to Azure Resource Manager, Azure Monitor, Log Analytics, the registry and Netlify, and the CI runner has no credential to reach any of them. The floor stays where it is. Lowering it would hide the same gap in every other project.

What changed instead. Seven readers each held a static `HttpClient` of their own, which is seven connection pools for the same few hosts. From 1.0.3.177 they share one, `OutboundHttp`: one `SocketsHttpHandler` that retires each connection after two minutes, so a changed DNS answer is picked up, and a cheap client per call that carries the caller's timeout. The same file is the suite's seam. A test routes its own flow's calls to recorded answers, which `CloudReadersTests` holds in the shapes each service gives. Every reader then runs its real request building, parsing and failure paths with no network: a metric Azure refuses at one grain, a Log Analytics query answered 403, and a Netlify token that cannot see the site.

```live path=api/TheYard.Api/OutboundHttp.cs region=outbound-http
```

## Addendum, 2026-10-20: the database cards read what they typed

From 1.0.3.195 the route asks one handler, `ReadCloudCardHandler`, over an `ICloudCards` port that `CloudReader` implements (ADR: The onion cut into vertical slices, rule 7). The Azure SQL Database card's last question names the tier the database's own record gives instead of saying Basic, its detail names the 30-day DTU 95th against the red line instead of the next tier's price, and the sessions sparkline, which was scaled to a typed ceiling of 300, became the workers share Azure reports as a percentage. The Cosmos DB card's RU dial and sparkline are read against the account's provisioned throughput, read each hour from the database's throughput record, where they were read against a typed thousand; its last row names the 30-day 95th against that figure and the free tier as the account record says it, and the price of 400 provisioned RU/s is gone. The second sparkline is the normalized RU share Azure reports, where it was latency against a typed 50 ms. One figure stays typed, with its reason on the constant: the free tier's 25 GB of storage, which no record Azure returns for the account states. Every card's threshold sentence and dashed line is written from the levels its tones use, and the sentence a card prints before its first minute names the recording site by its configured host.

## Addendum, 2026-10-20: the registry and the edge

From 1.0.3.196 the Container Registry card's storage dial is read against the registry's own Size limit, which the hourly read now asks of the registry's `listUsages`, where it was read against a typed 10 GB; its last question names the tier the registry's record gives, and its detail names the 30-day storage 95th against that limit instead of Standard's price. The pulls dial keeps one typed figure, Basic's 1,000 reads a minute, with its reason on the constant: the usages name the size and the webhooks, not the read rate. The pulls sparkline is scaled to that same figure, where it was scaled to a separate 50. The Netlify edge card's header names the plan Netlify reports and says so when it is not read, its credits figure comes from the one constant the dial uses, and its threshold sentence and dashed line are written from the levels its tones use. `CloudViewTests` and `CloudThresholdTests` hold it.

## Addendum, 2026-10-20: the insights and mail cards, and the names

From 1.0.3.197 the Application Insights card's cost row names the gigabytes past the free allowance and the month's figure from the bill, where it multiplied by a typed price a gigabyte; the allowance itself stays typed, with its reason on the constant, because no record the workspace returns states it. The Communication Services card reads its sends dial against the limit of the sender its domain record names, the Azure-managed one or a custom domain, where it always read against the first; both limits stay typed, with the reason on the constant, because no record the service returns states its sending limit, and the sparklines are scaled to the same limit a minute. Every resource the cards read is found by its type and a name from configuration (`Azure:Resources` in appsettings.json, `Shared/AzureResources.cs`), and `LiteralScanTests` fails the build on a name of this deployment written in the code. The resource group is App Service's own `WEBSITE_RESOURCE_GROUP` where the platform sets it. The edge card's link is the Netlify site record's own `admin_url`, and its retirement date is `Edge:Retires` in configuration. The SQL card no longer lists the server's paused sibling database: it was a second resource on a card about one, and nothing reads it.

## Addendum, 2026-10-20 (1.0.4.0): the frame is the CardShell

`CloudCard` became `CardShell` (`src/components/admin/CardShell/`), the one component the CardShell record's slots describe (ADR: The onion cut into vertical slices). The frame's six parts are unchanged in kind; what the shell adds is a tone chip beside the title from the card's one tone, the spec's source as a chip, the line under each dial that names its marks, each sparkline in its dial's colour with its newest figure, the thresholds as chips where the "Normal:" sentence was, the chart's legend as chips, a source under every summary line and row, and a retry line that counts down from the tab's own tick. The live block above now shows the shell.


## Addendum, 2026-10-20: the edge card scores what the edge is meant to keep

Until today the Netlify edge card's "What is cached?" row scored four addresses: `/`, `/favicon.svg`, `/og.png` and `/api/version`. The app told the edge to keep none of them (three said `no-cache`, and `/favicon.svg` is not served at all), so the share could not read above zero, and the misses tone (amber 50, red 80) turned a correct cache red. Steve read it on his phone at 16:06: "0 % of the sampled pages, warning".

From this change the hourly sample scores only addresses the app lets the edge keep: one hashed bundle file read from the built page at startup (`NetlifyEdge.BundleFileOf`; on a machine with no build it is skipped, and the card says so), `/og.png`, `/api/facets` and `/api/docs/readme` (ADR: Cache headers, the addendum of 20 October). Zero now means the edge is broken, and anything else is a real number. The page and `/api` are still asked once an hour, only to name the web app that answered each; neither is scored. The row's detail names the list the reader actually sampled and each address's `Cache-Status`, written by the reader into the hour's facts, never typed in the card. The tone stays on misses at the same levels, over the cacheable sample only. `EdgeKeepsTests` holds every sampled address to the edge's rule; `CloudReadersTests` holds the share and the per-address statuses.

The "Is it still needed?" row is unchanged; its date already comes from `Edge:Retires`.

## Addendum, 2026-10-20 (1.0.4.2): the sample asks twice

1.0.4.1 rolled at 16:59 CDT and the card's first sample read 0 % in red, every address `"Netlify Edge"; fwd=miss; fwd-status=200; stored`. The sample ran as the new container started, after the deploy had purged the edge and before the warm-up reached the node the app is served from, so it measured the purge. Read from Missouri at 17:01 on both Yard names, the bundle file, `/og.png`, `/api/facets`, `/api/docs/readme` and a diagram were all hits on the second fetch, with `/api/version` and the resume still forwarded every time. From 1.0.4.2 the sample asks each address twice and scores the second answer, the way that verification reads it: the first ask fills a copy the edge does not have, the second reads whether it kept it. A deploy no longer turns the card red for an hour, and zero still means the edge keeps nothing. `CloudReadersTests` holds the share.

## Addendum, 2026-10-20 (1.0.4.6): one sample for the dial and the detail

At 19:52 Steve read the edge card on his phone as RED, "0 % of 4 cacheable", while the row's own detail under it listed all four addresses as `hit`; read live at 19:53 on both Yard names, the newest sample was four hits and the kept minute behind the dial said 0. The dial and the tone read the kept minute, which carries the sample taken before it, an hour or more behind, and a roll in between leaves it older still; the detail read the newest hour's statuses. From 1.0.4.6 the dial, its tone, the row's answer and the readout read the share back from the newest hour's per-address statuses (`NetlifyEdge.ShareOf`), the same sample the detail prints, so the two can no longer disagree. The kept minute still feeds the sparkline and the chart. `CloudThresholdTests` holds a 0 minute under a four-hit sample reading 100 and green.

## Addendum, 2026-10-20: a seventh card, The Nightly Function

From this change the section holds seven resource cards after the plan: the nightly Function, the one resource the site watches that lives outside its own resource group, joins as one more definition in `ResourceCatalog.Cards`, with a second `ResourceDirectory` over the Function's group, its own `nightly` container like every card, and a year-long run log beside it. A card can now name more than one line of the bill (`ResourceCard.CostNames`, filled into `ResourceCardInputs.CostLines`). The record of the card is its own (ADR: The Nightly Function on the Admin tab).

## Addendum, 2026-10-21 (1.0.4.10): an hourly card reads with no keep

The recorder skipped a card whose keep was missing, minutes and hour alike. From 1.0.4.10 an hourly card's hour is still read once in its hour and held in the recorder's memory, and the reader draws the card from it until the keep exists; a minute card is skipped as before, and no card's minutes are read without a keep. The live-read line on every card now says why it has no minute ("not read yet: no minute kept yet", or the keep's own reason). The nightly card is the first to need it (ADR: The Nightly Function on the Admin tab, the addendum of 1.0.4.10).

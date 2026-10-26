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

```live path=api/TheYard.Api/Features/Admin/Cloud/CloudRecorder.cs region=cloud-recorder
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

```live path=api/TheYard.Api/Features/Admin/Cloud/CloudThresholds.cs region=cloud-thresholds
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
- [`api/TheYard.Api/Features/Admin/Cloud/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Features/Admin/Cloud) - the six data shapes: each resource's reads and its view
- [`api/TheYard.Api/Features/Admin/Cloud/CloudCards.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/CloudCards.cs) - what a resource card is
- [`api/TheYard.Api/Features/Admin/Cloud/CloudRecorder.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/CloudRecorder.cs) - the recorder shape
- [`api/TheYard.Api/Features/Admin/Cloud/CloudReader.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/CloudReader.cs) and [`api/TheYard.Api/Features/Admin/Cloud/ResourceView.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/ResourceView.cs) - the windows and the markers, once
- [`api/TheYard.Api/Features/Admin/Cloud/CloudThresholds.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/CloudThresholds.cs) - every card's numbers
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

```live path=api/TheYard.Api/Features/Admin/Cloud/ResourceView.cs region=hourly-tone
```

## Addendum, 2026-10-18: the edge builds from its own branch

The edge card reads Netlify's deploy log, and on the first day it read as a column of failures. None was a failure: every push to main started a Netlify build, `netlify.toml`'s ignore rule found no change under `edge/` and cancelled it, and Netlify files a cancelled build as an error, "Canceled build due to no content change". From 1.0.3.175 two things hold. The card names each deploy by Netlify's own reason, live, skipped or failed, so a skipped build is never shown as failed (`NetlifyEdge.Outcome`, held by `CloudViewTests`). And Netlify builds from an `edge` branch that `.github/workflows/edge-branch.yml` moves to main only when `edge/` or `netlify.toml` changes, on the owner's word on 18 October. Once the project's production branch is `edge` in Netlify's settings, a change made in the Netlify account that owns the project, a push that does not touch the edge starts no build at all; until then, and after, the ignore rule stays as the second guard.


## Addendum, 2026-10-18: a list is a table, and each resource says what it is

From 1.0.3.176 a row that answers with a list draws it as a table under the answer, through the Admin tab's one table component (ADR-083), and every table names each resource's type. The owner's reading of the plan card was the reason: "4 apps, running" followed by ".NET 10 Azure SQL" on one line read as though the database ran on the plan. It never has; the plan carries four web apps, and the SQL database and the Cosmos DB account are resources of their own with cards of their own. The plan's table says so in a column, Talks to, and the other cards' tables say Cosmos DB container, image repository, web app or SQL database beside each name. A row carries its table as `CloudRow.Table`, empty and never null when it has none.

Addendum, 19 October 2026 (1.0.3.186): the plan's table no longer describes an app from its name. Its Type, Runs and Answers to columns are Azure's own read of each site, taken in the same hourly read as its state and Always On: the site's kind, its linuxFxVersion (the image and tag) and its hostNames. Talks to is gone with the guess it was built on; which store a request reaches is the Hosting page's and the store cards' to say. An hour kept before the reader read these fields says "not read" rather than a guess. The owner asked for it on 19 October: the card "should always pull from real data from the api".

## Addendum, 2026-10-18: the readers are tested, and a good deploy is green

From 1.0.3.173 to 1.0.3.176 every push to main deployed green and ran CI red, and GitHub mailed the owner a failed run on each one. No test failed. The coverage step failed: 86.8 per cent of lines against its floor of 90 and 66.7 per cent of branches against 75, because the new readers behind these cards talk to Azure Resource Manager, Azure Monitor, Log Analytics, the registry and Netlify, and the CI runner has no credential to reach any of them. The floor stays where it is. Lowering it would hide the same gap in every other project.

What changed instead. Seven readers each held a static `HttpClient` of their own, which is seven connection pools for the same few hosts. From 1.0.3.177 they share one, `OutboundHttp`: one `SocketsHttpHandler` that retires each connection after two minutes, so a changed DNS answer is picked up, and a cheap client per call that carries the caller's timeout. The same file is the suite's seam. A test routes its own flow's calls to recorded answers, which `CloudReadersTests` holds in the shapes each service gives. Every reader then runs its real request building, parsing and failure paths with no network: a metric Azure refuses at one grain, a Log Analytics query answered 403, and a Netlify token that cannot see the site.

```live path=api/TheYard.Infrastructure/Shared/Http/OutboundHttp.cs region=outbound-http
```

## Addendum, 2026-10-20: the database cards read what they typed

From 1.0.3.195 the route asks one handler, `ReadCloudCardHandler`, over an `ICloudCards` port that `CloudReader` implements (ADR: The onion cut into vertical slices, rule 7). The Azure SQL Database card's last question names the tier the database's own record gives instead of saying Basic, its detail names the 30-day DTU 95th against the red line instead of the next tier's price, and the sessions sparkline, which was scaled to a typed ceiling of 300, became the workers share Azure reports as a percentage. The Cosmos DB card's RU dial and sparkline are read against the account's provisioned throughput, read each hour from the database's throughput record, where they were read against a typed thousand; its last row names the 30-day 95th against that figure and the free tier as the account record says it, and the price of 400 provisioned RU/s is gone. The second sparkline is the normalized RU share Azure reports, where it was latency against a typed 50 ms. One figure stays typed, with its reason on the constant: the free tier's 25 GB of storage, which no record Azure returns for the account states. Every card's threshold sentence and dashed line is written from the levels its tones use, and the sentence a card prints before its first minute names the recording site by its configured host.

## Addendum, 2026-10-20: the registry and the edge

From 1.0.3.196 the Container Registry card's storage dial is read against the registry's own Size limit, which the hourly read now asks of the registry's `listUsages`, where it was read against a typed 10 GB; its last question names the tier the registry's record gives, and its detail names the 30-day storage 95th against that limit instead of Standard's price. The pulls dial keeps one typed figure, Basic's 1,000 reads a minute, with its reason on the constant: the usages name the size and the webhooks, not the read rate. The pulls sparkline is scaled to that same figure, where it was scaled to a separate 50. The Netlify edge card's header names the plan Netlify reports and says so when it is not read, its credits figure comes from the one constant the dial uses, and its threshold sentence and dashed line are written from the levels its tones use. `CloudViewTests` and `CloudThresholdTests` hold it.

## Addendum, 2026-10-20: the insights and mail cards, and the names

From 1.0.3.197 the Application Insights card's cost row names the gigabytes past the free allowance and the month's figure from the bill, where it multiplied by a typed price a gigabyte; the allowance itself stays typed, with its reason on the constant, because no record the workspace returns states it. The Communication Services card reads its sends dial against the limit of the sender its domain record names, the Azure-managed one or a custom domain, where it always read against the first; both limits stay typed, with the reason on the constant, because no record the service returns states its sending limit, and the sparklines are scaled to the same limit a minute. Every resource the cards read is found by its type and a name from configuration (`Azure:Resources` in appsettings.json, `Shared/Resources/AzureResources.cs`), and `LiteralScanTests` fails the build on a name of this deployment written in the code. The resource group is App Service's own `WEBSITE_RESOURCE_GROUP` where the platform sets it. The edge card's link is the Netlify site record's own `admin_url`, and its retirement date is `Edge:Retires` in configuration. The SQL card no longer lists the server's paused sibling database: it was a second resource on a card about one, and nothing reads it.

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

## Addendum, 2026-10-21 (1.0.4.12): one sample everywhere on the edge card

The 1.0.4.6 addendum left the sparkline and the chart on the kept minute, and the minute was one sample behind by construction: the recorder asked an hourly card for its minute before its hour, and the edge's minute carries the cache share the last hour sampled. Read live on both names on the morning of 21 October, the dial said 100 beside 75 on the sparkline and the chart's newest point. Of the two fixes on the table, the minute reading the newest hour's statuses or the recorder asking the hour first, this takes the second: it is one rule for every hourly source, so any minute that carries what its hour read is in step, and it needs no copy of the share's arithmetic in the edge's minute. From 1.0.4.12 `CloudRecorder` asks an hourly card for its hour, then its minute (a minute card still reads minutes first, and a failed hour still lets the minutes be kept); and the edge card draws the sparkline's newest value and the chart's newest point from the same newest-hour share the dial reads (`EdgeCard.SameSample`), so a minute kept before this version, or one kept in a transition, can no longer disagree with the dial on screen. Earlier points stay the kept minutes. `CloudThresholdTests` holds the dial, the sparkline and the chart equal under a minute that says 0 and a sample of four hits, and the recorder's order with an hourly card whose minute carries its hour's sample.

## Addendum, 2026-10-21 (1.0.4.14): which rows lift a card

A row lifts its card's tone when it states a fault: a throttled request, a stopped app, a failed pull with its count, a capped hour, the edge past its retirement date. A row that states a fact a reader should know and nothing is wrong (a bill past the free allowance, no cap chosen, a planned or unplanned Azure event that changed nothing, days left before a date) reads info and leaves the card alone. A live read that is old is the frame's unread rule, not a warning line. And no line or row is given a source it did not name: each card's list says where each is read (ADR: The onion cut into vertical slices, the addendum of 1.0.4.14).

## Addendum, 2026-10-21 (1.0.4.16): the registry's images, one row each

The registry card's "What does it hold?" table listed each repository with its tag count and nothing a reviewer could act on. From 1.0.4.16 it is one row an image: the image, what it is, which web app runs it and at which live tag, its tags and untagged manifests, its size with the untagged bytes beside it, and its newest push in Central time. What an image is comes from one typed map in the API (`AcrCard.Descriptions`, read against deploy.yml, deploy-render.yml and the two Dockerfiles), because no record states it; an image the map does not name shows "no description" and the row warns. Which web app runs an image is matched on the image name in each app's linuxFxVersion, never the app's name, and is read from the plan card's own newest hour, the one read of every app's configuration, so nothing asks Azure a second time; an app on another registry is not counted. The sizes and the newest push are the registry's own manifest list (`AcrCatalog.Image`). When the API's image and the renderer's run different live tags the row warns and names both, because deploy-render.yml can land after deploy.yml and the pages are drawn in the browser until they match. Two columns were considered and left out: pulls per image, because the registry's pull metrics are not read here split by repository and an estimate is not a reading; and the commit beside the live tag, because no source the card reads names it. `CloudViewTests` holds the six cases and the sums from a recorded manifest list.


## Addendum, 2026-10-21 (1.0.4.23): one tone rule for information

The review of 1.0.4.11 to 1.0.4.18 read three treatments of one fact, the site's identity holding no role that reads something: the Insights card's dropped row turned the card amber for it, the registry and nightly cards said it as information. And the registry card's image table turned amber for an image with no line in `AcrCard.Descriptions`, which is a to-do for the developer, not a fault in Azure.

From 1.0.4.23 the rule is one: information never lifts a card. The Insights dropped row is info when the workspace refuses the query; the registry's "no description" is info, said in the row's detail with where a description goes, and only the two images of one build running different tags stays a warning. The mail card's "What did Azure do to it?" row takes its tone from `PlanThresholds.EventsTone` like the SQL and plan cards, so an unplanned event alarms there too. A tier the Communication Services record does not state leaves the spec line (it read "TIER NOT READ UNITEDSTATES") and is said on the readout's Tier line, "tier not stated by the record". The registry's table, the hourly catalogue, says its own age in the row's detail beside the per-minute Live read. Two notes from the same review: `CloudRecorder`'s log line names which read failed, the minute's or the hour's (`CloudRecorder.PartOf`), and `CloudReader` asks an unkept card's source live at most once a minute past its 61 minutes (`CloudReader.Due`), where it re-read the whole source on every 30 second poll. `CloudThresholdTests` holds the tones, the Tier line, the part and the minute; `CloudViewTests.Registry` holds the information tone, the catalogue's age and the Central hour on both sides of each daylight-saving change, by the hour itself.

## Addendum, 2026-10-21 (1.0.4.24): eight cards, nine containers

The record counts seven resources and seven containers. Since the Nightly Function's card (ADR: The Nightly Function on the Admin tab) there are eight cards, and the nightly card's run log and firings make nine containers beside the plan's. The section's drawing, `docs/images/cloud-infrastructure.svg`, still draws the seven; its caption on the Diagrams page says so until it is redrawn.

## Addendum, 2026-10-22 (1.0.4.25): every card calculates a status, and a card judges the site's own traffic

The count of the addendum of 1.0.4.24 stopped being true here: the nightly card's minutes and runs never got containers of their own and now live in the plan container (ADR: The Nightly Function on the Admin tab, the addendum of 1.0.4.25), so eight cards keep seven card containers beside the plan's, which the nightly card shares.

The owner's rule, 22 October 04:18 CDT: "All infrastructure cards should calculate a status." Three cards under Cloud infrastructure drew no dot on the rail: Azure's view of the container, What the machines are doing and Memory against traffic, because they draw their own bodies and the rail glanced only the plan and the seven cards in the shared frame. From 1.0.4.25 the rail glances all eleven. The list is `GLANCED` in `src/lib/railGlances.ts`; the three that draw their own body are judged there from the same ranges their pills are painted with (`ranges.ts`): Azure's view by the site's state, the certificate's days and the SQL store's share of its tier; the machines by the container's newest memory against its limit and the SQL store's own memory; memory against traffic by the server's own verdict. `railGlances.test.ts` holds `GLANCED` equal to the rail's Cloud infrastructure section, so a twelfth card cannot ship without a status.

A row that is not read now never colours a card. `CardFrame.Complete` skips any row whose answer starts "not read" when it takes the card's tone, so a question Azure would not answer says so with its reason and the card stays the colour of what was read (`CardFrameTests.A_line_that_is_not_read_says_so_and_never_colours_the_card`). A dial keeps its tone: a dial whose newest minute is missing still holds the tone its sustained minutes earned, which is a reading, not a gap.

The Cosmos DB card read amber at 0 % on the rail. Read live on 22 October at 04:40 CDT on both names: RU/s 4 of 1,000, storage 2.29 of 25 GB, every dial green, and the throttling row "941 throttled", warning, which lifted the card (the 1.0.4.14 addendum's rule, which rows lift a card). Azure Monitor split by container the same morning, over a window a few minutes later, counted 943: 825 on the live tests' copies (`tests-logs` 283, `tests-activity` 138, `tests-users` 108, `tests-vehicles` 106, `tests-bids` 103, `tests-photos` 74, `tests-resets` 11, `tests-machines` 2), which share the database's thousand RU/s with the site, and 118 on the site's own containers, of about 140,000 requests to them; the busiest were `activity` (31) and `logs` (30), in the hours the gate's suites ran. The card was charging the gate's tests to the site, and calling any 429 at all a warning.

From 1.0.4.25 the card reads two more series, the requests and the throttles of `CollectionName sw 'tests-'`, and judges the site by the difference: the throttling row turns amber when the site's own throttles reach 1 % of its own requests, or when there are throttles and no requests read to weigh them against (`CosmosCard.ThrottledAmberShare`; Azure's guidance reads 1 to 5 % of requests answered 429 and retried by the SDK as healthy), says the share, and names the tests' throttles on a line of their own. Azure Monitor's answer to a dimension filter is one series a value, so `PlanShapes.Series` now adds the series of a count or a total, and a filtered request asks for up to a hundred series. The same day's counts give 118 throttled, 0.08 % of the site's requests: the row is green and the card is green. "Requests, hour" on the readout is the site's own too.

What is not changed: the live tests still share the production database's throughput, and their bursts are when the site's own 118 happened. Moving the tests' copies to a database of their own costs 400 RU/s outside the free tier, which this addendum does not spend.

## Addendum, 2026-10-24 (1.0.4.52): the job-hunt share and the blob service

Two more cards in the frame, after Application Insights in the rail, both over the job hunt's storage account (stjobhuntss in RG-JOBHUNT-SS, named by the nightly settings), from the approved mockups J:\mockups\TheYard-Storage-*. They are definitions in ResourceCatalog like every card here, read by the one recorder and served by the one route, `GET /api/admin/cloud/{share|blob}` through ReadCloudCardHandler; their minutes are kept in the plan container under kinds of their own (share-*, blob-*), because the shared database is at its twenty-five containers. The hourly reads are StorageSource's: the share's record with its usage, the alert rule's own threshold, the transactions by ApiName and by authentication, thirty days of transactions by hour, the account's days on the bill, the containers and the blob listing (StorageReadings.cs). Every request goes through the host's outbound client; nothing here opens a socket of its own.

The share card. Dial one is the transactions of the last sixty minutes against the alert's line, read from `alert-jobhunt-share-transactions` and never typed: amber at half, red at the line, which is the same line that emails. Dial two is the bytes used of the quota, amber 80 %, red 90 %. Between them, listings, reads and writes for sixty minutes. The chart is transactions a day for thirty days against the line times 24. Who is reading it: the table by API for the last hour and yesterday; who, by name, needs the share's diagnostic log, and the row says so while it is off. What does it cost: today, yesterday, the month and the last seven days of the account's line. Is anything walking it: listings this hour against the 5th percentile of the last seven days' hours, a walker being four times that and over a thousand. What is on it: Azure's file count and bytes. The readout names who mounts it and who reads it on a timer from the authentication split (the laptop by SAS, the Function by the account key).

The blob card. Dial one is the blog's pictures against a line that is not a quota (Admin:BlobPicturesAmberBytes, 1 GB; red at Admin:BlobPicturesRedBytes, 1.8 GB). Dial two, the share of picture reads the host answers from memory, reads "not read" until the blog's pictures lane builds that cache. The chart is blob reads a day against Admin:BlobDailyReadLine, 24,000. The card is red at that line, or when the account or any container allows public access. The containers table prints each container's `purpose` metadata tag.

What the mockups showed and the cards do not, said on the card where it applies: the largest folders of the share (a daily census means a listing of every folder, which is the bill this record's lane just stopped; not built), the walkers file on J: (the site does not read J:), the cache's hits (not built yet), an Azure alert on blob reads (the line is the card's own), and the cost split by meter (the bill this site keeps has one line an account). The rail lists fourteen rows from BENCH_CARDS, which railGlances and the workbench tests count from; the drawing, docs/images/cloud-infrastructure.svg, draws eleven cards and CloudDrawingTests holds it to CLOUD_RESOURCES. The site's identity holds Storage Blob Data Reader on the account for the listing (granted 24 October inside the 10/22 grant).

## Addendum, 2026-10-24 (1.0.4.54): a description slot and a processes slot

The frame gains two slots, `description` (a paragraph and its source) and `processes` (a list of sections, each with its switches, its summary and schedule, four figures each with a source, a strip of thirty Central days and its run history). Only the nightly Function's card fills them: the reader puts them onto that card after the frame has built it (`NightlyProcesses`, kept two minutes so both names' polls cost one workspace read), and every other card answers them empty, so the shell draws nothing for them and no other card changed. The section's columns follow the card's own width, not the window's, because beside the rail and the readout a laptop leaves the card a phone's width.

## Addendum, 2026-10-25 (1.0.4.63): the email campaign's card

The rail's Cloud infrastructure section gains its fifteenth row, `campaign`, for the email campaign (ADR: The campaign sends, the model writes rows). It is drawn in this frame like every other card: the sends of the day against the cap of ten (would-sends while the switch is shadow) and the 30-day reply rate on the dials; sends, bounces and replies for sixty minutes; emails a day for thirty days against the cap, the days that drew replies marked; four questions (is it sending, are the leads flowing, is anyone answering with the Sunday table of the last four weeks by template, weekday, track and proof key, and is it costing anything); and a readout of the switch, the cap, the next 8 AM Central, the database, the finder and the last bounce.

What it reads is the campaign database's `card` schema, counts only, over the site's own server connection with the catalog changed and the site's own identity, which holds a database user that may select from that schema and from nothing else. So the card cannot show a name, a company, an address, a subject or a link code even by mistake, and CampaignCardTests holds the wire free of them. The switch is the Function's health route's word for `EMAIL_CAMPAIGN`, the same read the nightly card makes. The card is red at two bounces in a Central day (the campaign is then held in shadow) and when the switch is on, it is past 9 AM Central, nothing has been sent and leads with an address are ready. Every count names the day it counts from, the first email written. The drawing gains the card and its keep, `campaign-*` in the plan container.


## Addendum, 2026-10-25 (1.0.4.81): an hour kept before the bill was read

The share and blob cards keep their cost row with the hour, read from the bill the site holds in memory. The bill is read a minute after the site starts, and Cost Management can answer that read with a request to wait an hour; an hour kept in that minute read an empty bill and wrote it down as days that cost nothing. Read live at 12:47 CDT on 25 October, twenty minutes after 1.0.4.69 rolled: the share card said "$0.00 today so far, yesterday $0.00, this month $0.00", while the account's own line of the bill was $45.01 over thirty days and the cost card said Cost Management had asked the site to slow down. From 1.0.4.81 an hour kept from a bill with no day for any resource says the cost is not read yet and why, on both cards; a bill that was read and has no line for the account still says not on the bill yet. `StorageCardTests` holds it: red on the prior `CostFacts`, which wrote 0.00.

## Addendum, 2026-10-25 (1.0.4.84): the Insights cap read against what it caps

The Application Insights card read the day's ingestion from the workspace's Usage table, every app that sends to the workspace, and set it against the cap, which is the workspace's daily quota or, when the workspace sets none, the component's own. Read live at 14:2x CDT on 25 October: the workspace sets no quota, the component's cap is 0.1 GB, and the card read "173 %, 0.173 of 0.1 GB cap", red on the rail, while its own rows said no hour hit the cap and the month stood at 2.70 GB of the free 5 GB. Three apps' ingestion was being read against one component's cap. From 1.0.4.84 a component's cap is read against what that component alone sent today, the billed size of its own rows (`_ResourceId`), and the workspace's total stands beside it as a line of its own; a workspace's quota is still read against everything the workspace took. `InsightsCapTests`: red on the prior card, which read the component's cap against the workspace's total.

## Addendum, 2026-10-25 (1.0.4.85): the blob read line counts the reads it is for

The blob card's red line, 24,000 reads a day, was drawn for the blog's pictures, and it counted every GetBlob on the account. Read live at 14:2x CDT on 25 October the card stood red at "today 32,637" while its own row said the host serves no picture yet; Azure Monitor, split by authentication over the day before (J:\staging\sunday1025\blob-reads.txt), gave every GetBlob to the account key, which is the Functions host's own connection for its timers' state, its leases and its package, and none to the site's identity. From 1.0.4.85 the reads the line counts are GetBlob by anything but the account key, a filter Azure Monitor was shown to accept before it shipped, and the readout says the host's own reads are left out. `StorageCardTests` holds the filter and the words.

## Addendum, 2026-10-25 (1.0.4.87): a read that times out is a gap, not a 500

A card's route reads its resource live when the newest kept reading is stale, and a live read that fails was meant to be the recorder's gap: the card answers from what is kept and its live-read line says how old that is. The catch let every OperationCanceledException through so that a request the visitor abandoned would stop, and an HttpClient that gives up on Azure Monitor throws TaskCanceledException, which is one. On 25 October at 11:47 CDT the incidents card (1.0.4.80, read at 19:41) showed 58 TaskCanceledException and a SocketException in AzureMetrics on the one web app, the last of them at 11:47:37, and one 500 from GET /api/admin/cloud/{key} at 11:47:16. The same filter in the recorder let an hourly card's timed-out hour stop its minutes from being kept, against the comment beside it. From 1.0.4.87 both catch everything but the request's own cancellation. `CloudTimeoutTests`: red on the prior reader and recorder, a timed-out read throwing out of the route and an hour's timeout costing the minutes.

## Addendum, 2026-10-26 (1.0.4.94): the rail says what the cards know

Read live on 25 October at Steve's question "see if these are really red", four rail figures said more than their cards knew, and the share card added two windows together. Nothing here changes a threshold that emails anyone, and no setting moves.

- **The share's walking row** judged the hour against the quietest hour of the week, which is 0 every night, so any busy hour over a thousand listings read as a walker while the laptop's queue runner was shipping. It now reads the hour against the week's median busy hour (the median of the hours that listed anything) and against money: amber over 7,000 listings an hour, where listings alone add about a quarter of a dollar a day at the tier's list price, and red at the alert's own line, 20,000 transactions an hour. The row names the split by caller inside it, the mounts and chats by SAS and the Function by the account key.
- **The share's split by caller** summed two clock hours, the one before and the one under way, so it read more than twice the dial. It now reads the clock hour under way only, and says since when.
- **The share's month** reads the storage account's own line on the bill. A bill read that holds no line under the account's name says so, and never prints $0.00 for it.
- **Claude usage's rail** printed today against the week's average as a percentage, so a normal busy day read "118 %" in green beside a rail where every other figure over 100 is a limit crossed. The dial's figure is now a ratio ("1.2x the week"); its thresholds are the same. The "Plan week used" dial, which only ever said "not read", is not a reading; every card draws two dials (this record's frame, held by the e2e cloud.spec), so its place is taken by today's run count until the plan endpoint is read. The plan row still says it is not read. The first gate run of this version (40338025344) dropped the dial outright and the browser suite held the frame to two: that is why the count is there.
- **The campaign's rail** printed a bare 0. Its dial's figure is now "0 of 10", the day's emails against the cap.
- **The blob card's second dial** was a placeholder ("Served from memory: not read") for a cache that does not exist yet. It now reads the month's egress against the free 100 GB, which the readout already held; the cache dial comes back with the cache.
- **The Application Insights card** names its own share of the cap. After 1.0.4.84 reads the site's own ingestion, the site sends about 0.085 GB a day against its 0.1 GB daily cap, so the card stays amber and a deploy-heavy day caps. The row says so and names the two ways out, a higher cap or less telemetry from the Function; the cap is Steve's to change and is not changed here.

## Addendum, 2026-10-26 (1.0.4.95): the Cosmos DB card's throttling is the site's, and the first screen was its burst

Steve read the Cosmos DB card amber on 26 October at 06:37 CDT: 6,706 throttled, 2.68 % of the site's requests. The 1.0.4.25 addendum's rule stands, and this version does not move its line: read in Azure Monitor at 07:52 CDT, split by container and operation over a day, the 429s fell on reads a visitor waits for (Query 5,658 and Read 1,070 of 8,053; the Admin tab's cards reading their kept minutes, the Activity card, the catalogue's photos at 8.17 % of their reads, the vehicles and the catalogue), in the hours someone was on the site, while the recorders' own writes took 756. The card counted what it said it counted. What took the database's one partition to 100 % normalized, in 202 of the last 360 minutes, was a burst: the first screen's reading rebuilt every fifteen seconds, and each rebuild ran a grouped query over a whole day of the machines container, about 450 request units in one read, nearly half of a second's thousand. From this version the first screen folds its day from a ten-minute tail and reads it whole only every thirty minutes (`PulseDay`), so the day it answers is the same and the read costs a few request units. `PulseDayTests` holds the tail, the fold against a whole read, and the whole read again after the interval. The reading the card took before this version was right; what stopped being true is the query that made it amber.

## Addendum, 2026-10-26 (1.0.4.97): each series carries its own lines, each point its peak

Until this version a card's chart carried one red line for the whole chart, the first dial's, so the plan's chart drew CPU's red 80 % across memory too, whose red is 85, and memory's amber 70 nowhere. And a chart of seven or thirty days carried each bucket's average only, so a minute that touched a line was averaged away. From this version the wire says which line is whose: each series carries its own amber and red, the levels its own dial or row turns at (the plan's CPU 60 and 80, memory 70 and 85; each resource card's two dials' levels; the share's transactions and quota; the campaign's and the mail's caps; the nightly grant), and each point carries its bucket's peak beside its average. A bucket's tally, the Cosmos DB card's throttled requests and the campaign's replies, is a mark of its own kind, `count`, where it was an `event` like an Azure restart. And an Activity Log entry is an `event` only when it is a restart or maintenance; any other, a configuration write above all, is a `change` (`CloudMarks.KindOf`). Read live on 26 October, the plan's seven days held 228 full-height red marks, 221 of them "UpdateWebSite ... by Azure": each roll writes both web apps' configuration, and the finished entry in the Activity Log names no caller, so the card called every roll's write Azure's own doing. The chart's one red line stays on the wire, unchanged, for a client that has not read the series' own. No reading, threshold or mark is added or taken away. `CloudChartLinesTests` and `PlanViewTests.Each_series_carries_its_own_lines_and_each_point_its_buckets_peak` hold it; `CloudThresholdTests` holds the 429s as counted marks.

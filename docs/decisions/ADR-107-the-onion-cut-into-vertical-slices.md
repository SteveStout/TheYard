# ADR: The onion cut into vertical slices

Status: accepted, 2026-10-20, at 1.0.4.0, with the measurements in the addendum at the end. Proposed the same morning, at 1.0.3.188. The rings stay as they are and keep pointing inward. What changes is the second axis: every feature becomes one folder with the same name in each ring, with a `Module` in the host that registers and maps it, and the gate holds that shape the way it holds the rings. Seven rules go with it, each with the test that fails the build when it slips. The version ladder from 1.0.3.188 to 1.0.4.0 is in the Decision below; each rung is one commit.

[![The onion cut into vertical slices: five rings as bands, four slices cut through every ring, the Shared column, the rules the gate holds, and one bid through one slice](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/vertical-slices.png)](https://theyard.stevenstout.biz/api/docs/diagrams/vertical-slices)

*A preview. [Open the drawing in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/vertical-slices) to zoom in. It is drawn by [`docs/images/vertical-slices.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/vertical-slices.mjs) and redrawn when a slice or a rule changes.*

## Reading list

For Steve, when he is fresh, in this order:

1. [ADR: Onion and SOLID, how this codebase holds them](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid): the rings this record keeps, and the ten tests that hold them.
2. [ADR: The rules a change has to pass](https://theyard.stevenstout.biz/?doc=adr-rules): the table this record adds seven rows to.
3. Jimmy Bogard, [Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/): the shape, in the words of the person who named it. Read it for the folder-per-feature idea and the one-handler-per-request idea; this record keeps the rings he sets aside.
4. Robert C. Martin, [The Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html): the dependency rule, which is the one thing a slice may never break.
5. Microsoft Learn, [Minimal APIs, organising endpoints with extension methods](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers): the framework's own way to map a feature from a file of its own, which `Module.Map` is.
6. The Fable review of 1.0.3.186 (C:\Claude\Claude_Job_Hunt\mentor\REVIEW-FABLE-2026-10-19.md): findings F1, F2, F3, F6, F8 and the table "Architecture rules and what enforces them", which lists the rules that were written down and held by nobody.

## In plain words

The server code is built in rings, like an onion, and every ring may only use the rings inside it. That has been true since the first commit and a test fails the build when it stops being true. But a feature, say placing a bid, was spread across the rings in folders named for what kind of code they held: the endpoint in one folder, the registration in another, the handler in a third, ninety files loose at the root of the host. To change bidding you opened four folders and guessed which of the ninety files mattered.

From 1.0.4.0 every feature is one folder with one name, repeated in each ring: `Features/Bids` in the host holds its endpoint and its wiring, `Features/Bids` in Application holds its handler and its port, `Features/Bids` in Infrastructure holds its adapters, `Features/Bids` in Domain holds its rules. The rings still point inward. A new feature is one folder per ring and one line nowhere, because the host finds its modules itself. A test fails the build when a slice reaches into another slice, when a handler finds its dependencies at runtime instead of being handed them, or when a site, a store, a resource name or a price is typed into the code instead of read.

## Context

Steve, 20 October, 07:16 to 07:31 CDT, after the review of the merge: "could be better, can we make the modules more modular in the backend and make this UI better?", then "my focus is in onion, solid design and vertical architecture", then, on the seven recommendations, "all of this sounds good and that should put us at 1.0.4.0 when we give the final send off", and "I expect a professional diagram added to the website similar to what you have with documentation and an ADR on vertical slice".

What the review found, with the file and line each came from (REVIEW-FABLE-2026-10-19.md):

- The host is sliced by kind. `api/TheYard.Api` holds `Endpoints/`, `Composition/`, `Cloud/` and about ninety files at its root; `CompositionRootTests.cs:46` and `:58` enforce that horizontal shape. A feature lives in all four places.
- `YardComposition` (Program.cs:9-14) is a record every registration takes whole and `Startup.cs:17-28` reads eleven fields off: a service locator in a record's clothes.
- `SiteHosts.cs:71-80` and `Stores.cs:84` branch on the literals "sql" and "cosmos"; a third store or a third site is an edit to both.
- `Backend.cs:103-106` tells its adapters apart by null checks (`LogsOperations => Cosmos is not null`), so the two stores are not substitutable behind one interface.
- `ShedMount.cs` mounts exactly one guest application, and `TheYard.Api.csproj:47` references its whole assembly with no test on where the host may touch it.
- `SessionStamp.cs:34-36`, `ShedMount.cs:34-35`, `Program.cs:13-14`, `RequestPipeline.cs:122`, `Telemetry.cs:31` and `IdentityTokens.cs:45-55` find dependencies at runtime (`RequestServices`, `app.Configuration`, `Environment.GetEnvironmentVariable`). `OnionTests.cs:109` holds the endpoints to the rule and nothing else.
- `Auction.cs` composes Inventory, Bids and Market for every endpoint; a test of one use case needs the whole auction.
- The Admin cards state seventy-eight facts they did not read (the review's companion table), and the cloud readers (`AzureMetrics`, `ResourceDirectory`, `AcrCatalog`, `NetlifyEdge`, `InsightsSource`, `CostReader`, `PlanReader`) hold HttpClients in the host ring with no port in Application.

The rings held through all of that. The second axis did not exist.

## Decision

**The rings stay. A slice is the same folder in every ring.** `api/TheYard.Api/Features/<Slice>/`, `api/TheYard.Application/Features/<Slice>/`, `api/TheYard.Infrastructure/Features/<Slice>/`, `api/TheYard.Domain/Features/<Slice>/`. What every slice may use and no slice owns lives in `Shared/` in each ring: the pipeline, `SiteHosts`, the store choice, the auction's composition, the clock, FNV-1a, the tone rule. The slices at 1.0.4.0: Bids, Vehicles, Accounts, Docs, Health, Admin/Plan, Admin/Cloud/Sql, Admin/Cloud/Cosmos, Admin/Cloud/Acr, Admin/Cloud/Edge, Admin/Cloud/Insights, Admin/Cloud/Mail.

**A Module per slice; Program.cs is a loop.** Each host slice has `Module.cs` implementing `IModule` with `Register(IServiceCollection, IConfiguration)` and `Map(IEndpointRouteBuilder)`. Program.cs discovers every `IModule` in the assembly, registers them, builds, runs the shared pipeline, maps them, and runs. It maps no route itself and stays under eighty lines, as `CompositionRootTests` already demands; that test is rewritten to the new folders rather than deleted.

**One handler per request.** Each endpoint binds its wire and calls one Application class with one `HandleAsync(request, cancellation)`. The handler takes its ports by constructor. The auction's composition stays in Shared and the handlers ask it; nothing composes the three services a second time.

**The seven rules, each with its test.** They are the review's seven recommendations, agreed on 20 October:

1. A slice's types depend only on their own folder in each ring and on `Shared/`; never on another slice. `SliceTests.cs`, a NetArchTest rule per slice, failing with the type and the slice it reached.
2. `YardComposition` is retired; every registration takes parameters; `Startup.cs` becomes one `IHostedService` per concern. `SliceTests`: the type no longer exists.
3. Sites and stores are configured lists (`Sites:n:Host`, `Sites:n:Store`); `SiteHosts` and `Stores` are lookups over them. `SliceTests`: a Cecil scan finds no host or store literal in either file.
4. Every store answers `IStoreDiagnostics`; `Backend` has no nullable adapter-typed member; endpoints see the auction and the handler, never `Backend`. `SliceTests` plus the existing endpoint rule in `OnionTests.cs:109` extended with `TheYard.Api.Backend`.
5. Guest sites stand behind `IHostedSite` (`Host`, `StartAsync`, `HandleAsync`, `DisposeAsync`); The Shed is one implementation registered from configuration, disposed from a last-registered hosted service's `StopAsync` so it outlives the drain. `OnionTests`: `TheYard.Api` depends on `TestProject` only from that implementation. `ShedMountTests` from 1.0.3.188 holds the 503, the 500 problem, The Shed's own upload limit on its name only (The Shed's controllers answer it as a 400 problem naming the limit, not a 413), and the stop order.
6. Dependencies arrive by constructor. No `RequestServices`, `Environment.GetEnvironmentVariable` or `app.Configuration` outside Program and `*Module`; `APP_VERSION`, `APP_COMMIT`, `WEBSITE_SITE_NAME`, `IDENTITY_ENDPOINT` and `IDENTITY_HEADER` are bound once into `BuildInfo` and `HostIdentity`. `SessionStamp` becomes the `RefuseSession` handler and refuses a token whose store claim is not the request's store (review F1). `OnionTests`: the Cecil scan Domain already has for the clock, applied to the host for these three.
7. One handler per request, as above. `SliceTests`: every handler has a test with fake ports; every endpoint has one integration case per status code.

**Facts on the Admin tab are read or absent.** A card shows what it read, what it computed from what it read, or nothing. The cloud readers move to `Infrastructure/Features/Admin/Cloud/` behind `IResourceReader`, `IPlanReader`, `IEdgeReader`, `ICostReader` and `IRegistryCatalogue` in Application, and only Infrastructure opens a socket. A Cecil scan of string literals in the host against resource names, SKUs, dollar amounts and dates starts with an empty allow list. Every `CardView` the API answers has the same slots, and every row names its source.

**The version ladder.** Each rung one commit, the gate green between rungs:

| Version | What lands | Rules |
| --- | --- | --- |
| 1.0.3.187 | CI on main green, the coverage floor measured without The Shed's assembly (shipped 20 October) | the floor |
| 1.0.3.188 | `ShedMountTests`; this record proposed, with its drawing on the Diagrams page; `YardComposition` retired; the five environment reads bound once; `RefuseSession` refuses the other store | 2, 6 |
| 1.0.3.189 | `Features/` with `Module.cs` in every host slice; Program.cs a loop; `CompositionRootTests` rewritten | 1 |
| 1.0.3.190 | sites and stores as lists; `IStoreDiagnostics`; `IHostedSite` | 3, 4, 5 |
| 1.0.3.191 to 1.0.3.197 | one slice per commit through the rings: Bids, Vehicles, Accounts, Admin/Plan, then the six cloud cards two per commit, each with its handler, its read facts and its tests | 7, the facts |
| 1.0.4.0 | the `CardShell` on the Admin tab, this record accepted, the pages and drawings, the measurements | the send-off |

## What was considered

- **Vertical slices without the rings**, the shape Bogard describes: one folder per feature holding everything from the endpoint to the SQL. Rejected: the rings are what let the catalogue move from JSON to SQLite to Azure SQL to Cosmos DB without one line changing above the port (ADR: Onion and SOLID), and the dependency rule is the one test a reader of this codebase can trust without reading it. The slices are added on top of the rings, not instead of them.
- **MediatR or a request bus** for the one-handler shape. Rejected: a static map in each `Module` does the same with no package, no reflection at request time, and a stack trace that names the handler.
- **Folders by ring inside each slice** (`Features/Bids/Api`, `Features/Bids/Application`) in one project. Rejected: the rings are projects on purpose, so the compiler holds the dependency rule before any test runs; moving them into folders would trade a compile error for a test.
- **Leaving the Admin cards as they are and only moving the readers.** Rejected: the review's table of seventy-eight typed facts is the Admin tab saying what was true the day each line was written, which is the opposite of what that tab is for.

## Consequences

- A feature is in one place in each ring. Opening `Features/Bids` in four projects is the whole of bidding.
- Adding a slice touches nothing outside its own folders. Removing one is deleting them; the gate names anything that still referred to it, which the slice rule says is nothing.
- Program.cs stops being a table of contents that a human keeps in order and becomes a loop; the order of registration is each module's own, and a module that needs another's service asks for the port, not the module.
- The tests get smaller. A handler test has one fake per port and no `WebApplicationFactory`; the integration tests shrink to one case per status code per endpoint.
- The cost is the move itself: about ninety files change folders, every `using` moves with them, and the diff is wide. That is why the ladder lands one slice per commit, with the gate green between, and why the record is written before the first file moves.
- The second cost is the gate's time: `SliceTests` runs one NetArchTest rule per slice and the Cecil scans read every method body in the host. Measured at 1.0.3.189 and written here.

## Where it sits

All five rings, and the test project. The rings' dependency rule is untouched; what the record adds is a rule on the second axis in each ring and a loop in the host. Domain gains `Features/` folders and nothing else. Application gains the handlers and the cloud ports. Infrastructure gains the cloud adapters, moved from the host. The host loses its root files to the slices and its `Composition/` folder to the modules.

## Files

- [`docs/images/vertical-slices.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/vertical-slices.mjs) - the drawing at the top, and on the App Architecture page
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs) - the shared steps by name, what the platform says read once, and the loop over `IModule`
- [`api/TheYard.Api/Shared/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Shared) - what every slice shares: the pipeline, the stores, the registrations, `IModule` and the loop's `Modules`
- [`api/TheYard.Api/Features/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Features) - one folder per slice, each with its `Module.cs`
- [`api/TheYard.Api/Shared/Hosted/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Shared/Hosted) - what ran at startup, one hosted service per concern, and the one that stops The Shed after the drain
- [`api/TheYard.Api/HostIdentity.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/HostIdentity.cs) - the platform's three settings, read once
- [`api/TheYard.Api/RefuseSession.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/RefuseSession.cs) - the session refused on the other store
- [`api/TheYard.Tests/ShedMountTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ShedMountTests.cs) - The Shed at its edges: the 503, the 500 problem, its own upload limit and the stop order
- [`api/TheYard.Tests/OnionTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/OnionTests.cs) - the ten ring rules, plus the host's constructor rule and the `TestProject` rule
- [`api/TheYard.Tests/SliceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/SliceTests.cs) - the slice rule, the literal scans, the handler and endpoint coverage rules
- [`docs/decisions/ADR-075-the-rules-a-change-has-to-pass.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-075-the-rules-a-change-has-to-pass.md) - the table, seven rows longer

## Addendum, 2026-10-20 (1.0.4.0): accepted, what landed and what it measured

Every rung of the ladder above shipped, each through its own green gate: 1.0.3.188 to 1.0.3.197, then 1.0.4.0. The seven rules each have their test, listed in ADR: The rules a change has to pass. Three things landed differently from the Decision, each said here rather than left for a reader to find:

- **The cloud readers stayed in the host.** The Decision moves `AzureMetrics`, `ResourceDirectory`, `AcrCatalog`, `NetlifyEdge`, `InsightsSource`, `CostReader` and `PlanReader` into Infrastructure behind five ports. They are still in `TheYard.Api/Cloud/` and beside it. What the move was for landed another way: every card's route asks one handler over a port the slice declares (`IPlanCard`, `ICloudCards`), no name of this deployment is written as a string constant in the six assemblies the literal scan reads (`LiteralScanTests`, with an empty allow list; a name in a comment or an interpolated string is outside what it reads), and the cards read what they used to type. The move itself is a new project and about a dozen files with their tests, and it waits for a lane of its own.
- **The names are configuration, not read off the directory.** `Azure:Resources` in appsettings.json names the group, the web app and the six resources, read once into `AzureResources`, and the group is App Service's own `WEBSITE_RESOURCE_GROUP` where the platform sets it. The directory still finds each resource by its type; the name is configuration because the bill's line is matched before any hour is read, and the insights card is billed under the workspace, not the component the directory finds.
- **A card is unread after three of its own reads, not three of the tab's ticks.** The plan's newest Azure Monitor minute is one to three minutes old by design, so three thirty-second ticks would call a healthy card unread. A card read every minute is unread past three minutes, a card read hourly past three hours (`CardTones`, `CardFrameTests`).

Some figures stay typed, each with its reason on its constant, because no record the service returns states them: the Cosmos DB free tier's 25 GB, Container Registry Basic's 1,000 reads a minute, Communication Services' 100 and 1,800 sends an hour, Application Insights' free 5 GB a month, Netlify's credits a deploy and a gigabyte and its 300 a cycle, and the plan's 1,920 sockets. (The review of 1.0.4.0 found the list short and one figure wrong; the addendum of 1.0.4.4 below has the whole of it.)

**The two measurements**, read the same way before and after:

| Read | Before, 1.0.3.187 | During, 1.0.3.189 | During, 1.0.3.190 | After, 1.0.3.197 |
| --- | ---: | ---: | ---: | ---: |
| Plan memory in use, the plan card's own read a few minutes after the roll | 2.17 GB, 62 % | 2.14 GB, 61 % | 2.21 GB, 63 % | 2.15 GB, 57 % |
| The roll's first 200, Deploy's Verify step: "origin serves the version after" | 35 s | 31 s | 37 s | 46 s |
| The roll, push to both sites live, the ship's own read | not read | 222 s | 191 s | 193 s |

What they say: the slices cost the running site nothing it can measure. The percentage is Azure Monitor's own reading of the plan's memory and the gigabytes are the card's arithmetic on it; the percentage stayed inside its minute-to-minute range, 57 to 63 %, with the plan's 30-day 95th at 83 % throughout, and the first 200 after a roll stayed between half a minute and three quarters of one, the spread one reading a version gives. The gigabytes in the last column are worked against 3.78 GB, the machine's memory as Linux states it, which the card reads from 1.0.3.197; the earlier columns are worked against the 3.5 GB a size table said. Each figure is one read; the reads are in `staging\lane1040\measure-*.txt` and `plan-*.json` on the build machine.

## Addendum, 2026-10-20 (1.0.4.4): what the review of 1.0.4.0 found in the rules, and the fixes

The context-free review of 1.0.4.0 (mentor, REVIEW-FABLE-1.0.4.0-2026-10-20) read the seven rules' tests against the code and found two green on shapes they exist to refuse, one writer outside the shared store's collection, and typed figures the list above did not name. What changed at 1.0.4.4:

- **Rule 4.** `Backend.UserStore` was a `Func<IServiceProvider, IUserStore<YardUser>?>`, Infrastructure's user entity two generic levels down, and the test walked one. The factory is gone from `Backend` and `StoreAttachment`: each store's registration adds Identity's store under the store's key (`AddKeyedScoped<IUserStore<YardUser>>`), and the Accounts module resolves the request's by `CurrentBackend.Key`. The test now walks every generic argument, fields, constructors and methods of both types.
- **Rule 6.** The runtime scan flagged three calls and exempted any type named `*Registration` or `*Module`. It now flags `IServiceProvider.GetService`, the `GetService`, `GetRequiredService`, `GetServices` and keyed extensions, `ActivatorUtilities`, `HttpContext.RequestServices`, `WebApplication.Configuration` and `Environment.GetEnvironmentVariable`; the composition root is a list of named types, each with its reason in the test (Program, the eleven registrations, `Modules`, the slices' `Module` classes by namespace, the two pipeline steps `RequestPipeline` and `HostedSiteMount`, and `ShedApplication`, which composes a guest site's own container); and the same scan runs over the Application ring, where nothing is exempt. `SelfAddress` lost its `IServiceProvider` overload; the registration that calls it resolves the server itself.
- **Rule 7.** `POST /api/admin/reset-links` declared no status; it declares 200, 404 and 503 and each has its case.
- **The shared store.** `ProofTests` starts a proof whose round bids on a live vehicle and then resets every bid, on the shared Cosmos DB test database, outside the collection; it is in it now, and `SharedStoreTests` reads every test class for a bid, a buy, a reset, a round or a proof through the host and fails on one outside the collection. The Cosmos bid store's compare-and-set catches 404 beside 412 and 409: a standing another process reset between the read and the replace is a lost race, not a 500.
- **Typed figures.** The plan's socket limit was B1's 1,920 on a B2 plan. It follows the size the plan record names, by the rule Microsoft's limits page gives (sizes 1 to 3 of Basic and Standard, 0 to 3 of Premium v3: 128 short of a power of two at each step), and a size outside those is measured against no limit. The free tier's 25 GB applies only while the account record says the free tier is on; the Basic read rate only while the registry record says Basic. The whole typed list, with each constant's file: `CosmosCard.FreeGb` (25 GB), `AcrCard.BasicReadsPerMinute` (1,000), `MailCard` (100 and 1,800 sends an hour, and the chart's 10 a minute), `InsightsSource.FreeGbPerMonth` (5 GB), `NetlifyEdge` (15 a deploy, 20 a gigabyte, 300 a cycle, the four sampled paths), `EdgeCard`'s 14-day certificate warning, every amber and red level in `PlanThresholds` and `CloudThresholds`, the 15-minute hold, `CardFrame.RefreshSeconds` (30) and its 60 and 3,600-second cadences, and `CardTones.ReadsBeforeUnread` (3). Each is policy or a figure no record states; none is a size, a name or a price of this deployment. (Until 1.0.4.14 the list also held two tier words, "PAY AS YOU GO" and "PAY PER MESSAGE", which were prices of a kind; both are read from the records now, as the addendum of 1.0.4.14 says.)

What the review found and this rung leaves for the next: rules 1 to 3 hold more by the code's habits than by their tests (the root namespace counts as shared, the route scan misses `MapControllers`, rule 2 checks seven of eighteen hosted services, the store keys are typed in a dozen files outside the four routing types), rule 7 reaches five of eight slices, and the literal scan reads string constants only. Each is written here so the next lane starts from the finding rather than the claim.

## Addendum, 2026-10-20 (1.0.4.5): what the review of 1.0.4.0 found in the cards, and the fixes

- **The plan's memory was the VM's.** `MachineSize` opened cgroup v2's `memory.max` and fell to `/proc/meminfo`; App Service's containers run under cgroup v1, whose limit is `memory.limit_in_bytes`, so the card said the machine's total where the container has less, and the Memory tile on the same page (the collector's cap, thirty per cent of what the runtime sees) implied a smaller machine. It opens both files now, takes a limit only when it is below the machine's total, and the live read after this version is the figure the table at 1.0.4.0 should have carried. The figures are binary gigabytes, labelled GB the way Azure's portal labels them.
- **Four cards read the first resource of their type.** The Cosmos DB account, the registry, the mail service and the Insights workspace were found by type alone; the configured names labelled the card and matched the bill. Each is selected by its configured name now, and a second account of a type in the group changes nothing.
- **A card's tone counted the dials and not the rows.** The Cosmos DB card read fine beside its row of seven hundred throttled requests, and its own unrendered "Normal" sentence promised amber on any 429. The frame folds the rows' warning and alarm tones into the card's tone, and the sentence is gone from the wire: the chips were already the thresholds.
- **The source chips said Azure Monitor on lines that came from a record.** A tier, a container count, the free-tier flag, the registry's image and tag counts, the mail sender and its data location, the cap, retention and workspace of Insights: each names its own source now, the ARM record or the registry's catalogue.
- **The deploy bands had no version on the 1 h and 24 h windows.** The old container records on under its own build for ten minutes after the write, so the first minute after the write still said the old build and every band read "a configuration change". A band is named for the first build inside the half hour after its first write that differs from the one before.
- **A link-less resource said "opens in the Azure portal".** One sentence now says what a link opens, and nothing for no link.

Left for the next round, each a finding of the review: the power row's apps carry no uptime and no version of their own (slot 11 of the mockups); the narrow layout stacks the dials one across where the mockup has two; the refresh constant stays typed in two places, held equal by `CardFrameTests`, because the retry line is drawn when there is no wire to read it from.

## Addendum, 2026-10-20 (1.0.4.7): the record says what landed, and the picture draws it

The Decision above says a slice is the same folder in every ring, with `api/TheYard.Infrastructure/Features/<Slice>/` and `api/TheYard.Domain/Features/<Slice>/`, and lists the Cloud cards as six slices. That is the design as planned, and the review of 1.0.4.0 read it against the tree: the host and Application have the folders; Infrastructure has one, `Features/Accounts`, and answers every other slice's port from its root, where the relational and document adapters already sat as pairs (`EfBidStore` beside `CosmosBidStore`, `EfVehicleCatalogue` beside `CosmosVehicleCatalogue`); Domain has no `Features/` at all, because its rules are a dozen files and a folder each would be a folder of one; and the Cloud cards are one slice, `Features/Admin/Cloud`, with one handler and one port, because the seven cards share one shape. None of that changes the rules the tests hold, which never asked for the inner folders. What it changes is the picture: `docs/images/vertical-slices.mjs` drew twenty-eight file names the tree never had, the planned ones, and from 1.0.4.7 it draws the real ones, with `SliceTests` holding every name in it to a file in the tree and refusing a `Features/` folder in Domain until this record says so. ARCHITECTURE.md and HOSTING.md say the same.

The two deploys ignore the job hunt's paths (`functions/**`, `infra/jobhunt.bicep`, `nightly.yml`), so a push that changes only those never rewrites the version's image tag; `DeployWorkflowTests` holds the filter. The registry's keep-ten prune has refused every delete since it was written (the deploy identity lacks AcrDelete); that is a role assignment, outside this record and outside the code, and is left with Steve.

## Addendum, 2026-10-21 (1.0.4.14): the review's half-done fixes, finished

A cold check of 1.0.4.4 to 1.0.4.8 on 21 October found five of the review's fixes partly done. From 1.0.4.14:

- **Rows that are information do not lift a card.** The fold of 1.0.4.5 made every warning row lift its card, which was right for a throttled request and wrong for a fact. Information now reads info: paying past the Insights free allowance, no daily cap, an Azure event on the mail service, the edge before its retirement day (and a warning after it, with the date in the detail), a stale live read (the frame's unread rule already says it). A failed pull stays a warning, with the count in its detail. `CardFrameTests` holds one case a card.
- **The catalogue selects by configured name, and says when it cannot.** The Insights component is the one whose record names the configured workspace (or the group's only component), not the first of its type. A configured name the group does not hold is the card's note, "no resource named X in the group", where it was an exception only the log saw. The source-text grep in `CloudViewTests` is gone: a group of two resources of every type, the configured one second, holds what each card asks Azure for.
- **No default source.** `ResourceCard.Source` and the frame's fill are gone. Every card writes its own list of where each row and readout line is read (`CardFrame.Sourced`), so a line's source is a decision on the page, and a line the list leaves out reaches the wire unsourced, which `CardFrameTests` refuses on every card. The empty value before the workspace's name on the Insights row is gone.
- **The shared store's guard reads classes, and every verb.** `SharedStoreTests` reads each class, with the comment and attributes above it and the class it derives from, and counts any verb on `/api/bids`, a DELETE among them. The live row for the Cosmos DB 404 is not added: the loss it guards happens inside the store's compare-and-set behind the bid service's gate, and the tests- containers hold no second process to reset a standing in the middle of one.
- **Smaller.** The socket limit's comment says 16,256 for P3V3, the figure the code gives, and `PlanThresholdTests` holds B3, S3, P0V3, P1V3 and P3V3. The Insights tier is the workspace record's `properties.sku.name` and the mail tier the record's `sku.name`, each "tier not read" when absent, and an absent sampling share reads "not read" where it read 100. The header finds the plan's instances line by a key the wire carries (`CloudLine.Key`), not by its label. The rail, its glances and the browser spec read one list of the cloud cards (`CLOUD_RESOURCES` in `src/lib/workbench.ts`). The sentences above on the literal scan and the typed list are qualified and corrected where they overreached.


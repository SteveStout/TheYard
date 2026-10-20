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

- **The cloud readers stayed in the host.** The Decision moves `AzureMetrics`, `ResourceDirectory`, `AcrCatalog`, `NetlifyEdge`, `InsightsSource`, `CostReader` and `PlanReader` into Infrastructure behind five ports. They are still in `TheYard.Api/Cloud/` and beside it. What the move was for landed another way: every card's route asks one handler over a port the slice declares (`IPlanCard`, `ICloudCards`), no name of this deployment is written in the code (`LiteralScanTests`, with an empty allow list), and the cards read what they used to type. The move itself is a new project and about a dozen files with their tests, and it waits for a lane of its own.
- **The names are configuration, not read off the directory.** `Azure:Resources` in appsettings.json names the group, the web app and the six resources, read once into `AzureResources`, and the group is App Service's own `WEBSITE_RESOURCE_GROUP` where the platform sets it. The directory still finds each resource by its type; the name is configuration because the bill's line is matched before any hour is read, and the insights card is billed under the workspace, not the component the directory finds.
- **A card is unread after three of its own reads, not three of the tab's ticks.** The plan's newest Azure Monitor minute is one to three minutes old by design, so three thirty-second ticks would call a healthy card unread. A card read every minute is unread past three minutes, a card read hourly past three hours (`CardTones`, `CardFrameTests`).

Some figures stay typed, each with its reason on its constant, because no record the service returns states them: the Cosmos DB free tier's 25 GB, Container Registry Basic's 1,000 reads a minute, Communication Services' 100 and 1,800 sends an hour, Application Insights' free 5 GB a month, Netlify's credits a deploy and a gigabyte and its 300 a cycle, and the plan's 1,920 sockets.

**The two measurements**, read the same way before and after:

| Read | Before, 1.0.3.187 | During, 1.0.3.189 | During, 1.0.3.190 | After, 1.0.3.197 |
| --- | ---: | ---: | ---: | ---: |
| Plan memory in use, the plan card's own read a few minutes after the roll | 2.17 GB, 62 % | 2.14 GB, 61 % | 2.21 GB, 63 % | 2.15 GB, 57 % |
| The roll's first 200, Deploy's Verify step: "origin serves the version after" | 35 s | 31 s | 37 s | 46 s |
| The roll, push to both sites live, the ship's own read | not read | 222 s | 191 s | 193 s |

What they say: the slices cost the running site nothing it can measure. The percentage is Azure Monitor's own reading of the plan's memory and the gigabytes are the card's arithmetic on it; the percentage stayed inside its minute-to-minute range, 57 to 63 %, with the plan's 30-day 95th at 83 % throughout, and the first 200 after a roll stayed between half a minute and three quarters of one, the spread one reading a version gives. The gigabytes in the last column are worked against 3.78 GB, the machine's memory as Linux states it, which the card reads from 1.0.3.197; the earlier columns are worked against the 3.5 GB a size table said. Each figure is one read; the reads are in `staging\lane1040\measure-*.txt` and `plan-*.json` on the build machine.


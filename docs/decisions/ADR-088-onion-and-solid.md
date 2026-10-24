# ADR: Onion and SOLID, how this codebase holds them

Status: accepted, 2026-10-03.

## In plain words

The server is built in rings, like an onion. The rules of the auction sit in the middle, the use cases wrap them, and the database code and the web endpoints sit on the outside. Code may only use the rings inside it, never the ones outside it. That rule is checked by tests that read the compiled code, so breaking it fails the gate.

So a developer can change the database, the web framework or the hosting without touching the auction's rules, and the organization gets rules that are tested in milliseconds with no database, no web server and a fixed clock.

## The rings

[![TheYard's rings: Data at the centre, then Domain, then Application, with the host and the adapters outside, every dependency pointing inward](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/rings.png)](https://theyard.stevenstout.biz/api/docs/diagrams/rings)

*A preview. [Open the rings diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/rings) to zoom in and follow it.*

Innermost first:

- **Data** (`api/TheYard.Data`). `Vehicle` and `PhotoEntry`, exactly as the dataset has them. No behaviour and no dependencies. It is the shared kernel every ring may read. A vehicle goes out on the wire under the dataset's own names as `VehicleView`, built in `VehicleWire.cs`: every dataset field except the reserve amount, plus the facts the server derives. `BidView` is built the same way, in one place.
- **Domain** (`api/TheYard.Domain`). The rules, in nine files: `BidRules`, `AuctionSchedule`, `AuctionClock`, `StandingRules`, `VehicleFilter`, `VehicleOrdering`, `VehicleSearchIndex`, `PhotoGallery` and the `Fnv1a` hash the schedule is derived from. Pure functions of their inputs and the clock they are handed.
- **Application** (`api/TheYard.Application`). The use cases and the ports. `Auction` composes one store's catalogue, everybody's bids and the simulated room in the order the rules need. `IVehicleSource`, `IPhotoManifestSource` and `IBidStore` are the auction's ports; `IActivityStore`, `ILogStore`, `IMachineHistory`, `IResetLinks`, `IEmailSender`, `IStoreExperiment` and the rest are the operator's.
- **The adapters** (`api/TheYard.Infrastructure`, `api/TheYard.Infrastructure.Cosmos`). Each implements the ports against one store: EF Core over Azure SQL Database or SQLite, and the Cosmos DB SDK. The host uses a few of their types where it builds or measures a store: the shared user `YardUser`, the `StoreLoad` reading and the `CosmosStore` the composition root creates. No endpoint holds a database context or a Cosmos DB client, and a test holds that.
- **The host** (`api/TheYard.Api`). Endpoints read a request, ask Application and write the answer. `Composition/` is the composition root, where every service is registered, and `Program.cs` is a table of contents for it.

The front end keeps the same shape in `src/`: components use hooks, hooks use `src/lib`, and `src/lib` imports nothing from React (ADR: The React configuration, explained for a new developer).

## How the build holds them

`api/TheYard.Tests/OnionTests.cs` reads the compiled assemblies with NetArchTest (the maintained fork, NetArchTest.eNhancedEdition). It sees method bodies and the types the compiler writes for lambdas and async methods, so a dependency hidden in a handler's body fails as surely as a using line would. A failure names the type and the dependency it found. The ring rules select by assembly and then by namespace, so a type in a nested folder still belongs to its ring. They allow the .NET base class library (BCL), the `System` namespaces, in every ring, and two rules take the disk and the network back out of it for Domain and Application, and the clock out of Domain. The two endpoint rules select by name, every class whose name ends in `Endpoints`, and the registration rule allows `Program` and classes ending in `Registration`; a handler class named otherwise would escape them, which is why the naming is part of the house style.

| Rule | What it stops |
| --- | --- |
| `Data_depends_on_nothing_outside_the_BCL` | The dataset's shapes picking up a framework. |
| `Domain_depends_on_nothing_outside_the_BCL_and_Data` | A rule reaching for a package (EF Core, the Cosmos DB SDK, ASP.NET). |
| `Domain_never_reaches_the_disk_the_network_or_the_clock` | A rule reading a file, calling the network, or reading the system clock or a random number itself. |
| `Application_depends_only_on_Domain_Data_and_the_BCL` | A use case depending on EF Core, the Cosmos SDK or ASP.NET. |
| `Application_never_reaches_the_disk_or_the_network` | A use case reading a file or calling the network. |
| `The_document_store_adapter_borrows_only_the_shared_user_from_the_relational_one` | The Cosmos DB adapter growing a second dependency on the EF Core one. |
| `Endpoints_never_reach_a_store_or_the_service_container` | A handler holding a database context, a Cosmos DB client or `IServiceProvider`. |
| `Endpoints_reach_the_auction_only_through_the_Application_ring` | A handler composing the bid services itself, in an order of its own. |
| `The_host_never_queries_a_database_itself` | A query written in the host instead of the adapter that owns the store. |
| `Services_are_registered_only_in_the_composition_root` | Wiring hidden inside behaviour. |

The rules for the host, as the build runs them:

```live path=api/TheYard.Tests/OnionTests.cs region=host
```

Each rule was watched failing before it passed. Four failed against the code as it stood at 1.0.3.67: the endpoint rules found `IServiceProvider` in the account and admin handlers and the bid services in the bid, vehicle and admin handlers; the document store rule found the Cosmos DB adapter using the relational adapter's `DatabaseState`; and the database rule found three queries in the host, one in a lambda inside the composition root. The other four already held, so each was proved by planting one forbidden reference (a logging package in Data, Domain and Application, and a registration outside `Composition/`) and watching it go red. The two rules on the disk, the network and the clock were proved the same way, by a planted file read in Domain and another in Application. An adapter can never use the host, because the host references the adapters and the compiler refuses a cycle, so that line has no test of its own. The rule table in ADR: The rules a change has to pass lists all ten.

## SOLID, and where each principle shows

SOLID is five design principles. In one line each, with the records that apply them:

- **Single responsibility**: a class has one reason to change. An endpoint handles HTTP, `Auction` orchestrates, an adapter persists, `BidRules` decides. `PasswordReset` holds the reset's steps so its handler only reads and writes (ADR: The composition root, split by job; ADR: One folder per component).
- **Open/closed**: add behaviour by adding a class behind an interface, not by editing a core class. The Cosmos DB store arrived as new adapters behind the same three ports, after one change to the ports themselves: every member became a `Task`, because the document store has no synchronous driver (ADR: The ports learn to wait). That change touched every caller once; the adapters that followed touched none (ADR: A second store on Cosmos DB, and what it costs).
- **Liskov substitution**: any implementation of an interface can stand in for another without surprises. `CosmosUserStore` stands in for Identity's EF store. `NullBidStore` stands in for a real bid store in the tests and in production too: a backend whose store has not come up yet bids against it, so bids work and are forgotten at restart until the store attaches. That is a deliberate limit, written down in ADR: The relational store. Where substitution is not complete it is written down below (ADR: Accounts on a document store).
- **Interface segregation**: interfaces shaped to what a caller needs. The auction's three ports are separate because the catalogue, the photos and the bids are read by different code at different times. `IStoreExperiment` has one method because the Admin tab asks one question. Its result is shaped by the one experiment that exists, a partition-key comparison on Cosmos DB, so a second kind of experiment would reshape it.
- **Dependency inversion**: the inner ring owns the interface and the outer ring implements it, and code receives what it needs through its constructor. Application declares `IBidStore`; `EfBidStore` and `CosmosBidStore` implement it; `Composition/StoreRegistration.cs` decides which one a store gets (ADR: The relational store).

## What is kept on purpose

These were found in the review that wrote this record and left as they are. Each is the right size for this codebase, and each says what would change it.

- **One shared user entity across both adapters.** `YardUser` is an Identity type, so it cannot live in Application without Application taking the Identity package. The Cosmos DB adapter references the relational adapter for that one type, and a test allows that type and nothing else. A small `TheYard.Identity` project would remove the arrow; it would add an eleventh project and buy nothing at runtime, so it waits until a third store needs accounts.
- **The backend exposes its store to the host.** `Backend` carries `CosmosStore` and the relational context factory, because the composition root builds them and the Admin tab's readings and the performance proof measure them. Endpoints never use them, and the tests hold that. A port per reading would add interfaces for code that only observes.
- **The email sender sits in the host.** `AcsEmailSender` implements `IEmailSender` over the Azure Communication Services SDK in TheYard.Api, using the credential rule the Cosmos DB adapter owns. A third adapter project for one class buys nothing at runtime.
- **The cost history waits in Application.** `ICostHistory` and its only implementation, `CostsInMemory`, sit in Application with no adapter yet, because the port is where a kept history would plug in: one adapter and one line of registration (ADR: What Azure charges).
- **Host concerns live in the host.** Health checks, the Admin tab's readings, the cost card, served documents and the activity collector read what the host keeps or what Azure reports. They encode no auction rule, so they belong outside Application.
- **Ids as strings, money as whole dollars.** No value objects for ids or amounts: the wire uses the dataset's own names and types, every amount is a whole-dollar `int` that the rules add whole increments to, and there is no repeated validation a value object would remove. A second currency or cents would change this.
- **Exceptions where the code already uses them.** A refused bid is a typed `BidOutcome`. Infrastructure failures surface as exceptions that one handler turns into a problem document (ADR: Error handling, one shape everywhere). Rewriting either into the other would add churn and no safety.
- **The server's clock is a static.** Endpoints read the server's UTC clock through `Clocks`, `Clocks.Now()` for the auction and `Clocks.UtcNow()` for a stamp or a reporting window, and hand it inward. Domain and Application take the clock as a value, which is what lets their tests fix time. Injecting a `TimeProvider` into every handler would add a parameter that tests do not need.
- **Two read and write ports not split.** `IActivityStore` and `ILogStore` each have one writer and one reader. Splitting them would double the interfaces and allow no new swap, so they stay whole until a caller needs only half.
- **Known gaps in substitution, written down.** The Cosmos DB user store does not move the email claim when an address changes, and accepts and drops Identity's cancellation token. Nothing in the site changes an email address today. The catalogue's synchronous accessors wait on a load the host has already finished (ADR: The ports learn to wait).

## When not to build it this way

The rings pay for themselves here because there are two stores behind the same rules, a host with an operator's tab, and a test suite that runs the rules on their own. A smaller job pays the cost and gets none of that.

- **A small CRUD service** over one table: one project, a minimal API, and EF Core called from the endpoint. Add a ring the day a second store or a rule worth testing alone appears.
- **A spike or a proof of concept**: one file. The point is to learn something quickly, and the code is thrown away.
- **A script or a scheduled job**: no layers at all. A function that reads, changes and writes is the clearest shape.

The Shed, a file browser built the same way on another company's starter project, takes the middle road: one project with its rings as folders, held by the same kind of tests by namespace.

## Addendum, 2026-10-04: the wire only copies

A vehicle's status and its least next bid used to be worked out in `VehicleWire`, in the host, from
the Domain's schedule and bid rules. They now sit on `StandingVehicle` in Application, beside the
standing price and the sold flag the auction already hands out, and `VehicleWire` copies them. The
host composes nothing about a vehicle; `AuctionTests` holds that the two facts are the rules' own.

## Addendum, 2026-10-17: the two ports carry more reads than this said

"What is kept on purpose" says `IActivityStore` and `ILogStore` each have one writer and one reader. They do not, and have not for some time: `IActivityStore` carries `RecordAsync` beside `AvailabilityAsync`, `HoursAsync` and `VisitorsAsync`, and `ILogStore` carries `AppendAsync` beside `AvailabilityAsync`, `QueryAsync`, `CountAsync` and `RingAsync`. The ruling it supports is unchanged, and is the reason this is an addendum rather than an edit: each port is one writer and the reads its own callers ask for, splitting them would double the interfaces and allow no new swap, and they stay whole until a caller needs only half. Found by a reader of the SOLID page on the way to shipping it, which is what a page written against the code is for.

## Addendum, 2026-10-18 (1.0.3.163): each letter in plain words first

The SOLID Principles page opened with one paragraph that ran the five habits together, so a reader
who did not already know the acronym could not match a habit to its letter. The page now opens with a
drawing, `docs/images/solid.svg`, that takes the letters one at a time: the name, one plain sentence,
and one line on where TheYard shows it, the same examples the page's table holds. Under it, one line
per letter in words, and why the five matter. The drawing has its own page like every other, and the
browser test that opens the page holds the drawing's link and the five lines.

## Where it sits

Every ring: this record is the map of all of them and of the tests that hold them, and it serves all five SOLID principles by naming where each one shows and where it stops. It cost a new test package (NetArchTest), ten tests and a pass over every record. A codebase with one store and no rules worth testing alone would be built flat instead, as the section above says.

## Addendum, 2026-10-20 (1.0.3.193): PasswordReset became handlers

The single-responsibility line above names `PasswordReset` as the class that holds the reset's steps. From 1.0.3.193 those steps are `ResetPasswordHandler` and `ResetLinkMaker` in the Application ring, behind the accounts port (ADR: The onion cut into vertical slices, rule 7); the principle is unchanged and now holds one ring further in.

## Files

- [`api/TheYard.Tests/OnionTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/OnionTests.cs): the ten rules.
- [`api/TheYard.Application/Shared/Auction/StandingReader.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/StandingReader.cs): the use cases the endpoints ask.
- [`api/TheYard.Domain/Shared/Auction/StandingRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Shared/Auction/StandingRules.cs): the one rule for raising a price.
- [`api/TheYard.Application/Shared/Auction/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/Ports.cs): the auction's ports.
- [`api/TheYard.Api/Shared`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Shared): the composition root.
- [`docs/images/rings.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/rings.mjs): the drawing.
- [`docs/images/solid.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/solid.mjs): the SOLID letters, one at a time.

## Addendum, 2026-10-20 (1.0.4.0): the onion cut into vertical slices

The rings are unchanged, and each now carries the same feature folders: a slice is `Features/<Slice>` in the host with a `Module` that registers and maps it, and, where it has use cases, `Features/<Slice>` in the Application ring with its handlers and ports (ADR: The onion cut into vertical slices). What the letters gain from it: single responsibility now holds per request, because each route asks one handler with one `HandleAsync`; dependency inversion holds per slice, because a handler reaches its store, its cards or its accounts through a port the slice declares and an adapter implements; and open and closed holds for the host, because a new slice is a folder and a `Module`, with no line added to Program.cs. `SliceTests`, `HandlerRuleTests`, `CompositionRootTests` and `OnionTests` hold the shape.

## Addendum, 2026-10-21 (1.0.4.24): the rings refuse the breaks they exist for

A review of 1.0.4.17 planted six violations, one per ring, and every architecture test passed with them in: a Data record with a rule and a `File.ReadAllText`; a Domain class reading `Stopwatch`, starting a `Process` and finding an adapter by `Type.GetType`; an Application class reading the disk through `DirectoryInfo` and `FileInfo`; a second buy-now rule inside `EfBidStore`; a Cosmos DB adapter opening `SqlConnection`; and a static route class in Features/Docs named `SearchRoutes` that opened `SqliteConnection` and added a bid increment in the host. `System` is allowed whole in every ring, so the rules held only what they named.

From 1.0.4.24 they name more, and the six planted files are each red by name (the run is in mentor\THEYARD-MASTER-DONE-2026-10-21.md). `OnionTests`: the disk and the network now include `FileInfo`, `DirectoryInfo`, `FileSystemInfo`, `System.IO.Compression`, `System.IO.MemoryMappedFiles` and `System.IO.Pipes`, and Data is held to them as Domain and Application are; the machine (`Random`, `TimeProvider`, `Environment`, `Stopwatch`, `Process`) is held out of Data and Domain, and an IL scan refuses `Type.GetType`, `Assembly.Load` and `Activator` there; `Data_has_no_behaviour` allows a Data type accessors, constructors and what the compiler writes for a record, and nothing else; `Infrastructure_adapts_and_decides_nothing` refuses the web in either adapter beyond Identity, and an adapter method that compares money (a parameter named for a price, a bid or an amount, or a read of a vehicle's price, beside a comparison in its IL); the Cosmos DB adapter's borrowing rule adds `Microsoft.Data` and Identity's Entity Framework stores; the host's query rule adds `Microsoft.Data.Sqlite` and `System.Data.Common`'s commands and connections; and an endpoint class is any type that maps a route (calls a `Map` method of the route builder), not only one named `*Endpoints`. A route delegate or a handler that takes `IConfiguration` is refused: the two that did, the reset link's and the About page's origin, now read `SiteHosts.LinkOrigin`, which carries `Site:Url` bound once by `SiteSettings`.

## Addendum, 2026-10-21 (1.0.4.24): a third store is one line, and three hidden dependencies arrive by constructor

The store keys were written as "sql" and "cosmos" in eleven files across the host and the Application ring, so a third store was an edit in every one. From 1.0.4.24 they are written once, in `StoreKeys` (Application, so the use cases' logs key by it too), and every registration, card and route reads it; `SliceTests.Rule_3_a_store_key_is_written_once_in_StoreKeys` reads the source of both assemblies and refuses a store key anywhere else (the IL cannot tell, because a constant is copied into each place that uses it). Three dependencies a test could not hand in now arrive by constructor: `MarketService` takes the `Random` that shuffles the room's candidates, so a test seeds it; `BidService` has no constructor that makes its own `MemoryBidStore`; and `TokenIssuer` keeps one `JsonWebTokenHandler` where three methods each built their own.

What this rung of the review also asked and is not done here: the 300-line rule counted per type across partial files, with `InventoryService`, `AdminEndpoints`, `CosmosStore`, `BidService` and `ProofRunner` split by job; `Backend` made immutable; one diagnostics answer in place of `LogsOperations` and `LogsStatements`; one Availability rule for every memory double; `AccountPolicy` bound from configuration; the remaining static state registered. Each is a lane of its own, named in mentor\THEYARD-MASTER-DONE-2026-10-21.md.

## Addendum, 2026-10-23 (1.0.4.39): Interface Segregation is a rule, through CQRS

The I in SOLID was held by review until this version. From 1.0.4.39 every request is a query or a command (ADR: A request is a query or a command), and a query handler may not be handed a port whose name ends in `Store` or `Writer`, so a port a query needs half of is split: `IBlogReader` under `IBlogStore`, `IAccountReader` under `IAccountStore`, `ILinkOpensReader` under `ILinkOpens`, and the auction's three parts each behind a reading and a writing interface (`IStockReader`; `IBidReader` and `IBidWriter`; `IRoomReader` and `IRoomWriter`). `CqrsTests` holds it. The three services this record named as the use cases (`InventoryService`, `BidService`, `MarketService`) and `Auction`, which composed them, are gone: the bodies moved whole into `Stock`, `BidBook` and `Room`, the composition into `StandingReader` and the bid handlers, and the regions the records above quote moved with them under the same names. The per-type 300-line rule this record's addendum of 1.0.4.24 left open still waits for its version; `BidBook` and `Stock` are its first two cases.

## Addendum, 2026-10-23 (1.0.4.47): the 300-line rule counts a type, not a file

The addendum of 1.0.4.24 left this open and the one of 1.0.4.39 named its first two cases. From 1.0.4.47 `FileShapeTests.No_production_type_runs_past_three_hundred_lines_across_its_parts` adds up every top-level C# type under `api/` across all of its partial files, from the doc comment above it to its closing brace, and fails the build past 300. A partial class cut into five files of 150 lines was still one class of 750 doing more than one job, and the file rule could not see it.

Nine types were over at 1.0.4.46, and each was cut by job into types of its own rather than into more partial files:

| type at 1.0.4.46 | lines | the job taken out, and where it went |
| --- | ---: | --- |
| `Stock` | 771 | the choice of store or list and the fill (`StockSource`), the store's answers through the five-second cache with and without the moved vehicles (`CataloguePages`), the filter in the store's spelling (`CatalogueFilterResolver`) |
| `CosmosStore` | 568 | the client and the credential (`CosmosConnection`), the container check, seed and probe (`CosmosStoreStartup`, handed out as `Starting`), the wrapper that times every call and writes its charge (`CosmosOperationLog`, handed out as `Recorder`) |
| `CosmosVehicleCatalogue` | 562 | the kept places of every order (`CosmosCatalogueKeysets`) and the live segment's (`CosmosCatalogueLive`) |
| `EfVehicleCatalogue` | 515 | the typed words' table (`EfCatalogueWords`) and the fill (`EfCatalogueFill`) |
| `YardDbContext` | 513 | the column names (`YardColumns`) and each table group's model (`StandingsModel`, `CatalogueModel`, `BlogModel`, `ActivityModel`) |
| `BidBook` | 468 | the standings it holds and reads (`BidLedger`) and the pure fold of a bid into a standing (`StandingFold`); `BidBook` keeps the writes and the gate |
| `AdminEndpoints` | 445 | one route class per card family (`AdminActivityRoutes`, `AdminCostRoutes`, `AdminFollowsRoutes`, `AdminObservabilityRoutes`, `AdminEvidenceRoutes`, `AdminUsageRoutes`); `AdminEndpoints` keeps the map |
| `ProofRunner` | 406 | the timed requests a round makes (`ProofRequests`) |
| `TelemetryReader` | 317 | the query text (`TelemetryKql`) and the answer's shape (`TelemetryShape`) |

Nothing a visitor reads changed: every cut moved bodies whole, the regions the records quote moved with them under the same names (the live fences were repointed), and the full suite passed after each cut. The largest type is now 294 lines (`RequestPipeline`). The test only counts; what a job is stays a judgement, made here one type at a time with the reason in each new file's header.


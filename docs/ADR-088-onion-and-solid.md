# ADR: Onion and SOLID, how this codebase holds them

Status: accepted, 2026-10-03. Written with the pass that put a test behind every ring, moved the auction's composition out of the endpoints, and added a Where it sits section to every record.

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

`api/TheYard.Tests/OnionTests.cs` reads the compiled assemblies with NetArchTest (the maintained fork, NetArchTest.eNhancedEdition). It sees method bodies and the types the compiler writes for lambdas and async methods, so a dependency hidden in a handler's body fails as surely as a using line would. A failure names the type and the dependency it found. The ring rules select by assembly and then by namespace, so a type in a nested folder still belongs to its ring. They allow the .NET base class library (BCL), the `System` namespaces, in every ring, and two rules take the disk, the network and the clock back out of it for Domain and Application. The two endpoint rules select by name, every class whose name ends in `Endpoints`, and the registration rule allows `Program` and classes ending in `Registration`; a handler class named otherwise would escape them, which is why the naming is part of the house style.

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

## Where it sits

Every ring: this record is the map of all of them and of the tests that hold them, and it serves all five SOLID principles by naming where each one shows and where it stops. It cost a new test package (NetArchTest), ten tests and a pass over every record. A codebase with one store and no rules worth testing alone would be built flat instead, as the section above says.

## Files

- [`api/TheYard.Tests/OnionTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/OnionTests.cs): the ten rules.
- [`api/TheYard.Application/Auction.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Auction.cs): the use cases the endpoints ask.
- [`api/TheYard.Domain/StandingRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/StandingRules.cs): the one rule for raising a price.
- [`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs): the auction's ports.
- [`api/TheYard.Api/Composition`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Composition): the composition root.
- [`docs/images/rings.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/rings.mjs): the drawing.


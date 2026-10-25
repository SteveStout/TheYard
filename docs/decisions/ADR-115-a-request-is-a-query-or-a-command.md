# ADR: A request is a query or a command

Status: accepted, 2026-10-23, at 1.0.4.39. Every request the API answers is a record named `<Verb><Noun>Query` or `<Verb><Noun>Command`, with exactly one handler that implements `IQueryHandler` or `ICommandHandler`. A query handler is handed reading ports only. Nothing in the Application ring is named `*Service`. `CqrsTests` fails the build when any of that stops being true.

## Reading list

1. Martin Fowler, [CQRS](https://martinfowler.com/bliki/CQRS.html) (2011): one model to update and another to read, and his warning that for most systems the full pattern adds risky complexity. Read 2026-10-23.
2. Microsoft Learn, [CQRS pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs) (updated 2025-02-21): the basic form keeps one store, and separate stores, events and eventual consistency are the advanced form with their own costs. Read 2026-10-23.
3. Greg Young, [CQRS Documents](https://cqrs.wordpress.com/wp-content/uploads/2010/11/cqrs_documents.pdf): CQRS as Meyer's command-query separation lifted from methods to objects, with event sourcing a pairing and never a requirement. Read 2026-10-23.
4. Jimmy Bogard, [Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/) (2018): organise by request, and the request is already a command or a query. Read 2026-10-23.
5. [ADR: The onion cut into vertical slices](https://theyard.stevenstout.biz/?doc=adr-the-onion-cut-into-vertical-slices) and [ADR: Onion and SOLID](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid): the slices and the rings this record adds a rule inside.
6. [CQRS, the practices](https://theyard.stevenstout.biz/?doc=cqrs): the same decision as a page to learn from, with the counts this build has.

## In plain words

Every request to this site either asks a question or asks for something to change. Asking which vehicles are live is a question. Placing a bid is a change. From this version the code says which one each request is, in its name, and the build refuses a question that has been handed the means to change anything.

That is the whole of it here. There is one database per store, as before, and no queue or bus between the two halves. The value is that a reader can tell from one word whether a piece of code can change anything, and a test holds that word to be true.

## Context

Measured at 1.0.4.38: 27 handlers in the Application ring, each a `sealed class <Verb><Noun>Handler` with a `HandleAsync`, and no type named `*Query` or `*Command`. A handler's request was a loose list of arguments or a record with a free name (`PlaceBid`, `VehicleSearch`, `BlogOpen`). Beside the handlers sat three services and the class that composed them: `BidService` in four files placed bids, reset them and read standings; `InventoryService` in five files was reads only; `MarketService` ran the simulated room; and `Auction` held all three, so every bid and vehicle handler took `Auction` and could reach every write any of them had.

The ports had the same shape. `IBlogStore`, `IAccountStore` and `ILinkOpens` each carried the reads and the writes in one interface, so the query that lists blog posts was handed the method that saves one.

## Decision

1. Two interfaces in `api/TheYard.Application/Shared/Requests.cs`, and nothing else is added: no bus, no mediator, no pipeline. A slice's `Module` maps its route to its handler, as it did.

```live path=api/TheYard.Application/Shared/Requests.cs region=requests
```

2. Every request is a sealed record in its slice's Application folder, named `<Verb><Noun>Query` when it reads and `<Verb><Noun>Command` when it changes something, and its handler is named the same with `Handler` in place of the last word. A request with nothing to say is an empty record (`ReadFacetsQuery`, `ListPostsQuery`) rather than a missing one, so every handler has the same shape.
3. A query handler's constructor takes no parameter whose type ends in `Store` or `Writer`. A command handler may take both sides.
4. A port that a query needs only half of is split: the reading side is an interface ending in `Reader` or `Catalogue`, and the writing side extends it or stands beside it. `IBlogStore : IBlogReader`, `IAccountStore : IAccountReader` and `ILinkOpens : ILinkOpensReader` in this version.
5. The three services and `Auction` go. Each store's auction is three parts, each implementing a reading and a writing interface (`api/TheYard.Application/Shared/Auction/AuctionParts.cs`): `Stock` (the catalogue, read only, so `IStockReader` alone), `BidBook` (`IBidReader`, `IBidWriter`) and `Room` (`IRoomReader`, `IRoomWriter`). `StandingReader` lays the three reading sides over each other in the one order the rules need, and is the only way a query reads a vehicle. The round of the room and a buyer's start-over move into their command handlers.
6. Nothing in the Application ring's root or its slices is named `*Service`.

One query, the three the catalogue answers, each taking the reading side only:

```live path=api/TheYard.Application/Features/Vehicles/VehicleHandlers.cs region=vehicle-handlers
```

One command, the bid, which reads the vehicle as it stands and writes through the bids' writing side:

```live path=api/TheYard.Application/Features/Bids/PlaceBidHandler.cs region=place-bid
```

The three parts and their two sides:

```live path=api/TheYard.Application/Shared/Auction/AuctionParts.cs region=auction-parts
```

## What holds it

`CqrsTests`, new, red on 1.0.4.38 with only its file applied:

```live path=api/TheYard.Tests/CqrsTests.cs region=cqrs-tests
```

- every handler implements exactly one of the two interfaces and is named for its request;
- every `*Query` and `*Command` under `Features/` has exactly one handler, is sealed, and is handled as what its name says;
- a query handler is handed no store and no writer;
- no type in the Application ring's root or its slices is named `*Service`;
- every route that asks a handler asks a query or a command handler, beside `HandlerRuleTests`, which holds a ruled route to one handler;
- the counts on [CQRS, the practices](https://theyard.stevenstout.biz/?doc=cqrs) are this build's.

`HandlerRuleTests.Every_handler_is_tested_with_fakes_and_no_host` covers the renamed handlers unchanged: each is still built in a test with fakes at its ports and no host.

## What was considered

MediatR, or a hand-written dispatcher. Rejected: it puts a lookup between the route and the code it runs, so "go to definition" on a route lands on an interface. The `Module` already maps one route to one handler and the compiler checks it; a bus would add a moving part to say what the map says now.

A second store for reads, or events between the halves. Rejected for this codebase. The catalogue is one hundred thousand rows a page at a time, read by the store that holds it (ADR: The store does the work), and both stores answer a page inside the budget the site already keeps. Microsoft's own page names the cost: two stores that must be kept in step, and a read side that can be behind. Nothing here is slow enough to buy that.

Keeping the services and naming their methods as queries and commands. Rejected: the point of the split is what a handler can reach. A query handed `Auction` could place a bid whatever its name said; a query handed `StandingReader` cannot.

Moving every line of `BidService` into the three bid handlers. Rejected for this version: the book of bids is one instance per store that the bid, the reset, the room and every listing share, with a gate and a replay from the store at startup. Its body moved whole and its two sides are now interfaces; cutting it by job is the 300-line rule's rung (ADR-088's addendum of that version).

## Consequences

Every handler and its request were renamed, so the diff is wide and shallow: 27 records added, 27 handlers renamed or re-typed, the route bodies changed only in the record they build. The three services' files were renamed (`BidService*.cs` to `BidBook*.cs`, `InventoryService*.cs` to `Stock*.cs`, `MarketService.cs` to `Room.cs`), and every live fence and link that named them was repointed; prose in older records that names them was left as written. `Auction.cs` is gone; its two regions that records quote, `overlays` and `views`, live in `StandingReader.cs` under the same names. The handler tests build each handler over the three parts the way the slices' Modules do.

What this does not do: the 24 Admin reads that `HandlerRuleTests` still names as exempt ask no handler yet. They become queries in the next version (ADR-075's row for this record says so), and from then every route is one of the two.

## Where it sits

Application ring only, and the host's Modules that build the handlers. Single responsibility: ADR-107 says what a slice is and ADR-114 where a file lives; this record says what a request is. What would change it: a read that the store holding the writes cannot answer in time, which is the case Microsoft's page gives for a second store.

## Files

- [`api/TheYard.Application/Shared/Requests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Requests.cs) - the two interfaces (new)
- [`api/TheYard.Application/Shared/Auction/AuctionParts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/AuctionParts.cs) - the auction's three parts, a reading and a writing side each (new)
- [`api/TheYard.Application/Shared/Auction/StandingReader.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/StandingReader.cs) - a vehicle as every query reads it (new; what `Auction` read)
- [`api/TheYard.Application/Shared/Auction/BidBook.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/BidBook.cs), [`Stock.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/Stock.cs), [`Room.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/Room.cs) - the three services, renamed, with their sides
- [`api/TheYard.Application/Features/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Application/Features) - every request and its handler
- [`api/TheYard.Tests/CqrsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CqrsTests.cs) - holds this record (new)
- [`docs/best-practices/CQRS.md`](https://github.com/SteveStout/TheYard/blob/main/docs/best-practices/CQRS.md) - the practices page (new)

## Addendum, 2026-10-23 (1.0.4.43): the Admin tab's 24 reads are queries

The record's first version left the Admin tab's reads asking no handler, named as exempt in `HandlerRuleTests`. From 1.0.4.43 each is a query or a command with one handler: 21 queries (the SQL, store and log rings, the timings, the kept rings, the activity report, the visitor rows, the kept log, the machines, memory against traffic, Azure's view of this container, the peer, the telemetry, the arrival, the store bar, the page sweep, the gate's counts and its file, the proof's status, the costs, and a served document) and three commands (start the page sweep, start the proof, run the partition key experiment), in `Application/Features/Admin/Operations`, `Proof`, `Admin/Costs` and `Docs`. Each handler takes a port split by what it reads (`IRingReadings`, `IActivityReadings`, `IMachineReadings`, `IAzureReadings`, `IEvidenceReadings` and the rest), answered by the host part that holds the data (`OperationsReadings.cs`, `MachineCardReadings.cs`, `DocumentReadings.cs`): the bodies are the routes' bodies, moved, so every card reads what it read before. What a route decided in its body, a handler decides now: the window, the card and the kind it can read, and what to say when there is nothing; the answer is one `Reading` the route turns into its status. The operator's key and whether the visitor rows are served stay checks the route makes before it asks (`AdminSettings` joins `AdminKey` beside the handlers).

`AdminQueryTests` builds all 24 handlers over one fake at every port with no host, and `AdminQueryRouteTests` holds the store bar's 200, the gate's counts' 200 and 404 and a document's 200 and 404 through the real host. `Reading`, the answer every one of these handlers gives, is in the Application ring's `Shared/` folder, because the operations, the costs, the proof and the documents all answer with it and rule 1 of the slices keeps one slice out of another's folder. Steve's ruling of 11:37 that day was the reason: the Foreman reads exactly these routes, and a read with no handler has no fake to test against.

Still exempt, each with its reason in `HandlerRuleTests`: fifteen routes where the host serves its own files (the pictures, the drawings, the Bicep, the resume, the about page, the recommendations), its health and version, the pulse, and its error rings. They are not reads of the business or of the Admin cards, and they stay where they are until a lane gives them a reason to be queries.


## Addendum, 2026-10-25 (1.0.4.68): a reader port is known by what it can do

The rule that keeps a store out of a query read the port's name: a parameter type ending in Store or Writer was red. A port named "...Reader" that wrote passed. From this version `CqrsTests.A_query_handler_is_handed_no_port_that_can_write` reads the port itself: no type a query handler takes by constructor, among this codebase's own, has a member, counting every member of every interface it inherits, whose name starts with Keep, Write, Save, Upsert, Delete, Add, Remove, Record, Put or Set. The name check stays beside it.

It was green on 1.0.4.67 with only its file applied, and that green is the finding: the ports the queries take were already split, the visitors' reader from its store (`IVisitorReader` under `IVisitorStore`) and the blog's reader from its store among them. To prove it can fail, a writing member planted on `IVisitorReader` turned it red at once ("ReadPeopleCountHandler takes IVisitorReader visitors, which can KeepProbeAsync"). The test carries an exemption list, empty, that only shrinks. docs/best-practices/CQRS.md says how the rule reads a port.

```live path=api/TheYard.Tests/CqrsTests.cs region=a-reader-is-known-by-what-it-can-do
```

## Addendum, 2026-10-25 (1.0.4.75): what CQRS means in the job hunt's Function

The Function answers no request. Its timers each run one use case on a clock, so there is no request record to name `...Query` or `...Command` and no handler to count, and this record's naming rule does not carry over. The half that does carry over is the one that matters: a use case that only reads cannot write.

- **The ports are split.** `IMergeStore` (the job-hunt share) extends `IShareReader` (exists, read, list), and `IInbox` extends `IInboxReader` (find, read, the thread's senders and draft); the archive and the share's writes are on the extending halves only.
- **The reading use cases take the reading halves.** `InboxTriage` (the triage window) takes `IShareReader` and `IInboxReader`; `MergerHealth.ReadAsync` takes `IShareReader`.
- **Nothing is a service** in any of the three rings.

The Function's `CqrsTests` holds all three, reading a port by what it can do with the same writing-verb rule the site's queries are held to (the addendum of 1.0.4.68), widened by the Function's own verbs (Append, Move, Replace, Ensure, Archive, Mark, Block). It was red with the split reverted: "InboxTriage takes IMergeStore store, which can AppendAsync, EnsureFolderAsync, MoveAsync, ReplaceAsync, WriteAsync" and "InboxTriage takes IInbox inbox, which can ArchiveAsync".

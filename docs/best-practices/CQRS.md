# CQRS, the practices

TheYard is a used-vehicle auction platform built in the open: a .NET 10 API, a React front end, and
two databases underneath it. Every request its API answers is either a query or a command, and the
record of that decision, with what was weighed and what was left out, is
[ADR: A request is a query or a command](https://theyard.stevenstout.biz/?doc=adr-a-request-is-a-query-or-a-command).
This page is the practical half: the shape, shown with whole classes read from the running build, the
counts this build has, and what this codebase deliberately does not do.

## In plain words

A request either asks a question or asks for something to change. CQRS, command query responsibility
segregation, means writing those two kinds of request as two kinds of code. Here a question is a
*query* and a change is a *command*; each has exactly one piece of code that answers it, a *handler*,
and a query's handler is never handed anything that can write.

What that is worth: a developer can tell from one word whether a piece of code can change anything,
and can test a query without fearing it saves something; the organization gets that held by the build
on every push rather than by a reviewer reading carefully.

### The words this page uses

- **Query**: a request that reads and changes nothing. `ReadFacetsQuery`, `FindVehicleQuery`.
- **Command**: a request that changes something and may answer what it did. `PlaceBidCommand`,
  `ResetBidsCommand`.
- **Handler**: the one class that answers one request. Its name is the request's with `Handler` in
  place of the last word.
- **Port**: an interface the Application ring declares and an adapter implements, such as a store.
  A *reader* port only reads; a *store* or *writer* port can write.

## The two interfaces, and nothing else

The whole mechanism is two interfaces. There is no bus, no mediator library and no pipeline: the slice's
`Module` maps a route to its handler, the compiler checks the types, and "go to definition" on a route
lands on the code that runs.

[`api/TheYard.Application/Shared/Requests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Requests.cs):

```live path=api/TheYard.Application/Shared/Requests.cs region=requests
```

Greg Young's documents trace the idea to Bertrand Meyer's command-query separation, a method that
either does something or answers something, lifted from methods to objects
([CQRS Documents](https://cqrs.wordpress.com/wp-content/uploads/2010/11/cqrs_documents.pdf)). That
lift is all this codebase takes.

## A query is handed the reading side only

The catalogue's three queries. Each takes `StandingReader` or `IStockReader`, neither of which has a
method that writes, so a listing cannot place a bid however it is changed later.

[`api/TheYard.Application/Features/Vehicles/VehicleHandlers.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Vehicles/VehicleHandlers.cs):

```live path=api/TheYard.Application/Features/Vehicles/VehicleHandlers.cs region=vehicle-handlers
```

`StandingReader` is how every query reads a vehicle: the catalogue, then everybody's bids, then the
simulated room, in the one order the price rules need, from the reading side of each.

[`api/TheYard.Application/Shared/Auction/StandingReader.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/StandingReader.cs):

```live path=api/TheYard.Application/Shared/Auction/StandingReader.cs region=*
```

## A command takes the side it writes through

The bid. It reads the vehicle as it stands through the same reader the queries use, then writes
through the bids' writing side, and nothing else.

[`api/TheYard.Application/Features/Bids/PlaceBidHandler.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Bids/PlaceBidHandler.cs):

```live path=api/TheYard.Application/Features/Bids/PlaceBidHandler.cs region=place-bid
```

## Split a port where a query needs half of it

This is Interface Segregation from SOLID, made a rule by CQRS. A port that carries reads and writes in
one interface is split so the query can be handed the read half. The blog's store, the read half:

[`api/TheYard.Application/Features/Blog/BlogStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Blog/BlogStore.cs):

```live path=api/TheYard.Application/Features/Blog/BlogStore.cs region=blog-reader
```

And the whole store, which extends it with the three writes and is handed to the commands:

```live path=api/TheYard.Application/Features/Blog/BlogStore.cs region=blog-store
```

The auction's three parts are split the same way, one reading and one writing interface each:

[`api/TheYard.Application/Shared/Auction/AuctionParts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/AuctionParts.cs):

```live path=api/TheYard.Application/Shared/Auction/AuctionParts.cs region=auction-parts
```

## No services

A class named `BidService` is a bag of use cases behind one name: it placed bids, reset them and read
standings, and anything handed it could do all three. Each use case is a handler now, and the shared
state the handlers need is a part with two sides. Jimmy Bogard's vertical slices make the same move:
organise by request, and the request is already a command or a query, so shared layers of services and
repositories mostly fall away
([Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/)).

## The test that holds it

[`api/TheYard.Tests/CqrsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CqrsTests.cs):

```live path=api/TheYard.Tests/CqrsTests.cs region=cqrs-tests
```

## How the rule reads a port

A port's name is a promise and nothing checks a promise. From 1.0.4.68 the rule reads what a port can do: every type a query handler takes by constructor, among this codebase's own, is read by reflection with every interface it inherits, and a member whose name starts with a writing verb (Keep, Write, Save, Upsert, Delete, Add, Remove, Record, Put or Set) makes it a port that writes. A query handler handed one is red, whatever the port is called. The name check stays beside it.

```live path=api/TheYard.Tests/CqrsTests.cs region=a-reader-is-known-by-what-it-can-do
```

## The counts this build has

Read back by `CqrsTests` on every build, so a number here cannot go stale.

| shape | count |
| --- | --- |
| queries | 38 |
| commands | 18 |
| handlers | 56 |
| services | 0 |

## What this codebase does not do, and why

No second store for reads, no events between a write side and a read side, no event sourcing and no
bus. Martin Fowler's page warns that for most systems the full pattern adds risky complexity and keeps
it to the parts of a system that need it ([CQRS](https://martinfowler.com/bliki/CQRS.html)), and
Microsoft's pattern page is plain that the basic form shares one store and that separate stores bring
synchronisation and data that can be behind
([CQRS pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)). Here each store
answers a page of a hundred thousand vehicles inside the site's budget, and the bid that changes a
price has to be read back at once by the buyer who placed it. Two stores would buy nothing this site
needs and cost the one thing a bid must have, a price that is current. The Admin tab's 24 reads became
queries in 1.0.4.43; what is still exempt is the host serving its own files, its health and its error
rings, fifteen routes named in `HandlerRuleTests` with the reason for each.

## Sources

- Martin Fowler, [CQRS](https://martinfowler.com/bliki/CQRS.html), 2011. Read 2026-10-23.
- Microsoft Learn, [CQRS pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs), updated 2025-02-21. Read 2026-10-23.
- Greg Young, [CQRS Documents](https://cqrs.wordpress.com/wp-content/uploads/2010/11/cqrs_documents.pdf). Read 2026-10-23.
- Jimmy Bogard, [Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/), 2018. Read 2026-10-23.

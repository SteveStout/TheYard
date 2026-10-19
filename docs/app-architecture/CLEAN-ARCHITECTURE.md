# Onion and Clean Architecture

TheYard is a used-vehicle auction platform built in the open: a .NET 10 API, a React front end and
two databases underneath it. Its server is built as an onion. This page says what that pattern
means, where it came from, and how TheYard applies it ring by ring, project by project. Two other
pages carry the rest: [Onion architecture, the practices](https://theyard.stevenstout.biz/?doc=onion)
walks one bid through the rings with whole files you can copy, and
[ADR: Onion and SOLID, how this codebase holds them](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid)
is the record of the decision, with the ten rules the build runs. This page is the one to read first.

## In plain words

Imagine the code as rings, one inside another. The auction's rules sit in the middle. The code that
carries out what a visitor asks for wraps the rules. The database code and the web endpoints sit on
the outside. One rule holds it together: code may use the rings inside it and never the ones outside
it. So the rules in the middle know nothing about SQL Server, Cosmos DB or HTTP, and they can be
tested in milliseconds with no database running.

What that is worth: a developer can swap the database, the web framework or the hosting without
touching the auction's rules, and a new engineer can find any rule by asking which ring it belongs
to. The organization gets rules that are tested on every push without a database, and a shape the
build defends instead of one that depends on a reviewer noticing.

## The words this page uses

- **Ring**: one layer of the code. In TheYard each inner ring is its own .NET project.
- **Dependency**: code in one place using a type from another, through a project reference, a
  `using` line, or a call inside a method body.
- **The Dependency Rule**: dependencies point inward. Robert C. Martin's name for the one rule
  every version of this pattern shares.
- **Domain**: the rules of the business, here the auction's: who may bid, how much, and when.
- **Use case**: one thing a visitor does, such as placing a bid, written as a method that runs the
  rules and the stores in the right order.
- **Port**: an interface the inner code declares for something it needs from outside, such as a
  place to keep bids.
- **Adapter**: a class in an outer ring that implements a port against one real thing, such as SQL
  Server or Cosmos DB.
- **Host**: the web application itself: the endpoints that read a request and write the answer.
- **Composition root**: the one place, at startup, where the real classes are created and handed to
  the code that needs them.
- **BCL**: the .NET base class library, the `System` namespaces every .NET program has.

## What it means

Three names, one rule. The pattern has been described three times under three names, and all three
agree on the sentence that matters.

- **Hexagonal architecture, or ports and adapters** (Alistair Cockburn, 2005). The application
  should be driven equally well by a person, a test, a script or another program, and developed and
  tested apart from its databases and devices. The application declares ports, the interfaces it
  offers and the interfaces it needs, and adapters plug the real world into them.
- **Onion architecture** (Jeffrey Palermo, 2008). The same idea drawn as rings: the domain model at
  the centre, domain services around it, application services around those, and the user interface,
  the infrastructure and the tests in the outermost ring. Code may depend on rings more central than
  itself and never on rings further out.
- **Clean architecture** (Robert C. Martin, 2012). Entities at the centre, then use cases, then
  interface adapters, then frameworks and drivers, and one rule, the Dependency Rule: source code
  dependencies point only inward.

What each ring holds, in the textbook:

| Ring, innermost first | What lives there | Why it sits where it does |
| --- | --- | --- |
| Entities, the domain model | The business rules that would exist with no software at all. | Changes least, and nothing it needs lives outside. |
| Use cases, the application | What the software does with those rules, in order, and the ports it needs. | Depends on the rules, and on nothing about how they are stored or shown. |
| Interface adapters | Translators: controllers that read requests, gateways and repositories that talk to a database. | Each knows one real thing, and the rings inside it know none of them. |
| Frameworks and drivers | The web framework, the database engine, the browser. | The parts most likely to be replaced, kept furthest from the rules. |

What you get for building this way:

- **Rules tested alone.** The centre has no database, no network and no clock of its own, so a test
  can run a thousand rule checks in the time one database query takes.
- **Seams.** A database, a framework or a hosting platform is replaced at a port, and the rings
  inside the port do not change.
- **A shape the compiler helps defend.** When rings are projects, a project reference that would
  point outward makes a cycle, and the compiler refuses it.
- **A map.** A new engineer who knows the rings knows where a rule lives before opening a file.

What it costs: more projects and more interfaces, and a bid passes through more hands on its way
to the database. The rings pay that back only when there is something to protect. When there is
not, the record says so under
[When not to build it this way](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid#when-not-to-build-it-this-way):
a small service over one table, a spike, or a script is clearer flat.

## How it applies to TheYard

The textbook rings and TheYard's projects, side by side. Each textbook ring is one project here,
with one deliberate difference: the centre is split in two, because a ring with no behaviour at all
is a ring nothing can hide in.

[![Palermo's onion beside TheYard's rings: each textbook ring tied to the project that is that ring, every dependency pointing inward](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/onion-side-by-side.png)](https://theyard.stevenstout.biz/api/docs/diagrams/onion-side-by-side)

*A preview. [Open the drawing in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/onion-side-by-side) to zoom in and follow the tie lines.*

| Textbook ring | TheYard project | What lives there | May use |
| --- | --- | --- | --- |
| The domain model | `api/TheYard.Data` | `Vehicle` and `PhotoEntry`, exactly as the dataset has them. Records with no behaviour. | the BCL only |
| Domain services, the rules | `api/TheYard.Domain` | `BidRules`, `AuctionSchedule`, `AuctionClock`, `StandingRules`, `VehicleFilter`, `VehicleOrdering`, `VehicleSearchIndex`, `PhotoGallery`, and the `Fnv1a` hash the schedule is derived from. Pure functions of their inputs and the clock they are handed. | Data |
| Application services, the use cases | `api/TheYard.Application` | `Auction`, the one class an endpoint asks about the auction, composing `InventoryService`, `BidService` and `MarketService`; the auction's ports and the operator's ports. | Domain, Data |
| Infrastructure, the adapters | `api/TheYard.Infrastructure` and `api/TheYard.Infrastructure.Cosmos` | The same ports implemented twice: EF Core over Azure SQL Database or SQLite, and the Cosmos DB SDK. | Application; the document store adapter also borrows one shared user type from the relational one |
| User interface and host | `api/TheYard.Api`, with `src/` and `render/` | Endpoints that read a request, ask `Auction` and write the answer; the composition root in `Composition/`; the React front end and the service that draws it on the server, both clients of the API. | Application and both adapters; the front end, the wire only |
| Tests | `api/TheYard.Tests` | Rules with fixed clocks, use cases with hand-written fakes at the ports, the real host through `WebApplicationFactory`, and the ten tests that hold the rings. | everything |

### The arrows are project references

Each inward arrow in the drawing is one `ProjectReference` line in a project file, which is why the
compiler helps hold the shape: a reference pointing the wrong way would close a cycle, and the build
refuses it. The four project files, read from the running build:

```live path=api/TheYard.Domain/TheYard.Domain.csproj region=*
```

```live path=api/TheYard.Application/TheYard.Application.csproj region=*
```

```live path=api/TheYard.Infrastructure.Cosmos/TheYard.Infrastructure.Cosmos.csproj region=*
```

```live path=api/TheYard.Api/TheYard.Api.csproj region=*
```

Notice what is missing from the first two: no Entity Framework, no Cosmos DB SDK, no ASP.NET. The
rules and the use cases compile against the base class library and each other, and nothing else.

### The ports are declared on the inside

Application needs to save a bid, and the database is outside it. So Application declares the
interface it needs, in its own words, and each adapter implements it. At compile time every arrow
still points inward, at the interface. The auction's ports, read from the running build:

```live path=api/TheYard.Application/Ports.cs region=ports
```

The names say what the inner ring needs (`IBidStore`) and nothing about the technology that answers
(`ISqlBidRepository` would leak it). The two adapters that implement them, and the one place that
decides which adapter a store gets, are shown whole on the practices page under
[The inner ring owns the interface](https://theyard.stevenstout.biz/?doc=onion#the-inner-ring-owns-the-interface).

### One bid, through the rings

A signed-in buyer bids on a vehicle. The endpoint reads the request and asks `Auction`; `Auction`
hands the bid to `BidService`, which asks `BidRules` whether it stands; the rules answer with a
`BidOutcome` and never touch a database; on yes, `BidService` saves through `IBidStore` and only
then records the bid; whichever adapter was wired in at startup writes the row or the document; the
endpoint writes the answer. Nothing in the host decides anything, and nothing in the rules knows
which store it is talking to.

[![One bid through TheYard's rings: the endpoint asks Application, Application asks Domain, Domain decides, Application saves through the IBidStore port, an adapter writes the store, and the endpoint answers](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/bid-walk.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/bid-walk)

*A preview. [Open the bid's walk in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/bid-walk) to zoom in and follow it.*

The seven steps, with the four files the bid passes through shown whole, are on the practices page
under [A request walks the rings](https://theyard.stevenstout.biz/?doc=onion#a-request-walks-the-rings).

### The front end keeps the same shape

`src/` is drawn in the same direction. Components use hooks, hooks use `src/lib`, and `src/lib`
imports nothing from React, so the logic that formats a price or reads the address bar can be
tested with no component rendered. Every file in `src/app` and `src/library` opens with a header
that says what it does, what it does not, and who uses it, and a test reads the header back against
the real importers
([ADR: The React configuration, explained](https://theyard.stevenstout.biz/?doc=adr-react)).

## What holds the shape

A rule held by code review alone slips the first busy week. TheYard's rings are held by ten tests
that read the compiled assemblies with NetArchTest, so a dependency hidden inside a lambda or an
`async` method fails the build as surely as a `using` line would. The six rules for the rings, read
from the running build:

```live path=api/TheYard.Tests/OnionTests.cs region=rings
```

The four rules for the host, what each of the ten stops, and which four failed against the code
the day they were written are in the record, under
[How the build holds them](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid#how-the-build-holds-them).

```readouts
Architecture tests | {{live:facts api/TheYard.Tests/OnionTests.cs}}
Files with the header | {{live:headers}}
```

Numbers read from the build when the page is opened.

## Read next

- [SOLID Principles](https://theyard.stevenstout.biz/?doc=solid): the five principles the rings
  rest on, and where each one shows in this code.
- [Onion architecture, the practices](https://theyard.stevenstout.biz/?doc=onion): each practice
  with a whole file to copy, including the rings as folders in one project.
- [ADR: Onion and SOLID, how this codebase holds them](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid):
  the record, the ten rules, and what is kept on purpose.

## References

- [Alistair Cockburn, Hexagonal architecture](https://alistair.cockburn.us/hexagonal-architecture/)
  (2005): the original ports and adapters article, and the intent this page opens on, an
  application driven equally by people, tests and other programs.
- [Jeffrey Palermo, The Onion Architecture: part 1](https://jeffreypalermo.com/2008/07/the-onion-architecture-part-1/)
  (2008): the rings, and the case for putting interfaces in the core.
- [Robert C. Martin, The Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
  (2012): the Dependency Rule, and the four rings this page's table follows.
- [Common web application architectures, .NET architecture guides](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures):
  Microsoft's description of clean architecture in .NET, with abstractions in the core and
  implementations in the infrastructure layer.

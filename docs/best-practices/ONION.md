# Onion architecture, the practices

TheYard is a used-vehicle auction platform built in the open: a .NET 10 API, a React front end, and
two databases underneath it. Its server is built as an onion, and the record of that decision, with
every ring, every rule the build runs and what each one stops, is
[ADR: Onion and SOLID, how this codebase holds them](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid).
This page is the practical half. Each section is one practice you can copy, shown with a drawing or a
whole file read from the running build, and each section has an address of its own so a link can
land on it.

## In plain words

The server is built in rings. The auction's rules sit in the middle, the code that carries out what a
visitor asks for wraps them, and the database code and the web endpoints sit on the outside. Code may
use the rings inside it and never the ones outside it. This page follows one bid through those rings,
shows how the inner ring can save a bid without knowing which database it is talking to, why the
rules never read the clock, how ten tests keep the shape from slipping, and how a small codebase gets
the same shape from folders instead of projects.

What that is worth: a developer can copy each practice into their own codebase this week, and the
organization gets business rules that are tested in milliseconds with no database, plus a structure
the build defends on every push instead of one that depends on a reviewer noticing.

### The words this page uses

- **Ring**: one layer of the code. Here each inner ring is its own .NET project; in a smaller
  codebase a ring can be a folder.
- **Dependency**: code in one place using a type from another, through a project reference, a
  `using` line, or a call inside a method body.
- **Domain**: the rules of the business, here the auction's: who may bid, how much, and when.
- **Use case**: one thing a visitor does, such as placing a bid, written as a method that runs the
  rules and the stores in the right order.
- **Port**: an interface the inner code declares for something it needs from outside, such as a
  place to keep bids.
- **Adapter**: a class in an outer ring that implements a port against one real thing, such as SQL
  Server or Cosmos DB.
- **Host**: the web application itself, the endpoints that read a request and write the answer.
- **Composition root**: the one place, at startup, where the real classes are created and handed to
  the code that needs them.
- **BCL**: the .NET base class library, the `System` namespaces every .NET program has.

## The rule in one sentence

**Dependencies point inward: code may use the rings inside it and never the ones outside it.**

[![TheYard's rings: Data at the centre, then Domain, then Application, with the host and the adapters outside, every dependency pointing inward](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/rings.png)](https://theyard.stevenstout.biz/api/docs/diagrams/rings)

*A preview. [Open the rings diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/rings) to zoom in and follow it.*

Innermost first: Data (the dataset's shapes, with no behaviour), Domain (the rules), Application
(the use cases and the ports), and outside them the adapters and the host. What lives in each ring
is listed in the record, under
[The rings](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid#the-rings), and is not
repeated here.

Jeffrey Palermo, who named the pattern in 2008, put the same rule this way: "All code can depend on
layers more central, but code cannot depend on layers further out from the core." Everything else on
this page is a way of keeping that sentence true once the code is large and the week is busy.

## A request walks the rings

The rule is easiest to see by following one request. A signed-in buyer bids on a vehicle with
`POST /api/vehicles/{id}/bids`, and the code meets the rings in this order:

[![One bid through TheYard's rings: the endpoint asks Application, Application asks Domain, Domain decides, Application saves through the IBidStore port, an adapter writes the store, and the endpoint answers](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/bid-walk.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/bid-walk)

*A preview. [Open the bid's walk in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/bid-walk) to zoom in and follow it.*

1. **The host reads.** The endpoint checks that the session belongs to this store (401 if not) and
   that the vehicle exists (404 if not), reads the server's clock once, and asks `Auction`, the one
   class an endpoint asks about the auction.
2. **Application composes.** `Auction` hands the bid to `BidService`, which keeps everybody's bids. It
   lets one bid through at a time and folds everybody's bids into the vehicle, so the rules see the
   price as it stands.
3. **Application asks Domain.** `BidService` calls `BidRules.ResolveBid` with the vehicle, the amount,
   the clock and whether anybody has already bought it.
4. **Domain decides.** The answer is a `BidOutcome`: refused because the vehicle is sold, the auction
   is not live or the bid is under the minimum; won at the buy-now price; or accepted. It is a pure
   function, so the same inputs give the same answer every time.
5. **Application saves through the port.** On yes, `BidService` calls `IBidStore.SaveAsync` and only
   then records the bid in memory, so a failed write means the bid did not happen anywhere.
6. **An adapter writes.** Whichever adapter the composition root wired in at startup, for SQL or for
   Cosmos DB, writes the row or the document.
7. **The host answers.** A refusal becomes a 400 with the reason; a bid that stands becomes a 200 with
   the bid and the vehicle as it now stands.

**The practice:** an endpoint reads the request, asks one use case, and writes the answer. It does not
decide anything and does not put services together itself. Two of the ten architecture tests hold that
(`Endpoints_never_reach_a_store_or_the_service_container` and
`Endpoints_reach_the_auction_only_through_the_Application_ring`).

The code in the order the bid meets it: three files whole, and the one method between them. The
endpoint,
[`api/TheYard.Api/Endpoints/BidEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Endpoints/BidEndpoints.cs):

```live path=api/TheYard.Api/Endpoints/BidEndpoints.cs region=*
```

The use cases an endpoint asks,
[`api/TheYard.Application/Auction.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Auction.cs):

```live path=api/TheYard.Application/Auction.cs region=*
```

The method that asks the rules and then calls the port, from the part of `BidService` that moves a
price,
[`api/TheYard.Application/BidService.Bidding.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/BidService.Bidding.cs):

```live path=api/TheYard.Application/BidService.Bidding.cs region=place
```

The rules,
[`api/TheYard.Domain/BidRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/BidRules.cs).
Nothing in it is `async`, nothing in it reads a file, and the clock arrives as an argument:

```live path=api/TheYard.Domain/BidRules.cs region=*
```

## The inner ring owns the interface

The rule in one sentence has an obvious problem. Application has to save a bid, and the database is
outside it. The answer is dependency inversion, the D in SOLID: the inner ring declares the interface
it needs, in its own words, and the outer ring implements it. At compile time every arrow points
inward, at the interface. Only the call at runtime travels outward, and it travels through a type the
inner ring owns.

[![The inner ring owns the interface: Application declares IBidStore and uses it, EfBidStore and CosmosBidStore implement it from the outer ring, and the composition root decides which one each store gets](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/port-adapter.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/port-adapter)

*A preview. [Open the port and its adapters in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/port-adapter) to zoom in and follow it.*

Microsoft's architecture guide for .NET describes the same move: "defining abstractions, or
interfaces, in the Application Core, which are then implemented by types defined in the
Infrastructure layer." TheYard runs it twice over, because two stores sit behind the same three
ports, and the rules have no idea which one is answering.

**The practice:** name the port for what the inner ring needs (`IBidStore`) and keep the technology
out of the name (`ISqlBidRepository` would leak it), and keep every type the port mentions inside the
inner ring. Give
it a do-nothing implementation beside it, as `NullBidStore` is here, and the use cases can be tested
with no database and no mocking framework.

The ports, whole,
[`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs):

```live path=api/TheYard.Application/Ports.cs region=*
```

The relational adapters, whole: EF Core over Azure SQL Database or SQLite,
[`api/TheYard.Infrastructure/EfSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/EfSources.cs).
`EfBidStore` is the last class in the file:

```live path=api/TheYard.Infrastructure/EfSources.cs region=*
```

The document store adapters, whole: the Cosmos DB SDK,
[`api/TheYard.Infrastructure.Cosmos/CosmosSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosSources.cs).
`CosmosBidStore` is the last class in this one too:

```live path=api/TheYard.Infrastructure.Cosmos/CosmosSources.cs region=*
```

And the one place that names an adapter: the composition root,
[`api/TheYard.Api/Composition/StoreRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/StoreRegistration.cs),
building the relational store's backend and then the document store's. Look for
`new BidService(new EfBidStore(contexts))` in the first and `new BidService(new CosmosBidStore(store))`
in the second:

```live path=api/TheYard.Api/Composition/StoreRegistration.cs region=sql-backend
```

```live path=api/TheYard.Api/Composition/StoreRegistration.cs region=cosmos-backend
```

## The rules take the clock as a value

A rule that reads `DateTime.UtcNow` for itself gives a different answer every time it runs, and a
test of it can only check what happens "now". A rule that is handed the time gives the same answer for
the same inputs, so a test can pin any moment it likes: one millisecond before an auction opens, the
millisecond it opens, the last millisecond before it ends.

TheYard reads the clock at the edge, in the host. Its
[`api/TheYard.Api/Clocks.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Clocks.cs)
turns the current moment into an `AuctionClock` (now, and the UTC midnight the day's auctions are
scheduled from), and every endpoint that needs the time passes that value inward:

```live path=api/TheYard.Api/Clocks.cs region=*
```

The clock itself is a value in Domain,
[`api/TheYard.Domain/AuctionClock.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/AuctionClock.cs):

```live path=api/TheYard.Domain/AuctionClock.cs region=*
```

And the rule that uses it, whole,
[`api/TheYard.Domain/AuctionSchedule.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/AuctionSchedule.cs).
Every auction's opening and closing times are worked out from the vehicle's id and the anchor, so
nothing about the schedule is stored, and the status is a comparison of two numbers:

```live path=api/TheYard.Domain/AuctionSchedule.cs region=*
```

One test, from
[`api/TheYard.Tests/AuctionScheduleTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AuctionScheduleTests.cs),
pins the moment one millisecond either side of each edge of a window. `Clock` is built once at the top
of the class from a fixed date, so the anchor never depends on the day the test runs. With a clock the
rule read for itself, this test could not be written:

```live path=api/TheYard.Tests/AuctionScheduleTests.cs region=boundaries
```

**The practice:** read the clock once, at the edge, and pass it inward as a value. The build holds
this one too: `Domain_never_reaches_the_disk_the_network_or_the_clock` reads every method body in
Domain for a call to `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today` or their `DateTimeOffset`
twins, and fails on the first it finds. .NET 8 added `TimeProvider` for code that needs a clock it can
swap, and the record says why a plain value is enough here
([What is kept on purpose](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid#what-is-kept-on-purpose)).

## The build holds the rings

A rule held by code review alone slips the first busy week. TheYard's is held by ten tests that read
the compiled code with NetArchTest, a library that opens .NET assemblies (the compiled `.dll` files)
and checks which types use which. Reading compiled code matters: it sees method bodies and the hidden
types the compiler writes for lambdas and `async` methods, so a dependency tucked inside a lambda
fails the build as surely as a `using` line would. This repository uses the maintained fork,
NetArchTest.eNhancedEdition.

The ten rules, each with what it stops, are a table in the record, under
[How the build holds them](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid#how-the-build-holds-them).
Each one was watched failing before it was trusted. Four failed against the code as it stood when
they were written:

- the two endpoint rules found `IServiceProvider` in the account and admin handlers, and the bid
  services in the bid, vehicle and admin handlers;
- the document store rule found the Cosmos DB adapter using the relational adapter's `DatabaseState`;
- the database rule found three queries in the host, one of them in a lambda inside the composition
  root.

The other six were proved by planting a forbidden reference and watching each go red. The whole file,
[`api/TheYard.Tests/OnionTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/OnionTests.cs):

```live path=api/TheYard.Tests/OnionTests.cs region=*
```

### Watch one go red on your own repository

1. Add the package to your test project: `dotnet add <your test project> package NetArchTest.eNhancedEdition`.
2. Write one rule: your Domain project may depend on `System` and on itself, and nothing else (the test is below).
3. Run `dotnet test` and watch it pass.
4. Plant a forbidden reference: add `Microsoft.Extensions.Logging.Abstractions` to the Domain project and write `_ = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;` in any Domain method.
5. Run `dotnet test` again: it fails and names the type and the dependency it found. Delete the line, and keep the test.

```csharp
using NetArchTest.Rules;
using Xunit;

public sealed class OnionTests
{
    [Fact]
    public void Domain_depends_on_nothing_outside_the_BCL()
    {
        // Every type in the Domain assembly, including the ones the compiler writes for lambdas and
        // async methods, may use the base class library and Domain itself. Nothing else.
        var result = Types.InAssembly(typeof(YourApp.Domain.SomeRule).Assembly)
            .ShouldNot().HaveDependencyOtherThan("System", "YourApp.Domain")
            .GetResult();

        // A red run names each type that broke the rule and what it reached for, so the message
        // points at the line to fix.
        Assert.True(
            result.IsSuccessful,
            string.Join(Environment.NewLine, (result.FailingTypes ?? []).Select(type => $"{type.FullName}: {type.Explanation}")));
    }
}
```

If your continuous integration collects code coverage, add `"Coverlet.Core.Instrumentation"` to the
allowed list: the coverage tool writes a tracker into every assembly it measures, and the rule would
otherwise fail on code nobody wrote.

## One project, rings as folders

Folders can draw the rings as well as projects can. The Shed, a file and folder browser that lives
beside TheYard in this repository
([`samples/maplarge`](https://github.com/SteveStout/TheYard/tree/main/samples/maplarge)) and runs at
[theshed.stevenstout.biz](https://theshed.stevenstout.biz), is one project. Its rings are folders, each folder is a namespace, and the
tests select a ring by namespace instead of by project: `Data`, `Domain`, `Application`,
`Infrastructure`, `Controllers`, `Documentation` and `Composition`. The reasoning for keeping it to one
project is its own record,
[One project, seven folders, dependencies inward](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/docs/ADR-002-one-project-four-folders-dependencies-inward.md).

One project costs one thing. Across projects, the compiler refuses a reference that would make a
cycle, so an adapter can never use the host and nobody has to test it. Inside one project the compiler
allows anything, so every direction is a test's job, and so is a type that lands in a folder no rule
names. The last test in the file below exists for exactly that: a new folder or a missing namespace
line would otherwise be checked by none of the rules.

**The practice:** in a small codebase, start with folders and these tests, and split a ring into a
project of its own the day a second store or a second host appears. This is the section to copy first.

The Shed's tests, whole,
[`samples/maplarge/tests/TestProject.Tests/OnionTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/OnionTests.cs):

```live path=samples/maplarge/tests/TestProject.Tests/OnionTests.cs region=*
```

## When not to build it this way

The rings cost projects, interfaces and tests, and they pay that back only when there is something to
protect: here, two stores behind the same rules, a host with an operator's tab, and rules worth
testing on their own. A small service over one table, a spike written to learn something quickly, or
a script that reads, changes and writes is clearer flat, and gains a ring the day a second store or a
rule worth testing alone turns up. The record draws that line in more detail, under
[When not to build it this way](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid#when-not-to-build-it-this-way),
and The Shed above is the middle road between the two.

## References

- [Jeffrey Palermo, The Onion Architecture: part 1](https://jeffreypalermo.com/2008/07/the-onion-architecture-part-1/)
  (2008): the post that named the pattern, with the rule this page opens on and the case for putting
  interfaces in the core.
- [Common web application architectures, .NET architecture guides](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures):
  Microsoft's description of clean architecture in .NET, including the difference between a
  compile-time dependency and a call at runtime that the bid's drawing shows.
- [NetArchTest.eNhancedEdition](https://github.com/NeVeSpl/NetArchTest.eNhancedEdition): the
  library the ten tests are written in, a maintained fork of NetArchTest with the failure explanations
  the tests above print.
- [Robert C. Martin, The Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
  (2012): the same idea stated as The Dependency Rule, that source code dependencies can only point
  inward, and the argument for why it keeps frameworks and databases replaceable.

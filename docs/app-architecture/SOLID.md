# SOLID Principles

SOLID is five principles for arranging code so that it can change without breaking. The rings on the
[Onion and Clean Architecture](https://theyard.stevenstout.biz/?doc=clean-architecture) page rest on
them. This page takes the five one at a time: what each one means in plain words, where it came
from, and where it shows in TheYard, with the code read from the running build. The record behind
the page is [ADR: Onion and SOLID, how this codebase holds them](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid),
which also lists where a principle was deliberately not pushed further.

## In plain words

Each letter of SOLID is one habit. The drawing takes them one at a time, with where TheYard shows
each one.

[![The five SOLID letters one under the next, each with what it means in plain words and where TheYard shows it](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/solid.png)](https://theyard.stevenstout.biz/api/docs/diagrams/solid)

*A preview. [Open the drawing in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/solid) to read it full size.*

- **S, Single Responsibility.** Each class has one job, so it has only one reason to change.
- **O, Open / Closed.** Add new behaviour by writing new code, and leave the code that already works
  alone.
- **L, Liskov Substitution.** Anything that stands in for something else has to behave the same way,
  so the code using it never notices the swap.
- **I, Interface Segregation.** Keep interfaces small and shaped to the code that calls them, so no
  class has to depend on methods it never uses.
- **D, Dependency Inversion.** Important code depends on contracts it owns, never on the database or
  the framework underneath it. The details plug into the contract, not the other way round.

Why it matters: code that follows them is easier to read and to fix, its parts lean on each other
less, and each part can be tested on its own.

What that is worth: a developer can add a second database, a new endpoint or a new page without
reopening code that already works, and the organization gets a codebase where a change lands in one
place, is covered by the tests that already exist, and does not surprise the code around it.

### The words this page uses

- **Principle**: a rule of thumb about shape, not a law. Each one below says where TheYard stops
  applying it.
- **Responsibility**: one reason for a piece of code to change, which usually means one group of
  people who would ask for the change.
- **Interface**: in C#, a contract a class promises to keep: these methods, with these meanings.
- **Abstraction**: an interface or a base type that says what is done without saying how.
- **Substitution**: putting one implementation where another was, and nothing around it noticing.
- **Port and adapter**: the interface the inner ring declares, and the class outside that implements
  it (the words are explained on the [Onion and Clean Architecture](https://theyard.stevenstout.biz/?doc=clean-architecture#the-words-this-page-uses) page).
- **Composition root**: the one place, at startup, where the real classes are created and wired.
- **Sealed**: a C# class nothing can inherit from, so no derived type can change its meaning.

## Where the five came from

Robert C. Martin collected the five in the second half of the 1990s, starting from a 1995 newsgroup
post, and published them as the principles of object-oriented design; Michael Feathers later lined
the letters up into the word SOLID. Two of the five are older than the collection: Bertrand Meyer
stated the open-closed principle in 1988, and Barbara Liskov described the substitution idea in a
1987 keynote. The other three are Martin's own.

## S, single responsibility

**What it means.** A class, or a file, has one reason to change. Martin's later restatement is the
useful one: a reason to change is a group of people, so a module should answer to one group. When
two groups can each force a change to the same file, their changes collide.

**In TheYard.** A rule lives in `TheYard.Domain` and nowhere else. `BidRules` decides whether a bid
stands. `StandingRules` holds the one rule for raising a vehicle's shown price, and three callers
(the buyer's bids, the room's bids and the room's price to beat) all raise a price through it, so
the rule has one copy and the three cannot drift apart:

```live path=api/TheYard.Domain/StandingRules.cs region=raised-to
```

The host keeps the same discipline at the top. `Program.cs` is a table of contents: each line is
one call, and the comment beside it names the file in `Composition/` that holds the details, one
file per job (the stores, the sessions, the activity counters, the API's shape, the Admin tab)
([ADR: The composition root, split by job](https://theyard.stevenstout.biz/?doc=adr-composition-root)):

```live path=api/TheYard.Api/Program.cs region=composition
```

The front end holds it as a rule the build reads. Every file in `src/app` and `src/library` opens
with three lines, what it does, what it does not do and who uses it, and no file runs past three
hundred lines. A file that has to is named in the test with the reason it is allowed, which keeps
the reason where a reader of the rule will find it. The test reads the headers back, and checks
"Used by" against the files that really import it:

```live path=api/TheYard.Tests/FileHeaderTests.cs region=the-header-rule
```

**Where it stops.** `Program.cs` lists twelve calls, one per area of the application, rather than twelve
classes with one method each, because a list a reader can scan is the job. The record's section
[What is kept on purpose](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid#what-is-kept-on-purpose)
names the others.

## O, open and closed

**What it means.** Code should be open for extension and closed for modification: when a new case
arrives, add a class behind an interface the existing code already calls, instead of editing the
class that already works. Meyer stated it for classes in 1988; in practice it is about where a new
case lands.

**In TheYard.** The second database is the proof. Azure Cosmos DB arrived as new adapter classes
behind the same three ports the relational store already implemented, and the rules and the use
cases did not change
([ADR: A second store on Cosmos DB](https://theyard.stevenstout.biz/?doc=adr-second-store)). The
composition root grew a second branch, which is where a new case is supposed to land. The relational
backend, then the document backend, as the build wires them:

```live path=api/TheYard.Api/Composition/StoreRegistration.cs region=sql-backend
```

```live path=api/TheYard.Api/Composition/StoreRegistration.cs region=cosmos-backend
```

The same shape holds the documents you are reading. A new page is one row in the server's
catalogue and one entry in the front end's list of pages, and a test fails if the two ever
disagree. This page arrived that way: two rows, no change to the code that serves documents.

**Where it stops.** The ports themselves changed once for the second store: every member became a
`Task`, because the document store has no synchronous driver. That one change touched every caller
once, and the record says so plainly rather than pretending the door was never opened
([ADR: The ports learn to wait](https://theyard.stevenstout.biz/?doc=adr-ports-wait)).

## L, Liskov substitution

**What it means.** If code works with a type, it must keep working with anything that stands in for
that type, without checking which one it got. Liskov's 1987 keynote put it as a property of types:
a subtype must behave the way the supertype's users expect. For interfaces, that means any
implementation can replace any other and the callers cannot tell.

**In TheYard.** The gate is the proof. The same xUnit suite runs on both stores: once with the
relational store underneath, and again with Cosmos DB, by pointing the process at the account and
running `dotnet test` again. Not a second suite written for the second store: the same facts, the
other adapter, the same answers. The `if` in the block below is why the second pass is not run on
every version: it runs when the version touched the API, the Cosmos infrastructure or the three
specs that read both stores, and is carried forward from the version that last ran it otherwise.
Between the two passes sit the seven tests that do need the live account, which are the one set
written for the second store. From the gate's own workflow:

```live path=.github/workflows/gate.yml region=both-stores
```

`MemoryBidStore` is the smaller case. It implements `IBidStore` in a dictionary, with the same
compare-and-set rule the two real stores keep, and the tests of the use cases hand it in where a
real store would go, so a use case can be tested with no database and no mocking framework. It stands in for a real store in production too: a backend whose store has
not come up yet bids against it, and the bids are forgotten at restart. That is a deliberate limit,
written down in [ADR: The relational store](https://theyard.stevenstout.biz/?doc=adr-store).

**Where it stops.** Substitution is not complete everywhere, and the record says where: the Cosmos
DB user store does not move the email claim when an address changes, and nothing in the site changes
an address today ([Known gaps in substitution](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid#what-is-kept-on-purpose)).
Sealing classes helps hold the principle from the other side: a class nothing can inherit from is a
class no derived type can change the meaning of, which is why TheYard seals by default
([Sealed by default](https://theyard.stevenstout.biz/?doc=sealed)).

## I, interface segregation

**What it means.** No class should be forced to depend on methods it does not use. Shape an interface
to the code that calls it, and prefer several small interfaces to one wide one.

**In TheYard.** The auction has three ports, not one repository, because the catalogue, the photos
and the bids are read by different code at different times:

```live path=api/TheYard.Application/Ports.cs region=ports
```

`IStoreExperiment`, the port behind the Admin tab's performance proof, has one method, because the
Admin tab asks one question.

**Where it stops.** `IActivityStore` and `ILogStore` each carry one writer beside the handful of
reads their callers ask for, and are not split, because splitting them would double the interfaces
and allow no new swap. They stay whole
until a caller needs only half
([What is kept on purpose](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid#what-is-kept-on-purpose)).

## D, dependency inversion

**What it means.** The important code should not depend on the details underneath it; both should
depend on an abstraction, and the abstraction should belong to the important code. In the rings,
that is the move that makes the whole shape possible: the inner ring declares the interface it
needs, the outer ring implements it, and at compile time every arrow points inward.

[![The inner ring owns the interface: Application declares IBidStore and uses it, EfBidStore and CosmosBidStore implement it from the outer ring, and the composition root decides which one each store gets](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/port-adapter.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/port-adapter)

*A preview. [Open the port and its adapters in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/port-adapter) to zoom in and follow it.*

**In TheYard.** `TheYard.Application` declares `IBidStore`. `EfBidStore` in the relational adapter and
`CosmosBidStore` in the document adapter implement it. `Composition/StoreRegistration.cs` decides
which one a store gets, and every class receives what it needs through its constructor. Four tests
read the compiled host and fail the build if a handler reaches a database, a Cosmos DB client or the
service container, composes the bid services itself, queries a database, or registers a service
anywhere but the composition root:

```live path=api/TheYard.Tests/OnionTests.cs region=host
```

**Where it stops.** The server's clock is a static, read once at the edge and handed inward as a
value, rather than a `TimeProvider` injected into every handler: the rules take the clock as an
argument, which is what lets their tests fix time, and the extra parameter would buy the tests
nothing ([ADR: Onion and SOLID](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid#what-is-kept-on-purpose)).

## Five principles, one table

| Letter | The principle in one line | Where it shows in TheYard | What holds it |
| --- | --- | --- | --- |
| S | One reason to change. | A rule lives in Domain once; `Program.cs` is a list; one job per front-end file. | `FileHeaderTests`, `OnionTests` |
| O | Add a class, do not edit the core. | Cosmos DB arrived as adapters behind the same ports; a page is a catalogue row. | `DocumentationCatalogTests` |
| L | A stand-in behaves the same. | The same suite runs on both stores; `MemoryBidStore`. | The gate's second xUnit pass |
| I | Small interfaces, shaped to the caller. | Three auction ports; a one-method experiment port. | Review, and the record |
| D | Depend on an abstraction the inner ring owns. | `IBidStore` declared inside, implemented outside, wired in `Composition/`. | `OnionTests` |

## How the build holds them

```readouts
Architecture tests | {{live:facts api/TheYard.Tests/OnionTests.cs}}
File header tests | {{live:facts api/TheYard.Tests/FileHeaderTests.cs}}
Sealed-by-default tests | {{live:facts api/TheYard.Tests/SealedByDefaultTests.cs}}
Files with the header | {{live:headers}}
```

Numbers read from the build when the page is opened. The architecture tests, by name:

```text
{{live:fact-names api/TheYard.Tests/OnionTests.cs}}
```

## Read next

- [Onion and Clean Architecture](https://theyard.stevenstout.biz/?doc=clean-architecture): the
  rings these principles produce, and TheYard's projects beside the textbook.
- [Onion architecture, the practices](https://theyard.stevenstout.biz/?doc=onion): each practice
  with a whole file to copy.
- [ADR: Onion and SOLID, how this codebase holds them](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid):
  the record, the ten rules, and what is kept on purpose.

## References

- [Robert C. Martin, The Principles of OOD](http://butunclebob.com/ArticleS.UncleBob.PrinciplesOfOod):
  the five principles in one line each, from the author who collected them, with the 1995 origin.
- [Robert C. Martin, The Single Responsibility Principle](https://blog.cleancoder.com/uncle-bob/2014/05/08/SingleReponsibilityPrinciple.html)
  (2014): the restatement this page uses, a reason to change is a group of people.
- [Bertrand Meyer, Object-Oriented Software Construction](https://bertrandmeyer.com/OOSC2/)
  (1988; second edition 1997, online): the book that stated the open-closed principle.
- [Barbara Liskov, Data Abstraction and Hierarchy](https://doi.org/10.1145/62139.62141)
  (keynote 1987, published 1988): the origin of the substitution principle.
- [Architectural principles, .NET architecture guides](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles):
  Microsoft's version of the same ground, with dependency inversion and single responsibility as
  principles for a .NET application.

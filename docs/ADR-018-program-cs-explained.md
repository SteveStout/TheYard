# ADR: Program.cs, explained for a new developer

Status: accepted, 2026-09-02, shipped as 1.0.0.24. Written at Steve's
request for a developer new to ASP.NET Core, or new to this codebase, who
opens the host file and wants to know what each part does and why it is
the way it is.

## Context

Program.cs is the one file that starts the API. A newcomer sees four
hundred lines with no class and no `Main`, a block of `builder.Services`
calls, a run of `app.MapGet` calls, two `app.Use` blocks, and helpers after
`app.Run()`. Every one of those has a reason, and most of the reasons are
the difference between "works on my machine" and "works in the container,
in the tests, and in the pipeline". This record walks the file top to
bottom. Read it beside the file; the samples are the file, read from this
build.

## The walk

### No class, no Main

The file uses top-level statements: C# lets the entry file read like a
script. The compiler generates the `Program` class and `Main` around it.
The one visible trace is the last line, `public partial class Program;`,
which exists so the integration tests can name the class when they boot
the host in memory with `WebApplicationFactory<Program>`. Without that
line the generated class is internal and the tests cannot see it. Local
functions such as `RunChecks` and `HandleBid` sit between endpoints
because top-level statements allow them anywhere, and they can be called
before the line that declares them. A `static` helper like `FindUpward`
can even follow `app.Run()`.

### The files are found by walking up

The dataset, the README, the docs and the photo manifest are located at
startup. The API does not assume a fixed folder depth, because it is
started from three different places: `dotnet run` from the project folder,
the test host from `bin/Debug/...`, and the published image from `/app`.
`FindUpward` walks from the content root toward the disk root until it
finds the named file, and throws a clear `FileNotFoundException` if it
never does. The folder README.md sits in becomes `repoRoot`, and every
later path is built from it; ADR: The staff review removed the per-request
walks that used to repeat this work.

```live path=api/TheYard.Api/Program.cs region=find-upward
```

### The services, and why they are singletons

Dependency injection means: the endpoints ask for an `InventoryService` or
a `BidService` as a parameter, and the framework hands them the registered
instance. Every registration here is `AddSingleton`, one instance for the
life of the process, because the dataset is loaded once and shared by
every request, and the buyer's bids live in memory on purpose (this is a
demo with one anonymous buyer; the Admin tab and ADR: Observability say so
out loud). `InventoryService` holds the expanded dataset in a `Lazy`, so a
`Scoped` or `Transient` registration would hand every request a fresh
service with an empty `Lazy` and expand 100,000 records again each time.
That is the mistake this block is protecting against.

The source is built by decoration: `new SyntheticVehicleSource(new
JsonFileVehicleSource(dataPath), targetCount)`. The inner source reads the
200 seed vehicles from the JSON file; the outer one expands them
deterministically to `Inventory:TargetCount` records (100,000 by default,
overridable through configuration), so the app demonstrates scale without
a giant file in the repository. The project structure document explains
the layers those types come from.

```live path=api/TheYard.Api/Program.cs region=composition
```

### snake_case, in two places

The dataset is snake_case (`body_style`, `min_next_bid`) and the React app
reads the wire verbatim, so nothing is renamed on the way out. That takes
two settings, and a newcomer will wonder why one is not enough.
`ConfigureHttpJsonOptions` governs how minimal APIs bind request bodies
(a bid posted as `{ "amount": 1000, "anchor_ms": ... }` lands in
`BidRequest`) and what `Results.Json` does when it is handed no options.
`wireFormat` exists because `VehicleWire.ToWire` serializes each vehicle
to a JSON node with `System.Text.Json` directly, before overlaying the
auction facts, and that path knows nothing about the host's options. One
explicit options object, handed to every `Results.Json` call and to
`ToWire`, means the wire shape never depends on which path a call took.

### Fail at startup, not on the first request

`app.Services.GetRequiredService<InventoryService>().GetAll()` runs right
after `Build()`. It forces the dataset to load before the first request.
If the file is missing or malformed the process exits with the real error,
the container's health check fails, and the deploy's Verify step stops the
roll. Without this line the same problem would show up as a 500 on the
first visitor, which is a worse place to learn it.

### Endpoints: binding, then delegation

`app.MapGet("/api/vehicles", ...)` is a minimal API endpoint: a route and a
lambda. `[AsParameters] VehicleQueryParams query` binds every query string
parameter into one record, whose `TryBuildFilter` validates them all and
returns one error message; a bad request is `Results.Problem(...)` in the
shape every failure uses (ADR: Error handling), a good one
`Results.Json(..., wireFormat)`. The endpoint holds no rules. Filtering, sorting and paging happen in `InventoryService`, and
the auction facts on each vehicle come from `VehicleWire.ToWire`, so the
same rules serve the list, the detail, and the bid responses.

```live path=api/TheYard.Api/Program.cs region=inventory-endpoint
```

The two bid endpoints share one local function, `HandleBid`, which answers
three questions in order: is the clock anchor valid (400 if not), does the
vehicle exist (404 if not), does the domain accept the action (400 with
the reason if not). The status codes are the contract the React app relies
on: it reads `detail` out of a 400 and treats a 404 as gone.

```live path=api/TheYard.Api/Program.cs region=bid-endpoints
```

```live path=api/TheYard.Api/Program.cs region=bid-handling
```

The documents come from one endpoint over a catalog (ADR: The staff
review); the Bicep file and the resume keep literal routes, which win over
the `{slug}` pattern because routing prefers the more specific match.

### Middleware order, the part that bites

`app.Use(...)` registers middleware; `app.MapGet(...)` registers an
endpoint. Middleware runs in the order it is registered; endpoints run at
the end of the pipeline no matter where their `MapGet` line sits in the
file. That is why the error-recording middleware and the cache-header
middleware appear after some endpoints and before others without changing
what they cover: they cover everything. The error middleware is the
simplest example, a try around `next()` that records a 500 or an exception
and rethrows so the framework still answers.

```live path=api/TheYard.Api/Program.cs region=error-log
```

The static file middleware and the SPA fallback are registered last on
purpose. The cache middleware (ADR: Cache headers) must come before them so
index.html and the bundle files get their headers; the fallback answers
only addresses without a file extension, so a missing bundle file is a 404
rather than a page dressed as a script.

```live path=api/TheYard.Api/Program.cs region=static-files
```

### Liveness, readiness, and the Admin tab

`/healthz` answers "ok" if the process is up; the container's HEALTHCHECK
in the Dockerfile asks it every thirty seconds. `/readyz` runs the real
probes and answers 503 until the files the app needs are in place; the
deploy's Verify step asks it before trusting a roll. `/api/health` returns
the same probes with their timings for the Admin tab. The difference
matters: a process can be alive and not yet ready, and an orchestrator
treats the two differently.

```live path=api/TheYard.Api/Program.cs region=health-checks
```

```live path=api/TheYard.Api/Program.cs region=probes
```

### The environment the container sets

`ASPNETCORE_URLS=http://+:8080` in the Dockerfile tells Kestrel which port
to listen on; `APP_VERSION` and `APP_COMMIT` are baked in by the Docker
build and read once at startup (ADR: Version in the footer). Locally
neither exists, so the footer says "dev build" and the commit reads
"local".

```live path=api/TheYard.Api/Program.cs region=version-endpoint
```

### The records at the bottom

`BidRequest` and `BuyNowRequest` are `sealed record` types: immutable
shapes for request bodies, bound from JSON by the options above. Records
give value equality and a one-line declaration; there is no reason for a
class here. The partial `Program` line under them is the test hook from
the top of this record.

```live path=api/TheYard.Api/Program.cs region=records-and-test-hook
```

## Why this is one file

It is 1,244 lines, and that is the first thing a reviewer notices, so it is
worth saying that it is a decision rather than a drift.

What those lines are:

```
1,244 total
  444 comment
   86 blank
  714 code, across 29 endpoints
```

Twenty-five lines of code per endpoint, and most endpoints are a route, a
binding and a delegation. Nothing in here holds a rule; the rules are in Domain
and Application, and this file's job is to say what is reachable and in what
order.

**In what order is the reason.** A minimal-API host is two lists: the services
that get registered and the middleware that wraps every request. The second one
is ordering-sensitive in a way that does not announce itself, and this project
has already paid for that once: the timing middleware sat below the exception
handler, read the status before the handler had written it, and recorded every
failed request as a 200, including the endpoint whose whole job is to fail
(ADR: Reviewing my own work). A reader who can see the pipeline from `builder`
to `app.Run()` without opening another file can see that kind of mistake. A
reader following `AddYardEndpoints()` into a second file, and `AddYardAuth()`
into a third, cannot.

**What splitting would buy.** Navigation, mostly. Editors already do that: the
file is regioned end to end, and the regions are how the served documents quote
it. What it would cost is the one property worth keeping.

**What would change my mind**, which is the useful half of a decision like this:

- An endpoint that grows a body instead of a delegation. That is a use case
  trying to be born, and it belongs in Application, not in a new host file.
- The composition and the routes stopping fitting in a reader's head together.
  The trigger is a reader, not a number: 1,244 lines of which a third are
  explanation is not the same as 1,244 lines of logic, and a rule that says
  "split at a thousand" would have split this one at the wrong seam.

## What to change when

- **A new endpoint:** one `app.MapGet` or `app.MapPost` beside its
  neighbors, binding and delegation only; the rule goes in Domain or
  Application, and a test in `api/TheYard.Tests` boots the host and calls
  it.
- **A new document:** one line in `DocsCatalog.cs` and one in
  `DocsMenu.tsx`; a test fails if the two disagree.
- **A new service:** `AddSingleton` unless it holds per-request state, and
  then think again about whether it should exist.
- **A new file the app reads:** locate it once at startup from `repoRoot`,
  and add it to the Dockerfile's COPY lines, or the container will not
  have it.

## Files

- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the file this record walks.
- [`api/TheYard.Api/TheYard.Api.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/TheYard.Api.csproj): `net10.0`, nullable reference types on, implicit usings on (which is why the file has so few `using` lines).
- [`api/TheYard.Api/VehicleQueryParams.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/VehicleQueryParams.cs), [`api/TheYard.Api/Clocks.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Clocks.cs), [`api/TheYard.Api/VehicleWire.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/VehicleWire.cs): binding, the clock anchor, and the outgoing shape.
- [`api/TheYard.Application/InventoryService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/InventoryService.cs): the `Lazy` that makes the singleton registration matter.
- [`api/TheYard.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocsCatalog.cs), [`api/TheYard.Api/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/LiveSamples.cs), [`api/TheYard.Api/Observability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Observability.cs): the pieces the host wires.
- [`api/TheYard.Tests/AdminEndpointTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AdminEndpointTests.cs) and the tests beside it: every one boots this file through `WebApplicationFactory<Program>`.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the port, the provenance arguments, the HEALTHCHECK, and the files copied for the walk to find.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the Verify step that asks `/readyz`.
- [`docs/PROJECTS.md`](https://github.com/SteveStout/TheYard/blob/main/docs/PROJECTS.md): the layers the services come from (served as Project structure under About).

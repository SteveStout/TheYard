# ADR: Program.cs, explained for a new developer

Status: accepted, 2026-09-02, shipped as 1.0.0.24; rewritten on 2026-09-28 for 1.0.3.39, when the file
became a composition root and its parts moved to files of their own (ADR: The composition root, split by
job). Written at Steve's request for a developer new to ASP.NET Core, or new to this codebase, who opens
the host file and wants to know what each part does and why it is the way it is.

## Context

Program.cs is the file that starts the API. It is now a table of contents: the builder, one line per
concern that gets registered, the host built and started, the middleware, one line per feature that maps
routes, and `app.Run()`. Each line leads to the file that shows how. This record walks those lines in
order, and the samples are those files, read from this build.

## The walk

### The files are found by walking up

The dataset, the README, the docs and the photo manifest are located once, at startup, in
`Composition/HostPaths.cs`. The API does not assume a fixed folder depth, because it is started from three
places: `dotnet run` from the project folder, the test host from `bin/Debug/...`, and the published image
from `/app`. `FindUpward` walks from the content root toward the disk root until it finds the named file,
and throws a clear `FileNotFoundException` if it never does. `HostPaths` is registered, so an endpoint that
serves a file asks for it as a parameter.

```live path=api/TheYard.Api/Composition/HostPaths.cs region=find-upward
```

### The table of contents

What the file is today:

```
54 total
  10 comment
  6 blank
  38 code, across 0 endpoints
```

No route is mapped in Program.cs itself. Until 1.0.3.39 it was one file of 2,667 lines holding 48
endpoints; the record that replaced that decision says what changed and holds the new shape with a test.

```live path=api/TheYard.Api/Program.cs region=composition
```

### No class, no Main

The file uses top-level statements: C# lets the entry file read like a script, and the compiler writes
the `Program` class and `Main` around it. The one visible trace is the last line,
`public sealed partial class Program;`, which exists so the integration tests can name the class when they
boot the host in memory with `WebApplicationFactory<Program>`.

```live path=api/TheYard.Api/Program.cs region=records-and-test-hook
```

### What the steps hand one another

Registration is a sequence, and a later step needs what an earlier one made: the accounts need the
signing key, the kept log needs the stores, the page sweep needs the build's version. `YardComposition`
carries those between the steps, filled once at startup and never read by a request except through what
was registered. It is the one class in the composition with state, which is why it lives beside the other
state types at the project's root and not in `Composition/`, where every class is static.

### The stores come first

`builder.AddTheYardStoresAsync` (Composition/StoreRegistration.cs) is the only step that waits: it brings
both stores up before anything is registered, because the answer decides what gets registered. The
relational store is SQL Server when the deploy gave one and SQLite otherwise; the document store is there
when an account is configured; each stands behind the same ports, on the JSON files until it comes up, and
a store that refused at startup is asked again every thirty seconds for an hour (the second chance).

```live path=api/TheYard.Api/Composition/StoreRegistration.cs region=sql-backend
```

Nearly every registration in the steps is `AddSingleton`, one instance for the life of the process,
because the dataset is loaded once and shared by every request and each store's standing bids live in
memory beside it. `InventoryService` holds the expanded dataset in a `Lazy`, so a `Scoped` registration
would expand 100,000 records per request. The two exceptions are decided per request and can only be:
`CurrentBackend`, which store this request is on, and Identity's user store over it.

### The rest of the registrations

Each is one file and one method, in the order the host needs them:

- `AddTheYardObservability` (Composition/ObservabilityRegistration.cs): the rings behind the Admin tab,
  the kept log, and the request hook that files every request once.
- `AddTheYardSessions` and `AddTheYardAccounts` (Composition/AuthRegistration.cs): the signing key and the
  session issuer first, because the activity feature keys visitor tokens with the same key; then Identity
  and the JWT read back from its cookie.
- `AddTheYardActivity` (Composition/ActivityRegistration.cs) and `AddTheYardEmail`
  (Composition/EmailRegistration.cs): the collectors that write off the request path, and the one email.
- `AddTheYardApi` (Composition/ApiRegistration.cs): snake_case bodies, one ProblemDetails for every
  failure, one log line per request, and the OpenAPI document.
- `AddTheYardTelemetry` and `AddTheYardAdmin`: Application Insights where it is configured, and what the
  Admin tab reads that is not a ring.

### Started before it serves

`app.StartTheYardAsync` (Composition/Startup.cs) runs after `Build()` and before anything is served: the
loggers attached, a store that did not come up said out loud, and the default store's catalogue and bids
loaded and timed. If the dataset is missing or malformed the process exits with the real error, the
container's health check fails, and the deploy's verify step stops the roll, which is a better place to
learn it than a 500 on the first visitor.

### Middleware order, the part that bites

`app.UseTheYardRequestPipeline` (Composition/RequestPipeline.cs) holds every piece of middleware in one
method, in the order it runs, with the reason beside each. Timing is outermost, so a failed request is
recorded with the status that was actually sent; then the problem shape, the request log, the user, the
session's renewal, the store's warmth, the error record, the cache rules, and the files last. Endpoints
run at the end of the pipeline whichever line maps them, so the order that matters is the order in this
one file.

```live path=api/TheYard.Api/Composition/RequestPipeline.cs region=request-timing
```

```live path=api/TheYard.Api/Composition/RequestPipeline.cs region=error-log
```

### Endpoints: binding, then delegation

Each feature is a static class under `Endpoints/` with one `Map...Endpoints` method and private, named
handlers: `VehicleEndpoints`, `BidEndpoints`, `DocsEndpoints`, `HealthEndpoints`, `ErrorEndpoints`,
`AdminEndpoints`, `AccountEndpoints`, and `ReferenceEndpoints` for the OpenAPI document and its page. A
handler binds the request, calls the service that owns the rule, and shapes the reply; the rules live in
Domain and Application. A handler's return type is a `Results<...>` union or a typed result, which is what
the OpenAPI document reads, so naming the handlers changed nothing a client sees.

```live path=api/TheYard.Api/Endpoints/VehicleEndpoints.cs region=inventory-endpoint
```

The two bid endpoints share one method, `HandleBid`, which answers three questions in order: is this
session on this store, does the vehicle exist, does the domain accept the action, on the server's clock.

```live path=api/TheYard.Api/Endpoints/BidEndpoints.cs region=bid-handling
```

### Liveness, readiness, and the Admin tab

`/healthz` answers "ok" if the process is up; the container's HEALTHCHECK asks it. `/readyz` runs the
checks that gate traffic and answers 503 until the files the app needs are in place; the deploy's verify
step asks it. `/api/health` returns every check with its timing for the Admin tab.

```live path=api/TheYard.Api/Endpoints/HealthEndpoints.cs region=probes
```

### The single-page app, last

`app.MapTheYardSpa` (Composition/SpaRegistration.cs) answers any address that is not an API route or a
real file with index.html, so a missing bundle file is a 404 and never a page dressed as a script.

```live path=api/TheYard.Api/Composition/SpaRegistration.cs region=spa-fallback
```

## What to change when

- **A new endpoint:** a `Map...` line and a private handler in the feature's file under `Endpoints/`, or a
  new static class there with its own `Map...Endpoints` line in Program.cs; binding and delegation only,
  and a test in `api/TheYard.Tests` that boots the host and calls it.
- **A new service:** one line in the registration step it belongs to, `AddSingleton` unless it holds
  per-request state.
- **A new piece of middleware:** in `RequestPipeline.cs`, at the place its order demands, with the reason beside it.
- **A new document:** one line in `DocsCatalog.cs` and one in `src/library/records.ts` or `pages.ts`; a
  test fails if the two disagree.

## Addendum, 2026-09-08: two stores in one file

The walk above still reads top to bottom, and two of its stops changed shape
when the document store moved in beside the relational one (ADR: One
container, both stores).

The persistence region no longer chooses a store. It reads both settings, the
SQL connection string and the Cosmos DB endpoint, and creates the two rings
and the request describer early, because the interceptor that feeds the SQL
ring is attached to a context factory built by hand before the container
exists. The migrate-and-seed region then builds one `Backend` per store: the
relational one always, the document one when there is an endpoint, each
brought up and timed on its own, each holding its own `InventoryService`,
`BidService` and `MarketService`, and each saying how to build Identity's
store over its accounts. The three services left the container entirely; an
endpoint takes `CurrentBackend`, which is scoped to the request and resolved
once from the request's header, cookie or the container's default.

```live path=api/TheYard.Api/Composition/StoreRegistration.cs region=sql-backend
```

```live path=api/TheYard.Api/Composition/StoreRegistration.cs region=cosmos-backend
```

Identity is registered once, unconditionally, and its store is the one thing
about it chosen per request. The health check runs one database probe per
store. The metrics endpoint answers the store the request is on at the top
level, as it always did, and lists every store below it. And two endpoints
were added for the toggle at the top of the page.

```live path=api/TheYard.Api/Endpoints/AdminEndpoints.cs region=stores-endpoints
```

The numbers in the section above are the file's numbers on the day this
addendum was written; the test that holds them to the file does not care
which day that was.

## Addendum, 2026-09-09: one endpoint for the bar, not two

The toggle moved to the sites the next morning (ADR: One container, both
stores, the addendum of that name), and the second of the two endpoints
above went with it. `CurrentBackend` is resolved from the request's header
or the container's default now, not from a cookie; `POST /api/stores/select`
is gone, and `GET /api/stores` expires the cookie the old toggle set when a
request still carries one. The live block above shows what is left.

## Addendum, 2026-09-15: the wire shape is a record now

The paragraph on `wireFormat` above says `VehicleWire.ToWire` serialises each
vehicle to a JSON node and appends the auction facts, which is why the host's
options could not reach it. That stopped being how it works when the API
started describing itself (ADR: The API describes itself): `ToWire` builds a
`VehicleView`, a record with the dataset's twenty-nine fields and the five
derived ones, and the public endpoints answer through `TypedResults.Ok`, which
serialises with the host's own options, the same snake_case policy that
`ConfigureHttpJsonOptions` sets for request bodies, and which carries the
response type into the document where `TypedResults.Json` carries nothing.
`wireFormat` stays for the `Results.Json` calls the operator endpoints still
make, and both paths apply the one policy. The names, the values and their
order on the wire did not change.

## Addendum, 2026-09-30: the request pipeline, by name

Steve, naming the same file in The Shed: "pipeline can mean too many things and it's not clear". This
site alone serves ADR: The deploy pipeline beside this record. The middleware file is now
`Composition/RequestPipeline.cs` and the call `app.UseTheYardRequestPipeline(host)`, ASP.NET Core's own
name for it ("The ASP.NET Core request pipeline consists of a sequence of request delegates", Microsoft
Learn, ASP.NET Core Middleware). Nothing in the order changed; the file moved and its type was renamed.

## Files

- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the table of contents this record walks.
- [`api/TheYard.Api/Composition`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Composition): one file per registration step, the startup, the pipeline and the single-page fallback.
- [`api/TheYard.Api/Endpoints`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Endpoints): one static class per feature.
- [`api/TheYard.Api/YardComposition.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/YardComposition.cs): what the steps hand one another.
- [`api/TheYard.Api/TheYard.Api.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/TheYard.Api.csproj): `net10.0`, nullable reference types on, implicit usings on (which is why the file has so few `using` lines).
- [`api/TheYard.Api/VehicleQueryParams.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/VehicleQueryParams.cs), [`api/TheYard.Api/Clocks.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Clocks.cs), [`api/TheYard.Api/VehicleWire.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/VehicleWire.cs): binding, the clock anchor, and the outgoing shape.
- [`api/TheYard.Application/InventoryService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/InventoryService.cs): the `Lazy` that makes the singleton registration matter.
- [`api/TheYard.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocsCatalog.cs), [`api/TheYard.Api/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/LiveSamples.cs), [`api/TheYard.Api/Observability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Observability.cs): the pieces the host wires.
- [`api/TheYard.Tests/AdminEndpointTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AdminEndpointTests.cs) and the tests beside it: every one boots this file through `WebApplicationFactory<Program>`.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the port, the provenance arguments, the HEALTHCHECK, and the files copied for the walk to find.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the Verify step that asks `/readyz`.
- [`docs/PROJECTS.md`](https://github.com/SteveStout/TheYard/blob/main/docs/PROJECTS.md): the layers the services come from (served as Project structure under About).

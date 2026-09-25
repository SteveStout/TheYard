# ADR: The API describes itself

Status: accepted, 2026-09-15, shipped as 1.0.0.137. Written on the day a hiring manager said,
through a recruiter, that the first thing they wanted to see was how deep the REST API goes. The API
was forty-one endpoints and no document. This is the document, and the rules that keep it true.

## Context

Every other part of this project explains itself from inside the running app: the records, the
architecture pages, the code samples read from the build. The HTTP surface did not. A reader who
wanted to know what `/api/vehicles` accepts had to open Program.cs and read `VehicleQueryParams`,
and a reader who wanted to know what a bid answers had to read `HandleBid` to the end. The README
described features the code did not have and missed two it did, which is what a hand-written
description of an API becomes after a fortnight.

Two facts about the code shaped the answer. First, the endpoints were untyped on the way out: every
one answered `Results.Json(new { ... })` with an anonymous object, which a generator can only
describe as "an object". Second, fifteen of the forty-one endpoints sit under `/api/admin/` and are
operator surfaces, some of them behind a key: the SQL ring, the Azure state, the reset-link mint.
A generated document that listed those would be an index of the parts of the site a stranger has
the least business reading, and on a public site that is disclosure rather than untidiness.

## What was considered

| Option | What it buys | What it costs |
| --- | --- | --- |
| **`Microsoft.AspNetCore.OpenApi`, the framework's own generator** | Reads the routing table and the typed results directly; no attributes to keep in sync; the package ships with the runtime and moves with it | Newer than Swashbuckle, so fewer worked examples; the document is only as honest as the endpoint metadata |
| Swashbuckle | Fourteen years of examples and every filter anybody has needed | No longer in the template since .NET 9, a second model of the endpoints beside the framework's, and a UI that looks like every other API in the world |
| NSwag | Client generation in the same tool | The same second model, and nothing here needs a generated client |
| Write the document by hand | Total control of the wording | The README already proved what a hand-written description of this API turns into |

**Decision: the framework's generator, curated.** `AddOpenApi` builds one document from the
endpoints as they are mapped, and the work of this record is making the endpoints say enough for
that document to be worth reading.

For the page a person opens, **Scalar** rather than Swagger UI. Both render the same document;
Scalar renders it as a reference with a request builder and generated client code in the visitor's
language, the assets ship inside the package rather than from a content delivery network, and it
does not look like the default page of every API written in the last decade. It sits at
`/api/reference`, and the document it renders is at `/api/openapi/v1.json` rather than the
framework's default `/openapi/v1.json`, because `/api` is the one prefix the development server
proxies to the host: at the default address the page's request for its own document came back as
`index.html` on a developer's machine and in the browser suite, and the first run of that suite said
so. Both are served by the same container as everything else, and both are rows of an **API
Reference** section in the sidebar, right under App Architecture, so a reader finds the API without
opening anything (ADR: The sidebar, the addendum on the twelfth section).

## The four rules, and the test that holds each

**1. The admin surface is not in the document.** `OpenApiOptions.ShouldInclude` answers false for
any route under `/api/admin/`, in one place, so the document never carries an operator endpoint
however many are added. That is a filter, and a filter is the kind of rule that breaks silently a
year later when somebody maps an admin endpoint under a new prefix, so `ApiDocumentTests` reads the
served document and fails if any path in it starts with `/api/admin`, and it reads the routing
table's own description of the host to prove the filter is doing work rather than passing because
there was nothing to filter.

```live path=api/TheYard.Api/ApiDocument.cs region=public-surface
```

**2. Every public endpoint is in it, and nothing else is.** The same test compares the document's
paths and methods against every endpoint the host maps, minus the admin ones, in both directions.
An endpoint that somebody excludes from description by accident disappears from the document and
fails the build; a document that grew a path the host does not serve fails it too.

**3. Every operation carries an operation id, a summary, and the responses it can actually
answer.** The gate here is the one that stops the document rotting the way the README did. An
operation id, because a generated client and a search both key on it. A summary, because a list of
paths with no sentence beside each is a directory listing. And responses, including the failures:
a protected endpoint declares its 401, an endpoint with a route parameter declares its 404, and an
endpoint that reads a body declares its 400. A document that only shows 200s is the tell of a
generated document nobody read.

Declaring those responses honestly meant typing the endpoints. Every anonymous object became a named
record (`VehiclePage`, `BidResult`, `HealthReport` and the rest, in `Replies.cs`), the vehicle on
the wire became `VehicleView` rather than a JSON node with five fields added by hand, and the
handlers return `TypedResults` through `Results<...>` unions, so the generator reads the shape from
the type rather than inferring it. `TypedResults.Ok` rather than `TypedResults.Json`, because only
the first carries its type into the document; the second serialises the same bytes and declares
nothing. Where a handler answers text or a file, the endpoint says so with `Produces`. The wire did
not change: the same snake_case names, in the same order, with the derived auction facts last.

**4. The bearer scheme is declared, and only the protected operations require it.** The session is
a JWT that the browser carries in an httpOnly cookie and that a client can carry as a bearer header;
the handler reads either. The document declares both schemes, and an operation transformer marks
every endpoint that has `RequireAuthorization` as requiring one or the other, so the reference page
shows a lock on exactly the operations that will answer 401 without a session. The test reads the
routing table for the endpoints that carry authorization metadata and requires the set of
operations with a security requirement to be exactly that set.

```live path=api/TheYard.Api/ApiDocument.cs region=transformers
```

The whole of the test class is the enforcement, and the rules table in ADR: The rules a change has
to pass names it beside the other seventeen:

```live path=api/TheYard.Tests/ApiDocumentTests.cs region=document
```

## What changed on the wire, and what did not

Two behaviours moved, both from an implicit answer to a stated one, and both are recorded here so
that a reader of ADR: Error handling, one shape everywhere is not misled by its sentence about the
empty 404.

That record says an unknown vehicle "stays an empty 404". It stopped being empty when the status
code pages middleware arrived: a bare 404 with no body was already being filled in with a
ProblemDetails body on the way out, and nothing said so. From this version the endpoints that
answer 404 and 401 say it themselves, with a title and a one-sentence detail, in the same
ProblemDetails shape every other failure here uses. The status codes are unchanged; the difference
is that the body now names what was not found instead of the middleware naming nothing.

One thing the first build of the document taught, recorded because the next person to add a query
record will meet it: an `[AsParameters]` record is read through a wrapper that merges the
constructor parameter's attributes with the property's into a plain `Attribute` array whenever the
constructor parameter has any, and the generator then casts that array to a typed one and throws,
which answered the whole document with a 500 on the first run. `VehicleQueryParams` had carried its
`[FromQuery]` names on the constructor parameters since it was written. They target the properties
now, the wrapper hands back the property's own typed array, and the document builds.

The vehicle's wire shape is unchanged in every name and value. What changed is how it is built: a
record with the dataset's twenty-nine fields and the five derived facts, rather than a serialised
node with five keys appended. ADR: Program.cs, explained for a new developer carries an addendum on
this, because its explanation of `wireFormat` was written against the old path.

## What this record does not claim

The document describes the HTTP surface. It is not a promise of stability: this is a portfolio
with a changelog, not a versioned public API, and the document's version is the build's version
because that is the honest number. Nor does the document describe the two stores, the identity, or
the deploy; those have their own records, and the reference page's description points at them.

The admin endpoints are excluded from the document, not from the site. They answer as they did,
behind the key where they were behind it, and their tests are unchanged. Hiding a route from a
document is not a security measure and is not described as one here; ADR: The code is public and
the secrets are not is where that question is answered.

## Addendum, 2026-09-25 (1.0.3.21): the site's page, not a product's

The reference page came with Scalar's own extras: a telemetry call on every visit, an AI chat button, an MCP link, a developer toolbar on a local run, and a dark mode the stylesheet the site hands the page (the operator's look, 1.0.3.19) is not written in. All of them are off in the page's options, so it offers what the site offers and fetches from nothing but this host and the one font. `sidebar.spec` opens the page and holds it there: no Ask AI, no developer toolbar, no dark mode button, no request to any other host.

## Files

- [`api/TheYard.Api/ApiDocument.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/ApiDocument.cs): the document's name, title and routes, the public-surface filter, and the two transformers.
- [`api/TheYard.Api/Replies.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Replies.cs): the named shapes the endpoints answer with.
- [`api/TheYard.Api/VehicleWire.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/VehicleWire.cs): `VehicleView`, the vehicle as the wire carries it.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the generator and the reference page wired, and every public endpoint named, summarised and typed.
- [`api/TheYard.Api/TheYard.Api.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/TheYard.Api.csproj): the two packages, pinned.
- [`api/TheYard.Tests/ApiDocumentTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ApiDocumentTests.cs): the four rules, held.
- [`docs/ADR-075-the-rules-a-change-has-to-pass.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-075-the-rules-a-change-has-to-pass.md): the rules table, with this record's row.

# ADR: Error handling, one shape everywhere

Status: accepted, 2026-09-02, shipped as 1.0.0.29. Steve's ask, in the same
message as the telemetry: "hook up error handling and log every API call
and error, and every React error." The README also promised this at the
start of the build and it was still open.

## Context

Three gaps, all of them honest ones the README already listed:

1. **An unhandled exception was a shapeless 500.** The error middleware
   recorded it for the Admin tab and rethrew, and the framework answered
   with an empty body. A caller learned nothing, and the response looked
   the same as a proxy failure.
2. **Two different 400 bodies.** A rejected query answered `{ "error":
   "..." }` and a rejected bid answered `{ "reason": "..." }`. The browser
   had to know which endpoint it had called to read the message, and a new
   endpoint had a coin flip to make.
3. **A render crash was a white page.** React unmounts the whole tree when
   a render throws. There was no boundary, so a bug in one component took
   the site with it, and nothing about it ever reached the Admin tab.

## Decision

**One failure shape: RFC 9457 ProblemDetails.** `AddProblemDetails` plus
`UseExceptionHandler` turn every unhandled exception into
`application/problem+json` with a status, a title and a trace identifier.
Every deliberate 400 uses `Results.Problem(...)` with the human-readable
message in `detail`, so a query rejection and a bid rejection read the
same way:

```json
{ "type": "...", "title": "The bid was rejected", "status": 400,
  "detail": "Your bid must be at least $23,300.", "traceId": "00-a1b2..." }
```

The 404 for an unknown vehicle stays an empty 404: there is nothing to
say that the status code does not. (That sentence stopped being true
quietly, and the addendum at the end of this record says when and what
replaced it.)

**The browser reads `detail` first.** `src/lib/data.ts` prefers `detail`,
falls back to the old `reason` and `error` keys so nothing breaks
mid-deploy, and then to a generic sentence. One helper, used by every
call.

**Every request is logged, as structured JSON.** `AddHttpLogging` records
the method, path, status and duration for every API call; the console
formatter writes JSON so a log line is machine-readable wherever it lands
(ADR: Telemetry, when it ships, sends the same events to Application
Insights). Static files and the SPA fallback are excluded, or the log is
mostly bundle chunks.

**A React error boundary at the root.** `ErrorBoundary` wraps `<App />`
in `main.tsx`. A render crash shows what happened plus two ways out
(reload, or back to the inventory with the query string dropped) instead
of a blank page. Errors that never reach a boundary, thrown in an event
handler or an unhandled promise rejection, are caught by
`window.onerror` and `window.onunhandledrejection`.

**Browser errors land where server errors already do.** All three paths
POST to `/api/errors/client`, which records into the same ring buffer the
Admin tab reads, tagged with the page the visitor was on. The Admin tab's
Recent errors card now shows both sides of the app.

## In the code

The handler and the logging, in `api/TheYard.Api/Program.cs`:

```live path=api/TheYard.Api/Program.cs region=problem-details
```

The endpoint browser errors report to:

```live path=api/TheYard.Api/Program.cs region=client-errors
```

A deliberate 400, in the same shape:

```live path=api/TheYard.Api/Program.cs region=inventory-endpoint
```

The boundary, and the reporter every path uses:

```live path=src/components/ErrorBoundary.tsx region=boundary
```

```live path=src/components/ErrorBoundary.tsx region=report
```

The two window-level handlers, in `src/main.tsx`:

```live path=src/main.tsx region=bootstrap
```

How the browser reads a failure:

```live path=src/lib/data.ts region=problem-detail
```

## Consequences

- A caller can read one field, `detail`, for the message on any failure
  from this API.
- `traceId` on every problem response ties a visitor's report to a log
  line, and to a request in Application Insights once telemetry ships.
- The ring buffer holds fifty entries, browser and server together, and
  is in memory: it is a demo's observability, not an audit log. That
  limitation is recorded in ADR: Observability and is the reason the
  telemetry record exists.
- The boundary catches render crashes only. Event handlers and promises
  are covered by the window handlers, which is why both exist.
- Reporting is best effort. `keepalive` lets a report survive the
  navigation away, and a failed report is swallowed rather than replacing
  the error the visitor already sees.

## Addendum, 2026-09-15: the empty 404 was not empty

`UseStatusCodePages` was added to the pipeline after this record was written,
and with the problem-details service registered it fills any bare status code
with a ProblemDetails body on the way out. So the "empty 404" above had been
answering `application/problem+json` with a generic title for some time, and
nothing said so. When the API started describing itself (ADR: The API
describes itself), every declared response had to match the wire, and a
declaration of "empty" would have been the one lie in the document. The
endpoints that answer 404 and 401 now say so themselves, with a title and a
one-sentence detail in the shape every other failure here uses. The status
codes did not change; the body names what was not found instead of the
middleware naming nothing.

## Addendum, 2026-09-19: the frames, and still not the message

Steve, on a 500 he found on his own Admin tab: recent errors should be a table, and they should show
the stack trace with line numbers. Both, and the second one needs saying carefully, because this
record's own rule is that the list at `/api/errors` carries an exception's **type** and never its
message: a message is where a framework writes a filesystem path, a connection detail or the value
that broke a constraint, and the list is public.

The frames are a different thing from the message. A frame is a method, the file it is in and the
line it is on, which is source this repository already publishes in full, and it is the whole
distance between "something threw on `/api/admin/machines`" and "line 160 of Machines.cs reads a
decimal into a double". So the ring carries up to twelve frames per entry, the endpoint serves them,
the card shows them behind a disclosure in a table with the time, the status and the path, and the
message stays out. A test throws on the self-test endpoint and asserts both halves: the frames are
there, and the sentence the self-test throws with is not.

## Files

- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the handler, the logging, the client-error endpoint, and the 400s.
- [`api/TheYard.Api/Observability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Observability.cs): the ring buffer both sides record into.
- [`src/components/ErrorBoundary.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/ErrorBoundary.tsx) and [`ErrorBoundary.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/ErrorBoundary.module.css): the boundary and the reporter.
- [`src/main.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/main.tsx): the boundary around the app and the two window handlers.
- [`src/lib/data.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/data.ts): reading `detail`.
- [`api/TheYard.Tests/ProblemDetailsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ProblemDetailsTests.cs): every 400 carries the same shape, and a browser report reaches the errors list.
- [`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts): a reported browser error appears on the Admin tab.
- [`docs/ADR-010-observability.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-010-observability.md): the Admin tab this feeds.

## Addendum, 2026-09-25 (1.0.3.27): a page left open across a deploy

Steve, on his iPhone, on the Admin tab after 1.0.3.26 rolled: every card he opened showed this boundary with "Importing a module script failed". His page was 1.0.3.25's. It asks for each card's chunk by the hashed name that build gave it, and the roll had replaced those files with 1.0.3.26's. Nothing was broken on the server, and a reload fixed it; the boundary was the wrong answer to a stale page. From 1.0.3.27 an error that is a chunk the server no longer has (each engine words it differently; `src/lib/staleChunk.ts` knows the three, and Vite's own preload error) loads the page again onto the new version, once, and says "The site was just updated" for the moment that takes, from the boundary's first frame, so the error card never flashes first. A page that is offline is not reloaded, since the same words come from a chunk that had no network and a reload would land on the browser's offline page. A second such failure inside a minute is not a stale page, so it is reported and shown, never looped. `admin.spec` stands in for a deploy by refusing the Errors card's chunk: refused once, the page reloads and the card opens; refused every time, the page reloads once and then shows the error.

# ADR: Error handling, one shape everywhere

Status: accepted, 2026-09-02, shipped as 1.0.0.29. Steve's ask, in the same
message as the telemetry: "hook up error handling and log every api call
and error, and every react error." The README also promised this at the
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
say that the status code does not.

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

The handler and the logging, in `api/TheBlock.Api/Program.cs`:

```live path=api/TheBlock.Api/Program.cs region=problem-details
```

The endpoint browser errors report to:

```live path=api/TheBlock.Api/Program.cs region=client-errors
```

A deliberate 400, in the same shape:

```live path=api/TheBlock.Api/Program.cs region=inventory-endpoint
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

## Files

- [`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs): the handler, the logging, the client-error endpoint, and the 400s.
- [`api/TheBlock.Api/Observability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Observability.cs): the ring buffer both sides record into.
- [`src/components/ErrorBoundary.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/ErrorBoundary.tsx) and [`ErrorBoundary.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/ErrorBoundary.module.css): the boundary and the reporter.
- [`src/main.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/main.tsx): the boundary around the app and the two window handlers.
- [`src/lib/data.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/data.ts): reading `detail`.
- [`api/TheBlock.Tests/ProblemDetailsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/ProblemDetailsTests.cs): every 400 carries the same shape, and a browser report reaches the errors list.
- [`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts): a reported browser error appears on the Admin tab.
- [`docs/ADR-010-observability.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-010-observability.md): the Admin tab this feeds.

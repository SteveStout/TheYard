# ADR: Observability, the Admin tab

Status: roughed in 2026-09-01, the evening it was asked for. A polish pass
is planned for the Thursday build day; this records the shape and the
reasoning so the rough-in is a decision, not an accident.

## In plain words

The site has a public Admin tab that shows its own condition: the health checks with how long each took, and what Azure reports about the running container. A third card lists recent server errors, kept in memory and cleared on each deploy.

What that is worth: a developer sees a slow check or a restart without opening the Azure portal, and the organization can show that the site reads its own state but can change nothing (Reader scope).

## Context

The site could report its version but not its condition. The ask, verbatim:
health checks and errors, viewable from the website like an admin tab,
reaching through Azure.

## Decision

An Admin tab in the header, a real screen with three cards that fetch and
fail independently:

- Application health. Hand-rolled probes behind /healthz (liveness),
  /readyz (readiness), and /api/health (structured JSON: overall status,
  each probe, uptime, version, commit). The container healthcheck now hits
  /healthz. The framework health-check library is the planned upgrade; the
  hand-rolled version shipped the same evening it was asked for.
- Azure's view of the container. The container group runs as a
  user-assigned identity, that identity now holds Reader on its own
  resource group, and the API trades the identity for a management-plane
  token and reads its own container group: group state, container state,
  restart count, and the image Azure believes it is running. Cached 60
  seconds. Anywhere the identity endpoint does not exist, local dev
  included, the card degrades to app-level health and says so.
- Recent server errors. Middleware records unhandled exceptions and 5xx
  responses into a fixed 50-entry in-memory buffer served at /api/errors.
  No stack traces are exposed. The buffer resets on every deploy and the
  screen says that plainly.

The tab is public, no login. On this site the observability is the
exhibit, the same reasoning that serves the Bicep file from a menu. A real
product gates this behind authentication and ships errors to a persistent
sink; both are documented here and deliberately not built, the same
pattern as the undeployed production design.

## Consequences

- The client id and resource path baked in as defaults are identifiers,
  not secrets; a token for them can only be minted from inside the
  container itself.
- Reader scope means the site can see itself but change nothing.
- The error buffer is honest about its limits, which is the demo working
  as intended: the limits are part of the story.

## Addendum, 2026-09-02: second pass, shipped as 1.0.0.17

After the first pass, Steve said he liked a screen where the health checks
are easy to see. The second pass makes the screen say more without
saying it louder.

- **Every health check reports its duration.** Each probe is timed with a
  stopwatch and the Admin tab prints the milliseconds beside the check in
  the muted style, so a slow disk or a slow lookup is visible before it
  turns into a failure. The readiness endpoint uses the same probes and is
  unchanged.
- **Azure's card lists the container's recent events.** The management
  read already returned the container's instance view; the card now shows
  its last three events, newest first, with the name, how many times it
  happened, when it last happened, and the message trimmed to a line. A
  restart story (pulled, started, killed, started again) reads from the
  tab without opening the portal. Unavailable degrades exactly as before.
- **The sweep.** Five comments in Program.cs carried a double-encoded em
  dash, and one of them a mangled "resume", from an encoding-blind edit on
  the first day; they are plain ASCII now, changed byte by byte with
  everything else in the file untouched.

The samples below are read from this build's source each time the page is
served (ADR: Live code samples). The timed checks
([`api/TheYard.Api/Features/Health/HealthEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Health/HealthEndpoints.cs)):

```live path=api/TheYard.Api/Features/Health/HealthEndpoints.cs region=health-checks
```

The events, read from the same management response (the observability
types moved to
[`api/TheYard.Api/Shared/Observability/Observability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Observability/Observability.cs)
in ADR: The staff review):

```live path=api/TheYard.Api/Shared/Observability/Observability.cs region=azure-events
```

The proof is one API test that every check carries a non-negative
duration and two end-to-end checks that the Admin tab prints it, on a
laptop and on a phone, in
[`api/TheYard.Tests/AdminEndpointTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AdminEndpointTests.cs),
[`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts)
and [`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts).
The events list is proven by the live site, since only the container on
Azure can ask about itself.

## Where it sits

Everything this record built lives in the host Api and the front end: the probes in Endpoints/HealthEndpoints.cs, the ErrorRingBuffer and the AzureSelf reader in Observability.cs, and the cards under src/components/admin. Each card fetches and fails on its own, so one broken reading never blanks the tab. No port was needed because the container reads its own memory and its own Azure resource, and the price is an error buffer that empties on every deploy. Errors that must survive a roll are the reason to choose differently, and that is the job ILogStore, an Application port, now does for logs that outlive the container.

## Files

- [`api/TheYard.Api/Features/Health/HealthEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Health/HealthEndpoints.cs): the probes (region health-checks),
  `/healthz`, `/readyz` and `/api/health`.
- [`api/TheYard.Api/Features/Health/ErrorEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Health/ErrorEndpoints.cs) and [`api/TheYard.Api/Features/Admin/Operations/AdminEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Operations/AdminEndpoints.cs): `/api/errors` and
  `/api/admin/azure`.
- [`api/TheYard.Api/Shared/Wiring/RequestPipeline.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Wiring/RequestPipeline.cs): the middleware that records server errors (region error-log).
- [`api/TheYard.Api/Shared/Observability/Observability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Observability/Observability.cs): the health record, the error
  ring buffer and the Azure reader (region azure-events).
- [`src/components/admin/HealthCard/HealthCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/HealthCard/HealthCard.tsx), [`src/components/admin/ErrorsCard/ErrorsCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/ErrorsCard/ErrorsCard.tsx) and
  [`src/components/admin/AdminPanel/AdminPanel.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/AdminPanel/AdminPanel.module.css): the three cards.
- [`api/TheYard.Tests/AdminEndpointTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AdminEndpointTests.cs): the API proof;
  [`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts) and [`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts):
  the browser proof.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the container health check that hits `/healthz`.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the deploy's Verify step asks
  `/readyz` before it trusts a roll (ADR: The staff review).

## The look, from the live site

![The Admin tab at 1.0.0.22: three passing health checks with their durations, Azure reporting the container group Running with its last three events, and no server errors recorded](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-admin.jpg)

The health card, from the fetch to the rows
([`src/components/admin/HealthCard/HealthCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/HealthCard/HealthCard.tsx), a chunk of its own since the workbench):

```live path=src/components/admin/HealthCard/HealthCard.tsx region=health-card
```

## Addendum, 2026-10-07 (1.0.3.89): ready means the catalogue is in

The process listens before its catalogue loads (ADR: The ports learn to wait, the addendum on listening first), so `/readyz` gained one check that gates it, `catalogue`, which passes once the default store's hundred thousand vehicles are in memory. `/healthz` still answers the moment the process is up. `/api/health` shows the new check beside the others, and since it is an API read it waits for the load like every other, so the Admin tab sees the check pass; it is `/readyz` that says 503 while a fresh container loads. The deploy waits on `/readyz` before it reads the filters, and a blue-green swap will wait on the same thing.

## Addendum, 2026-10-29: the error ring has its own file, and the Azure read takes a token

Two changes, both forced by a rung rather than chosen for this record's own reasons.

`Observability.cs` was 294 lines, four inside the three-hundred cap, and the cancellation rung
needed three parameters in it. A split made under the pressure of an unrelated change is made by
the alphabet instead of by job, so it was made first and by job: `ErrorEntry`, `StackFrames` and
`ErrorRingBuffer` are now
[`api/TheYard.Api/Shared/Observability/ErrorRing.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Observability/ErrorRing.cs),
which changes when what is recorded about a failure changes. `Observability.cs` keeps its name,
the health probe's outcome, the site asking Azure about itself, and an index of its parts. The
`azure-events` region a live sample on this page reads was left where it is.

`AzureSelf.GetStateAsync` takes the cancellation of the poll that asked and carries it to the
token exchange and to all three reads, so a card the Admin tab has stopped drawing stops asking
Azure about this web app. It caches for sixty seconds either way, so the saving is a connection
and not a read.

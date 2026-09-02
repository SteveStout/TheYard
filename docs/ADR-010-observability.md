# ADR: Observability, the Admin tab

Status: roughed in 2026-09-01, the evening it was asked for. A polish pass
is planned for the Thursday build day; this records the shape and the
reasoning so the rough-in is a decision, not an accident.

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

Steve's words after the first pass: "I like a screen where you can easily
see health checks". The second pass makes the screen say more without
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
([`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs)):

```live path=api/TheBlock.Api/Program.cs region=health-checks
```

The events, read from the same management response (the observability
types moved to
[`api/TheBlock.Api/Observability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Observability.cs)
in ADR: The staff review):

```live path=api/TheBlock.Api/Observability.cs region=azure-events
```

The proof is one API test that every check carries a non-negative
duration and two end-to-end checks that the Admin tab prints it, on a
laptop and on a phone, in
[`api/TheBlock.Tests/AdminEndpointTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/AdminEndpointTests.cs),
[`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts)
and [`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts).
The events list is proven by the live site, since only the container on
Azure can ask about itself.

## Files

- [`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs): the probes (region health-checks),
  `/healthz`, `/readyz`, `/api/health`, `/api/errors`, `/api/admin/azure`,
  and the middleware that records server errors.
- [`api/TheBlock.Api/Observability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Observability.cs): the health record, the error
  ring buffer and the Azure reader (region azure-events).
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx) and
  [`src/components/AdminPanel.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.module.css): the three cards.
- [`api/TheBlock.Tests/AdminEndpointTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/AdminEndpointTests.cs): the API proof;
  [`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts) and [`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts):
  the browser proof.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the container health check that hits `/healthz`.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the deploy's Verify step asks
  `/readyz` before it trusts a roll (ADR: The staff review).

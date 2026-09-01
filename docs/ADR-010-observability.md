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

# ADR: Telemetry that outlives the container

Status: accepted, 2026-09-03, shipped as 1.0.0.34. Steve's ask: "hook up
azure error handling and show it on the admin page and log every api call
and error, and every react error. App insights please tell me it's on the
solo version." It is: the free trial includes it, and the first 5 GB a month
of ingestion costs nothing.

## Context

ADR: Observability built the Admin tab on an in-memory ring buffer of fifty
entries, and said out loud what that costs: the buffer resets on every roll,
holds nothing older than the current container, and cannot answer a question
about last Tuesday. ADR: Error handling then routed browser errors into the
same buffer, which made it more useful and no more durable.

Structured request logs go to the container's stdout, where Azure keeps
them for a container group's lifetime and nobody reads them. Neither the
logs nor the buffer can answer the questions worth asking about a running
site: how many requests failed in the last hour, which route is slowest,
what threw and when.

## Decision

**Application Insights, in the same resource group, on the free tier.** A
Log Analytics workspace (`log-theyard-ss`, PerGB2018, 30-day retention) and
a component bound to it (`appi-theyard-ss`), both in RG-THEYARD-SS. The
daily ingestion cap is set to 0.1 GB with collection stopped at the cap, so
the resource cannot generate a charge even if something goes wrong.

**The API sends with the Azure Monitor OpenTelemetry distro.** One call in
Program.cs gives requests, dependencies and exceptions with their durations,
correlated by trace id. It is registered only when a connection string is
present, so a local run and every test are untouched and need no fake.

**The connection string never enters the repository.** It is an ingestion
key. The deploy workflow reads it from Azure at roll time with the federated
credential it already uses, masks it in the log, and substitutes it into the
container spec, which carries a `__APPINSIGHTS_CONNECTION_STRING__`
placeholder in the repository. The app treats any value starting with `__`
as absent, so a manual rollback with the committed file runs clean instead
of sending telemetry to nowhere.

That read is allowed to fail. If Azure does not answer, the roll leaves the
placeholder in place, logs a workflow warning and ships anyway. Telemetry is
an addition to this system, not a dependency of it, and a delivery pipeline
that a monitoring resource can block is a worse trade than an hour of
missing traces.

**Browser errors go through the API, not straight to Azure.** The boundary
and the two window handlers already POST to `/api/errors/client`
(ADR: Error handling); that endpoint now also writes a structured log, which
the distro forwards. Three things follow: the page loads no second external
script, the ingestion key stays server-side, and a browser error is
searchable beside the server's own with the same trace correlation.

**The Admin tab reads it back with the container's own identity.**
`id-theyard-ss` was granted Monitoring Reader on the component, so the
container asks Application Insights about itself the same way it already
asks Azure Resource Manager about its own container group. No key is stored
for reading either.

## In the code

The registration, and the reader it hands the Admin tab
(`api/TheYard.Api/Program.cs`):

```live path=api/TheYard.Api/Program.cs region=telemetry
```

```live path=api/TheYard.Api/Program.cs region=telemetry-endpoint
```

One query answers the whole card, because three questions in three round
trips is three chances to time out (`api/TheYard.Api/Telemetry.cs`):

```live path=api/TheYard.Api/Telemetry.cs region=kql
```

```live path=api/TheYard.Api/Telemetry.cs region=read
```

Kusto answers in columns and rows; the card wants objects. Reading each row
by column name rather than position is what keeps a query edit from shifting
every value silently:

```live path=api/TheYard.Api/Telemetry.cs region=shape
```

The card, which renders every state the reader can answer with
(`src/components/AdminPanel.tsx`):

```live path=src/components/AdminPanel.tsx region=telemetry-card
```

The container spec's placeholder, and the roll step that fills it in
(`infra/aci-theyard.yaml`, `.github/workflows/deploy.yml`):

```live path=infra/aci-theyard.yaml region=container
```

```live path=.github/workflows/deploy.yml region=roll
```

## What this cost, exactly

Two resources and one role assignment, all inside RG-THEYARD-SS, all
additive. Nothing else in the group changed. Steve's authorization is
recorded in the mentor notes with the scope it covers and the rollback,
which is `az monitor app-insights component delete` and
`az monitor log-analytics workspace delete`: the app runs unchanged without
the connection string.

## Consequences

- The Admin tab now shows the last hour from a store that survives a roll,
  beside the in-memory buffer that does not. Both are on the page on
  purpose: one is durable and a minute stale, the other is immediate.
- A trace id ties a ProblemDetails response (ADR: Error handling) to a
  request in the portal.
- Telemetry is off in development and in CI, which is the right default and
  also means the tests prove the off path rather than the on path. The on
  path is proved from the domain after each deploy.
- A deploy can now ship with telemetry off, and says so in the workflow log
  and on the Admin card. That is the intended failure, not a gap.
- The reader caches for a minute. The Admin tab refreshes every thirty
  seconds and would otherwise ask Azure twice a minute forever for a page
  someone left open.
- 30-day retention and a 0.1 GB daily cap are demo settings, not production
  ones. A real deployment would size both to its traffic and its audit
  needs, and would not put the Admin tab on a public route at all
  (ADR: Observability says why this one is).

## Files

- [`api/TheYard.Api/Telemetry.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Telemetry.cs): the reader, its query and its shaping.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the registration, the browser-error log, and the Admin endpoint.
- [`api/TheYard.Api/TheYard.Api.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/TheYard.Api.csproj): the one package this added.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the card and its three states.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml) and [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the placeholder and the roll-time substitution.
- [`api/TheYard.Tests/TelemetryTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/TelemetryTests.cs): the off path, the shape of every answer, and the endpoint.
- [`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts): the card rendering its not-configured state in a browser.
- [`docs/ADR-010-observability.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-010-observability.md) and [`docs/ADR-023-error-handling.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-023-error-handling.md): the Admin tab and the error path this extends.

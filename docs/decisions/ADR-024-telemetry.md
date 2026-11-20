# ADR: Telemetry that outlives the container

Status: accepted, 2026-09-03, shipped as 1.0.0.34. Steve asked for
Azure error handling shown on the admin page, with every API call, every API
error and every React error logged, in Application Insights if the solo
subscription includes it. It does: the free trial includes it, and the first 5 GB a month
of ingestion costs nothing.

## In plain words

This page sends the site's requests and errors to a store in Azure that keeps them when the container is replaced (Application Insights), on the free tier with a daily cap so it cannot generate a charge. The Admin tab reads the last hour back from it, and the key that sends the data never enters the repository.

What that is worth: a developer can see which route is slowest or what threw an hour ago, and the organization gets lasting monitoring at no cost that never blocks a deploy.

## Context

ADR: Observability built the Admin tab on an in-memory ring buffer of fifty
entries, and said out loud what that costs: the buffer resets on every roll,
holds nothing older than the current container, and cannot answer a question
about last Tuesday. ADR: Error handling then routed browser errors into the
same buffer. That made it more useful and no more durable.

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
present. So a local run and every test are untouched and need no fake.

**The connection string never enters the repository.** It is an ingestion
key. The deploy workflow reads it from Azure at roll time with the federated
credential it already uses, masks it in the log, and substitutes it into the
container spec, which carries a `__APPINSIGHTS_CONNECTION_STRING__`
placeholder in the repository. The app treats any value starting with `__`
as absent, so a manual rollback with the committed file runs clean instead
of sending telemetry to nowhere.

That read is allowed to fail. If Azure does not answer, the roll leaves the
placeholder in place and logs a workflow warning, then ships anyway. Telemetry is
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

```live path=api/TheYard.Api/Shared/Wiring/TelemetryRegistration.cs region=telemetry
```

```live path=api/TheYard.Api/Features/Admin/Operations/AdminEndpoints.cs region=telemetry-endpoint
```

One query answers the whole card, because three questions in three round
trips is three chances to time out (`api/TheYard.Api/Shared/Observability/Telemetry.cs`):

```live path=api/TheYard.Api/Shared/Observability/Telemetry.Kql.cs region=kql
```

```live path=api/TheYard.Api/Shared/Observability/Telemetry.cs region=read
```

Kusto answers in columns and rows; the card wants objects. Reading each row
by column name rather than position is what keeps a query edit from shifting
every value silently:

```live path=api/TheYard.Api/Shared/Observability/Telemetry.Shape.cs region=shape
```

The card, which renders every state the reader can answer with
(`src/components/admin/TelemetryCard/TelemetryCard.tsx`, a chunk of its own since the workbench):

```live path=src/components/admin/TelemetryCard/TelemetryCard.tsx region=telemetry-card
```

The container spec's placeholder, and the roll step that fills it in
(`infra/aci-theyard.yaml`, `.github/workflows/deploy.yml`):

*The container group file this showed live retired on 19 October with the container groups' last settings (ADR: One web app for every .NET process); it is kept at [`infra/aci-theyard.yaml` at 22b3015](https://github.com/SteveStout/TheYard/blob/22b3015/infra/aci-theyard.yaml), the last commit that carried it.*

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

## Where it sits

Telemetry is a host concern and stays in the host Api, where Composition/TelemetryRegistration.cs adds the exporter only when a connection string is present and TelemetryReader in Telemetry.cs reads the last hour back for the Admin tab's TelemetryCard in the front end. Application and Domain never learn that Application Insights exists, which is the onion's dependency rule at work, and no SOLID principle is in play beyond that. It cost two Azure resources held to a 0.1 GB daily cap and one Monitoring Reader grant, plus a deploy that can ship with telemetry switched off. A site with real traffic would size retention and the cap to its load and take these readings off a public route.

## Files

- [`api/TheYard.Api/Shared/Observability/Telemetry.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Observability/Telemetry.cs): the reader, its query and its shaping.
- [`api/TheYard.Api/Shared/Wiring/TelemetryRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Wiring/TelemetryRegistration.cs): the registration.
- [`api/TheYard.Api/Features/Health/ErrorEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Health/ErrorEndpoints.cs): the browser-error log.
- [`api/TheYard.Api/Features/Admin/Operations/AdminEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Operations/AdminEndpoints.cs): the Admin endpoint (region telemetry-endpoint).
- [`api/TheYard.Api/TheYard.Api.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/TheYard.Api.csproj): the one package this added.
- [`src/components/admin/TelemetryCard/TelemetryCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/TelemetryCard/TelemetryCard.tsx): the card and its three states.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/22b3015/infra/aci-theyard.yaml) and [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the placeholder and the roll-time substitution.
- [`api/TheYard.Tests/TelemetryTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/TelemetryTests.cs): the off path, the shape of every answer, and the endpoint.
- [`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts): the card rendering its not-configured state in a browser.
- [`docs/decisions/ADR-010-observability.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-010-observability.md) and [`docs/decisions/ADR-023-error-handling.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-023-error-handling.md): the Admin tab and the error path this extends.

## Addendum, 2026-11-07 (1.0.4.156): the box's routes are left out of the request telemetry

Until this version every request reached Application Insights. From it, every request under /api/admin/cloud/box/ is left out of the request telemetry through the distro's own filter (`AspNetCoreTraceInstrumentationOptions.Filter`, set in TelemetryRegistration.cs): the box's agent posts about forty-three thousand times a day, which would spend most of the component's 0.1 GB daily cap on itself (ADR: The box reports itself). Every other path is kept.

## Addendum, 2026-11-16 (1.0.5.71): what fills the workspace, and what the site leaves out

In plain words: the log workspace's first 5 GB a month are free, and it was on course for about twice that. Most of it was not the site's visitors at all. The site now leaves out two kinds of line nothing reads, and the largest source, the job hunt's Function, has its own change with the lane that owns it.

Read live on 16 November from the workspace `log-theyard-ss` (the `Usage` table and each table by role, `az monitor log-analytics query`): about 0.34 GB a day billable, 0.332 GB on 15 November, so roughly 10 GB a month. Over seven days: traces 0.951 GB, metrics 0.661, dependencies 0.272, performance counters 0.178, requests 0.066. In one day the Function `func-jobhunt-ss` sent 147.2 MB of information-level traces, 96 % of all traces (its host's log level is Information for every category), and 28 MB of its health-check metric; the site sent 5.4 MB of traces, of which Entity Framework's line for every command was 4.1 MB, and about 34 MB of the HttpClient's connection-pool metrics (open connections 25.5 MB, active requests 5.7, connection duration 1.8, time in queue 1.4).

What changes here:

1. The four connection-pool instruments are dropped with the distro's documented view (`MetricStreamConfiguration.Drop`). No card, alert or query reads them; the request durations stay.
2. Entity Framework's command lines are filtered from the telemetry provider alone, below a warning. The Admin tab's rings and the kept log read the same lines through their own providers and keep them all.
3. The Function's log levels are the job hunt's lane's file (`functions/jobhunt/host.json`), so the measurement and the ask went to that lane by a state.md row: Warning by default with the Function's own run lines kept, which the process table reads.

Together that is about 1.1 GB a month off the site's side and, once the Function's change lands, about 4.5 GB more, which brings the month under the free 5 GB without raising the component's cap, as Steve chose. `TelemetryTrimTests` holds the two lists.

## Addendum, @DATE@ (@VER@): the site's dependency rows, one in three, and a cancelled read is not an exception

In plain words: the site's own dependency rows were the largest single source in the workspace, 62.8 MB in the 24 hours to midday on 20 November (66,606 rows), against a December line of 0.167 GB a day for the whole workspace. And the Admin tab's failures list carried 237 TaskCanceledException rows in seven days for this web app, every one a card reader's own call that it cancelled at its time limit (Azure Monitor and Resource Manager reads at about 20 s, Log Analytics queries, Cosmos DB writes of kept card documents, the Function's health route at 10 s), none of them a request a visitor made.

What changes:

1. **One in three successful dependency calls is kept** (`TelemetrySampling`, a processor added before the distro so it runs before the exporter). The choice is the call's own span id. A kept row carries the exporter's sample rate, 33.3 %, so its item count is 3 and a count by item count stays whole. Requests, exceptions and every failed call are kept whole. Every query the Admin tab runs against the dependency table is named in `TelemetrySamplingTests`: the reporters row now counts `sum(ItemCount)`, the component's size reads billed bytes (which sampling lowers, as it should), and a run's trace is the Function's, which is not sampled; the failures list and the telemetry card read requests and exceptions only.
2. **A cancelled call is a read that timed out, not an exception.** The HttpClient instrumentation no longer records an exception on a call itself; it records any other exception as before and tags a cancellation `theyard.read = timed out`, which stays in the dependency row with its duration and its failure. The card that made the read already says in words that it is not read and why (the addendum of 1.0.4.87, "a read that times out is a gap, not a 500").

No daily cap is the fix: a cap drops errors with everything else. `TelemetrySamplingTests` holds both.

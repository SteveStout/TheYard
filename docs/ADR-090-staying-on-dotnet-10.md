# ADR: Staying on .NET 10, and on xUnit v2 for now

Status: accepted, 2026-10-03. Two upgrades looked at on the same day and both left for later, each with the measurement that decided it.

## In plain words

The site runs on .NET 10, the long-term support release, and moved to its newest patch (10.0.12) the day this was written. .NET 11 is out as a release candidate, and the site waits for the final release before it moves. The tests stay on version 2 of the xUnit test library, because moving to version 3 turned out to change how the tests are run, not only which package they use.

What that is worth: a developer gets a runtime that is patched for three years and a test setup that keeps working the same way on a laptop and in the pipeline, and the organization gets upgrades that are planned and measured instead of taken because something newer exists.

## .NET 10, patched

Microsoft's release metadata, read live on 2026-10-03, lists 10.0.12 (8 September 2026) as the current .NET 10 release, a security release with six CVEs fixed. Every `Microsoft.*` package at 10.0.11 moved to 10.0.12 in this pass: the JWT bearer handler, OpenAPI, Identity's EF Core store, the three EF Core packages and the test host. The Docker images use the floating `sdk:10.0` and `aspnet:10.0` tags, so every build pulls the current patch.

.NET 10 is a long-term support release, supported until November 2028. .NET 11 is at 11.0.0-rc.1 (8 September 2026) with "go-live" support, which means Microsoft supports it in production but the final release is still to come, and it is a standard-term release supported for two years after that.

The move to 11 makes sense when three things are true: 11.0 has shipped as a final release; EF Core 11, the Cosmos DB SDK and the other packages here have stable releases built against it; and there is something in it the site needs. Until then, an RC buys preview behaviour on a live site and a second upgrade a few weeks later.

## xUnit v3, measured and left for a lane of its own

xUnit v3 (`xunit.v3` 4.0.1) was tried against the whole suite. It built, and two things decided it:

- **It runs on a different test platform.** v3 runs tests on Microsoft.Testing.Platform, and with the .NET 10 SDK `dotnet test` refuses to run such a project through the old VSTest runner until the repository opts in to the new `dotnet test` mode. That changes the gate's script, the CI workflow's `--filter "Store!=cosmos"` (the new platform filters differently), the trx output the gate reads, and the coverage collector CI uses for its floors, which is a VSTest data collector.
- **It adds a new analyzer rule.** `xUnit1051` asks every call that takes a `CancellationToken` to pass the test's own token. It raised 267 warnings across the suite, and the gate treats any warning as red.

None of that is hard, and all of it is a change to how the tests run rather than to what they check. Doing it inside a pass about architecture would have mixed two kinds of change in one deploy. So the suite stays on xUnit 2.9.3, the last v2 release, with `xunit.runner.visualstudio` at 3.1.5, the newest runner for v2. The move to v3 is its own lane: opt in to the new `dotnet test`, change the filter and the coverage tool together, and pass the cancellation token where the analyzer asks.

The test packages that leave the way tests run alone did move in this pass: `Microsoft.NET.Test.Sdk` to 18.10.1, `Microsoft.AspNetCore.Mvc.Testing` to 10.0.12, and `NetArchTest.eNhancedEdition` 1.4.5 arrived for the ring rules. `coverlet.collector` stayed at 6.0.4 for the measured reason in ADR: Technology versions.

## Where it sits

No ring: this is a platform and tooling decision about the runtime and the test runner, so the onion and SOLID do not apply to it. It cost a measured trial and a wait. A final .NET 11 release with the packages caught up, or a free lane for the test platform, would change it.

## Files

- [`api/TheYard.Api/TheYard.Api.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/TheYard.Api.csproj): the host's packages at 10.0.12.
- [`api/TheYard.Infrastructure/TheYard.Infrastructure.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/TheYard.Infrastructure.csproj): EF Core and Identity at 10.0.12.
- [`api/TheYard.Tests/TheYard.Tests.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/TheYard.Tests.csproj): xUnit 2.9.3 and its runner.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the floating .NET 10 image tags and the Node 24 build stage.
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the test filter and the coverage collector a move to xUnit v3 would change.

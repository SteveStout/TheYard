# ADR: Technology versions

Status: accepted, 2026-10-03. Every package and image this repository names, the version in use, the newest stable version on the day it was read, and the reason when the two differ.

## In plain words

This page lists every library and tool the site is built from, which version it uses, and the newest stable version available when the list was checked. Where the site is behind, the reason is written beside it. A test reads this table against the project files, so the table cannot say one version while the code uses another.

What that is worth: a developer can see in one place what the site runs on and why anything is held back, and the organization can answer "are you patched?" from a page instead of a search.

## How the list was read

Every "latest stable" below was read live on 2026-10-03 from nuget.org, the npm registry, Docker Hub, the Node.js release schedule and Microsoft's .NET release metadata, through the runner on the build machine and never from memory. Preview, beta and release-candidate versions are left out of "latest stable". `TechnologyVersionsTests` holds the "In use" column to the project files: every package reference in `api/` and every dependency in `package.json` has a row, each row's version is the one the project uses, and a row that is behind carries a reason.

## .NET packages

| Package | In use | Latest stable | Why behind |
| --- | --- | --- | --- |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.12 | 10.0.12 | |
| Microsoft.AspNetCore.OpenApi | 10.0.12 | 10.0.12 | |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.12 | 10.0.12 | |
| Microsoft.EntityFrameworkCore.Design | 10.0.12 | 10.0.12 | |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.12 | 10.0.12 | |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.12 | 10.0.12 | |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | 10.0.12 | |
| Scalar.AspNetCore | 2.17.13 | 2.17.13 | |
| Microsoft.Azure.Cosmos | 3.63.2 | 3.63.2 | |
| Newtonsoft.Json | 13.0.4 | 13.0.4 | |
| Azure.Identity | 1.17.1 | 1.21.0 | Pinned. 1.21.0 broke the managed-identity token for SQL Server in the container, and PackagePinTests holds the pin (ADR: A second store on Cosmos DB, and what it costs). |
| Azure.Communication.Email | 1.1.0 | 1.1.0 | |
| Azure.Monitor.OpenTelemetry.AspNetCore | 1.6.0 | 1.6.0 | |
| Microsoft.NET.Test.Sdk | 18.10.1 | 18.10.1 | |
| coverlet.collector | 6.0.4 | 10.1.0 | Measured both on the same code: 10.1.0 counts branches differently and reports 66.0% where 6.0.4 reports 67.4%, which would move CI's coverage floor with no change to a test. Moving it is its own change, with the floor set again from a measured run. |
| xunit | 2.9.3 | 2.9.3 | The last v2 release. xUnit v3 is 4.0.1 and changes the test platform (ADR: Staying on .NET 10, and on xUnit v2 for now). |
| xunit.runner.visualstudio | 3.1.5 | 4.0.0 | 4.0.0 is the runner for xUnit v3; 3.1.5 is the newest for v2. |
| NetArchTest.eNhancedEdition | 1.4.5 | 1.4.5 | |

## Build tools and images

| Package | In use | Latest stable | Why behind |
| --- | --- | --- | --- |
| .NET SDK and runtime | 10.0 (10.0.12) | 10.0.12 | 11.0 is a release candidate (ADR: Staying on .NET 10, and on xUnit v2 for now). |
| Microsoft.Build.Sql | 2.3.0 | 2.3.0 | |
| node (Docker build stage and CI) | 24 | 24 | 24 is Active LTS until 20 October 2026; 26 becomes LTS on 28 October 2026. |

## Front-end packages

| Package | In use | Latest stable | Why behind |
| --- | --- | --- | --- |
| react | 19.3.0 | 19.3.0 | |
| react-dom | 19.3.0 | 19.3.0 | |
| marked | 18.0.14 | 18.0.14 | |
| highlight.js | 11.12.0 | 11.12.0 | |
| vite | 8.3.2 | 8.3.2 | |
| @vitejs/plugin-react | 6.1.1 | 6.1.1 | |
| typescript | 7.0.2 | 7.0.2 | |
| vitest | 5.0.3 | 5.0.3 | |
| @playwright/test | 1.63.0 | 1.63.0 | |
| @axe-core/playwright | 4.13.0 | 4.13.0 | |
| @types/react | 19.3.0 | 19.3.0 | |
| @types/react-dom | 19.3.0 | 19.3.0 | |
| oxlint | 1.86.0 | 1.86.0 | |
| prettier | 3.9.9 | 3.9.9 | |
| sharp | 0.35.5 | 0.35.5 | |
| concurrently | 10.0.5 | 10.0.5 | |

## C# 14, where it landed

The language moved with .NET 10. Each feature was used only where it removes code or says the intent more plainly:

- **Extension members.** `StandingRules` in `api/TheYard.Domain/StandingRules.cs` gives `Vehicle` a `RaisedTo` method and a `Reserve` property, the one copy of the rule the buyer's bids, the room's bids and the room's price to beat all use; it replaced three hand-written copies. `UtcBuckets` in `api/TheYard.Application/UtcBuckets.cs` gives `DateTimeOffset` the `UtcDay`, `UtcHour` and `UtcMinute` properties and replaced five helper methods spread across four classes.
- **Null-conditional assignment.** `cosmos?.Logger = ...` in `api/TheYard.Api/Composition/Startup.cs` replaced a four-line null check.
- **The `field` keyword.** Not used. No property here wraps a backing field that `field` would remove, and adding one to use the keyword would be the opposite of the point.

## Where it sits

No ring: this is a dependency and tooling record, so the onion and SOLID do not apply to it. It cost a live read of four registries and a test that keeps the table honest. A package with a reason to stay behind gets a row that says why, and a new release changes the right-hand column the next time the list is read.

## Files

- [`api/TheYard.Tests/TechnologyVersionsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/TechnologyVersionsTests.cs): the test that reads this table against the project files.
- [`api/TheYard.Tests/PackagePinTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PackagePinTests.cs): the Azure.Identity pin.
- [`package.json`](https://github.com/SteveStout/TheYard/blob/main/package.json): the front-end packages.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the build images.

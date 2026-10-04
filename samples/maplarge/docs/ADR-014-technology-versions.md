# ADR: Technology versions

Status: accepted, 2026-10-03.

## Context

A reader should be able to see what The Shed is built from without opening every project file,
and see whether any of it is behind.

## Decision

Every package, image and tool the project names is listed here with the version in use and the
newest stable version on the day it was checked (2026-10-03, read live from nuget.org, the npm
registry and Microsoft's .NET release list). `TechnologyVersionsTests` checks the "In use" column
against the project files and fails when a row that is behind gives no reason.

| Package | In use | Latest stable | Why behind |
| --- | --- | --- | --- |
| .NET SDK and ASP.NET Core runtime images | 10.0 | 10.0 | |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | 10.0.12 | |
| Microsoft.NET.Test.Sdk | 18.10.1 | 18.10.1 | |
| NetArchTest.eNhancedEdition | 1.4.5 | 1.4.5 | |
| xunit | 2.9.3 | 2.9.3 | The last v2 release. v3 changes how tests are run, and TheYard moves first. |
| xunit.runner.visualstudio | 3.1.5 | 4.0.0 | 4.0.0 is the runner for xUnit v3; 3.1.5 is the newest for v2. |
| typescript | 7.0.2 | 7.0.2 | |

The app itself uses no NuGet package at all: everything it does comes with ASP.NET Core. The page
needs no Node at run time either, because its compiled JavaScript is committed (ADR-006).

## What it cost

One table and one test that keeps it honest.

## Where it sits

This is a list of tools and touches no ring, so the onion and SOLID do not apply to it.

## Files

- [`tests/TestProject.Tests/TechnologyVersionsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/TechnologyVersionsTests.cs): the check.
- [`tests/TestProject.Tests/TestProject.Tests.csproj`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/TestProject.Tests.csproj): the test packages.
- [`package.json`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/package.json): the TypeScript compiler.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Dockerfile): the images.

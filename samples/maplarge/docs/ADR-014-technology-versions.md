# ADR: Technology versions

Status: accepted, 2026-10-03.

## In plain words

This page lists every package and tool The Shed is built from, the version it uses, and the newest stable version on the day the list was checked. Where it is behind, the reason sits beside it, and a test holds the list to the project files.

What that is worth: a reviewer sees in one table what The Shed is built from and that nothing is behind without a reason, and the organization gets a list the build keeps true.

## Context

A reader should be able to see what The Shed is built from without opening every project file,
and see whether any of it is behind.

## Decision

Every NuGet and npm package, the .NET images and the Node version the project names are listed here with the version in use and the
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
| Node (the TypeScript build and the node tests) | 24 | 26 (Current); 24 is the newest LTS | The long-term support line, the same one TheYard builds with. |

The app itself uses no NuGet package at all: everything it does comes with ASP.NET Core. The page
needs no Node at run time either, because its compiled JavaScript is committed (ADR-006).

## What it cost

One table and one test that keeps it honest.

## Where it sits

Outside the code: the project files, package.json and the Dockerfile, which TechnologyVersionsTests reads against this table. It cost one table and one test; a new release changes the right-hand column the next time the list is read.

## Files

- [`tests/TestProject.Tests/TechnologyVersionsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/TechnologyVersionsTests.cs): the check.
- [`tests/TestProject.Tests/TestProject.Tests.csproj`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/TestProject.Tests.csproj): the test packages.
- [`package.json`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/package.json): the TypeScript compiler.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Dockerfile): the images.

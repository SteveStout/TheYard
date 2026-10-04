# ADR: The starter, kept as given

Status: accepted, 2026-09-29.

## Context

MapLarge's test project arrives as a zip: `TestProject.sln`, `TestProject.csproj` on net8.0 with
controllers, a `Program.cs` that adds controllers and static files, one `TestController` that
returns a string, and a `wwwroot/index.html` that says hello. The brief asks for a file and folder
browser as a single page app over a JSON API, and says three things about how it wants to read the
result: simple beats boilerplate, delete what is not used, and spend the time on original code
rather than on framework or template.

The author's other project, [TheYard](https://theyard.stevenstout.biz), is a React and .NET 10 site
with two databases, a Docker image, Bicep, a deploy pipeline and ninety of these records. Almost
none of that belongs here, and the temptation to bring it anyway is the thing this record refuses.

## Decision

The starter's shape is the project's shape. `TestProject.sln` and `TestProject.csproj` keep their
names, `Program.cs` keeps one class with one `Main`, the API is controllers because the starter was,
`wwwroot/index.html` is the one page. `TestController.cs` is deleted: it was the placeholder for
what this is. The target is `net10.0`, the framework TheYard is on, because the two are one stack by
decision (Steve: the sample and the site share every version); the starter arrived on `net8.0`. The
brief lists Visual Studio 2022 or newer, Rider, VS Code and the command line SDK, and the .NET 10
SDK covers the last three and Visual Studio 2026 outright. The package versions the test project
references are the ones TheYard pins.

Five settings are added to the project file: warnings are errors, the code checkers run in the
build (the sealed rule sits in `.editorconfig`), doc comments are generated, culture is invariant,
and the documents, the sample home and the sources travel with a publish so a container can serve
them. No package is referenced by the app. The starter's `UseHttpsRedirection` runs in Development only:
behind the edge that terminates TLS the app sees HTTP, and the redirect would have nothing to
redirect to. The test project references xunit and the in-memory test host,
which is the least a test project can reference.

What comes from TheYard is practice, not code: the layering rule (ADR-002), the record set you are
reading, the tests that hold the rules (ADR-009), the palette (ADR-010), and the habit of measuring
before claiming (ADR-008). What is left behind, each with its reason:

| Left behind | Why |
| --- | --- |
| React, Vite, a bundler | The brief says vanilla JavaScript or TypeScript with no UI library; the page is TypeScript compiled by `tsc` to plain modules, nothing else (ADR-006). |
| .NET 8 | The starter's framework; the sample runs on .NET 10 with TheYard, one stack for both. |
| The two data stores | A file browser's store is the filesystem. |
| Application Insights, accounts, the admin tab | Nothing to observe or protect at this size; a request log is a line in the console. |
| Docker, Bicep, the pipeline | Kept to a Dockerfile and one workflow so the sample can run beside TheYard on the same plan, and nothing more; the brief's reviewers open the solution, not the container. |
| Eighty-six records | Twelve, one per decision that was actually made here. |

## What it cost

The starter's `Program` is a class with a static `Main` rather than top-level statements, which is
a little more ceremony than the .NET 8 template writes. It is kept because the in-memory test host
names `Program` as its entry point either way, and because a reviewer who opened the zip should find
the file they sent.

## Addendum, 2026-10-03: one more test package, two more records

The test project now also references NetArchTest, which reads the compiled app to check which folder may use which (ADR-013). Its version is the one TheYard pins, like the others. There are fourteen records now, not twelve: ADR-013 and ADR-014 are new.

## Where it sits

This record touches no ring: it sets the project file and the starter's shape, which every folder sits inside.

## Files

- [`TestProject.csproj`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/TestProject.csproj): the starter's project file plus five settings.
- [`Program.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Program.cs): the host, still one class and one `Main`.
- [`wwwroot/index.html`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/wwwroot/index.html): the one page.

```live path=TestProject.csproj region=*
```

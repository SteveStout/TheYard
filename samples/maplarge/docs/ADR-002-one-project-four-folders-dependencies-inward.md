# ADR: One project, four folders, dependencies inward

Status: accepted, 2026-09-29.

## Context

TheYard is five projects in an onion: Data, Domain, Application, Infrastructure, Api, and the
compiler stops a reference from pointing the wrong way. Five projects for a file browser would be
the "framework and template usage" the brief asks not to see. One project with everything in one
folder would be the other failure: a reviewer could not tell a rule from a wire type from a disk
call without reading every file.

## Decision

One project, six folders, the same names and the same rule. A folder may use itself and anything
to its left, never anything to its right:

```
Data  <-  Domain  <-  Application  <-  Infrastructure  <-  Controllers  <-  Composition
```

- `Data/` is records and an enum, and nothing else: no method with a body, no dependency. If it
  computes anything it is in the wrong folder.
- `Domain/` is the rules: the home directory's guard, the search pattern, the totals. Pure. No
  filesystem, no clock, no HTTP; `HomePath` is tested against root strings on whatever OS runs the
  suite because it never asks the disk anything.
- `Application/` is the use cases (`FileBrowser`) behind one port (`IFileStore`), plus the options
  and the one exception type a use case throws. It knows what a browse is and not what a directory
  is.
- `Infrastructure/` is the disk, behind the port: `PhysicalFileStore`, thin calls into `System.IO`.
- `Controllers/` is HTTP: an action per route, each a line or three, and the handler that turns a
  refusal into a problem document. `Documentation/` sits beside it for the documents the app serves
  about itself and is used only by the controllers and the host.
- `Composition/` is the host's own folder, the outermost ring: one registration file per part of
  the app and the pipeline in its order. `Program.cs` calls them one line each, with the file
  beside each call, so it reads as a table of contents (the addendum below).

The compiler cannot hold a folder rule, so a test does: `LayeringTests` reads every `using
TestProject.X;` line in every folder and fails on one that points right. A second test holds Data
and Domain to no `File.`, no `Directory.`, no `DateTime.Now`.

The one port is what makes the use cases testable without a temp folder. `FileBrowserTests` runs
them over a store that lives in a dictionary and holds the rules: folders first, a move into itself
refused, an upload past the limit refused before a byte is read. The disk adapter is then tested
through the real host in `FilesApiTests`, where the thing under test is the wiring.

## What it cost

`FileBrowser` is the largest C# file in the project at about 260 lines, because every rule lands
there. That is the point: a reviewer who wants the rules reads one file. The alternative, a service
per verb, would spread eight rules over eight files and the shared ones (resolve the path, check
what is there, refuse home) over all of them.

## Addendum, 30 September: why Program.cs is so short

Steve, reading the host before the review: `Program.cs` "should be high level like the yard", so
that it is readable at a glance. It had grown to 90 lines of registrations, each with its reason.
The registrations moved, unchanged, into `Composition/`, the way TheYard's `Composition/` holds its
own, and `Program.cs` is now the list of calls in the order the app is made:

```live path=Program.cs region=composition
```

Program.cs is the first file a reviewer opens, so it answers one question: what is this app made
of, and in what order. Each line names a part and the file beside it shows how. There is no rule
in it to review (a rule in the host is a bug in layering, docs/STYLE.md), so the only thing it can
get wrong is the order, and the order is on one screen. A new part is one line here and one file
in `Composition/`. `LayeringTests` counts `Composition` as the outermost folder, and the host test
holds `Program.cs` to 40 lines, so it cannot grow back.

The middleware file is `RequestPipeline.cs`, ASP.NET Core's own name for it, because "pipeline"
alone can mean a build or a data pipeline and this one is the path every request takes, in order,
and every response takes back out:

```live path=Composition/RequestPipeline.cs region=request-pipeline
```

The file that sets up the Docs tab and the footer's version is
`DocumentationAndVersionRegistration.cs`, named for both things it registers: "Docs" was an
abbreviation, and it hid the version. Its comments say where each value is really set (the top of
the changelog, the deploy, the docs folder), because the registration itself sets none of them.

## Files

- [`Program.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Program.cs): the table of contents.
- [`Composition/FilesRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Composition/FilesRegistration.cs): the port and the use cases, registered.
- [`Application/IFileStore.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/IFileStore.cs): the port.
- [`Application/FileBrowser.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/FileBrowser.cs): the use cases.
- [`Infrastructure/PhysicalFileStore.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Infrastructure/PhysicalFileStore.cs): the disk.
- [`tests/TestProject.Tests/ProjectRulesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/ProjectRulesTests.cs): `LayeringTests`.
- [`tests/TestProject.Tests/FileBrowserTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/FileBrowserTests.cs): the use cases over the fake store.

The port, as it is in this build:

```live path=Application/IFileStore.cs region=*
```

The rules a move and a copy share:

```live path=Application/FileBrowser.cs region=transfer-rules
```

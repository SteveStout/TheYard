# ADR: One project, four folders, dependencies inward

Status: accepted, 2026-09-29.

## Context

TheYard is five projects in an onion: Data, Domain, Application, Infrastructure, Api, and the
compiler stops a reference from pointing the wrong way. Five projects for a file browser would be
the "framework and template usage" the brief asks not to see. One project with everything in one
folder would be the other failure: a reviewer could not tell a rule from a wire type from a disk
call without reading every file.

## Decision

One project, five folders, the same names and the same rule. A folder may use itself and anything
to its left, never anything to its right:

```
Data  <-  Domain  <-  Application  <-  Infrastructure  <-  Controllers
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
  refusal into a problem document. `Library/` sits beside it for the documents the app serves
  about itself and is used only by the controllers and the host.

The compiler cannot hold a folder rule, so a test does: `LayeringTests` reads every `using
TestProject.X;` line in every folder and fails on one that points right. A second test holds Data
and Domain to no `File.`, no `Directory.`, no `DateTime.Now`.

The one port is what makes the use cases testable without a temp folder. `FileBrowserTests` runs
them over a store that lives in a dictionary and holds the rules: folders first, a move into itself
refused, an upload past the limit refused before a byte is read. The disk adapter is then tested
through the real host in `FilesApiTests`, where the thing under test is the wiring.

## What it cost

`FileBrowser` is the largest file in the project at about 230 lines, because every rule lands there.
That is the point: a reviewer who wants the rules reads one file. The alternative, a service per
verb, would spread eight rules over eight files and the shared ones (resolve the path, check what is
there, refuse home) over all of them.

## Files

- [`Application/IFileStore.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/IFileStore.cs): the port.
- [`Application/FileBrowser.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/FileBrowser.cs): the use cases.
- [`Infrastructure/PhysicalFileStore.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Infrastructure/PhysicalFileStore.cs): the disk.
- [`tests/TestProject.Tests/RepoRulesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/RepoRulesTests.cs): `LayeringTests`.
- [`tests/TestProject.Tests/FileBrowserTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/FileBrowserTests.cs): the use cases over the fake store.

The port, as it is in this build:

```live path=Application/IFileStore.cs region=*
```

The rules a move and a copy share:

```live path=Application/FileBrowser.cs region=transfer-rules
```

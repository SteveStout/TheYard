# ADR: One project, seven folders, dependencies inward

Status: accepted, 2026-09-29.

## Context

TheYard is six projects in an onion: Data, Domain, Application, two Infrastructure adapters and
Api, and the compiler stops a reference from pointing the wrong way. Six projects for a file browser would be
the "framework and template usage" the brief asks not to see. One project with everything in one
folder would be the other failure: a reviewer could not tell a rule from a wire type from a disk
call without reading every file.

## Decision

One project, seven folders (six rings and `Documentation/` beside Controllers), the same names and the same rule. A folder may use itself and anything
to its left, never anything to its right:

```
Data  <-  Domain  <-  Application  <-  Infrastructure  <-  Controllers  <-  Composition
```

This is onion architecture, also called clean architecture: the business rules sit in the middle
and everything else depends on them, never the other way round. Pulling the rules out of the disk
and HTTP code, into `Domain/` and `Application/`, is what lets them be tested on their own.
`HomePath` is the clearest case: the security rule in the innermost ring, pure, strings in and a
decision out, so its tests run with no file system on Windows and on Linux (ADR-003). The disk is in
the outer ring behind the `IFileStore` interface, and `LayeringTests` fails the build if an inner
folder reaches for the file system or points outward.

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
refused, an upload past the limit refused before the file is written into home. The disk adapter is then tested
through the real host in `FilesApiTests`, where the thing under test is the wiring.

## What it cost

`FileBrowser` is the largest C# file in the project at about 260 lines, because every rule lands
there. That is the point: a reviewer who wants the rules reads one file. The alternative, a service
per verb, would spread eight rules over eight files and the shared ones (resolve the path, check
what is there, refuse home) over all of them.

## Addendum, 30 September: why Program.cs is so short

Steve, reading the host before the review, asked for `Program.cs` to stay high level, as it is in
TheYard, so that it is readable at a glance. It had grown to 90 lines of registrations, each with its reason.
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

## Addendum, 2 October: every file shows its intention

Steve, reading `src/main.ts`, asked for it to read like `Program.cs`, because a file a reader
cannot follow at a glance hides what it is for. He set the rule behind it at the same time: no
large, tangled files; anyone should be able to open a file and see its intention.

`src/main.ts` had grown to 148 lines that found the page's elements, kept the state, wrote the
address, drew the dialog, wired every button and filled in the footer. It is now the page's table
of contents, the same shape as `Program.cs`: one line per part, in order, with the file that holds
the details beside it.

```live path=src/main.ts region=composition
```

The parts moved, unchanged in what they do, to files named for their one job:
`src/navigation.ts` (the one way the page changes, and the only file that writes the address),
`src/ui/pageElements.ts`, `src/ui/controls.ts` and `src/ui/versionFooter.ts`. The same pass split
`src/ui/browser.ts`, the largest file at 414 lines, by job: `fileTable.ts` draws the table,
`rowPrompts.ts` asks questions inside a row, `uploads.ts` sends files and `notices.ts` shows what
happened, which leaves `browser.ts` to wire them together.

What that is worth: a reader opens any file and sees what it is for before reading how, and a
change to one job touches the one file that holds it. `FileShapeTests` holds the line: no C# or
TypeScript file runs past 300 lines, and every line of `src/main.ts` names the file that holds its
part.

## Addendum, 2026-10-03: the rings, checked in the compiled app

The title said four folders when it was written; the code has six rings, plus `Documentation/` beside Controllers, as the decision above says. The title changed to seven on 4 October; the file name and the address stay. `LayeringTests` no longer reads `using` lines. `OnionTests` now checks the rings by reading the compiled app, so a fully written name is caught too (ADR-013), and `LayeringTests` keeps the Data and Domain purity check and the `Program.cs` check. One line is tighter than the diagram: Controllers may not use Infrastructure or Composition or touch the disk, so downloads and the health check now go through `FileBrowser`, and Documentation reaches only Data. `FileBrowser` sits at the 300-line cap, not about 260.

## Where it sits

This record draws every ring, from Data in the middle out to Composition at the edge, and a folder may only use the folders inside it. It follows the D in SOLID: Application owns the IFileStore interface and Infrastructure plugs the disk into it, so the rules never call the disk by name. The cost is that the compiler cannot hold a folder line, so a test does, and FileBrowser carries every rule in one file of about 300 lines. With a bigger team or a second app reusing the rules, each ring would become its own project so the compiler stops a wrong reference.

## Files

- [`Program.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Program.cs): the table of contents.
- [`src/main.ts`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/src/main.ts): the page's table of contents.
- [`tests/TestProject.Tests/FileShapeTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/FileShapeTests.cs): no file past 300 lines, and the entry file a list.
- [`Composition/FilesRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Composition/FilesRegistration.cs): the port and the use cases, registered.
- [`Application/IFileStore.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/IFileStore.cs): the port.
- [`Application/FileBrowser.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/FileBrowser.cs): the use cases.
- [`Infrastructure/PhysicalFileStore.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Infrastructure/PhysicalFileStore.cs): the disk.
- [`tests/TestProject.Tests/ProjectRulesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/ProjectRulesTests.cs): `LayeringTests`.
- [`tests/TestProject.Tests/FileBrowserTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/FileBrowserTests.cs): browsing and search over the fake store; transfers and uploads have a file each beside it.
- [`tests/TestProject.Tests/SampleTree.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/SampleTree.cs): the fake store and the tree every use-case test starts from.

The port, as it is in this build:

```live path=Application/IFileStore.cs region=*
```

The rules a move and a copy share:

```live path=Application/FileBrowser.cs region=transfer-rules
```

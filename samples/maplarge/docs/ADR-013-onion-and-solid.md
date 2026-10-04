# ADR: Onion and SOLID, how this codebase holds them

Status: accepted, 2026-10-03.

## Context

The Shed is one project with its layers as folders (ADR-002). A folder rule that only lives in a
record is a rule a busy afternoon breaks, so this record says what the rings are, which test holds
each one, where each SOLID idea shows up, and when this shape would be the wrong one.

## Decision

The folders are rings, from the middle out. A ring may use the rings inside it, never one outside
it.

- **Data** holds plain records: what the API sends, and what the file store reports. Nothing else.
- **Domain** holds the rules that need no disk: `HomePath` keeps every path inside home,
  `NamePattern` matches a search, `ViewTotals` adds up a folder.
- **Application** holds the use cases, `FileBrowser`, and the port it needs, `IFileStore`.
- **Infrastructure** holds `PhysicalFileStore`, the one class that touches the files the app browses.
- **Documentation** sits beside Controllers and reads the app's own documents for the Docs tab. It
  uses Data and nothing outside itself.
- **Controllers** turn a request into a call to `FileBrowser` and its answer into a reply.
- **Composition** wires it all together, and is the only place that does.

`OnionTests` reads the compiled app with NetArchTest and checks each line: Data and Domain use
nothing outside .NET itself, Application uses only Domain and Data, Infrastructure never reaches
outward, Controllers never touch the disk, the file store or Composition, Documentation reaches
nothing outside itself, and only Composition registers services. It reads method bodies too, so a fully written name is caught as surely as a `using`
line. The controllers rule failed on the code as it was: the health check and the documents
controller both touched the disk. The other rules already held, so each was proved by adding one
forbidden reference and watching it fail.

SOLID is five ideas about how to shape classes. Here is where each one shows:

- **Single responsibility** (one reason to change). `FileBrowser` holds the rules, the controllers
  hold HTTP, `PhysicalFileStore` holds the disk.
- **Open/closed** (add a class, don't edit one). A Blob storage version would be one new class that
  implements `IFileStore` and one line in Composition. Downloads go through the store too, so they
  would work unchanged.
- **Liskov substitution** (any version fits). The tests swap in a fake store in memory, and every
  rule runs the same.
- **Interface segregation** (small interfaces). `IFileStore` is one port because `FileBrowser`, its
  only user, needs all of it.
- **Dependency inversion** (the inside owns the plug). Application declares `IFileStore`,
  Infrastructure fills it in, and `FileBrowser` gets it through its constructor.

Kept on purpose: `HomePath` refuses a bad path by throwing `PathRefusedException`, and
`FileBrowser` refuses a bad request by throwing `ApiRefusalException`. One handler turns both into
a problem reply (ADR-004). A typed result would mean the same checks in a new shape, so the
exceptions stay.

## When not to build it this way

A one-page form or a script does not need rings: one file is clearer. A service with several
teams or a second app that reuses the rules would turn each ring into its own project, so the
compiler stops a wrong reference instead of a test.

## What it cost

One test package (NetArchTest), seven tests, and moving the download stream and the health check
behind the use cases so the controllers never touch the disk.

## Where it sits

Every ring: this is the map of all of them and the five SOLID ideas. A second app reusing the rules would split the folders into projects instead.

## Files

- [`tests/TestProject.Tests/OnionTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/OnionTests.cs): the rings, read from the compiled app.
- [`Application/IFileStore.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/IFileStore.cs): the port.
- [`Application/FileBrowser.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/FileBrowser.cs): the use cases.
- [`Composition/FilesRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Composition/FilesRegistration.cs): where the port is filled in.

The ring rules, as they are in this build:

```live path=tests/TestProject.Tests/OnionTests.cs region=rings
```

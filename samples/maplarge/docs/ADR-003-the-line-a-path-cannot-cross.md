# ADR: The line a path cannot cross

Status: accepted, 2026-09-29.

## Context

The brief: "a server side home directory should be configurable via variable." Everything the API
does is relative to that directory, and every request carries a path. A path from a browser is
input, and the one thing a file API must never do is serve, write or delete outside the home because
a path said `..` in a way the code did not think of. This is the security model of the whole
project, so it is one class, and it is the first thing a reviewer should read.

## Decision

The folder the app shows is a setting, not code. It is the `Files:Home` line in
`appsettings.json`, and on a server the `FILES__HOME` environment variable replaces it. A full path
is used as written; a short one is taken from the folder the app runs from. Left empty, the app
shows `sample-home`, a small practice folder that ships with the project, so a fresh clone has
something to browse. The folder is created if it is missing.

What that is worth: a developer clones and runs with no setup, and an organization ships one build
to every machine and points each at its own folder without touching the code. Whatever folder is
set, the rest of this record is why no request can reach a file outside it.

`HomePath` is the only code that turns a request path into an absolute one. It sits in `Domain/`,
the innermost layer of the onion architecture (ADR-002): business logic pulled out of the disk and
HTTP code so it can be tested on its own. It is pure: it reads no filesystem, so the tests run it
against `C:\home\files` on Windows and `/home/files` elsewhere with the same assertions. `Resolve`
refuses three things as strings, before any filesystem touch:

1. A rooted path: a drive letter, a leading slash or backslash.
2. A `.` or `..` segment anywhere.
3. A segment carrying a character the operating system forbids in a name.

Then it joins, normalises with `Path.GetFullPath`, and requires the result to be the root or to
start with the root plus a separator. That last check is the one that catches whatever the first
three did not think of, and the "plus a separator" is the difference between `/home/files` and
`/home/files-old`, which begins with the same characters and is not inside.

Case follows the operating system: Windows compares without it, everything else exactly. Symbolic
links are followed, because the home is the operator's and a link they put there is theirs to serve;
the record says so rather than pretending otherwise.

A new name (an upload's file name, a new folder) goes through `ValidName`: one segment, not `.` or
`..`, no separator, no forbidden character. An upload keeps only `Path.GetFileName` of what the
browser sent, so `../../evil.txt` lands as `evil.txt` in the folder that was asked for.

The API answers every refusal with a 400 problem document carrying the sentence (ADR-004), which is
how a person finds out what the rule was rather than that there is one.

## What it cost

A path with a trailing space or dot on Windows is normalised by `GetFullPath` in ways this class
does not second-guess; the containment check still holds. Reserved device names (`CON`, `NUL`) are
not refused by name; on Windows `GetFullPath` maps them out of the home and the containment check
refuses the result.

## Addendum, 2026-10-03: home itself

`HomePath` now refuses a home at the top of a drive or of the filesystem, such as `C:\` or `/`, with an `ArgumentException` when it is built, because a home there would put every file on the machine in reach. `IsRoot` says whether a path is home itself, with or without a slash on the end. `Resolve` always hands back home in one spelling, so a path that Windows trims back to home (such as `...`) is still home, and home still cannot be deleted or moved.

## Where it sits

HomePath lives in Domain, the innermost ring, and every path from a request walks through it before anything can touch a file. It follows the S in SOLID: its one job is deciding whether a path stays inside home, so a change to that rule touches one class. Because it only reads strings, it cannot see where a symbolic link inside home really points, and it follows the link. If strangers could make links in the home folder, a second check on the disk in Infrastructure would earn its place.

## Files

- [`Domain/HomePath.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Domain/HomePath.cs): the guard.
- [`Application/FilesOptions.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/FilesOptions.cs): the settings.
- [`Composition/FilesRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Composition/FilesRegistration.cs): `HomeFor`, where configuration becomes a `HomePath`.
- [`tests/TestProject.Tests/HomePathTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/HomePathTests.cs): the refusals, on both operating systems.

The guard, as it is in this build:

```live path=Domain/HomePath.cs region=guard
```

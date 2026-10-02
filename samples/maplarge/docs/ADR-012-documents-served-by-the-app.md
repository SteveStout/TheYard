# ADR: Documents served by the app, with live code

Status: accepted, 2026-09-29.

## Context

A record that describes code drifts from it the week after it is written, unless the code it quotes
is read from the build rather than pasted. TheYard serves its records from inside the running app
and expands a `live` fence into the current lines of a named region at request time. The same
here, in about a hundred lines, because it is the mechanism that keeps these twelve records
honest.

## Decision

**The catalogue.** `DocumentationCatalog` lists what the Docs tab shows, in order: Start here, the
README, About Steven, the records by number, then the guides (Style, Built with AI, Changelog). A
slug is a lower-case file name; `GET /api/docs` is the list and `GET /api/docs/{slug}` is one
document as `text/markdown`. The files are read from `docs/` at request time, so editing a record
and reloading is enough.

**Live code.** A record may hold an empty fenced block whose info string reads `live
path=Domain/HomePath.cs region=guard`. `LiveSamples.Expand` replaces it with an ordinary fenced
block holding the current lines between `// #region guard` and its `// #endregion` in that file,
from this build, with the path on the fence line as a caption. `region=*` shows a whole file. Paths
are checked as strings against a short list of allowed roots before any file is read, the same idea
as the home directory's guard on a shorter list; a path outside it, or a region that is not there,
renders a one-line note rather than an error, so a renamed region shows up on the page.
`DocumentationTests` requests every document and fails on any such note, which is how a record
cannot quote code that no longer exists.

**Rendered in the browser.** The brief says do not render HTML server side, and the server does not:
it serves markdown, and `src/lib/markdown.ts` reads it into a tree of plain objects that
`buildFromMarkdown` builds with `createElement` and `textContent`. The reader handles what the
records use (headings, lists, fenced code with a caption, tables, quotes, inline marks) and nothing
else, and it is tested in node with no DOM. A little colouring of comments, strings and keywords is
done the same way, as spans in the tree.

**The version.** `GET /api/version` reads the first `- 1.0.0.N` line of `docs/CHANGELOG.md` and the
short hash from `.git/HEAD` (following the ref, or `packed-refs`), once at startup. There is no
version typed anywhere else, so shipping a version and writing its changelog line are one act, and
the footer can never claim a version the log does not know. A build with no `.git` says "unknown";
a container is handed the hash in `SHED_COMMIT`.

## What it cost

A markdown reader of a hundred and fifty lines that a library would replace. It is kept because
the brief asked for original code over libraries, because it is the reason a document cannot carry
markup into the page, and because it is a reasonable thing to talk about in a code review.

## Addendum, 2 October: one About page, and every quoted file is published

About Steven is no longer in the catalogue. The page lives on TheYard, and the header, Start here
and the README link to it there, so there is one copy to keep current instead of two that drift
apart. The Docs tab now opens with Start here and the README.

Two records quoted files the publish left out: ADR-001 quotes `TestProject.csproj` and ADR-008
quotes `tests/TestProject.Tests/PerformanceTests.cs`. Every test passed, because the tests read the
source folder, while the live site reads the published output and showed the missing-sample note
in both. The project file now publishes both, and `PublishListTests` holds every file a live block
names to the publish list, so the gap fails the build instead of reaching a reader.

## Files

- [`Documentation/DocumentationCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Documentation/DocumentationCatalog.cs): the list and the slugs.
- [`Documentation/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Documentation/LiveSamples.cs): the expander.
- [`Documentation/VersionReader.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Documentation/VersionReader.cs): the version and the commit.
- [`Controllers/DocumentationAndVersionController.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Controllers/DocumentationAndVersionController.cs): the three routes.
- [`src/lib/markdown.ts`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/src/lib/markdown.ts): the reader.
- [`tests/TestProject.Tests/DocumentationTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/DocumentationTests.cs): every fence resolves, every link lands.
- [`tests/TestProject.Tests/PublishListTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/PublishListTests.cs): every document and every quoted file travels with a publish.

The allowed roots, and the check:

```live path=Documentation/LiveSamples.cs region=allowed
```

The reader's block loop:

```live path=src/lib/markdown.ts region=blocks
```

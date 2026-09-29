# ADR: Documents served by the app, with live code

Status: accepted, 2026-09-29.

## Context

A record that describes code drifts from it the week after it is written, unless the code it quotes
is read from the build rather than pasted. TheYard serves its records from inside the running app
and expands a `live` fence into the current lines of a named region at request time. The same
here, in about a hundred lines, because it is the mechanism that keeps these twelve documents
honest.

## Decision

**The catalogue.** `DocsCatalog` lists what the Docs tab shows, in order: Start here, the README,
the records by number, then the guides (Style, Built with AI, Changelog). A slug is a lower-case
file name; `GET /api/docs` is the list and `GET /api/docs/{slug}` is one document as
`text/markdown`. The files are read from `docs/` at request time, so editing a record and reloading
is enough.

**Live code.** A record may hold an empty fenced block whose info string reads
`live path=Domain/HomePath.cs region=guard`. `LiveSamples.Expand` replaces it with an ordinary
fenced block holding the current lines between `// #region guard` and its `// #endregion` in that
file, from this build, with the path on the fence line as a caption. `region=*` shows a whole file.
Paths are checked as strings against a short list of allowed roots before any file is read, the
same idea as the home directory's guard on a shorter list; a path outside it, or a region that is not
there, renders a one-line note rather than an error, so a renamed region shows up on the page.
`DocsTests` requests every document and fails on any such note, which is how a record cannot quote
code that no longer exists.

**Rendered in the browser.** The brief says do not render HTML server side, and the server does
not: it serves markdown, and `js/lib/markdown.js` reads it into a tree of plain objects that
`toDom` builds with `createElement` and `textContent`. The reader handles what the records use
(headings, lists, fenced code with a caption, tables, quotes, inline marks) and nothing else, and
it is tested in node with no DOM. A little colouring of comments, strings and keywords is done the
same way, as spans in the tree.

**The version.** `GET /api/version` reads the first `- 1.0.0.N` line of `docs/CHANGELOG.md` and the
short hash from `.git/HEAD` (following the ref, or `packed-refs`), once at startup. There is no
version typed anywhere else, so shipping a version and writing its changelog line are one act, and
the footer can never claim a version the log does not know. A build with no `.git` says "unknown";
a container is handed the hash in `SHED_COMMIT`.

## What it cost

A markdown reader of a hundred and fifty lines that a library would replace. It is kept because
the brief asked for original code over libraries, because it is the reason a document cannot carry
markup into the page, and because it is a reasonable thing to talk about in a code review.

## Files

- [`Library/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Library/DocsCatalog.cs): the list and the slugs.
- [`Library/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Library/LiveSamples.cs): the expander.
- [`Library/VersionReader.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Library/VersionReader.cs): the version and the commit.
- [`Controllers/DocsController.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Controllers/DocsController.cs): the three routes.
- [`wwwroot/js/lib/markdown.js`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/wwwroot/js/lib/markdown.js): the reader.
- [`tests/TestProject.Tests/DocsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/DocsTests.cs): every fence resolves, every link lands.

The allowed roots, and the check:

```live path=Library/LiveSamples.cs region=allowed
```

The reader's block loop:

```live path=wwwroot/js/lib/markdown.js region=blocks
```

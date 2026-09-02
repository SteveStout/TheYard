# ADR: The staff review

Status: accepted, 2026-09-02, shipped as 1.0.0.22.

## Context

Ten versions shipped in one day, each reviewed on its own before its push.
Nobody had yet read the day's work as one body of code the way a staff
engineer reads a pull request: for duplication between files, for the
seams where one change assumed another, for the small things a fast day
leaves behind. Steve's instruction, in his words: "play the role of
Arcitect/Staff engineer and code review your self, and make corrections and
ADR on improvements." This record is that review. Every finding is listed,
including the ones deliberately left alone, so the next reviewer starts
from the same page.

## Decision

The review is a written pass with three verdicts: fixed (shipped in this
version, with a test where behavior changed), kept (a choice that stands,
with its reason), and deferred (worth doing, not today, with what it would
take). It repeats whenever a day's work is large enough to need it, and each
pass gets its own dated addendum here.

### Fixed

- **One endpoint for every document.** Program.cs had twenty routes, one
  per document, each naming its file, and three of them bypassed the
  live-sample expander. They are one route over a catalog now: the slug in
  the address is looked up in
  [`api/TheBlock.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/DocsCatalog.cs),
  the file is read from the repo root, and every document goes through
  the expander. A slug that is not in the catalog is a 404, never a file
  read. Adding a record is one line in the catalog and one in
  [`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx),
  and
  [`api/TheBlock.Tests/DocsCatalogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/DocsCatalogTests.cs)
  fails the build when the two lists disagree, when a file is missing, or
  when any slug serves a live fence unexpanded. The directory walk that
  found the docs folder on every request is gone; the repo root is
  resolved once at startup.
- **The phone header's way home from Admin.** The brand button in the
  header called the list's back-to-inventory path, which does nothing while
  the Admin tab is showing; the sidebar's brand button knew to close Admin
  first. Both call the same home function now
  ([`src/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/App.tsx)),
  and the phone spec proves the tap
  ([`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts)).
- **Honest failure states on the Admin tab.** A card whose fetch failed
  showed "Loading" forever. Each card now says it could not read its data
  and that the next try is thirty seconds away
  ([`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx)).
  An observability screen that cannot say "I do not know" is not one.
- **The observability types out of the host file.** The health record,
  the error buffer and the Azure reader lived at the bottom of Program.cs.
  They moved verbatim to
  [`api/TheBlock.Api/Observability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Observability.cs),
  so Program.cs reads as a composition root: what is wired, not how each
  piece works. The live block in ADR: Observability follows them.
- **Build provenance read once.** The version and commit environment
  variables were read in two endpoints; they are read at startup beside
  the repo root and shared.
- **One brand mark.** The lightning bolt was drawn twice, in the sidebar
  and the phone header; it is one component,
  [`src/components/BrandMark.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/BrandMark.tsx).
  The four link rows in the sidebar were the same markup four times; they
  are one `LinkRow` in
  [`src/components/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.tsx).
  The section headings there were `h3` under a page with no `h2`; they are
  `h2`, and the block's indentation was straightened.
- **Tests clean up after themselves.** The live-sample tests created a
  temporary repository per test and left it in the temp folder; the test
  class now deletes what it made
  ([`api/TheBlock.Tests/LiveSamplesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/LiveSamplesTests.cs)).
- **The pipeline's small safeties.** The Deploy job has a thirty-minute
  timeout, so a hung roll cannot hold the deploy-production concurrency
  group for the default six hours, and its Verify step asks the origin's
  readiness endpoint as well as its version
  ([`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml)).
  CI runs with a read-only token
  ([`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml)).

### Kept

- **The doc viewer caches each document for the life of the tab**
  ([`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx)).
  A deploy under an open tab is served on the next load, and the page is
  no-cache since ADR: Cache headers, so a reload is always fresh.
- **The rendered markdown is our own.** The viewer sets HTML from the
  markdown the API serves; the API serves only files in this repository,
  and the expander splices source into fenced blocks, which the renderer
  escapes. That trust boundary is the repository, and it is written down
  here so nobody widens the catalog to user-supplied content without
  reading this.
- **A deploy has about a minute of downtime.** The roll replaces the one
  container group; zero-downtime is the App Service slots story under
  Hosting, deliberately undeployed. The Verify step waits for the new
  build rather than pretending otherwise.
- **The Admin endpoints stay public**, as ADR: Observability decided.
- **The live-sample whitelist is a string check first**, then a resolved
  path check, then the read, in that order, with tests for each rejection
  ([`api/TheBlock.Api/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/LiveSamples.cs)).
  The review looked for a way around it and found none.

### Deferred

- **The slug list lives in two places**, the catalog and the sidebar
  record, held together by a test. A served catalog (titles, menus and
  slugs from the API) would make it one, at the cost of the sidebar
  rendering after a fetch. Worth it when a second client appears.
- **Documents are expanded on every request.** The files are small and
  the ground is a warm disk; a per-build cache keyed by file is a few
  lines when a measurement asks for it.
- **The health checks are file-existence probes** and read 0 ms. A probe
  that exercises the dataset (a search) would give the duration column
  something to say.

## In the code

The endpoint and the catalog, read from this build
([`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs)
and
[`api/TheBlock.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/DocsCatalog.cs)):

```live path=api/TheBlock.Api/Program.cs region=docs-endpoint
```

```live path=api/TheBlock.Api/DocsCatalog.cs region=docs-catalog
```

## Consequences

- Program.cs is shorter by the twenty routes and the four types, and every
  document is served the same way; the next record costs two lines and
  cannot be forgotten on the server without a red test.
- The Admin tab's three cards each have three states, and a reviewer can
  see all three in the component.
- Seventeen records, every one reachable from the sidebar and from
  /api/docs/{slug}; the files each one decided are listed in the record
  itself, so a code review starts from the record and lands on the lines.

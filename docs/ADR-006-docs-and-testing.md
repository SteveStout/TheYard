# ADR: Docs and testing

Status: accepted, 2026-09-01. A full review of the ADR set and the test
suites is planned future work; this record captures the decisions as they
stand so nothing is lost before that review happens.

## Context

The project doubles as a portfolio piece. The decisions about how it is
documented and tested accumulated across the first week of building, in
chat and in session logs, and only some of them had made it into decision
records. This ADR closes that gap.

## The documentation decisions

- Docs are served by the running app, from header menus, so everything is
  exposed without opening the repository. The site tells its whole story
  standalone.
- The audience is a hiring manager or a person trying to learn. Plain
  language wins over jargon every time.
- Menus are organized parent over children. About holds the project docs
  and the resume. Hosting holds the hosting overview with its ADRs and the
  Bicep file. CI/CD holds the pipeline story. Best Practices holds the
  practice record with its ADRs. A new decision gets a record and a menu
  home the day it lands.
- Real configuration screenshots are embedded in the docs, captured from
  the actual dashboards, so every claim can be checked against a picture.
- The production design stays documented and undeployed: main.bicep is
  served in the Hosting menu as how this would be hosted in production.

## The testing decisions

- Three suites gate every ship: the .NET API tests, the vitest unit tests,
  and the Playwright end-to-end checks. A red suite stops the pipeline
  before any image is built.
- Every doc menu gets a Playwright spec proving the menu opens its
  documents in the running app. Docs are features here and get tested like
  features.
- The suites run locally through the same scripts that ship, so a green
  run means the same thing on a workstation and in a deploy.

## Consequences

- Decisions stop living only in chat transcripts and session logs.
- The planned review has a single starting point: this record plus the
  five ADRs before it.
- The cost is accepted and known: every new surface carries a doc, a menu
  entry, and a spec, which slows a ship by minutes and is worth it.

## Addendum, 2026-09-09: the drawing carries the two sites

The infrastructure drawing was redrawn by hand again for the morning's
change (ADR: A permanent address for the second site): the visitor's box
names both sites, the DNS box carries both CNAME records, the edge box says
the Host header picks the origin and names the two rules above the
catch-all, and the "both stores" box says the second group answers behind
the same edge at its own name and that the Store bar links each site to the
other rather than setting a cookie. A fourth drawing, TheYard's two sites,
shows that path on its own at `/api/docs/diagrams/two-sites`, drawn by
`docs/images/two-sites.mjs` in the same style. The PNG beside each SVG is
rendered by `docs/images/render.mjs` in the repository's own Chrome, as
before.

## Files

Documentation:

- [`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx): every document the sidebar can
  open, its title, its menu and its kind, one record.
- [`api/TheYard.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocsCatalog.cs): the same documents by slug on
  the server, held to the sidebar's list by a test (ADR: The staff review).
- [`docs/BEST-PRACTICES.md`](https://github.com/SteveStout/TheYard/blob/main/docs/BEST-PRACTICES.md), [`docs/HOSTING.md`](https://github.com/SteveStout/TheYard/blob/main/docs/HOSTING.md),
  [`docs/CICD.md`](https://github.com/SteveStout/TheYard/blob/main/docs/CICD.md): the three overviews the records hang under.

Testing:

- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the three suites as CI jobs, shown
  live below; a red job stops the deploy (ADR: The deploy pipeline).
- [`api/TheYard.Tests/TheYard.Tests.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/TheYard.Tests.csproj) and the tests beside
  it: the API suite, integration tests over the real host.
- [`vite.config.ts`](https://github.com/SteveStout/TheYard/blob/main/vite.config.ts): the unit suite's configuration (vitest, only
  `src/**/*.test.ts`).
- [`playwright.config.ts`](https://github.com/SteveStout/TheYard/blob/main/playwright.config.ts): the end-to-end suite, which launches the
  API and the dev server itself and drives the installed Chrome.
- [`tests/e2e/smoke.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/smoke.spec.ts) and the specs beside it: the browser
  checks, one file per surface.

```live path=.github/workflows/ci.yml region=ci-jobs
```

## Addendum, 2026-09-02: the diagram, the screenshots, and what counts as commented

Steve's words, the evening of the second build day: "Make sure we have a
diagram of our infrastructure and make sure we have as many screen shots
and code references as possible and if the code is in the ADR it's good
enough to be commented." Three rules follow from it, all in force.

- **The infrastructure has a picture.** [`docs/images/infrastructure.svg`](https://github.com/SteveStout/TheYard/blob/main/docs/images/infrastructure.svg)
  is drawn by hand from the records and the pipeline logs, rendered to
  [`docs/images/infrastructure.png`](https://github.com/SteveStout/TheYard/blob/main/docs/images/infrastructure.png), and served at the top of the
  Hosting page and the README. It is redrawn when a box changes; a picture
  that disagrees with the records is a bug in the picture.
- **Every surface has a screenshot from the live site.** The records that
  decided a look carry a capture of that look from the domain, taken by the
  same headless Chrome the end-to-end suite uses, signed out, so the picture
  is what a visitor sees and not what a developer sees. The captures live in
  [`docs/images`](https://github.com/SteveStout/TheYard/tree/main/docs/images) beside the configuration screenshots from the first
  day. The first pass found a real defect: a long Azure event message ran
  past the edge of its card, fixed in the same version.
- **Code shown in a record is documented by the record, and teaches.** A
  region marker names its record in a comment, the record explains the code
  beside the live sample, and the region itself opens with a comment written
  for someone meeting the pattern for the first time: why it is done this
  way, and what breaks otherwise. Steve's rule for it, on the second build
  day: "any code samples must be well documented, that helps people learn
  and grow." Code no record shows keeps the comments a reader needs at the
  line. Code that no record shows keeps its own comments. So the
  question for a reviewer is never "is this commented" but "which record
  shows this", and the Files section at the end of every record answers it.
- **A diagram opens on its own page.** The picture in a record is a preview
  that links to the drawing on a page of its own, opened in a new tab,
  zoomable, with its text selectable; the Data Flow page's text diagram
  became a drawing in the same style as the infrastructure one. Recorded in
  ADR: Diagram pages.

The unit suite's configuration and the end-to-end suite's two servers, read
from this build ([`vite.config.ts`](https://github.com/SteveStout/TheYard/blob/main/vite.config.ts), [`playwright.config.ts`](https://github.com/SteveStout/TheYard/blob/main/playwright.config.ts)):

```live path=vite.config.ts region=unit-tests
```

```live path=playwright.config.ts region=web-servers
```

The one record of every document the sidebar can open
([`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx)):

```live path=src/components/DocsMenu.tsx region=docs-record
```

## Addendum, 2026-09-09: the picture redrawn for two stores

The infrastructure drawing gained a row: the Azure Cosmos DB store beside the
Azure SQL one, and a box for both stores in one process with the toggle, the
peer read and the proof (ADR: One container, both stores; ADR: Same
performance, proven). The ship lane's arrows up to the origin now route
between the two store boxes, the counts in the CI box were brought up to the
suites as they run today, and the date in the heading moved. The preview is
rendered from the SVG at twice its size by
[`docs/images/render.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/render.mjs),
in the repository's own Chrome with the site's font, so the picture on the
README is the drawing and not a second drawing of it.

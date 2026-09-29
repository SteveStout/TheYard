# ADR: Every diagram opens on its own page

Status: accepted, 2026-09-02, shipped as 1.0.0.25. Steve's ask, from his
phone, after reading the day's records: "All diagrams should be separate
and open in a new page, so you can zoom in and follow."

## Context

The infrastructure diagram (ADR: Docs and testing, addendum) sat inside the
Hosting page and the README as a PNG scaled to the width of the document
dialog. On a laptop that reads; on a phone the dialog is about 340 pixels
wide and the diagram's text becomes texture. The Data Flow page had a text
diagram in a code block, which on a phone scrolls sideways inside the
dialog and cannot be zoomed at all. And every link inside a document had no
target, so a tap on a GitHub link in a Files section navigated the app's
own tab away from the app.

## Decision

1. **A diagram is a page.** `/api/docs/diagrams/{name}` serves a small HTML
   document with the SVG inlined: the title in the tab, the palette, a
   viewport line so a phone can pinch to zoom, and the text left selectable
   so a reader can find a name with the browser's own search. The names
   come from a catalog beside the documents catalog; anything else is a
   404 and never a file read.
2. **The record keeps a preview.** The PNG stays inline at the document's
   width, as the link to the page, with a caption under it that says where
   it opens. The picture still gives the shape at a glance; the page is
   where you read it.
3. **The data flow is drawn.** `docs/images/dataflow.svg` replaces the text
   diagram in the Data Flow page, in the same style as the infrastructure
   drawing: two lanes, the read path top to bottom and a bid's path beside
   it, every box a file with its path under the title, and the refetch loop
   that ties the two together. Like the first drawing it is redrawn when
   the code moves; the SVG is generated from a short layout script kept
   with the session notes, not drawn by hand.
4. **Links in a document open in a new tab.** A `marked` hook adds
   `target="_blank"` and `rel="noopener"` to every link in a served document
   and turns the site's own absolute links into relative ones. The
   documents name the live domain in full because they must also read right
   on GitHub; in the app the same link stays on whichever host is serving,
   so a checkout on localhost opens its own diagram page and not the live
   one.

## In the code

The endpoint, in `api/TheYard.Api/Program.cs`, and the catalog beside the
documents in `api/TheYard.Api/DocsCatalog.cs`:

```live path=api/TheYard.Api/Endpoints/DocsEndpoints.cs region=diagram-page
```

```live path=api/TheYard.Api/DocsCatalog.cs region=diagrams
```

The page itself, `api/TheYard.Api/DiagramPage.cs`. The palette is repeated
here and in each SVG on purpose: the page carries no bundle, so the tokens
file is not loaded, and a palette change touches the drawings anyway (ADR:
The palette):

```live path=api/TheYard.Api/DiagramPage.cs region=page
```

The hook that makes links leave the dialog without leaving the app, in
`src/lib/markdown.ts` since 1.0.0.141, when the renderer moved into a chunk
of its own (ADR: Code that reads like code, addendum):

```live path=src/lib/markdown.ts region=doc-links
```

The tests, in `api/TheYard.Tests/DiagramPageTests.cs`: every name in the
catalog opens as HTML with its SVG and its title, an unknown name is a 404,
and the XML prolog a standalone SVG may carry never reaches the page:

```live path=api/TheYard.Tests/DiagramPageTests.cs region=page-tests
```

## Consequences

- The page is on the domain under the API's no-cache rule (ADR: Cache
  headers), so a redrawn diagram shows on the next open.
- An SVG's own `<style>` rules are unscoped once inlined; the page uses
  element selectors only, so nothing collides. A future drawing keeps to
  the same class names.
- Screenshots are not diagrams and stay inline; a tap on one opens nothing.
  If that turns out to be wanted, the hook is the place.
- The Data Flow page lost its text diagram. The drawing carries every box
  and path the text had; the text version stays in the history at
  1.0.0.24 if anyone misses it.

## Files

- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the `/api/docs/diagrams/{name}` endpoint.
- [`api/TheYard.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocsCatalog.cs): the diagram catalog beside the documents.
- [`api/TheYard.Api/DiagramPage.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DiagramPage.cs): the HTML page around an SVG.
- [`api/TheYard.Tests/DiagramPageTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/DiagramPageTests.cs): the page tests.
- [`src/lib/markdown.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/markdown.ts): the link hook, beside the renderer it belongs to.
- [`docs/images/dataflow.svg`](https://github.com/SteveStout/TheYard/blob/main/docs/images/dataflow.svg) and [`docs/images/dataflow.png`](https://github.com/SteveStout/TheYard/blob/main/docs/images/dataflow.png): the new drawing and its preview; [`docs/images/infrastructure.svg`](https://github.com/SteveStout/TheYard/blob/main/docs/images/infrastructure.svg) the first one.
- [`docs/DATAFLOW.md`](https://github.com/SteveStout/TheYard/blob/main/docs/DATAFLOW.md), [`docs/HOSTING.md`](https://github.com/SteveStout/TheYard/blob/main/docs/HOSTING.md), [`README.md`](https://github.com/SteveStout/TheYard/blob/main/README.md): the previews and their captions.
- [`tests/e2e/hosting.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/hosting.spec.ts) and [`tests/e2e/smoke.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/smoke.spec.ts): the browser checks that the captions link to the pages, in a new tab, and that the pages answer.

## The look, from the live site

![The Hosting page on a 375 pixel phone: the infrastructure preview, unreadable at that width, with the caption under it that opens the diagram in a new page](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-diagram-caption.jpg)

![The data flow page as it opens on the phone: the title, the zoom hint, the Source and TheYard links, and the whole drawing fitted to the width](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-diagram-page.jpg)

![The same page zoomed in, the way a pinch does it: the read path boxes readable, InventoryService through Cards, detail, bid panel](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-diagram-zoomed.jpg)

![The infrastructure page on a laptop: the drawing at its full width with the header above it](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-diagram-laptop.jpg)

Taken from the domain at 1.0.0.25 by the repository's headless Chrome
(`mentor` tooling, no sign-in), the phone captures at 375 pixels and twice
the density; the third one has the SVG widened to 270 percent and scrolled,
which is what a pinch does on a phone.

## Addendum, 2026-09-09: the section

Five drawings now open on pages of their own, and until this addendum a
reader reached each one only through the document that carried its preview:
the infrastructure from four documents, the data flow from three, the two
sites from three, the database from App Architecture alone, and the SQL
Server and Cosmos DB comparison from its own page alone. Steve, asked whether
every diagram was linked on the UI, wanted one place, and the sidebar now
has a Diagrams section near the top: one row per page, each a link
that opens in a new tab the way the preview links already do, in the order a
reader meets the system (the whole, the data, the schema, the two sites, the
two stores). The list lives beside the menus in `src/library/sections.ts`, the server's
`DocsCatalog.Diagrams` stays the authority for which drawings exist, and a
test holds the two equal, so a drawing cannot gain a page without a row or a
row without a page; the browser suite reads the section and every row's
address. The rows are links rather than documents on purpose: a drawing is
read zoomed, on a page of its own, and the dialog the documents open in is
the wrong shape for it, which is the decision this record made in the first
place.

![The Diagrams section on the live site: five rows, Infrastructure, Data flow, The database, The two sites, SQL Server vs Cosmos DB, each with the new-tab icon, under the SQL vs Cosmos DB section](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/sidebar-sections.png)

## Addendum, 2026-09-29 (1.0.3.47): a page about him, on the same pattern

A search for his name with ".NET" or "Azure" found his LinkedIn and not this site. The site had a head that described the auction and a structured-data block naming him only as the author of the code, and no page whose subject was him. `/about` is that page, served by the API the way a diagram is: one small HTML document, `api/TheYard.Api/AboutPage.cs`, with the palette inline, a viewport line, selectable text, and nothing fetched from another host, the font included (the diagram pages still ask Google for theirs; this one uses the reader's own copy of the face or the system's). Outside `/api` because it is a page a search engine lists; a literal route wins over the app's fallback, and the dev server proxies it to the API like `/api`.

What it says comes from the served resume and the README and nothing else: his name as the heading, the resume's title line under it, the resume's summary tightened to one paragraph, one paragraph on what TheYard is with links to the Performance page, the decision records, the API reference and the gate's test record, and three links out: LinkedIn, the repository and the resume. Its head is its own: a title and description written for a search result, a canonical, Open Graph and Twitter tags, and a schema.org Person. Each container names itself from `Site:Url`, so the Cosmos DB site's page says its own address; with none set (a developer's machine, the test host) the request's address is used.

The Person says his name, the resume's title, the site, the two profiles that are him (`sameAs`) and his city, Saint Charles, Missouri, and nothing else: no street, no postcode, no email, no phone. `index.html` carries the same Person in a graph beside the `SoftwareSourceCode` node it already had, whose `author` now points at it by id. The sitemap lists `/about` at priority 0.9 with a lastmod the Dockerfile writes on the day the image is built, and the About section of the sidebar opens with it, in a new tab like every served page. Search Console's HTML-tag check has a slot: a comment in `index.html` that the image build replaces with the meta tag when the repository variable `GOOGLE_SITE_VERIFICATION` is set, and leaves alone when it is not. It ships empty.

Held by `DiagramPageTests` (the page, its head, what the Person may and may not say, each site naming itself), `PublicFaceTests` (the head's Person and slot, the sitemap row) and `DockerBuildInputsTests` (the stamp and the tag, and the variable passed to both builds of the image).

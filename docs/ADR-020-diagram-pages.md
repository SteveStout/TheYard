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

```live path=api/TheYard.Api/Program.cs region=diagram-page
```

```live path=api/TheYard.Api/DocsCatalog.cs region=diagrams
```

The page itself, `api/TheYard.Api/DiagramPage.cs`. The palette is repeated
here and in each SVG on purpose: the page carries no bundle, so the tokens
file is not loaded, and a palette change touches the drawings anyway (ADR:
The palette):

```live path=api/TheYard.Api/DiagramPage.cs region=page
```

The hook in `src/components/DocsMenu.tsx` that makes links leave the dialog
without leaving the app:

```live path=src/components/DocsMenu.tsx region=doc-links
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
- [`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx): the link hook.
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

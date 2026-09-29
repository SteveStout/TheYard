# ADR: Vanilla JavaScript, organised

Status: accepted, 2026-09-29.

## Context

The brief: build the UI in vanilla JavaScript or TypeScript, no React, no Angular, no UI library;
render all HTML client side. Plain JavaScript with no framework can still be organised, and the
organisation is what a reviewer will judge, so it is written down.

## Decision

**Modules, in two folders, with the same inward rule as the C#.** `js/lib/` is pure: `urlState`,
`format`, `markdown`, `api`. Nothing in `lib` touches the document, which is why three of them run
under `node --test` as they are. `js/ui/` renders: `dom` (the two ways HTML is made), `browser`,
`docs`, and `main.js` is the shell that reads the address and hands the state to a view. `ui` may
import `lib`; `lib` never imports `ui`.

**Two ways to make HTML, neither of them `innerHTML`.** `h(tag, attrs, ...children)` builds what the
code writes, with text as text nodes; `toDom(tree)` builds what the markdown reader read. A file
name, a folder name, a document's contents: none of it can become markup, because nothing is ever
parsed as markup. `markdown.test.js` checks that a `<script>` in a document arrives as text.

**Render from state, replace in one step.** A view is a function of the state it is handed and the
reply it fetched; it builds the new nodes and swaps them in with `replaceChildren`. There is no
diffing and no framework because at this size the whole table is cheaper to rebuild than to
reconcile. A render that was overtaken by a newer one (the person clicked twice) checks and
returns without drawing.

**One listener on the table.** Rows carry `data-path` and `data-kind`; buttons carry
`data-action`; one `click` listener on the grid reads both and dispatches. A table of a thousand rows
has one listener, not four thousand.

**The search box is made once and kept.** Rebuilding it on every render would take the caret from
someone typing. The breadcrumb and the table rebuild; the input does not.

**Listings are cached by path** for the life of the page and forgotten when anything under that
path is written, so Back is instant and a delete re-reads once. The cache lives in `api.js` beside
the calls that fill and empty it.

**Uploads go by `XMLHttpRequest`**, the one place `fetch` is not used, because `fetch` cannot report
upload progress and a person watching a large file go up should see a bar. A 409 (the name exists)
is offered as an Overwrite button in place, not a dialog.

**Every write asks in the row.** Delete asks a question with one confirming button; Move and Copy
swap the row's actions for an input holding the destination. No `window.prompt`, no modal on a
modal.

## What it cost

No framework means no reactivity: a view redraws when told, and the code has to remember to tell
it. The discipline that makes this bearable is ADR-005, one function that changes state, so there
is one place to forget.

## Files

- [`wwwroot/js/ui/dom.js`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/wwwroot/js/ui/dom.js): `h` and `toDom`.
- [`wwwroot/js/ui/browser.js`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/wwwroot/js/ui/browser.js): the file browser.
- [`wwwroot/js/lib/api.js`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/wwwroot/js/lib/api.js): every call, and the cache.
- [`tests/js/`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/js): the `node --test` suite.

The cache:

```live path=wwwroot/js/lib/api.js region=cache
```

Uploading with progress:

```live path=wwwroot/js/lib/api.js region=upload
```

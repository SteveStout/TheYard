# ADR: TypeScript, organised

Status: accepted, 2026-09-29.

## Context

The brief: build the UI in vanilla JavaScript or TypeScript, no React, no Angular, no UI library;
render all HTML client side. On 29 September, reading the first version's plain JavaScript, Steve asked for
it to stay simple and move to TypeScript. A page with no
framework can still be organised, and the organisation is what a reviewer will judge, so it is
written down.

## Decision

**TypeScript, compiled by `tsc` and nothing else.** The sources are in `src/`; `tsc` writes plain ES
modules to `wwwroot/js`, and the page loads those as they are with one `<script type="module">`. No
bundler, no dev server, no framework, no runtime dependency: `package.json` has one entry, the
compiler. The compiled output is committed, so `dotnet run` works with the .NET SDK alone and a
reviewer without Node still sees the page; `npm run build` regenerates it, and the compiler is
strict (`strict`, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`, unused locals and
parameters as errors). The shapes on the wire are declared once in `src/lib/types.ts`, in the same
snake_case as `Data/ApiResponses.cs`, so the two can be read side by side (ADR-004).

**Modules, in two folders, with the same inward rule as the C#.** `src/lib/` is pure: `types`,
`urlState`, `format`, `markdown`, `api`. Nothing in `lib` touches the document, which is why three
of them run under `node --test` as compiled. `src/ui/` renders: `elements` (the two ways page
elements are made), `pageElements`, `controls`, `versionFooter`, `browser`, `fileTable`, `rowPrompts`,
`uploads`, `notices`, `documentation`. `navigation.ts`
is the shell that reads the address and hands the state to a view, and `main.ts` is a short list of
the page's parts, one line each with the file beside it, read the same way as `Program.cs`. `ui` may import `lib`; `lib` never imports `ui`. A test holds the
line.

**Two ways to make HTML, neither of them `innerHTML`.** `buildElement(tag, attrs, ...children)`
builds what the code writes, with text as text nodes; `buildFromMarkdown(tree)` builds what the
markdown reader read. A file
name, a folder name, a document's contents: none of it can become markup, because nothing is ever
parsed as markup. `markdown.test.js` checks that a `<script>` in a document arrives as text, and a
test checks that no source uses `.innerHTML` at all.

**Render from state, replace in one step.** A view is a function of the state it is handed and the
reply it fetched; it builds the new nodes and swaps them in with `replaceChildren`. There is no
diffing and no framework because at this size the whole table is cheaper to rebuild than to
reconcile. A render that was overtaken by a newer one (the person clicked twice) checks and
returns without drawing.

**One listener on the table.** Rows carry `data-path` and `data-kind`; buttons carry `data-action`;
one `click` listener on the grid reads both and dispatches. A table of a thousand rows has one
listener, not four thousand.

**The search box is made once and kept.** Rebuilding it on every render would take the caret from
someone typing. The breadcrumb and the table rebuild; the input does not.

**Listings are cached by path** for the life of the page and forgotten when anything under that
path is written, so Back is instant and a delete re-reads once. The cache lives in `api.ts` beside
the calls that fill and empty it.

**Uploads go by `XMLHttpRequest`**, the one place `fetch` is not used, because `fetch` cannot report
upload progress and a person watching a large file go up should see a bar. A 409 (the name exists)
is an `ApiError` with a status the page can read, and is offered as an Overwrite button in place,
not a dialog.

**Every write asks in the row.** Delete asks a question with one confirming button; Move and Copy
swap the row's actions for an input holding the destination. No `window.prompt`, no modal on a
modal.

## What it cost

No framework means no reactivity: a view redraws when told, and the code has to remember to tell
it. The discipline that makes this bearable is ADR-005, one function that changes state, so there
is one place to forget. Committing the compiled output means two copies of every module in the
repository; the alternative, a build step before `dotnet run`, was judged the worse cost for a
reviewer who wants to run the thing.

## Where it sits

The page has two rings of its own: src/lib holds the pure pieces and src/ui draws, and a test stops lib from ever importing ui. It follows the S in SOLID: each file has one job, such as drawing the table, asking a question in a row or sending uploads, so a change lands in one file. The cost is that no framework redraws for us, so every view redraws only when navigate tells it to, and the compiled JavaScript is committed beside its source. A page with many views sharing live data would justify a small framework.

## Files

- [`src/main.ts`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/src/main.ts): the page's parts, in order, one line each.
- [`src/ui/elements.ts`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/src/ui/elements.ts): `buildElement`, `buildFromMarkdown` and `replaceContents`.
- [`src/ui/browser.ts`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/src/ui/browser.ts): the file browser.
- [`src/lib/api.ts`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/src/lib/api.ts): every call, and the cache.
- [`src/lib/types.ts`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/src/lib/types.ts): the wire, as TypeScript sees it.
- [`tsconfig.json`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tsconfig.json): the compiler, and the whole build.
- [`tests/js/`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/js): the `node --test` suite, over the compiled modules.

The cache:

```live path=src/lib/api.ts region=cache
```

Uploading with progress:

```live path=src/lib/api.ts region=upload
```

# ADR: The React configuration, explained for a new developer

Status: accepted, 2026-09-02, shipped as 1.0.0.24. Written at Steve's
request for a developer new to React and Vite, or new to this codebase,
who opens the root of the repository and wants to know what every
configuration file is for and why the source is laid out the way it is.

## In plain words

This page walks every configuration file at the root of the front end, in the order a request meets them, and says why each one is there. There is no router and no state library, and the page says what does those jobs instead.

What that is worth: a developer new to React or to this code learns the setup in one read instead of by trial and error, and the organization spends less senior time onboarding each new hire.

## Context

The root of the repository holds a package.json with a page of scripts
(eleven when this was written, seventeen today) and
three runtime dependencies, an index.html, a vite.config.ts, three
tsconfig files, and a playwright.config.ts. Under `src/` there is no
router, no state library and no CSS framework. A newcomer used to larger
React projects will look for those and wonder what replaced them. This
record walks the files in the order a request meets them, from the
manifest to the build in the container, with the why beside each. The
samples are the files, read from this build.

## The walk

### package.json: few scripts, fewer dependencies

`npm start` runs the API and the Vite dev server together (`concurrently
-k` kills both when either exits, and `vite --open` opens the browser).
`npm run build` is `tsc -b && vite build` because the two tools split the
job: Vite strips types without checking them, so the compiler runs first
and emits nothing (`noEmit`, below), and a type error stops the build.
`npm test` is Vitest and `npm run test:e2e` is Playwright; the two are
separated further down.

There are three runtime dependencies. `react` and `react-dom` are the
framework; `marked` renders the markdown the API serves for every document
in the sidebar. Everything else is a dev dependency, which is why the
production bundle is small and why adding a package is a decision, not a
reflex.

```live path=package.json region=*
```

### index.html is the entry point

Vite starts from HTML, not from a JavaScript file. The one script tag
loads `/src/main.tsx` as a module; in development the browser fetches the
source files one by one and Vite transforms them on the way, and in a
build Vite rewrites the tag to point at the hashed bundle (which is what
makes the year-long cache rule in ADR: Cache headers safe). The favicon is
an inline SVG data address in the brand colors, so it costs no request.
The two preconnect lines warm the connection to Google Fonts before the
Poppins stylesheet is asked for. The viewport line is what makes the phone
layouts in ADR: The phone header possible at all.

```live path=index.html region=*
```

### main.tsx: the lines that start React

`main.tsx` reads like `Program.cs`: one line per part, in order, with the file that holds the
details named beside it. It loads the stylesheets, keeps the operator's key before the address bar
is tidied, starts reporting the crashes an error boundary never sees, and draws the site.

```live path=src/main.tsx region=*
```

The stylesheets load in one fixed order, from `styles/globalStyles.ts`: the font first, then the
four design token files, so every variable exists before a component's styles use it, then
`base.css`, the page's defaults, and the two shared looks.

```live path=src/styles/globalStyles.ts region=load-order
```

`app/mount.tsx` draws the site. `createRoot` is the React 18 and 19 way to mount; the older
`ReactDOM.render` is gone. `StrictMode` costs nothing in production; in development it mounts,
unmounts and remounts every component once, so every effect's setup runs twice, which flushes out
effects that leak a timer or a listener. The throw on a missing `#root` turns a blank page into a
message.

```live path=src/app/mount.tsx region=mount
```

### vite.config.ts: the dev server and the proxy

`plugins: [react()]` gives the JSX transform and Fast Refresh, which swaps
an edited component into the running page without losing its state. The
proxy is the part that matters for a newcomer: the browser asks the Vite
server for `/api/...` and Vite forwards it to the .NET API on port 5210,
so the page and the API share one origin and the API needs no CORS
configuration at all. The preview server gets the same proxy or `npm run
preview` breaks. The `watch.ignored` line keeps Vite's file watcher out of
the .NET build output, where locked files crash it on Windows.

```live path=vite.config.ts region=dev-server
```

Vitest reads its settings from the same file, under `test`. The include
pattern keeps it to the unit tests, and the one CSS entry lets
`tokens.test.ts` read the palette file raw to measure its contrast.

```live path=vite.config.ts region=unit-tests
```

### Three tsconfig files, and why not one

The root `tsconfig.json` compiles nothing itself (`files: []`); it points
at two projects, and `tsc -b` builds both. Editors use the same references
to pick the right settings for whichever file is open.

```live path=tsconfig.json region=*
```

`tsconfig.app.json` is for the browser code under `src/`. Read its options
in groups. `target` and `lib` say which JavaScript and which browser APIs
the code may assume (`DOM.Iterable` is what lets a `NodeList` be spread).
`module: ESNext` with `moduleResolution: bundler` tells the compiler that
Vite, not Node, will resolve imports, which allows extensionless paths;
`resolveJsonModule` lets a JSON file be imported as typed data.
`allowImportingTsExtensions` permits `./x.ts` in an import and requires
`noEmit`, which is true anyway because Vite emits.
`verbatimModuleSyntax` forces `import type` for types, so Vite can drop
type-only imports file by file without a whole-program view.
`jsx: react-jsx` is the automatic runtime: no `import React` at the top
of every component. `strict` plus the `noUnused` pair keep dead code from
accumulating, and `noUncheckedSideEffectImports` makes a bare
`import './styles/tokens.css'` an error when the file does not exist,
which used to fail silently. `tsBuildInfoFile` keeps the incremental state
under `node_modules/.tmp`, out of the tree.

```live path=tsconfig.app.json region=*
```

`tsconfig.node.json` exists because `vite.config.ts` runs in Node during
the build, not in a browser: it gets a newer language target and no `DOM`
library, so a stray `window` in the config is a compile error.

```live path=tsconfig.node.json region=*
```

One more file belongs here. `src/vite-env.d.ts` is a single reference to
Vite's client types, and it is what teaches the compiler that
`import styles from './X.module.css'` yields an object, that `?raw` on an
import yields a string, and what `import.meta.env` holds.

### Styling: CSS modules over a token sheet

Every component that has styles has a `Name.module.css` beside it. Vite
hashes the class names, so `styles.card` in one component can never
collide with `.card` in another and nobody has to invent a naming scheme.
The colors, spacing and type all come from `src/styles/tokens.css` as
custom properties, and a test measures every text and ground pair against
WCAG AA (ADR: The palette). A CSS framework would add a dependency to
solve problems this app does not have: one palette, one font, and a
component count you can hold in your head.

### No router: the address bar is the state

Everything the visitor is looking at is in the URL: the filters and sort
as GET parameters, `?vehicle={id}` for a detail page, `?view=admin` for
the Admin tab. Two functions in `inventory.ts` translate between the
filter model and the query string, using the same parameter names the API
takes, so the address bar mirrors the request.

```live path=src/lib/inventory.ts region=url-state
```

`App.tsx` keeps the URL current with `replaceState`, so typing in a filter
does not pile up history entries. Opening a tile is different: it pushes
an entry so the browser's Back button closes the detail page, and a
deep-linked visit that has no list entry behind it swaps the URL in place
instead.

```live path=src/app/hooks/useAddressBar.ts region=url-mirror
```

```live path=src/app/hooks/useNavigation.ts region=history
```

Back and Forward re-read the whole view from the URL, which is the only
place it lives.

```live path=src/app/hooks/useAddressBar.ts region=back-forward
```

A router would add a dependency to do what these functions do, and it
would still need this logic for the parameters. The visitor's own
settings are the exception: the collapsed rail is a preference, not a
view, so it lives in `localStorage` (ADR: The sidebar).

### data.ts: one seam to the API

Every `fetch` for the inventory is in one file; the account calls, the
store list, the Admin tab, the error boundary, the document viewer and the
build stamp each read their own endpoint, because a cache and a debounce
built for the listing would be the wrong tool for a health check or a
sign-in. `fetchVehicles` keys a small
cache by the query string (five minutes, thirty entries, the oldest
evicted first), takes an `AbortSignal` so an effect's cleanup can cancel a
request the visitor has already typed past, and `forceRefresh` bypasses
the cache for the retry buttons and the status interval. Bids and the
reset clear the cache because they change what the server would answer.
Every request also carries the buyer's local midnight as `anchor_ms`,
which the auction schedule depends on; the URL bar leaves that out because
it is clock plumbing, not user state.

```live path=src/lib/data.ts region=fetch-vehicles
```

### lib, hooks, components

`src/lib` is plain TypeScript with no React import: the auction schedule,
the formatting, the filter model, the API seam. That is why the unit tests
run in milliseconds and need no browser. `src/hooks` holds React state
that more than one component needs: `useNow` is one clock at the app root
so every countdown ticks together, `useBids` is the buyer's standing,
`useMediaQuery` is the phone breakpoint. `src/components` render; each
takes props and owns a stylesheet. `App.tsx` composes them and owns the
three things that must be in one place: the fetch effect, the URL mirror,
and the selected vehicle.

### Two test runners, on purpose

Vitest runs `src/**/*.test.ts`: pure functions, no DOM, in the CI job
before the build. Playwright runs `tests/e2e` in a real Chrome against
both servers, which its config starts itself, so `npm run test:e2e` needs
nothing running beforehand. The CI job installs that Chrome with
`npx playwright install --with-deps chrome`.

```live path=playwright.config.ts region=web-servers
```

### The same build, in the container

The Dockerfile's first stage runs the same `npm ci` and `npm run build` in
Node 22 and hands the `dist` folder to the API image as `wwwroot`, where
the API serves it with the SPA fallback (ADR: Program.cs, explained). The
three tsconfig files are copied in because `tsc -b` needs them; a build
that passed locally and fails in the image usually means a file the stage
never received.

```live path=Dockerfile region=frontend-build
```

## What to change when

- **A new component:** `Name.tsx` and `Name.module.css` in
  `src/components`, colors from the tokens, props in and nothing fetched.
- **A new API call for the inventory:** one function in `data.ts`, so it
  shares the cache, the debounce and the abort signal; a call for another
  concern lives in that concern's file under `src/lib`, never in a component.
- **A new piece of view state:** if it describes what the visitor is
  looking at, put it in the URL through `filtersToSearchParams` and its
  reader; if it is the visitor's own setting, `localStorage`.
- **A new dependency:** ask whether a file in `lib` would do. The three
  runtime dependencies are three on purpose.
- **A new compiler option:** `tsconfig.app.json` for browser code,
  `tsconfig.node.json` for the config; CI runs `tsc -b` over both before
  anything is built.

## Addendum, 2026-09-28 (1.0.3.36): two files split by job

Until 1.0.3.35 two files did most of the frontend's work, and a newcomer had to read
all of either one to find the part they came for. `src/App.tsx` was 1,100 lines doing
seven jobs: it read and wrote the address bar, fetched the vehicle list, opened and
closed every view, knew who was signed in, placed bids, docked the rail, and drew the
header and the footer. `src/components/docs/DocsMenu/DocsMenu.tsx` was 1,308 lines and
held no menu at all (the rail draws the menu): a list of 106 document names typed by
hand, every document as data, what each sidebar section holds, a document's address,
and the window a document opens in.

They are two folders now. Every file in them does one job, its name says which, and
its first three lines say it again.

### src/app: the app shell

| File | What it does |
| --- | --- |
| `App.tsx` | Joins the pieces and picks which view shows. It holds no logic of its own. |
| `Shell.tsx` | The frame every page sits in: the skip link, the rail, the store bar, main, the footer. |
| `Header.tsx` | The header a phone shows: the bolt, The Yard, Reset bids, the resume, the menu button. |
| `Footer.tsx` | The version line. |
| `InventoryView.tsx` | The inventory page: the filter bar, the grid, Load more, and the loading and unreachable notices. |
| `hooks/useAddressBar.ts` | Holds the open view and keeps the address bar in step with it, both ways. The only code that writes the address bar. |
| `hooks/useNavigation.ts` | Opens and closes each view, moves focus, and says the change to a screen reader. |
| `hooks/useInventory.ts` | The vehicle list: fetched only when shown, filtered, paged, retried, with this visitor's bids on it. |
| `hooks/useListingRefresh.ts` | Asks for the list again when an auction starts or ends, never while nobody can see it. |
| `hooks/useOpenVehicle.ts` | The vehicle on screen: keeps its price current, places a bid, buys it now. |
| `hooks/useAccount.ts` | Who is signed in, asked of the API. |
| `hooks/useRail.ts` | Docked, collapsed or a drawer, and remembering it. |
| `hooks/useRunningBuild.ts` | Which build is running, asked of the API once; the rail and the footer both show it. |

A hook is named for what it gives back, so `App.tsx` reads top to bottom like a table
of contents: one line per hook with a comment saying what it holds, then the frame
with one view inside it.

```live path=src/app/App.tsx region=table-of-contents
```

### src/library: the documents the site serves

| File | What it does |
| --- | --- |
| `records.ts` | Every decision record, numbered, as data. |
| `pages.ts` | Every other document: the overviews, the Bicep file, the changelog, the Author page. |
| `documents.ts` | Joins the two into the one list every document is looked up in. |
| `sections.ts` | What each sidebar section holds. The sections' order and icons stay in `src/lib/siteMap.ts`. |
| `addresses.ts` | A document's address (`?doc=slug`) and back again. |
| `DocDialog.tsx` | The window a document opens in: it asks the API, renders, lays out the panels, and copies the link. |

The hand-typed list of names is gone. `DocKey` is worked out by TypeScript from the
two lists, so a document cannot be named in code without existing, and adding one is
one entry in `records.ts` or `pages.ts`:

```live path=src/library/documents.ts region=doc-key
```

The folder is `library`, not `docs`, because the repository root already has `docs/`
for the markdown, and two folders called docs would send a new developer to the wrong
one.

### The header every file opens with

```ts
/**
 * Does:      Holds the open view (the page, the vehicle, the Admin card, the document, the filters and sort)
 *            and keeps the address bar in step with it, both ways: the first-load reads, deep links, Back and Forward.
 * Does not:  Decide when a view opens (useNavigation.ts does), fetch the vehicle list, or draw anything.
 * Used by:   App.tsx, useNavigation.ts, useInventory.ts, useOpenVehicle.ts.
 */
```

"Does not" is there because the question a reader brings is usually "is it in here?",
and the fastest answer is the file saying where it went instead. `FileHeaderTests`
fails any file in either folder that ships without the three lines, reads "Used by"
against the files that really import it, so the line cannot go stale, and holds every
file to 300 lines except the ones it names with why: `records.ts`, which is data, and
the two stylesheets that moved whole (ADR: The rules a change has to pass lists it).

### What did not change

Nothing a visitor sees: the same pages, the same addresses, the same Back and Forward.
Every region moved with its code under the same name, so every record above that shows
one shows the same lines from its new file. Every existing test passes with only paths
changed. The lazy chunks keep their names (`AdminPanel`, `markdown`, `author`), because
each lazy import still names the same file.

Two lines read differently, because the linter reads small files more closely than it
read the large one. The rail now closes its drawer while it renders, the moment the
window crosses the docking line, instead of in an effect after the paint; and moving
focus to `<main>` finds it by its id, the same id the skip link names, instead of
through a ref handed down the tree.

Where the plan had six hooks there are seven, and one more file on each side: the list
refresh has a file of its own because the list's hook would have been 350 lines with
it, the running build has one because the rail and the footer both show it,
`InventoryView.tsx` keeps the list's markup out of `App.tsx`, and `documents.ts` is
where the two lists are joined.

### Where a change goes now

- **A new view:** its flag in `useAddressBar.ts`, its open and close in
  `useNavigation.ts`, one branch in `App.tsx`.
- **Something the whole app knows:** a hook in `src/app/hooks`, named for what it
  gives back, with the header.
- **A new document:** the markdown in `docs/`, its slug in `DocumentationCatalog.cs`, its entry
  in `records.ts` or `pages.ts`, and its row in `sections.ts`.

## Addendum, 2026-10-02: the records in three runs

`records.ts` was the one file the 300-line rule let through, because it is data. It is now the list
that joins three runs of records in number order, `src/library/decisionRecords/records001to030.ts`,
`records031to060.ts` and `records061to087.ts`, each under the line, so no file is exempt for being
data any more. A new record goes at the end of the last run. A type check in `records.ts` fails the
compile if one key appears in two runs, and `DocumentationCatalogTests` reads every run, so a run
that `records.ts` does not join fails the build too.

## Files

- [`package.json`](https://github.com/SteveStout/TheYard/blob/main/package.json): the scripts and the dependencies.
- [`index.html`](https://github.com/SteveStout/TheYard/blob/main/index.html) and [`src/main.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/main.tsx): the entry point, one line per part.
- [`src/styles/globalStyles.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/globalStyles.ts) and [`src/app/mount.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/app/mount.tsx): the stylesheets in order, and the mount.
- [`vite.config.ts`](https://github.com/SteveStout/TheYard/blob/main/vite.config.ts): the plugin, the proxy, the watcher, and the Vitest settings.
- [`tsconfig.json`](https://github.com/SteveStout/TheYard/blob/main/tsconfig.json), [`tsconfig.app.json`](https://github.com/SteveStout/TheYard/blob/main/tsconfig.app.json), [`tsconfig.node.json`](https://github.com/SteveStout/TheYard/blob/main/tsconfig.node.json), [`src/vite-env.d.ts`](https://github.com/SteveStout/TheYard/blob/main/src/vite-env.d.ts): the compiler, split by where the code runs.
- [`src/styles/colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css) and [`src/styles/colors.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.test.ts): the palette and its contrast test.
- [`src/lib/inventory.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/inventory.ts), [`src/lib/data.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/data.ts), [`src/app/hooks/useAddressBar.ts`](https://github.com/SteveStout/TheYard/blob/main/src/app/hooks/useAddressBar.ts): the URL as state and the one seam to the API.
- [`src/app/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/app/App.tsx): the table of contents.
- [`src/library/documents.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/documents.ts): the documents, joined, and the names worked out from them.
- [`api/TheYard.Tests/FileHeaderTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/FileHeaderTests.cs): the header rule.
- [`src/hooks/useNow.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useNow.ts), [`src/hooks/useBids.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useBids.ts), [`src/hooks/useMediaQuery.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useMediaQuery.ts): the shared state.
- [`playwright.config.ts`](https://github.com/SteveStout/TheYard/blob/main/playwright.config.ts) and [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the two runners and the jobs that run them.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the build stage that repeats `npm run build` in the image.
- [`api/TheYard.Api/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/LiveSamples.cs): the whole-file samples this record uses for the files that cannot carry a region marker (ADR: Live code samples, second addendum).

## Addendum, 2026-09-29 (1.0.3.42): the stylesheets follow

The split of 28 September moved two stylesheets whole and named them as allowed past the 300-line cap. They were the last two files in those folders with more than one job, and they are split now, by the same rule as the code: one job per file, the name says the job, the three-line header.

| The sheet | Before | After |
| --- | --- | --- |
| `src/library/DocDialog.module.css` | 828 lines, the window, the prose, the swatches, the reading panels and the Author page | `DocDialog.module.css` (the window), `DocProse.module.css`, `DocSwatches.module.css`, `DocPanels.module.css`, `AuthorPage.module.css` |
| `src/app/App.module.css` | 425 lines, the frame and the inventory page | `App.module.css` (the frame), `InventoryView.module.css` |

Every rule moved byte for byte; only the headers are new. Each document sheet scopes its rules under its own `.prose`, so the element that holds a document carries all of them (`DocDialog.tsx`, the `PROSE` line), and the reading panel's frost is scoped under the window as well, so the window carries that sheet's `.dialog`. The CSS modules keep the order the one sheet had, because `DocDialog.tsx` imports them in that order.

**The header on every stylesheet.** `FileHeaderTests` reads every `.css` under `src` beside the code in `src/app` and `src/library`: the header, "Used by" against the files that really import each sheet, and the 300-line cap. Forty sheets under `src/components` and `src/styles` got the header in this version. The two "moved whole" allowances are gone. Four component sheets are over the line (`Landing`, `SideNav`, `AdminPanel`, `VehicleDetail`); each holds one component's look and is named in the test with that job and the lane that will split it with its component. A live fence may quote a file's header with `region=header`, since the header comes before anything a marker could wrap.

```live path=api/TheYard.Tests/FileHeaderTests.cs region=the-header-rule
```

The next lane is the components that have the disease the split cured in `src/app`: `AdminPanel.tsx`, `ActivityCard.tsx` and `SideNav.tsx`, and their sheets with them.

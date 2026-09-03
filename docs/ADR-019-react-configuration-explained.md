# ADR: The React configuration, explained for a new developer

Status: accepted, 2026-09-02, shipped as 1.0.0.24. Written at Steve's
request for a developer new to React and Vite, or new to this codebase,
who opens the root of the repository and wants to know what every
configuration file is for and why the source is laid out the way it is.

## Context

The root of the repository holds a package.json with eleven scripts and
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

`tokens.css` is imported before anything else so the palette's custom
properties exist before the first component's styles are applied (ADR:
The palette). `createRoot` is the React 18 and 19 way to mount; the older
`ReactDOM.render` is gone. `StrictMode` costs nothing in production; in
development it mounts, unmounts and remounts every component once, so
every effect's setup runs twice, which flushes out effects that leak a
timer or a listener. The throw on a missing `#root` turns a blank page into a message.

```live path=src/main.tsx region=*
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

```live path=src/App.tsx region=url-mirror
```

```live path=src/App.tsx region=history
```

Back and Forward re-read the whole view from the URL, which is the only
place it lives.

```live path=src/App.tsx region=back-forward
```

A router would add a dependency to do what these functions do, and it
would still need this logic for the parameters. The visitor's own
settings are the exception: the collapsed rail is a preference, not a
view, so it lives in `localStorage` (ADR: The sidebar).

### data.ts: one seam to the API

Every `fetch` in the app is in one file. `fetchVehicles` keys a small
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
- **A new API call:** one function in `data.ts`; nothing else in the app
  calls `fetch`.
- **A new piece of view state:** if it describes what the visitor is
  looking at, put it in the URL through `filtersToSearchParams` and its
  reader; if it is the visitor's own setting, `localStorage`.
- **A new dependency:** ask whether a file in `lib` would do. The three
  runtime dependencies are three on purpose.
- **A new compiler option:** `tsconfig.app.json` for browser code,
  `tsconfig.node.json` for the config; CI runs `tsc -b` over both before
  anything is built.

## Files

- [`package.json`](https://github.com/SteveStout/TheYard/blob/main/package.json): the scripts and the dependencies.
- [`index.html`](https://github.com/SteveStout/TheYard/blob/main/index.html) and [`src/main.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/main.tsx): the entry point and the mount.
- [`vite.config.ts`](https://github.com/SteveStout/TheYard/blob/main/vite.config.ts): the plugin, the proxy, the watcher, and the Vitest settings.
- [`tsconfig.json`](https://github.com/SteveStout/TheYard/blob/main/tsconfig.json), [`tsconfig.app.json`](https://github.com/SteveStout/TheYard/blob/main/tsconfig.app.json), [`tsconfig.node.json`](https://github.com/SteveStout/TheYard/blob/main/tsconfig.node.json), [`src/vite-env.d.ts`](https://github.com/SteveStout/TheYard/blob/main/src/vite-env.d.ts): the compiler, split by where the code runs.
- [`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css) and [`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts): the palette and its contrast test.
- [`src/lib/inventory.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/inventory.ts), [`src/lib/data.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/data.ts), [`src/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/App.tsx): the URL as state and the one seam to the API.
- [`src/hooks/useNow.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useNow.ts), [`src/hooks/useBids.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useBids.ts), [`src/hooks/useMediaQuery.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useMediaQuery.ts): the shared state.
- [`playwright.config.ts`](https://github.com/SteveStout/TheYard/blob/main/playwright.config.ts) and [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the two runners and the jobs that run them.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the build stage that repeats `npm run build` in the image.
- [`api/TheBlock.Api/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/LiveSamples.cs): the whole-file samples this record uses for the files that cannot carry a region marker (ADR: Live code samples, second addendum).

# ADR: One folder per component

Status: accepted, 2026-09-28. Steve's plan for the frontend's layout, in his words: "one folder per component", grouped by section, "an index.ts that re-exports it, so imports read components/<section>/<Name>", and "src/lib and src/hooks do not move". Shipped as 1.0.3.35 with the Admin stylesheet split, the edge warm-up, the prefetch, the first activity read and the keep-warm loop.

## Context

By 1.0.3.34 `src/components` held 49 files in one flat folder, and the Admin tab's twenty-four files in a second flat folder under it. A component and its stylesheet sat next to each other only because their names sorted together, the badges a vehicle card draws sat beside the Admin workbench, and a reader looking for the inventory had to know its file names to find them. The Admin tab's one stylesheet, 1,766 lines and 164 classes, had no folder its classes could move into.

## Decision

**Every component has a folder of its own, and the folders are grouped by the section of the site that draws them.**

- A folder holds the component's `.tsx`, its `.module.css` when it has one, and an `index.ts` that re-exports it. An import names the folder, `components/<section>/<Name>`, never the file inside it. A file imports its own folder's neighbours directly (`./Ribbons`), never through its own index.
- The sections are `layout` (the rail, the store band, the brand, the intro strip, and the page's ground as `Background`), `inventory`, `vehicle`, `account`, `docs`, `landing`, `shared`, and `admin`, which holds the workbench (`admin/AdminPanel`), the shared card helpers (`admin/shared`), the charts (`admin/charts`), the table (`admin/DataTable`) and a folder per card.
- **A component another section renders goes to `shared`.** The rule is read off who imports a file, not off what the file is about.
- `src/lib` and `src/hooks` do not move, and `src/lib` still imports nothing from React.
- The lazy imports stay lazy: the Admin tab from `App.tsx`, each card from the workbench, and the markdown renderer and the Author layout from the document dialog are each still a chunk of their own, since a dynamic import of a folder's `index.ts` splits exactly where a dynamic import of the file did.

## Where the rule moved a file off the first plan

The plan put the vehicle's badges in `vehicle` and the sheet icons in `layout`. Read by who imports them:

- `AuctionCountdown`, `ConditionBadge`, `ReserveBadge`, `TitleStatusBadge` and `VehicleImage` are drawn by the inventory's `VehicleCard` and by the vehicle's `VehicleDetail`, two sections, so they are in `shared`.
- `SheetIcons` is drawn by the rail (`layout`) and by the landing page (`landing`), so it is in `shared`.
- `DocsMenu` stays in `docs` although the rail opens its dialog and the landing page reads its menus. It is that section's own front door: the catalogue, the menus and the dialog are the docs section, and a section may open another section's front door through its `index.ts`. What `shared` is for is the small pieces two sections both draw, and moving the whole docs section there would leave `docs` empty.
- `CountUp` and `ErrorBoundary` each have one user (the landing page, and `main.tsx`), and stay in `shared` as the plan put them: nothing in the rule sends a one-user component into its user's section, and both are general pieces rather than the landing page's own.

## What held it

The move changed no value, selector, class name or behaviour; `App.tsx` draws `<Background />` where it drew the ribbons and then the watermark, and `Background` is a fragment of those two in that order, so the page's markup is the same. The tests that read components as text read every folder: `icons.test.ts` globs `components/**`, the unit-test build reads any component sheet as text wherever it sits (`vite.config.ts`), and `StyleRulesTests` reads the Admin tab's files recursively. Every link, live sample and test that named a file by its path names the new one.

## Addendum, 2026-09-28 (1.0.3.36): DocsMenu leaves `docs`, and the shell leaves `src`

The same day, the two largest files were split by job (ADR: The React configuration, explained, the addendum on the split). `DocsMenu` was not a component: most of it was data, the documents and what each section holds, with the document window at the end. It is now `src/library`, a folder beside `components` rather than a section in it, and `src/components/docs` is gone; the sections are `layout`, `inventory`, `vehicle`, `account`, `landing`, `shared` and `admin`. `App.tsx` and its stylesheet moved the same way, to `src/app`, with the hooks that hold what the app knows under `src/app/hooks`. Neither folder is a component folder, so neither has an `index.ts`: an import names the file, `library/sections` or `app/hooks/useAddressBar`, and every file in both opens with a header saying what it does, what it does not, and which files use it. The rule above is unchanged for everything under `src/components`, and `icons.test.ts` and the unit-test build now read `src/app` and `src/library` too.

## Files

- [`src/components`](https://github.com/SteveStout/TheYard/tree/main/src/components): the sections and their folders.
- [`src/components/layout/Background/Background.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/Background/Background.tsx): the page's ground, the ribbons and then the watermark.
- [`src/lib/icons.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/icons.test.ts): every component and every component sheet, read from every folder.
- [`vite.config.ts`](https://github.com/SteveStout/TheYard/blob/main/vite.config.ts): the unit-test build's list of sheets read as text.
- [`CLAUDE.md`](https://github.com/SteveStout/TheYard/blob/main/CLAUDE.md): the folder rule, in the file an agent reads first.

# ADR: The stack has a page of its own

Status: accepted, 2026-11-19, at 1.0.5.121. The Tech stack table leaves the home page for a page of its own, `/?view=stack`; the home page keeps the table's header band and its four chips with one link to it.

## Reading list

1. [ADR: The landing page and the site map](https://theyard.stevenstout.biz/?doc=adr-landing-page): the home page's order, whose addendum of the same day this record serves.
2. [ADR: A hook on every page](https://theyard.stevenstout.biz/?doc=adr-a-hook-on-every-page): the first box the stack page opens with.

## In plain words

The home page carried every row of the stack, what the site runs on, where, at which version and the file that proves it. It is the most checkable table on the site and the longest thing on the home page: on a phone it was most of the scroll between the first screen and the way out. A hiring manager who wants it can open it; one who does not should not have to scroll past it.

## Decision

1. **A view of its own.** `/?view=stack` draws a hook whose one line is the site map's own line for the page ("What it runs on, with the file that proves each line."), the whole Tech stack panel as the home page drew it, and Where next. It is a view like the recommendations page: a `stack` section in the site map's union (`src/lib/siteMapTypes.ts`), one entry under "How it is built" after "Built with AI", and a `MENUS.stack` section whose one link is the page, so the sidebar, the home page's tiles and the address bar read one list (ADR-082).
2. **The home page keeps the header.** Under "Tech stack" the home page draws the gate's three figures, then `TechStackSummary`: the band, its four chips and "The whole stack, with the file that proves each line". No element of the table is in the home page's markup.
3. **The table is unchanged.** It is still generated from the repository (`src/lib/techStack.ts`), and still fails CI when it drifts.

## Consequences

- The home page is about half as tall (ADR-082, the addendum on the home page's order).
- What stops being true: that the home page shows every row of the stack.
- What holds it: `siteMap.test.ts` (the entry and its place), `render.test.ts` (no table in the home page's first load, the link to the page) and `landing.spec.ts` (the summary on home, the whole table at `/?view=stack`, every row with a version and a link to its file).

## Where it sits

The page is a view of the React app, beside the recommendations page: its flag in the address bar's hook (`src/app/hooks/useAddressBar.ts`) and the first address (`src/app/firstAddress.ts`), its component in the landing's own folder, where the table already lived. Nothing on the server changed: the table is the build's.

## Files

- [`src/components/landing/StackPage/StackPage.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/landing/StackPage/StackPage.tsx) - the page: the hook, the whole table and Where next
- [`src/components/landing/TechStack/TechStack.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/landing/TechStack/TechStack.tsx) - the panel whole, and `TechStackSummary`, its header and the way to the page
- [`src/lib/siteMapEntries.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/siteMapEntries.ts) and [`src/library/sections.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/sections.ts) - the entry and its one link
- [`src/app/hooks/useAddressBar.ts`](https://github.com/SteveStout/TheYard/blob/main/src/app/hooks/useAddressBar.ts) and [`src/app/firstAddress.ts`](https://github.com/SteveStout/TheYard/blob/main/src/app/firstAddress.ts) - `?view=stack` opened and kept in the address bar

# ADR: The landing page and the site map

Status: accepted, 2026-09-22. Asked for over one morning: a landing page at the site's address with an icon for each entry in the navigation, the Author section at the top beside Inventory in larger buttons, and, on how it is built, one data structure in React that feeds both the navigation and the landing page so it is easier to configure. Shipped as 1.0.1.0, the release Steve called the first polished one.

## In plain words

This page decides that the site's address opens a landing page with a tile for each section. The sidebar and those tiles are drawn from one shared list (the site map), so they cannot drift apart, and every older link that meant the inventory still opens it.

What that is worth: a developer adds or renames a section in one file and both places follow, held by a test that reads the same map. The organization's visitors see everything the site holds on the first screen instead of hunting for a sidebar.

## Context

Until 1.0.0.187 the address opened the inventory. A reviewer who follows a link from LinkedIn lands on a grid of cars and has to find the sidebar to learn that the site also carries its architecture and its decision records. Its pipeline and its author are there too. The sidebar held that map, and it was the only place that did.

A landing page that lists the same sections is a second list, and a second list drifts: the first section added to the sidebar and not to the landing page makes the landing page wrong, and nothing would notice.

## What was considered

| Option | What it buys | What it costs |
| --- | --- | --- |
| A landing page with its own list of tiles | Quick to write, free to order however reads best | Two lists that must be kept in step by hand, which is the drift this project writes tests against everywhere else |
| Keep the inventory as the front door and add a banner | No change to any address | The map stays hidden in the rail, and on a phone behind the hamburger |
| **One site map that both the sidebar and the landing page are drawn from** | A section added, removed, renamed or reordered changes both places at once, and a test can read the map | The landing page's order is the sidebar's order, not an order chosen for the landing page alone |

**Decision: `src/lib/siteMap.ts` holds the shape of the site, and the landing page is what the address opens.** `SITE_MAP.sections` is the sidebar's section order and the landing grid's order, each with an icon, one line and whether it is shown large; `SITE_MAP.actions` holds Inventory, Sign in, Admin, the resume and the repository, each saying whether it is a sidebar row and whether it is a landing tile. What a section contains stays in the menus in `DocsMenu.tsx` (since the UI split, `src/library/sections.ts`). The file imports no React, so `siteMap.test.ts` holds it on its own, and `landing.spec.ts` reads its expected tiles off the same map.

## Old links keep working

Every address shared before 1.0.1.0 opened the inventory, so the rule is that any address that already meant the inventory still opens it: `?view=inventory`, any filter or sort, and `?vehicle=`. `opensInventory()` in `src/lib/inventory.ts` is that rule in one function, tested in `inventory.test.ts`. A bare address opens the landing page and the brand goes home to it; Back returns from it.

## What is not done, and is said here

- The rail's pinned block is one row taller with Inventory in it, so on a screen 900 px tall the section list scrolls sooner. The list already scrolled on shorter screens, and Author is the large tile on the landing page.
- Admin's "Back to inventory" goes back in history, which is the landing page when Admin was opened from it. The label is Admin's own and is left for a later change.

## Addendum, 2026-09-22 (1.0.1.1): a Home row

Steve asked for the landing page to be on the navigation too. The site map's first action is Home: a sidebar row and not a tile. It opens the landing page and is the current row while it shows, so the rail says where the reader is on the landing page as it does everywhere else. The brand still goes home too. He chose the label Home over Dashboard, which could read as the Admin tab's figures.

## Addendum, 2026-09-22 (1.0.1.4): three groups, two photographs and one live reading

Steve answered eight questions on the landing page that morning, and four of them change this record.

- **The sections are grouped, in both places, from the one map.** Each section in `SITE_MAP` names its group, and `SITE_GROUPS` holds the three headings in order: How it is built (App Architecture, API Reference, SQL vs Cosmos DB, Diagrams, Style, Built with AI), How it is run (Performance, Hosting, CI/CD, Best Practices), and Who and why (Decision Records, Changelog, About, and Author, which the landing page still shows large at the top). The map lists a group's sections together, so `MENU_ORDER` is still the one order, and the sidebar and the landing page draw each heading above its group's first section. Sign in, Admin and GitHub stay last on the landing page, and the sidebar's pinned rows are unchanged. `siteMap.test.ts` holds the order he gave and that no group is split.
- **The two large tiles wear a photograph in the badge.** The Author tile shows the vineyard selfie of Steve and Katie (option A of his mock), and the Inventory tile one of the inventory's own photographs, the yellow Nissan Fairlady Z (option B, "maybe a car for the inventory?"), each a square crop in the same 88 px round gold ring, cut on the machine and served from `/api/images/badges/`. The car's photographer and licence are its line in `credits.json`, carried on the picture as its title.
- **One live reading, on the Admin tile only.** The landing page reads `/api/health` once when it opens and puts a dot and a word under the Admin tile: Healthy is green, Degraded amber, and no answer red. Before this the tile was drawn green whatever the site said, which was a claim the page had not checked. He said no to an auction count on Inventory and no to a live line under the title.
- **What it costs:** the grid is no longer four even rows of four on a desk; the first group is six tiles: a row and a half. The groups are the order he asked for, and a group is easier to scan than an even grid.

## Addendum, 2026-09-22 (1.0.2.0): the reviewer's first minute

The landing page was drawn for a reader who explores. The reader it actually gets is a reviewer with a minute, and three things were in their way.

- **The resume was a sidebar row.** The one file that reader came for opened from the rail, which on a phone is behind the menu button. It is a large tile now. After Inventory and Author it is third across the top, and it still opens the PDF this site serves. Large tiles take an explicit `featuredRank`, because their order is a judgement about a reader and not the map's own order.
- **The Author tile said that he exists, not what he is.** "Steven Stout, who built it." It now carries his resume's own opening line: a staff-level .NET engineer who owns platform architecture end to end, twelve years full stack and seven fully remote. The words are the resume's so the two cannot say different things.
- **The site's argument was behind the tiles.** Every version runs three suites in one gate and rolls only if they are green, and the only way to learn that was to open Built with AI or CI/CD. The evidence strip under the title now says it in four figures: the tests the gate ran, how long the gate took, how many decision records the site holds, and that one codebase serves two stores. The counts come from `/api/tests/summary`, the gate's own results file added up, and the record count is the site map's own list, so neither can drift from what the site actually has. A test is counted once rather than once per store, a suite the gate carried forward from an earlier version says so, and a red gate is reported as failures rather than drawn as green.

## Addendum, 2026-10-04: the first screen says only what is true

A cold read of the landing page found four lines a reviewer could catch out. The Hosting tile named
Front Door, which the Hosting page says is not deployed; it now reads "Azure, a free edge in front,
and the web app behind it." The Author tile, the About page and the Author document gave three
levels; all three now give the resume's title line, Lead / Staff .NET engineer. The tests tile counts
each suite once, so it says so ("test runs in the gate, each suite once"), and the gate tile names
its target beside the time ("one gate, target 300 s"), in the label, so the detail under the number
keeps to two lines on a phone and every number in the row stays on one baseline. The Author badge is the vineyard photograph
cropped to Steve alone, so a reader knows which person is the author. The cropped badge is served under a new name (`steve-176`, `steve-264`), because the edge and the browser keep an image for a day by its address and the old name went on showing the couple.

## Where it sits

The site map lives in the front end: src/lib/siteMap.ts and its entries import nothing from React, Landing and SideNav both draw from them, and the host Api adds only TestSummary.cs for the evidence strip's counts. It follows the open/closed principle, adding behaviour without editing core code, in data form: a new section is one more entry in siteMapEntries.ts, and neither the sidebar nor the landing page needs a change. The cost is that the landing grid's order is the sidebar's order, softened only by featuredRank on the large tiles. A landing page that needed its own grouping or copy apart from the sidebar would justify a second list and a test holding the two together.

## Files

- [`src/lib/siteMap.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/siteMap.ts): the one structure, its groups and the two badge photographs.
- [`src/lib/landingHealth.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/landingHealth.ts): the Admin tile's health dot, as a word and a tone.
- [`src/lib/landingProof.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/landingProof.ts): the evidence strip's four figures, from the gate's own counts.
- [`api/TheYard.Api/TestSummary.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/TestSummary.cs): those counts, read out of the results file the gate wrote.
- [`src/components/landing/Landing/Landing.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/landing/Landing/Landing.tsx): the landing page drawn from it.
- [`src/components/layout/SideNav/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.tsx): the sidebar's pinned rows drawn from it.
- [`src/lib/inventory.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/inventory.ts): `opensInventory()`, which keeps every older address on the inventory.
- [`tests/e2e/landing.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/landing.spec.ts): the landing page against the map.

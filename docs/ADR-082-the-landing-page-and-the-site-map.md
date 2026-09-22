# ADR: The landing page and the site map

Status: accepted, 2026-09-22. Asked for over one morning: "I want a landing page for when you hit our URL ... it should have icons for each of our navigation bar", "I want the author section at the Top next to Inventory in bigger buttons", and on how it is built: "one data structure in react that feeds the navigation and the dashboard so this is more configurable". Shipped as 1.0.1.0, the release Steve called the first polished one.

## Context

Until 1.0.0.187 the address opened the inventory. A recruiter or a hiring manager who follows a link from LinkedIn lands on a grid of cars and has to find the sidebar to learn that the site also carries its architecture, its decision records, its pipeline and its author. The sidebar held that map, and it was the only place that did.

A landing page that lists the same sections is a second list, and a second list drifts: the first section added to the sidebar and not to the landing page makes the landing page wrong, and nothing would notice.

## What was considered

| Option | What it buys | What it costs |
| --- | --- | --- |
| A landing page with its own list of tiles | Quick to write, free to order however reads best | Two lists that must be kept in step by hand, which is the drift this project writes tests against everywhere else |
| Keep the inventory as the front door and add a banner | No change to any address | The map stays hidden in the rail, and on a phone behind the hamburger |
| **One site map that both the sidebar and the landing page are drawn from** | A section added, removed, renamed or reordered changes both places at once, and a test can read the map | The landing page's order is the sidebar's order, not an order chosen for the landing page alone |

**Decision: `src/lib/siteMap.ts` holds the shape of the site, and the landing page is what the address opens.** `SITE_MAP.sections` is the sidebar's section order and the landing grid's order, each with an icon, one line and whether it is shown large; `SITE_MAP.actions` holds Inventory, Sign in, Admin, the resume and the repository, each saying whether it is a sidebar row and whether it is a landing tile. What a section contains stays in the menus in `DocsMenu.tsx`. The file imports no React, so `siteMap.test.ts` holds it on its own, and `landing.spec.ts` reads its expected tiles off the same map.

## Old links keep working

Every address shared before 1.0.1.0 opened the inventory, so the rule is that any address that already meant the inventory still opens it: `?view=inventory`, any filter or sort, and `?vehicle=`. `opensInventory()` in `src/lib/inventory.ts` is that rule in one function, tested in `inventory.test.ts`. A bare address opens the landing page, the brand goes home to it, and Back returns from it.

## What is not done, and is said here

- The rail's pinned block is one row taller with Inventory in it, so on a screen 900 px tall the section list scrolls sooner. The list already scrolled on shorter screens, and Author is the large tile on the landing page.
- Admin's "Back to inventory" goes back in history, which is the landing page when Admin was opened from it. The label is Admin's own and is left for a later change.

## Addendum, 2026-09-22 (1.0.1.1): a Home row

Steve: "you also need the dashboard on the navigation". The site map's first action is Home, a sidebar row and not a tile, which opens the landing page and is the current row while it shows, so the rail says where the reader is on the landing page as it does everywhere else. The brand still goes home too. He chose the label Home over Dashboard, which could read as the Admin tab's figures.

## Addendum, 2026-09-22 (1.0.1.4): three groups, two photographs and one live reading

Steve answered eight questions on the landing page that morning, and four of them change this record.

- **The sections are grouped, in both places, from the one map.** Each section in `SITE_MAP` names its group, and `SITE_GROUPS` holds the three headings in order: How it is built (App Architecture, API Reference, SQL vs Cosmos DB, Diagrams, Style, Built with AI), How it is run (Performance, Hosting, CI/CD, Best Practices), and Who and why (Decision Records, Changelog, About, and Author, which the landing page still shows large at the top). The map lists a group's sections together, so `MENU_ORDER` is still the one order, and the sidebar and the landing page draw each heading above its group's first section. Sign in, Admin and GitHub stay last on the landing page, and the sidebar's pinned rows are unchanged. `siteMap.test.ts` holds the order he gave and that no group is split.
- **The two large tiles wear a photograph in the badge.** The Author tile shows the vineyard selfie of Steve and Katie (option A of his mock), and the Inventory tile one of the inventory's own photographs, the yellow Nissan Fairlady Z (option B, "maybe a car for the inventory?"), each a square crop in the same 88 px round gold ring, cut on the machine and served from `/api/images/badges/`. The car's photographer and licence are its line in `credits.json`, carried on the picture as its title.
- **One live reading, on the Admin tile only.** The landing page reads `/api/health` once when it opens and puts a dot and a word under the Admin tile: Healthy is green, Degraded amber, and no answer red. Before this the tile was drawn green whatever the site said, which was a claim the page had not checked. He said no to an auction count on Inventory and no to a live line under the title.
- **What it costs:** the grid is no longer four even rows of four on a desk; the first group is six tiles, a row and a half. The groups are the order he asked for, and a group is easier to scan than an even grid.

## Files

- [`src/lib/siteMap.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/siteMap.ts): the one structure, its groups and the two badge photographs.
- [`src/lib/landingHealth.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/landingHealth.ts): the Admin tile's health dot, as a word and a tone.
- [`src/components/Landing.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/Landing.tsx): the landing page drawn from it.
- [`src/components/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.tsx): the sidebar's pinned rows drawn from it.
- [`src/lib/inventory.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/inventory.ts): `opensInventory()`, which keeps every older address on the inventory.
- [`tests/e2e/landing.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/landing.spec.ts): the landing page against the map.

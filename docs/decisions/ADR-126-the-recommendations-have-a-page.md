# ADR: The recommendations have a page

Status: accepted, 2026-11-07, at 1.0.4.153. The recommendations on Steven's LinkedIn profile get a page of their own at `/?view=recommendations`, with a short door at `/recommendations` that keeps a tagged link's code, drawn whole by the rendering service from `/api/recommendations` and grouped by where each writer stood.

## Reading list

1. [ADR: The sidebar](https://theyard.stevenstout.biz/?doc=adr-sidebar): the one file the words live in, and the addendum of 1.0.4.152 that makes the count, the years and the groups worked out rather than stated.
2. [ADR: Ten minutes, seven stops](https://theyard.stevenstout.biz/?doc=adr-ten-minutes-seven-stops): the review route, whose view, short door and tagged open this page copies.
3. [ADR: Tagged links, who opened what](https://theyard.stevenstout.biz/?doc=adr-tagged-links-who-opened-what): why a page reports its own open under its own path.
4. [ADR: The landing page and the site map](https://theyard.stevenstout.biz/?doc=adr-landing-page): the one map the sidebar row and the landing tile come from.

## In plain words

Thirteen people who worked with Steven wrote a recommendation on LinkedIn. Until now they stood in a column on the landing page and on About Steven, where a reader scrolling past might read a line or two. Now they have a page that holds every word, says how each writer knew him, and links each name to the writer's own profile, so a reader can check any of them in one click.

What that is worth: a hiring manager who doubts what a candidate says about himself can read what thirteen other people said, grouped by whether they reported to him, sat beside him, worked across the building or came from another company, and can check each one where it was written. It is the one proof on the site a reader cannot put down to self-report.

## Context

On 7 November Steve asked whether there should be a recommendations page, saw it mocked up, and asked for it built from one data source so a new person moves every number on their own. The site's own numbers that morning said the people who matter open the resume and stop: 104 people opened it in seven days and 2 went into the app. The recommendations were on the site but nowhere a link could point to.

## Decision

1. **A view, at `/?view=recommendations`.** It is one of the pages that cover the rest of the app, beside the blog and the review route: the address bar holds which of the three is open, and one close shuts all three.
2. **A short door, `/recommendations`.** It answers 301 to the view with the query kept, the same handler as `/review`, so a tagged `/recommendations?r=<code>` reaches the page that reports it. The page reports its open under `/recommendations` (the Links slice's page views), and the door itself is not an open, so a visit counts once.
3. **Drawn whole, from the one read.** The rendering service reads `/api/recommendations` for this address and draws the page from it, every name and the count in the title, so a crawler and a link preview see the people and not a placeholder. The browser starts from the same answer and reads it again.
4. **Grouped as the API groups.** The page draws the groups the API answers, in their order, and works nothing out: the title's count, the three tiles (how many, the years, the vantage points) and every group come off the wire. Each card has the writer's name linking to their profile, their title, the relationship line and the month, the full text against a gold rule, and two pills: the recommendation on LinkedIn and the writer's profile. An index of every name sits beside the groups at a desk's width and above them on a phone.
5. **A row and a tile.** The sidebar's Who and why section gains Recommendations after Author, as a section whose one row is a link to the view; the landing page's site map draws its tile from the same entry.

What was considered: a third list on the landing page (a longer page, and still nothing a link can point to), and an eighth stop on the review route (the route stays seven stops; its last stop links here in the next version).

## What holds it

`recommendationsPage.test.ts` holds the anchors, the sections and the tiles; `render/recommendations.test.ts` the page drawn whole from its one read with the count from the file; `recommendations.spec.ts` the page at 1280 and 390 against the API's own answer, its tagged open and its short door; `RecommendationsOpenTests` the open counted once; `siteMap.test.ts` the row and the tile.

## Where it sits

A page component under `src/components/recommendations/`, a pure lib beside the others in `src/lib`, one more flag in the address bar, one more read in the rendering service, and one more route in the Docs slice beside `/review`.

## Files

- [`src/components/recommendations/RecommendationsPage/`](https://github.com/SteveStout/TheYard/tree/main/src/components/recommendations/RecommendationsPage) - the page
- [`src/lib/recommendationsPage.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/recommendationsPage.ts) - the sections, the anchors and the tiles
- [`src/app/pageOpen.ts`](https://github.com/SteveStout/TheYard/blob/main/src/app/pageOpen.ts) - which page is open
- [`render/loaders.ts`](https://github.com/SteveStout/TheYard/blob/main/render/loaders.ts) - the one read
- [`api/TheYard.Api/Features/Docs/DocumentationEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Docs/DocumentationEndpoints.cs) - the short door

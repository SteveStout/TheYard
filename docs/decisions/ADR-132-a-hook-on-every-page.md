# ADR: A hook on every page

Status: accepted, 2026-11-12, at 1.0.5.47. Every page a reader is sent to opens with one box of evidence: the landing, About, a blog post, the resume door, the Quality page and the review route. The words for each live in one data file, `data/hooks.json`, which the React build and the API both read, every line copied word for word from its source, and each box is drawn on the server with no read above the fold.

## Reading list

1. [ADR: One reading for the first screen](https://theyard.stevenstout.biz/?doc=adr-one-reading-for-the-first-screen): the live readouts the landing's hook sits directly above.
2. [ADR: The resume has an address](https://theyard.stevenstout.biz/?doc=adr-the-resume-has-an-address): the door whose hook this adds.
3. [ADR: Ten minutes, seven stops](https://theyard.stevenstout.biz/?doc=adr-ten-minutes-seven-stops): the review route, whose stop 1 now starts on a phone's first screen.
4. [ADR: Quality you can watch](https://theyard.stevenstout.biz/?doc=adr-quality-you-can-watch): the page whose first chart and sentence the landing now opens with.

## In plain words

A reader who opens any page the site sends people to sees one piece of proof first, before the frame and the navigation have had their say: on the landing, the sentence about the quality ratchet with its chart; on About, the two lines from the resume about the releases and being the sole engineer; on a blog post, the post's own title, summary and first picture; on the resume door, the quality sentence and the recommendations. On a phone every one of them is on the first screen, with no scroll.

What that is worth: the first screen of every page says something that can be checked, and says it in words already published elsewhere, so nothing on it is a claim made up for the page.

## Context

Steve, 10 November: "if we get a manager to open it, every page must be fast and hook them with high value work". On a phone the header and the store switch take the top 195 pixels of every app page, and measured that night at 390 by 844 the landing's live readouts started 415 pixels down under a welcome card, a post's title started about 520 pixels down, and the review route's stop 1 sat below the fold.

Two of the pages are drawn by the API in C# (`/about` and the resume door) and four by React through the rendering service, so a hook typed in each place would be six copies of words that must match their sources. The sources are fixed: the quality sentence is Steve's own, approved word for word; the About lines are the resume of record's own bullets; the resume itself never says zero downtime, so no hook does. On the landing the owner chose the hook in the welcome card's place with the live readouts kept directly below it (option A of the ladder's third question, 11 November).

## Decision

1. **One file, one entry a page.** `data/hooks.json` holds each page's lines, the source they were copied from, the chart the landing draws and one link. A count is never typed in it: the resume door's link names `{recommendations}`, which the API fills from `data/recommendations.json` when it draws the page, and leaves the link out when that file cannot be read.
2. **One shared box.** `components/shared/Hook` draws it for React: a title when the page has one, the words, one figure and one link. The landing's sentence is the page's heading; the chart under it is the Quality page's first chart, built from `data/quality-history.json`, which the build carries, so the landing draws it on the server with no read. The chart moved from components/quality to components/shared, because a second section now draws it. The API draws the same box in its own markup (`PageHooks`), first on `/about` under the name and title, and first on the resume door above the band.
3. **A post's hook comes from the post.** The rendering service already reads the published posts for a post's link preview; the post an address names goes into the first load with its title, summary and first image, so the hook is drawn on the server and the browser asks for nothing to show it.
4. **Quality and the review route keep their own first content.** The Quality page already opens with its paragraph and its first chart; the hook is that. On a phone the review route's person panel now folds below the stops, so stop 1's heading and what it shows start on the first screen.
5. **No hook opens Admin.** Every address in the file is held away from any `view=admin` address and Admin's reads, on both sides.
6. **The landing's sentence is held to the ratchet.** It says "Fifteen", and the ratchet file holds fifteen numbers; a sixteenth turns a test red rather than leaving the sentence stale. The sentence itself is never reworded to fit.

## What holds it

`pageHooks.test.ts`: one entry a page, the copy rules (no em dash, never the two job words, no downtime claim, a number in each page's words), the count placeholder, the landing chart's readings from the history, and no Admin address. `PageHooksTests`: /about's two lines and the door's sentence and counted link come from the file, the sentence's first word is the ratchet's count, the landing chart's label, direction and number are the Quality page's first fence, and the file names no Admin address. `render.test.ts`: a post address draws its hook from the posts read for its head. `hooks.spec.ts`: at 390 by 844 each hook's box sits inside the first screen on `/`, `/about`, `/resume`, a post and `/?doc=quality`, the readouts sit directly under the landing's, and stop 1 starts on the review route's first screen.

## Consequences

- The landing's welcome card is gone and the page's heading is now the quality sentence; the card's sentence on who built the site and what it is stays on the landing, directly under the readouts.
- The landing's bundle carries the quality history, about five kilobytes compressed, so the chart costs no read.
- A post's title shows twice on its page, in the hook and on the post's card, because the card is the post as LinkedIn readers know it.
- On a phone the review route's person panel and its links move from under the lede to after the finish panel.

## Where it sits

Outside the rings for the words (data/ beside the recommendations file), and in the presentation for the rest: the box in the shared components with its arithmetic in src/lib, the post's hook in the rendering service's first load, and the two C# pages' box in the host's Docs slice beside the recommendations it counts. Nothing in Domain or Application knows a hook exists. One file read by two processes is the cost of drawing two pages in C#; what would change it is those two pages moving to the rendering service, when the file would have one reader.

## Files

- [`data/hooks.json`](https://github.com/SteveStout/TheYard/blob/main/data/hooks.json) - one entry a page
- [`src/lib/pageHooks.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/pageHooks.ts) - the entries, the landing chart, counts and the Admin check
- [`src/components/shared/Hook/Hook.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/Hook/Hook.tsx) - the box
- [`src/components/shared/QualityTrend/`](https://github.com/SteveStout/TheYard/tree/main/src/components/shared/QualityTrend) - the chart, now drawn by the Quality page and the landing
- [`api/TheYard.Api/Features/Docs/PageHooks.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Docs/PageHooks.cs) - the box on /about and the resume door
- [`render/render.ts`](https://github.com/SteveStout/TheYard/blob/main/render/render.ts) - a post's hook in the first load
- [`api/TheYard.Tests/PageHooksTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PageHooksTests.cs), [`src/lib/pageHooks.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/pageHooks.test.ts) and [`tests/e2e/hooks.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/hooks.spec.ts) - both sides held to the one file, and every hook on the first screen

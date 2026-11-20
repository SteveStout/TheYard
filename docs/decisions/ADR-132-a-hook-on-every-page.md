# ADR: A hook on every page

Status: accepted, 2026-11-12, at 1.0.5.50. Every page a reader is sent to opens with one box of evidence: the landing, About, a blog post, the resume door, the Quality page and the review route. The words for each live in one data file, `data/hooks.json`, which the React build and the API both read, every line copied word for word from its source, and each box is drawn on the server with no read above the fold.

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

## Addendum, 2026-11-17 (1.0.5.82): the landing opens on the gate

What stopped being true: that the landing's hook is Steve's quality sentence, the same words as the resume door's. The landing now opens on the headline of the affected-pages markup of 17 November, which Steve approved that morning: "Built with AI. Gated so quality never slides." Directly under it is a line computed from data/quality-history.json, never typed: the first version after the last one that made any quality number worse, its day, and how many releases have held since (1.0.5.1, 10 November, at this version). Under that, the hook's own line names how many quality numbers the gate checks and how many versions it has measured, both read from the history when the page is drawn, and then how many versions the changelog dates in the last seven Central days, counted on the page against the visitor's day from the dates the build read. The resume door keeps Steve's sentence, word for word, and its test still turns red if the ratchet gains a sixteenth number. The landing's chart, the Quality page's first, and its one link are unchanged.

Why: the first screen is where a manager who runs continuous deployment decides whether to read on, and the two things such a reader looks for are whether quality holds and how often the team ships. Both are now on it, each bound to the record that proves it, and "held" has one meaning everywhere it appears (no worse than the version before), computed by one function (src/lib/qualityHeld.ts) that the header's gate badge reads too. The badge sits at the end of the store bar on every page; to give it room, the bar's note keeps its short form at every width, the site and its store, with the whole sentence as its title (the first try put the badge beside the whole sentence and went red at 1440 on the Cosmos DB site, whose sentence is the longer). The second try went red at 768 on the Cosmos DB site because the badge, built on a gate branch before the gate had written its rows, said "Gate reading not on file", which left the note too little room; before the gate has finished a version the badge now says "Gate pending", and it fits beside the note's short form at every width on both sites.

What holds it: PageHooksTests (the landing's headline word for word, no count typed in its lines; the resume's sentence against the ratchet), pageHooks.test.ts and qualityHeld.test.ts (the held line, the counts, the week), releaseFacts.test.ts (the count a day and the seven-day window), and hooks.spec.ts, which still holds the whole box on a phone's first screen.

## Addendum, 2026-11-18 (1.0.5.96): what the gate stopped, and the run that shipped it

What stopped being true: that the first screen can say only what shipped. A red gate never promotes, so until G5 nothing on main recorded a version the gate turned back. Promote now keeps every one in `data/gate-reds.json` (seeded from the repository's Actions history, every gate run that ended red with its promote not run, then grown from the gate feed's reds by `scripts/gate_reds.mjs`), and `data/test-results.json` carries the id of the public gate run that shipped the version.

What it changes: the landing ends on the last release the gate stopped, with its day and the jobs that failed, under the strip of what the gate measured and never inside the hook, which has to fit a phone's first screen (its own gate run at 1.0.5.93 put the hook 73 px past 844 at 390 wide), and leaves the line out when the file keeps none; the header badge's popover opens the gate run that shipped the version; Built with AI's first section gains its third column, what the gate caught, read live from the same file; and the tests card leads with the newest push to live the gate has measured, toned like the gate's own row (a quarter of an hour the target, information past it, red at half an hour).

What holds it: GateRedsTests (the fold, once a run and never a cancelled one; the live column; the real list's order and promote's writes), releaseFacts.test.ts and gateFacts.test.ts (the run only for the version the gate measured, the newest stop's Central day), pageHooks.test.ts (the stopped line, and the hook ending on the week's releases), codeHealth.test.ts and ranges.test.ts (the push to live row and its lines), and DockerBuildInputsTests (both images copy the two files the build now reads).

## Addendum, 2026-11-19 (1.0.5.121): the landing's split hook

What stopped being true: that every hook centres its words over its figure. The landing asks the shared `Hook` for a `split` layout that only it uses: its words on the left, with the name line ("Steven Stout / Lead / Staff .NET engineer / Remote", the review route's own list, never typed twice) above the heading, the heading at the size of a page title (`--text-3xl` from 640 up, `--text-2xl` under it, balanced), the "showcase" sentence of `src/lib/intro.ts` under it, the hook's sentences and the page's moves; the chart, its link and the newest post on the right. Two columns from 1280 up, one under it. Every other page's hook draws as it did. The heading and the sentences are still `data/hooks.json`'s, none reworded, and everything in the first screen but the newest post is in the build's own data, so the page still draws on the server with no read (ADR-092).

## Addendum, @DATE@ (@VER@): Quality's two links

The Quality page's Where next (`next`, page `quality`) leads back into the sales path instead of out of it: "Back to the three questions", "Two more answers, each the running system.", 1 minute, `/?view=review#questions`, and "Quality on both ends", "The story behind this page, in one post.", 6 minutes, `/?view=blog&post=quality-on-both-ends`. They are drawn in the sheet's foot, under the closing question (ADR-130, the addendum on the panel, the numbers and the foot); opened over the review route, the first closes the sheet and scrolls to the questions. A visit's code now goes into the query before a section's name, so `#questions` is still found (`withCode`, links.ts). `pageHooks.test.ts` holds both links and no Admin address.

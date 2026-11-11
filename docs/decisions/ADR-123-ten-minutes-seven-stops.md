# ADR: Ten minutes, seven stops

Status: accepted, 2026-11-03, at 1.0.4.130. The site has a page at `/?view=review` that walks a visitor with ten minutes through it in a fixed order: seven stops, each one live thing to look at, one line on what it shows and one thing to do, ending at the resume. The place is kept on the browser. The page is a row in the rail directly under Home and a banner on the landing page beside the resume's, it is drawn on the server, `/review` is a permanent redirect to it that keeps the query, and a tagged link that opens it is counted under its own path.

## Reading list

1. [ADR: The landing page and the site map](https://theyard.stevenstout.biz/?doc=adr-landing-page): the one list the rail and the landing page are drawn from, which this record adds an action and a banner to.
2. [ADR: Tagged links, who opened what](https://theyard.stevenstout.biz/?doc=adr-tagged-links-who-opened-what): the six-character code a link carries and the page's own report of its open, which this page reports under a third page view.
3. [ADR: A rendering service beside the API](https://theyard.stevenstout.biz/?doc=adr-a-rendering-service-beside-the-api): why a view is drawn on the server, so a crawler and a link preview read the page and not a blank frame.
4. [ADR: Every LinkedIn post is also a blog post](https://theyard.stevenstout.biz/?doc=adr-every-linkedin-post-is-also-a-blog-post): the newest view added before this one, and the template this one was added on.

## In plain words

The site holds a lot: a live auction, an Admin tab of readings, more than a hundred decision records, eighteen diagrams, the gate's own results. A visitor with ten minutes does not know where to start, and the landing page shows everything at once. This page picks seven things in the order a reviewer usually asks about them, says in one line what each one shows, and says what to do when you get there. Each stop is the live site, not a screenshot of it, so every number on the way is read from the running system.

The page remembers where you were on this browser, so you can open a stop, look around, come back and pick up at the next one.

What that is worth to a developer: a new stop, a changed address or a reworded line is one edit to one list (`src/lib/reviewStops.ts`), which the server and the browser both draw from and a test holds to addresses the site serves. What it is worth to the organization: the ten minutes a visitor gives the site are spent on the things that prove it, in an order that builds, instead of on whatever the first click happened to find.

## Context

- **The landing page shows everything.** It was drawn for a reader who explores (ADR: The landing page and the site map): two large tiles, the resume, the evidence strip, three groups of sections. A reader with a short window has to choose, and the choice is the reader's guess about what matters.
- **The links sent out land on the home page.** `make_ref.py` mints a code and the Site link opens `/`. The Admin tab can say that a code opened the site, and nothing about what the reader looked at.
- **The views follow one pattern.** Admin, Account and the blog each live at `?view=`, are mirrored into the address bar by the one hook that writes it (`useAddressBar.ts`), and are drawn by the rendering service. A new page that broke the pattern would be the one page a link could not carry.

## Decision

1. **One list, seven stops.** `REVIEW_STOPS` in `src/lib/reviewStops.ts` holds each stop's key, title, address, minutes, what it shows and what to do. The minutes add up to ten. Every address is the site's own: the home page, a view, an Admin card, a record, or the resume's door. The only link that leaves the site is the GitHub link on the gate's card, stop 5.
2. **A view like every other.** The page is `/?view=review`, read by `readFirstAddress`, mirrored by `useAddressBar`, opened by `useReviewNavigation` as a history entry so Back returns to where it was opened from, and drawn by `ReviewPage` in the frame the blog is drawn in.
3. **The place is kept on the browser, never per code.** `src/lib/reviewProgress.ts` keeps the current stop and the stops reached under two keys of the browser's storage (`theyard-review-stop`, `theyard-review-reached`), every read and write in a try, because a private window throws. The page is drawn at the first stop on the server and moves to the kept place once it is on screen, so the server's page and the browser's first draw agree.
4. **Three states on a stop.** Done is a green disc with a tick. Current is the only stop that shows "Do this", a filled "Open this stop" and "Mark done". Next shows its number in a ring and a small "Open". Next stop marks the stop on screen done and moves on; Previous moves back and leaves what was done. Mark done on the last stop goes to the finish panel, which reads its two counts (the records and the diagrams) from the sidebar's own lists.
5. **A row and a banner, from the site map.** The action `review` sits directly after Home, so the rail reads Home, Review in ten minutes, Blog. It is drawn on the landing page as a banner, and so is the resume: `banner: true` on both, `landingTiles()` hands them out apart from the photo tiles, the resume first. The top row is Inventory and Author, the two photographs, and the two banners sit in one row under them from 640 px of column and stacked below that. The review banner opens the view in place; the resume's opens the PDF in a new tab.
6. **Drawn on the server, with no read.** The rendering service draws the page from the list; `loadFirst` reads nothing for it beyond the build every page reads. A crawler, a link preview and a reader without JavaScript get the title and all seven stops.
7. **A short door.** `GET /review` answers 301 to `/?view=review` with the rest of the request's query carried in its order, beside `/resume` in the Docs slice, so `/review?r=<code>` works on a card or in a message. The page sweep checks it as a page wherever the app is served.
8. **A tagged open counts under its own path.** `TaggedRequests.PageViews` gains `/review`, so the page's one report on load (`/api/links/open?r=<code>&path=/review`) is kept under it, read during the first render like the home page's. The door itself is not an open: the page it lands on reports one, so a visit through the door counts once.

```live path=src/lib/reviewStops.ts region=review-stops
```

```live path=src/lib/siteMapEntries.ts region=review-action
```

## Consequences

- The resume leaves the top row: it was the third large tile and is a banner now. The top row is two tiles across the width the strip's three share.
- A stop that moves (a card renamed, a record renumbered) fails `reviewStops.test.ts` before it ships, because the test asks the catalogue and the Admin card list whether each address is served.
- The place is per browser: the same visitor on a phone and a laptop starts twice, and a cleared browser starts again. Nothing on the server knows where a visitor is until a later version reports it.
- The stops open in the same tab, as ordinary links, so Back returns to the route; a middle click opens one beside it.

## Where it sits

All of it is the front end's, in the rings the front end keeps: the list and the place are `src/lib` (no React), the hook over the place is `src/hooks`, the four components are `src/components/review`, and the shell's part is one hook in `src/app/hooks` and a line each in the address bar, the navigation and `App.tsx`. On the API side the door is one route in the Docs slice (`Features/Docs/DocumentationEndpoints.cs`) and the page view is one entry in the Links slice's rule (`Application/Features/Links/LinkOpens.cs`). Nothing in Domain knows the route exists.

## Files

- [`src/lib/reviewStops.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/reviewStops.ts) and [`src/lib/reviewStops.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/reviewStops.test.ts) - the seven stops, and the test that holds them to addresses the site serves
- [`src/lib/reviewProgress.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/reviewProgress.ts) and [`src/hooks/useReviewProgress.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useReviewProgress.ts) - the place, kept on the browser
- [`src/components/review/ReviewPage/ReviewPage.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/review/ReviewPage/ReviewPage.tsx) - the page, with [`ReviewProgress`](https://github.com/SteveStout/TheYard/tree/main/src/components/review/ReviewProgress), [`ReviewStop`](https://github.com/SteveStout/TheYard/tree/main/src/components/review/ReviewStop) and [`ReviewFinish`](https://github.com/SteveStout/TheYard/tree/main/src/components/review/ReviewFinish)
- [`src/lib/siteMapEntries.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/siteMapEntries.ts) and [`src/components/landing/LandingBanners/LandingBanners.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/landing/LandingBanners/LandingBanners.tsx) - the rail row and the landing page's banners
- [`src/app/hooks/useReviewNavigation.ts`](https://github.com/SteveStout/TheYard/blob/main/src/app/hooks/useReviewNavigation.ts) - opening the view
- [`api/TheYard.Api/Features/Docs/DocumentationEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Docs/DocumentationEndpoints.cs) and [`api/TheYard.Tests/ReviewAddressTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ReviewAddressTests.cs) - the door, and the test that holds the redirect and its query
- [`api/TheYard.Application/Features/Links/LinkOpens.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Links/LinkOpens.cs) - the third page view
- [`tests/e2e/review.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/review.spec.ts) - the page in a browser at 390 and 1280
- [`api/TheYard.Application/Features/Review/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Application/Features/Review) - the reach: its ports, `ReviewFolding`, the command, the query and the memory keeper
- [`api/TheYard.Infrastructure.Cosmos/Features/Review/CosmosReviewStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/Features/Review/CosmosReviewStore.cs) - the reach in the activity container
- [`api/TheYard.Api/Features/Review/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Features/Review) - the two routes, the collector and the module
- [`src/components/admin/ReviewCard/ReviewCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/ReviewCard/ReviewCard.tsx) and [`src/lib/review.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/review.ts) - the card and its words
- [`api/TheYard.Tests/ReviewFoldingTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ReviewFoldingTests.cs), [`ReviewHandlerTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ReviewHandlerTests.cs), [`ReviewEndpointTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ReviewEndpointTests.cs) and [`CosmosReviewStoreTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CosmosReviewStoreTests.cs) - the rule, the handlers, the routes and the store

## Addendum, 2026-11-03 (version B, 1.0.4.131): the reach rows and the card

What stopped being true: "Nothing on the server knows where a visitor is" in the Consequences above. From this version the page reports how far a visitor got, and the Admin tab's desk question has a card that says it.

1. **The page reports each stop once a browser.** `useReviewProgress` posts `{ "stop": n, "r": "<code>" }` to `POST /api/review/reached`: stop 0 when the route opens, and a stop from 1 to 7 the first time it is reached on this browser (the reached set it already keeps), by Next stop, Mark done or opening the stop. The code is the `?r=` one read during the first render, as the tagged open is. A send that fails is let go; `keepalive` lets one made as a stop's link is followed finish. `reachBody` and `newlyReached` in `reviewProgress.ts` build it, and a test holds both.
2. **One row a visitor a day, never a name.** The endpoint asks the shared visitor tokens for the stable keyed hash (`VisitorTokens.PersonFor`, ADR-116) and never makes one; a request the site does not count as a person (its own tools, the operator's cookie, the loopback address, a scanner's agent) is answered 204 and not kept. `RecordReviewReachCommand` refuses a stop outside 0 to 7 with a 400 problem, drops a code that is not one rather than refusing it, and hands the reach to `ReviewReachCollector`, which drains once a minute, folds what it took by the one rule and writes.
3. **The one rule.** `ReviewFolding`: a higher stop replaces a lower one and never the reverse, the first time and the first code of the day stay, and the last time moves. The memory keeper and the document store both fold through it, and the document store replaces a row only under the ETag it read, so two containers cannot lower each other's rows.
4. **The fourth kind in the activity container.** `CosmosReviewStore` keeps `kind = "review"` documents with the id `review:{day}:{hash}`, partitioned on the day like the link opens, with no expiry, and reads them with `kind = @kind AND day >= @day`. No container, setting or role changed; a host with no document store keeps the rows in memory (`MemoryReviewStore`).
5. **The card counts people, not rows.** `ReadReviewFunnelQuery` reads the rows of the window (24 h, 7 d, 30 d or 90 d) and folds each visitor's days to one, so a visitor on two days is one visitor at the highest stop reached; `reached_by_stop` has eight places, opened and then the seven stops, each the visitors whose highest stop is at least that one, and `by_code` is one row a code with its highest stop, the minutes from its first report to its last, and its last day. `counted_from` is the first day a row is kept, and the card says it: "Counting from <day>, the day version B shipped." It never reads a bare count as all time.
6. **The card.** "Review route, how far they got", under the desk question right after Who opened what (`CARD_SLUGS`, `bench.ts`): the four window pills, the funnel as eight bars drawn with the activity card's `PathBars` in the first series' teal, the by-code table (Tagged link, Reached, Took, Last seen) with `DataTable`, the counting line and "A code says which link was sent, never who received it". Nothing on it is a status colour. Its code is a chunk of its own; the list of chunks moved to `AdminBenchCard/cardChunks.ts` because `AdminBenchCard.tsx` was at the cap.

What differed from the plan, and why: the reading port hands out rows (`RowsSinceAsync`, `FirstDayAsync`) rather than a finished funnel, so the counting is written once, in the query, and not once in each adapter; the funnel carries `available` and `window` beside the five fields, so a store that cannot be read says so on the card; `KeepAsync` takes the rows the collector already folded. And `LinkCodes`, the rule for what a code is, moved from the Links slice to `Application/Shared/Links`, because a slice may not use another (ADR-107, rule 1) and the review route reads it too; the type and the rule are unchanged.

```live path=api/TheYard.Application/Features/Review/ReviewFolding.cs region=review-folding
```

```live path=api/TheYard.Api/Features/Review/Module.cs region=review-wiring
```

`ReviewFoldingTests` holds the rule, `ReviewHandlerTests` the command, the query over the memory keeper, the four windows and the collector's fold, `ReviewEndpointTests` the 204, the 400 problem, a lower stop leaving the row where it was and a scanner never counted, and `CosmosReviewStoreTests` the same rule against the real account in the gate's live pass. `admin.spec.ts` draws the card.

## Addendum, 2026-11-07 (1.0.4.149): the page says what it is for

What stopped being true: the page's title "Ten minutes, seven stops", its lede, the rail's "Review in ten minutes", the banner's "Seven stops, each one live, ending at the resume." and the last stop's "One page.". The record keeps its title, which is its name.

1. The page's title is "Check my work in ten minutes", and so is the screen reader's announcement. The rail row and the landing banner read "Check my work".
2. The lede says what a reader leaves with: how I design, how I test and how I decide, without taking my word for any of it.
3. The last stop says two pages, which is what the locked resume is (the resume record's addendum of 1.0.4.146).

Why: on 6 November the route had been opened by 18 people in seven days and 2 of them went past its first screen (GET /api/admin/review?window=7d, read 13:42 CST). The page described the tour; a reader deciding whether to spend an interview on me needs to know what checking it gets them. The words stay evidence, not pitch: every stop is still one live thing to look at.

## Addendum, 2026-11-07 (1.0.4.150): every stop names the decision behind it

From this version each stop draws one more line under what it shows, "Why it is built this way:", with the decision record behind the stop as a link by its sidebar title, read from the catalogue and never typed. It opens in the same tab like every stop link, and opening it counts as opening the stop.

| Stop | The record behind it |
| --- | --- |
| The first screen | ADR: One reading for the first screen |
| Bid in the room | ADR: The store does the work |
| What Azure charges | ADR: What Azure charges |
| One decision, with its code | ADR: The numbers that may only fall |
| The gate that shipped this version | ADR: The five-minute gate |
| One codebase, two stores | ADR: Measuring both stores |
| The resume | ADR: The resume has an address |

Seven distinct records, and none is the address its own stop opens: stop 4 already is the rules a change has to pass, so its line names the ratchet that is the reason for them. The records are the one thing on the site no other candidate has, and a reader with ten minutes reaches them only if each sits beside the thing it explains. The route stays seven stops; a second list would be a second tour.

## Addendum, 2026-11-07 (1.0.4.151): the last stop opens the PDF itself

/resume became a door in this version (ADR: The resume has an address, the addendum of this date): a browser asking for it gets a band that links back to this route. A reader at stop 7 has walked the route already, so the stop opens `/resume?pdf=1`, the PDF itself, and is not sent round again.

## Addendum, 2026-11-07 (1.0.4.154): who vouches for it, beside the route

Until this version the route was one column and its last stop was "The resume". From this version:

1. **The last stop is "The resume, and who vouches for it".** It says the six stops above are the evidence for the resume's claims and the recommendations page is who saw it first hand, with no number in the sentence, and it carries a second link, "The recommendations", beside its own Open. A stop may carry more links than its address (`links` in the one list), each the site's own.
2. **A rail beside the stops.** At a wide desk (1280 pixels and up, the nearest step of the site's width scale to the mockup's 1180) the page is two columns: the stops at their 720 pixels and a 332-pixel rail holding the person the route is about, with his links, and every recommendation's pull sentence, each with "Read the whole thing" to its card on the recommendations page. Under that width the rail folds: the person's links under the lede, the quotes after the finish panel. The quotes are read from /api/recommendations in the browser, the same answer the landing column reads, so the stops still draw whole on the server and read nothing.

The route stays seven stops. `review.spec.ts` holds the rail at 1440 and the fold at 390; `reviewStops.test.ts` the last stop's title, its link and its unnumbered sentence.

## Addendum, 2026-11-11 (1.0.5.20): Quality you can watch is stop 1

What stopped being true: the table in the addendum of 1.0.4.150, and its line "stop 4 already is the rules a change has to pass". The route opens on the Quality page now, the one place the site shows code quality held on every version, and "One decision, with its code" folds into it: the rules table it opened is a link on stop 1, "The rules and the tests that hold them", and its record, the numbers that may only fall, is stop 1's record. Still seven stops and ten minutes, so the page's title and lede stay word for word, and the gate is still stop 5. The gate stop now names CodeRabbit reviewing the pull request, true since 1.0.4.176. Three of the route's sentences said more than the site does, and Steve chose to fix them before this version shipped: the cost stop told the reader that every resource name links to its place in Azure, and the cost card holds no link; the gate stop ended on a blue-green roll with no downtime, and a roll swaps the one web app in place with no second copy (ADR: Blue-green on a $13 plan, measured and held); and it said the card's link opens the run on GitHub, and the tests card holds no link. They now read "Open the cost card and read the bill by resource.", "then a roll in place that is read back on both names before the run ends." and "Read the suites and their times for the build you are looking at.", and reviewStops.test.ts holds all three.

| Stop | Minutes | The record behind it |
| --- | ---: | --- |
| Quality you can watch | 2 | ADR: The numbers that may only fall |
| The first screen | 1 | ADR: One reading for the first screen |
| Bid in the room | 2 | ADR: The store does the work |
| What Azure charges | 1 | ADR: What Azure charges |
| The gate that shipped this version | 1 | ADR: The five-minute gate |
| One codebase, two stores | 2 | ADR: Measuring both stores |
| The resume, and who vouches for it | 1 | ADR: The resume has an address |

**The Admin card keeps its rows.** Reach is stored by stop number, never by key (the addendum of version B, 1.0.4.131), so a visit counted before this version cannot be relabelled after the fact, and the stored rows are neither rewritten nor dropped. The card "Review route, how far they got" draws its bars under the new titles and says so in one more muted line under its counting line: the day the stops were reordered, and that reach before that day was counted against the earlier order. The day is one named constant in src/lib/review.ts, and review.test.ts holds the line.

**The kept place starts again.** The browser keeps the place as indexes, so a reader who reached stop 1 under the first order would come back with Quality already ticked and be sent past it. Both keys gain a "-2" (theyard-review-stop-2 and theyard-review-reached-2), and every browser starts the new order at stop 1. A returning reader reports stop 1 again, which the server folds as it folds any repeat: a higher stop replaces a lower one, one row a visitor a day.


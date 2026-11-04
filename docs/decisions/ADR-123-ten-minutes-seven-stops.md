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

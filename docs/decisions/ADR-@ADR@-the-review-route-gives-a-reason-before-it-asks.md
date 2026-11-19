# ADR: The review route gives a reason before it asks

Status: accepted, @DATE@, at @VER@. The first screen of `/?view=review` gives a reason before it asks for any time: one claim a manager can repeat, three proof tiles (two from the record, one live from this build), one named quote, one primary button that opens the proof and "Book 20 minutes" beside it. The seven-stop tour of ADR: Ten minutes, seven stops stays as it was, below the fold, under "The full tour, if you want it". The address, the `/review` door and the reach rows do not change.

## Reading list

1. [ADR: Ten minutes, seven stops](https://theyard.stevenstout.biz/?doc=adr-ten-minutes-seven-stops): the route this record amends, and the reach rows and second actions the metric below is read from.
2. [ADR: Quality you can watch](https://theyard.stevenstout.biz/?doc=adr-quality-you-can-watch): the page the primary button opens, at "The slip, and the turn".
3. [ADR: Tagged links, who opened what](https://theyard.stevenstout.biz/?doc=adr-tagged-links-who-opened-what): how an open of a sent link is counted, and the scanners kept out of it.

## In plain words

Every message I send to a hiring manager links to one page on this site. In the week to 15 November, 32 people opened it through a tagged link and none of them went past its first stop. The page asked for ten minutes and seven stops before it said why anyone should spend them. This record turns the page around: what a manager gets comes first, on one screen, and the long walk is still there underneath for anyone who wants it.

What that is worth: a manager who gives the page ten seconds leaves with a claim they can repeat, a number they can check, and one person who worked with me saying it in their own words; one who is already convinced can ask for twenty minutes without hunting for an address.

## Context

Steve, 19 November: "I'd just feel better if one person who is hiring would look at the yard and be damn I need this guy". The reading behind it: 32 tagged visitors opened `/?view=review` in the 7 days to 15 November and 0 reached stop 2. Some of those codes were link scanners (6 opened every link inside 60 seconds), but not all: three stayed on stop 1 for 8 to 22 minutes and still never moved on.

The review of the first screen as it stood at 1.0.5.104 named six failures:

1. It opened with an ask, ten minutes and seven stops, before it gave a reason.
2. There was no claim about Steve a manager could repeat to their boss; "Lead / Staff .NET engineer" sat small in the right column.
3. The strongest proof was buried: the named recommendations ran down the side as small cards, and the hard numbers lived on stops 2 to 7.
4. Stop 1 was homework ("look at the first chart, read the six cards, then scroll"), not a payoff.
5. The chrome talked to Steve, not to a stranger: the store toggle, "2/2 READY", "Gate green, Quality 15 of 15 held".
6. There was no single next action for a manager who was already convinced.

Three first screens were drawn for it, each at 1568 by 772 and at 390 wide: A, proof first, the ten-second verdict; B, the tour cut to three stops and two minutes; C, written to the hiring manager's own problem with AI-written code. A is first because it answers failures 1, 2, 3, 4 and 6 with the least new code (two tiles from the record, one live read the landing page already makes, one quote, two buttons, and the tour moved down untouched), and because it serves the visitor the data describes: someone who arrives from an email and goes straight to the resume gets a verdict before they leave. C follows at its own address for outreach that is about AI, and B's three stops follow under both first screens.

## Decision

1. **The first screen.** An eyebrow ("Steven Stout / Lead / Staff .NET engineer / Remote"), the claim "Production .NET that stays up. Built with AI, measured on every version.", one line of lede, and three proof tiles: 200+ production releases with zero downtime, a release cycle 50% shorter, and the tests passed on the version being read, the same figure the landing page prints from `/api/tests/summary`, with the quality numbers this build held. Beside them, one named quote from `/api/recommendations` (Drew Dell's, else the answer's first) with the answer's own count, and "Open to remote Lead, Staff and Principal roles." with LinkedIn, GitHub and About Steven. The words are `reviewVerdict.ts`, word for word from the design.
2. **One primary move.** "See the proof in 2 minutes" opens Quality in place at "The slip, and the turn"; a modified click follows `/?doc=quality#the-slip-and-the-turn`. "Book 20 minutes" is `/api/go/book`, an email to Steven with the request for a time already written. The resume link is beside them. On a phone the moves come straight after the lede, before the tiles, so the primary button is on the first screen.
3. **The tour, below the fold.** A line at the fold, "Want the long version? Seven stops, ten minutes, all optional", points to "The full tour, if you want it": the seven stops, the place on the browser, the finish panel, unchanged, and what the sidebar still holds. "Ten minutes" and "seven stops" appear only there.
4. **The chrome on this view.** The store bar is one sentence, "Live system. This version passed every check before it shipped.", and "Live system." alone when the build has no gate reading of its own. The sidebar starts collapsed when this browser keeps no choice for it; a kept choice wins, and the default is never written.
5. **The rail's cards leave this view.** The quotes stay on the recommendations page; the five links the rail held move into the first screen at the same addresses.
6. **Every button says what it was.** Each reports its name to `POST /api/review/reached` (proof, book, resume, recommendations, linkedin, github, about, tour), kept as ADR: Ten minutes, seven stops, the addendum on the second actions, says.
7. **No layout shift.** The rendering service reads `/api/tests/summary` and `/api/recommendations` for this view and hands them over in the first load, so the server draws the tile and the quote and the browser takes the markup over.
8. **Every surface that made the ten-minute ask changes with it:** the link preview's title and description, the tab's title, the site map's blurb that the landing banner reads, the banner list's name, and the README's sentence.

## The metric, and when it is read

Share of review opens that reach a second action, against 0 of 32: the Admin card "Review route, how far they got" gives it beside its eight bars, with the share that reached the running system or the booking button as the guard. Scanners and Steve's own visits are out of both sides. Target above 30%. It is read two full weeks after this version lands, or at 50 opens with scanners removed, whichever comes first.

## What holds it

`reviewVerdict.test.ts` holds the words, the tile's figure against the landing page's, the quote's pick and that nothing above the fold names ten minutes or seven stops; `railStorage.test.ts` the collapsed default and that it is never written; `heads.test.ts` and `render.test.ts` the link preview and the server's draw of the first screen; `review.spec.ts` the first screen at 1568 by 772 and 390 by 844 with axe, the click that lands on "The slip, and the turn", and the action reports.

## Consequences

- A visitor who reads one screen and leaves has still seen the claim, three numbers and one named person.
- What stops being true: that the review route's first words are an ask for ten minutes, and that the rail of recommendation cards runs beside the stops.
- What stays true: `/?view=review`, the `/review` door, the reach rows of stops 0 to 7 and every tagged link already sent, so the 0 of 32 reading stays comparable.

## Where it sits

The page's view layer, in the review route's own components, with one query of the review slice's reading side behind its figures; the API gains nothing for this record but the counted booking link of its first version.

## Files

- [`src/lib/reviewVerdict.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/reviewVerdict.ts) - the first screen's words, the tiles from the record, and the pickers for the live tile, the quote and the counts
- [`src/components/review/ReviewVerdict/ReviewVerdict.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/review/ReviewVerdict/ReviewVerdict.tsx) - the first screen
- [`src/components/review/ProofTile/ProofTile.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/review/ProofTile/ProofTile.tsx) - one proof tile
- [`src/components/review/ReviewLeadQuote/ReviewLeadQuote.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/review/ReviewLeadQuote/ReviewLeadQuote.tsx) - the one named quote
- [`src/components/review/ReviewPage/ReviewPage.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/review/ReviewPage/ReviewPage.tsx) - the first screen, the fold and the tour under it
- [`src/hooks/useReviewReadings.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useReviewReadings.ts) - the tests summary and the recommendations, from the first load or the browser
- [`src/components/layout/StoreBar/StoreBar.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/StoreBar/StoreBar.tsx) - the one sentence on this view
- [`src/lib/railStorage.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/railStorage.ts) - the collapsed default, never written
- [`src/library/pendingAnchor.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/pendingAnchor.ts) - the section the next document lands on
- [`render/heads.ts`](https://github.com/SteveStout/TheYard/blob/main/render/heads.ts) and [`render/loaders.ts`](https://github.com/SteveStout/TheYard/blob/main/render/loaders.ts) - the link preview and the first load's two reads
- [`tests/e2e/review.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/review.spec.ts) - the first screen at both widths, the proof's click and the action reports

# ADR: The sales pages set the site's look

Status: accepted, 2026-11-20, at 1.0.5.133. The three pages a stranger is sent to, the review page (`/?view=review`), the home page (`/`) and the Quality page opened over the review page (`/?view=review&doc=quality`), are one unit with one look, built from named shared parts. The review page takes them first; the home page and the Quality page take them in the next lane, and the rest of the site after them, in the order below.

## Reading list

1. [ADR: The review route gives a reason before it asks](https://theyard.stevenstout.biz/?doc=adr-the-review-route-gives-a-reason-before-it-asks): the first screen these parts are drawn on, and its addendum of the same day on the case cards and the numbered path.
2. [ADR: Ten minutes, seven stops](https://theyard.stevenstout.biz/?doc=adr-ten-minutes-seven-stops): the reports the route's buttons send, and the action `work` this record adds.
3. [ADR: The landing page and the site map](https://theyard.stevenstout.biz/?doc=adr-landing-page): the home page, which takes these parts next.
4. [ADR: One reading for the first screen](https://theyard.stevenstout.biz/?doc=adr-one-reading-for-the-first-screen): the one read of `/api/pulse` the live panels share with the home page's band.

## In plain words

The review page is the link on my resume. A hiring manager opens it, and the home page and the Quality page are where it leads. Steve, 20 November: "this page ... and https://theyard.stevenstout.biz/ and ...&doc=quality should all have the same look and feel as they're our sales materials what you're doing here will be cascaded through the website ... make sure our ADR reflect these new design choices". So the look is decided once, here, as a set of parts with names and files, and every page that takes the look takes those parts rather than drawing its own.

What that is worth: a reader moving between the three pages sees one product, and a change to a part (the number badge, the dark live panel, a door) is made in one place and lands on every page that uses it.

## Context

On 19 November the review page shipped with every exit criterion true and looked bad under Steve's name, because three mockups were joined in words and never drawn whole (systemRules RULE 82). From 20 November every design is one whole-page picture at 1920, 1568, 1440 and 390, a check spec beside it, and the build is not done until the check prints PASS against the live page. The picture for this record is J:\mentor\fablereview1120\pictures\ (the review page beside the home page and the Quality page, and every motion in place), drawn with the site's own tokens; its sheet is `sales_css.py`, ported here rule by rule.

## Decision

1. **The parts, by name, each in one file.**

| Part | What it is | Where it lives |
|---|---|---|
| `op-num` | The gold number badge before a section's heading, "01" to "06" | `src/styles/sales.css`; drawn by `SectionHead` |
| `SectionHead` | A section's opening: the badge, the heading, where the reader is or a door on the right, one line under | `src/components/shared/SectionHead` (it was `review/ReviewHead`) |
| `op-live-panel` (`op-live-head`, `op-live-row`, `op-live-facts`, `op-live-band`, `op-live-cell`) | The dark live panel: the header's gradient, gold labels, white values, a LIVE tag with its dot; rows, a two-by-two block of facts, or laid on its side as a band | `src/styles/sales.css`; `src/components/shared/NowPanel` (LivePanel is the Admin tab's name) |
| `--color-live-on-dark` | The one new colour: the live green lightened for the dark panel, 5.33 on the deep teal | `src/styles/colors.css`, measured in `colors.test.ts` |
| `usePulse` | The one read of `/api/pulse`, in the background, for the home page's band and the review page's panels | `src/hooks/usePulse.ts`; the rows in `src/lib/nowPanel.ts` |
| `op-case` | A case card: the body on glass, the dark panel beside it 1.65 to 1, under it on a phone | `src/styles/sales.css`; `src/components/review/CaseCard`, words in `src/lib/reviewCases.ts` |
| `op-door`, `op-tile-door` | A door, a link that leads on, teal on a gold underline; a number tile's "See how" | `src/styles/sales.css` |
| `op-roles-row` | The roles line and its three links as one quiet row under the first moves | `src/styles/sales.css` |
| `op-ask` | The closing question and its line on the left, the moves on the right, under it on a phone | `src/styles/sales.css`; `src/components/review/ReviewClose` |
| `op-pathbar`, `op-pathdock` | The numbered path: a bar at the top of a desk window once the first screen has gone, a row over the phone's dock | `src/styles/sales.css`; `src/components/shared/PathBar`, its steps and rules in `src/lib/pathSteps.ts`, loaded after the first screen |
| The motions | `op-shine`, `op-ping`, `op-nudge`, `op-check` and the comet, all started by one hook | `src/styles/sales.css`; `src/hooks/useFirstSeen.ts`; the comet in `src/lib/comet.ts`, loaded when its card is seen |
| The pills | The filled move, the outlined one and the gold resume | `src/styles/pills.css`, as it was |

The dark live panel and the case card are the two panels on the site without the glass's brackets: they wear the gold rule along an edge as the picture draws them (ADR: The glass look, its addendum of this version). Every colour, size and radius in `sales.css` is a token from `src/styles`; a component draws a part by its class and never writes a colour of its own (`StyleRulesTests`). The sheet is registered once in `src/styles/globalStyles.ts`, after `pills.css`.

2. **Never dead.** A live value never read hides its row; a read that fails keeps the last value and says "as of HH:MM"; a panel with no row is not drawn. Never a dash, an empty cell, NaN or 0 ms on a page a stranger opens. The rendering service reads `/api/pulse` for the review page as it does for the home page, so the panels are drawn with figures and nothing moves when the browser takes over.

3. **Numbers.** Every number on a sales page is on the locked resume and LinkedIn, or read live from this site. No budget, no other person's work, no year added.

4. **What moves, on every sales page.** A motion is decorative and never tied to a real run. It plays three times when its holder first comes into view, and again when the reader comes back to it. With `prefers-reduced-motion` nothing moves and the finished state shows; before it is seen, and with JavaScript off, the finished state shows, never an empty one. Never a video, never a recording of Steve. One hook, `useFirstSeen` (an IntersectionObserver at 0.25 that sets and clears one class on the holder), drives every motion. The five on the review page: a gold comet runs the quality line on the first question's chart, 4.5 seconds a run; the six checks on the second fill in order 0.38 seconds apart, each with a gold ring as it lands; the dot on a dark live panel breathes out a ring; the first move's arrow dips, two seconds after the page opens; a number badge takes one pass of light.

5. **The cascade.** After the three sales pages the look goes, one lane at a time, to: the stack page, the blog list and a post's body, About's body, the recommendations page's body, the document sheets (the decision records, Architecture), and the Admin tab last.

## Why

- **One unit is what Steve asked for, twice.** On 19 November ("this whole page should feel like one unit") and on 20 November ("same look and feel ... cascaded through the website"). Parts with names are how a look is kept across pages and lanes: the next lane takes `op-case` rather than drawing a card of its own.
- **A part per file keeps the look checkable.** `StyleRulesTests` already holds every sheet to the tokens; a shared sheet under it means one place to look.
- **Motion that shows the work without pretending.** A manager's eye goes to what moves. The rules keep it honest: it is decoration, it stops, it respects reduced motion, and the page is whole without it.

## Consequences

- The review page is the first to take every part; the home page and the Quality page follow (saleshome1120) and extend this record.
- What stops being true: that a section of the review page opens with `ReviewHead`, and that a page draws its own live readout.
- What holds it: `StyleRulesTests` and `colors.test.ts` (tokens only, the new colour's contrast, gold named as trim), `nowPanel.test.ts` (fresh, stale with "as of", never read), `reviewCases.test.ts` (the cards' words, doors and named voices, the post link both ways), `ReviewFoldingTests` and `review.test.ts` (the action `work` in neither figure), `LinkHandlerTests` (the booking email's three questions), `render.test.ts` (the panels drawn on the server), and `review.spec.ts` (the order, the cards, the close, at 390 and 1440); the whole page against its picture at four widths with `check_mockup.py`.

## Where it sits

The look is the React app's: one sheet under `src/styles`, loaded once beside the pills, and components under `src/components/shared` that any page draws, with the review route's own parts beside the route. The only server change is the booking email's three question lines in `appsettings.json`, read by `GET /api/links/book` and `/api/go/book` as before; the rendering service reads `/api/pulse` for the review route as it does for the home page.

## Files

- [`src/styles/sales.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/sales.css) - the shared parts, one region each
- [`src/components/shared/SectionHead/SectionHead.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/SectionHead/SectionHead.tsx) - a section's opening with its number
- [`src/components/shared/NowPanel/NowPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/NowPanel/NowPanel.tsx) - the dark live panel
- [`src/hooks/usePulse.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/usePulse.ts) and [`src/lib/nowPanel.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/nowPanel.ts) - the one read and its rows
- [`src/components/review/CaseCard/CaseCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/review/CaseCard/CaseCard.tsx), [`src/components/review/ReviewWork/ReviewWork.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/review/ReviewWork/ReviewWork.tsx) and [`src/lib/reviewCases.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/reviewCases.ts) - the case cards
- [`src/components/review/ReviewClose/ReviewClose.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/review/ReviewClose/ReviewClose.tsx) - the closing question beside its moves
- [`src/styles/colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css) - `--color-live-on-dark`

## Addendum, 2026-11-20 (1.0.5.136): the numbered path and the motions are live

The second version of the review page's picture: `PathBar` and the five motions, as the Decision above names them. Three widths were settled against the site's one width scale (`src/lib/breakpoints.ts`, which `StyleRulesTests` holds every media query to): the bar shows every step's name from the wide desk step, 1280, and only the current step's name under it (the picture drew 1100, which is not a step); the bar is drawn from the desk step, 1024, because under it the rail is a drawer behind the sticky header the bar would cover, and the phone's row under the dock is drawn under 640; between 640 and 1024 the page has no path. At the page's foot, where the close can never rise to 45 per cent of the window, the last section in view is the current step, so "06 Talk" is marked when the reader reaches it. With reduced motion the bar and the row appear without sliding. Held by `pathSteps.test.ts`, `useFirstSeen.test.ts`, `comet.test.ts` and `review.spec.ts` (the bar after the first screen at 1440, the row over the dock at 390, nothing moving with reduced motion, the comet and the six checks playing when their card is seen).

## Addendum, 2026-11-20 (1.0.5.140): the home page takes every part

The home page is the second sales page to take the look (saleshome1120, from its whole-page picture), part by name. **`SectionHead`** opens every group, 01 Start here to 07 Who and why, its badge before the words; the pill-shaped heading the home page drew of its own is gone, and the link inside "What colleagues say" keeps its address. A head on the home page is its own holder of the badge's pass of light (`SectionHead`'s `seen`), since its section holds no other motion. **The dark live panel** (`op-live-panel`) is the band of live readings, laid on its side: the head "TheYard, right now" with the LIVE tag over the four instruments, four across at a desk and two by two on a phone; the marks take their colours through the chart tokens set on the band, never a colour of their own. **The close** (`ReviewClose`'s home form) draws the `op-ask` row: the review route's question and line under the 08 badge, its heading for a screen reader still "Next step". **The first row** carries Book 20 minutes as the outlined pill after the gold resume, opening the booking sheet. **The motions**: the live dot breathes, the primary arrow nudges right (`op-nudge-x`; the review route's dips down) and each badge takes its pass of light, all through `useFirstSeen` under the rules above. What it reads does not change. Held by `landingMoves.test.ts`, `landing.spec.ts`, `hooks.spec.ts` (the hook on a phone's first screen at 360 and 390) and `home-look.spec.ts` (the first row and the dark band at 390 and 1440), and `check_mockup.py` against the picture at four widths.

## Addendum, 2026-11-20 (1.0.5.141): the Quality page takes every part

The Quality page is the third sales page to take the look (saleshome1120, V2), part by name. **The dark live panel** (`op-live-panel`) is its four readouts, on its side under the head "The ratchet, right now" with the LIVE tag. **The number badge** (`op-num`) is before every titled card's heading, 01 to 10, the cards hooked as `q-card`; the layout draws it for the wide sheet. **The close** (`op-ask`) is the sheet's last card, `QualityFoot`, with the close's three moves named once in `CLOSE_MOVES` (reviewVerdict.ts) and reported by the same names as on the review route (`reportAction`), and Where next under it. **The motions** play through `watchSeen`, `useFirstSeen`'s form for holders drawn as markup. The three sales pages now read as one: the same head, the same panel, the same badges and the same closing question.

## Addendum, 2026-11-20 (1.0.5.142): the one close, and every page a reader is sent to ends on it

The closing row the review route drew as its "06 Talk" is now the site's one closing row, `AskClose` (`src/components/shared/AskClose`): a glass card with the question "What has to work, where is it today, and what does success look like?", its line "Book 20 minutes opens an email to me with those three questions in it, and I answer with a time.", and three moves, Book 20 minutes (the filled pill, `/api/go/book`, which opens the booking sheet too), the gold Resume (PDF) and Message me on LinkedIn (the outlined pill). Every public page a reader is sent to ends on it: the review route draws it with its 06 badge and its send line, the home page with its 08 badge under the heading a screen reader lands on (Next step), the Quality sheet's foot draws the same three moves from the same list (`CLOSE_MOVES`), a blog post draws it in its own card directly above Where next, and the recommendations page draws it after its last card, That is everyone. /about, which the API draws in C#, draws the same row from `AskCloseHtml.cs` in its own sheet with the site's token values, with one quiet link under the moves, TheYard on GitHub, in place of the row of six links it ended on. The words live in `src/lib/reviewVerdict.ts` (`CLOSE`, `CLOSE_MOVES`); `AskCloseHtmlTests` holds the C# copy equal to them. A post and the recommendations page open the review page's booking sheet; About's Book 20 minutes is the plain link to the counted redirect. Nothing new is counted: each move is the address the rest of the site already counts, and these pages are outside the review route's reach card. Built from the onepath1120 picture; held by `AskClose.test.ts`, `AskCloseHtmlTests` and `next.spec.ts` (a post, About and the recommendations page at 1440 and 390 each end on the question and its three moves, then the right two links).

## Addendum, 2026-11-26 (1.0.5.166): three more places never look dead

What stopped being true, in three places, that a live part of these pages could look dead or wrong for a moment. The home page's stack tiles counted up through figures that were never true ("1 stores", "99 decision records"): their text is now the final figure from the first paint, in the HTML the rendering service sends (it reads the tests summary for the bare address too) and after the browser takes the page over; the count is drawn by a pseudo-element over the finished figure, so it is visual only and a reader who asked for less motion sees none. The review route's live lot drew empty and its button fell back to the inventory until its read answered: the service now draws the page with a lot, a later read replaces it only with a lot, and the route opened again in the same visit starts from the last lot read. The blog's side card said "No reads in the last 30 days yet." while its read was still out: its body is empty in its kept box until the read answers, and says that only when the answer is none. home-look.spec.ts, review.spec.ts and blog.spec.ts hold each one with the API's answer held back.

## Addendum, @DATE@ (@VER@): the landing views, one look for every page a recruiter link opens

What stopped being true: that the review route's look stopped at the review route, home and the Quality sheet. Steve, 27 November: "make sure my main/landing links have the same look and feel ... look impressive to a hiring manager and draw in visitors". The six **landing views** are home, the review route, About, the blog (the list and every post) and the recommendations, one list in `src/app/pageOpen.ts` (`LANDING_PAGES`, `landingOf`), and on each of them five things hold, ported rule by rule from the whole-page designs (look1127, reviewed by Fable the same morning):

- **M1, the shell.** The rail opens closed (a visitor's own choice is still kept, ADR-013), the band is the review route's live sentence in place of the SQL / Cosmos DB toggle (the inventory and every other view keep the toggle), and main is the review route's box, `--landing-width` (1234 px), so every page draws 1186 px at a desk, 159 to 1345 at 1440. Main carries `data-landing`; `data-view='review'` stays the review route's own for its paddings.
- **M2, the page head.** The review route's hero grid is `PageHead` (`src/components/shared/PageHead`): a main column and a 344 px side 32 px apart, in three areas so a page says whether its side comes straight after the title on a phone (About's photo) or after the body (the blog's cards). The eyebrow (`Eyebrow`, "Steven Stout / ...") and the 40 px title over the 3 px gold rule are `op-eyebrow` and `op-title` in sales.css; the review route draws through the same component and measures as it did.
- **M3, section heads.** `SectionHead` with its number badge, on the ground above the cards it names; 48 px between sections at a desk.
- **M4, one frame per card.** Steve, 06:42: "do we need the double wrapper". No section wraps its cards in a second glass panel: How I lead's and Away from the keyboard's panels are gone, and home's hero stands on the ground.
- **M5, the close.** Every landing view ends on `AskClose`, the box's full width, numbered next where the page numbers its sections (About 04, the blog 03, a post 02).

**About** is woven, by Steve's three rulings (06:42 to 06:52): "I want the professional mixed in not clustered at the top", "I liked bigger images mixed around when possible, it gave it a more personal feel", and the double wrapper above. The order is one list, `src/lib/aboutOrder.ts`: the head (the photo as the side), 01 How I lead, St. Charles, 02 The work, History, Food, 03 the voices, Games, Modding, the other voices, the coaching card, the house plants, Freya and Vincent, Why I write, the couple's photographs, 04 the close. Each personal row (`AboutRow`) is one card, photographs and words 7 to 5, the photographs taking the 7 column on a photo-left row and the 5 on a photo-right row, left and right in turn, each labelled "Away from the keyboard"; the Pantheon's 678 px file takes a 5 column seat. Each photograph's `sizes` names the seat it now draws in, so the browser loads a cut at least as wide as it is drawn, and the 2x cap of 1.0.5.168 is gone with the small cards it served. **Why I write** is Steve's own line, word for word, in `docs/author/AUTHOR.md` and in `AboutPage.cs` for /about, beside the newest post, which the server's first draw carries. Nothing was removed: every picture, caption, credit and sentence stays; "About Steven" moved into the eyebrow and "Away from the keyboard" into the row labels.

**The blog** opens with `PageHead`: "Blog" and its line, the side the author card (`AuthorCard`: his photo in the gold ring, his role and About's sentence) over the most read and where readers came from; the keywords fold behind one "More keywords (n)" pill whose row stays in the page, hidden; "01 Latest post", "02 Every post", then the close. A post's own title is the page's one h1, its summary the line and its cover under it; "01 The full post" no longer prints the title or the hook. Every byline is his photo, not the initials.

Held by `pageOpen.test.ts`, `aboutOrder.test.ts`, `blogHead.test.ts`, `AuthorCard.test.ts`, `BlogFilters.test.ts`, `render/aboutWeave.test.ts` (Why I write character for character, each photograph's seat), `render/about.test.ts`, `AuthorPageTests` (/about carries the line), `landing-shell.spec.ts` (the shell on all six, a returning visitor's rail, Admin from the closed rail, About's photo on a phone's first screen, the byline photo, a post's long code line at 390 and 1440), and `check_mockup.mjs` against the five designs at 1920, 1568, 1440 and 390.

### Files

- [`src/app/pageOpen.ts`](https://github.com/SteveStout/TheYard/blob/main/src/app/pageOpen.ts) - the landing views, one list
- [`src/components/shared/PageHead/PageHead.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/PageHead/PageHead.tsx) and [`src/components/shared/Eyebrow/Eyebrow.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/Eyebrow/Eyebrow.tsx) - the page head
- [`src/components/shared/AuthorCard/AuthorCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/AuthorCard/AuthorCard.tsx) - the blog's author card
- [`src/lib/aboutOrder.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/aboutOrder.ts) and [`src/components/about/AboutRow/AboutRow.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/about/AboutRow/AboutRow.tsx) - the About weave and its rows
- [`src/components/about/AboutWrite/AboutWrite.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/about/AboutWrite/AboutWrite.tsx) - Why I write
- [`src/styles/sizes.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/sizes.css) - `--landing-width`

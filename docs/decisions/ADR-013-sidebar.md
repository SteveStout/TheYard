# ADR: The sidebar

Status: accepted, 2026-09-02, shipped as 1.0.0.15. Supersedes the desktop
half of ADR: The phone header; the phone half lives on inside this one.

## In plain words

The site's menus live in one side panel (the sidebar) at every screen width. At 1024 pixels and wider it stays open beside the page and can shrink to a column of icons; on a phone or tablet the same panel slides in as a drawer, and the old dropdown menus are gone.

What that is worth: a developer adds a page to the menu with one line of data and it appears in both shapes, and the organization keeps every document one glance away for a visitor on a wide screen.

## Context

The header had grown five dropdown menus (Hosting, CI/CD, Best Practices,
Changelog, About) plus an Admin button, and on a phone they gave way to a
drawer. Two navigation surfaces for one site, and the desktop one was the
weaker: a visitor landing on a laptop had to guess which of five
small words hid the decision records. Steve's words, with a reference
attached: "I wanted a sidebar instead of dropdowns", and "the dropdowns
look horrible on mobile". The reference was the Dribbble mobile-sidebar
pattern: one dark side panel, a brand block at the top, icon-led rows under
muted headers, everything in one place.

## Decision

One navigation surface at every width, built from the same MENUS record the
dropdowns and the drawer already shared. The dropdowns are deleted.

- **At 1024 pixels and up the panel docks** as a persistent left rail, 272
  pixels wide, beside the page. A collapse control shrinks it to a 64 pixel
  icon rail; the labels stay in the accessibility tree and return as
  tooltips, and the choice is remembered per browser in localStorage. The
  page keeps its 1200 pixel content width inside the remaining column.
- **Below 1024 pixels the same panel is the drawer** from the previous
  record: a hamburger in the header opens it over the dimmed page, and
  Escape, the backdrop, or the X closes it.
- **The header goes away when the rail is docked.** The rail's brand block
  is the way home, so a second brand in a header would have been a
  duplicate. Below 1024 pixels the header stays, carrying the brand, Reset
  bids, and the hamburger.
- **Admin and Reset bids move into the rail's pinned group**, with the
  resume and the repository, so every action lives in the one panel. Admin
  keeps its ?view=admin address and its row reads as current while the tab
  is showing; a doc's row reads as current while that doc is open.
- **1024 is the docking line** because a 272 pixel rail beside the 760
  pixel doc viewer needs the room, and because 640 to 1023 covers tablets
  and narrow windows where a drawer is the honest answer.
- **The cost was paid in tests, not in taste.** Six desktop checks that
  opened docs through dropdown menu items were rewritten to open them
  through the rail; a new sidebar check covers the docked shape, the
  collapse, the memory of it, and the 1023 to 1024 boundary. The five phone
  checks were kept as they were, which is how the drawer is proven unchanged.

## In the code

The samples below are read from this build's source each time the page is
served (ADR: Live code samples). The one component and its two shapes
([`src/components/layout/SideNav/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.tsx)):

```live path=src/components/layout/SideNav/SideNav.tsx region=shapes
```

The docking line, read by the app as a media query, and the rail's memory
([`src/app/hooks/useRail.ts`](https://github.com/SteveStout/TheYard/blob/main/src/app/hooks/useRail.ts)
and [`src/hooks/useMediaQuery.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useMediaQuery.ts)):

```live path=src/app/hooks/useRail.ts region=docking
```

The layout, two columns with the rail's width from a token
([`src/app/App.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/app/App.module.css)
and [`src/styles/sizes.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/sizes.css)):

```live path=src/app/App.module.css region=rail-grid
```

```live path=src/styles/sizes.css region=rail-widths
```

The rows, the icons, and the palette are unchanged from the phone record:
[`src/components/layout/SideNav/SideNav.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.module.css),
[`src/components/shared/SheetIcons/SheetIcons.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/SheetIcons/SheetIcons.tsx),
and the contrast proof in
[`src/styles/colors.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.test.ts).
The proof of the docked shape is
[`tests/e2e/sidebar.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/sidebar.spec.ts).

## What this replaced

The dropdown component and its styles were deleted outright rather than
kept behind a flag. The MENUS record, the doc viewer, and the phone drawer
were kept; the drawer was renamed into the sidebar component because it
already was the sidebar, only shown on phones.

The alternative considered was a hamburger at every width with no docked
rail. It matches the reference shots more literally and costs a breakpoint
less, but it hides the documents behind a tap on the widest screens, which
is where a reviewer lands first. The rail keeps them one glance away.

## Consequences

- Five menus became one panel; a new doc or section appears in the rail and
  the drawer from one line of data, as before.
- Twenty-five end-to-end checks, five of them at phone size and five in
  the new sidebar file. The desktop specs read simpler than they did, because a
  rail row is one click where a dropdown was two.
- The header exists only below 1024 pixels now. Anything added to it later
  needs a home in the rail as well, which the pinned group provides.
- The collapsed rail depends on tooltips for labels, so it is a power-user
  shape; the default is open, and the memory is per browser.

## Addendum, 2026-09-02: light, on Steve's word, shipped as 1.0.0.19

After seeing 1.0.0.17 on his phone, Steve found the side panel too dark and
asked for everything light and modern. The dark navy panel came from
the reference in ADR: The phone header's addendum; the rest of the site
never followed it, so the sidebar was the one dark surface on a light
page. It is light now.

What changed, all of it in the tokens and one stylesheet:

- **White ground, a hairline instead of a shadow.** The rail is white with
  a one-pixel border on its page side; the drawer stays white over a
  lighter dim. The brand and the current row read in the heading navy,
  and the brand blue marks active icons, the focus ring and the current
  row's edge.
- **Rows without dividers.** The hairline under every row is gone; a row
  is a rounded highlight that appears on hover and stays on the current
  one, the shape most light sidebars use. Rows are still 48 pixels tall
  with an icon leading each.
- **Contrast measured again.** Text 15.1:1 on white and 13.6:1 on the
  tinted row, muted text 5.9:1 and 5.3:1, icons 4.8:1 and 4.4:1, the
  brand blue 5.1:1 and 4.5:1. The unit test that guarded the dark palette
  guards this one; only the numbers it reads changed.

The palette, read from this build
([`src/styles/colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css)):

```live path=src/styles/colors.css region=sheet-tokens
```

Nothing moved: the rows, the icons, the docking line, the collapse and the
drawer are as this record describes above, and the end-to-end
checks passed unchanged.

## Addendum, 2026-09-15: a twelfth section, for the API

The API describes itself from this version (ADR: The API describes itself), and the page a person
reads that description on is not markdown, so it cannot be a document row. It is a section of its
own, **API Reference**, right under App Architecture, holding two link rows that open in a new tab
the way the drawings do: the reference page and the OpenAPI document itself. A section rather than
two rows inside App Architecture because a reader who came to see the API should find it in the
table of contents without opening anything. The browser suite's count of headings is twelve now.

## Where it sits

The sidebar is front end code: SideNav.tsx renders both shapes, useRail.ts in src/app/hooks decides docked, collapsed or drawer, and useMediaQuery.ts in src/hooks reads the 1024 pixel line. Each file keeps one reason to change, which is single responsibility; the rows come from the sections data so a new document or section appears without editing the component, the open/closed principle at small scale. Deleting the dropdowns cost six rewritten desktop browser tests, and the collapsed rail leans on tooltips for its labels. A site read mostly on phones would have made a hamburger at every width the simpler answer.

## Files

- [`src/components/layout/SideNav/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.tsx) and
  [`src/components/layout/SideNav/SideNav.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.module.css): the one navigation surface in
  its two shapes.
- [`src/app/hooks/useRail.ts`](https://github.com/SteveStout/TheYard/blob/main/src/app/hooks/useRail.ts) and [`src/app/App.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/app/App.module.css): the docking line,
  the rail grid, and the header that exists only below it.
- [`src/hooks/useMediaQuery.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useMediaQuery.ts): how the docking line is read.
- [`src/library/sections.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/sections.ts): the sections, in `MENU_ORDER`.
- [`src/components/shared/SheetIcons/SheetIcons.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/SheetIcons/SheetIcons.tsx) and
  [`src/components/layout/BrandMark/BrandMark.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/BrandMark/BrandMark.tsx): the row icons and the brand.
- [`src/styles/sizes.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/sizes.css) and [`src/styles/colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css): the rail widths and the sheet tokens.
- [`tests/e2e/sidebar.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/sidebar.spec.ts) and [`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts):
  the proof at 1280, 1023 and 375.

## The look, from the live site

Captured from the domain at 1.0.0.22 by the same headless Chrome the
end-to-end suite uses (ADR: Docs and testing, addendum).

![The docked rail beside the inventory at 1280 pixels: sections of icon rows, Admin and the links pinned below](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-home.jpg)

![The rail collapsed to a 64 pixel icon column, the page taking the width](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-rail-collapsed.jpg)

![The drawer on a 375 pixel phone: the same rows, sliding in over the dimmed page](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-phone-drawer.jpg)

The rows themselves, one loop over the shared record
([`src/components/layout/SideNav/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.tsx)):

```live path=src/components/layout/SideNav/SideNav.tsx region=rows
```

## Addendum, 2026-09-09: two sections, for the stores and for the drawings

Two sections joined the top of the sidebar. **SQL vs Cosmos DB** holds the
page that puts the two stores side by side (`docs/sql-vs-cosmos/SQL-VS-COSMOS.md`); it
stands beside Hosting because the subject is as large as where the site runs,
and a reader should not have to know which record holds the comparison. The
records the page draws on stay in the Decision Records index, where they were
decided. **Diagrams** holds one row per drawing that opens on a page of its
own, five today, each a link in a new tab rather than a document in the
dialog, for the reason ADR: Every diagram opens on its own page gives; its
addendum has the list and the test that holds it to the server's catalogue.
The order from the top is now App Architecture, SQL vs Cosmos DB, Diagrams,
Hosting, CI/CD, Best Practices, Decision Records, Changelog, About, and the
browser suite reads every heading.

![The rail on the live site at 1.0.0.107: App Architecture with its four rows, then SQL vs Cosmos DB with one row, then Diagrams with five link rows, each with the new-tab icon](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/sidebar-sections.png)

![The comparison open from its section: the record dialog over the inventory, the rail row marked current, the page's first paragraphs](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/sidebar-sql-vs-cosmos-open.png)

## Addendum, 2026-09-15: every section is closed on arrival, shipped as 1.0.0.135

The records index has been a closed `details` since it was written, and the other ten sections were
always open. That was the right call at four sections and the wrong one at eleven: the rail arrived
holding about a hundred rows, the reader's own section was somewhere inside them, and finding
anything meant scrolling past everything.

**Every section is a `details` now, and every one of them starts closed.** The sidebar arrives as
eleven headings, which is what a table of contents is for, and a section opens on a click. The state
is deliberately not remembered between visits: the reason to collapse is the arrival, not the
session, and a reader who wants one section open wants it open once.

Three things stay as they were. The element does the work, so the keyboard path, the screen reader
announcement and the disclosure marker all come from `details` and `summary` rather than from a
reimplementation with a button and a piece of state. The icons-only rail keeps its rows, because it
shows no headings to collapse and a closed section there would be a triangle with nothing to read.
And the current row still reads as current while its document is open.

The browser suite carries the rule: eleven sections, none open on arrival, one open after a click,
and none open again after a reload. Every spec that opens a document now names the section it lives
in, through one helper in `tests/e2e/app.ts`, which is the part of this change that touched ten
files.

```live path=src/components/layout/RailSectionShell/RailSectionShell.tsx region=section-shell
```

## Addendum, 2026-09-21: a fourteenth section, for the author, shipped as 1.0.0.171

Everything in the sidebar was about the site, and nothing was about the person who built it. The
reader this site is for is someone deciding whether to start a
conversation, and a conversation is with somebody. So the last section, right under About, is
**Author**, with one page in it, **About Steven**: who he is at work in three sentences, three ways
to reach him, the rest of his life in small headed blocks, a closing line. The intro strip over the
inventory gained a fourth link, "Who built this", which opens it.

**The words are his.** They live in `docs/author/AUTHOR.md`, a served document like every other, so the
catalogue test, the page sweep and the house voice read it. He edits a draft and approves the
text. 1.0.0.171 carries the draft, shipped at his word so that he could review it on the live
page, and his edits follow it. Nothing about him or his family reaches the page any other way: an
image the document names is shown only if it is one of the photographs in
`src/lib/authorPhotos.json`, and any other is left out by the layout.

**It is a page and not a letter, and the document stays a document.** A panel with buttons, blocks
two to a row and photographs side by side are not things markdown can say. A component with the
words typed into it would take the words out of the documents, where the tests read them and where
he edits them. So the layout is a function, `src/lib/author.ts`, over the HTML the one renderer
already wrote: second-level headings open the panels, third-level headings open the blocks, a list
in which every item is one link is a row of buttons, a rule opens the closing panel. It arrives on
demand with the renderer, so the inventory's first load pays nothing for it. The dialog is wider for
this one page and stays solid, as every document's is.

```live path=src/lib/author.ts region=panels
```

**What is deliberately not on it, and why.** The page grew from an introduction he once wrote to a
new team, which was private and is dated. Left out because they go stale: an age, and anything that
was true on one date. Left out because they are somebody else's: the names of private people other
than the one he approved, and anybody from a former employer. Left out because they place his home:
a street, a neighbourhood, a phone number, any email address. There is no contact form and no
address to harvest: the resume, LinkedIn and GitHub are the three ways to reach him. `AuthorPageTests`
holds all of it, each rule with its reason. The few exact words that must never appear are held as
salted digests and not as text, because the test is as public as the page, and a list of what to
keep off the page, written out, would publish it.

**The photographs.** Served the way the inventory's are (ADR: Responsive photos): several widths of
one cut, WebP before JPEG, the real width of every file in the `srcset`, the box reserved so
nothing jumps, lazy below the fold, from `/api/images/author` with the same day of caching.
`scripts/author_photos.mjs` cuts them from originals that stay outside the repository, refuses to
scale one up, and reads every file back to prove it holds no EXIF, XMP, IPTC or colour profile: a
phone's photograph knows where it was taken, and this page must not. The same test opens every
file in the gate, checks its real width against the list, and fails on any file in the folder that
the list does not name. The floor for a photograph's largest cut is 1920 pixels. The two snapshots
of the rabbits are 1600, and the list says why: they are cropped tight so the room is not the
subject, and 1600 is every pixel the crop has.

**The frame and the blocks.** Every photograph wears one frame, from two tokens,
`--frame-photo-border` and `--frame-photo-shadow`: a 3 pixel deep teal border, a gold hairline
outside it, 8 pixel corners, a soft teal shadow. All gold on the pictures was tried and was too
much. The gold went to the headed blocks instead: each has a 3 pixel top edge, gold then teal then
gold, by the block's place on the page. No block carries a colour class, so an edit to the
document cannot leave two neighbours the same. Gold there is trim on a white card with no amber
near it, which is the job the style page gives it; the panels keep the dark green left edge and the
dark green rule with its gold tick.

```live path=src/library/AuthorPage.module.css region=author-alternation
```

The browser suite opens the page from the rail and from the phone's drawer, counts the three
buttons and their 44 pixels, loads every photograph from this site in its frame, reads the blocks'
top edges for the alternation, and holds that nothing on the page is wider than a phone. The
suite's count of headings is fourteen.

1.0.0.172 added the two photographs of him and Katie that were in the approved mock-up, with his
yes to publishing them and the photographer's credit under the panels; a phone is never offered a
file of any photograph wider than 960.

1.0.0.174 added the Mass Effect Legendary Edition key art under Games, at Steve's word, with
its credit to Electronic Arts as the picture's caption: it is Electronic Arts' art and not his,
and the credit says so.

Files this addendum decided about:
[`docs/author/AUTHOR.md`](https://github.com/SteveStout/TheYard/blob/main/docs/author/AUTHOR.md),
[`src/lib/author.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/author.ts),
[`src/lib/authorPhotos.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/authorPhotos.ts) and
[`src/lib/authorPhotos.json`](https://github.com/SteveStout/TheYard/blob/main/src/lib/authorPhotos.json),
[`scripts/author_photos.mjs`](https://github.com/SteveStout/TheYard/blob/main/scripts/author_photos.mjs),
[`api/TheYard.Tests/AuthorPageTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AuthorPageTests.cs) and
[`tests/e2e/author.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/author.spec.ts).

## Addendum, 2026-10-05: one page about him

The sidebar had two pages about Steve: About Steven Stout, the served page at `/about` that opened the About section in a new tab, and About Steven, the Author section's page. Steve asked for one. About Steven now carries everything. The intro keeps its words and gains the paragraph on TheYard as the proof of how he builds. A new panel, How I lead, holds the drawing of a team formed into an arrow with one gold figure out in front, his one-line lede and the four habits as headed blocks. The drawing is cut from `public/about-lead.svg` the way the photographs are cut, by `scripts/author_photos.mjs` at three widths, so it wears the same frame and passes the same checks.

The About section now holds the project's README and how it was built, and has no row for `/about`. The served page itself stays: a search engine or a tool that does not run the app still reads his name and How I lead there (ADR: The public face, the addendum of 5 October). `AuthorPageTests` holds the panel and its links. `author.spec` holds the fourth panel and the drawing in its place, and `sidebar.spec` holds that the rail offers one page about him.

## Addendum, 2026-10-06: the intro keeps its own words

Steve asked for the paragraph on TheYard to come out of About Steven. The intro is back to the words it had before one page about him, and the How I lead panel, the photographs and the links stay as they were.

## Addendum, 2026-10-06 (1.0.3.84): the folders

Until this version every document sat flat in `docs/`: ninety records and twenty-eight pages in one list on GitHub, while the site already showed them in fifteen sections. Steve asked for a folder structure. Each section that holds documents now has a folder of the same name, and the repository reads the way the sidebar does: `about/`, `author/`, `app-architecture/`, `sql-vs-cosmos/`, `performance/`, `style/`, `site-traffic/`, `hosting/`, `built-with-ai/`, `ci-cd/`, `best-practices/` and `decisions/`. API Reference and Diagrams hold links and drawings and no markdown, so they have no folder. The changelog stays in the root, because both deploys read the version from it there, and so does `docs/images/`, which the drawing scripts and every document's pictures name.

Nothing a reader of the site sees moved. A document is served by its slug, and the slugs did not change, so every `?doc=` address and every `/api/docs/` address posted anywhere still opens the same page. The files moved with `git mv`, so each one keeps its history on GitHub. `DocsFolderTests` holds the shape: every document sits in the folder of the section that offers it, every markdown file under `docs/` is one the catalogue serves, and the root holds only the folders, the changelog and the pictures.

## Addendum, 2026-10-06 (1.0.3.86): the folders have their own record

The folder rule described in the addendum above is a standing rule for every new document, so it has a record of its own, ADR: One folder per sidebar section, which holds the tree, how to add a document and what the test checks.

## Addendum, 2026-10-15 (1.0.3.135): what colleagues say

Steve asked for his LinkedIn recommendations on the landing page and on About Steven. Ten people have written one, and every one is public on his profile, so the site quotes them: About Steven gains a panel, What colleagues say, between How I lead and Away from the keyboard, one block per person with the name and title LinkedIn shows, how they worked together and the month, and the recommendation word for word as a quotation. The landing page carries a strip of three sentences lifted from three of them, each credited, with one link to the ten in full.

The rule that nobody from a former employer is named on the page stands for everybody who did not write one. A recommendation is the writer's own public words about him, with their name on it by their choice, which is the opposite of a page naming a colleague who never asked to be on it. The words live once, in `src/library/recommendations.ts`, and `AuthorPageTests` holds About Steven to the same lines, so an edit to either shows up in the gate. `landing.spec` holds the strip and its link; `author.spec` holds the panel and its ten blocks. Nothing is reworded: the one place the site shortens anything is a title that LinkedIn shows as a headline, cut to its first part.

## Addendum, 2026-10-15 (1.0.3.136): the row

Steve looked at the panel and asked for the recommendations in a row, on the landing page and on About Steven. Ten blocks two across ran the length of the page, and the landing strip showed three and hid seven. Both pages now show the ten in one row that scrolls sideways, each block snapping to the left edge with the next one's edge showing. On the landing page every recommendation lends one sentence, as written; About Steven keeps them in full. The row is a named region with a tab stop, so the arrow keys scroll what a mouse scrolls, the rule the admin tables already follow.

The document did not change: `layoutAuthor` reads a panel whose every block quotes somebody as a row, and nothing else about the panel is special, so a second panel of quotations would get the same shape without a line of code. `author.test` holds the rule and `author.spec` reads the row on the page; `landing.spec` reads the ten sentences and the region.

## Addendum, 2026-10-16 (1.0.3.140): The Shed

The Shed is a second app in this repository: a file and folder browser built in the same working method, with its own site, its own deploy and its own decision records. Until now nothing on TheYard pointed to it, so a visitor found it only through the repository. It now has a section of its own under How it is built, with two links out: the running app and its code. The landing page draws it as a tile like every section, and because the section holds only links, the tile opens the app in a new tab. The sample's folder moved from its first working name to `samples/theshed`, the name it goes by everywhere else, and its documents now describe it as a developer test project without naming a company.

```live path=src/library/sections.ts region=shed-section
```

## Addendum, 2026-10-17 (1.0.3.152): the column on the right, and the words served from one file

Steve looked at the row and asked for two things. The recommendations should stand in a column on the right of the landing page, with everything else on the left, so a reader who came for the inventory or the resume has them in view the whole way down; a row under the figures was the easiest thing on the page to scroll past. And the words should come from the API in one consistent shape that is easy to add to and adjust, even while the values are fixed.

The words now live in one file, `data/recommendations.json`, beside the dataset and the gate's results: one entry per person in the order they are shown, every entry the same six fields (name, title, relationship, when, paragraphs, pull), and nothing reworded. Adding a recommendation is adding one entry and moving the count the site states (ten, which `RecommendationsTests` and the About Steven document hold); the shape, the reader and the pages do not change. `GET /api/recommendations` answers the file as it is, snake_case on the wire like every other answer, and is in the API document with the rest. The shape is written once, in `Recommendations.cs`, beside the check that says what an entry has to get right; the endpoint answers a failure that names the slip when an entry is out of shape, and `RecommendationsTests` reads the same check in the gate, so a shipped build never gets that far.

```live path=api/TheYard.Api/Recommendations.cs region=recommendation-shape
```

```live path=api/TheYard.Api/Features/Docs/RecommendationEndpoints.cs region=recommendations-route
```

On the landing page the column stands to the right from 900 px of the page's own box (about 1,220 px of screen with the rail open) and runs as far down the page as there are quotations, so a reader scrolling the left side passes every one of them. The left column lays itself out by its own width, which is 320 px and a gap narrower than the page: the featured tiles go two across from 640 px of column, the figures four across from 720 and the groups from 760, so a desk at 1440 with the rail open reads four across beside the quotes and a desk at 1280 reads the figures two by two and the groups three across, the trade the column is worth. Narrower than two columns it follows the featured tiles as one block that scrolls inside itself. The column reads the endpoint when the page opens, hinted from the head with the landing page's other reads (ADR: The landing page rendered at build time, server rendering as the goal), and draws ten blank places until the answer arrives, so nothing on the page moves when it does; an answer that does not come is said in words. The TypeScript copy of the words is gone with this version; `AuthorPageTests` held About Steven to the file until that page read the endpoint too, which the next version is.

```live path=src/lib/recommendations.ts region=recommendations-from
```

## Addendum, 2026-10-17 (1.0.3.153): About Steven's column

The same two asks, on About Steven: the recommendations stand in a column on the right of every other panel, in full, and they come from the endpoint rather than from the document. The document keeps the panel's heading and its one sentence, moved up to follow who he is, and quotes nobody: `layoutAuthor` stands the panel headed What colleagues say aside with a place in it, and the document window fills that place from `/api/recommendations` once the page is in it, through the same component the landing page draws. One file, one endpoint, one component; the two pages cannot quote different words, and a recommendation added to the file appears in both.

```live path=src/lib/author.ts region=said-aside
```

The order is the document's: who he is, what colleagues say, then the rest. One column reads it that way, and so does the keyboard and a screen reader at every width. From 1280 px of screen, the wide-desk step of the site's width scale, the Author page's window is wider than before (1280 px where there is room) and the three are placed in the page's grid, which moves the column to the right of the others without moving a word in the document. The row that 1.0.3.136 introduced is gone with its rule; `author.test` holds the aside, `author.spec` reads the column on both widths, and `AuthorPageTests` holds the document to a heading and a sentence with no quotation in it.

## Addendum, 2026-10-17 (1.0.3.154): one scroll bar, and all of them

Steve read the live column and asked for two things in one breath: one scroll bar on the screen, and every recommendation shown, all the way down the page. They are the same ask. The column had been a box with a height and a scroll of its own, which put a second bar beside the page's own and hid six of the ten behind it.

The box is gone. On both pages the quotations are part of the page: one under the next, as far down as there are quotations, with the page's own bar the only one on the screen. Beside the left column they fill the grid row that spans the page, so a reader scrolling the left side passes every one of them; in one column they stand after the introduction and the reader scrolls through them on the way to the rest. The region keeps its name, for a reader moving by landmark, and loses the tab stop it carried: a box with no scroll of its own needs none, and the rule that gave it one is for boxes that scroll. `landing.spec` and `author.spec` read the column's own scroll as zero in both directions at both widths, so a box that starts scrolling again fails the build.

## Addendum, 2026-10-17 (1.0.3.155): whole, on both pages

Steve read the live column again and asked for the link under it to go and for every review to show. One ask: the link existed to send a reader somewhere else for the words the page was holding back, and a reader who has to click is a reader who does not read them.

So there is one way to draw a recommendation now, on the landing page and on About Steven alike: whole, every paragraph the writer wrote, under their name, their title, how they worked with him and when. The component takes no argument for it and the page offers no button under it. The `pull` sentence stays in the file and in the endpoint's answer, because it is the writer's own sentence and a short place may want one; no page quotes it today, and the file's shape check still holds it to being a sentence of the paragraphs it sits with, so it cannot drift from them while it waits.

## Addendum, 2026-10-17 (1.0.3.156): the name opens the page

Steve asked for each person who wrote a recommendation to be a link to their LinkedIn, which is the thing that makes a quotation checkable: a reader who wonders whether these are real people can go and see one.

The file gains a seventh field, `profile`, beside the title, and the endpoint answers it with the rest. The ten addresses were read from his own recommendations page in his browser, signed in, rather than written from memory, and the gate holds each one to being a page under `linkedin.com/in/` and to being one page per writer, so a wrong or shared address fails the build rather than the reader. The name is drawn as the link, in the heading's colour with its underline under the pointer and under the keyboard, so a column of ten names does not read as a column of ten links; each opens in a tab of its own.

## Addendum, 2026-10-17 (1.0.3.158): its own section, and a page that does not lean

Four asks in one reading of the live column. What colleagues say should be the link, with no paragraph under it. The column's own contents should sit centred in the area they are given, with nothing indenting them from the left. And something of his should stand under the quotations, so the page comes out square rather than one long column beside a short one.

The heading on both pages is now the link to the page the recommendations were written on, and the line of prose that used to carry that address is gone: a reader who wants to check them wants the address, not a sentence about it. The claim that sentence made is kept where it is held rather than where it is asserted, by the file's shape check and by `AuthorPageTests`, which holds the document to that one heading and to naming nobody.

Under the quotations stand Freya and Vincent, his two rabbits, from the same photographs About Steven serves and in the same gold frame, one under the next. They are named in the component by the names the photo manifest knows them under, so a picture reaches the first screen the way it reaches the Author page and an address typed into a page cannot put one there. The column was about half the height of the left side before them; it is about the same height now.

And the column is a section of its own, on the glass, the way About Steven's panel is, rather than a heading with cards loose beside the page. Its width is a share rather than a fixed 320 px: the left column takes a little over half and the quotations a little under, which at 1440 with the rail open measures about 630 and 470. A fixed 320 beside a 780 px column read as a page with a margin down the side of it, which is what he meant by one column dominating. The evidence strip still goes four across beside it, from 600 px of its own column rather than 720.

## Addendum, 2026-10-18 (1.0.3.162): the rabbits leave the home screen

Steve read the landing page on his phone and said the rabbits should not be on the home screen. The first screen is where a recruiter or a hiring manager decides whether to keep reading, so it carries the work and what colleagues say about it. Freya and Vincent stay on About Steven, in the same photographs, where a reader who wants to know the person behind the site goes looking for them.

The paragraph above on the rabbits standing under the quotations stopped being true at this version. At a desk width the quotations column is shorter than the left side again; the first screen comes before an even page. The Pets component is deleted rather than left unused, and the landing test no longer looks for it.

## Addendum, 2026-10-18 (1.0.3.164): three columns

Steve read the left column as three layouts stacked on each other: Inventory and Author side by side, the resume alone across both, then four narrow figures. It now runs on one set of three columns from top to bottom. Inventory, Author and the resume are three equal cards, the badge on top and the words centred under it, the same shape as every other tile on the page and the same height as each other. The evidence strip sits under them on the same three columns with the same gap, and the groups below land on the same columns too, so the right edge of the third card, the third figure and the third tile of the first group are one line.

The figures four across and the groups four across, described in the addendum on the column on the right, stopped being true at this version: a fourth column would put them out of line with the featured row. Beside the quotations the column is narrower than the page, about 550 px on a 1280 desk and 630 px at 1440 with the rail open, so the three columns start from 480 px of column there and from 640 px when the page is one column. Phones keep one featured card per row and the strip two across. The landing test reads the three right edges off the page at a desk width and the stacking at a phone's.

## Addendum, 2026-10-18 (1.0.3.165): the last row

A review of 1.0.3.164 found the three columns had dropped an older rule of the page: no tile sits alone on a group's last row. How it is built has seven tiles, which three across read as three, three and one tile with two empty places beside it. A last tile alone on its row now takes the whole row, on the same two edges as the rows above it; four tiles are still two rows of two. The landing test holds the seventh tile to the row's full width at a desk width.

The same review found that between about 640 and 690 px of window the featured cards stood one per row at their desk size, because their smaller phone shape was keyed to the window while the column count is keyed to the column. The phone shape now applies when the page is one column and the column is under 640 px, the same condition that puts the cards one per row.

## Addendum, 2026-10-21 (1.0.4.11): the workshop, and a drawing in a block

1.0.4.3 put Steve's workshop words and the birdhouse drawing on the server's /about page and nowhere else; the About Steven a recruiter opens from the sidebar, `docs/author/AUTHOR.md`, never had them, and Steve looked for them there. From 1.0.4.11 the Away from the keyboard panel holds a block, The workshop, with the same approved words (the test reads them from `AboutPage.Workshop`, so the two pages cannot drift) and the same drawing. A drawing is not a photograph: it has no cuts and no frame, so `src/lib/author.ts` keeps a short list of the site's own drawings by file name (`AUTHOR_DRAWINGS`), serves each from the site's root at the size it is drawn, and floats it small beside its block's words. Any other picture is still left out. `AuthorPageTests` holds the block's words, the drawing and its alt text, and `author.test.ts` holds the markup.


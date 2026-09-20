# ADR: The Admin tab, as a product

Status: accepted, 2026-09-20. Asked for in one sentence, which is this record's whole specification:
"My goal is to make the admin portal very easy to understand and very graphically impressive, because
when I get a new job I want to advocate the admin portal we have at the new job, to make it easier
to maintain the site statuses."

## Context

The Admin tab grew a card at a time, each one answering the question of the week it was written in:
health, Azure's view of the container, traffic from Application Insights, errors, timing, the SQL the
application ran, what the document store ran, the comparison, the proof, site activity, the kept log,
every page checked, what the machines are doing. Every one of them is correct, and most of them are
paragraphs and tables. A person who built it reads it easily. A person who did not, which is the
person this record is for, meets several paragraphs before the first picture.

The bar is a hiring manager looking at it and wanting one. That reader gives a page a few seconds,
and in those seconds the page has to answer four questions in the order they would be asked at
three in the morning: is it up, is it fast, is it costing anything, what broke.

## What was considered

| Option | What it buys | What it costs |
| --- | --- | --- |
| A component library, Material UI | Cards, tiles, icons and charts that look finished on day one | Priced before it was decided: about 300 KB on the wire for a site whose own script is about 100 KB compressed, and a contradiction of the decision the README states in its first paragraph on the stack, no component, icon, state or CSS libraries |
| A hosted dashboard, Azure Monitor workbooks or Grafana | Real charting, alerting, history without writing any | Not on the site: a reader would have to be given an Azure sign-in to see it, and the thing being advocated is a page a team can open, not a portal they need a role in |
| **The site's own CSS over the token sheet, and its own chart arithmetic** | Every number on the page comes from the thing it is about, the page stays inside the bundle it has, and the whole of it is readable source a team could lift | Every chart is drawn here, by hand, and has to be tested here |

**Decision: our own CSS over `src/styles/tokens.css`, and charts drawn by `src/lib/machineChart.ts`,
which imports nothing from React and is tested on its own.** Steve chose it over Material UI with the
price in front of him.

## The rules the charts follow

They are the rules the machine charts already followed (ADR: What the machines are doing), written
down because there are now enough charts for them to be a system.

- **One question a chart.** How busy, how fast, what failed, what it cost. A chart that answers two
  questions in two units is two charts.
- **One frame.** Every chart is the same 720 by 170 drawing with the same axis, the same three
  labels along the bottom and the same legend under it, so the eye learns it once.
- **A gap is a gap and a zero is a zero.** A stretch nobody measured breaks the line. A measured
  minute in which nobody asked for anything is zero requests, and has no median, because there is no
  median of nothing. The arithmetic that decides which is which is one function a window, tested,
  and the hour and the month go through the same one.
- **A percentage keeps a full axis.** A quiet hour drawn against its own maximum looks like a busy
  one, which is a chart lying with true numbers.
- **Colour means something or it means position.** Where a line is good news or bad news it takes
  the status colours the rest of the site already uses; where it is one store against the other it
  takes the two store colours; otherwise it takes its place in the legend.
- **A reading with no history behind it keeps its table.** The health checks, Azure's view, the SQL
  the application ran: these are lists of facts about now, and a line through them would be
  decoration. They stay tables.

## What is drawn, as of this record

**Traffic**, a new card above the machines: requests a minute, the median and the ninety-fifth in
milliseconds, and answers that were 5xx and 4xx, a minute at a time. The hour is the request ring;
the day, the week and the month are the minutes each site keeps (ADR: What the machines are doing,
the addendum on the windows), which is why a kept minute carries its traffic. One window is chosen
once, on this card, and the machines card under it follows, because the two are only worth reading
side by side over the same stretch.

**The proof**, as pairs of bars over its table: each path's two medians, each bar a share of the
longest median on the card. The table says most pairs are the same and the rest differ by a round
trip; the bars say it before anybody reads a number.

**Request units a minute** and **the machines** were already drawn, and **unique visitors a day** is
the graph whose shape the rest were matched to.

## What it costs

Nothing on the bill and nothing on the wire that is not this site's own. No dependency was added.

## Addendum, 2026-09-20 (1.0.0.161): the look

The charts were the second ship and this is the third: the page now opens on the four questions this
record started from, and everything under them is filed under the question it answers.

**A strip of tiles across the top.** Eight tiles, each a label, one number, a line saying what the
number is a number of, and where there is an hour behind the number, the hour drawn under it as a
line with no axis. Version, health and pages answer "is it up?"; the slowest ninety-fifth of the last
hour and today's visitors answer "is it fast?"; memory against its limit and the request units
charged answer "is it costing anything?"; errors answer "what broke?". A tile is a button, and it
goes to its question.

**The tiles ask the server for nothing.** They are made of what the cards below have already read, so
a tile and the card it points at cannot disagree, and the strip added no endpoint and no request. A
reading that has not arrived is a tile that says "waiting", not a zero.

**What makes a tile amber or red is a rule somebody can read.** `src/lib/statTiles.ts` has no React
in it, and the thresholds are constants with names:

| Tile | Amber, worth a look | Red, needs attention |
| --- | --- | --- |
| Health | the site calls itself healthy while a check fails, which is the fallback serving | the site does not call itself healthy |
| Pages | the sweep checked nothing | any address is down |
| Slowest 95th | 1,000 ms or more in any minute of the hour | 3,000 ms or more |
| Memory | four fifths of the limit | nineteen twentieths of it |
| Errors | | anything answered 5xx in the hour, or anything reported |

They are this site's own, read off what it measures on a quiet day, and they are there to be argued
with. The tone is written on the tile as a word, "fine", "worth a look", "needs attention", as well
as a colour, because a colour alone says nothing to somebody who cannot see it.

**Four questions, as headings, with the cards under them.** Is it up: application health, Azure's
view, every page checked. Is it fast: traffic, the last hour as Application Insights recorded it,
timing, the two backends side by side, the proof. Is it costing anything: the machines, the partition
key, what each store ran. What broke: recent errors, the log, the kept log. The operator's desk,
which is site activity, the key and the reset link, comes last, because it is where somebody works
and the rest is where somebody looks. No card was removed and no number moved to a different card.

**The paragraph on each card is one tap away.** Twelve cards opened with a paragraph on what the card
is and how to read it. They are all still there, word for word, inside a closed "What this shows" on
the card, so the first thing on a card is its numbers. On a phone that was most of the scrolling.

**All of it is the token sheet.** The tiles, the headings and the soft shadow every card now carries
are `AdminPanel.module.css` over `src/styles/tokens.css`: the surfaces, the three status colours and
their soft grounds, the accent, the radius and the two shadows were already there, and no token was
added or changed. No dependency was added either.

**Checked at 375 px.** The strip is two tiles to a row on a phone and four from 720 px. The browser
suite opens the tab on a 375 by 812 screen and holds that the first two tiles sit side by side at the
same size, that the third is under the first, and that nothing on the tab makes the page wider than
the phone: charts and tables scroll inside their cards.

**What is still small on a phone** is the writing on the charts. A chart is a 720 wide drawing, and in
a 320 wide card its axis labels are drawn at under half size. The lines read and the labels do not,
and that is the next thing to fix on this tab, said here so that nobody finds it before the record
does.

**What it cost on the wire**, from the build's own output: the script went from 101.15 kB compressed
to 103.30 kB and the stylesheet from 8.74 kB to 9.26 kB.

## Addendum, 2026-09-20 (1.0.0.163): the first amber tile, investigated

The strip had been live for eight minutes when a tile went amber: "Slowest 95th, 1212 ms, worth a
look". Steve's instruction was that anything the dashboard marks is investigated, which is the
point of marking it. This is what the tile led to, in the order it was found.

**Where.** The machines endpoint's traffic, a minute at a time, on both sites: the slow minutes were
12:30 and 12:32 UTC on the SQL site and 12:30, 12:31, 12:36 and 12:37 on the Cosmos DB site. The
processes had started at 12:30 and 12:32: the roll of 1.0.0.161. The later pair had no roll
behind them.

**What.** The timing card's rows by path, same read: `/api/vehicles` at a median of 278 ms and a
ninety-fifth of 779 ms, 1,059 ms at worst, server-side, against 0 to 31 ms for every other API path.
One `/index.html` at 1,212 ms was the first request a cold process served.

**Why.** `InventoryService.Search` sorted every match and then took the page. An unfiltered listing
matches all 100,000 vehicles, so every landing page was a full stable sort of 100,000 rows to show
100. On a container group's own core that read 51 ms and nobody looked again. On the plan it shares
one core with the other site (ADR: One plan, two sites), it read 122 to 160 ms warm, and in the
minutes after a roll, with two processes starting on that core, it passed a second.

**The fix** is three lines in one method: count the matches, then `Skip` and `Take` straight off
the ordering, which lets the runtime sort only as far as the page asked for. The rows and their order
are the same, ties included, because the ordering is still the stable one, and
`A_page_is_the_rows_a_full_sort_would_have_given_ties_included` holds that for every sort and six
pages over forty vehicles at four prices. What it reads on the plan afterwards is measured after the
roll and goes in the changelog line that follows this one, not guessed at here.

**What the tile learned.** A tile that says "slow" and not "when" sends somebody through an hour of
rows, so the tile now names the minute: "74 requests in the last hour, slowest at 07:32". The
threshold did not move. A ninety-fifth over the ten requests of a quiet minute is that minute's
slowest request, which makes the tile quick to go amber after a roll, and that is left as it is on
purpose: it went amber over something real the first time it was looked at.

## Files

- [`src/lib/machineChart.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/machineChart.ts): the arithmetic for every chart on the tab, React-free: axes, paths with their gaps, the kept windows' timelines, traffic as slots, and the proof's bars.
- [`src/lib/machineChart.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/machineChart.test.ts): what a gap is, what a zero is, and what a bar is a share of.
- [`src/lib/statTiles.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/statTiles.ts): what each tile says and what makes it amber or red, React-free, and the line under a tile as the points of a polyline.
- [`src/lib/statTiles.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/statTiles.test.ts): every threshold, a reading that has not arrived, and a gap in the line.
- [`api/TheYard.Application/InventoryService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/InventoryService.cs): the page sorted only as far as the page, which is what the first amber tile found.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the traffic card, the window both cards share, and the proof's bars.
- [`src/components/AdminPanel.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.module.css): the tab's styles, over the token sheet.
- [`api/TheYard.Api/Machines.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Machines.cs): the request ring folded into minutes, once, for the hour on the card and for the minute that is kept.
- [`docs/ADR-078-what-the-machines-are-doing.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-078-what-the-machines-are-doing.md): the first charts, and the windows these are drawn over.

```live path=src/lib/machineChart.ts region=traffic
```

```live path=api/TheYard.Api/Machines.cs region=traffic-minutes
```

```live path=src/lib/statTiles.ts region=tile-rules
```

```live path=api/TheYard.Application/InventoryService.cs region=page
```

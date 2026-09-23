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
| Typical answer | the hour's ninety-fifth is 1,000 ms or more, over twenty requests at least | 3,000 ms or more |
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

## Addendum, 2026-09-20 (1.0.0.165): one window for every chart

Steve, with the tab open: "make sure on all graphs you can choose between 30 day, 7 days and 24 hours,
we should go back 30 day min on all charts."

Every chart of something over time already could: the three traffic charts and the three machine
charts read the last hour, 24 hours, 7 days or 30 days from the minutes each site keeps for thirty-one
days, and the visitors graph reads 24 hours, 7 days or 30 days from counters kept for thirty-five.
What he found is that the page did not say so where he was looking. 1.0.0.161 had filed the traffic
card under "is it fast" and the machines card under "is it costing anything", a long scroll apart,
and left the buttons on the first with a sentence on the second saying to go and find them. And the
lines under the tiles were the last hour and nothing else.

**One window, and its buttons wherever a chart is:** over the tiles, on the traffic card and on the
machines card. They are one piece of state, so pressing a row presses all three, and the browser
suite holds that. The lines under the tiles follow it: over a kept window they are drawn from the
same buckets the charts are, one value a bucket, with a gap where the store holds nothing, by
`keptSparks`, which is tested on its own. A sentence beside the buttons says what the lines are lines
of, and that the number over a line is still now, because a line with no axis says nothing about its
own width. A change of window no longer sends the tiles back to "waiting": they are made of the last
answer that arrived.

**The chosen button looks chosen.** Every row of window buttons on the tab marked its choice with
`aria-pressed` and with nothing a sighted reader could see, which a picture of the new row taken by
the precheck showed at once. The pressed button is now filled with the accent colour, from the token
sheet, on every row on the tab.

**What is not on the window, and why.** The proof's bars are one run and not a stretch of time. The
visitors graph keeps its own three buttons, because its shortest window is a day and its card is the
operator's desk. And thirty days is what the page offers, not yet what the store holds: the minutes
began to be kept at 1.0.0.159, this morning, so a month drawn today is a gap with one day at the end
of it, which is what the charts draw and what the card's sentence counts.

## Addendum, 2026-09-20 (1.0.0.166): where a window starts, and an alarm that fired on every deploy

Two things the live tab showed an hour after 1.0.0.165, looked at in a headless browser from Steve's
machine and not asked about.

**A month drawn whole looked broken.** The minutes began to be kept this morning, so Last 30 days
held two four-hour buckets of 180, and the charts and the lines under the tiles drew them as one
sliver at the right-hand edge of an empty frame. It was correct, by the rule this record set, a gap
is a gap, and it read as a chart that had failed to load. The rule was too wide. A gap after the
first reading is the site not reporting, and that is information. The emptiness before the record
began is not a gap in anything, and drawing it tells nobody anything for the next twenty-nine days.
So a kept window is still asked for whole and still counted whole, "2 of 180 buckets hold a
reading", but the drawing starts at the first reading (`fromFirstReading`), never at fewer than a
dozen slots, and the sentence over the charts says so and gives the time. Every later gap is drawn.

**The speed tile went amber after every roll.** It went amber over something real the first time
(the addendum above on the first amber tile), and then again after 1.0.0.164 and 1.0.0.165, each
time naming the minute the process started: a cold process answers its first request in a second or
more, and a ninety-fifth over a quiet minute is that minute's slowest request. An alarm that fires
on every deploy teaches people to look past it, which is worse than no alarm. The tile now reads the
hour without the first three minutes after a start (`afterColdStart`, and the three is a named
constant), and says on its face that it did: "slowest at 07:41; the start at 07:30 is left out".
Nothing is hidden: every request and every error in those minutes is still counted on the tile, the
line under it still draws them, and so does the traffic card. When the process started is its newest
sample's time less its uptime, both from the one answer.

**Corrected the same morning, 1.0.0.167.** The look that found these two was run again after the
roll, and found the fix's own sentence wrong: with a dozen four-hour slots kept, a month's drawing
starts two days back, and the page called that time "its first reading", two days before anything was
kept. The sentence now gives the first reading and the start of the drawing as the two things they
are. The same look found a site whose only requests in the hour were its own cold start reading
"quiet" beside "67 requests"; it reads "warming". Both were true defects of a fix, live for twenty
minutes, and both were found by looking at the page and not by a test, which is the argument for
looking at the page.

## Addendum, 2026-09-21 (1.0.0.168): the traffic card in plain words, and a colour that raised an alarm

Steve, reading the tab as the person it is for: "The traffic section of the Admin tab is not clear to
me, what it is doing." And on the first drawing of a plainer one: "You have red for the slowest
response on how fast did it answer, and that indicates something is wrong; that colour raises a
warning while scanning."

**The colour rule, restated, with the defect named.** This record said "colour means something or it
means position", and then let the traffic card break it: the median was drawn in the success colour
and the ninety-fifth in the warning colour, as though one were the good line and the other the bad
one. They are two series. A slow-requests line is slow by definition, on a good day and a bad one,
so a warning colour on it is an alarm that never stops, and the eye of somebody scanning the page
lands on it every time. The same chart family had the second half of the defect: a turned-away
request, a wrong address or a visitor who is not signed in, was drawn in the warning colour, and
nothing is wrong with the site when it turns one away. So the rule, in the form the code now holds:

- **A series takes an identity colour.** On the traffic card those are two tokens of their own,
  `--color-series-1` and `--color-series-2`, which today hold the accent's value and the heading's:
  neither a store's colour nor a status (ADR: The palette, the addendum on the series colours).
- **The status colours are for states.** A tile, a pill, a row of the kept log: things that are
  fine, worth a look, or wrong.
- **The one state a line can be is a server error**, which is something wrong whenever it is above
  zero, so that line alone takes the danger colour.
- **The alarm for slow answers lives on the speed tile**, which goes amber at 1,000 ms and red at
  3,000 and knows about a cold start. The chart draws the series and leaves the judgement there.

`MachineChart` lost its "good" and "warn" tones, so the defect cannot be written again by passing a
prop, and the browser suite reads the stroke of every line on the card and holds that none of them
is a status colour except the server errors line, which has to be.

**Every other chart on the tab was checked for the same defect, and none has it.** The three machine
charts over the hour and the three over a kept window pass no tones and take their colours by
position. The visitors graph and the proof's bars take the two store colours, which is what they
are comparing. The line under a tile takes the tile's tone, and a tile's tone is a state. The kept
log's rows keep the warning and danger colours on their edge, because a warning there is a warning.

**"Did anything fail?" comes first, and zero is said in words.** It was an untitled chart with a
legend reading "Answered 5xx (a minute)". It is now the first section on the card: the question as
its title, one sentence saying that a server error is the site's fault and should be zero and that
a turned-away request is the visitor's, a legend with the plain word first and the term in brackets,
and "errors / min" on the axis. Most hours that chart is a flat line at zero, and a flat line reads
as a chart that did not load. So the card says it: "No server errors in the last hour", beside the
same good pill the health card uses. When there are any, the sentence counts them and the pill says
so.

**Four numbers in place of one sentence.** "33 requests in the last hour, 0 answered 5xx and 7
answered 4xx, and the slowest stretch answered its ninety-fifth in 139 ms" was correct and written
for the person who wrote it. The card now opens on four blocks that wear the tiles' look, two to a
row on a phone: requests; the typical answer, "half of requests were faster"; slow answers, "19 in
20 requests beat this"; and server errors, with the turned-away count on its detail line. A block
takes a tone only for a state: no server errors is good and any is bad, and an answer under the
speed tile's own amber line is called fast. Over that line the block says nothing, because the
warning is the tile's to give and this block does not know about a cold start.

**One number is new, and it says what it is.** The slots the charts are drawn from hold a median a
minute, or a median a bucket over a kept window, and a median of every request in the window cannot
be made from those. The typical answer is the middle one of the medians the slots hold, by nearest
rank, and its detail line says "in the typical minute", or "stretch" over a kept window. Requests,
server errors, turned away and the slowest ninety-fifth are the numbers the sentence carried, from
the same function. No endpoint changed and no C# did.

**A question a chart, as its title.** "How busy is it?", requests per minute, with the sentence that
a flat line at zero means the site was up and nobody asked. "How fast does it answer?",
milliseconds, lower is better, "Typical request (median)" and "Slow requests (95th percentile)",
and one sentence on what 19 in 20 means. Every axis on the card carries its unit. The sentence that
a record is younger than its window is still under the blocks, word for word, and the folded "What
this shows" is unchanged.

**Where the words live.** `src/lib/trafficCard.ts` has no React in it and decides what the four
blocks say, the three titles and their sentences, and the sentence over the fail chart, and Vitest
holds them, including that no block's words contain a status code or a percentile.

## Addendum, 2026-09-21 (1.0.0.169): the tab in the new look

The Admin tab moved with the rest of the site to teal, dark green and gold, and to glass panels over
a soft watermark (ADR: The glass look; ADR: The palette, the addendum on teal, dark green and gold).
What that changed on this tab, and what it did not:

- **The rules this record set did not move.** One question a chart, one frame, a gap is a gap, and
  colour means something or it means position. The series are dark green, bright teal and a neutral
  grey, in that order on every chart, and the status colours are still only states. A turned-away
  request is the third series' grey.
- **Three tiles carry a ring**: health, pages and memory, because each of those numbers is a share
  of a known whole. The speed tile and the request units tile carry none, because milliseconds and
  request units have no whole, and a ring drawn for looks is a gauge that measures nothing. Which
  tiles have one is decided in `statTiles.ts` with the rest of the tile rules, and tested there.
- **A plain tile is the deep teal and never the accent teal**, which sits 1.09 from the status
  green. Plain, fine, worth a look and needs attention read as four different things.
- **A chart has a fine grid and a readout**: a rule at the slot under the pointer, the slot's time,
  and each line's reading in the unit of the axis, or "not measured" where a gap is. The arithmetic
  and the words are in `machineChart.ts` and tested there. On a phone the writing on a chart is
  still small, as the addendum on the look said it was, and the readout is a desk feature in
  practice.
- **The unit on an axis keeps a halo of the card's ground** and is drawn over the lines, because the
  live look after 1.0.0.168 showed a line that starts at the top of the axis passing through the
  word.

## Addendum, 2026-09-22 (1.0.1.4): red needs a busy minute

Steve saw "Slowest 95th: 6355 ms" and "needs attention" on a morning nothing was wrong, and asked for it to be checked against the last 30 days: "it maybe a deployment or wake up blip but we need to make sure those blips don't raise a alarm". The kept minutes only reach back to 2026-09-20, about 50 hours, so that is what was read, on both sites, at five-minute grain for the last 24 hours. Nine readings were red. None was a restart: the sampler kept every minute and the uptime never reset. Seven were quiet stretches after an idle spell, 10 to 28 requests in five minutes and no database work at all, and the 6355 ms was one of them. Two came within a minute of a ship commit, under a couple of hundred requests; their cause was not found.

The tile's number is the worst minute's ninety-fifth, and over a handful of requests a ninety-fifth is that minute's one slowest request. So **red now needs a minute of at least 20 requests (`RED_NEEDS_REQUESTS`)**; a slower minute with fewer is amber at most, the number still shows, and the line under it says the minute had too few requests to call red. Over the same 24 hours that leaves 2 of the 9 red. The three minutes after a start are still left out (the cold-start region), and every request and error in the hour is still counted.

## Addendum, 2026-09-23 (1.0.3.4): the typical answer, and the ninety-fifth beside it

Steve, the next morning: "is it fast keeps showing it's slow and either the snippet is off or something, should we be going off the average instead of the 95th?" Measured before anything changed, over the plan's first three days (2026-09-20 to 09-23): every request the two sites answered from Application Insights, about 30,000; the kept minutes, which are what the tile reads; and 48 process starts read off the after-ship logs plus one platform restart on the 23rd. The tile was sampling wrong in three ways, and the site has a small real tail.

**The tile headlined the slowest minute of the hour.** Its number was the largest of sixty minutes' ninety-fifths, so one slow request coloured the tile for the sixty minutes that minute stayed in the hour. On the SQL site 1.5 per cent of minutes had a ninety-fifth of a second or more, and the tile read amber 45 per cent of the time (Cosmos DB: 1.1 against 35). **Starts were most of the slow requests.** Of the 131 visitor requests over a second on the SQL site, 98 fell in the first three minutes of a process and 15 in minutes three to ten; a roll is a cold catalogue. **And the Admin tab's own reads were the slowest routes on the site**: 751 of the 882 requests over a second were `/api/admin/azure`, `experiment`, `activity`, `telemetry` and `/api/health`, which the ring already leaves out but the request log does not, and which no visitor makes. The real tail is `/api/vehicles` when warm: 19 of 808 calls over a second on SQL (median 70 ms, ninety-fifth 594), 16 of 123 on Cosmos DB; none of them waited on a database, and each coincided with other requests on the one B1 core the two sites share.

So the tile reads the hour the way a visitor felt it. **The number is the hour's median over every request in its warm minutes, and the ninety-fifth stands beside it**, both from the durations each traffic minute now carries sorted (`hourTiming`), because a percentile of an hour cannot be had from sixty minutes' percentiles. **Under twenty requests the hour reads "quiet" and shows the count with both numbers** (`QUIET_BELOW_REQUESTS`, which replaces the busy-minute rule above: over fewer, a ninety-fifth is one request's time). Amber is an hour whose ninety-fifth is a second or more, red three seconds or more, and neither needs the extra rule now, because the floor is on the hour rather than the minute. The three minutes after a start are still left out and still said. Replayed over the same three days the tile reads good 48 per cent of the time on the SQL site and quiet the rest, amber under 1 per cent on either site, red never. The line under the tile is the median a minute, the same reading as the number over it; the traffic card keeps drawing every request, the slow line included.

## Files

- [`src/lib/machineChart.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/machineChart.ts): the arithmetic for every chart on the tab, React-free: axes, paths with their gaps, the kept windows' timelines, traffic as slots, and the proof's bars.
- [`src/lib/machineChart.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/machineChart.test.ts): what a gap is, what a zero is, and what a bar is a share of.
- [`src/lib/statTiles.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/statTiles.ts): what each tile says and what makes it amber or red, React-free, and the line under a tile as the points of a polyline.
- [`src/lib/statTiles.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/statTiles.test.ts): every threshold, a reading that has not arrived, and a gap in the line.
- [`api/TheYard.Application/InventoryService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/InventoryService.cs): the page sorted only as far as the page, which is what the first amber tile found.
- [`src/lib/trafficCard.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/trafficCard.ts): what the traffic card says, React-free: the four blocks, the three questions, and zero said in words.
- [`src/lib/trafficCard.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/trafficCard.test.ts): the words, the one new number, and what makes a block good or bad.
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

```live path=src/lib/statTiles.ts region=hour-timing
```

```live path=src/lib/trafficCard.ts region=traffic-words
```

```live path=api/TheYard.Application/InventoryService.cs region=page
```

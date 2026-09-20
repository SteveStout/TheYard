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

## Files

- [`src/lib/machineChart.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/machineChart.ts): the arithmetic for every chart on the tab, React-free: axes, paths with their gaps, the kept windows' timelines, traffic as slots, and the proof's bars.
- [`src/lib/machineChart.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/machineChart.test.ts): what a gap is, what a zero is, and what a bar is a share of.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the traffic card, the window both cards share, and the proof's bars.
- [`src/components/AdminPanel.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.module.css): the tab's styles, over the token sheet.
- [`api/TheYard.Api/Machines.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Machines.cs): the request ring folded into minutes, once, for the hour on the card and for the minute that is kept.
- [`docs/ADR-078-what-the-machines-are-doing.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-078-what-the-machines-are-doing.md): the first charts, and the windows these are drawn over.

```live path=src/lib/machineChart.ts region=traffic
```

```live path=api/TheYard.Api/Machines.cs region=traffic-minutes
```

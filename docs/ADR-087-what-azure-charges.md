# ADR: What Azure charges

Status: accepted, 2026-09-30, shipped as 1.0.3.55. Steve asked for the portal's subscription overview on the Admin tab, in public: "this is nothing to hide from the public". A reader who sees how fast the site answers should see what it costs to run in the same place.

## Context

The Azure portal opens a subscription on four cards: the spending rate with a forecast, the cost by resource, the top resource types, and Defender coverage. The Admin tab already showed the milliseconds (the traffic, timing and machines cards) and the request units the document store charged, and the dollar figure lived only in the portal and on the Performance page, where it was typed from a price list on 20 September.

Read live on 30 September, with the subscription just renamed Stout-PayAsYouGo and its reactivation finished at about 15:30 CDT:

| Reading | Value | Where it was read |
| --- | --- | --- |
| Month to date | $47.18, every cent of it in RG-THEYARD-SS | Cost Management, grouped by resource group, 16:09 CDT |
| Forecast for September | $49.03: $45.85 billed through 28 September and $3.18 forecast for the last two days | Cost Management's forecast, 16:09 CDT, the same figure the portal showed at 15:36 |
| Rows for 35 days by resource | 198, one page, four columns: Cost, UsageDate, ResourceId, Currency | Cost Management, 16:09 CDT |
| Newest day | 30 September, $0.48 so far | the same read |

The month to date is more than the $22.38 list price on the Performance page because the two container groups the plan replaced ran until 20 September, and September's bill carries them.

## Decision

The first three portal cards, on one workbench card called What Azure charges, under "Is it costing anything?": the spend over the window as a running total with Azure's forecast dashed to the month's end, the cost by resource as a donut that names the top four and folds the rest into Others, and the resources by type as bars. Public, like the rest of the tab, with no operator key. Defender coverage is left out, because it describes the subscription's security and this card is about its bill.

- **Who asks.** The site's own identity, `id-theyard-ss`, the one that already reads Application Insights (ADR-024) and its own resource. It asks for a management token the same way, from whichever door the host has, and holds no key (ADR-072). It needed one role, Cost Management Reader on the subscription, which is free and adds nothing to the bill; Steve assigned it on 30 September and the probe read it back at 16:09.
- **When it asks.** Once an hour per site, from a background recorder, never on a request. Cost Management is rate limited and reports a day eight to twenty four hours late, so asking on every open of the card would spend the allowance on the same answer. The first read is half a minute after the site starts, so a roll costs one read and nothing else.
- **What it asks.** Two calls. The actual cost, daily, grouped by resource, for the last thirty-five days: one call answers the line and the donut, and thirty-five days is the month window and five more, because Azure revises a day after it ends. And the forecast for the month, which answers with the days Azure counts as billed and the days it forecasts; added up they are the month's forecast, the portal's own figure, and the days to come after the newest reported day are the dashed line. The resource types are read off the resource paths in the first answer, so the bars need no second role and no third call.
- **What leaves the process.** A resource's name and type, a day and a cost. The resource path carries the subscription's id, so it is cut down in the one method that reads the answer and never reaches the wire. A refusal is named for what it means (no role yet, slow down, the billing account is still being set up) and never quotes the service's own error, which names the scope it refused.
- **Where the read is held.** In memory, the last read only. The handoff for this change proposed a Cosmos DB container. Azure Cost Management is already the record of the bill and keeps it for the life of the subscription, so a copy here would be a second truth that could only drift from the first; the repository's first rule is to derive rather than store. What a roll loses is an hour's read, taken again thirty seconds later. The port the recorder writes through is the same shape a store would take, so a kept history is one adapter away if a year of bills is ever wanted on the card.
- **The window.** The same buttons as every other chart on the tab (ADR: The Admin tab, as a product). Azure bills by the day, so the day is the smallest grain: the last day is two days (yesterday whole, today as far as Azure has reported it), a week is seven and a month thirty, and the hour the other charts offer is shown as the last day, with a sentence that says so. The forecast is drawn on the month window, where its end is inside the chart.
- **The newest day.** Marked as still being added to while it is today or yesterday, on the line and in a sentence, because a total that is still moving should look like one.
- **An absent reading.** A sentence, never $0.00: off Azure ("a local run reads nothing"), before the first read, and without the role. A card that drew a zero for "no role yet" would be a false statement about the bill.

Money on this card is dollars and cents as a `double`, rounded to the cent. The repository's rule that money is whole dollars as `int` is about the auction, whose dataset prices are whole dollars and whose rules add whole-dollar increments. A bill is counted in cents.

## What holds it

- `CostTests`: a fixed Cost Management answer, with its columns out of the order the code asks for, becomes the expected days with no subscription path left in them; a nested type reads whole (a database is `microsoft.sql/servers/databases`); the forecast answer keeps each day marked billed or forecast by Azure's own status column; the donut folds past four; the newest day is flagged, a quiet day before it is a zero and a day after it is not drawn; each absent state is a sentence with nothing drawn; a refusal never quotes the scope; the held read is replaced whole; the reader's minute of cache holds; off Azure the recorder asks nobody; and the endpoint answers every window, and a window it does not know as the month.
- `PageStatusTests`: the page sweep reads `/api/admin/costs?window=30d` at every roll, because a card that throws is a page that is down (ADR: Every page, checked at every roll).
- `spend.test.ts`: the hour shown as the day, money as the bill writes it, the headline sentences, the axis's round top, the line turning to dashes where the reported days end, the donut's shares and the bars' lengths.
- `admin.spec.ts`: with an answer in hand, the card draws the line, the donut with its legend, and the bars.

The shaping, the one method a subscription path passes through:

```live path=api/TheYard.Api/Costs.cs region=cost-shape
```

The recorder, which is the only caller of Azure:

```live path=api/TheYard.Api/Costs.cs region=cost-recorder
```

The view the cards are drawn from, and the cache in front of it:

```live path=api/TheYard.Api/CostReport.cs region=cost-view
```

The tests that hold the view:

```live path=api/TheYard.Tests/CostTests.cs region=cost-view-tests
```

## Addendum, 2026-09-30: the first read on the Cosmos DB site

Read live at 17:00 CDT, a minute after 1.0.3.55 rolled, uncached on both domains: the SQL site's card drew September at $47.24 with $49.03 forecast (aci-theyard-ss $21.97, aci-theyard-cosmos-ss $13.11, the registry $4.97, the same figures as the portal), and no answer carried the subscription's id. The Cosmos DB site's card said "the last read did not finish (TaskCanceledException); the next is in an hour": its first read, thirty seconds after the start, ran past the twenty seconds the reader allowed while the plan was still loading that site's catalogue, and the recorder then waited the full hour.

So from 1.0.3.56 the reader allows a minute, and a read that did not finish is tried again in five minutes. A read that went through, or one Azure refused, still waits the hour: a missing role does not appear in five minutes, and a request to slow down is a request to slow down.

```live path=api/TheYard.Api/Costs.cs region=cost-retry
```

## Addendum, 2026-09-30: the bars measure dollars

Steve, on his phone at 19:28 CDT, on the Cosmos DB site's card: "The bar chart is off". The bars measured how many resources of a type were on the bill and were ordered by that count, while each row printed the type's cost on its right. So the longest bar, at the top, was three App Service apps at $0.00, whose bill the plan carries, and a reader saw the biggest bar beside the smallest figure: two measures on one row, and the bar drew the one a bill is not read for.

From 1.0.3.57 a bar is a type's cost, a share of the costliest, ordered by cost; the count is in words beside the name. The apps still show, as an empty bar at $0.00 with "3 resources", which is the true picture: they are on the bill and the plan pays for them. The portal's own card counts resources; this one shows the money and keeps the count, because a reader of this tab came for what it costs.

## Files

- [`api/TheYard.Api/Costs.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Costs.cs): the two questions, the shaping, the reader and the recorder.
- [`api/TheYard.Api/CostReport.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CostReport.cs): the view the cards are drawn from and its cache.
- [`api/TheYard.Application/CostHistory.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/CostHistory.cs): the day, the port and the read held in memory, and the windows.
- [`api/TheYard.Api/Composition/CostRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/CostRegistration.cs): the wiring.
- [`api/TheYard.Api/Endpoints/AdminEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Endpoints/AdminEndpoints.cs): `GET /api/admin/costs`.
- [`src/components/admin/SpendCard/SpendCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/SpendCard/SpendCard.tsx) and [`src/lib/spend.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/spend.ts): the card and the layout it draws from.
- [`api/TheYard.Tests/CostTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CostTests.cs) and [`src/lib/spend.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/spend.test.ts): the tests above.

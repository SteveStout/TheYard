# ADR: People who have visited, counted once

Status: accepted, 2026-10-23, at 1.0.4.41. The first screen and the Admin tab show one figure: the people who have visited this site, all time, each counted once, from the day this version shipped. Scanners, crawlers and the site's own reads are left out. The count is kept as one record per visitor under a keyed hash that does not rotate. The address and the user agent are never stored.

## Reading list

1. [ADR: Site activity, and the line an address does not cross](https://theyard.stevenstout.biz/?doc=adr-activity): the daily token this record could not count with, and the privacy line it keeps.
2. [ADR: One reading for the first screen](https://theyard.stevenstout.biz/?doc=adr-one-reading-for-the-first-screen): why the figure reaches the first screen through `/api/pulse` and nowhere else.
3. [ADR: A request is a query or a command](https://theyard.stevenstout.biz/?doc=adr-a-request-is-a-query-or-a-command): the count is one query with one handler, and the store splits into a reader and a writer.

## In plain words

On 23 October I asked for a count of the people who have visited the site, on the first screen and on the Admin tab, with no time limit. The site could not answer that from what it kept. Each visitor gets a token that changes every day, by design, so that nothing kept can follow a person from one day to the next. The same person on Monday and Tuesday is two tokens, and adding up the days counts them twice.

So the count keeps something new: one record per visitor, under a hash of the address and the browser that does not change from day to day. The hash cannot be turned back into the address. It is keyed with the site's secret, so nobody outside can work it out from an address they already have. The record says the first day the visitor was seen, the last day, how many days, and whether their requests read as a person or a scanner. That is all. A person who changes network or browser counts again, and the record says so rather than hiding it.

## Context

Measured at 1.0.4.40: the activity container keeps one document per visitor token per store per day, and the token is `HMACSHA256(key, day | address)`, cut to sixteen bytes. No field survives the day, so no all-time count of distinct people can be read from it. The Activity card's visitor-days are the only count. The first screen's pulse carried speed, cost and uptime and no visitors. The shared Cosmos DB database holds twenty-five containers, which is its limit, so `CosmosDefinitionTests` refuses a twenty-sixth.

## Decision

1. **The hash.** `VisitorTokens.PersonFor(address, userAgent)` is `HMACSHA256(key, "person|" + address + "|" + userAgent)`, cut to sixteen bytes. It uses the same key as the daily token, a label of its own, and no day. Neither input is kept. A read by the site's own tools carries no hash. The prompt named FNV-1a. I used the keyed HMAC the daily token already uses, because FNV-1a with a key mixed in is not a keyed hash in any sense an attacker respects.
2. **Who is a person.** One rule in Domain, `VisitorKinds.Of` in `VisitorKind.cs` at its root, where every Domain rule sits: the site's own network is self, a visitor whose every request looked like a bot is a scanner, and anyone else is a person. The Activity card's `ActivityWho` now reads the same rule, so the two surfaces cannot disagree. A link-preview fetcher is a bot by its agent and is never counted (ADR-108's line).
3. **Where it is kept.** In the activity container, in a partition of its own named `visitors`, under the kind `visitor-record`, because the database refuses a twenty-sixth container. Each document holds `id` (the hash), `first_seen`, `last_seen`, `days_seen` and `who`, with no time-to-live. The activity queries ask for their own two kinds by name, so they never read these. Without a document store, the records are kept in the process's memory, and the count's source says so.
4. **When it is written.** The activity collector already writes off the request path every five seconds. It hands each batch's visitors on as one sighting per hash per day. A hash already handed on today with the same kind is not handed on again, so a visitor costs one point read and at most one write a day.
5. **How it is read.** One query, `ReadPeopleCountQuery`, whose handler takes the reading side (`IVisitorReader`) and answers all time, new today, new this month, the scanners left out, the day counting began, the last thirty UTC days, and the source. The Admin tab reads it at `/api/admin/visitors/people`. The pulse reads it through the same handler and carries `people_all_time`, `people_new_by_day` and `counted_from`. The store's tally is read at most once a minute.

```live path=api/TheYard.Application/Features/Visitors/ReadPeopleCountHandler.cs region=read-people-count
```

```live path=api/TheYard.Api/Shared/Activity/ActivityCollector.cs region=keep-visitors
```

## What holds it

`VisitorTests`: the kind rule, the hash (the same every day, different for another browser, never the address or the daily token), the collector's one sighting per visitor per day with none for the site itself, the count over a store in memory with today, this month and thirty UTC days, an unread store, and the route's 200 with every field.

```live path=api/TheYard.Tests/VisitorTests.cs region=visitor-tests
```

## What was considered

Counting distinct daily tokens. Rejected: it counts visitor-days, which the Activity card already shows, and calls them people.

Keeping the address, or a hash of it with no key. Rejected by ADR-071 and ADR-072: an address is the one thing this site never keeps. An unkeyed hash of a four-billion-value space can be reversed by trying every value.

A container of its own. Rejected: the shared database is at twenty-five, and the next container would fail to create. That is the failure the nightly card hit on 21 October.

A cookie. Rejected: it would need a consent banner, which the site does not have and does not want, and it counts browsers that keep cookies rather than people.

## Consequences

The count starts at zero on the day this version ships, and every surface says "counting from" that day. A person on two networks or two browsers is two visitors. A shared address behind one browser build, such as an office, is one visitor. Neither error can be fixed without keeping something this site does not keep. The figure is never graded. It is a count, not a target.

## Where it sits

Every ring, each holding its own part. The rule of who is a person is in Domain, so the Activity card and the count read one rule. The count's query and its two ports are in Application, the kept records are an adapter in the document store's ring, and the host's collector writes the sightings it already gathers. Single responsibility holds it: the collector gathers, the store keeps, and the query counts. It cost one point read and at most one write per visitor per day, and a partition in a container already paid for. What would change it: a second database with room for a container, or a consent banner, which would let a cookie count browsers more exactly than a hash of an address can.

## Files

- [`api/TheYard.Domain/Shared/Visitors/VisitorKind.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Shared/Visitors/VisitorKind.cs) - who a visitor was, the one rule
- [`api/TheYard.Application/Features/Visitors/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Application/Features/Visitors) - the reader and the writer, and the count's query
- [`api/TheYard.Infrastructure.Cosmos/Features/Visitors/CosmosVisitorStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/Features/Visitors/CosmosVisitorStore.cs) - the kept records
- [`api/TheYard.Api/Features/Visitors/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Features/Visitors) - the slice's route
- [`api/TheYard.Api/Shared/Activity/ActivityCollector.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Activity/ActivityCollector.cs) - the sightings, written with the activity
- [`src/components/landing/PulseBand/`](https://github.com/SteveStout/TheYard/tree/main/src/components/landing/PulseBand) - the first screen's fourth instrument
- [`src/components/admin/PeopleReadouts/`](https://github.com/SteveStout/TheYard/tree/main/src/components/admin/PeopleReadouts) - the Activity card's readouts and the new-people chart
- [`api/TheYard.Tests/VisitorTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/VisitorTests.cs) - holds this record

## Addendum, 2026-10-23 (1.0.4.48): the kept visits, back to the first day, beside the count

The count began at zero on the evening this record shipped, so the first screen read "1 people, all time" on a site with six weeks of visitors behind it. I read it on my phone that evening and asked for the total to be backfilled from what the site had kept.

What can be backfilled is visits, not people. The activity kept before this record holds a token that rotates daily, so the same person on two days is two tokens and nothing kept can join them; that was ADR-071's line, and it stands. What that history does count exactly is one visit a person a day, by the same rule the Activity card and this count share (the site's own network is self, a visitor whose every request looked like a bot is a scanner, anyone else a person, every loopback row on a day one machine). So the one query now carries both: the people counted once since the day the count began, and the visits by people since the first day anything was kept.

- `VisitHistory` (the host's `Shared/Activity`) counts the keeper's visitor rows in the background and answers the Application port `IVisitHistory` with the last count: the total, the first day, and the visits on each kept day. The first count reads every kept day (one cross-partition query, about five thousand documents at this size); each count after it, once an hour, reads only from the day before the newest day it holds, because a day's rows can still be written at its boundary and today's are being written, so the hourly read stays two days of rows however long the site keeps them. A count that fails leaves the last one standing and waits five minutes before the next, rather than asking a store that is refusing every fifteen seconds.
- `ReadPeopleCountHandler` takes the port beside the visitors' reading side and answers `visits` (`all_time`, `from`, `by_day` for the last thirty UTC days) with the figures it answered before, so `/api/admin/visitors/people` and `/api/pulse` cannot say different numbers. The pulse carries them flat as `visits_all_time`, `visits_from` and `visits_by_day`.
- The first screen's instrument is labelled "Visits by people" and says "2,3xx visits since 13 Sep", with the visits of the last thirty days drawn under it; until the first hourly count lands it is "People who have visited" with the people counted once ("5 people since 23 Oct", "person" for one). The pulse never fails on the count: a tally the store cannot answer is an unread instrument, not a first screen that answers 500. The Activity card's readouts say "each counted once · since 23 Oct" for the people and gain a fifth, "Visits · one a person a day · since 13 Sep". The People tile says "since 23 Oct" where it said "all time".
- A public figure never says "all time" about a count that began that evening again: `unitOf` and `unitWords` (`pulse.ts`) choose every unit on the band, held at none, one and many, and the strip's tiles are one list, `TILE_KEYS`, that the browser specs count from, after 1.0.4.41's first gate went red on a spec that still counted eight tiles.

A review of the day's work from three seats, by a reader who had written none of it, found the first cut of this addendum's code reading the whole history every hour, retrying a failed count at once, failing the pulse when the tally failed, and labelling visits as people; each is fixed above. It also named what stays as it is: the person hash is keyed with the session signing key, so rotating that key starts the count again, and the tally reads every kept visitor record, scanners included, once a minute per process.

`VisitorTests.The_kept_history_counts_one_visit_a_person_a_day_and_leaves_out_scanners_and_the_site_itself` holds the rule, the merge of a later read into the held count, and the handler's thirty days; `VisitorRouteTests` and `PulseTests` hold the fields; `pulse.test.ts`, `people.test.ts` and `statTiles.test.ts` read an older server's answer as no visits and hold the words.

```live path=api/TheYard.Api/Shared/Activity/VisitHistory.cs region=visit-history
```

## Addendum, 2026-10-24 (1.0.4.50): my own browsing is not a person, and the count runs from the first person

The review of 24 October found two things the count said that were not so. First, my own phone and laptop were counted as people: a read was the site's own only by its tools' agent tag or App Service's own agents. Now a request that presents the operator's key leaves with a cookie, `yard_operator`, whose value is a keyed hash of a fixed label that nobody without the signing key can make, and a request carrying the cookie or the key is kept as the site reading itself, with no person hash. A browser counts as mine from the first time it opens the Admin tab with the key. A loopback request is self too, whatever agent it names, so the kept row and the Activity card no longer disagree on a request with no agent. The under-lines say what is left out in those words: my own tools' reads and my own browsing.

Second, "since 23 Oct" was the first scanner's day: the count's first day was the earliest first-seen over every kept record. It is the earliest first-seen over the people now, and none before the first person, in both adapters.

## Addendum, 2026-10-24 (1.0.4.51): a browser that updates itself is the same person, the tally is counted not read, and today's visits are to the minute

The person hash took the whole user agent, and Chrome's version changes every four weeks, so the same person on the same network became a new person each month. From 1.0.4.51 the hash takes the address and the agent's family and platform, the agent with every version number taken out (VisitorTokens.AgentFamily, held by a test over six real agents: Chrome on a desktop and on Android, Safari on an iPhone, Edge, Firefox and a crawler). The count of people resets once on the day this rolled, 24 October 2026: every person already counted is counted once more, the next time they visit. Two people on one network with the same browser on the same platform are one person, which the count already accepted for one browser.

The tally the Activity card and the first screen read once a minute read every kept visitor record, and scanners were kept for good, about 150 a day. Now it is counted, not read: one aggregate query by kind, the first person's day, and the first days of the people first seen in the thirty-day window. A scanner's record expires after ninety days (`ttl` on the scanner records only; a person's is -1, never), and the scanners left out of the count are a running number on one `visitors-summary` document in the same partition, so the figure keeps its history after the records go. If the store refuses the aggregate read, the whole read it replaced still answers. The request-unit cost of one tally before and after was not read from the response headers in this lane; the before is every visitor document (over a hundred, growing), the after three aggregate answers and one point read.

The visits were counted once an hour, so for up to an hour "new today" could read more people than "visits today". VisitHistory counts today alone again every minute between the hourly counts, one partition, and the readout says "today to the minute".


## Addendum, 2026-10-25 (1.0.4.67): who a visitor was is shared

`VisitorKind.cs` stood in the Domain ring's Visitors slice, and the Admin tab's Activity card reads it too. Once rule 1 of the slices held across the rings (ADR-107, the addendum of 1.0.4.67), the Admin slice reaching into Visitors was one slice using another. A rule two slices read is a Shared concern, so it moved to `api/TheYard.Domain/Shared/Visitors/VisitorKind.cs`. The rule is unchanged and is still the one place that says who a person is.

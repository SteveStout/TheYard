# Who comes

**Every page and API request the site serves is counted once, by the store that served it, as a visit that names nobody.** The files a page is made of (scripts, styles, fonts, photographs, and the text and XML files at the root) are not counted, because one visit is one page and not the forty files behind it, and neither are the Admin tab's own reads. The Admin tab draws the count day by day, split three ways: people, scanners and crawlers, and the site reading itself.

## In plain words

This page says how the site counts who visits it. Every page or API request is counted once as a visit that names nobody. People are counted apart from bots, and the site's own checks are counted apart from both.

What that is worth: a developer sees a visitor count that groups a day's requests without ever storing a full address, and the organization gets a count of real readers that its own tools cannot inflate.

## What a visit carries

A visit is handed to the counter as a record with six fields, and a record with no field for a thing cannot carry it:

```live path=api/TheYard.Application/Activity.cs region=activity-port
```

- **When**, to the hour.
- **The visitor token**, a keyed hash of the day and the address. Within a day the same address is the same token on both sites, so one person's requests group together; the next day it is a different token, so nothing joins one day to the next. The key is the signing key the containers share and nobody outside has, which is what stops anybody turning a token back into an address by hashing every IPv4 address there is.
- **The network**, the address cut to its first three octets (`203.0.113.x`): enough to see a network, a country or an obvious scanner range, and not enough to name a machine. A full address is never stored, on either store.
- **The path**, with any at sign percent-encoded so an address pasted into a URL cannot travel. The query string is never recorded.
- **The store** that served the request (Azure SQL Database or Azure Cosmos DB), so the graph keeps a line for each site.
- **Whether it looked like a bot**, read from the request's user agent and what it asked for. The agent itself is read and forgotten.

```live path=api/TheYard.Api/Activity.cs region=visitor-token
```

A port after an address is dropped before the token is made, so one machine is one visitor whatever port it came from.

## Three kinds of visitor

Each visitor-day is exactly one of three kinds, and the three add up to the day's total:

- **People.** Everybody who made a request that did not look like a bot.
- **Scanners and crawlers.** A token whose every request looked like a bot (by its agent or by what it asked for): the probes for WordPress, `.env` and `.git` that every public address gets, and the search engines, whose agents name themselves.
- **The site's own reads.** The tools that check the site on its owner's behalf, the page sweep, the release's own readers and App Service's health check among them, carry `TheYard-SelfRead` in their user agent or come from the machine itself, and are counted apart so they never pass for people. A stranger could claim the mark and hide from the count; that costs a count of visitors and reaches nothing else.

The chart stacks the three day by day, people at the bottom, in three colours chosen as a set that stays apart under colour blindness ([Colour and style](https://theyard.stevenstout.biz/?doc=color-style#chart-colours)). Visitors only is the default view; the lines by store are a click away.

## Where they came from

On a page load, and only then, a visit also keeps the lowercased host of the page that linked here and nothing else of it, so neither a path nor a query that could say something about the reader reaches a row. The hosts are grouped on the card as LinkedIn, GitHub, search, another site, or typed and unknown. A link opened from a PDF, the resume among them, sends no referrer, so it reads as typed unless the link carries a tag of its own.

## The path through

The card also counts, in visitor-days, how many readers took each step of the path a reader takes through a portfolio: the landing page, the inventory, the page about the author, and the resume. Each step is a set of paths, and the resume counts both addresses it is served from.

## The decisions behind it

- [ADR-071, Site activity, and the line an address does not cross](https://theyard.stevenstout.biz/?doc=adr-activity), with its addenda on the three kinds, the referrers and the path.

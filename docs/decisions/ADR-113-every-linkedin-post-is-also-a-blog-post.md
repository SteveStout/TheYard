# ADR: Every LinkedIn post is also a blog post

Status: accepted, 2026-10-22. Every post Steve publishes on LinkedIn is kept on this site in full, at its own address, with the slides, the code it talks about and a link back to the LinkedIn post. The posts, every read and everything a reader does are rows in Azure SQL, in a `blog` schema of their own, and every number the blog shows is a SQL view over those rows.

## Reading list

1. [ADR: Data first, and the database in source control](https://theyard.stevenstout.biz/?doc=adr-data-first), where the schema is written and who publishes it.
2. [ADR: Tagged links, who opened what](https://theyard.stevenstout.biz/?doc=adr-tagged-links-who-opened-what), the six-character code a read is joined on.
3. [ADR: A second store on Cosmos DB, and what it costs](https://theyard.stevenstout.biz/?doc=adr-second-store), the store this feature does not use for its model, and why that is a different choice from the Inventory's.

## In plain words

A LinkedIn post scrolls away in a day. Kept here it stays: the text, the slides, a link to the code and to the live page it is about. The side nav has a Blog row directly under Home, marked NEW POST for a week after a post goes up, and the home page shows the three newest.

Each time someone opens a post, the site writes one row: which post, when, and where the reader came from (LinkedIn, a tagged link, a search engine, or straight in). As they read it writes a few more: how far down they got, how long they stayed, whether they opened "see more", which slides they looked at, and whether they went on to LinkedIn or the code. The Admin tab's Blog analytics card reads those rows back as a table per post.

Why SQL, in Steve's words: the posts, the read log, the events, the tagged-link codes and, later, the posting schedule are one relational model. "Which post works", the funnel from opened to finished, and "what to repost next" are then plain views and joins, not code.

## Context

- **There was no blog.** The site's pages are documents served from the repository, and nothing on it took writes from a reader other than bids and the traffic log.
- **The site already records traffic.** `RequestRecorder` and the activity log count requests, and `Features/Links` counts tagged opens (ADR: Tagged links, who opened what). Neither knows what a post is, so neither can say how far into one a reader got.
- **The relational store is always there.** The SQL Server database runs whichever store the toggle names, because accounts, the traffic minutes and the catalogue live in it. The blog joins posts to reads to codes, which is that store's work.
- **The schema is published by a person.** The site's database user holds only `db_datareader` and `db_datawriter` (ADR: Data first, and the database in source control), so the `blog` schema arrives the way every other table did: the SQL project in `api/TheYard.Database`, published with sqlpackage. On SQLite, locally and in the tests, an EF migration builds the same tables.

## Decision

1. **Five tables and three views in a `blog` schema.** `blog.Post` (the slug and the LinkedIn URL each unique), `blog.PostImage`, `blog.PostTag`, the read log `blog.PostView` and the event log `blog.PostEvent`. The views are `blog.PostStats` (reads, readers, reads in seven days, from tagged links, last read, average depth and time, clicks to LinkedIn and to the code), `blog.Funnel` (opened, read past half, finished, clicked out) and `blog.ReadsBySource`. Bots are flagged when a row is written and left out of every view.
2. **A read keeps where it came from, never who.** The page sends the slug, the referring page, the `?r=` code and the utm fields. The API keeps the referrer's host only, works the source out with `BlogRules.Source`, reads the user agent for a device class and a bot guess and drops it, and keys the reader on an HMAC of the address with the day in it, so two reads on one day are one reader and nothing joins across days. No address and no agent is stored.
3. **Six event types, each with one shape.** `depth` (25, 50, 75 or 100), `time` (whole seconds), `more`, `slide` (its number), `click` (`linkedin`, `code`, or `body:` and a host) and `copy`. `BlogRules.Wrong` answers the one sentence wrong with anything else, and the route answers 400 with it.
4. **One write path, behind the operator's key.** `POST /api/admin/blog/posts` adds a post or replaces the one with its slug. The public routes read posts and record reads and events; nothing public writes a post.
5. **Not on the store toggle.** The Inventory shows two stores as interchangeable behind one port. The blog shows the other pattern: one store for one job. The model and its views are SQL only, whichever store the toggle names.

## Consequences

- The numbers are views, so a new question (which slide loses readers, which post brings the most clicks to the code) is a new view, not new code.
- A post is published by a call with the key or by a seed, and a post is only kept once its real LinkedIn URL and date are read from LinkedIn.
- Raw events in a Cosmos DB container, rolled up into these tables on a timer, are the next step once the volume asks for it; at today's traffic SQL takes the writes directly, and this record gets an addendum when that changes.

## Where it sits

The rules (where a read came from, the device class, what an event may carry, what makes a slug) are in Domain (`BlogRules`), and the shapes in Data (`BlogPost.cs`). The port, the memory store and the six handlers are the Blog slice's, in `Application/Features/Blog`. The relational store's adapter is in `Infrastructure/Features/Blog` (`EfBlogStore`), with the rows and their mapping beside the context (`BlogRows.cs`, `YardDbContext.Blog.cs`); the schema itself is the SQL project's (`api/TheYard.Database`, the `blog` folders), and SQLite gets the same tables from the `Blog` migration. The host holds the six routes and the wiring (`Features/Blog`) and the day's keyed hash (`Activity.cs`). The page draws the blog in `src/components/blog`, the band in `src/components/landing/LatestPosts`, the card in `src/components/admin/BlogCard`, and reads and reports through `src/lib/blog.ts`.

## Files

- [`api/TheYard.Domain/BlogRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/BlogRules.cs) - where a read came from, the device class, the events and the slug
- [`api/TheYard.Data/BlogPost.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Data/BlogPost.cs) - a post, a read, an event and a post's numbers
- [`api/TheYard.Application/Features/Blog/BlogHandlers.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Blog/BlogHandlers.cs) and [`BlogStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Blog/BlogStore.cs) - the handlers, the port and the memory store
- [`api/TheYard.Infrastructure/Features/Blog/EfBlogStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/Features/Blog/EfBlogStore.cs) - the relational store
- [`api/TheYard.Database/Tables/Blog/PostView.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Blog/PostView.sql) and [`api/TheYard.Database/Views/Blog/PostStats.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Views/Blog/PostStats.sql) - the read log and the numbers
- [`api/TheYard.Api/Features/Blog/BlogEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Blog/BlogEndpoints.cs) - the six routes
- [`src/components/blog/BlogPage/BlogPage.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/blog/BlogPage/BlogPage.tsx) and [`src/components/blog/BlogPostCard/BlogPostCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/blog/BlogPostCard/BlogPostCard.tsx) - the page and the post
- [`src/components/admin/BlogCard/BlogCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/BlogCard/BlogCard.tsx) - Blog analytics on the Admin tab
- [`api/TheYard.Tests/BlogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BlogTests.cs), [`BlogEndpointsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BlogEndpointsTests.cs) and [`tests/e2e/blog.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/blog.spec.ts) - the rules, the store, the routes and the page

## Addendum, 2026-10-22 (1.0.4.33): a feed

The blog has an RSS 2.0 feed at `/api/blog/rss.xml`, written from the same handler the page reads: every published post, newest first, with its address on the blog, its date, its two-line summary and its hashtags as categories. A reader can follow the posts without LinkedIn, and a feed reader is one more source the read log can name. It answers 503 with the store's reason while the blog cannot be read, like the list (`BlogFeed.cs`, held by `BlogEndpointsTests`).

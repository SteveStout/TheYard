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

The rules (where a read came from, the device class, what an event may carry, what makes a slug) are in Domain (`BlogRules`), and the records in the Blog slice beside its port (`Application/Features/Blog/BlogPost.cs`). The port, the memory store and the six handlers are the Blog slice's, in `Application/Features/Blog`. The relational store's adapter is in `Infrastructure/Features/Blog` (`EfBlogStore`), with the rows and their mapping beside the context (`BlogRows.cs`, `BlogModel.cs`); the schema itself is the SQL project's (`api/TheYard.Database`, the `blog` folders), and SQLite gets the same tables from the `Blog` migration. The host holds the six routes and the wiring (`Features/Blog`) and the day's keyed hash (`Activity.cs`). The page draws the blog in `src/components/blog`, the band in `src/components/landing/LatestPosts`, the card in `src/components/admin/BlogCard`, and reads and reports through `src/lib/blog.ts`.

## Files

- [`api/TheYard.Domain/Features/Blog/BlogRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Features/Blog/BlogRules.cs) - where a read came from, the device class, the events and the slug
- [`api/TheYard.Application/Features/Blog/BlogPost.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Blog/BlogPost.cs) - a post, a read, an event and a post's numbers
- [`api/TheYard.Application/Features/Blog/BlogHandlers.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Blog/BlogHandlers.cs) and [`BlogStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Blog/BlogStore.cs) - the handlers, the port and the memory store
- [`api/TheYard.Infrastructure/Features/Blog/EfBlogStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/Features/Blog/EfBlogStore.cs) - the relational store
- [`api/TheYard.Database/Tables/Blog/PostView.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Blog/PostView.sql) and [`api/TheYard.Database/Views/Blog/PostStats.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Views/Blog/PostStats.sql) - the read log and the numbers
- [`api/TheYard.Api/Features/Blog/BlogEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Blog/BlogEndpoints.cs) - the six routes
- [`src/components/blog/BlogPage/BlogPage.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/blog/BlogPage/BlogPage.tsx) and [`src/components/blog/BlogPostCard/BlogPostCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/blog/BlogPostCard/BlogPostCard.tsx) - the page and the post
- [`src/components/admin/BlogCard/BlogCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/BlogCard/BlogCard.tsx) - Blog analytics on the Admin tab
- [`api/TheYard.Tests/BlogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BlogTests.cs), [`BlogEndpointsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BlogEndpointsTests.cs) and [`tests/e2e/blog.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/blog.spec.ts) - the rules, the store, the routes and the page

## Addendum, 2026-10-22 (1.0.4.33): a feed

The blog has an RSS 2.0 feed at `/api/blog/rss.xml`, written from the same handler the page reads: every published post, newest first, with its address on the blog, its date, its two-line summary and its hashtags as categories. A reader can follow the posts without LinkedIn, and a feed reader is one more source the read log can name. It answers 503 with the store's reason while the blog cannot be read, like the list (`BlogFeed.cs`, held by `BlogEndpointsTests`).

## Addendum, 2026-10-24 (1.0.4.55): where the pictures live

The blog has modelled pictures since it shipped (`BlogImage`, the `blog.PostImage` table, the slides strip), and both posts had none, because the files had nowhere to live. They live in a private blob container, `blog`, on the job hunt's storage account (stjobhuntss), and the site serves them from its own address: `GET /blog/images/{name}`.

Why a blob container. Not the repository: a post is a row in SQL and its pictures follow the post, not the image, so adding a picture is not a deploy. Not the job-hunt share: a share is billed by the listing, and this record's day is the day that bill was found. A blob read costs $0.004 for ten thousand and a gigabyte $0.018 a month at Hot LRS; a hundred pictures at 300 KB is 30 MB, a tenth of a cent a month to keep, and a year of this blog's pictures is under a dollar.

The name is the picture's own: the first sixteen hex digits of the SHA-256 of its bytes and its kind (`3f9a0c2b1d4e5f60.webp`), so an edited picture is a new name. That is what lets the route answer `Cache-Control: public, max-age=31536000, immutable` with an ETag of the name: a year's cache can never be wrong. A name the rule does not make is never looked up, and a name not kept is a 404 problem.

Reads are paid once a roll, not once a view: the host keeps the pictures it served in its own memory (`CachedBlogImages`, the site's bounded cache, least recently used, 32 MB, a day each, a miss never kept), and the edge keeps them for the year the header allows. The container is private (public access None); the site reads and writes it with its own identity (Storage Blob Data Reader on the account, Storage Blob Data Contributor on the container), over the blob service's REST interface through the one connection pool (`BlobBlogImageStore`, Infrastructure), and a process off Azure keeps pictures in its memory and says so. A lifecycle rule is on the account and off, the hook for a cap when one is chosen. A picture is a page's asset, never a visit.

The query takes the reader only (`IBlogImageReader`); the writer (`IBlogImageWriter`) is for the upload command that follows.

## Addendum, 2026-10-24 (1.0.4.56): putting a picture in

The operator puts a picture on a post with `POST /api/admin/blog/posts/{slug}/images`, a multipart form with the file, its alt text and its position, behind the operator's key. `PutBlogImageCommand` holds the picture to four limits, each refused as a problem that names its figure: at most one megabyte (the route reads one byte past it and no further), a PNG, a JPEG or a WebP by its own magic numbers (never by its file name), at most 2000 pixels wide, and an alt text of one sentence saying what the picture shows. The width and height are read from the file's own header (`BlogImageHeaders`: PNG's IHDR, JPEG's start-of-frame, WebP's VP8, VP8L or VP8X chunk, with no image library), and a test holds one file of each kind.

A kept picture goes in under the hash of its bytes, and its row on the post (`blog.PostImage`) carries the served address `/blog/images/<name>`, the alt text, the width and the height, at its position; a picture already at that position is replaced. The host never resizes: the post lane resizes before it uploads, so the work and the CPU stay off the web app.

The Blog analytics card says how many pictures are kept and their bytes, read from the container's own listing at most once a minute, with its source named.

## Addendum, 2026-10-24 (1.0.4.57): a strip of slides the keyboard can scroll

With the two posts' fourteen real slides in, the strip is wider than its card on every screen, and axe named it: a region that scrolls must be reachable from the keyboard (scrollable-region-focusable). The strip takes focus (`tabIndex` 0), names itself ("The slides, 7 of them, scroll sideways") and shows the site's focus ring, so the arrow keys scroll it. Before the slides, no strip on the live site was wide enough for the rule to fire, which is why the gate's axe pages never saw it; blog.spec.ts now runs axe on a post with a strip whether the store holds real pictures or the test answers made-up ones.


## Addendum, 2026-10-26 (1.0.4.104): a post that reads as a blog

The body was kept and drawn as LinkedIn had it: one run of plain text with its line breaks, the hashtags coloured, and every link in a first comment the blog could not show. Read on this site that is a LinkedIn post pasted on a page, and the reference a post rests on (the video, the test, the record) is not on it. From this version the body is written for the blog and the LinkedIn text stays on LinkedIn, where the link under "Comment on LinkedIn" already leads.

The body is a small Markdown: a line of one to three hashes is a heading, a line that opens with a dash is an item and the items that follow one another are one list, a blank line ends a paragraph, and inside a line `**bold**`, `` `code` `` and `[words](https://...)` are what they say. `src/lib/blogBody.ts` reads the text into those blocks (`bodyBlocks`, `inlineParts`) and the card draws each as the element it is: `h3`, `p`, `ul`, `strong`, `code`, `a`. A link is let through only when it is http or https, and a mark that is not one of these stays as the writer typed it. A body with no marks at all comes out as paragraphs, so a post kept as LinkedIn had it still reads. The hashtags are no longer in the body (the post's tags are a column of their own) and the "save this" line, which asks for a repost, is not on a page that has no repost.

A link in the body is a way out of the post, so a click on it is kept the way the two actions already were: `click` with `body:` and the host it went to, as decision 3 named, never the address. `blogBody.test.ts` holds the reading: plain text whole, a heading, a list and a paragraph apart, bold, code and a link read, and a `javascript:` address refused.

What a post should carry, written for the page: the date of what it answers rather than "yesterday", the reference as a link in the sentence that uses it, and a References list at the end with the source, the code and the record, so the page stands on its own after the LinkedIn post has scrolled away.

## Addendum, 2026-11-03 (1.0.4.130): the Blog row is third

What stopped being true on this day: the Blog row is no longer directly under Home. The review route (ADR: Ten minutes, seven stops) takes the row under Home, so the rail reads Home, Review in ten minutes, Blog. The row, its NEW POST and the home page's Latest posts band are otherwise as above; `blog.spec.ts` holds the new order.

## Addendum, 2026-11-12 (1.0.5.44): the blog holds still on a phone

What stopped being true: the blog page drew its frame, then its posts and its sources box as each read answered, so on a phone the page jumped: a layout shift of 0.394 on the blog and 0.666 on a post, read cold at 390 by 844 on 11 November, where Google counts 0.1 as good. While the posts are read, the list now keeps a screen of room, so nothing after it is on the first screen and nothing on the first screen moves when they arrive. The sources box reads an Admin route, so it follows one rule: a public page reads an Admin route only into a box whose size is fixed before the read. It has room for four rows, the four sources the site names, and draws at most four. blog.spec.ts holds both reads back a second and a half on every run and holds the shift at 0.1 or less on the blog and a post, and holds the sources box's height the same before and after its read.

## Addendum, 2026-11-18 (1.0.5.90): a blog that filters, finds and shows its slides, fast

What stopped being true: "The views are the numbers" for the list. From this version the list reads cards, not posts: no body, the cover slide, the counts, the reading time, the topics and the keywords; a post's own address reads the post. The list answered every post with every body and image, 143,102 bytes (43,783 in brotli), and its reads came from loading both logs whole on every request. Steve's words on 18 November: "this needs to be filterable and fast on the SQL side and you should be able to view the slides". The mockup he reviewed that day is the design.

**The read model.** `ListPostsQuery` carries the filter (the typed words, a topic, a keyword, a month or a year, the order, the cursor) and `IBlogReader.ListAsync` answers one page of ten: the cards, how many posts match, each chip's count among the posts the other filters leave, and the next cursor. The relational store does it in SQL (`EfBlogList`): the filter is a WHERE on the published posts, newest first is an ORDER BY on a filtered covering index (`IX_Post_Published`, published only, keyed newest first, carrying what a card shows) with a keyset for the page, most read is a correlated COUNT, and the chips are GROUP BYs. A card's reads and reads this week are one GROUP BY over the page's posts through `IX_PostView_Reads`, filtered to people (IsBot = 0) and carrying what a count reads. The memory store answers the same way through the same rules, and one table of cases (`BlogListTests`) runs on both and on SQLite; a large read log is held to cost the list nothing but two counts.

```live path=api/TheYard.Database/Tables/Blog/Post.sql region=*
```

**Why the list's two counts moved to SQL and the funnel stayed in code.** The list is the public page's hot path and asks for two plain counts, which SQL does through an index without reading a row of the log; the funnel's arithmetic (deepest depth per reader, longest time, distinct readers who clicked out) lives in `BlogNumbers.Of`, one place both stores and the views' own description share. So the funnel keeps its arithmetic and changes what it reads: the Admin card's numbers are over the last thirty days, cut in SQL through the window indexes (`IX_PostView_Window`, `IX_PostEvent_Window`), so its cost follows a month of reads and never the whole log.

**Search, and why not full-text.** Full-text search is installed on the live database (Basic tier, read on the day), and the brief chose CONTAINSTABLE where it is. It is not used: nothing in the gate or in CI runs SQL Server, so a CONTAINSTABLE query would ship with no test ever executing it, and its rank is not the weights Steve's mockup ranks by. The catalogue met the same question earlier and answered it with a table of words (ADR-103, the addendum on a typed word as a seek); the blog does the same. On save the Domain cuts a post into its words, each word of the title, the summary and the full body's text with its count, plus the word pairs a phrase can land on, and the store keeps them in `blog.PostWord`, keyed by the word. A search widens each typed word through `blog.SearchSynonym` (written from `KeywordRules` by the site, never typed), asks for every widened term's counts in one statement of seeks on the word's start, and `SearchRules` turns the counts into the score: title 6, a keyword 5, a topic 4, the summary 3, the body 0.6 a mention up to five plus 0.4, every typed word landing somewhere. The same query runs on SQLite and on Azure SQL, and the rules are held to the mockup's own six searches over its own copy of the posts (`BlogSearchRulesTests`).

```live path=api/TheYard.Domain/Features/Blog/SearchRules.cs region=weights
```

```live path=api/TheYard.Domain/Features/Blog/KeywordRules.cs region=keyword-list
```

**Topics and keywords.** Topics are the post's tags (`blog.PostTag`), ten of them, never drawn with a "#". Keywords are kept on save in `blog.PostKeyword`: a keyword counts when the title names it or the words a reader reads name it at least its minimum. Link addresses are not counted: the mockup's counts read them, which gave three posts "Blob Storage" from github.com/.../blob/main addresses; the seven pairs that came only from addresses are named in the lane's DONE note.

**Drawn once, derived again.** A save renders the body's Markdown to HTML (`BlogBodyHtml`, the same small Markdown the page read, every piece escaped, links to http or https only), works out the reading minutes, the words and the keywords, and stamps them with the rules' version. A post kept under another version, or none, is derived again by a pass at start (`BlogDerivationPass`), which also gives a post never derived its topics from `data/blog/topics.json`: that is how the fifteen posts saved before this version got theirs. The page carries no Markdown reader.

**Cached a minute.** The list and the post answer with an ETag and `Cache-Control: public, max-age=60, stale-while-revalidate=600`, a repeat is a 304, and the host keeps each answer a minute in its own bounded memory (`BlogAnswers`), dropped on every save, picture put and derivation.

**The slides.** A slide in the strip opens a native dialog: the slide full size with its alt text as the caption, Prev and Next, "3 of 11", a strip of every slide, click to zoom, swipe, Escape, focus held inside and returned, a link to the slide, and the address `?view=blog&post=<slug>&slide=3` that opens it there. The strip loads 320 px thumbnails, kept beside each picture under `<16 hex>-320.webp` with the same year's cache; a thumbnail not put yet is answered by its picture for five minutes. `scripts/blog_thumbs.mjs` makes them on a shipping machine, and the upload route takes one beside a picture. The Ignite post's strip went from 1,623,796 bytes to 147,756. Opening the viewer is a click on "slides", which `BlogRules.IsWayOut` keeps out of the funnel's "clicked out".

**The headers.** Every answer from the host and every page the rendering service draws carries one policy: scripts from the site and the page's own inline script by its hash (the service adds a nonce for anything React streams), styles from the site and inline, pictures from the site and the repository's GitHub hosts, nothing framed by another site, nosniff, a referrer kept to the origin across sites, the browser's unused powers off, the opener isolated, and HSTS with its subdomains, never preloaded (`SecurityHeaders`, `render/security.ts`).

```live path=api/TheYard.Api/Shared/Wiring/SecurityHeaders.cs region=the-headers
```

**Budgets, and the one not met.** Held in the browser suite on the built site at a phone's size (`blog-budgets.spec.ts`): the blog route's JavaScript at most 150 KB in brotli and none of the Markdown or Admin chunks, the first screen at most 300 KB, the largest paint the post's title or its cover slide asked for first, layout shift under 0.01, no long task over 50 ms while a search is typed, and the viewer's slide on screen within 200 ms. The main chunk lost the quality history (213 KB) it carried for the gate badge and the landing hook, which now read two rows and a digest from the build's release facts. Lighthouse on a phone, read on the built site through the rendering service: 91 to 96 for Performance and 100 for Accessibility, Best Practices and SEO. Performance 100 on a phone is not reached: what is left is Lighthouse's estimate of a slow phone network counting the React bundle's download before the largest paint, and closing it needs the blog's first load to arrive without React's runtime, a change of architecture of its own.

### Files

- [`api/TheYard.Domain/Features/Blog/KeywordRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Features/Blog/KeywordRules.cs), [`SearchRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Features/Blog/SearchRules.cs), [`BlogText.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Features/Blog/BlogText.cs), [`BlogMarkdown.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Features/Blog/BlogMarkdown.cs), [`BlogBodyHtml.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Features/Blog/BlogBodyHtml.cs), [`BlogListRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Features/Blog/BlogListRules.cs) and [`PostDerivation.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Features/Blog/PostDerivation.cs): the rules
- [`api/TheYard.Application/Features/Blog/MemoryBlogStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Blog/MemoryBlogStore.cs) and [`BlogListShape.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Blog/BlogListShape.cs): the memory store's list and the shaping both stores share
- [`api/TheYard.Infrastructure/Features/Blog/EfBlogList.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/Features/Blog/EfBlogList.cs) and [`EfBlogIndex.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/Features/Blog/EfBlogIndex.cs): the list in SQL and what a save keeps
- [`api/TheYard.Database/Tables/Blog/PostWord.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Blog/PostWord.sql), [`PostKeyword.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Blog/PostKeyword.sql) and [`SearchSynonym.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Blog/SearchSynonym.sql): the three new tables
- [`api/TheYard.Api/Features/Blog/BlogAnswers.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Blog/BlogAnswers.cs), [`BlogWire.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Blog/BlogWire.cs) and [`BlogDerivationPass.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Blog/BlogDerivationPass.cs): the minute's cache, the wire and the pass
- [`api/TheYard.Api/Shared/Wiring/SecurityHeaders.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Wiring/SecurityHeaders.cs) and [`render/security.ts`](https://github.com/SteveStout/TheYard/blob/main/render/security.ts): the headers
- [`src/components/blog/BlogFilters/BlogFilters.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/blog/BlogFilters/BlogFilters.tsx), [`src/components/blog/SlideViewer/SlideViewer.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/blog/SlideViewer/SlideViewer.tsx), [`src/lib/blogFilter.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/blogFilter.ts) and [`src/lib/blogReads.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/blogReads.ts): the page
- [`scripts/blog_thumbs.mjs`](https://github.com/SteveStout/TheYard/blob/main/scripts/blog_thumbs.mjs): the thumbnails
- [`api/TheYard.Tests/BlogListTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BlogListTests.cs), [`BlogSearchRulesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BlogSearchRulesTests.cs), [`BlogApiTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BlogApiTests.cs), [`SecurityHeadersTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/SecurityHeadersTests.cs), [`tests/e2e/blog-filters.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/blog-filters.spec.ts), [`blog-viewer.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/blog-viewer.spec.ts) and [`blog-budgets.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/blog-budgets.spec.ts): the tests

## Addendum, 2026-11-20 (1.0.5.128): what a reader racing a save sees, and one sentence corrected

**Corrected.** The 1.0.5.90 addendum says the same query runs on SQLite and on Azure SQL. It does not, and never did: the search is one statement of seeks on each store, but the seek is written twice (`EfBlogList.Starting`). Azure SQL gets a prefix match the index serves; SQLite gets a range on the word, which its index can seek, where its case-blind LIKE would read every row. The ranking (`SearchRules`) and the shaping (`BlogListShape`) are the one copy both stores share. The SQL Server branch is still run by no test in the gate or CI, as that addendum says of full-text. And the changelog's 1.0.5.90 line says the main bundle came out 153 KB lighter; the build's own figure is 136 KB (609,446 B to 473,194 B). The changelog line stays as written, since a shipped line is not edited.

**The kept answers.** `BlogAnswers` now reads through the bounded cache's one-load-a-key path, so readers who miss together share one read, and every save starts a new generation of keys, so a read that was under way when the save cleared the copy is kept where nobody asks for it again. The copy is per process: another host keeps its own answer out its minute, as the cache rule already tells the browser.

**The save.** `EfBlogStore.SaveAsync` writes the post's row, its slides and topics, and its words and keywords in one transaction through the context's retry strategy, so a save that fails partway keeps none of it (`BlogEdgeTests`).

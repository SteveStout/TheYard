# ADR: Search engines are told about every post

Status: accepted, 2026-11-20, at 1.0.5.143. The blog's sitemap at `/sitemap-blog.xml`, its line in robots.txt, `/llms.txt` written with every post, the IndexNow ping after a post is saved, and what he knows and where he studied on his Person node ship at 1.0.5.143. The list's post titles become links to each post's own address in the version after it, and the blog filters' case and back-button checks after that.

## Reading list

1. [ADR: The public face](https://theyard.stevenstout.biz/?doc=adr-public-face): robots.txt, the static sitemap, llms.txt and the Person node this record extends.
2. [ADR: Every LinkedIn post is also a blog post](https://theyard.stevenstout.biz/?doc=adr-every-linkedin-post-is-also-a-blog-post): the blog, its store and the head each post gets.

## In plain words

The goal is traffic. Sixteen posts were on the blog on 20 November and no search engine had been told about one of them: the sitemap listed the documents and no post, llms.txt never said the word blog, and the list drew each post's title as plain text with a button beside it, so a crawler reading the page found no address for most posts. When a manager or an AI search tool looks for ".NET AI code quality" or for Steven Stout, the posts should be what comes back.

## Context

- Each post already has its own head on the server (`render/heads.ts`): its title, its summary and a canonical link to `/?view=blog&post=<slug>`, with no tagged code.
- Posts reach the store from outside the repository: the operator saves them through `POST /api/admin/blog/posts`, and `blog-post.yml` saves a body again from `data/blog/<slug>.md`. A list written into a file at build time is out of date with the first post saved after the build.
- `public/sitemap.xml` is held to the document catalogue in both directions by `PublicFaceTests`, so a post cannot go there.

## Decision

1. **The addresses stay as they are.** A post's address is `/?view=blog&post=<slug>`, with a canonical link to itself; a query address with a self canonical is indexable, and the head is already right per post. No `/blog/<slug>` path routes.
2. **The blog has a sitemap of its own, written from the store.** `GET /sitemap-blog.xml` lists `/?view=blog` and every published post once, each `<loc>` the post's canonical exactly as the head writes it (`&` escaped as `&amp;`) and its `<lastmod>` the day it was published; the blog's own entry is dated by its newest post. It is read when it is asked for, so a post the operator saves is listed with no commit. A root path, because a sitemap may only list addresses at or under its own. It is kept like the RSS feed: no edge copy, the browser asks each time.
3. **robots.txt names both sitemaps.** A second `Sitemap:` line for `/sitemap-blog.xml`; `public/sitemap.xml` stays as it is, the documents only.
4. **llms.txt is the API's.** `/llms.txt` is the site's own words from `data/llms.txt` with a `## Blog` section read from the store: one line per post, `- [Title](canonical): its summary`, before the Optional section the format keeps last. The file left `public/`, because a file in the web root wins the route and could only be as current as the last build. A store that cannot be read leaves the section out and the rest stands.
5. **IndexNow is told after a save.** The key is a 32-character hex string, public by design, served as `public/<key>.txt` holding the key. `blog-post.yml` posts the post's canonical and `/?view=blog` to `https://api.indexnow.org/indexnow` after the body reads back on both names; an answer other than 200 or 202 is a warning and never fails the run. The workflow's `indexnow_only` input sends the ping alone, to prove it is answered. No Google Indexing API: it covers job postings and livestreams only.
6. **The list's titles are links.** Each title on the list, the full post's `<h2>` and every short card's `<h3>`, holds an `<a href>` to the post's own address (`postLink`), drawn on the server, so the HTML a crawler reads carries one address per post. It looks exactly as the title did, with an underline only on hover; a plain click still opens the post in place, a modified click opens the address.
7. **His Person node says what he knows and where he studied.** `knowsAbout` (.NET, C#, Azure, React, TypeScript, SQL Server, Software architecture, AI-assisted development, Code quality) and `alumniOf` the University of Missouri, on the home page and on `/about`; both are on his resume and his profile. Nothing else in the node changes.

```text
a post is saved (the operator, or blog-post.yml)
        |
        +--> the store --> GET /sitemap-blog.xml   (read at request time; robots.txt names it)
        |              --> GET /llms.txt           (the Blog section, read at request time)
        |              --> GET /?view=blog          (each title an <a href> drawn on the server)
        |
        +--> blog-post.yml, after the read-back --> POST api.indexnow.org (the post and the blog)
                                                          |
search engines and AI tools  <----------------------------+  crawl the sitemaps, the list and llms.txt
```

## Why

- **The store is the only list that is never stale.** A post is saved without a commit, so anything written at build time misses it.
- **The canonical is written once.** The sitemap and llms.txt write the address the head names, against the public address the configuration gives (`Azure:Resources:PublicSite`, which a test holds equal to the head's `SITE`; no name of the deployment is written in the code), so a crawler never meets two addresses for one post.
- **A link a crawler can follow is worth more than a sitemap entry.** The sitemap says a page exists; a link on a page says it matters, and the list is the one page every post is on.

## Consequences

- What stops being true: that `public/llms.txt` is the file `/llms.txt` serves (it is `data/llms.txt`, written with the posts), and that the Person node names only his name, title, profiles and city.
- Search Console is told of `/sitemap-blog.xml` by Steve; nothing here signs in to it.
- What holds it: `BlogSitemapTests` (every post once at its canonical, no `r=`, valid XML, the root path, the head's site; the Blog section before Optional; a store that cannot be read), `PublicFaceTests` (both Sitemap lines, the IndexNow key file and the workflow's key, the home page's Person), `DiagramPageTests` (the Person on `/about`), `DockerBuildInputsTests` (no `public/llms.txt` to hide the route) and `PageStatusTests` (the sweep reads both files at every roll).

## Where it sits

Two read-only routes in the API's Blog slice, each asking the feed's own query handler (`ReadFeedHandler`) in the Application ring: nothing new in the rings under it. The words of llms.txt are a file beside the dataset, read through `HostPaths`. The ping is the workflow's, outside the app.

## Files

- [`api/TheYard.Api/Features/Blog/BlogSitemap.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Blog/BlogSitemap.cs) - the blog's sitemap
- [`api/TheYard.Api/Features/Blog/LlmsText.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Blog/LlmsText.cs) and [`data/llms.txt`](https://github.com/SteveStout/TheYard/blob/main/data/llms.txt) - llms.txt with every post
- [`public/robots.txt`](https://github.com/SteveStout/TheYard/blob/main/public/robots.txt) - both sitemaps
- [`.github/workflows/blog-post.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/blog-post.yml) - the IndexNow ping
- [`api/TheYard.Api/Features/Docs/AboutPage.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Docs/AboutPage.cs) and [`index.html`](https://github.com/SteveStout/TheYard/blob/main/index.html) - the Person node
- [`api/TheYard.Tests/BlogSitemapTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BlogSitemapTests.cs) - what holds it

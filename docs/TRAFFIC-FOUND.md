# Being found

**A search for the author's name should find a page on this site beside his LinkedIn, and an AI tool asked about him should find words it can read.** Everything on this page is served from the root of the domain or from the head of a page: the two places search engines and AI tools look.

## In plain words

This page explains how a search for the author's name finds this site, and how an AI tool asked about him finds words it can read. It covers what search engines read (robots.txt and the sitemap) and the plain index written for AI tools (llms.txt).

What that is worth: a developer sees how to make a JavaScript app readable to tools that never run JavaScript, and the organization's work can turn up in search beside the author's LinkedIn.

## What a crawler reads

A search engine renders the app the way a browser does; most AI tools do not run JavaScript at all and read only the HTML the server sends. So the site speaks to both. The home page's head carries a title, a description written for a search result, a canonical address, Open Graph and Twitter tags for a link preview, and structured data. Its body carries, for a reader with no JavaScript, a short summary: the author's name, what TheYard is, and links to the page about him, the README and the documents, as plain HTML.

## robots.txt

Every crawler is welcome everywhere but one address: the endpoint that throws on purpose so the error path can be exercised in production. A crawler hitting it would make real errors for no reason. The file names the sitemap.

## The sitemap

Every document the site serves is in the sitemap, {{live:documents}} of them, each at the address it is served from as markdown (`/api/docs/` and its name), which any crawler reads whether or not it runs JavaScript, beside the home page, the API reference, the diagrams and the page about the author. A test reads the sitemap against the catalogue the documents are served from, so a document cannot be added without being listed, or listed after it is gone. The page about the author carries a `lastmod` the image build writes on the day it is built, so the date is never one somebody typed.

## The page about the author

[`/about`](https://theyard.stevenstout.biz/about) is one small HTML page the API renders, on the pattern the diagram pages use: the palette inline, nothing fetched from another host, plain text a search engine reads without running anything. His name is the heading and the resume's title line sits under it, then who he is and what TheYard proves, with links to his LinkedIn, the repository and the resume. It has its own title, description, canonical and preview tags, and each of the two sites names itself in them. It is the first row of the About section in the sidebar.

## Structured data

Both `/about` and the home page describe the author in schema.org's vocabulary as a `Person`: his name, the resume's title line, the site, and the two profiles that are him, which is what lets a search engine connect this site to his LinkedIn. His city is there and nothing more personal: no street, no postcode, no email, no phone. A test holds that. The home page adds the code itself as `SoftwareSourceCode`, with the repository and its author.

## For AI tools

[`/llms.txt`](https://theyard.stevenstout.biz/llms.txt) is a plain index of the site for language models: who the author is, what TheYard is, and links to the documents as markdown, served by `/api/docs/` and its name, which a tool can read without running the app. A test holds every link in it to a document the site serves.

## Search Console

Google's Search Console proves ownership of a site with a meta tag. The home page keeps a slot for it, and the image build writes the tag into the slot when the repository variable `GOOGLE_SITE_VERIFICATION` is set, and leaves the slot empty when it is not. The token is public by design, so it is a variable rather than a secret. The site was verified on 30 September 2026, its sitemap submitted, and `/about` indexed about ten minutes after it was requested; [Search Console, step by step](https://theyard.stevenstout.biz/?doc=traffic-search-console) walks through each screen with the code behind it.

## How it will show

The activity card groups where readers came from, and search engines are one of the groups ([Who comes](https://theyard.stevenstout.biz/?doc=traffic-who#where-they-came-from)). A crawler's own visits count as scanners and crawlers. The files a crawler reads first, robots.txt, the sitemap and llms.txt, are files, and like the page's scripts they are not counted.

## The decisions behind it

- [ADR-053, The public face](https://theyard.stevenstout.biz/?doc=adr-public-face)
- [ADR-020, Every diagram opens on its own page](https://theyard.stevenstout.biz/?doc=adr-diagrams), the addendum on the page about the author

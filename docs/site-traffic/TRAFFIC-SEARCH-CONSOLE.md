# Search Console, step by step

**How this site was put into Google Search Console on 30 September 2026, one screen at a time, with the code behind each step, and how the same job runs at a company with many sites and many teams.**

Search Console is Google's report on what its crawler sees. It shows whether a page is in the index, which address Google chose for it, what people searched before they clicked, and anything that is broken. Nothing in it moves a ranking. It replaces guesswork about crawling and indexing with Google's own answer. [Being found](https://theyard.stevenstout.biz/?doc=traffic-found) covers what the site offers a crawler. This page covers the other half, where Google reports what it made of it.

## In plain words

This page walks through putting the site into Google's report on what its crawler sees (Google Search Console), one screen at a time, with the code behind each step. Ownership is proven by a tag the build writes into the home page, and a test keeps it there. It ends with how the same job runs at a company.

What that is worth: a developer can repeat every step from the screenshots and the live code, and the organization gets a plan for running it with shared owners, so one departure never takes the property away.

## The order it was done in

| Step | What happened | When (CDT) |
| --- | --- | --- |
| 1 | A property for `https://theyard.stevenstout.biz/`, as a URL prefix | 08:50s |
| 2 | The verification token saved as the repository variable `GOOGLE_SITE_VERIFICATION` | 08:50s |
| 3 | 1.0.3.49 built with the tag in its home page and read live on both sites | 09:16 |
| 4 | Ownership verified by the HTML tag | after the roll |
| 5 | `sitemap.xml` submitted | after verifying |
| 6 | `/about` inspected, not yet on Google, and indexing requested | about 09:20 |
| 7 | Googlebot crawled `/about` | 09:31 |
| 8 | `/about` reported as on Google | by 09:35 |

About ten minutes from the request to the crawl is quick. A new site can wait days for the same thing, and a request only joins a queue.

## Step 1: add a property

A property is the unit Search Console reports on. There are two kinds.

![Search Console's Select property type dialog: Domain covers all subdomains and both protocols and needs DNS verification; URL prefix covers only the address entered, under one protocol, and allows several verification methods](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/search-console-property-type.jpg)

- **Domain** covers every address under the name: `www.`, `m.`, any other subdomain, `http` and `https`. It can only be proven with a DNS record.
- **URL prefix** covers one address and everything under it, on one protocol. It can be proven five ways, one of them a tag in the page.

This site is a URL prefix property. Other things live under `stevenstout.biz`, including [The Shed](https://theshed.stevenstout.biz/), and the proof was wanted inside this repository, passing through the same gate as the code. A DNS record lives outside the repository, in the domain's DNS provider.

## Step 2: prove ownership

Google has to be sure the account asking for the data controls the site. For a URL prefix it offers five ways.

![Ownership verification: You are a verified owner. Verification methods used: HTML tag, successfully verified. Additional methods: HTML file, Google Analytics, Google Tag Manager, and Domain name provider](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/search-console-verified.jpg)

| Method | What it asks for | When it fits |
| --- | --- | --- |
| HTML tag | A `<meta>` tag in the home page's head | The page comes out of a build you control. Used here. |
| HTML file | A named file served from the root | Static hosting where the head cannot be edited |
| Google Analytics | The site's Analytics tag, and edit rights on that property | Analytics is already installed |
| Google Tag Manager | The container snippet, and publish rights | Tag Manager is already installed |
| Domain name provider | A TXT record on the domain | Domain properties, and most companies |

The HTML tag takes four pieces of code and one setting.

**The slot.** The home page keeps a comment where the tag belongs. Built without a token, the comment stays a comment and the page claims nothing.

```live path=index.html region=search-console-slot
```

**The token.** Search Console hands out a token, and it is saved in the repository's settings as a variable, not a secret. Secrets are masked in logs and cannot be read back. The token will be printed into a public page anyway, so hiding it buys nothing, and a variable can be read back to check it ([ADR-072, The code is public, the secrets are not](https://theyard.stevenstout.biz/?doc=adr-secrets)). The three Azure values in the same list are blurred in this picture.

![GitHub's repository variables for TheYard: three Azure values, blurred here, and GOOGLE_SITE_VERIFICATION with its token, updated 48 minutes before](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/github-variable-site-verification.jpg)

**The workflow passes it in.** The deploy workflow hands the variable to the image build as a build argument. The same line is in the cache export's build, so the cached layers match the pushed ones.

```live path=.github/workflows/deploy.yml region=build-cache
```

**The Dockerfile writes it.** After `npm run build`, one step replaces the comment with the tag. A token is letters, digits, underscores and hyphens, so a value with anything else stops the build before it can reach a page.

```live path=Dockerfile region=stamp-the-build
```

**A test holds all of it.** Delete the step, rename the variable or move the value into a secret, and the gate goes red.

```live path=api/TheYard.Tests/DockerBuildInputsTests.cs region=verification-tag
```

To see what Google sees, read the live page:

```bash
curl -s https://theyard.stevenstout.biz/ | grep google-site-verification
# <meta name="google-site-verification" content="aRxQL1arbQs2E1FNf16uCn71BrY3o1IWJVngU9xZ2pU" />
```

The tag has to stay. Google checks again from time to time, and a property whose tag has gone loses its verified owner. That is the reason the tag sits in the build rather than in one version of a file: every image carries it.

Settings is where the property is run from once it is verified: who has access, a move to a new address, the daily export to BigQuery, and whether `robots.txt` reads cleanly.

![Search Console settings: ownership verified, users and permissions naming Steven Stout, associations, change of address, bulk data export to BigQuery, and robots.txt reported valid](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/search-console-settings.jpg)

## Step 3: submit the sitemap

A sitemap lists every address a site wants found. Submitting it tells Google where to start, and the report then shows how many of those addresses it has discovered.

![Search Console's Sitemaps page: /sitemap.xml submitted and read on 30 September 2026, status Success, 119 discovered pages](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/search-console-sitemaps.jpg)

The first read reported *Couldn't fetch*, which is common on a property minutes old. The file was checked live at the time (200, `text/xml`, 119 addresses, valid), and the report read it cleanly later without being submitted again. The 119 are the home page, `/about`, the API reference, the diagram pages and every document at its markdown address. One entry looks like this:

```xml
<url>
  <loc>https://theyard.stevenstout.biz/about</loc>
  <lastmod>__BUILD_DATE__</lastmod>
  <changefreq>monthly</changefreq>
  <priority>0.9</priority>
</url>
```

`__BUILD_DATE__` is replaced with the day the image is built, in the same Dockerfile step as the tag. Google uses `lastmod` only from sites whose dates prove accurate over time, and it ignores `changefreq` and `priority` altogether; other engines may read them. A date written by the build is never a date somebody forgot to change.

A test keeps the sitemap and the catalogue of served documents the same list, both ways:

```live path=api/TheYard.Tests/PublicFaceTests.cs region=sitemap-both-ways
```

The file itself is at [/sitemap.xml](https://theyard.stevenstout.biz/sitemap.xml), and [/robots.txt](https://theyard.stevenstout.biz/robots.txt) names it on its last line.

## Step 4: inspect a URL and ask for indexing

URL inspection answers one question for one address: is it in Google, and if not, why not. `/about` was a day old and not yet on Google, so it was inspected and *Request indexing* was pressed. About ten minutes later it had been crawled, and a few minutes after that it read like this.

![URL inspection for https://theyard.stevenstout.biz/about: URL is on Google, page is indexed, page is served over HTTPS](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/search-console-inspect-about.jpg)

Opening *Page indexing* shows how Google got there, and it reads top to bottom as a checklist:

![The page indexing detail: discovered through the sitemap, no referring page, last crawled 30 September 2026 at 9:31:23 AM by Googlebot smartphone, crawl allowed, page fetch successful, indexing allowed, the user-declared canonical https://theyard.stevenstout.biz/about and the Google-selected canonical the inspected URL](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/search-console-inspect-about-detail.jpg)

| Line | What it says | What sets it here |
| --- | --- | --- |
| Sitemaps | Where Google learned of the address | `public/sitemap.xml` |
| Referring page | A page that links to it, if Google has seen one | None seen yet |
| Crawled as | Which Googlebot fetched it | Google indexes the phone version of a page |
| Crawl allowed? | Whether `robots.txt` lets it in | `public/robots.txt` |
| Indexing allowed? | Whether a `noindex` tag or header keeps it out | None is sent |
| User-declared canonical | The address the page asks to be listed under | `<link rel="canonical">`, written by `AboutPage.cs` |
| Google-selected canonical | The address Google chose | The same one, which is the good outcome |

When the two canonicals differ, Google has decided another address is the real copy, and the page will not be listed under its own. That is the first thing to check when a page that loads fine never shows up.

Some habits that save time here:

- *Test live URL* fetches the page now. The report above it describes the copy Google already holds. After a fix, the live test proves the fix and the request asks for a recrawl.
- *Request indexing* has a small daily allowance per property. It suits one page that matters. For many pages the sitemap is the route.
- The home page was already on Google before any of this. It was requested again so the new `Person` data would be read.

The search index trails the inspection tool. At 09:41 a `site:` search still showed only the home page, with the description written for it in `index.html`:

![A Google search for site:theyard.stevenstout.biz: one result, TheYard, a used-vehicle auction platform, with the home page's description](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/google-site-search.jpg)

## Step 5: check the structured data

Structured data is how the site tells a search engine who the author is, in schema.org's vocabulary rather than in prose. `/about` builds its `Person` in C#:

```live path=api/TheYard.Api/AboutPage.cs region=person-json-ld
```

The home page carries the same `Person`, with the same `@id`, beside the code as `SoftwareSourceCode`:

```live path=index.html region=structured-data
```

Google's Rich Results Test reports only the types Google turns into rich results, and a `Person` on its own is not one of them. The check for it is the [Schema Markup Validator](https://validator.schema.org/), which reads the live page the way a crawler does:

![The Schema Markup Validator on https://theyard.stevenstout.biz/about: the page source on the left, and on the right one Person with 0 errors and 0 warnings, its name, job title, URL, two sameAs profiles and an address of Saint Charles, Missouri, US](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/schema-validator-about.jpg)

The two `sameAs` links, LinkedIn and the repository, are what let a search engine treat this site and those profiles as the same person. The address stops at the city by design, and a test holds that.

## What to read in the weeks after

Reports fill in over the first few days.

- **Performance.** Clicks, impressions, click-through rate and average position, by query, page, country and device, kept for 16 months. A search for the author's name shows up here as a query.
- **Pages.** Every address Google knows, indexed or not, with the reason when not. The common ones: *Discovered, currently not indexed* (known and not fetched yet), *Crawled, currently not indexed* (fetched and judged not worth listing yet), *Duplicate without user-selected canonical* (a second copy with no canonical to settle it), *Alternate page with proper canonical tag* (a copy that correctly points at the real one), *Blocked by robots.txt* and *Excluded by noindex tag*.
- **Sitemaps.** Discovered against indexed, per file. A large gap between the two is worth reading.
- **Core Web Vitals.** Speed and stability as real Chrome users measured them. A small site often shows *not enough data*, and the site's own measurements on [Performance](https://theyard.stevenstout.biz/?doc=performance) stand in until it has some.
- **Manual actions and Security issues.** Empty is the goal. Google also emails the owners when either one appears.

## How it runs at a company

The steps are the same at any size. What changes is who owns the property and how many copies of the site exist.

| Concern | On this site | At a company |
| --- | --- | --- |
| Who owns the property | The author's own Google account | At least two owners, one of them a shared group account, so a departure never takes the property with it |
| Proof | An HTML tag written by the build | A DNS TXT record on the domain, kept in infrastructure code |
| Property type | One URL prefix property | One Domain property for the whole name, plus URL prefix properties per product so each team reads its own slice |
| Environments | Production only | Staging and preview kept out with sign-in or a `noindex` header, never verified or submitted |
| Sitemaps | One file, held to the catalogue by a test | Generated from the database or the routes, split under a sitemap index past 50,000 addresses or 50 MB per file |
| Access | One owner | Owners, full users and restricted users, reviewed with the rest of the access list |
| Reporting | Read by hand | The Search Console API or the daily BigQuery export feeding a dashboard |
| Releases | The gate | A release check: a `robots.txt` diff, canonicals and `noindex` reviewed before launch |
| Moving a site | Not needed | 301 redirects, then the *Change of address* tool |

The classic outage is a staging `robots.txt` shipped to production. It carries `Disallow: /`, which tells every crawler to stop, and traffic falls away over days, too slowly for anyone to link it to the release that caused it. That is why the `robots.txt` diff belongs in the release check.

Two samples for that column. Neither is in this repository.

A Domain property's proof as infrastructure code, for a domain whose DNS is in Azure:

```bicep
resource zone 'Microsoft.Network/dnsZones@2018-05-01' existing = {
  name: 'example.com'
}

// Search Console's Domain property proof. Keep it: deleting it unverifies the owners.
resource searchConsole 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: zone
  name: '@'
  properties: {
    TTL: 3600
    TXTRecords: [
      { value: [ 'google-site-verification=TOKEN' ] }
    ]
  }
}
```

Keeping a non-production environment out of every index, in ASP.NET Core:

```csharp
// Staging and previews answer every request with noindex, so a leaked link never becomes a search result.
// robots.txt cannot do this job: it stops the crawl, and a blocked page can still be listed by its address alone.
if (!app.Environment.IsProduction())
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        await next();
    });
}
```

**Bing is next.** Bing Webmaster Tools does the same job for Bing's index, and it can import a site straight from Search Console, verification included. Bing also takes IndexNow, a protocol for telling search engines about a changed address the moment it changes; Google does not use it.

## Glossary

The words on this page are in the [Site traffic glossary](https://theyard.stevenstout.biz/?doc=site-traffic#glossary-of-traffic-terms): property, verification token, URL inspection and the rest.

## The decisions behind it

- [Being found](https://theyard.stevenstout.biz/?doc=traffic-found), what the site offers a crawler
- [ADR-053, The public face](https://theyard.stevenstout.biz/?doc=adr-public-face), the addendum of 30 September
- [ADR-072, The code is public, the secrets are not](https://theyard.stevenstout.biz/?doc=adr-secrets), why the token is a variable
- [ADR-014, Live code samples](https://theyard.stevenstout.biz/?doc=adr-live-samples), how the code on this page is read from the build

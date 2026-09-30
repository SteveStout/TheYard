# Site traffic

**Who comes to this site, what it keeps about them, and how it gets found: {{live:facts api/TheYard.Tests/ActivityTests.cs}} activity tests and {{live:facts api/TheYard.Tests/LogTests.cs}} kept-log tests hold every rule on these pages.**

The site counts its own traffic and shows it on the Admin tab, to anybody. That is only safe because of what a visit is allowed to leave behind: a daily token, three octets of a network, the page asked for and the store that answered, and nothing that names a person. This section explains the counting, the keeping and the finding, and every rule on it is one a test enforces.

## In this section

```tiles
traffic-who | Who comes | People, scanners and crawlers, and the site reading itself, told apart and counted. | author
traffic-kept | What is kept | What a visit leaves behind, where it is kept, for how long, and what is never kept. | records
traffic-found | Being found | Robots, the sitemap, the page about the author and the data search engines read. | about
```

## The standing rules

- No email address, no full IP address, no user agent and no query string is ever kept ([`Activity.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Activity.cs), the hit's six fields, held by [ActivityTests](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ActivityTests.cs)).
- A visitor is a keyed hash of the day and the address, so nothing joins one day to the next.
- No request waits on the store: a visit is handed to a queue and written in the background.
- The totals are public; the per-visitor rows and the kept log answer only to the operator's key, and are a 404 without it.
- The site's own reads are marked and counted apart, so they never pass for people.
- Every document the site serves is in the sitemap, and a test keeps the two lists the same ([PublicFaceTests](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PublicFaceTests.cs)).

## Glossary of traffic terms

The words these pages use, in plain language, each linked to the section that shows it in use.

```glossary
Visitor token |  | A keyed hash of the day and the visitor's address. The same address is the same token on both sites for a day, and a new one tomorrow. | traffic-who#what-a-visit-carries | Who comes
Network |  | The address cut to its first three octets, enough to see a network or a scanner range and not enough to name a machine. | traffic-who#what-a-visit-carries | Who comes
Visitor-day |  | One visitor on one day, the unit every count on the card is made of. | traffic-who#three-kinds-of-visitor | Who comes
Self-read | TheYard-SelfRead | The mark the site's own tools carry on their requests, so a check the site runs on itself is not counted as a person. | traffic-who#three-kinds-of-visitor | Who comes
Referrer |  | The page that linked here. Only its host is kept, on a page load, and it is grouped as LinkedIn, GitHub, search, another site, or typed. | traffic-who#where-they-came-from | Who comes
Time to live | ttl | How long a document stays in Azure Cosmos DB before the database removes it by itself. | traffic-kept#how-long | What is kept
Ring |  | A fixed-size list held in memory that drops its oldest entry when it is full, which is what the Admin tab read before anything was kept. | traffic-kept#the-kept-log | What is kept
Operator's key | Admin__Key | The one secret that opens the per-visitor rows and the kept log. Without it both are a 404. | traffic-kept#public-and-keyed | What is kept
Sitemap | sitemap.xml | The list of every address the site wants found, read by search engines from the root of the domain. | traffic-found#the-sitemap | Being found
Structured data | application/ld+json | Facts about a page written for machines in schema.org's vocabulary: who the author is and where the code lives. | traffic-found#structured-data | Being found
llms.txt | /llms.txt | A plain index of the site for AI tools, pointing at the documents as markdown they can read without running the app. | traffic-found#for-ai-tools | Being found
Canonical | rel="canonical" | The one address a page wants to be listed under, so the two sites do not compete for it. | traffic-found#the-page-about-the-author | Being found
```

## How it is enforced

```readouts
Activity tests | {{live:facts api/TheYard.Tests/ActivityTests.cs}}
Kept log tests | {{live:facts api/TheYard.Tests/LogTests.cs}}
Documents in the sitemap | {{live:documents}}
Crawler and head tests | {{live:facts api/TheYard.Tests/PublicFaceTests.cs}}
```

Numbers read from the build when the page is opened. The facts, by name:

```text
{{live:fact-names api/TheYard.Tests/ActivityTests.cs}}
```

## The decisions behind it

- [ADR-071, Site activity, and the line an address does not cross](https://theyard.stevenstout.biz/?doc=adr-activity)
- [ADR-073, Logs that outlive the container](https://theyard.stevenstout.biz/?doc=adr-kept-logs)
- [ADR-053, The public face](https://theyard.stevenstout.biz/?doc=adr-public-face)
- [ADR-020, Every diagram opens on its own page](https://theyard.stevenstout.biz/?doc=adr-diagrams), the addendum on the page about the author

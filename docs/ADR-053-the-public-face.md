# ADR: The public face

Status: accepted, 2026-09-03, shipped as 1.0.0.58 and written down now. The
record was owed from the day the work shipped, and a test had been citing it by
name for five versions.

## Context

Most of what this project says about itself is written for a person who has
already opened it: the sidebar, the records, the README's own headings. For a
long time that was all of it. The head of the page held a title and a font.

Four readers here are not people, and between them they see the site before any
human does:

- a search engine, which shows the description and nothing else
- a chat client unfurling the link in Slack or iMessage
- a crawler deciding what to fetch
- an applicant tracking system or a recruiter's parser, reading the head of a
  page somebody pasted into a field

None of them scroll. All of them read the same twenty lines.

## Decision

Say what this is, in the places those readers look.

**A description sized to where it is shown.** Search results truncate near 160
characters, and a description cut off mid-sentence reads worse than a shorter
one that finishes. The test holds it between 50 and 160.

**Open Graph and a Twitter card, with an absolute image URL.** A relative
`og:image` is silently dropped by most unfurlers, which is the failure mode that
looks like it worked: the tags are there, the card is blank, and nothing reports
an error.

**A canonical link**, because the same page answers at several URLs once every
view is a query parameter.

**Structured data that is true rather than flattering.** The JSON-LD declares
`SoftwareSourceCode` with a named author. `Organization` and `Product` are the
types that make a portfolio look like a company, and this is one person's source
code. A schema is a claim, and a claim a reader can check is worth more than one
that reads well.

**A robots file and a sitemap.** Both are only possible because every view here
is a GET URL, which was a decision made much earlier for other reasons. The
robots file disallows `/api/admin/selftest/`, the endpoint that throws on
purpose: a crawler hitting it manufactures real 500s and real Application
Insights exceptions for nothing.

**A preview card drawn from the repository.** The image an unfurler fetches is
generated from the application's own palette, and its numbers are read from the
code at generation time rather than typed. One command writes the SVG and the
PNG together, because when they were two commands they drifted on the first
change that moved a count.

## What a generated number is worth without a test

Nothing, and this is the part worth keeping.

The README said "twenty-nine decision records" while there were forty-five. It
had been wrong for sixteen records. Nobody noticed, because a number in prose
looks the same whether it is right or wrong, and there is no moment at which
anybody re-reads a paragraph they wrote a week ago to check its arithmetic.

So the counts are asserted. Any number word in front of "decision records" in a
living document has to equal the number of records the catalogue serves, and the
preview card's count is checked against the same source, which means adding a
record and forgetting to regenerate the card fails the suite rather than shipping
a card that undercounts.

Three of those checks were themselves too easy at first, and all three are
recorded because the pattern is the interesting part:

- The claim regex ran over the file as written, and an editor had wrapped one of
  the README's two claims across a line break, so the number and the noun were
  separated by a newline and the check never saw it. It normalises whitespace
  now.
- It read the README and nothing else, while "How this was built" carried the
  same count in a sentence of its own. It reads every living document now, which
  means every served document that is not a record and not the changelog, since
  those two are dated by nature and quote counts that were true when written.
- Nothing checked the version on the card at all. The generator reads it from
  the changelog's top line, and on one ship the changelog was momentarily empty
  when it read: it exited zero and wrote a card with no version on it, and every
  check passed, because the record count was right and the file was large enough
  to have rendered. The version is asserted against the changelog now, which is
  the same line the deploy reads.

## Alternatives

**Leaving the head alone.** Defensible while the site was a demo nobody linked
to. It stopped being defensible the moment the link went on a resume, which is
the only reason this project has a domain.

**Hand-drawn preview card.** Faster once, wrong forever after. The counts move
every day this project is worked on.

**Claiming more in the structured data.** `Organization` would unfurl more
impressively. It would also be false, and a reviewer who checks one claim and
finds it inflated stops checking the others.

## Addendum, 2026-09-13: the head said forty-five, and the number came out of it

Both live sites served a meta description claiming forty-five decision records
on 13 September, with seventy in the catalogue. The number was typed into
`index.html` at 1.0.0.58, the same version that put a generated count on the
preview card and wrote, two sections up, that a generated number is worth
nothing without a test. The card had the test. The head did not: the count
scan reads the living documents in the docs catalog, and `index.html` is not
in it, so the description drifted for twenty-five records and fifty-five
versions while every check stayed green. Found by reading the page as a
recruiter's parser would, from outside.

Two fixes were open. Generating the number into the head at build time is the
obvious one, and it is the wrong one here: the image builds the frontend from
an explicit list of inputs that does not include the docs folder (ADR: The
second manifest), so a build-time count would read an empty directory in the
one place that matters. The head now makes no claim a count could contradict.
It says every decision record is served from inside the app, which is true at
any count, and the preview card, which is generated from the catalogue and
held by a test, carries the number.

And the scan reads `index.html` now, so a count typed back into the head is
held to the catalogue like every other. Two lines in `PublicFaceTests`.
Nothing here decided differently about counts; it decided that the list of
documents a count can live in was one short.

## Consequences

- The link unfurls with a real card, and a search result shows a sentence rather
  than a truncated heading.
- Every count this project states in a living document is held to the code.
- The crawler files went live and answered 404, because the image is built from
  an explicit list of COPY lines that did not mention the folder they live in.
  That is its own record (ADR: The second manifest); it is named here because
  this work is what exposed it.

## Files

- [`index.html`](https://github.com/SteveStout/TheYard/blob/main/index.html): the head, and everything in it that is not for a person.
- [`public/robots.txt`](https://github.com/SteveStout/TheYard/blob/main/public/robots.txt): what a crawler may fetch, and the one endpoint it may not.
- [`public/sitemap.xml`](https://github.com/SteveStout/TheYard/blob/main/public/sitemap.xml): the views, which are URLs because they always were.
- [`docs/images/og.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/og.mjs): the card, its palette and the counts it reads from the repository.
- [`api/TheYard.Tests/PublicFaceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PublicFaceTests.cs): the claims, held to the code.
- [`docs/ADR-047-the-second-manifest.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-047-the-second-manifest.md): what shipping these files taught us about the image.

## Addendum, 2026-09-29 (1.0.3.48): read without the app

Steve: "do anything to make this more indexable or searchable by AI and search engine." A search engine renders the app; most AI tools do not run JavaScript and were reading an empty `<div id="root">`. Three changes, each held by a test. `index.html` carries a `<noscript>` summary: his name, what TheYard is, and links to /about, the README, the architecture, Performance, the API reference, llms.txt and the repository, all addresses the site serves without the app. `public/llms.txt` is the plain index language models look for: a title, a one-paragraph summary as a quote, and links to the documents as the markdown `/api/docs/` serves, each held to the catalogue by `PublicFaceTests`. And the sitemap lists every document in the catalogue at its markdown address, {{live:documents}} today, held to `DocumentationCatalog.Files` in both directions so a document cannot go unlisted or stay listed after it is gone. robots.txt already welcomed every crawler and is unchanged.

## Addendum, 2026-09-30 (1.0.3.50): verified, and written down

Steve asked for the Search Console work to be written up on the site with code samples and screenshots, as a way into a field he knew from the outside and had not yet run at a company. The property is a URL prefix, `https://theyard.stevenstout.biz/`, proven by the HTML tag the image build writes from the repository variable `GOOGLE_SITE_VERIFICATION` into the slot 1.0.3.47 left for it; 1.0.3.49 was the first image built with the variable set. Ownership was verified on 30 September, the sitemap read as Success with 119 addresses, and `/about` was crawled at 09:31 CDT, about ten minutes after indexing was requested, and reported on Google by 09:35.

[`docs/TRAFFIC-SEARCH-CONSOLE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/TRAFFIC-SEARCH-CONSOLE.md) is the walkthrough, a fifth page in Site traffic. Its code is read from the build rather than pasted, so five regions were marked for it: `structured-data` and `search-console-slot` in `index.html`, `person-json-ld` in `AboutPage.cs`, `sitemap-both-ways` in `PublicFaceTests.cs` and `verification-tag` in `DockerBuildInputsTests.cs`; LiveSampleCoverageTests holds each block to rendering as code, on the checkout and in the image. The two samples for a company, a DNS record in Bicep and a `noindex` header in ASP.NET Core, are marked on the page as not in this repository. The nine pictures are screenshots taken in the owner's browser on the day. On the GitHub variables screen the three Azure identifiers were blurred before the file was written: the page is public, and a reader does not need them to follow the step.

A Domain property, proven by a DNS record, was not added. It would cover every name under `stevenstout.biz`, which is more than this repository speaks for, and its proof would live in the DNS provider rather than pass through this gate. The page explains it as the usual choice at a company.

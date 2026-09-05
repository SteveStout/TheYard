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

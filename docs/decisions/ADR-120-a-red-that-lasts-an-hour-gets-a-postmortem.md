# ADR: A red that lasts an hour gets a postmortem

Status: accepted, 2026-10-25, at 1.0.4.78. Any Admin tab grade that reads red for 60 minutes or more, and any day whose cost is more than twice the median of the seven days before it, opens an incident file under `docs/incidents/` within the next lane. Every file keeps one shape, eight sections in one order, and `IncidentShapeTests` fails the build when one does not. The first is INC-001, the share that billed for looking.

## Reading list

1. Google, [Site Reliability Engineering, chapter 15: Postmortem Culture: Learning from Failure](https://sre.google/sre-book/postmortem-culture/): the written record of an incident, its impact, the actions taken and the follow-ups, written without blame. Read 2026-10-25.
2. Atlassian, [Incident postmortem: process and best practices](https://www.atlassian.com/incident-management/postmortem): a postmortem for every incident over a set severity, written within days of the fix while the record is fresh. Read 2026-10-25.
3. [INC-001: the share that billed for looking](https://theyard.stevenstout.biz/?doc=inc-001-the-share-that-billed-for-looking): the first one, and the reason for this record.
4. [ADR: One frame for every resource](https://theyard.stevenstout.biz/?doc=adr-one-frame-for-every-resource) and [ADR: What Azure charges](https://theyard.stevenstout.biz/?doc=adr-azure-costs): where the cards' tones and the cost card's day rows, the two things this record reads, come from.

## In plain words

When something on the site breaks for an hour, or a day costs twice what the week before it did, I write down what happened: when, what it cost, why, why nothing caught it sooner, and what changed. Each one is a page under Incidents in the sidebar, in the same eight sections every time, so they can be read side by side. The point is not the page. The point is that a red I fixed once does not come back the same way.

## Context

On 20 to 23 October a file share cost $49.29 over four days, $44.01 above its baseline, and the Admin tab never went red. It was found from a phone. The fixes went in the next morning, three versions and four changes made in place (the mount, the tier, the alert and the budget), and the account of it lived in a lane's DONE note on the job-hunt drive, which a reader of the site cannot open. The site had decision records for every choice and nothing for the times a choice went wrong.

The two triggers are the two ways the site has found out about trouble: a card's grade, which the Admin tab already draws every minute, and the day rows of the cost card, which is where INC-001 was visible all along. Twice the median of the seven days before it is a line the day rows crossed on 20 October, the first day ($3.07 against a median of $1.27 for the seven before it), three days before the worst of it.

## Decision

1. **The triggers.** A grade on the Admin tab red for 60 minutes or more, or a day's cost over twice the median of the seven days before it, opens an incident.
2. **The file.** `docs/incidents/INC-<nnn>-<slug>.md`, numbered in order, served through the same docs route the records use and listed in `docs/incidents/INCIDENTS.md`, the section's index.
3. **The shape.** A title `# INC-<nnn>: <title>`, a status line, "In plain words", then Summary, Timeline, Impact, Root cause, Detection gap, What changed, What stays true and Follow-ups, in that order. The Timeline is a table with a source named in every row. What changed is a table whose every version is a line of the changelog. Follow-ups carry a state column, read from the cards or the repository.
4. **Who writes it.** The lane after the one that saw the red. When the Foreman ships, it drafts the Timeline from the cards' recorded minutes and a person writes the rest.
5. **What the Admin tab gains.** Nothing. The index is a docs page.

## Consequences

- Every incident costs a page and a row in the index, written while the sources are still fresh.
- The shape is held by a test, so an incident with no timeline sources or a version the changelog never shipped is red at the gate.
- The record does not decide who is to blame, and the template has no section for it: the root cause names the mechanism.

## What holds it

`IncidentShapeTests`: every file under `docs/incidents/` other than the index has the title, the status line and the eight headings in order; its Timeline is a table with a source in every row; its What changed table names only versions the changelog has; its Follow-ups table carries a state in every row; and the index lists every incident by its id.

## Where it sits

A record about how the site is run, not how it is built: it touches no ring of the onion. Single responsibility is the principle it serves at the scale of a document, one page per incident and one shape for all of them. It cost a docs section, an index and one test. What would change it: a third way the site finds out about trouble, which would be a third trigger written here first as an addendum.

## Files

- [`docs/incidents/INCIDENTS.md`](https://github.com/SteveStout/TheYard/blob/main/docs/incidents/INCIDENTS.md) - the index
- [`docs/incidents/INC-001-the-share-that-billed-for-looking.md`](https://github.com/SteveStout/TheYard/blob/main/docs/incidents/INC-001-the-share-that-billed-for-looking.md) - the first incident
- [`api/TheYard.Tests/IncidentShapeTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/IncidentShapeTests.cs) - the shape, held

## Addendum, 2026-10-25 (1.0.4.80): the Admin tab reads the index

Decision 5 said the Admin tab gains nothing. It gains one card under What broke?, and the rail gains no row of its own: the card lists the index's rows and links each to its postmortem, beside the failures Application Insights kept over seven days, so the incidents and the errors that could become the next one are read in one place (ADR: The Admin tab, as a product, the addendum of 1.0.4.80). The index stays the one list; the card reads it and types nothing.

## Addendum, 2026-10-25 (1.0.4.82): the Foreman ships without drafting

Decision 4 said the Foreman drafts the Timeline when it ships. It shipped at 1.0.4.82 at rung 0, a page with no model (ADR: The Foreman, a place to start), and drafts nothing: its page names the cards that moved, the nearest change and where to start, which are a timeline's first rows, but the incident file is still written by the lane after the red. The drafting waits for the Foreman's model rung.

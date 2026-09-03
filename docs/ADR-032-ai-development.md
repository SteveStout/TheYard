# ADR: Saying how it was built

Status: accepted, 2026-09-03, shipped as 1.0.0.42. Steve's ask: "document the
AI-assisted development methodology."

## Context

This application was written with heavy AI assistance, and a reader who works
that out on their own draws worse conclusions than a reader who is told. The
repository is already unusual: thirty-two decision records, three suites, live
code samples that expand from the working tree, an Admin tab that reads the
container's own state. Unexplained, that reads as either a very thorough
engineer or a very productive text generator, and the difference matters more
than anything else on the page.

The obvious way to handle it is a paragraph in the README saying "built with
AI assistance." That is a disclosure, not an argument, and it invites the three
objections it is trying to answer rather than meeting them.

## Decision

**A document that names the criteria and points at the evidence**, served in
the About menu beside the README, at `docs/AI-DEVELOPMENT.md`.

It is organised as questions a reviewer would ask, each answered with a place
in this repository to look: whether the code gets explained, whether the
documents track the code, whether there is judgment as well as generation,
whether anything is measured, whether mistakes are found and written down,
whether the thing can be operated, and whether the system is honest about
itself.

**The evidence is specific, and most of it is unflattering.** The eleven
defects a self review found in code that had already passed its tests. The
screenshot that caught a live defect. The style check that failed in CI on the
same command that had just passed locally. The record that predicted a failure,
chose a procedural guard, and watched the guard fail the next day. The test
written to assert a shape the API did not actually have. A methodology document
that only listed successes would be the exact thing it is trying to disprove.

**Three red flags are named before the reader gets there.** That nobody
understands code they did not write; that thirty-one records is documentation
theatre; that a green suite proves nothing when the tests were written
alongside the code. Each gets a straight answer, and the first one gets a
partial concession: understanding is not proven by a document claiming it, so
the document offers surface to test instead and says so.

**No rubric is quoted, because none was given.** The criteria are the ones a
reviewer of AI-assisted work would reasonably apply, stated openly so they can
be argued with. Inventing someone else's scoring sheet would be the wrong kind
of confidence.

## What was deliberately left out

The brief for this record mentioned a mutation-testing episode that proved a
green suite blind. That never happened in this repository. Mutation testing has
not been run here, and writing it up would have made the one document about
honesty the only dishonest thing in the project. The same point is carried by
things that did happen: the checks proven to fail on deliberately bad input,
the suites that failed in ways that changed the code, and the ship gate that
verifies the deployed result from the public domain rather than from the
pipeline's own account of itself.

## Consequences

- The document dates quickly on purpose. It cites specific records and specific
  incidents, so it has to be revisited when those change, which is the right
  cost for a claim about method.
- It is served like every other document here, from the repository, through the
  API, so it cannot describe a version of the project that is not the one
  running.
- It gives an interviewer a set of questions to ask. That is intended. The
  document is only worth anything if the conversation it invites goes well.

## Files

- [`docs/AI-DEVELOPMENT.md`](https://github.com/SteveStout/TheYard/blob/main/docs/AI-DEVELOPMENT.md): the document.
- [`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx): the About menu entry.
- [`api/TheBlock.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/DocsCatalog.cs): the slug that serves it.
- [`docs/ADR-014-live-samples.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-014-live-samples.md): why a document here cannot quietly drift from the code it describes.
- [`docs/ADR-027-competing-bidders.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-027-competing-bidders.md): the eleven defects the document cites.

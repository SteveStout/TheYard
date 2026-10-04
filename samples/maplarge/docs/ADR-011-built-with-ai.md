# ADR: Built with AI

Status: accepted, 2026-09-29.

## Context

The brief says using AI is not required and asks, in the recording, how it was used here and how
it is used day to day. This record is the written answer, so the recording can point at it, and it
is written to be exact about who did what, because a vague answer to that question is worse than
none.

## Decision

The project was built with an AI assistant (Claude) in the working method TheYard was built with:
the shape is decided and the tests are listed before any code. The assistant drafts against that
list, and a person reads the result before it goes anywhere.

**Decided by a person, before code.** What the brief was asking for and how it would be read; that
the answer is TheYard's practices applied to their starter and not TheYard's stack (ADR-001); plain
TypeScript with no framework; the palette carried over and the branding dropped (ADR-010); that
the sample lives beside TheYard and deploys on its plan. The assistant put the rest to him as a
written plan first: the layering (ADR-002), the guard (ADR-003), the wire (ADR-004), the address as
the only state (ADR-005), the list of tests that became ADR-009. That plan was approved, with the
name chosen by the assistant and accepted, before the first file was written.

**Drafted by the assistant.** Every file, from the plan and the test list, tests included. The
draft was reviewed as it was written and corrected on the way: the first file browser rebuilt the
search box on every render and would have taken the caret from someone typing, and asked its
questions with `window.prompt`; the shipped one keeps the input and asks in the row. The build ran
green on a machine, not in the assistant's head, before anything was committed.

**Read by a person before it is sent.** Every file, with the assistant's own three-reader review
(a tester, a reviewer, a staff engineer) beside it as a checklist. The recording is that
walk-through.

How the same assistant is used day to day: the first draft of a class against a test that was
written first, the first draft of a record, a second reader on a diff, and the tedious half of a
change (rename a field across forty files, write the twelfth test in a pattern the first eleven
set). The decisions and the last read are not delegated, because those are the parts that have to
be right and the parts a reviewer asks about.

## What it cost

Reading everything before shipping it, which is slower than trusting it and faster than typing it.
The records exist partly so the next reader can tell which choices were made on purpose and why.

## Where it sits

This record is about how the project was built and touches no ring of the code.

## Files

- [`docs/BUILT-WITH-AI.md`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/docs/BUILT-WITH-AI.md): the working method, as a guide.
- [`docs/ADR-009-the-rules-a-change-has-to-pass.md`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/docs/ADR-009-the-rules-a-change-has-to-pass.md): the test list that came first.

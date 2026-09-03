# ADR: The exemption that hid a contrast failure

Status: accepted, 2026-09-03. Not a feature. Five defects found in one
afternoon, all of the same species: a check that answered an easier question
than the one it was written for.

## What happened

The browser suite went red on CI and could not say why. Fixing that (ADR: The SQL
Server backend, addendum) made the next run readable, and the first thing it read
out was not a flake:

```
axe.spec.ts > WCAG 2.1 AA, on every view > a vehicle
color-contrast (serious) on 1 node(s)
first: header > ._countdown._ended
Element has insufficient color contrast of 3.82
(foreground #6f737e, background #e9e6e7, 13px, normal weight)
Expected contrast ratio of 4.5:1
```

An auction that has ended paints its countdown in the palette's faint colour on
the page ground. That is normal-size text and it needs 4.5:1. It measures 3.82.

It is intermittent only in the sense that you have to open a vehicle whose
auction has ended to see it, which the suite does when the dataset's clock puts
one at the top.

## Why the palette test passed

The repository has a test that computes the WCAG ratio for every text and ground
pair the site uses. It contained this:

```ts
it('faint labels clear AA on white, where they sit, and 3:1 on the ground', () => {
  expect(contrast(token('color-text-faint'), token('color-surface'))).toBeGreaterThanOrEqual(4.5);
  expect(contrast(token('color-text-faint'), token('color-bg'))).toBeGreaterThanOrEqual(3);
});
```

Somebody decided faint labels live on white, and wrote a 3:1 floor for the page
ground on the strength of "where they sit". The stylesheet did not agree.
`AuctionCountdown.module.css` paints `.ended` in `--color-text-faint` at 13px on
the page ground, and has since it was written.

This is the same failure ADR: The accessibility check recorded, which found two
contrast bugs in a repository whose palette test was passing, and named the
cause: a test holds the pairs a person listed while a stylesheet composes
whatever it likes. The lesson was recorded and then a new exemption was written
anyway, which is worth saying plainly rather than filing as bad luck.

## Decision

**The exemption goes, not just the colour.** `--color-text-faint` is `#61656e`,
which measures 4.71 on the page ground and 5.84 on white. The test now requires
AA on both grounds:

```live path=src/styles/tokens.test.ts region=composed-pairs
```

A token used for text clears AA on every ground this site puts it on. If a future
label really is large text, it can say so with its own assertion rather than by
lowering everybody's floor.

## The same species, four more times

**A test that waited for a heading instead of a response.** `account.spec` clicks
"Create an account" and then waits five seconds for a heading with the new
address. Five seconds is Playwright's default for a UI assertion, and what sits
behind this one is a network round trip whose cost is dominated by a password
hash that is deliberately expensive: 120 ms on an idle machine, measured, and
longer when the whole suite is sharing the CPU with a simulated room bidding over
a hundred thousand vehicles. When it overran, the failure said "heading not
found", which is true and useless: the page snapshot showed the form exactly as
it was before the click, with no error on it, which is what a request still in
flight looks like.

It waits for the response now and asserts its status. That is not a longer
timeout in a disguise. It waits for the thing the test is blocked on, and a
rejected registration reads as a rejected registration.

**A test that raced the feature it was testing.** `market.spec` read the minimum
next bid off a placeholder and then posted it, while the simulated room raised
prices every eight seconds. Recorded in ADR: The SQL Server backend, because it
was found during that work, and it belongs in this list.

**A test that asserted a title the page never had.** The ERD arrived in the
diagram catalogue as `"TheYard's database"`. Diagram pages put the title in the
tab through `WebUtility.HtmlEncode`, which is right, and turns that apostrophe
into `&#39;`. The catalogue test asserted the raw string. It had passed for two
years of diagrams because the only two titles in the catalogue were "TheYard
infrastructure" and "TheYard data flow", neither of which contains a character
that encoding touches. The first title that did broke it, and the failure named
the substring rather than the rule.

It asserts `WebUtility.HtmlEncode(diagram.Title)` now, so it holds for any title,
and a second test pins the apostrophe case on its own so the rule cannot drift
back to a coincidence.

**A test that measured a cold start and reported on the keyboard.** `a11y.spec`
opens with `page.goto('/')` and asserts the Inventory heading, then walks the
keyboard path. Until the first query answers, the view is the words "Loading
inventory", and the heading does not exist. So line 9 of a test named for the
Tab key was also asserting that Vite's first compile and the API's first query
finish inside five seconds. On one run in three, with four workers arriving at an
unwarmed server together, they did not, and the suite reported a keyboard
failure with the keyboard never pressed.

The suite loads the app once in a `globalSetup` that is allowed to be slow, so
the five-second budget in the specs measures what the specs are named for. A
server that is actually down still fails, earlier and in a sentence that says so.

## What these five have in common

Each one was a check that answered a slightly easier question than the one it was
written for. Does faint clear 3:1 on the ground, rather than does faint clear AA
where it is actually used. Did a heading appear within five seconds, rather than
did the server accept the registration. Was a bid accepted, rather than was the
bid the current price. Does this exact string appear, rather than does the title
reach the tab correctly. Did the page render in five seconds, rather than does
the Tab key land where it should.

Three of the five had passed for months. That is the tell: a check of this kind
does not announce itself, it waits for the first input that separates the easy
question from the real one.

An easier question is not a smaller version of the real one. It passes when the
real one would fail, which is the only property that matters in a gate.

## Consequences

- One serious WCAG 2.1 AA failure is gone from the vehicle page, on the element
  that tells you an auction is over.
- Every use of `--color-text-faint` got slightly darker. It is used for
  timestamps, captions and secondary labels, and at 4.71 against 3.82 the change
  is visible if you look for it and invisible if you do not.
- The palette test is stricter than it was, and one line shorter.
- Four tests now fail with a sentence that names the cause instead of the symptom,
  and one of them is a .NET test that had been asserting a coincidence.
- The suite is slower by nothing measurable: waiting for a response returns as
  soon as the response arrives, which is faster than waiting out a fixed budget.

## Files

- [`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css): the colour, and the measurement in the comment beside it.
- [`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts): the exemption that is no longer there.
- [`src/components/AuctionCountdown.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/AuctionCountdown.module.css): where the pair was composed.
- [`tests/e2e/account.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/account.spec.ts): waiting for the answer rather than for the consequence.
- [`tests/e2e/market.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/market.spec.ts): answering the price it posts against.
- [`api/TheBlock.Tests/DiagramPageTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/DiagramPageTests.cs): the encoded title, and the apostrophe pinned on its own.
- [`tests/e2e/warm-up.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/warm-up.ts): the cold start, paid once, somewhere it is allowed to be slow.
- [`playwright.config.ts`](https://github.com/SteveStout/TheYard/blob/main/playwright.config.ts): where that runs from.
- [`docs/ADR-035-accessibility-check.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-035-accessibility-check.md): the first time this exact thing happened, and the sentence that should have prevented this one.

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

## Addendum, 2026-09-03: the fix that was too narrow

The cold-start case above was fixed by warming the app once in a `globalSetup`,
and that fix was too small. It helps whichever test runs first. Two versions
later the same failure came back in the middle of a run, in a different spec:

```
a11y.spec.ts:27 opening a vehicle, and coming back, moves focus to the view
  Locator: locator('article h3 button').first()
  Timeout: 5000ms
  Error: element(s) not found
```

A focus test, failing because no vehicle tile existed yet. Same cause, different
name on the failure, and it was never about the first test: every spec here opens
with a navigation and then asserts against a loaded app, and every one of those
first assertions was carrying an unstated five-second load budget.

So the fix is now where the navigation is, and every navigation goes through it:

```ts
export async function openTheYard(page: Page, path = '/'): Promise<void> {
  await page.goto(path);
  await expect(page.getByText('Loading inventory')).toHaveCount(0, { timeout: 45_000 });
}
```

The load gets its own budget once, at the point where waiting is the actual
subject. Everything after it is back on five seconds, which is the right budget
for "did clicking this do the thing" and the wrong one for "has the server
finished starting".

The warm-up stays, because the two cover different costs. `openTheYard` waits for
the first query, which is an assertion timeout. The navigation itself is what
pays for Vite compiling the module graph, and that is bounded by the navigation
timeout, which no assertion budget can widen.

The lesson to keep is not about Playwright. A fix aimed at the instance rather
than the class passes the run in front of you and leaves the defect in place,
and the second sighting is more expensive than the first because by then the
first one is written down as solved.

## Addendum, 2026-09-03: the check that could not be read

Two CI runs failed and said nothing. The run page's only public words were
"Process completed with exit code 1"; the report artifact holding the answer is
253 KB and behind a GitHub sign-in.

The first attempt at fixing that wrote the suite's output into a job summary,
which also renders only for somebody signed in. The second attempt emitted
annotations, which come back from the public API and sit at the top of the run
page, and on its first red run it printed:

```
1) tests/e2e/axe.spec.ts:66:3 > WCAG 2.1 AA, on every view > the admin tab
   Error: expect(received).toEqual(expected)
   + Received  + 3
   43 passed (1.8m)
```

Three violations, and three is the number of scrollable table containers the SQL
section had just added. A box with `overflow-x: auto` and nothing focusable
inside is a region a mouse can scroll and a keyboard cannot, which is WCAG 2.1.1
and which axe names `scrollable-region-focusable`.

It fires only when the box actually overflows. On a wide development window the
tables fit, so eleven local runs of the suite reported nothing, and the runner's
narrower viewport made all three overflow at once. That is the same species as
everything else in this record from the other direction: not a check that asked
an easier question, but a real check that could only be read by somebody with
credentials, on a defect that only appeared where nobody was looking.

## Correction, later the same day: it was not the machine

The addendum above ends by saying the load wait was moved to every navigation.
What it does not say, and what was said out loud at the time, is that three ship
attempts in a row came back two green of three, and that this was attributed to
the machine:

> the local browser gate has stopped being a measuring instrument

That call was made on real measurements. The same suite ran in 2.0 minutes at
midday and 3.4 in the evening, and the .NET suite was unchanged, so something
about the machine had changed. All of that was true and none of it was the cause.

Since `openTheYard` started waiting for something that is there rather than for
something that is gone, the browser suite has run twelve times in a row without a
failure, at 1.7 to 1.9 minutes:

```
1.0.0.56   44 passed (1.8m)   44 passed (1.7m)
1.0.0.57   44 passed (1.7m)   44 passed (1.7m)
1.0.0.58   44 passed (1.8m)   44 passed (1.7m)
1.0.0.59   44 passed (2.2m)   44 passed (1.9m)
1.0.0.59b  44 passed (1.8m)   44 passed (1.7m)
1.0.0.60   44 passed (1.8m)   44 passed (1.8m)
```

So the defect was the helper, and the slow evening was a coincidence that fitted.
The reasoning was written down honestly and it was still the wrong conclusion,
which is worth keeping for a reason that has nothing to do with Playwright: a
measurement that is real and an explanation that is true are different things,
and having taken the trouble to measure makes the explanation feel earned. It
should not. The measurement said the machine was slower. It never said the
slowness was what broke the tests.

The rule this leaves: an environmental explanation is the one to hold most
loosely, because it is unfalsifiable in the moment and it exonerates the code.
Ship on it if the alternative is worse, say so out loud as was done here, and
then go back and check, which is what this paragraph is.

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

## Addendum, 2026-09-03: the annotation that could not have said it

The addendum above ends with annotations working, and the record left it there.
This one is about the direction they fail in.

An annotation assembled by grepping a suite's output is a check with no check on
it. If the pattern matches nothing the step still exits zero, the annotation list
is still empty, and the run page still reads "Process completed with exit code
1". Nothing anywhere goes red. The reporting is broken in exactly the state where
it looks identical to reporting that had nothing to report, and you find out on
the day a job fails and the page is blank again.

Two of the patterns were in that state.

`dotnet test` prints its totals two ways. Left alone it writes a single line:

```
Passed!  - Failed:     0, Passed:   281, Skipped:     0, Total:   281
```

Asked for `--logger 'console;verbosity=normal'`, which is what this job asks for
so that individual test names appear, it writes a block instead:

```
Test Run Failed.
Total tests: 252
     Passed: 251
     Failed: 1
```

The pattern knew the first spelling. The job produces the second. The first
attempt at fixing this only anchored the leading whitespace, which was the wrong
correction confidently applied: it was aimed at the format the job does not
produce, and it was checked against a transcript that happened to be the format
the job does not produce.

Playwright's gap was the same species facing the other way. The pattern matched
`Timeout of` and Playwright writes `Test timeout of 90000ms exceeded.`, so the
one line describing a hang was dropped. A hang is the failure shape with no
locator, no expectation and no received value, which means that line was not the
best evidence, it was the only evidence.

### What holds it now

The patterns are read back out of `ci.yml` by a test, translated from POSIX to
.NET (only the two character classes the workflow actually uses, and anything
else is refused rather than quietly compiled into something that means something
different), and run over real failing transcripts from both suites kept as
fixtures. The assertions are written as what a reader needs: the failing test's
name, the assertion message, the stack trace and the totals for .NET; the spec,
the locator, the timeout headline and the thrown message for the browser suite.
A second pair asserts what must stay out, because GitHub shows a handful of
annotations and forty passing specs would push both failures off the top.

Two of the nine fail against the patterns as they were. The other seven were
already passing and are held anyway, which is the point: the line that names the
failing test is one careless edit away from matching nothing, and nothing else in
this repository would notice.

The general shape, which is the third time this record has arrived at it from a
different direction: a check that cannot fail is not a check. An exemption made
one ask an easier question, a report nobody could read made one unusable, and a
pattern matching nothing makes one silent. All three are green.

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
- [`api/TheYard.Tests/DiagramPageTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/DiagramPageTests.cs): the encoded title, and the apostrophe pinned on its own.
- [`tests/e2e/warm-up.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/warm-up.ts): the cold start, paid once, somewhere it is allowed to be slow.
- [`playwright.config.ts`](https://github.com/SteveStout/TheYard/blob/main/playwright.config.ts): where that runs from.
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the two steps that turn a red suite into something a stranger can read.
- [`api/TheYard.Tests/CiAnnotationTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CiAnnotationTests.cs): the patterns read back out of the workflow and run against real failures.
- [`docs/ADR-035-accessibility-check.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-035-accessibility-check.md): the first time this exact thing happened, and the sentence that should have prevented this one.

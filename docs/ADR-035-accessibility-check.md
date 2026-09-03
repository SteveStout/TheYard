# ADR: The accessibility check

Status: accepted, 2026-09-03, shipped as 1.0.0.44. Steve's ask, as part of the
performance and accessibility pass: "an axe or Playwright accessibility check
that runs in CI."

## Context

There were already two things here that looked like accessibility coverage.

`tests/e2e/a11y.spec.ts` walks the keyboard path (ADR: The keyboard path): the
skip link, focus moving to the view that changed, the live region naming where
you arrived. Four tests, all passing.

`src/styles/tokens.test.ts` reads `tokens.css` with `?raw` and computes the
WCAG contrast ratio for every colour pair the palette uses, asserting 4.5:1 for
text and 3:1 for graphics (ADR: The palette). Fifteen assertions, all passing.

Both of those are real and neither of them can see the page. The keyboard suite
tests the paths it was told about. The palette test checks the pairs somebody
listed. Between them they had never once looked at what the browser actually
renders.

## What it found on the first run

Two violations, both `color-contrast`, both rated serious, on four of the six
views checked, and both on elements that appear on nearly every screen.

| element | foreground | background | ratio | needed |
| --- | --- | --- | --- | --- |
| "Reserve not met" badge | `#62666f` | `#e4e0e1` | 4.39 | 4.5 |
| the live countdown on a vehicle | `#15803d` | `#e9e6e7` | 4.04 | 4.5 |

Fifty-two nodes on the inventory page alone, since the badge is on every card.

**Why the palette test missed them is the interesting part.** It checks
`--color-text-muted` against `--color-surface` and `--color-bg`, and both pass.
The badge puts that same colour on `--color-neutral-soft`, which is a pair
nobody wrote an assertion for. Likewise `--color-success` is checked against
`--color-success-soft` and against white, and the countdown puts it on
`--color-bg`.

Neither of those pairs is exotic. They are just pairs that were composed by CSS
rather than by a person writing a test, and that is the whole category the
enumerated test cannot cover: it holds the combinations somebody thought of,
and a stylesheet combines whatever it likes.

## Decision

**axe-core through Playwright, on six views, at WCAG 2.1 AA, with zero
tolerance.** The inventory and a vehicle at desktop width, the Admin tab, an
open document dialog, and the inventory and drawer on a phone. Each is its own
test so a failure names the view. It runs inside the existing browser job, so
it is in CI without a new job or a new runner.

**The tags are `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`.** Best-practice
rules outside the standard are deliberately not included: a check that fails on
advice rather than on a standard is a check people start ignoring.

**The two colours moved rather than the threshold.**

| token | before | after | on | before | after |
| --- | --- | --- | --- | --- | --- |
| `--color-text-muted` | `#62666f` | `#5f636c` | `--color-neutral-soft` | 4.39 | 4.60 |
| `--color-success` | `#15803d` | `#146c34` | `--color-bg` | 4.04 | 5.25 |

Both changes are darkenings, so every other pair either improves or is
unaffected, and the existing palette assertions still hold.

**The palette test keeps its job and gains the two pairs it was missing.** It
is not made redundant by axe: it runs in under a second in the unit suite and
fails before a browser is started. It is a floor, and the record now says so
where the test can be read.

## What this does not do

Automated tooling catches a minority of accessibility problems, and the
majority it misses are the ones that matter most: whether a label says
something a person can act on, whether the reading order makes sense to
somebody who cannot see the layout, whether an error is announced at a moment
that helps. This check finds none of that.

The README has said since its first version that an audit with a real screen
reader is a person's job. That is still true, and this changes nothing about
it. What it changes is that the mechanical half is no longer being done by
inspection.

## In the code

The check (`tests/e2e/axe.spec.ts`):

```live path=tests/e2e/axe.spec.ts region=axe
```

The two pairs the palette test now also holds
(`src/styles/tokens.test.ts`):

```live path=src/styles/tokens.test.ts region=composed-pairs
```

## Consequences

- Six more browser tests, from 31 to 37, adding a few seconds to the suite.
- A colour change that fails AA now fails in two places, one of them fast.
- The live site had two serious contrast failures on it for as long as those
  colours have existed, and both were on the busiest elements on the page.
  That is the argument for the check, and it is worth stating plainly rather
  than being folded into a changelog line.
- `AuctionCountdown.module.css` still has two literal hex colours in it for the
  text over a photo, which is a separate rule this project sets for itself and
  breaks in one file. It is not fixed here, because the overlay sits on an
  image rather than a token and needs a different answer.

## Files

- [`tests/e2e/axe.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/axe.spec.ts): the check.
- [`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css): the two colours that moved.
- [`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts): the enumerated floor, with the two pairs it was missing.
- [`tests/e2e/a11y.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/a11y.spec.ts): the keyboard path, which is the half a machine cannot check for you.
- [`docs/ADR-016-palette.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-016-palette.md): where the palette and its measurement came from.
- [`docs/ADR-026-keyboard.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-026-keyboard.md): the focus work this sits beside.

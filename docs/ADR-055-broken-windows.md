# ADR: Broken windows, and the rule that answers them

Status: accepted, 2026-09-04. Asked for by name. Written with this repository's
own week as the evidence, because a record about discipline that cites nothing
is the exact thing it is warning about.

## Two ideas with almost the same name

The **broken window fallacy** is Bastiat, 1850, and it is about economics: a
smashed shop window is not a stimulus, because the glazier's gain is paid for by
everything the shopkeeper now cannot buy. It is an argument about unseen costs.

**Broken windows theory** is Wilson and Kelling, 1982, and it is about signals:
one window left broken in a building says nobody is watching, and the second one
follows more easily than the first. Hunt and Thomas brought it into software in
1999, and it is the one this record is about.

They are not the same idea, and the software one borrows something from the
other anyway: the cost of a broken window is unseen. Nobody can point at the
hour it took.

## What a broken window is here

Not a bug. A bug is loud and gets fixed. A broken window is small, visible, and
tolerated:

- A comment that was true when it was written and is not any more.
- A test that is skipped, or focused, or asserts something easier than its name
  claims.
- A check that runs somewhere only one person can run it.
- A citation pointing at a document that does not exist.
- A number in prose that was right two weeks ago.
- A card at the top of the front page that says "Ended".

Each of those is defensible on its own. What they do together is teach the next
reader, who is usually you in a month, that the standard here is roughly this.
The second one costs less to leave than the first.

## The evidence, from this week

Every one of these was visible for versions before anybody looked at it, and
every one of them was found by looking at something else.

| The window | How long it stood | Record |
| --- | --- | --- |
| `DELETE /api/bids` deleted everyone's bids, defended by a comment written when a bid belonged to a browser | Twelve versions | ADR-048 |
| The market tick took no account, defended by a comment about what the room bids against | Since the room existed | ADR-049 |
| A lockout policy in the schema and nowhere in the code | Since accounts shipped | ADR-050 |
| An em dash gate that ran on one laptop | Forty versions | ADR-052 |
| Five code comments citing records by names that do not exist | Unknown, one of them since 1.0.0.58 | 1.0.0.73 |
| An annotation pattern that matched nothing, so a red job stayed silent | One version, and it was written that morning | ADR-042 addendum |
| The front page decaying into ended lots a minute after it loaded | Since the ranking was moved to the server | ADR-056 |

The pattern in the first three is the one worth naming: a comment that
defends a decision is a claim with an expiry date, and it does not announce it.
When a system gains users, or persistence, or a public URL, every sentence that
says "this is only a demo" becomes a work list.

## The rule

The Boy Scout Rule, from Robert Martin, borrowed from Baden-Powell: leave the
campground cleaner than you found it. Applied literally to a codebase it means
every commit tidies whatever it walked past.

Applied literally it is also wrong, and this is the part usually left out. A
focused change that also renames three things, reflows a file and fixes an
unrelated comment is a change nobody can review and `git bisect` cannot use. The
tidying hides inside the work, which is how a cleanup becomes an outage.

So the rule here has a shape:

1. **Fix it in place when it is small and on the same subject.** A stale comment
   next to the line you are changing is part of the change.
2. **Otherwise it becomes its own version.** Its own commit, its own changelog
   line, its own record if it decided anything. Today produced seven of those
   rather than one commit with seven unrelated corrections in it, and each one
   can be read, reviewed and reverted on its own.
3. **A window you decide not to fix gets written down where the next reader will
   meet it.** In the record that owns the subject, or in the security page, or
   in the file itself. Not in a marker comment addressed to nobody: this
   repository has never contained a TODO and the reason is not discipline, it is
   that a thing worth doing later needs a reason and a reader, and a marker has
   neither.
4. **Every cleanup ships with the check that keeps it clean.** Otherwise the
   window is repaired and the building still has nobody watching it. The em dash
   scan became a test, the citations became a test, the annotation patterns
   became a test.

## What stops this from becoming infinite polish

A real question, and the answer is a test rather than a feeling. Something
counts as a window here if it can **mislead a reader or hide a defect**. A
comment that is now false qualifies. A check that cannot fail qualifies. A
number that is wrong qualifies. A name somebody would choose differently does
not, and neither does a file somebody would have split.

The other limit is that cleaning is not shipping. This week's list is seven
findings and every one of them shipped as a version with a behaviour change or a
test behind it. A version whose whole content is that the code now reads more
like the author's taste has not been written and should not be.

## The cheap half, enforced

Most windows cannot be found by a machine. A comment whose premise expired reads
exactly like a comment whose premise holds; that is why they last. The ones a
machine can see are worth pinning anyway, because they are the ones that arrive
by accident at three in the morning:

- No `TODO`, `FIXME`, `HACK` or `XXX` in the source.
- No focused or skipped test, in either runner. Both refuse one on CI and allow
  one locally, since focusing is how you debug a single test; Playwright is told
  to, and Vitest already does, so what is checked there is that nothing turns it
  off. This is the most expensive window available: a committed `test.only`
  turns a suite into one test and reports it green, so the check gets louder
  while checking almost nothing.
- No `console` call and no `debugger` statement under `src/`, where a crash is
  supposed to reach the same ring the server's crashes reach.

Every one of those passes today, which is the only moment worth writing them.
The first is free and the fiftieth is a rewrite.

## Consequences

- Seven findings this week were each their own version, each reviewable alone.
- Three new tests exist whose entire job is to keep a repaired window repaired.
- One habit is written down rather than carried: when a system gains users or
  persistence, its "only a demo" comments are the work list.

## Files

- [`api/TheYard.Tests/BrokenWindowsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BrokenWindowsTests.cs): the half a machine can see.
- [`playwright.config.ts`](https://github.com/SteveStout/TheYard/blob/main/playwright.config.ts): a focused browser test refused where it would do harm.
- [`vite.config.ts`](https://github.com/SteveStout/TheYard/blob/main/vite.config.ts): the unit runner, where the same rule is already the default and the check is that nothing switches it off.
- [`api/TheYard.Tests/HouseVoiceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/HouseVoiceTests.cs): a repaired window with its own guard.
- [`api/TheYard.Tests/RecordLinksTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RecordLinksTests.cs): another, in both directions.
- [`docs/ADR-045-reviewing-my-own-work.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-045-reviewing-my-own-work.md): where most of this week's windows were found.
- [`docs/SECURITY.md`](https://github.com/SteveStout/TheYard/blob/main/docs/SECURITY.md): the ones that were also security findings, with what each allowed.

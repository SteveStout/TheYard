# How this was built

This application was written with heavy AI assistance. That is not a footnote,
it is the method, and the repository is arranged so you can check what the
method produced rather than take a claim about it.

The short version: I set the direction and the constraints, an AI agent did the
implementation, and every change had to survive a gate before it could reach
the live site. What follows is where to look if you want to decide for yourself
whether that produced engineering or output.

## What is being claimed

Not that AI wrote a large application quickly. It did, and on its own that is
worth very little.

The claim is narrower and easier to falsify: **the work is verified, the
decisions are recorded with their reasoning, and the record includes the times
the decision was wrong.** A codebase built this way is either full of confident
prose with nothing behind it, or it is unusually well documented. The way to
tell them apart is to check the documents against the code, which this
repository is built to let you do.

## The criteria, and where the evidence is

**Does the code get explained, or just produced?**
Three records walk the configuration surfaces line by line at the level of a
developer meeting them for the first time: ADR: Program.cs, explained,
ADR: The React configuration, explained, and ADR: The tests, explained. They
exist because generated configuration is the easiest place for a project to
carry things nobody can account for.

**Do the documents track the code, or drift from it?**
Code shown in a record is not pasted. A fence marked `live` names a file and a
`#region`, and the API expands it from the working tree when you open the
document (ADR: Live code samples). A sample that goes stale cannot go stale
quietly; it either shows the current code or renders a visible "sample
unavailable" line. There is a test for both.

**Is there judgment, or only generation?**
The clearest evidence is the decisions that went the other way. Mermaid
rendering in the browser was built, measured at 112 added packages and about
2.3 MB of lazily loaded chunks for two flowcharts, and backed out (ADR: Style,
enforced). ESLint was configured, found to refuse the repository's TypeScript
version, and replaced rather than worked around, with the upstream issue
linked. The brief for the style work asked for records at `docs/adr/NNNN-*.md`;
they stayed where they were, and the record says why and calls it a deviation.
A record that only ever agrees with the plan is not a record.

**Does anything get measured, or is it all assertion?**
Search was optimised against a stopwatch and the before and after are in
ADR: The search index, including a measurement that looked like a regression
and turned out to be a cold container being compared with a warm one. The
cache-header work has numbers. The style tooling has a table of exit codes
proving each check fails on deliberately bad input, because a check that
returns zero on a clean tree and a check that does nothing look identical from
the outside.

**Do mistakes get found, and are they written down?**
Yes, and this is the part I would look at first.

- A self review before shipping the simulated bidders found eleven defects in
  code that had already passed its tests, four of them serious. They are listed
  in ADR: Competing bidders with what each one would have done.
- A screenshot taken to illustrate a feature caught a live defect instead: for
  a few hundred milliseconds the bid panel offered a minimum equal to the
  standing bid, which the server would have rejected. That is in the same
  record.
- The style checks were added on a Wednesday and their first CI run failed on
  the same command that had just passed locally. The cause was the repository
  claiming CRLF line endings while storing LF, so the rule was true on Windows
  and false on the Linux runner. The reproduction, the table, and the fix are
  in ADR: Style, enforced.
- ADR: The changelog predicted that a red build could make the changelog name a
  version that never shipped, and chose a procedural guard. The guard failed
  the next day. ADR: The version comes from the changelog is the structural
  replacement, and both records now say so.
- A test written to assert that every rejected request answers in one shape
  failed on the first run, because a malformed request body was answered with a
  bare 400 and no body at all. The API changed, not the test.

**Can the thing be operated?**
The Admin tab runs timed health checks, lists recent errors from the server and
the browser alike, reads the container group's own state from Azure with a
managed identity, and shows the last hour of traffic from Application Insights.
There is an endpoint that throws on purpose so the failure path can be
exercised against the live container instead of assumed (ADR: The exception
handler). The manual deployment scripts the pipeline replaced are kept as the
rollback.

**Is the system honest about itself?**
The version in the page footer is read from the running container, and since
1.0.0.41 the number itself is read out of the changelog's top line, so the
footer and the file documenting the footer are the same string rather than two
numbers kept in step by hand. Every document in the sidebar is served by the
API from the repository. Nothing in the app is a screenshot of itself.

## Three things a reviewer should be suspicious of

**"Nobody understands code they did not write."**
Fair, and the honest answer is that understanding is not proven by a document
saying so. What the repository offers instead is a lot of surface to test: ask
about the bid increment tiers, the FNV-1a hash that makes photo selection
deterministic, why the auction clock takes an anchor, or why the search index
is keyed by vehicle id rather than by reference. The explanations are written
down, which means they can be checked against me in a conversation. A document
that could not survive that question would be worse than no document.

**"Thirty-two decision records is documentation theatre."**
It would be, if they all said yes. Several of them exist to record a rejection,
a deviation, or a mistake: the Mermaid runtime that was measured and removed,
the linter that could not run, the naming convention that was not adopted, the
eleven defects, the guard that failed the day after it was chosen. The test of
a record is whether it would embarrass you to have written it, and some of
these do.

**"A green suite proves nothing when the tests were written alongside the code."**
The strongest version of this objection is right: tests generated with an
implementation tend to assert what the implementation does. Three pieces of
counter-evidence. The suites have repeatedly failed in ways that changed the
code rather than the test, and each is named above. The style checks were each
run against deliberately broken input and the non-zero exit codes are recorded,
so the checks are known to be capable of failing. And the ship gate runs the
full suite, the format checks, an em dash sweep, a scan for leaked keys, and a
check that the README's stated test counts match the numbers the suites
actually printed, then verifies the deployed result from the public domain
rather than from the build log.

## How the loop actually ran

Measure before deciding. Write the decision down with the number that drove it.
Ship through a gate that runs everything and refuses on any failure. Push only
after an objection window. Verify from the live domain, not from the pipeline's
own account of itself. When something is wrong, reproduce it before fixing it,
and put the reproduction in the record.

That loop is the reason this repository is worth reading. The AI made it
possible to run it many times in a day. It did not make the loop unnecessary.

# ADR: Counting what the tests cover

Status: accepted, 2026-09-03. 276 tests is a number about the tests. This is the
number about the code.

## Context

This repository says how many tests it has in several places, and until today
that was the only thing it said about them. "276 tests" is compatible with
testing one class very thoroughly and everything else not at all, and a reader
who has been handed a test count and no coverage figure is entitled to wonder
which.

`coverlet.collector` has been a dependency of the test project since the project
existed, and nothing ever ran it. The number cost one flag.

## What it is

```
89.6% of lines, 71.7% of branches
4,745 of 5,294 lines, 622 of 868 branches

TheYard.Data              100.0%   100.0%
TheYard.Domain             98.8%    97.9%
TheYard.Migrations.Sqlite  97.6%   100.0%
TheYard.Infrastructure     93.8%    89.3%
TheYard.Application        93.2%    88.8%
TheYard.Api                78.2%    64.1%
```

The shape is the interesting part, and it is the shape the architecture predicts.
The rules, the entities and the use cases are the parts worth being sure about
and they are at 93 to 100 per cent. The host is the lowest, and the host is
mostly composition: which adapter gets wired to which port, decided by a setting.

## Why the host is at 78 and why that is the right answer

The uncovered part of `TheYard.Api` is not scattered. It is two classes:

- `AzureSelf`, which asks Azure about this container group using the container's
  own managed identity.
- `TelemetryReader`, which queries Application Insights.

Both talk to Azure, and CI has no Azure credentials and is never getting any.
That is a standing constraint of this project, not an oversight: a build that
needs a cloud credential is a build that cannot run in a fork, and the credential
is the thing most worth not having lying around.

Covering them would mean putting a fake HTTP layer underneath and asserting that
the fake was called, which tests the fake. What is worth asserting about those
two is that they degrade rather than throw when the identity endpoint is not
there, and that is tested: the Admin tab's card renders its "not configured"
state, and the telemetry endpoint answers with telemetry switched off.

So the honest reading is that the untested lines are the ones whose behaviour
against the real service cannot be established from a machine that is not allowed
to talk to it, and whose behaviour without the service is tested.

## Decision

Collect coverage in CI, publish it where somebody without a login can read it,
and hold it to a floor of 85 per cent of lines and 68 per cent of branches.

**The floor is a ratchet, not a target.** It sits just under where the suites
actually are, which makes it a detector for a change that quietly stops testing
something, and keeps it clear of the number where people start writing tests that
execute a line without asserting anything about it. A coverage figure chased for
its own sake buys nothing and costs a suite that is slower and harder to read.

**Published as annotations, not only as a job summary.** A job summary renders
for somebody signed in to the repository. Annotations come back from the public
API and sit at the top of the run page, which is the difference between a number
this project can point at and a number only its author can see. This repository
has now paid twice for the lesson that a check nobody can read is not a check
(ADR: The exemption that hid a contrast failure, addendum).

**A script in the repository rather than a marketplace action**, so a reader can
see exactly what the number means and so the same command runs locally.

## What this is not

It is not a claim that 89.6 per cent is a good number in general. It is the
number this suite produces, published so that it can be argued with, and the
argument that matters is the per-project table rather than the total: a high
average over a system whose rules are untested would be worse than a lower one
where they are not.

It also says nothing about the browser suite or the frontend unit tests, which
cover a different 44 and 48 things and are not measured here.

## Consequences

- The claim "276 tests" is now accompanied by what they touch.
- A change that removes coverage fails the build with a sentence naming which
  floor it went under.
- One more thing to keep true: the floor has to be raised deliberately if the
  suites get much better, or it stops detecting anything.

## Files

- [`.github/coverage.py`](https://github.com/SteveStout/TheYard/blob/main/.github/coverage.py): the report, the table, the annotations and the floor.
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): where it runs, and why the floor is where it is.
- [`docs/ADR-021-tests-explained.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-021-tests-explained.md): what the tests are for, which is the question this one does not answer.

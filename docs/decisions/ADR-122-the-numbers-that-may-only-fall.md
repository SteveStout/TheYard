# ADR: The numbers that may only fall

Status: accepted, 2026-10-29, at 1.0.4.115. Fourteen numbers describe the shape of this codebase, they live in `.github/quality-ratchet.json`, and `.github/quality.py` reads them off a checkout and holds each one to its limit. CI's api job runs it beside the coverage step and the ship gate runs it before the verdict, so a change that makes any of them worse is red. Twelve may only fall and two may only rise, and the only way a limit moves is in the same commit as the work that earned it.

## Reading list

1. [ADR: The rules a change has to pass](https://theyard.stevenstout.biz/?doc=adr-rules): the table of rules a test holds, which this record adds a row to. A rule is a thing that is true or false; a number here is a thing that is getting better or worse, which is the gap this record fills.
2. [ADR: Counting what the tests cover](https://theyard.stevenstout.biz/?doc=adr-coverage): the coverage floor, the ratchet this project already had, and the argument for a floor set just under where the suites are rather than at a number somebody would like.
3. [ADR: Onion and SOLID, how this codebase holds them](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid): four of the fourteen numbers are the rings and the slices, which are already held at zero by tests that read the compiled code. They are counted here anyway, so the one report says whether the whole shape held.
4. [ADR: Code that reads like code](https://theyard.stevenstout.biz/?doc=adr-highlighting): the house voice, which is why a comment carrying a date or who asked for something is counted as a defect and not as context.
5. Ron Jeffries, [Refactoring: Not on the backlog!](https://ronjeffries.com/xprog/articles/refactoring-not-on-the-backlog/): why a quality number that is a project of its own never gets paid down, and why the payment has to be part of the ordinary commit. Read 2026-10-29.
6. Michael Feathers, [Working Effectively with Legacy Code](https://www.oreilly.com/library/view/working-effectively-with/0131177052/), on seams and boundaries: the practice this ratchet is the arithmetic for, which is that the line a codebase holds today is the only line it can defend tomorrow.

## In plain words

Some things about code are either right or wrong, and a test says which: a dependency points the wrong way, a class is not sealed, a file is over three hundred lines. Those are already held.

Other things are a count. How many declarations have no sentence saying what they are for. How many methods that talk to a database or a network take no way to be cancelled. How many places read the clock directly instead of being handed one. No single occurrence of those is a crisis, and that is exactly why they grow: there is never a good moment to object to one more.

So they are counted, and the count is written in a file. The build reads the code, counts again, and fails if any number is worse than the file says. Nothing has to improve. Nothing may get worse. When somebody does fix a batch, they write the smaller number into the file in the same commit, and the line never comes back up.

What that is worth: the slow decline that happens between reviews becomes a red build the day it starts, and a week of drift costs an hour instead of a quarter.

## Context

The site's production code went from 26,516 lines to 53,427 in the two weeks to 29 October 2026, across 192 commits. Every architecture gate held through it: zero project references pointing outward, zero inner-ring leaks, zero cross-slice usings, every class sealed, no blocking wait, no file over the cap. Nothing that a test asked a yes-or-no question about moved at all.

Everything that nobody was counting moved a long way. Declarations with no summary went from 239 to 549. Service classes taking six or more dependencies went from 0 to 9. Async methods with no cancellation token went from 50 to 113. Direct clock reads went from 46 to 57. Comments carrying a date or a name went from 2 to 17. Files within ten lines of the three-hundred cap went from 3 to 7. The test-to-production line ratio fell from 0.83 to 0.76, while the test count itself nearly trebled, which is the measurement that says the tests kept up in effort and not in reach.

The pattern is not that the rules were weak. It is that the rules were binary and the decline was continuous. A review would have caught all of this, and there was no review for two weeks because the person who does them was travelling. A number that only a person checks is a number that is checked when that person is free.

## Decision

1. **Fourteen numbers, one file.** `.github/quality-ratchet.json` holds one limit per number. The names are the names the scanner prints, so the file and the log never need translating between them.
2. **One scanner, run in two places.** `.github/quality.py` reads a checkout and builds nothing. CI's api job runs it beside the coverage step, under `if: always()`, so a reader of a red run sees both. The ship gate runs it before the verdict and the verdict reads its result, so no version reaches main with a number worse than the file says.
3. **Twelve may only fall, two may only rise.** A name ending `higher_is_better` is a floor and everything else is a ceiling. The table below says what each one counts.
4. **A limit moves in the commit that earns it, and only the good way.** A fix lowers its number in the same commit. The step prints a notice, not an error, when a number has moved the good way and the file has not followed it: a run is not failed for being better than the line, and the slack is said out loud so the line gets tightened.
5. **The coverage floor is not this file's.** Line coverage at 90 per cent and branch coverage at 75 per cent stay in `.github/coverage.py`'s arguments, and nothing in this record lowers them.
6. **The remediation order is the order of the damage.** Cancellation first, then the clock, the wide classes, the files at the cap, the missing summaries, the history comments, the untested types, and the Function's hand-kept list of read-only use cases. One version each, each lowering its number in the same commit.

## The numbers, and what each one counts

| The number | What it counts | Why it may only move the good way |
| --- | --- | --- |
| `undocumented_declarations` | Public, internal and protected declarations in production C# with no `///` line above them | A summary is the only documentation that cannot drift from the code it sits on, and the one a newcomer reads first |
| `onion_outward_refs` | Project references that point outward through the rings | The dependency rule is the one thing a slice may never break, and a reference is how it breaks first |
| `inner_ring_leaks` | A `using` in Data, Domain or Application naming EF Core, an Azure SDK, the disk, an HTTP client or ASP.NET | An inner ring that can reach the network is an inner ring that will, and then it cannot be tested without one |
| `cross_slice_usings` | A file under one slice naming another slice's namespace | A module is only a module while nothing reaches past its registration and its ports |
| `wide_service_classes` | Classes whose constructor takes six or more dependencies | Six collaborators is more than one job, and a class with more than one job is changed for more than one reason |
| `async_without_cancellation` | Async methods declared with no `CancellationToken` parameter | Work a caller has walked away from is work the site pays for twice, and a path that drops the token cannot be made to stop later without changing every caller |
| `clock_outside_composition` | Reads of `DateTime.Now`, `DateTime.UtcNow` or `DateTimeOffset.UtcNow` outside a module, the wiring, `Program.cs` or the clock seam | Two clocks in one codebase disagree on a daylight-saving change, and the half that cannot be faked cannot be tested |
| `history_or_name_comments` | Comments carrying a date, or who said what, in place of the what, the how and the why | A comment that says when something was decided tells the next reader nothing they can act on, and goes stale on its own |
| `blocking_waits` | `.Result`, `.Wait()` and `GetAwaiter().GetResult()` in production code | A blocked thread on a small plan is the whole plan, and the deadlock it causes arrives under load and never in a test |
| `files_over_300` | Production files past the three-hundred-line cap | Already zero and held by `FileShapeTests`; counted here so the one report says whether the whole shape held |
| `files_within_10_of_cap` | Production C# files at 290 lines or more | The next edit to one of these forces a split, and a split made under the pressure of an unrelated change is made by the alphabet instead of by job |
| `behavior_types_never_named_in_a_test` | Handlers, stores, rules, readers, writers, policies and calculators whose name appears nowhere in a test | A type no test names is a type whose behaviour nobody has written down, whatever the coverage percentage says about the lines in it |
| `test_to_prod_ratio_higher_is_better` | Lines of test code divided by lines of production code, as a floor | The test count can treble while this falls, which is what happened, and it is the number that says whether the tests kept up in reach and not only in effort |
| `dotnet_tests_higher_is_better` | xUnit facts and theories plus their `InlineData` rows, as a floor | A suite that shrinks is a suite somebody deleted rather than fixed |

The block that builds them, read from the scanner itself:

```live path=.github/quality.py region=ratchet
```

## What holds it

`QualityRatchetTests`: every key in the ratchet file has a row in the table above and every row names a key in the file, read both ways as `RuleTableTests` reads the rules table; both workflows run the scanner against the file; the gate's verdict reads its result, so the step cannot run and not count; and the coverage floor is still 90 and 75.

Proved by lowering a limit by one and watching the step turn red, then restoring it. The run that proved it is named in the changelog line for this version.

## Consequences

- A change that makes any of the fourteen worse is red in CI and at the gate, on the commit that made it, rather than at the next review.
- The ratchet records the worst this codebase has been, which is today. It is not a target and it is not a compliment: twelve of the numbers are debts with their sizes written down.
- The step costs seconds and no build, so it can run under `if: always()` and report beside a red suite instead of behind it.
- A number that moves the good way by accident is a notice and not a failure, so an unrelated commit is never red for being an improvement. The cost is that the file has to be tightened on purpose.
- The scanner reads text with regular expressions and compiles nothing. It will miscount a declaration written in a shape it has not seen. That is the price of a measurement a reader can check by eye on one file, and the four numbers where being wrong would matter most are the four already held at zero by tests that read the compiled assemblies.

## Where it sits

Outside the rings, beside the coverage script: `.github/` is the build's own folder and nothing in the application references it. Single responsibility, at the level of the build: `coverage.py` answers how much of the code the tests touch and `quality.py` answers what shape the code is in, and neither reads the other's floor. It cost two files in `.github/`, a step in each of two workflows, a test class and this record, and no resource. What would change it: a number that turns out to be unactionable, which comes out of the file and out of the table here in the same commit, with the reason written down rather than the row quietly deleted.

## Files

- [`.github/quality.py`](https://github.com/SteveStout/TheYard/blob/main/.github/quality.py) - the scanner, and the ratchet block the table above is drawn from
- [`.github/quality-ratchet.json`](https://github.com/SteveStout/TheYard/blob/main/.github/quality-ratchet.json) - the limits, one per number
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml) - the step in the api job, beside the coverage step
- [`.github/workflows/gate.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/gate.yml) - the step at the gate, and the verdict that reads it
- [`api/TheYard.Tests/QualityRatchetTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/QualityRatchetTests.cs) - the table and the file, held to each other both ways

## Addendum, 2026-10-29 (1.0.4.116): the first rung, and what a count is worth

`async_without_cancellation` went from 113 to 33 in one version, which is the first evidence
this record set out to collect. Three things are worth writing down about how it went, because
the next seven rungs will go the same way.

**The number was not the work; it was the map.** Reading the scanner's own list told the rung
where to start and, more usefully, where the cost actually was: not in the endpoints, which
minimal APIs already hand a token, but in the four auction ports and the two store adapters
under them, where a token had been left out on purpose in 2026-09 and the reason had stopped
being true (ADR: The ports learn to wait, the addendum on the token). A review would have
found the endpoints and stopped.

**Three methods already took a token and threw it away.** `RoundTripMillisecondsAsync` on both
store diagnostics, and three Application handlers whose `HandleAsync(request, cancellation)`
ADR-107 had already shaped, received the request's cancellation and called the store without
it. That shape is invisible to the count, which asks only whether the parameter is declared,
and it is the shape a reader is most likely to trust on sight. So the rung's own guard is a
test of the ports' surface by reflection plus one behaviour test that cancels the caller's
source and reads the token the store was handed, because a version that threads the parameter
and then passes `CancellationToken.None` at the last call satisfies a count and nothing else.

**Thirty-three are left, and they are named rather than rounded off.** Seven are Function
timers, five are the proof runner's rounds, three the observability writers, two The Shed's
sample, and the rest are a startup or a host path each. One of them, `TokenValidated` in
`RefuseSession.cs`, is an override of a framework type whose signature this codebase does not
own and can never take a token; it is a known miscount of the kind the Consequences above
warn about, and it will still be there when this number reaches its floor. The limit is 33
from this version, so none of the thirty-three can grow back while the rest of the rung is
paid off.

**Taking a token is not the same as cancelling, and three places say so out loud.** A bid
write that has entered the gate, a reset that has dropped a buyer's rows, and a transactional
batch each run to their end whatever the caller does, because the alternative is a store
holding half of something. A count cannot tell the difference between a token dropped by
accident and one not used on purpose, so each of those carries the reason beside it in the
code.
## Addendum, 2026-10-29: the count was reading the first bracket, not the signature

The rest of the first rung found that eight of the thirty-three were not there. The scanner read
a declaration's parameters with `([^)]*)`, which stops at the first closing bracket, and three
ordinary shapes put a bracket in front of the token:

- an attribute with arguments of its own, which every Azure Functions timer has
  (`RunAsync([TimerTrigger("0 5 * * * *")] TimerInfo timer, CancellationToken ct)`), and which
  accounted for six of the eight;
- a tuple parameter, `LeadAsync(..., (string Path, bool Descending) key, CancellationToken cancellation)`;
- a signature wide enough to wrap, where `PrepareAsync`'s token is two lines below the line the
  declaration starts on.

Every one of those eight declares a token and carries it. The number was wrong, not the code.

**Why this is written here and not fixed quietly.** The cheapest way to make the count fall was
to reorder an Azure Functions trigger's parameters so a regular expression could see past them:
a change to six deployed entry points, in service of a script. That is the failure mode a ratchet
invites and the reason a limit may only move in the commit that earns it; the rule says nothing
about *earning it honestly*, so the record has to. A number that disagrees with the code is fixed
in the scanner, with the eight signatures quoted, and the fix is held by a test.

**What the scanner does now.** It reads the text between a declaration's own brackets, balancing
them across up to twelve lines (`# #region whole-signature` in `.github/quality.py`). The set of
lines it considers is unchanged, and only the test for the token widened, so the count could only
fall: it went 33 to 25 on an untouched checkout, and the eight are named above.

**And the rest of the rung.** The remaining twenty-four are done, so
`async_without_cancellation` is 1: `TokenValidated` in `RefuseSession.cs`, the framework override
the addendum above named as a permanent miscount. That is the floor, and `CancellationTests`
asserts both the one and which one. Four more methods were found to have taken a token and thrown
it away, on top of the three the addendum above names: `AzureReadings.SelfAsync`, `.PeerAsync` and
`.TelemetryAsync` each received a cancellation and called with none, and the blog's image store was
built twice over a `Func<CancellationToken, Task<string>>` with `_ =>`. The count cannot see any
of them, which is this record's own Consequences section happening twice in two versions.

**One thing the build already holds.** A surface-only version of this change, where
`UploadAsync` takes a token and `CopyToAsync` does not get it, does not compile: analyzer CA2016
refuses it under `-warnaserror`. So take-and-drop is already caught wherever the callee has a
token overload, and the hole is only where it does not: a delegate, a port of this codebase's
own, or a call that reads a token off something else.

## Addendum, 2026-10-29 (1.0.4.120): the width was counted in commas, and it was wrong both ways

The third rung is the wide classes, and the first thing it found was that the count did not
mean what the table above says it means. `wide_service_classes` read a class's declaration with
`([^,)]*,){5,}`: five commas on the line the class is declared on, before the first closing
bracket. How many things a class is handed is a different question from how many commas are on
a line, and the two disagreed in both directions.

**Three classes were counted that take four and five.** A generic type argument carries a comma
of its own, so `Func<CancellationToken, Task<string?>>` is one parameter that reads as two:

- `CampaignSource` takes four and has two such parameters, so it read as six;
- `NightlyProcesses` takes five and has one;
- `PlanHistoryReader` takes five and has one.

Splitting any of those three would have been work done for a regular expression, which is the
failure mode the addendum above named when the same thing happened to `async_without_cancellation`.

**Seven were not counted at all.** Six wrap their parameter list over several lines, so the
commas the rule wanted were not on the line it read: `RequestRecorder` takes eight,
`StoreAnnouncement`, `MachineRecorder` and `PulseReader` take seven, and `CostRecorder` and
`CloudReader` take six. The seventh, `BoundedCache`, has a traditional constructor in the class
body rather than a primary one, and the rule only ever looked at class declarations.

**What the rule does now.** It reads the parameter list through the same whole-signature helper
the async rule has used since the addendum above, and splits it only where a reader would: on a
comma at the list's own bracket depth, outside any angle brackets. `WIDE_AT = 6` is named rather
than spelled as five commas, because the number in the table is six.

```live path=.github/quality.py region=constructor-width
```

**One shape is still invisible, and it is named rather than quietly included.** A traditional
constructor, `public Thing(...)` inside the class body, is not counted: telling a class's
constructor from a record's means tracking the enclosing type, which is more than a line reader
does, and this record's own Consequences section accepts that price. The one production type it
hides is `BoundedCache` in `api/TheYard.Application/Shared/Caching/BoundedCache.cs`, whose six
parameters are four pieces of configuration and two optional collaborators. Whether that is a
class with six jobs or a cache with four settings is a judgment, not a count, and the next
instalment of this rung makes it in the open rather than having a regular expression make it by
accident.

**This is the first corrected count that made a number go up.** The async fix could only lower a
count, because only the test for the token widened. This one both adds and removes, and on an
untouched checkout of 1.0.4.119 it took `wide_service_classes` from 9 to 12 and turned the step
red: `WORSE wide_service_classes: 12 (limit 9)`. A limit may only move the good way, so the honest
number had to land under the old one in the same commit, and four classes were split to get it to
eight:

| Class | Was handed | Now | Split by |
| --- | ---: | ---: | --- |
| `StoreAnnouncement` | 7 | 2 | `StoreLogAttachment` attaches the host's loggers, `StoreStartupLines` says what came up, and the hosted service holds only the order |
| `RingReadings` | 7 | 4 | `HostRings`, the four rings this process keeps, as one thing |
| `MachineCardReadings` | 6 | 5 | the same `HostRings` |
| `CostRecorder` | 6 | 2 | `CostKeeping` holds one read and everything it leaves behind; the recorder holds the schedule |

`HostRings` is a parameter object and not a job split, which is worth saying plainly because the
difference is exactly what this number is for. It is the right move in these two cases because
every answer both classes give reads the rings and nothing else about them, so the rings are one
collaborator rather than four, and a fifth ring now adds a property instead of changing two
constructors. It would be the wrong move on a class whose parameters have nothing to do with each
other, where it would hide the width instead of removing it.

**Eight are left and `ConstructorWidthTests` names them**, by file and type, beside the three
over-counts and the four wrapped shapes, so the next instalment starts from a list it can check
rather than a count it has to trust. That test also resolves every part split off a class from the
real host: the compiler catches every `new X(...)` whose arity changed, but a type the container
activates by type is resolved at run time, and 1.0.4.118's first gate went red on exactly that,
on a boot the shipping laptop cannot run.


## Addendum, 2026-10-30 (1.0.4.124): a count that falls a project at a time, and the rule under it

`undocumented_declarations` is the largest of the fourteen and the one that had not moved since the
rung above it began: 543, where a fortnight of doubling production code took it from 239. This
instalment takes one project to zero, `TheYard.Infrastructure`, 142 declarations, and the number
falls to 401 with the file lowered in the same commit.

The order is the one the quality guard set, worst documented first: Infrastructure, then the job
hunt Function, the document store, Application, the host, Data and Domain. What is left after this
instalment is 401, and it is 120 under `functions/`, 100 in `TheYard.Infrastructure.Cosmos`, 74 in
`TheYard.Application`, 59 in `TheYard.Api`, 26 in `TheYard.Data`, 21 in `TheYard.Domain` and 1 in
The Shed.

**Most of the 142 are row properties, and a generated comment cannot write them.** A column's
summary has to say what the column holds *and why it is kept*, and the second half is the half worth
reading: that the reserve price is kept on the server and never sent, because the wire carries
whether the reserve is met; that a visitor's token is a keyed hash which rotates with the day,
because that counts distinct visitors within a day and cannot be joined to the next day's; that a
bot's read is kept rather than dropped, because a row thrown away can never be counted another way;
that the dataset's auction date is kept as text on purpose, because the window comes from the id's
hash and parsing the string would invent a second schedule. Each of those was read off the model,
the mapping or the record that decided it, and none was invented to fill a line.

**A count has a weakness this instalment exposes, so a rule goes in beside it.** A total falls when
one project is paid down, and stays level when that project fills back up while another is paid down
in its place. So from this version the projects taken to zero are named one by one in
`MemberSummaryTests`, and the question asked of a named project is not "how many" but "any", which
has one right answer. The rule body is `tests/rules/SummaryRule.cs`, shared with the job hunt's
suite the way the length rule is, so the Function's own projects can be named there in the same way
when their instalment lands. It asks exactly what `.github/quality.py` asks: the same declaration
pattern, and the same walk back over the attributes, blank lines and region markers that may sit
between a summary and the thing it describes. That sameness is deliberate. If the two ever
disagreed, a project this suite called finished would still be counted by the ratchet, and the
number and the rule would be describing different codebases. The red before it was green named all
142, and its count agreed with the scanner's exactly, which is the check that the port is honest.

The rule, from the build:

```live path=tests/rules/SummaryRule.cs region=summary-rule
```

A constant is excused in both, because its name and its value are already on the line the reader is
looking at. Nothing any of the ten files does has changed: every line this instalment adds is a
comment.

# ADR: Reviewing my own work, and what that found

Status: accepted, 2026-09-03. Not a feature. A record of an adversarial review of
a single day's changes, the seven defects it found, and the one property that
made it worth doing.

## Why this is written down

Everything in this repository was written by an AI agent, which is stated plainly
in "How this was built" and is the thing a reader is entitled to be suspicious
of. The most useful answer to that suspicion is not a claim about care. It is a
record of the work being checked by something that did not write it, and of what
the check found.

So at the end of the day's work the whole diff, `a162238..c54262f`, went to a
reviewer with no memory of writing any of it, with instructions to be skeptical,
to ignore style, and to hunt specifically for concurrency, re-entrancy, leaks,
privacy and arithmetic.

It found seven things. Two of them were serious. Both were mine at my most
confident.

## The one that mattered

The change being reviewed had, hours earlier, argued at length that a parameter
value must be structurally impossible to publish:

```csharp
/// <summary>One parameter of a SQL statement, described but never valued.</summary>
public sealed record SqlParameterShape(string Name, string Type, int? Size);
```

The argument is good. A redaction rule is a list of what to hide, written today,
correct only for the columns that existed then. A type with nowhere to put a
value cannot leak one.

The same change added a second section to the same public page carrying the
application's raw log, and this line went into it:

```csharp
app.Logger.LogError(
    "The store could not be prepared ... : {Note}", database.Note);
```

`database.Note` was built as `$"{Describe()}: {ex.GetType().Name}: {ex.Message}"`.
`Describe()` is careful and says only "Azure SQL Database". `ex.Message` is not
careful at all. A `SqlException` from this path says the server's hostname, the
login name, the database name, and the IP address the connection came from.

Two screens further down, the health check refuses to do exactly this:

> The reason is in the log, not in this response. A health endpoint is public on
> purpose, and an exception message from a storage failure is typically a
> filesystem path.

That sentence was written a week earlier, by me, about this same page. Then I
made the log public and never went back to read it.

The front door was reinforced and a window was left open in the same commit, and
the note explaining why the door mattered was still taped to the door.

The fix is not a rule about which messages are safe. The exception travels in the
exception slot rather than inside the message template, and the ring already
reduces an exception to its type:

```csharp
app.Logger.LogError(database.Failure, "... : {Note}", database.Note);
```

What is in the template reaches a public page. What is in the exception slot
reaches the console and Application Insights. The two are now different things
on purpose, and `DatabaseState` carries the message as an `Exception` rather than
as text so a caller cannot accidentally print it.

The reviewer also found that the log ring captured every category, including the
framework's, and that on a completely healthy container that publishes the
content root and the data-protection key directory. Those are server filesystem
paths, written before anything goes wrong. The ring is an allow-list now: this
application's own categories, plus the one Entity Framework category the SQL
section exists to show. An allow-list rather than a deny-list, so a dependency
added next year is silent by default instead of public by default.

## The one that made the feature wrong

The request timing middleware carried this comment:

> Timing, outside the error middleware so the number is the whole cost a caller
> waited for, including the time spent turning an exception into a
> ProblemDetails.

Both halves were false. The middleware sat below `UseExceptionHandler`, and
unwinding runs inner to outer, so a request that threw reached the `finally`
before the handler had written anything. Every failed request was recorded as a
200, with the handler's time excluded rather than included.

`/api/admin/selftest/exception` answers 500 to its caller and was appearing in
the metrics as 200. The endpoint that exists to prove the failure path works was
being misreported by the feature built to watch it.

It is the outermost middleware now, above the exception handler and above
authentication, so it sees the status that was actually sent and counts the
requests that authentication rejects.

The lesson is narrow and worth keeping: a comment asserting an ordering is not
evidence of that ordering. This one was written from intent, not from reading the
pipeline, and it was wrong in the direction that made the feature look right.

## The other five, briefly

- **The connect timeout multiplied.** Raising it to ninety seconds left
  `EnableRetryOnFailure(maxRetryCount: 4)` untouched, which is five attempts of
  ninety plus backoff: about eight minutes against a five minute deploy. A
  database that was genuinely gone would have turned an outage into a failed
  deploy, which is precisely what the file-backed fallback exists to prevent, and
  precisely the failure recorded one version earlier. Sixty seconds and two
  retries now, about three minutes, with the arithmetic written in one place and
  a test asserting it fits.
- **The metrics endpoint published everybody's browsing.** It returned the whole
  five hundred entry request ring: which vehicles each visitor opened, which
  filters they typed, in near real time, to anyone. The page never rendered it.
  Aggregates answer the question and name nobody. The request string dropped its
  query string for the same reason: that is the line a password reset token would
  arrive on.
- **The load helper did not wait.** `openTheYard` asked for forty-five seconds
  inside a thirty second test, so it could never spend the budget it claimed to
  give, and it waited for the absence of a loading message, which is already true
  in the instant after `goto` resolves and before React mounts. It waited for
  nothing and handed the next assertion back its five seconds. It waits for the
  announcement region to exist and to stop saying "Loading inventory" now, and
  the suite runs at sixty seconds.
- **The feature erased its own evidence.** The health check runs two SELECTs, an
  open Admin tab asks for it every thirty seconds, and those statements filled
  the two hundred slot ring in under an hour. The section showed nothing but the
  act of reading it. The observability endpoints are excluded from their own
  rings.
- **A test asserted an accident.** `Assert.All` on the SQL ring's contents passed
  only because of the order xUnit happened to choose from a hash of the method
  names, and one rename would have broken it.

## What the review did not find

Worth recording, because a review that finds only problems has not been read
carefully either. The locking on all four ring buffers is correct: one private
gate, both operations guarded, immutable snapshots, no nested locks, no lock held
across an await or across I/O, no reachable deadlock. There is no re-entrancy:
nothing on the logging path logs, nothing on the interceptor path issues SQL. The
percentile arithmetic was checked against exact rational arithmetic for nine
percentile values at every sample size from one to five thousand, with no
divergence. And the readiness split itself was sound, including that
`GatesReadiness` defaults to true so a check added later gates by default rather
than being silently excluded.

## Consequences

- Nothing a database driver writes into an exception message can reach a public
  page through the log section, and the mechanism is a different argument slot
  rather than a rule about content.
- The framework's own log lines, and the server paths in them, are out.
- The timing section is correct about failed requests, which are the ones worth
  looking at.
- The startup connect budget and the retry policy are one number with the
  arithmetic beside it, and a test fails if they stop fitting the deploy.
- The Admin tab no longer fills with the act of watching it.
- Two tests stopped asserting accidents, and one canary now covers both public
  sections rather than one.

## Files

- [`api/TheBlock.Infrastructure/EfSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/EfSources.cs): `DatabaseState` carrying the failure as an exception rather than as text.
- [`api/TheBlock.Api/AdminObservability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/AdminObservability.cs): the category allow-list, the self-observation filter, the capacity guards.
- [`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs): the timing middleware where it belongs, and readiness that runs only what it needs.
- [`api/TheBlock.Infrastructure/YardConnection.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/YardConnection.cs): the connect budget and the retry policy read together.
- [`tests/e2e/app.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/app.ts): a wait for something that is there.
- [`docs/ADR-042-exemptions-that-hide.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-042-exemptions-that-hide.md): the checks that asked easier questions, which this is the sequel to.

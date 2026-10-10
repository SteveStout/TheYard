# ADR: Watching your own SQL, explained

Status: written for a developer who has used an ORM but never hooked one, 2026-09-03.
Companion to ADR: What the database is actually doing, which is the decision.
This one is the walkthrough.

## In plain words

This page shows a developer how to see the SQL the code's data mapper sends, by asking it to hand over each command (an EF Core interceptor) instead of parsing log text. It also shows how a type with no field for parameter values keeps email addresses off a public page.

What that is worth: a developer learns to build a type that has no place for a secret, and the organization gets query timings on a public page without a metrics library.

## The problem, in one sentence

You want to see the SQL your ORM sends, and you cannot see it by reading your own
code, because your own code does not contain any SQL.

That is the trade you made when you picked an ORM. You write
`db.Vehicles.OrderBy(v => v.Seq).ToList()` and something else decides what the
database receives. Most of the time that is fine. The moment it is not fine is
the moment somebody asks why a page takes two seconds, and the answer is a query
nobody has ever looked at.

## The three ways to look, and why two of them are traps

**Turn on the log.** Entity Framework already logs every command at Information.
One line of configuration and it is on your console. This is the right answer for
local debugging and it is where you should start.

It stops being the right answer the moment you want the data somewhere other
than a console, because a log line is a string. To put the duration in a column
you have to find it in the string. To group by statement you have to find that in
the string too. You are now writing a parser for a message format that is not a
contract and changes between versions.

**Time it yourself.** Wrap your repository calls in a stopwatch. This tells you
how long your method took, which is not the same question. Your method might send
four queries, or one, or none because the value was cached, and the stopwatch
cannot tell you which.

**Ask the ORM to hand you the command.** EF Core has a type for this:

```csharp
public sealed class SqlLogInterceptor(ISqlLog log, ICurrentRequest request) : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData.Duration, "read");
        return base.ReaderExecuted(command, eventData, result);
    }
}
```

You get the `DbCommand` itself. Not a rendering of it: the object, with
`CommandText` as a string you did not have to parse and `Parameters` as a
collection you can walk. And `eventData.Duration` is a `TimeSpan` that EF
measured, so you are not guessing where to start your own stopwatch.

You register it on the context:

```csharp
builder.Services.AddDbContextFactory<YardDbContext>((services, options) =>
{
    yard.Configure(options);
    options.AddInterceptors(new SqlLogInterceptor(sqlLog, services.GetRequiredService<ICurrentRequest>()));
});
```

There are six methods to override rather than one, because there are three kinds
of command (a reader, a non-query, a scalar) and each has a synchronous and an
asynchronous form. Override the ones you use and the rest fall through to the
base.

## The part that is a security decision, not a logging one

Here is the thing to take away from this record, and it is not about EF.

This application's Admin page is public. So the SQL on it is public. And the
parameters of a registration are an email address.

The first design anybody reaches for is a redaction rule: show the values, hide
the sensitive ones. Think about what that rule is. It is a list, written today,
of which columns are sensitive. Next year somebody adds a phone number. The rule
does not know about it. Nothing fails. The page shows it.

The design that does not have that problem is not a better rule. It is no rule:

```csharp
public sealed record SqlParameterShape(string Name, string Type, int? Size);
```

There is nowhere to put a value. Not a redacted one, not a masked one. The code
that builds these reads `ParameterName`, `DbType` and `Size` and never touches
`Value`, and a future column cannot leak through a field that does not exist.

This is worth recognising as a shape, because it comes up constantly and it has a
name in older books: make the illegal state unrepresentable. When you are about
to write a rule that says "and remember to hide X", check first whether you can
build a type that has no X.

And note this is also why the interceptor beats the log line here. EF's log
message has the values formatted into it before any of your code runs. By the
time you could strip them, they have already been built, already been in memory,
and already been handed to every other logging provider in the process, which in
this application includes Application Insights. Taking something out of a string
after you have broadcast the string is not taking it out.

## The percentile, which is smaller than it looks

The page shows p50 and p95 per endpoint. Those sound like they need a metrics
library. They need eleven lines:

```csharp
public static long Of(IReadOnlyList<long> values, int percentile)
{
    if (values.Count == 0)
    {
        return 0;
    }

    long[] sorted = values.ToArray();
    Array.Sort(sorted);
    int rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
    return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
}
```

Sort, then take the value at that position. That is the nearest-rank percentile,
and it is exact.

The reason real systems do not do this is that they cannot keep every sample:
at a million requests a minute you need a sketch, which is an approximation with
a memory bound. This application keeps its last five hundred requests in a ring
buffer, so sorting five hundred longs on each page load costs microseconds and
the answer is exact for that window.

The lesson is not "percentiles are easy". It is that the expensive machinery
exists to solve a scale problem, and you should know whether you have that
problem before you take on the machinery.

## What to read next

- The decision, with the constraint that drove it: ADR: What the database is
  actually doing.
- Where the ports came from and why Infrastructure does not know what an
  `HttpContext` is: ADR: Program.cs, explained.
- Why the Admin page is public at all: ADR: Observability.

The interceptor this record explains, read from this build. It subclasses EF Core's `DbCommandInterceptor`, overrides one method per command kind, and writes the statement's shape into the port in `SqlLog.cs`, never a parameter's value:

```live path=api/TheYard.Infrastructure/SqlLogInterceptor.cs region=sql-interceptor
```

## Where it sits

The walkthrough centres on Infrastructure: SqlLogInterceptor subclasses EF Core's DbCommandInterceptor and writes into the ports and shapes in Application/SqlLog.cs, while the percentile helper sits in the host's RequestTimings.cs. Open/closed is the EF feature on show: the interceptor adds behaviour to the ORM through a hook EF publishes, with no change to EF or to any query. Its cost is an override per command kind to keep in step, and a type with no Value field that cannot show a value even when debugging wants one. A system with far more traffic than a five-hundred-entry ring would need a sketch or a metrics library for its percentiles.

## Files

- [`api/TheYard.Infrastructure/SqlLogInterceptor.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/SqlLogInterceptor.cs)
- [`api/TheYard.Application/SqlLog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/SqlLog.cs)
- [`api/TheYard.Api/AdminObservability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/AdminObservability.cs)
- [`api/TheYard.Tests/AdminObservabilityTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AdminObservabilityTests.cs)

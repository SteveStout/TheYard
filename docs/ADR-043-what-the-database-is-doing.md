# ADR: What the database is actually doing

Status: accepted, 2026-09-03. The Admin tab now shows every SQL statement this
application sends, its own log, and how long both take. The interesting decision
is not that it shows them. It is that the SQL table has nowhere to put a
parameter value.

## Context

The Admin tab already showed health, Azure's view of the container, an hour of
Application Insights, and a ring of recent errors. What it could not show is the
one thing a reviewer actually asks about a data-access layer: what queries does
this thing send, and how long do they take.

Entity Framework knows. It writes a line like this for every command, at
Information, on the `Microsoft.EntityFrameworkCore.Database.Command` category:

```
Executed DbCommand (6ms) [Parameters=[@normalizedEmail='?' (Size = 18)],
CommandType='Text', CommandTimeout='30']
SELECT "a"."Id", "a"."Email", ... FROM "AspNetUsers" AS "a"
WHERE "a"."NormalizedEmail" = @normalizedEmail LIMIT 2
```

So the data exists. The question is how to get at it, and what to keep.

## The constraint that decided the design

**This page is public.** That is not an oversight, it is ADR-010: the site is a
portfolio and the Admin tab is part of what it is showing. Anyone can open it.

And the parameters of a registration carry an email address. So do the
parameters of a sign-in. There is no version of "show the raw SQL" that is safe
by default here.

The obvious answer is a redaction rule: show values, mask anything that looks
like an address. The obvious answer is wrong in a specific and boring way. A rule
that lists what to hide is correct only for the columns that existed when
somebody wrote it. Add a phone number, a postal address, a note field, and the
rule is silently wrong, and it is wrong on a public page, and nothing fails.

## Decision

**Read the command, not the log line, and never read `Value`.**

The type the table is built from has no field for a parameter value:

```csharp
/// <summary>One parameter of a SQL statement, described but never valued.</summary>
public sealed record SqlParameterShape(string Name, string Type, int? Size);
```

That is the whole safety argument. Not a rule about which values to hide: no
place to put one. A column added next year cannot leak through this, because
there is nothing for it to leak into.

Getting there means an interceptor rather than a log filter:

```csharp
public sealed class SqlLogInterceptor(ISqlLog log, ICurrentRequest request) : DbCommandInterceptor
{
    private void Record(DbCommand command, TimeSpan duration, string outcome)
    {
        var parameters = new List<SqlParameterShape>(command.Parameters.Count);
        foreach (DbParameter parameter in command.Parameters)
        {
            // Name, type, size. Never Value.
            parameters.Add(new SqlParameterShape(
                parameter.ParameterName,
                parameter.DbType.ToString(),
                parameter.Size == 0 ? null : parameter.Size));
        }
        ...
    }
}
```

Reading EF's own message and taking the values back out of it would mean the
values had already been formatted into a string, already been in memory as a
message, and already been handed to every other logging provider in the process,
Application Insights included. Removing something after it has been broadcast is
not removing it.

**A statement carries the request that caused it.** A list of SELECTs with no
cause is a screensaver. The same list beside `GET /api/vehicles?make=Ford` is an
explanation, and it is the thing an interviewer would actually ask about. The
interceptor gets it through a port:

```csharp
/// <summary>
/// Port: what request is in flight on this call path, as one short string like
/// "GET /api/vehicles". Infrastructure asks; the API answers from the ambient
/// HttpContext. Outside a request, such as at startup, the answer is null.
/// </summary>
public interface ICurrentRequest
{
    string? Describe();
}
```

Infrastructure does not learn what an `HttpContext` is, which is the same reason
`IVehicleSource` exists.

**Percentiles are computed on read.** A running percentile needs a sketch, and a
sketch needs a reason. These rings hold a few hundred entries; sorting a few
hundred longs costs microseconds and the answer is exact for the window rather
than approximate forever. Nearest rank, no interpolation, and the empty sample
answers zero rather than throwing.

**Everything is in this process's memory and the page says so.** Three rings:
200 statements, 300 log lines, 500 requests. They empty on every container roll.
A log store is exactly the kind of thing that turns a free tier into a bill, and
this is a demo.

## What the tests hold

The test that matters registers an account and then reads the endpoint:

```csharp
string email = $"leak-canary-{Guid.NewGuid():N}@example.com";
await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "correct horse battery" });

string body = await _client.GetStringAsync("/api/admin/sql");

Assert.Contains("AspNetUsers", body, StringComparison.Ordinal);
Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
```

It asserts both halves. That the statements are there, so the test cannot pass by
the section being broken, and that the address is not. A second test walks every
parameter in the response and asserts the serialized object has exactly three
fields, so a future `Value` property fails here rather than on the live site. The
browser suite repeats the same check against the rendered page.

## Consequences

- A reviewer can watch the queries this application runs, see which request
  caused each one, and see how long the database took, without a login and
  without a trace tool.
- The slowest endpoint on the site is now a number on a page rather than a guess.
- A parameter value cannot reach the page through this path, and the reason is
  structural rather than a rule somebody has to maintain.
- Three more fixed-size buffers in memory, about a megabyte at capacity.
- Timing is measured outside the error middleware, so a request that fails is
  still timed. An endpoint that fails slowly is the one worth seeing.

## Files

- [`api/TheYard.Application/SqlLog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/SqlLog.cs): the types with nowhere to put a value, and the two ports.
- [`api/TheYard.Infrastructure/SqlLogInterceptor.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/SqlLogInterceptor.cs): reading the command instead of the log line.
- [`api/TheYard.Api/AdminObservability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/AdminObservability.cs): the three rings, the logging provider, and the percentiles.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): where they are wired, and the timing middleware.
- [`api/TheYard.Tests/AdminObservabilityTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AdminObservabilityTests.cs): the canary, the shape check, and the percentile table.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the three sections.
- [`docs/ADR-010-observability.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-010-observability.md): why this page is public in the first place.

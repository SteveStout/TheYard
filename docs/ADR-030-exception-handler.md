# ADR: The exception handler, and the endpoint that proves it

Status: accepted, 2026-09-03, shipped as 1.0.0.40. This extends
ADR: Error handling rather than replacing it: that record decided the shape,
this one decides what goes in it when nobody wrote the failure.

## Context

ADR: Error handling shipped `AddProblemDetails`, `UseExceptionHandler()` and a
`traceId` on every response, and the suite proves the shape on a rejected
query, a rejected bid, a rejected browser report and a 404. Every one of those
is a failure an endpoint returns on purpose.

The failure nobody wrote code for was never tested, and there is a reason it
was easy to miss: the built-in handler does produce a correctly shaped 500, so
reading the code gives no sign anything is missing. Running it does. Three
things were wrong.

**The developer exception page was answering, not the handler.** `WebApplication`
inserts it automatically in Development, in front of everything the app
registers. So the response a developer sees when something throws, and the
response a caller gets in production, were never the same response, and only
one of them had ever been looked at.

**The bare 500 says nothing at all.** No title, no detail. A caller gets a
status code and a trace id and no sentence, which is worse than it sounds: the
front end renders `detail` for every other failure, so a crash was the one case
that produced an empty message box.

**Nothing decided what a 500 may reveal.** Nothing leaked, but nothing was
stopping it either. The next person to add `Detail = ex.Message` to make
debugging easier would have been making a security decision without knowing it.

## Decision

**A custom `IExceptionHandler`, because three questions need an application's
answer and the framework cannot have one.**

*Which failures are the caller's fault.* `BadHttpRequestException` is the
framework saying the request was malformed, which is a different claim from
"this server broke". It carries its own status code and its message describes
the caller's own input, so it is answered at its status, with its message, and
logged as a warning rather than an error.

Minimal APIs do not raise that exception by default. A body that will not parse
answers a bare 400 with nothing in it, on the reasoning that a bad request
should not cost an exception. That left exactly one kind of failure on this API
without a sentence in it, so `RouteHandlerOptions.ThrowOnBadRequest` is on and
binding failures join everything else. The test that proves this is the test
that found it: it was written to assert one shape for every 400, and it failed
on the first run against the API it was describing.

*What a server failure may reveal.* One fixed sentence, `ProblemHandler.ServerDetail`,
and the trace id. The exception message, the type and the stack stay inside.
An error message that varies with the exception is a map of the inside of the
process, drawn for whoever asks.

*What makes the trace id worth returning.* One structured log line, written
before the response, carrying the exception, the method, the path, the status
and the same trace id. The response can afford to say nothing precisely because
everything it withholds is in that line.

There is a fourth case worth naming: a caller who hangs up mid-response
produces an `OperationCanceledException` on an aborted request. Nothing went
wrong, and there is no socket left to answer on, so it is a debug line and
returns handled, which also keeps it out of the Admin tab's error list.

**A deliberate failure endpoint, `/api/admin/selftest/exception`.** Every other
endpoint here is written not to throw, which is why the exception path had
never once run in production: the middleware's catch, the ring buffer's
record, the handler's log line and the Application Insights `exceptions` table
the Admin tab reads were all assumed to work end to end and none of them had
been asked. This endpoint asks. It throws a sentence chosen to be searchable,
and the answer it produces is the answer any real bug would produce.

It is public, like every other endpoint on this site, and cheaper to serve than
the inventory query. `DELETE /api/bids`, which resets the auction for everyone,
has been public since the first deploy.

**The tests run against Production.** `ProductionApi` is a factory that sets
the environment, because asking what a caller sees means asking the environment
a caller reaches. This is the point the whole record turns on: a test against
the default environment would have passed while asserting the developer
exception page's HTML.

## In the code

The handler (`api/TheBlock.Api/ProblemHandler.cs`):

```live path=api/TheBlock.Api/ProblemHandler.cs region=exception-handler
```

The five tests (`api/TheBlock.Tests/ExceptionHandlerTests.cs`):

```live path=api/TheBlock.Tests/ExceptionHandlerTests.cs region=exception-tests
```

## Consequences

- A crash now renders a sentence in the browser instead of an empty message
  box, and the sentence tells the reader what to quote.
- The shape of a crash and the shape of a rejected query are asserted to have
  the same fields, so a client needs one parser and not two. That test is what
  will fail if someone later adds a field to one path only.
- The exception path can be exercised against the live container, which is how
  the Admin tab's Application Insights card gets checked against a real
  exception rather than an absence of them.
- `/api/admin/selftest/exception` will appear in the Admin tab's recent-errors
  list every time it is used, which is correct: it is a 500 and the list is the
  list of 500s.
- One thing is deliberately not done: known domain exceptions are not mapped to
  statuses, because this application does not throw any. Every rule that can
  reject something returns a result instead. If that changes, the mapping goes
  here.

## Files

- [`api/TheBlock.Api/ProblemHandler.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/ProblemHandler.cs): the handler.
- [`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs): the registration, `ThrowOnBadRequest`, and the self-test endpoint.
- [`api/TheBlock.Tests/ExceptionHandlerTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/ExceptionHandlerTests.cs): the five tests, and the Production factory.
- [`api/TheBlock.Tests/ProblemDetailsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/ProblemDetailsTests.cs): the deliberate failures, unchanged.
- [`docs/ADR-023-error-handling.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-023-error-handling.md): the shape this fills in.

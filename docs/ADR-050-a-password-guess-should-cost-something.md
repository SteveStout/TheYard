# ADR: A password guess should cost something

Status: accepted, 2026-09-03. `POST /api/auth/login` had a lockout policy on
paper and none in practice, for as long as it has existed.

## What was there

The login endpoint is public, which it has to be, and it does the right things
about what it says: one sentence for a wrong password and one for an address
that is not registered, on purpose, so the endpoint is not a list of who has an
account here.

What it did not do is count. The check was:

```csharp
if (user is null || request.Password is null || !await users.CheckPasswordAsync(user, request.Password))
```

`UserManager.CheckPasswordAsync` verifies a password and does nothing else. It
does not touch `AccessFailedCount`, so lockout never engages, and nothing else in
the application was counting either: there is no rate limiter in front of it and
the identity options never configured a lockout.

The `AspNetUsers` table has had `LockoutEnd`, `LockoutEnabled` and
`AccessFailedCount` in it since the schema was written. Three columns, correctly
typed, faithfully migrated, and never once read or written.

That is the specific shape worth naming: **a control that exists in the schema,
in the framework and in everybody's mental model of what Identity does, and is
absent from the code path.** It is more comfortable than a missing control,
because everything about the system looks like it is there.

## What it allowed

An unmetered password oracle against real accounts in Azure SQL Database. One
request per guess, no delay, no counter, no ceiling. A wordlist against a known
address is bounded only by how fast the container answers.

The demo's own accounts are throwaway, so the loss is small. The property is not:
this is the endpoint in front of the only table in the system holding anything
that belongs to a person.

## Decision

Count the failures, and refuse for five minutes after five of them.

```csharp
options.Lockout.MaxFailedAccessAttempts = 5;
options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
options.Lockout.AllowedForNewUsers = true;
```

```csharp
if (await users.IsLockedOutAsync(user)) return refused;
if (!await users.CheckPasswordAsync(user, request.Password))
{
    await users.AccessFailedAsync(user);
    return refused;
}
await users.ResetAccessFailedCountAsync(user);
```

Four things about that, each of which is a decision rather than a line.

**The lockout is checked before the password.** Otherwise a locked account keeps
answering guesses, and the lock only stops the attacker from learning the answer
rather than from asking the question.

**It is asked through `UserManager` rather than by reading the column**, because
that is what knows the window has expired.

**A success resets the count.** Without it, five wrong guesses spread over a week
eventually lock somebody out of their own account, and a control that punishes
the legitimate user is how a control gets removed.

**The refusal after a lockout says exactly what a wrong password says.** A
distinct "this account is locked" tells a stranger the address is registered
here, which is the one thing this endpoint has always gone out of its way not to
say. The cost is a real user seeing a confusing message during their lockout,
which is the smaller harm.

## Why five and five, and what it actually buys

The numbers are arithmetic rather than strength. Five attempts per five minutes
is twelve guesses an hour per account. Against a wordlist that is the difference
between finishing and not finishing, and it is invisible to somebody who
mistyped their password twice.

It is per account, not per address, which is what matters here: an attacker with
a botnet defeats an IP limit and does not defeat this one.

## What this is not

**It is not a rate limit**, and this application still has none. The audit that
found this also found `POST /api/errors/client` and `POST /api/auth/register`
unmetered, and `GET /api/admin/selftest/exception` a free way to evict the public
error ring. A limiter is worth adding.

It is worth noting why one was not added here instead. Behind the edge, the
origin sees the edge's address for every visitor, so an IP-partitioned limiter is
a global cap rather than a per-attacker one, and the origin is directly reachable
in any case because the origin lock is still owed (ADR: Azure Front Door,
addendum), so an attacker can bypass the edge and forge whatever address they
like. A limiter here would be worth having and it would not be the control this
needed. Lockout does not depend on knowing who is asking.

## Consequences

- Six wrong guesses on an account stops working, including the sixth being right.
- Three columns that have been carried, migrated and indexed since the schema was
  written are now load-bearing.
- Two tests, and the one that matters asserts that the **correct** password is
  refused after five wrong ones, because a test that only checks the wrong
  password still passes when nothing is counting.

## Files

- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the options, and the login path that now counts.
- [`api/TheYard.Tests/AuthTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AuthTests.cs): the right password, refused.
- [`api/TheYard.Database/Tables/Identity/AspNetUsers.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Identity/AspNetUsers.sql): the three columns this was always going to use.
- [`docs/ADR-037-accounts.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-037-accounts.md): the accounts themselves.

# ADR: Identity and the session token, explained for a new developer

Status: accepted, 2026-09-03. The Operating Contract asks for a
junior-developer record for any foundational configuration this project
touches, and ADR: Accounts and per-user bids touched the composition root, the
request pipeline and the database schema in one change. That record says what
was decided. This one says what the words mean.

Read it beside `api/TheBlock.Api/Program.cs` and `api/TheBlock.Api/Tokens.cs`.
The samples below are those files, read from this build.

## Context

Before this change the API had no idea who was calling it. There was one set of
bids in memory, and "you are the high bidder" meant "the only person in the
room is". Adding accounts meant answering four questions that a newcomer to
ASP.NET Core will meet in that order:

1. Where do usernames and password hashes live, and who writes that code?
2. Once somebody proves who they are, how does the next request know?
3. How does the server read that proof back without trusting the browser?
4. How does an endpoint say "not for strangers"?

The answers are Identity, a signed token, a cookie the page cannot read, and
`RequireAuthorization()`. Each gets a section.

## 1. What Identity is

ASP.NET Core Identity is two things wearing one name.

The first is a **schema**: seven tables, all prefixed `AspNet`, holding users,
their password hashes, their roles, their external logins and their claims. You
can see them created in
`api/TheBlock.Infrastructure/Migrations/20260903084932_AccountsAndPerUserBids.cs`.
This project uses one of the seven. The other six are along for the ride
because the store that manages the first one knows how to manage all of them.

The second is a **service**, `UserManager<YardUser>`, which is the only thing
the endpoints actually call. It hashes passwords with PBKDF2 and a per-user
salt, it normalises email addresses so that `Steve@Example.com` and
`steve@example.com` cannot both register, and it enforces the password rules
configured beside it. None of that is hard to describe and all of it is easy to
get subtly wrong, which is the argument for not writing it here.

`YardUser` is the project's own user row. It inherits everything Identity needs
and adds one column:

```csharp
public sealed class YardUser : IdentityUser
{
    public long CreatedAtMs { get; set; }
}
```

That is the whole customisation. Inheriting from `IdentityUser` rather than
copying its fields is what lets `AddEntityFrameworkStores<YardDbContext>` find
the columns it expects.

### AddIdentityCore versus AddIdentity

This distinction traps people, so here it is plainly.

`AddIdentity<TUser, TRole>()` registers the user store **and** Identity's own
cookie authentication scheme, its sign-in manager, its redirect-to-login-page
behaviour, and a set of cookies named `.AspNetCore.Identity.Application`. It is
built for a server-rendered site with login pages.

`AddIdentityCore<TUser>()` registers the user store and stops. No
authentication scheme, no redirects, no second cookie.

This API is a JSON service behind a React page, and it already has an
authentication scheme, described in section 3. Calling `AddIdentity` here would
install a second, competing one, and the symptom would be a 302 redirect to a
login page that does not exist where a 401 was expected. `AddIdentityCore` is
the one line that avoids all of that.

```live path=api/TheBlock.Api/Program.cs region=auth
```

### Why a scoped DbContext sits beside the factory

ADR: The relational store registered `AddDbContextFactory<YardDbContext>`,
which hands out a fresh context on request rather than registering one.
Everything in this application is a singleton and loads its data once, so a
factory was the right shape.

Identity's Entity Framework store does not use a factory. It asks the container
for a `YardDbContext` directly, once per request, because that is the pattern
every ASP.NET Core sample uses. So the composition root adds one adapter
between the two:

```csharp
builder.Services.AddScoped(services =>
    services.GetRequiredService<IDbContextFactory<YardDbContext>>().CreateDbContext());
```

"Scoped" means one instance per HTTP request, disposed when the request ends.
That matters because a `DbContext` is not thread-safe and holds a change
tracker, so sharing one across requests would let two callers see each other's
half-finished work. This registration is the only scoped one in the
application, and the comment above it in `Program.cs` says why it exists so
that nobody deletes it as an oddity.

## 2. What signing a token means

A JWT is three base64 chunks joined by dots: a header saying which algorithm
was used, a payload of claims, and a signature. Two things about it surprise
people.

**It is not encrypted.** Anyone holding the token can read the claims. Paste
one into any JWT decoder and the email address is right there. That is why
`Issue` puts the account id and the email in it and nothing else: everything in
the payload is disclosed to whoever gets hold of it, and everything else the
application needs it can look up.

**The signature is what makes it trustworthy.** HMAC-SHA256 takes the header,
the payload and a secret key, and produces a fixed-length value. Change one
character of the payload and the value no longer matches. Since only the server
knows the key, only the server can produce a signature that validates, so a
token that validates is one this server issued. Forging one means guessing 32
random bytes.

```live path=api/TheBlock.Api/Tokens.cs region=issue
```

`ClockSkew` deserves a note. The library defaults to five minutes of slack on
expiry, which is a sensible allowance when two different servers with two
different clocks are involved. Here one process both issues and validates the
token, so five minutes of extra life is slack with no purpose behind it. Thirty
seconds is the setting.

### Where the key comes from

`Auth:SigningKey` is configuration: an environment variable in the container, a
user secret locally, a secret store in a real deployment. When it is absent the
process generates 32 random bytes and logs that it did.

That behaviour is deliberate and its cost is stated out loud: a restart with no
configured key signs everybody out, because the new process cannot validate
tokens the old one signed. The alternative a repository must never take is a
committed default. A signing key in source control is every session forever for
anyone who can read the repository, and it stays true after the file is deleted
because git keeps history.

## 3. Why the cookie is httpOnly

The token proves who you are. Whoever holds it is you, which is the property
that makes bearer tokens convenient and also the one that makes them dangerous.

The common alternative is `localStorage`: the page keeps the token in a
variable and sets an `Authorization` header on each request. That works, and it
means every script running on the page can read the token. Any injected script,
any compromised dependency, any browser extension with page access can take it
and use it from anywhere.

`HttpOnly = true` tells the browser to withhold the cookie from JavaScript
entirely. `document.cookie` does not show it. The page never has the token, so
the page cannot leak it, and the client code gets shorter rather than longer:
`src/api/auth.ts` never touches a token at all.

The other attributes each earn their place:

- **`SameSite = Lax`** stops the cookie from riding along on a cross-site POST,
  which is the shape a CSRF attack takes. Lax rather than Strict so that
  arriving from an external link still finds you signed in.
- **`Secure`** tells the browser to send the cookie over HTTPS only. It is
  computed rather than hardcoded, because behind the edge this process is
  spoken to over plain HTTP even when the visitor's connection was HTTPS the
  whole way. `X-Forwarded-Proto` is the header that carries that fact across
  the hop, and ADR: Edge economics explains the hop.
- **`Path = "/"`** so one cookie covers the API and the page. Deleting a cookie
  means re-sending it with the same path and same-site attributes, which is why
  the logout endpoint calls the same `CookieFor` helper the login endpoint
  does. Get that wrong and the browser keeps a second cookie of the same name
  on a different path, and the user stays signed in after signing out.

## 4. How the server reads it back

`AddJwtBearer` expects the token in an `Authorization: Bearer ...` header. This
project puts it in a cookie, so one event hook redirects the lookup:

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        if (context.Request.Cookies.TryGetValue(TokenIssuer.CookieName, out string? cookie))
        {
            context.Token = cookie;
        }
        return Task.CompletedTask;
    },
};
```

`OnMessageReceived` runs before validation and its job is to answer "where is
the token". Setting `context.Token` says: here, use this. Everything after that
point is the normal path, signature check included, so nothing about validation
has been weakened by moving where the token was found.

Two lines in the pipeline finish the job:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

Order matters and the names are easy to blur. **Authentication** is "who are
you": it reads the token and builds `HttpContext.User`. **Authorization** is
"may you": it checks that principal against whatever the endpoint requires.
Authentication has to run first, because authorization cannot check a principal
nobody built yet. Put these two after the endpoints and every request arrives
anonymous.

Both run on every request, including the ones that do not require an account.
That is on purpose. A signed-in visitor loading the listing gets a populated
`HttpContext.User`, which is how `GET /api/bids` knows whose badges to draw
without a separate request.

## 5. What RequireAuthorization does

`.RequireAuthorization()` attaches metadata to one endpoint saying a principal
is required. The authorization middleware reads that metadata and, finding no
authenticated user, ends the request with 401 before the handler runs.

Four endpoints carry it: placing a bid, buying now, clearing your bids, and the
bid history. Reading stays open, so an anonymous visitor still watches the
auction and cannot bid in it.

```live path=api/TheBlock.Api/Program.cs region=auth-endpoints
```

Inside an endpoint that requires it, `http.UserId()` reads the account id from
the validated claims:

```live path=api/TheBlock.Api/Tokens.cs region=who
```

It throws rather than returning null. Reaching that line with no id would mean
authorization admitted a token with no subject, and a graceful fallback there
hides a broken pipeline behind a working-looking page. `UserIdOrNull()` is the
sibling for endpoints where signing in is optional.

## What to change when

- **A new endpoint that changes something:** `.RequireAuthorization()` on the
  map call, `HttpContext` in the parameter list, `http.UserId()` for the owner.
  A test in `api/TheBlock.Tests/AuthTests.cs` should assert it answers 401 to a
  caller with no cookie, because that is the assertion that fails when somebody
  copies the endpoint above it and forgets the attribute.
- **A new field on the user:** add the property to `YardUser`, then
  `dotnet ef migrations add <Name> --project api/TheBlock.Infrastructure
  --startup-project api/TheBlock.Api`. The startup project argument is required
  and ADR: The relational store explains why.
- **A password rule:** the options block inside `AddIdentityCore`. Length is
  the requirement with evidence behind it; the composition rules mostly teach
  people to write the password down, and NIST 800-63B says so at more length.
- **The token's contents:** `TokenIssuer.Issue`. Remember that every claim is
  readable by anyone holding the token and is sent on every request.
- **A real deployment:** set `Auth__SigningKey` in the container's environment
  from a secret store. Nothing else changes, and nothing about the key ever
  enters the repository.

## Files

- [`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs): the composition root, the pipeline, and the four auth endpoints.
- [`api/TheBlock.Api/Tokens.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Tokens.cs): issuing, validating, the cookie attributes, and reading the caller back out.
- [`api/TheBlock.Api/Accounts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Accounts.cs): the request and response shapes, and the translation of Identity's error codes into a sentence.
- [`api/TheBlock.Infrastructure/YardUser.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/YardUser.cs): the user row and its one extra column.
- [`api/TheBlock.Infrastructure/YardDbContext.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/YardDbContext.cs): now an `IdentityDbContext`, which is what puts the seven tables in the model.
- [`api/TheBlock.Tests/AuthTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/AuthTests.cs): the suite that holds all of this to its claims, including that the token never appears in a response body.
- [`docs/ADR-037-accounts.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-037-accounts.md): the decisions this record explains.
- [`docs/ADR-018-program-cs-explained.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-018-program-cs-explained.md): the rest of the host file, walked the same way.

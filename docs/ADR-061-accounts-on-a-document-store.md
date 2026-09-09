# ADR: Accounts on a document store

Status: accepted, 2026-09-08. ASP.NET Core Identity, over one document per
account and one document per email address, in a container partitioned on the
account id. Parent: ADR: A second store on Cosmos DB, and what it costs. The
relational side is unchanged and is described in ADR: Accounts and per-user
bids and ADR: Identity and the session token, explained.

## Context

Identity's relational shape is seven tables: a user row plus claims, logins,
tokens, roles and two join tables, related by foreign keys and read with joins.
This application uses one of the seven. It registers an address with a password,
signs in by address, locks an account after five wrong guesses, and asks "who am
I" by id. It has no roles, issues no Identity tokens, holds no external logins
and stores no claims; its session is a JWT it signs itself (ADR: Accounts and
per-user bids).

There are no joins on the document store, and there is no unique index that
spans partitions. Those two facts are the whole design problem.

## The three options

**Keep accounts on SQL Server.** A two-store application, with the hard part
avoided. It is what a lot of real systems do and it would have been defensible.
Steve chose the full stack, and a comparison that kept accounts relational
would not measure sign-in, which is one of the three paths the parity bar is
earned on.

**`IdentityDbContext` on the EF Core Cosmos provider.** It compiles and it runs.
Seven entity types land in a document store as seven document shapes with a
discriminator; `FindByEmailAsync` becomes a query across every partition on a
property no index covers unless one is paid for on every write; and two
registrations of the same address a few milliseconds apart both succeed, because
the unique index that stops them on SQL Server (`UserNameIndex`, on the
normalized user name, which is the address here) does not exist on this
side. Nothing fails. The store just has two accounts with one
address, and the next sign-in finds whichever it finds.

**A custom `IUserStore` over one document per account.** Chosen.

## Decision

**One container, `users`, partition key `/id`.** Two document shapes in it.

```live path=api/TheYard.Infrastructure.Cosmos/Documents.cs region=documents
```

**The account is one document**, id and partition key both the account id, so
"who am I" is one point read, about one request unit. It carries exactly the
columns of the user row this application reads: the address and its normalized
form, the password hash, the security stamp, the lockout fields and the count
of failed guesses, and the one field this application added to Identity's
user, when the account was created.

**The address is a second, tiny document**, id `email:<normalized address>`,
carrying the account id. It exists to be unique. A store that cannot declare a
unique constraint across partitions can still refuse to create two documents
with one id in one partition, and a document whose id is the address and whose
partition is its own id is exactly that: the second registration of an address
is a 409 from the store, not a race that both sides win.

**Registering writes the claim first and the account second.**

```live path=api/TheYard.Infrastructure.Cosmos/CosmosUserStore.cs region=create
```

If the claim is refused the registration fails with Identity's own duplicate
email error, and `Accounts.Explain` turns that into the same sentence the
relational side shows. If the account write fails after the claim succeeded, the
claim is deleted again, so a failed registration leaves nothing behind.

**Sign-in is two point reads.** The claim by address, then the account by the id
the claim carries. About two request units and two round trips, in region, and
neither of them a query:

```live path=api/TheYard.Infrastructure.Cosmos/CosmosUserStore.cs region=find
```

This application registers the address as the user name, so Identity's lookup by
normalized user name is the same lookup as the one by normalized email, and the
store treats them as one.

**A failed guess is one replace, carrying the etag.** Identity keeps a
concurrency stamp on the user and rotates it on every update; on this side the
stamp is the document's own `_etag`, read with the document and sent back as
`If-Match` on the replace. A stale one is refused with 412, which the store
reports as Identity's concurrency failure, exactly as a row that moved would be
reported on SQL Server:

```live path=api/TheYard.Infrastructure.Cosmos/CosmosUserStore.cs region=update
```

A successful sign-in with no failed guesses on the account writes nothing at
all, because `UserManager.ResetAccessFailedCountAsync` returns before touching
the store when the count is already zero.

**The store implements five interfaces and no more**: the base, password, email,
lockout and security stamp. Roles, claims, logins and tokens are not implemented,
which means a call to them fails loudly at registration time rather than
silently at runtime, and which is the honest description of what this
application uses.

## Where the document model costs something

Said plainly, because the comparison record keeps this list.

- **Uniqueness is a document you write, not a constraint you declare.** The
  claim document is a line of code doing what one line of DDL does on the other
  side, and a reviewer has to know it is there.
- **A process that dies between the two writes leaves a claim with no account.**
  Until it is cleaned up, that address reads as taken. The relational side has
  no such window because the row and its unique index are one write.
- **There is no cascade.** Deleting an account on SQL Server takes its bids with
  it, because the foreign key says so. Here a bid is a document in another
  container partitioned on the buyer, and deleting the account leaves them. This
  application has no delete-account endpoint, so nothing reaches this today; the
  record says it rather than a visitor finding it.
- **Nothing enforces the shape.** A document with a missing field is a document
  with a missing field. The conformance tests on the relational side compare
  DDL to model; here the round-trip tests are what hold the shape.

## What the tests hold

Against the real account, on the runner, as the signed-in principal, through a
real `UserManager<YardUser>` rather than the store's methods directly, so what
is tested is what the endpoints call:

```live path=api/TheYard.Tests/CosmosStoreTests.cs region=accounts
```

Registration creates two documents, the second registration of the address is
refused with a duplicate error, sign-in finds the account and checks the
password, a failed guess moves the count through a replace, the store ran no
query at all, no operation in the store log carries the address, and deleting
the account frees the address. The suite also runs whole with every application
booted on the document store, so the accounts tests written for SQL Server run
against this store unchanged (ADR: A second store on Cosmos DB, and what it
costs).

## Consequences

- Sign-in and "who am I" are point reads, which is the cheapest thing the store
  does, and the comparison card can put their charge beside their milliseconds.
- Identity's seven tables are four fewer document shapes than the provider
  would have made, and the two that exist are readable beside the API's own JSON.
- The address is unique by construction and the construction is visible.
- The costs above are real and are written here rather than found later.

## Files

- [`api/TheYard.Infrastructure.Cosmos/CosmosUserStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosUserStore.cs): the five interfaces, the claim, the etag.
- [`api/TheYard.Infrastructure.Cosmos/Documents.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/Documents.cs): the two document shapes.
- [`infra/cosmos/users.json`](https://github.com/SteveStout/TheYard/blob/main/infra/cosmos/users.json): the container, partitioned on the id, indexing nothing.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): where Identity is given this store instead of the Entity Framework one.
- [`api/TheYard.Tests/CosmosStoreTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CosmosStoreTests.cs): the proof, against the real account.
- [`docs/ADR-037-accounts.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-037-accounts.md): the relational side, and the endpoints both sides serve.

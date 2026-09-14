# ADR: The code is public and the secrets are not

Status: accepted, 2026-09-13, shipped as 1.0.0.117. Steve's ruling, on the
day the visitor table went behind a key: "There should be no secret keys in
the code, it should all be managed in other ways", so that "anyone who has
the code doesn't have full access to the data", and then, on the key itself:
"do that as long as I can see the table from the deployed website."

## Context

This repository is public and always has been. Anybody can read every line,
clone it, build it and run it, and the site's own docs sidebar serves the
source of every decision back to the visitor. That is the point of a
portfolio, and it sets the one security question the project has to get
right: **what does a person who holds the whole repository not hold?** The
answer has to be "nothing that opens the live data", and it has to stay true
under a full clone, a fork, a search of the git history and a read of every
workflow log.

Until 1.0.0.114 the project had exactly one secret, the session signing key,
and one convention for it, written when it arrived (ADR: Three readers with
no memory of the project): a repository secret, filled into the container
manifest at roll time, never a value in a file. The Admin tab's visitor rows
(ADR: Site activity, and the line an address does not cross) added a second,
the operator's key that opens the per-visitor table, and with two of them the
convention deserved a record of its own, written as the security decision it
is rather than a paragraph inside a feature.

## Decision

**No secret is a value in this repository.** Not in code, not in
configuration, not in a manifest, not in a test, not in the history. A secret
is a name here and a value somewhere the code cannot read.

There are two secrets, and they travel one way. `YARD_AUTH_SIGNING_KEY` and
`ADMIN_KEY` are repository secrets on GitHub. The deploy workflow receives
each as an environment variable of one step, substitutes it into a copy of
the container manifest under `$RUNNER_TEMP` in python (never sed, because a
secret may carry a shell's syntax), and hands the copy to `az container
create`. The manifest in the repository carries the placeholders
`__YARD_AUTH_SIGNING_KEY__` and `__ADMIN_KEY__` as `secureValue` entries,
which Azure keeps out of `az container show` and out of the portal. GitHub
masks each secret in the workflow log, and the step's own echo never prints
them. The running process reads `Auth:SigningKey` and `Admin:Key` from
configuration, and the code knows only the names.

**Everything else authenticates as the machine, with no secret at all.** The
workflow signs in to Azure with OpenID Connect (`azure/login` with a client
id, a tenant id and a subscription id, all three repository variables and
none of them secret): GitHub proves to Entra ID that this workflow on this
branch is running, and Entra ID issues a token for the job. The container
reaches Azure SQL Database as its own managed identity: the connection string
is composed in the workflow from the server name, the database name and the
identity's client id, with `Authentication=Active Directory Managed
Identity` and no password, because there is none. The container reaches
Azure Cosmos DB the same way, `Cosmos__Credential: managed-identity`, so the
account's keys are never read, never copied and never in a place a leak
could start. Application Insights' connection string is read from Azure at
roll time and masked; it is an address rather than a credential, and it is not in
the repository either.

**A missing secret degrades, never crashes, and never opens anything.** The
substitution checks its own work: a placeholder left behind is detected in
the workflow, logged as a warning, and the container starts with the setting
reading as "no value". No signing key means the process invents one at
startup and sessions end on the next roll. No admin key means the endpoint
the key guards answers 404 to everybody, including the operator. A lost
secret makes the site smaller. It never makes it open.

**The keys in the tests are fixtures, and they unlock nothing.** `the-test-key`
in the xUnit suite and `e2e-admin-key` in the Playwright configuration are
plain strings in the repository on purpose. Each is handed to a throwaway host
the test itself starts, compared in constant time against the same string,
and discarded with the host. They are on the public side of the line because
they open nothing on the other side.

**What the repository holder does get.** The aggregate activity graph, the
metrics card, the proof card and the health page, because those are public
by design (ADR: Observability). The ability to run the whole system against
their own stores, which is the point. What they do not get is a session
signed as anybody else, a row from the live visitor table, or a byte from
either live database, because none of the values that would allow it exist
anywhere they can reach.

**Rotation is a two-minute operation with no code change.** Set the secret
to a new value on GitHub, roll. The signing key's rotation ends every
session, by design; the admin key's rotation changes the operator's URL and
nothing else.

## Alternatives

**Azure Key Vault, read by the container at startup.** The right answer at
the next size up, and not this one. Two secrets, both consumed by one
deploy step, do not earn a vault, a role assignment, a network rule and a
second thing to be unreadable at three in the morning. The GitHub secret
store is already the thing a roll trusts. If a third party ever needs a
secret this pipeline does not hand it, a vault is where it goes, and this
record gets an addendum.

**The admin key in the code, "obfuscated".** Rejected outright. A value in a
public repository is public whatever shape it is written in, and the history
keeps it after it is removed. His ruling is the whole reason this record
exists.

**A signed-in operator instead of a key.** The Admin tab could ask for the
owner's account and check a role. Rejected for now because an account here is a
bidder (ADR: Accounts and per-user bids) and carries no role to anchor a
check to; a key in a URL he keeps is the smaller surface. Reversible
in one record if the account model ever grows a role.

**A secret scanner in the gate.** Considered, and held back because it would
be a test asserting an absence it cannot define: the two fixture strings are
meant to be there, and a scanner that allows them by name is a list, and a list
proves nothing. What holds the line instead is the shape: no setting in the repository
carries a value that opens anything, and this record is the place a reviewer
checks that against.

## Consequences

- Two repository secrets, `YARD_AUTH_SIGNING_KEY` and `ADMIN_KEY`, are the
  whole secret inventory. Anything added to that list gets an addendum here
  before it ships. The email sender added the same evening added nothing to
  it: Azure Communication Services is reached as the containers' identity,
  and its endpoint and sender address are plain values in the manifests.
- The workflow's identity, the SQL identity and the Cosmos DB identity are all
  federated or managed; there is no password or account key to rotate, leak or
  find in a log.
- A fork runs, on the forker's own stores, with no secret copied from anyone.
- The operator's admin URL carries the key as a query parameter, so it is his
  to keep out of screenshots and bookmarks he shares. The page reads it once
  at load and drops it from the address bar on the first render.

## Files

- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml) and [`deploy-cosmos.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy-cosmos.yml): the OIDC sign-in, the substitution, and the checks that a placeholder never ships unnoticed.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml) and [`aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard-cosmos.yaml): the placeholders as `secureValue`, the identity settings as plain values, and the comment beside each saying which is which.
- [`api/TheYard.Api/Activity.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Activity.cs): the admin key compared in constant time, and 404 when there is none.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the two settings read by name.
- [`playwright.config.ts`](https://github.com/SteveStout/TheYard/blob/main/playwright.config.ts) and [`api/TheYard.Tests/ActivityTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ActivityTests.cs): the two fixture keys, and why they are allowed to be in the open.

```live path=.github/workflows/deploy.yml region=secrets
```

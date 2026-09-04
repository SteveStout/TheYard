# Security

TheYard is a public demo with real accounts and a real database. That combination
is what makes the question worth answering properly: nothing here is worth
stealing, and the shape of the answer is the same whether or not it is.

This document says what is protected and how, what is deliberately not, and what
went wrong and got fixed. The third section is the useful one. A threat model
with no findings in it has usually not been applied.

## Reporting something

Open an issue on [the repository](https://github.com/SteveStout/TheYard/issues),
or email the address on the resume the site serves. There is no bounty and no
formal window, and anything real will be fixed and written down in a record here
with what it allowed.

## What is protected, and by what

**The database has no password, because it has no login.** The Azure SQL server
was created Entra-only, so SQL authentication does not exist on it. The container
reaches it as the user-assigned managed identity it already carries, and the
connection string in its environment is a server name, a database name and an
authentication mode. There is nothing in it to leak
(ADR: The SQL Server backend).

**That identity can read and write rows, and nothing else.** It holds
`db_datareader` and `db_datawriter`. It cannot create, alter or drop a table,
which is why the schema is a SQL project published separately from the
application (ADR: Data first, and the database in source control). An
application that cannot change its own schema cannot be made to.

**Passwords are hashed by ASP.NET Core Identity and never stored.** The session
is a JWT this service signs and reads itself, carried in an httpOnly cookie, so a
script on the page cannot read it and cannot be tricked into sending it elsewhere
(ADR: Accounts and per-user bids).

**Five wrong passwords buy five minutes off**, per account, and the refusal says
exactly what a wrong password says, so the endpoint is not a list of which
addresses are registered here (ADR: A password guess should cost something).

**Public surfaces publish shapes, not values.** The Admin tab shows every SQL
statement the application sends, with each parameter's name, type and size. Not
its value: the type those rows are built from has no field for one, so there is
no redaction rule for a future column to get past. Exception messages do not
reach any public surface either, because that is where a driver writes a server
name, a login name or the value that broke a constraint; the type goes to the
page and the message goes to Application Insights
(ADR: What the database is actually doing, ADR: Reviewing my own work).

**Every state-changing endpoint identifies its caller** and changes only that
caller's data, with one deliberate exception below.

**No secret is in the repository.** Not the connection string, not the
Application Insights key, not the JWT signing key. The build has no credential in
it, and CI has no Azure credential at all, which is also why the coverage number
has the shape it does (ADR: Counting what the tests cover).

## What is deliberately not protected, and why

**The Admin tab is public.** That is the decision, not an oversight
(ADR: Observability). The site is a portfolio and the running system reporting on
itself is part of what it is showing. The cost is that everything on that page
has to be safe to publish, which is a constraint the page is designed around and
which has been got wrong twice and corrected twice, both recorded.

**The origin is reachable without going through the edge.** The design is Front
Door with a locked origin, it is in the template, and it needs a subscription
this project does not pay for. That trade was easy when the origin held nothing.
It is a smaller trade now that it holds accounts, and ADR-001's addendum says so
rather than pretending the control is in place.

**There is no per-address rate limiter.** Behind the edge, the origin sees one
address for every visitor, so an IP-partitioned limit is a global cap rather than
a per-attacker one, and because the origin is directly reachable an attacker can
bypass the edge and forge whatever address they like. A limiter of that shape
would be worth having and would not be the control that mattered, which is why
the effort went into per-account lockout instead. This is still a gap and is
named as one.

**Registration has a ceiling, and it is the one limit that argument never
covered.** The paragraph above is about partitioning: any limit that has to tell
one visitor from another is either lying to itself behind the edge or being lied
to in front of it. A limit that does not partition has nothing to forge. Since
1.0.0.74 this site accepts 120 new accounts per sliding hour, counted across the
whole site, which matters because registration is the only write an anonymous
caller can make that persists, and because every request through it pays for a
deliberately expensive password hash on a container that serves everything else.
What it costs is stated rather than hidden: while somebody is spending the
hour's allowance, a real visitor cannot register either, and browsing, signing
in and bidding are untouched. The window is in memory in one container, so a
second instance would get its own; a durable bound belongs with the origin lock
(ADR-054).

**`POST /api/errors/client` is anonymous.** A crash in the page should reach the
same place a crash in the server does. Its message and stack are bounded, and
browser reports keep their own ring so a flood of them cannot push real server
errors off the page an operator would read during an outage.

**The bidding data is invented.** Every vehicle is synthetic and every bid is
play money. This lowers the stakes; it does not change the shape of any of the
above, which is the point of writing it down.

## What went wrong

All of this was found on 3 September 2026, in one afternoon of auditing, mostly
by reading comments whose justification had expired.

| What | What it allowed | Record |
| --- | --- | --- |
| `DELETE /api/bids` took no user | Any signed-in visitor could delete every other visitor's bids, durably | ADR-048 |
| `POST /api/market/tick` took no account | A stranger with curl could counter-bid every auction any signed-in visitor was winning and move the public listing's prices | ADR-049 |
| Login never counted failures | An unmetered password oracle against real accounts | ADR-050 |
| A database exception's message reached the log section | Server hostname, login name and caller address on a public page | ADR-045 |
| The same, in the error buffer next to it | The same, for every unhandled exception | ADR-045 addendum |
| The metrics endpoint returned the whole request ring | A live feed of what every other visitor was doing | ADR-045 |
| Three scrollable tables with no keyboard access | WCAG 2.1.1, found by CI once CI could say what it found | ADR-042 addendum |
| Registration had no ceiling | The only anonymous write that persists, unbounded in rows and in a container's CPU, one loop away | ADR-054 |

The pattern in the first three is one thing, and it is the reason this page exists:
each was a deliberate decision, defended in a comment, correct when it was
written, and left standing after the assumption underneath it changed. Accounts
arrived and a database arrived, and the sentences did not move. That is harder to
catch than an absent thought, because the file reads as though the question has
been settled.

The habit that comes out of it: when a system gains users, or persistence, or
anything a person would call theirs, the comments that say "this is only a demo"
are a work list.

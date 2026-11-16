# ADR: One SQL database, one schema per area

Status: accepted, 2026-11-16, at 1.0.5.54. TheYard runs on one Azure SQL database, `sqldb-theyard-ss-basic` on the server `sql-theyard-ss-westus3`, and every area that keeps relational data keeps it in a schema of its own inside that database: the site's tables in `dbo`, the blog in `blog`, the campaign in `campaign`, the campaign card's view in `card`, the tracker in `jobhunt` and the network in `network`. A new area adds a schema, never a database. The rule is Steve's, given on 15 November: one database with a schema per area, and more than one database is a red flag.

## Reading list

1. [ADR: The SQL Server backend](https://theyard.stevenstout.biz/?doc=adr-sql-server): why the site has a relational store at all, and the addendum that moved it onto the Basic database.
2. [ADR: Data first, and the database in source control](https://theyard.stevenstout.biz/?doc=adr-data-first): the schema as a project in the repository, which is where each area's schema lives.
3. [ADR: Unused resources are deleted](https://theyard.stevenstout.biz/?doc=adr-unused-resources-are-deleted): the rule that deleted the paused serverless database.
4. [ADR: The campaign sends, the model writes rows](https://theyard.stevenstout.biz/?doc=adr-the-campaign-sends-the-model-writes-rows): the addendum that folded the campaign's own database into the site's as schemas.

## In plain words

Everything the site and the job hunt keep in SQL lives in one database, and each part of the work has its own named section inside it (a schema), so the blog's tables, the campaign's tables and the tracker's tables never mix, and each process may sign in only to the sections it needs.

What that is worth: one database costs $4.90 a month on the Basic tier, and a second one would cost the same again for no gain; there is one thing to back up, one firewall and one set of sign-ins, and a test fails the build before a second database can be added by accident.

## Context

The site has run two databases twice and gone back to one both times. The free serverless database beside the Basic one spent its month's allowance in two weeks and was paused, then deleted on 19 October under ADR-106. The campaign was given a Basic database of its own on 24 October so that a deploy of it touched nothing else, and on 25 October, on Steve's ask, its tables moved into the site's database as the `campaign` and `card` schemas, with the tracker's tables beside them in `jobhunt`, and that database was deleted (ADR-118, the addendum of 1.0.4.72).

So one database has been the practice since 25 October, but no record said it was the rule, and Azure's own cost view still counted three databases on 15 November, because both deletes fall inside its thirty-day window. Steve ruled that day that the answer is one database with a schema per area. Read live on 16 November at 08:08 Central (`az sql server list`, `az sql db list` on every server): the subscription has one SQL server, `sql-theyard-ss-westus3`, holding `master` and `sqldb-theyard-ss-basic` on the Basic tier, and nothing else.

## Decision

1. **One database.** `sqldb-theyard-ss-basic` on `sql-theyard-ss-westus3` is the only SQL database. `master` is the server's own and is not counted.
2. **One schema per area.** The site's tables stay in `dbo` (`api/TheYard.Database`); the blog's in `blog` (the same project); the campaign, its card, the tracker and the network in `campaign`, `card`, `jobhunt` and `network` (`functions/JobHunt.Database`). A new area adds a `CREATE SCHEMA` to the project that owns it.
3. **Access is by schema, not by database.** Each process signs in as its own identity and is granted only the schemas it uses, as ADR-118's addendum set out: the Function's user reaches the job hunt's schemas only, and the site's identity is denied `campaign` and `jobhunt` and may read `card`.
4. **No template declares the database.** The database was made by hand and is read by name. A database name reaches Azure only through three kinds of value, so those are what is held: each template's parameter default for the Initial Catalog (`sqlDatabase` in `infra/appservice.bicep`, `campaignSqlDatabase` in `infra/jobhunt.bicep`), the deploy's `SQL_DB`, and the names the Admin cards read it by (`Azure.Resources.SqlDatabase` and `CampaignDatabase` in the site's settings).

## What holds it

`OneSqlDatabaseTests`: no template under `infra/` declares a `Microsoft.Sql/servers/databases` resource or a child database of a server; every Initial Catalog in a template reads a parameter whose default is the one database, and the site's and the job hunt's templates are both among them; every Initial Catalog in a workflow reads a variable that workflow sets to the one database; and every database name written in full in a template, a workflow or a settings file is the one database.

## Consequences

- A change that needs separate data for a new area writes a schema and a grant, not a database and a connection string. If one area ever outgrows the Basic tier's 2 GB, the tier of the one database moves, and that is a decision for Steve with its cost written here first.
- A deploy of one area's schema reaches the same database as every other area's. That is the cost ADR-118's first decision avoided, and Steve accepted it on 25 October: a schema project publishes only its own objects, and each identity can touch only its own schemas.
- Azure's cost view keeps counting the two deleted databases until its window passes 25 October; the Admin cards read the live list, which shows one.

## Where it sits

Outside the rings. The rule is about the Azure resources and the templates and workflows that name them; the code in Infrastructure takes its connection string from configuration and does not know how many databases exist. The schemas themselves sit in the two database projects beside the code that owns each area.

## Files

- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep) - the site's connection string, from `sqlDatabase`
- [`infra/jobhunt.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/jobhunt.bicep) - the job hunt's connection string, from `campaignSqlDatabase`
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml) - the roll's `SQL_DB`
- [`api/TheYard.Database/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Database) - the site's schemas, `dbo` and `blog`
- [`functions/JobHunt.Database/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Database) - the job hunt's schemas
- [`api/TheYard.Tests/OneSqlDatabaseTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/OneSqlDatabaseTests.cs) - the one database, held in the templates, the workflow and the settings

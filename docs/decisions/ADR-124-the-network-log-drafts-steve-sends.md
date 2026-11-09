# ADR: The network log drafts, Steve sends

Status: accepted, 2026-11-04, at 1.0.4.136, over the four versions before it (1.0.4.132 to 1.0.4.135) that built what it records. The job hunt's Function logs every two-way conversation, by email and through the 6 AM program on LinkedIn, in one table, and on a Monday writes one follow-up draft per silence into the original Gmail thread under the label Follow-up/Ready, 21 days after the last exchange. Nothing sends. Steve presses Send.

## Reading list

1. [ADR: The campaign sends, the model writes rows](https://theyard.stevenstout.biz/?doc=adr-the-campaign-sends-the-model-writes-rows): the pattern this copies, the model writing rows to a file and code writing everything else, and the database the schema lives in.
2. [ADR: One host per process, the rings under them](https://theyard.stevenstout.biz/?doc=adr-one-host-per-process-the-rings-under-them): the rings the NetworkLog slice sits in, and why only the Gmail adapter writes a search.
3. [ADR: The modular monolith](https://theyard.stevenstout.biz/?doc=adr-the-modular-monolith): why what the campaign and the network log both read moved to the shared concerns.
4. [ADR: The Nightly Function on the Admin tab](https://theyard.stevenstout.biz/?doc=adr-the-nightly-function-on-the-admin-tab): the card the network log's pass is the eighth section of.

## In plain words

The cold side of the job hunt already follows up on its own. The warm side had nothing: people Steve had actually talked to, an interviewer, a recruiter who engaged, a former colleague who answered, went quiet because nothing kept track of them, and referrals come from exactly those people. From these versions the Function keeps the list. Every morning it reads the mailbox for threads where a person wrote and Steve answered, and keeps one row a person. Three weeks after the last word either way, on a Monday, a short follow-up is waiting in Gmail, in the same thread, for Steve to send or delete.

## Context

Steve's ask, 11/4: log every real conversation and have a follow-up draft ready three weeks later, email and LinkedIn both, on the Function that already reads his mail. The rulings it obeys: nothing sends, a draft waits for his yes; the model writes rows and code writes drafts; silence after a follow-up is the answer; Calendar is not read, an invite is read as the email it arrives as; the campaign owns its recipients until they reply; one database.

## Decision

1. **Who is a conversation is a rule with named cases, in the Domain.** `HumanSenderRule` (not a machine, not Steve, not a former employer's work address), `TwoWayRule` (a person and Steve both wrote, or a person sent an invite), `WarmContactRule` (a company domain, or a personal address the tracker or the responders list already knows), `PersonKey` and `MatchKey` (one row a person, one person across channels by name and company), `StopRule` (the campaign's stop phrases, and a bounce), `DueRule` (Open, a TopicLine, 21 to 45 days since the last exchange, at most fifteen a Monday, older than 45 closes stale), `QuietRule` (a sent follow-up met by 21 days of silence is Quiet; a deleted draft is declined). Each held by NetworkLogRulesTests and NetworkLogFollowUpTests.
2. **One table in its own schema.** `network.Conversations` and `network.Passes` in the site's database, beside campaign, card and jobhunt, written by the Function alone through its managed identity; `card.Network` is the card's counts. The schema is published by a person with DDL rights, once; until it is, the pass answers `blocked: no_table`.
3. **One pass a day, in the 6 o'clock hour Central, behind `NETWORK_LOG`.** Off unless `shadow` or `on`. It reads 120 days on its first pass and two after, leaves the campaign's open threads alone, reads the 6 AM program's LinkedIn spool, imports the 4 AM lane's TopicLines and exports the rows that still need one, and writes one line to `networklog/pass.log` ending with the conversations started this ISO week. A day finished in shadow runs once more when the switch reads `on`.
4. **The model writes the TopicLine; code writes the draft.** The 4 AM lane reads `topics-needed.tsv` and writes one plain clause per person to its own run folder; `TopicLineRule` holds the clause, and no TopicLine means no draft. FOLLOWUP-v1 is a constant, changed only with Steve's yes, rendered and checked (no dash, no tell, no blank, one question, a one-line `Re:` subject) before anything is written.
5. **Shadow first, and `on` drafts only.** In shadow the Monday writes `networklog/would-drafts/<day>.md` and the draft port is the refusing one. In `on` each draft goes into the person's own thread, to the address they wrote from, under Follow-up/Ready, and is read back before the row is Drafted. The adapter has no send; NetworkSqlTests and NetworkGmailTests hold that.
6. **The card counts, and never names.** The network card under Cloud infrastructure reads `card.Network`: conversations started each week for five weeks, the rows by state, the switch, the next pass. No name, company, address, subject or code leaves the view. The alert `alert-jobhunt-network-silent` emails Steve when no pass ran in the 6 o'clock hour.

## What holds it

NetworkLogRulesTests, NetworkLogFollowUpTests, NetworkLogMergeTests, NetworkTopicTests, NetworkLogPassTests, NetworkDraftTests, NetworkSqlTests, NetworkGmailTests and NetworkSharedTests in the Function's suite; NetworkCardTests and the template's alert test beside them. Each has its row in ADR-075's table.

## Not done in these versions, and why

- **The LinkedIn half of the reading and the sending.** The 6 AM program owns the only signed-in LinkedIn on the box and lives on another lane's branch until it reaches main. The Function already ingests its spool and keeps LinkedIn rows; the program's network step and the Monday LinkedIn queue are their own version when the program is on main.
- **The optional blog line.** The only minter of tagged codes is the share's make_ref.py; a second minter in C# would be a second source of codes, so v1 drafts carry no blog line and the row keeps the two columns for it. The addendum of 1.0.4.137 adds it with the 4 AM lane as the minter.

## Addendum, 2026-11-05 (1.0.4.137): the post line, minted by the lane

The brief's optional line, ` Since then I wrote up <post title>: <tagged link>`, goes into FOLLOWUP-v1 after the TopicLine, still FOLLOWUP-v1, because with no post named it renders word for word as before. Steve's call D7, option (b), is the yes for this one line.

1. **The lane picks the post and mints the code; the Function mints nothing.** `topics-needed.tsv` gains `Needs`, `DueOn` and `LastOutbound`: a row is listed for its TopicLine as before, and also from three days before it is due until it goes stale when it is an Open email row with no post named (`BlogLine.Needed`). For such a row the 4 AM lane picks a post from `GET /api/blog/posts` published after `LastOutbound`, mints its code with `make_ref.py --channel Email --lane netlog`, and writes `PersonKey`, the clause (empty when it already has one), the slug and the code. No post newer than Steve's last message means no slug and no code.
2. **The import stores a post once.** `TopicImport` holds the slug to the feed's shape and the code to make_ref's six characters (`BlogLine.Fails`), stores the first that pass in `BlogSlug` and `RefCode`, and refuses the rest into `topics-refused.log`. A stored post is not replaced: its code is already in the link-token spool under that person.
3. **The line is checked at the draft, not trusted from the row.** A Monday with a due row naming a post reads the feed once (`BlogFeed`); the line goes in only when the post is on the feed and went out after Steve's last message (`BlogLine.For`). The link is the post's address with the row's code, an anchor in the HTML part. Its `?` is not counted as the draft's question. A draft whose line fails a check goes out without the line, and a feed that does not answer names no post.
4. **The pass line** gains `posts=<n>` (stored this pass) and `named=<n>` (drafts naming one).
5. **The card's names are links, from the API.** The network card's header named `sqldb-theyard-ss-basic` with no link, and so did the campaign card's: both found the database's resource id but passed no tenant, and a portal URL needs one. Both now take the tenant the reader's token was issued in (`AzureMetrics.TenantAsync`), the same tenant-qualified shape `PlanView.Portal` gives every other card. The network card also names `func-jobhunt-ss`, the Function that runs the pass, linked the same way, and its readout no longer repeats the database's name as plain text. The page renders whatever href the API sends; it builds none.

Held by NetworkBlogLineTests; its row is in ADR-075's table.

## Addendum, 2026-11-07 (1.0.4.148): the card reads the pass, and the alert counts a failure

Until this version the card's first row, "Is it logging?", took its tone from the switch alone: shadow or on read good, whatever the pass came to. And the silent-pass alert counted invocations, so a pass that ran and threw counted as a pass. Both stopped being true on the two mornings this caught. The 6 AM passes of Friday 6 November and Saturday 7 November both came to `failed, SqlException` ("threw before it counted anything", 6.8 s and 6.3 s in the run history), the card said good both days, and the alert stayed quiet.

What was missing was a grant. The 5 November publish made `network.Conversations`, `network.Passes` and `card.Network` and granted the Function's user nothing on the `network` schema, so its first read, `PassAsync`, was refused. The laptop file `Grant-NetworkSchema.vbs` proves the refusal as that user, grants SELECT, INSERT, UPDATE and DELETE on the schema, reads the four rows back from `sys.database_permissions` and proves the read again, all in its own log.

1. **The row reads the newest pass.** The card's hourly read also keeps the newest pass the run history lists (the same Application Insights rows `/api/admin/cloud/nightly/runs/NetworkLogPass` answers) as the hour's facts, the way the nightly card keeps its runs. A failed pass reads alarm, "the last pass failed", with what it came to. A blocked or late pass reads warning in its own words. No pass for 25 hours while the switch is on or shadow reads warning, "no pass since" its day and time. A pass that came through reads good with this week's count. A history that was not read reads info and says why. The switch off reads "Off" as before.
2. **The alert trips on either.** The query counts the invocations and the failed ones over the three hours before the 13:00 UTC hour, and fires when there were none or any failed: "The network log did not run, or failed, at 6 AM Central".
3. **A new schema ships with its grant.** A schema added to the site's database is published with its GRANT to the Function's user in the same file, and the grant is read back as that user before the lane closes.
4. **Gmail's slow-down is waited out, and a refusal names itself.** With the grant in place the file's own pass at 08:00 Central on 7 November got past the database and failed 20 seconds in, on a 403 from Gmail's `users.threads.get` (`HttpRequestException`, GmailThreads.cs). The first pass reads a backfill four threads at a time, and Gmail answers a user over its per-second quota with a 403 whose reason is `rateLimitExceeded` or `userRateLimitExceeded`, which the reader did not retry. It now waits that 403 out like a 429 or a server error, five times with a doubling wait, and any answer it gives up on fails with Gmail's own reason word in the message, so the next failure says whether it was the quota or a refusal.

Held by NetworkPassTests, NetworkGmailTests and TemplateTests; their rows are in ADR-075's table.

## Where it sits

A feature slice in each of the Function's rings and a card in the site's Admin module, with the shared concerns the campaign now reads from too. Separation of duties is the principle: the model reads and summarises, code decides and writes, a person sends. It cost a schema, an hourly timer and one sign-in. What would change it: Steve saying `send` for email, which would be a new record, not an addendum.

## Files

- [`functions/JobHunt.Domain/Features/NetworkLog/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Domain/Features/NetworkLog) - the rules
- [`functions/JobHunt.Application/Features/NetworkLog/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Application/Features/NetworkLog) - the pass, the topic import and the Monday drafts
- [`functions/JobHunt.Infrastructure/Features/NetworkLog/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Infrastructure/Features/NetworkLog) - the SQL store and the Gmail reader and draft writer
- [`functions/jobhunt/NetworkLogTimer.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt/NetworkLogTimer.cs) - the timer
- [`functions/JobHunt.Database/Tables/Conversations.sql`](https://github.com/SteveStout/TheYard/blob/main/functions/JobHunt.Database/Tables/Conversations.sql) - the table

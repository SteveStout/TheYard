# ADR: The campaign sends, the model writes rows

Status: accepted in part, 2026-10-24, at 1.0.4.58: the rules (Decision 1). Filled as each rung of the campaign ships: the database, the renderer and the shadow send, the inbox side, the lead run, the card, the shadow week and the switch to on. Nothing sends while this record says shadow.

## Reading list

1. [ADR: One host per process, the rings under them](https://theyard.stevenstout.biz/?doc=adr-one-host-per-process-the-rings-under-them): the rings these rules live in.
2. [ADR: The tracker merger runs in Azure](https://theyard.stevenstout.biz/?doc=adr-the-tracker-merger-runs-in-azure): the Function the campaign joins, and the inbox pass its bounces and replies will ride on.
3. [ADR: Every Claude lane reports its usage](https://theyard.stevenstout.biz/?doc=adr-every-claude-lane-reports-its-usage): how the lead run's model cost reaches the usage card.

## In plain words

Steve asked for an email campaign to hiring managers that runs on its own. A model is good at reading a posting and finding the one plain thing a company does, and bad at being the same twice. So the work is split down the middle. A model writes rows: who the company is, who runs its engineering team, where the address was published, one clause saying what the company makes. Code does everything that touches the send: it checks every rule, fills the approved template word for word, picks the order, holds the count, and sends. No model is in the send path, and a template changes only when Steve says yes.

This first version is the rules, written as code with no database and no mail. Each one is a test that fails the build.

## Context

- Steve, 2026-10-24 12:22 CDT: "the emails should run from Azure functions and AI is only used to generate the leads", and "It sends automatically at 8:00 am". 12:30: seven days a week. 12:58, after the review: "Okay do all of that, but I'm not sure about the 3rd party for email", so there is no address finder: public sources with their URL only, no account, no key.
- The review of the plan (J:\mentor\REVIEW-EMAIL-CAMPAIGN-2026-10-24.md, findings F1 to F19 and its section 8) found what the plan missed. The do-not-apply list carries none of his five former employers (F1). A "per domain" 30-day rule misses a company under its parent and a company known only by name (F10). Template A said "I applied today" on a send the next morning (F8). Re-ranking every lead with a model every run reorders the list every run (F9). A {WhatTheyDo} written by a model can carry its voice into his email (F15). The live-thread rule had no shape in code (F16).
- The Function's Gmail token already holds gmail.modify, which can send (F4). The only things between a defect and his own inbox are the switch and the cap in code, so both are constants a test reads.

## Decision

1. **The rules are code in `functions/JobHunt.Domain/Features/Campaign/`, with no I/O, each held by a test that names the review's case.**
   - `FormerEmployers`: Storee Construction, Conway Investment Research (also Conway Investment Solutions), Cass Information Systems, Hunter Engineering and the State of Missouri, with their variants and domains (storee.com, conwayis.com, cassinfo.com, hunter.com, mo.gov). Matched by name the way Rule 50 matches names, or by domain key. No date: a former employer stays out for good.
   - `CompanyKey`: the registrable domain, public-suffix aware for the suffixes this search meets (mail.acme.co.uk is acme.co.uk, dor.mo.gov is mo.gov). An unknown suffix over-joins, which over-holds.
   - `ThirtyDayRule` and `CompanyGate`: one email a company in a rolling 30 days, follow-ups counted, matched by key, by parent key, or by name. A send on day 0 holds through day 30; day 31 is the first allowed. The gate reads a former employer first and names it, then the do-not-apply list, then the 30 days.
   - `LiveThreadRule`: a tracker row for the company in the last 30 days that is a person's thread (a LinkedIn message, an email, a call, a rejection) holds the lead with its Count. An application row, the ATS sweep's receipt and an unanswered connection request do not hold: the first two are the application itself, and the third is the LinkedIn order's input.
   - `Tells` and `WhatTheyDoRule`: S1_WORDS and S1_PHRASES of ai_tells_five_signs.py in one constant, matched against a checked-in copy of the Python file by count and content. The clause opens with a verb from a list of fifteen, runs 20 to 110 characters, ends without punctuation, has no comma clause longer than the phrase before it, no dash, quote marks, braces or address, never names the company, and carries no tell. A failing clause blocks its lead with the rule named.
   - `CampaignRules`: `DailyCap` 10, `Ramp` 3, 5, 10 on the first three days of `on`, five seconds between sends.
   - `RecipientDomainRule`: business domains only (no gmail.com, yahoo.com, outlook.com, hotmail.com, icloud.com and the rest of the list), and MX records checked and found.
   - `Strength`: a formula over stored facts (remote stance, .NET evidence rows, a posted role, the band's top against $155,000 and $170,000, the ghost score, days since first seen, a .NET posting in the last 30 days), the model's step held to plus or minus one, and a 16-digit hash of the inputs so the model is asked again only when a fact moves. A ghost score of 3 or more takes 25 points, more than the rest of the formula spans.
   - `AppliedWhen`: "today", "yesterday", "on Friday", "last Saturday" from AppliedAt and the send time in Central; Track A never past seven days.
   - `ProofKey`: reliability, platform, leadership and ai, each a clause built from a line of the 2026-10-24 resume. No template has a {Proof} line yet; adding one is a template change that waits for Steve.
   - `LinkedInOrder` and `SendOrder`: an accepted invite first, then an invite out more than three days, then the rest; then Strength, then the oldest lead.
   - `TemplateFill`: every {Variable} from the row, a required blank fills nothing and names the blank, and an optional line goes whole with the gap under it when any of its values is blank.

## What holds it

`CampaignRulesTests` (former employers by name and domain, the company key, the 30-day cases F10 names, the gate's order, the live thread over the tracker's real row shapes with every name removed) and `CampaignTextTests` (the tells against the Python file, the WhatTheyDo cases F15 names, the cap and ramp, the recipient domains, the Strength formula and hash, the applied word across the November 1 clock change, the proof clauses, the send order, the optional line and the named blank).

## Not done in this version, and why

Each of these is its own rung and version, in this order: the Basic database on the existing server with the Function's identity as a database user; the renderer and the shadow send path; bounces, replies and stop phrases in the inbox pass; the lead run; the campaign card, its rail row and its alert; seven days of shadow; the switch to on.

## Where it sits

A new slice, `Campaign`, in the job hunt's Function rings, beside `Leads`, whose Rule 50 name match it reuses rather than copies. Separation of concerns is the principle: the model's judgement stays in rows a person can read and a rule can refuse, and the send is code a test can run. It cost one folder of rules and a copied Python file in the tests. What would change it: Steve approving a finder, a template with a {Proof} line, or a different cap, each a change to one constant or one adapter with its test.

## Files

- [`functions/JobHunt.Domain/Features/Campaign/`](https://github.com/SteveStout/TheYard/tree/main/functions/JobHunt.Domain/Features/Campaign) - the rules
- [`functions/jobhunt.Tests/CampaignRulesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt.Tests/CampaignRulesTests.cs) - the company rules, held
- [`functions/jobhunt.Tests/CampaignTextTests.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt.Tests/CampaignTextTests.cs) - the text and order rules, held
- [`functions/jobhunt.Tests/Fixtures/ai_tells_five_signs.py`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt.Tests/Fixtures/ai_tells_five_signs.py) - the copy the tells are held to

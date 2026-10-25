# INC-001: the share that billed for looking

Status: closed, 2026-10-24, by the cost lane (cost1024) in versions 1.0.4.49, 1.0.4.50 and 1.0.4.52 and the changes it made to the share and the laptop the same morning.

## In plain words

For four days in October the job hunt's file share on Azure cost more each day than the whole site had in a week, and the Admin tab never said so. A share of that kind is cheap to fill and expensive to look at: Azure bills it for every look, and the laptop that mounted it kept looking. Steve found it from his phone. This page is what happened, what it cost, why the cards missed it, and what catches it now.

## Summary

- **What:** a pay-per-transaction Azure Files share (`stjobhuntss`, Standard LRS, the Hot tier) was walked by the laptop that mounted it: 50 million transactions in four days, 88 % of them file-property reads.
- **How long:** 20 to 23 October, four billed days, until the two-way sync was unregistered in the 17:00 hour on the 23rd; seen on the 24th at 08:11.
- **What it cost:** $49.29 over the four days, $44.01 above the $1.32 the subscription cost on 19 October.

## Timeline

Central time.

| When | What happened | Source |
| --- | --- | --- |
| 20 Oct 11:11 | The share is created on the Hot tier; the two-way sync task and the rclone mount, which listed every folder again after 30 seconds, come up against it | rung2b4.txt (the tier "changed 2026-10-20T16:11:16Z"); COST-DONE-2026-10-24.md, rung 2 |
| 20 to 23 Oct | The subscription's day rows read $3.07, $7.82, $13.41 and $24.99 against $1.32 on the 19th | /api/admin/costs, the day rows, read 2026-10-25 12:1x |
| 22 Oct | The share's busiest day: 20,282,893 transactions, 18,413,355 of them file-property reads | COST-DONE-2026-10-24.md, transactions by day |
| 23 Oct 16:00 to 17:00 | 'JobHunt Share Sync' is unregistered when the laptop's copy of the folder is retired; the share's hour falls from 890,280 transactions to 37,080, then about 7,000 | DONE-RETIRE-C-2026-10-23.md, step 4; COST-DONE-2026-10-24.md, by the hour |
| 24 Oct 08:11 | Steve asks "why is it $7" after a projection of $735 a month on his phone | failure-log.md, line 6881; PROMPT-THEYARD-COST-2026-10-24.md, line 5 |
| 24 Oct 08:2x | The review lane, which had read the forecast and not the day rows, writes the cost prompt | failure-log.md, line 6881 |
| 24 Oct 08:29 | Steve says yes to the tier change | PROMPT-THEYARD-COST-2026-10-24.md, rung 2b |
| 24 Oct 08:30 | The cost lane opens | COST-DONE-2026-10-24.md, line 3 |
| 24 Oct 08:51 | The mount keeps its listings an hour; the editor stops watching the drive | COST-DONE-2026-10-24.md, rung 2 |
| 24 Oct 09:33 | 1.0.4.49 live: the forecast reads $84.94 above $70.20 spent, where it read $60.58 | COST-DONE-2026-10-24.md, rung 3 |
| 24 Oct 09:42 | The share moves from Hot to Transaction optimized | rung2b4.txt, 09:42:48 |
| 24 Oct 09:43 to 09:44 | The transactions alert at 20,000 an hour and the $50 monthly budget are deployed | rung2b4.txt, 09:43:53 and 09:44:42 |
| 24 Oct 10:32 | 1.0.4.50 deployed to the Function, carrying 1.0.4.49's change: the merger stops asking for folders it has | COST-DONE-2026-10-24.md, rung 8, point 7 |
| 24 Oct | 1.0.4.52 live: the share and blob cards on the Admin tab | COST-DONE-2026-10-24.md, the ships |
| 24 Oct | The subscription's day row is $2.00 | /api/admin/costs, read 2026-10-25 12:1x |

## Impact

$49.29 over the four days, $44.01 of it above the baseline, and the share at $45.01 of the subscription's 30 days (59 %). No visitor saw anything: the site never slowed, because the share serves the job hunt, not the site. The impact that matters is how it was found. The cost reached Steve through a projection on his phone, not through the Admin tab, on a site whose point is that the Admin tab sees first.

## Root cause

Azure Files on the Standard tiers bills every transaction, and on the Hot tier the reads and listings cost far more than the gigabytes behind them: on 23 October the share's meters read $12.68 for read operations, $8.60 for list operations and $2.41 for write operations, and everything else under a cent. The laptop listed the share on clocks of its own. The two-way sync task walked the whole folder, and the mount's 30-second listing cache meant that every folder anything on the laptop looked at was listed again half a minute later. 88 % of the transactions were file-property reads signed with the laptop's own access signature. The gigabytes were never the bill.

## Detection gap

Three things should have caught it, and none did.

1. **The forecast lagged the spend.** At 1.0.4.48, `api/TheYard.Api/Shared/CostReadings/CostReport.cs` line 90 summed Azure's forecast rows, which run two days behind and are anchored on them, so the month's forecast read $60.58 while the month to date was already $70.20.
2. **The review read the summary, not the rows.** The day rows showed the climb on every read of /api/admin/costs; the review lane read the forecast line (failure-log.md, line 6881).
3. **No alert watched the meter.** The one alert in place watched the Function for silence. Nothing watched the share's transactions or the subscription's spend.

## What changed

| Change | Version | What shows it now |
| --- | --- | --- |
| The forecast starts from the spend and adds the days ahead; the cost card shows the month at yesterday's rate beside it | 1.0.4.49 | The first screen's "Azure this month" and the cost card |
| The Function's merger remembers a folder it has made sure of for an hour | 1.0.4.49 | The share card's transactions; CreateDirectory fell from about 2,170 an hour to 321 |
| The share's tier, the transactions alert and the $50 budget written into the templates (they were set live at 09:42 to 09:44) | 1.0.4.50 | The alert `alert-jobhunt-share-transactions` and the budget `budget-subscription-monthly` |
| The share and blob cards: transactions this hour against the alert's own line, who reads the share, cost by day | 1.0.4.52 | Admin tab, Cloud infrastructure, the share card |
| The mount keeps its listings an hour and the editor ignores the drive | no version | The share card's hourly transactions |
| A review reads the day rows before any cost summary and flags a day over twice the median of the fourteen before it | no version | failure-log.md, line 6881, the CHECK every review runs |

## What stays true

- A pay-per-transaction share costs a few dollars a month when nothing walks it.
- What makes a cost spike visible within the hour is an alert on the meter and a read of the day rows. A better forecast does not.
- A resource whose cost depends on behaviour gets a card before it gets a saving.

## Follow-ups

The state of each is read from the card or the repository named.

| Follow-up | State | Where it is read |
| --- | --- | --- |
| The share and blob cards | shipped in 1.0.4.52 | Admin tab, Cloud infrastructure |
| The Function's own state files off the share and onto blob storage | open | ADR: One host per process, the rings under them, "Not done" (stage B) |
| The Foreman drafting an incident's timeline from the cards' minutes | open | ADR: A red that lasts an hour gets a postmortem |

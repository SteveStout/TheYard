# Incidents

Every time a grade on the Admin tab read red for an hour or more, and every day that cost more than twice the median of the seven before it, gets a postmortem here ([ADR: A red that lasts an hour gets a postmortem](https://theyard.stevenstout.biz/?doc=adr-a-red-that-lasts-an-hour-gets-a-postmortem)). Each one keeps the same eight sections in the same order, so they can be read side by side, and names the versions that fixed it.

## In plain words

Things break on any site that runs long enough. This page lists every time something on TheYard broke badly enough to matter, with a link to what happened, what it cost, and what changed so it would not happen the same way twice.

## The incidents

| Id | Title | Dates | Hours red | The card that went red | Fixed in |
| --- | --- | --- | --- | --- | --- |
| [INC-001](https://theyard.stevenstout.biz/?doc=inc-001-the-share-that-billed-for-looking) | The share that billed for looking | 20 to 24 October 2026 | none: no card went red, which is the finding | none; the cost card's day rows showed it and nothing read them | 1.0.4.49, 1.0.4.50, 1.0.4.52 |

## How one is written

Summary, Timeline, Impact, Root cause, Detection gap, What changed, What stays true and Follow-ups. Every Timeline row names its source, every version under What changed is a line of the changelog, and every follow-up says its state; `IncidentShapeTests` holds all three.

# ADR: Edge deploy economics

Status: accepted, 2026-09-01, the evening the meter was read.

## In plain words

The site's secure entry point (the Netlify edge) runs on a free plan with a monthly credit allowance, and on its first full day, app changes that never touched the edge used over half of it by rebuilding the edge. One line of settings (netlify.toml) now rebuilds the edge only when its own files change.

What that is worth: a developer learns to read a platform's meter in the first week, and the organization keeps the edge free with no paid plan.

## Context

The HTTPS edge runs on Netlify's free plan, which allots 300 credits a
month. On the first full day of the custom domain the billing page showed
168.2 credits gone. The breakdown was the lesson: 11 production deploys had
consumed 165 credits at 15 each, while actually serving the site all month
cost 3.3 (1,835 requests plus bandwidth). Netlify's default is to rebuild
the edge on every push to the connected repository, and this repository
pushed ten times that day shipping application changes that never touched
the three files the edge is made of.

## Decision

Scope edge rebuilds to edge changes with one line in netlify.toml:

    ignore = "git diff --quiet $CACHED_COMMIT_REF $COMMIT_REF -- edge/ netlify.toml"

When the diff is empty the build exits before it starts and no deploy
credit is spent. No paid plan. The math never justified one: the burn was
a default behavior, not real usage, and this edge layer retires at the
planned registrar transfer around the end of October anyway.

## Consequences

- Application pushes cost zero Netlify credits. Edge changes still deploy
  themselves, which is the behavior that was always wanted.
- Build hooks would bypass the rule; this project uses none. A deliberate
  edge redeploy can be forced from the Netlify UI.
- The known trap: on a cold build cache (a cache clear, or Netlify's first
  build after a config change) the two commit references can be equal, the
  diff comes back empty, and a REAL edge change gets silently skipped. The
  routine after any edge change is therefore: push, then glance at the
  deploys page and confirm a build actually ran; force one from the UI if
  it did not.
- The general lesson, worth keeping: a managed platform bills on its
  defaults, not on your intent. Read the meter in the first week, find
  which line item is really moving, and fix the configuration before
  reaching for a credit card.

## Addendum, 2026-09-09: a second origin behind the one edge

The edge fronts two origins now. Two lines above the catch-all send
`theyard-cosmos.stevenstout.biz` to the second container group, the one whose
default store is Azure Cosmos DB, and the rest of the file is as it was (ADR:
A permanent address for the second site). The economics are the reason the
second name is an alias on this site and not a site of its own: a second site
would have had a second deploy meter, and every edge change would have cost
15 credits twice. This change cost one edge deploy, and the routine above
applied to it unchanged: push, read the deploys page, force a build if the
cold-cache trap skipped it. The record it belongs to carries the meter's
reading before and after.

## Addendum, 2026-10-05 (1.0.3.76): what it would cost to serve the script from the edge

The edge proxies every request to Azure, and a proxied file is cached per node, so a quiet site misses on its hashed script most of the time (ADR: Cache headers, the addendum of 5 October). The way to end that on Netlify is to publish the built files to the edge itself, which makes every app version a Netlify production deploy. Priced against Netlify's pricing page on 5 October:

| | Credits a month | Cost a month |
| --- | --- | --- |
| Today: only edge changes deploy | about 0 | $0 on the free plan, 300 credits |
| September's pace, 252 versions at 15 credits each | 3,780 | about $30 on Pro (3,000 credits for $20, then $10 for 1,500 more) |
| October's pace so far, about 115 versions | 1,725 | about $19 on Personal (1,000 credits for $9, then $5 for each 500 more) |

What it buys is about a quarter of a second on a first visit's script, once per visitor per version. The recommendation is to wait: the domain's registrar lock ends around 30 October, the edge was always going to move to Cloudflare then, and Cloudflare's free plan caches the hashed files with Smart Tiered Cache, which serves a quiet site's files from one upper tier instead of a cold cache per node. Bandwidth and requests already cost credits on today's plan and are not changed by this.

## Where it sits

No ring is involved, because this is a hosting cost decision about when the Netlify edge rebuilds, settled by one ignore line in netlify.toml.

## Files

- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml): the ignore rule that stops app-only pushes from
  redeploying the edge, shown live below.
- [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the whole edge, seven lines, shown live below:
  the bare and www names redirect, the second site's name proxies to the
  second origin, everything else proxies to the first.
- [`edge/README.md`](https://github.com/SteveStout/TheYard/blob/main/edge/README.md): how the edge project is wired to the repo.
- [`docs/hosting/HOSTING.md`](https://github.com/SteveStout/TheYard/blob/main/docs/hosting/HOSTING.md): where the edge sits in the chain.

```live path=netlify.toml region=ignore-rule
```

```live path=edge/_redirects region=rules
```

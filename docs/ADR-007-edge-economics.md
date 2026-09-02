# ADR: Edge deploy economics

Status: accepted, 2026-09-01, the evening the meter was read.

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

## Files

- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml): the ignore rule that stops app-only pushes from
  redeploying the edge, shown live below.
- [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the whole edge, five lines, shown live below:
  the bare and www names redirect, everything else proxies to the origin.
- [`edge/README.md`](https://github.com/SteveStout/TheYard/blob/main/edge/README.md): how the edge project is wired to the repo.
- [`docs/HOSTING.md`](https://github.com/SteveStout/TheYard/blob/main/docs/HOSTING.md): where the edge sits in the chain.

```live path=netlify.toml region=ignore-rule
```

```live path=edge/_redirects region=rules
```

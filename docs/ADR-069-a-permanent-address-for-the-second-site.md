# ADR: A permanent address for the second site

Status: accepted, 2026-09-09. The second container, the one whose default
store is Azure Cosmos DB, gets a name under the site's own domain,
https://theyard-cosmos.stevenstout.biz, with HTTPS from the same certificate
authority the live site uses, through the same edge. Parent: ADR: One
container, both stores.

## Context

Steve's words on the morning of 2026-09-09, after his first look at the two
sites: "we need a permanent url for the cosmos db". What the second site had
was the container group's Azure hostname on port 8080, plain HTTP:
`http://theyard-cosmos-ss-zmnetj67bn5h2.westus2.azurecontainer.io:8080`.

That hostname is already permanent in the narrow sense. The label is fixed in
`infra/aci-theyard-cosmos.yaml` (`dnsNameLabel`), so a roll of the group keeps
it, and the Deploy Cosmos workflow rolls from that file. What it lacks is
everything the live site has: his domain, so the address reads as his rather
than as Azure's; a certificate, so the browser shows a padlock rather than
"Not secure"; and a port nobody has to type. It also decides a second thing.
The Store bar links each site to the other (ADR: One container, both stores,
addendum), and the next change to the bar makes those links the toggle
itself; a link from an HTTPS page to a plain HTTP address is a link Chrome
marks, so both sites have to be on HTTPS before the toggle can honestly point
at either.

The live site's address works the way it does because of two walls recorded
in ADR: Deployment strategy: the free trial refuses Azure Front Door, and the
registrar (Wix) refuses nameserver changes until the domain can transfer,
around the end of October 2026. So HTTPS is terminated at a free Netlify edge,
three files in this repository, with one CNAME at Wix pointing the name at it.
Any answer for the second site has to live inside the same two walls.

## The options, priced

| Option | What it takes | What it costs | Verdict |
| --- | --- | --- | --- |
| A. A second hostname on the existing Netlify edge | One domain alias on the Netlify site, one CNAME at Wix, two lines in `edge/_redirects`, one edge deploy | 15 credits of the month's 300, once; $0 | This one |
| B. A second Netlify site for the second origin | A new site, its own link to the repository, its own deploys and its own credits, a second `netlify.toml` | 15 credits per edge change, on two meters; twice the surface to keep | No |
| C. A path on the live site, `/cosmos/*` proxied to the second origin | A base path through the whole application; every `/api` and `/assets` address is absolute today | Days of work for a worse address | No |
| D. Front Door, App Service, or Cloudflare in front | Refused by the trial, refused by the registrar until late October | Not available | No |

Option A is the live site's own design applied twice. The edge already
answers for three names and forwards two of them to the third; a fourth name
that proxies somewhere else is the same mechanism with a different target.

## Decision

**The name is `theyard-cosmos.stevenstout.biz`.** One label, so any DNS form
accepts it, and it is the container group's own name with the suffix dropped,
so the address and the Azure resource say the same thing.

**Three parts, and only one of them is in this repository.**

The first is two lines in `edge/_redirects`, above the catch-all. Netlify
reads the file top to bottom and takes the first rule that matches, and the
catch-all matches everything, so a rule for the new name below it would never
be reached. The `from` column carries the whole address rather than a path,
which Netlify's redirect documentation calls a domain-level redirect: it works
for any domain assigned to the site, including a domain alias, and it works
with a proxy (`200`) and the force flag as well as with a redirect. The file
already relies on the same feature for the bare and www names. The second line
sends the plain-HTTP spelling to HTTPS, the way the four lines above it do for
the other names, because the file does not assume the platform's own HTTPS
forcing.

```live path=edge/_redirects region=rules
```

The second part is a domain alias on the Netlify site `theyard-edge`, which
is what makes the domain-level rule eligible to match. There is no Netlify
token on the machine that ships (checked by name, never printed, and the
Netlify CLI is not installed there either), so the alias is a click in the
Netlify UI: Domain management, Add domain alias. Netlify then issues the
Let's Encrypt certificate for the new name on its own once the name resolves,
as it did for the bare and www names on 2026-09-01 (ADR: Deployment
strategy, the completion note). Whether it did, and what the certificate
says, is read rather than assumed, and the reading is in the addendum.

The third is a CNAME at Wix: host `theyard-cosmos`, value
`theyard-edge.netlify.app`, TTL 30 minutes, matching the record that already
exists for `theyard`. DNS is Steve's and stays his; the record was spelled out
for him so it took fifteen seconds. The CNAME goes in before the alias, for
the reason ADR: Deployment strategy learned on 2026-09-01: certificate
issuance validates every attached name against DNS, and a lookup that lands
before the record exists is cached as a miss by every resolver on the path,
Netlify's included, for as long as the zone's negative TTL says.

**The order of operations, with ADR: Edge deploy economics in mind.** The
ignore rule that stops application pushes from spending credits can, on a
cold build cache, read a real edge change as no change and skip it, so the
push of these rules is followed by a read of the Netlify deploys page, and a
skipped build is forced from the UI. The CNAME and the alias depend on
nothing in the repository and were asked for while the rules were on their
way through the gate, the CNAME first for the reason above. Until the rules
are live, a request to the new name falls through the catch-all to the first
origin; until the alias exists, it reaches nothing; so the address is not
announced until it is read. The certificate is waited for, not assumed. Then
the runner reads `https://theyard-cosmos.stevenstout.biz/api/version` and
`/api/stores` from the domain: the version proves the edge reached a
container, and `current` answering `cosmos` proves the new rule matched and
the build ran, rather than the request falling through the catch-all to the
first origin. Only then do the two container groups' `Peer:Site` settings
change, the live site's to the new address and the second site's staying
`https://theyard.stevenstout.biz`, so the bar's link on the live site never
points at a name that does not answer yet.

## What it costs

One production deploy of the edge, which on Netlify's free plan is 15 of the
month's 300 credits (ADR: Edge deploy economics has the arithmetic). The
meter is read before and after rather than estimated, and both readings go in
this record's addendum with the certificate and the version once the address
answers. Nothing on Azure changes: no new resource, no new group, no billing.
The second container group costs what it cost yesterday.

## What this is not

**Not a second edge.** One Netlify site, one certificate, one deploy meter,
two origins behind it. Option B would have doubled the surface for nothing.

**Not a path.** `/cosmos/*` on the live site would have needed a base path
through the whole application, and the address it produced would still say
which site was the real one.

**Not permanent in the way the rest of the hosting is.** The whole Netlify
edge retires at the registrar transfer around the end of October 2026 (ADR:
Deployment strategy), and this name retires with it: at Cloudflare the two
names become two records instead of one, pointing at the same two origins.
The application touches the edge in two places only, the `Peer:Site`
setting on each group and the forwarded-protocol header the cookie rule reads,
so the move is two settings and two DNS records.

## Consequences

- The second site has an address a person can type and a resume can carry,
  with a padlock. `docs/HOSTING.md`'s sentence that the second group runs
  "behind no edge and no domain" stops being true, and the page says so.
- The Store bar on the live site links to `https://theyard-cosmos.stevenstout.biz`
  once `Peer:Site` on that group changes, and the toggle that follows
  (ADR: One container, both stores, addendum) can be a link between two HTTPS
  sites.
- `edge/_redirects` is seven lines instead of five, and ADR: Edge deploy
  economics carries an addendum saying that the one edge now fronts two
  origins.
- The two old addresses keep working. The Azure hostname still answers on
  port 8080, plain HTTP, exactly as before, because nothing about the group
  changed; the domain is a second door, not a replacement.

## Files

- [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the two new lines, above the catch-all, shown live above.
- [`edge/README.md`](https://github.com/SteveStout/TheYard/blob/main/edge/README.md): what the edge answers for now.
- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml): the ignore rule that made the order of operations matter.
- [`infra/aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard-cosmos.yaml): the fixed `dnsNameLabel` the edge proxies to.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml): the live group's `Peer__Site`, which points at the new name once it answers.
- [`docs/HOSTING.md`](https://github.com/SteveStout/TheYard/blob/main/docs/HOSTING.md): the chain, with the second name in it.

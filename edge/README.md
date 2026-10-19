# Phase-1 TLS edge

This folder is the edge's rules: a Netlify site that terminates HTTPS for
theyard.stevenstout.biz and, since 1.0.0.100, for theyard-cosmos.stevenstout.biz,
a domain alias of the same site (docs/decisions/ADR-069). It forwards the bare
domain and www to the primary URL with a 301, sends a page on either name to the
rendering service as that site, and proxies everything else to the one web app
that serves both sites (docs/decisions/ADR-105). The page rules and the 301s are
in `_redirects`, which Netlify reads first; the two proxy rules are in
`../netlify.toml`, because only a rule written there can add the `X-Yard-Host`
header that tells the web app which name the visitor typed. One site, one
certificate, one origin for the API. The app itself runs only on Azure, and
theshed.stevenstout.biz does not come through here at all.

Why an external edge exists at all, and how it retires, is recorded in
docs/decisions/ADR-004 (the free trial blocks Azure Front Door; the registrar blocks
nameserver delegation). Swapping this edge for Front Door later is a DNS edit;
the public URLs never change.

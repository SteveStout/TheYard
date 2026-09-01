# Phase-1 TLS edge

This folder is the whole phase-1 edge: a Netlify site that terminates HTTPS for
theyard.stevenstout.biz and proxies every request to the Azure Container
Instances origin on port 8080. The app itself runs only on Azure.

Why an external edge exists at all, and how it retires, is recorded in
docs/ADR-004 (the free trial blocks Azure Front Door; the registrar blocks
nameserver delegation). Swapping this edge for Front Door later is a DNS edit;
the public URL never changes.

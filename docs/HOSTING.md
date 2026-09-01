# Hosting

TheYard is on the internet at https://theyard.stevenstout.biz. This page is the
parent record for how that works, written for a hiring manager or anyone
learning how a small production setup fits together. The records under it in
this menu are its children: read this page for the shape, open a child for the
full reasoning behind one decision.

## Websites and resources used

- **Azure (portal.azure.com).** Runs the app: Container Instances for the
  compute, Container Registry for the image. The only place code executes.
- **Wix (wix.com).** The domain registrar. Holds stevenstout.biz and answers
  DNS; one CNAME record points theyard at the edge.
- **Netlify (netlify.com).** The free edge. Terminates HTTPS, holds the
  certificate, and forwards every request to Azure unchanged.
- **Let's Encrypt (letsencrypt.org).** Issues the certificate at no cost;
  Netlify renews it automatically.
- **Cloudflare (cloudflare.com).** Configured and dormant; becomes the edge
  after the domain transfers registrars, around late October 2026.
- **GitHub (github.com/SteveStout/TheYard).** Holds the code, runs the test
  wall on every push, and feeds the edge deploys.

## The chain, request by request

1. **DNS.** theyard.stevenstout.biz is a CNAME record at the registrar (Wix)
   pointing at the edge. TTLs sit at 30 minutes while the setup is young so
   changes propagate fast. They get lengthened once things are boring.
2. **Edge.** Netlify's free tier terminates HTTPS and forwards every request
   unchanged. The entire edge is three files in this repository, deployed from
   GitHub on every push.
3. **Origin.** Azure Container Instances runs the Docker image in RG-THEYARD-SS
   (westus2), serving HTTP on port 8080. Azure does all the compute. The edge
   only forwards.

## The certificate

Let's Encrypt at the edge, issued and renewed automatically. Nothing was
purchased and nothing expires by surprise. The edge-to-origin hop stays plain
HTTP in phase 1 because the container has no TLS listener; the phase-2 managed
certificate closes that hop end to end.

## Why not Front Door today

The free trial refuses to create it, and that was measured rather than assumed
(child 4). The registrar also refuses nameserver changes, which rules out
Cloudflare's free tier until the domain can transfer, earliest late October
2026. The pattern survived both walls. Only the vendor is temporary.

## Phase 2

Upgrade the subscription, flip the parameters in infra/main.bicep, and point
the same subdomain at Front Door. The resume URL never changes. That permanence
is the entire point of the domain layer.

## The configurations, as they stand

Screenshots of the real settings, so nothing here is taken on faith.

**Netlify holds the certificate and the custom domain.** HTTPS enabled,
Let's Encrypt, renewing itself.

![Netlify domain management with the Let's Encrypt certificate](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/netlify-domain-cert.jpg)

**Netlify deploys the edge straight from GitHub.** No build command; it
publishes the repo's edge folder on every push.

![Netlify build configuration linked to the GitHub repository](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/netlify-build-config.jpg)

**Wix answers DNS.** One CNAME sends theyard traffic to the edge; TTLs are
short while the setup is young.

![Wix DNS records with the theyard CNAME](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/wix-dns-records.jpg)

**Cloudflare waits in the wings.** A fully configured zone sits dormant
(pending banner and all) until the domain can transfer registrars; activating
it is a nameserver change away.

![The staged Cloudflare DNS records](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/cloudflare-dns-staged.jpg)

![The staged Cloudflare redirect rule](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/cloudflare-rules-staged.jpg)

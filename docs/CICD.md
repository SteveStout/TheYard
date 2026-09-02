# CI/CD

How code gets from a laptop to https://theyard.stevenstout.biz, written for a
hiring manager or anyone learning how small projects ship safely.

## What runs today

**Continuous integration.** Every push to GitHub runs the full test wall: the
API suite, the frontend suite, and a browser suite that clicks through the
running app the way a person would. A change that fails any of them does not
ship.

![Twenty green CI runs on GitHub Actions](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/github-ci-green.jpg)

**Continuous deployment.** When CI finishes green on main, a second workflow
named Deploy builds the container image, pushes it to Azure Container
Registry, and rolls it onto Azure Container Instances, with no human in the
loop. The workflow signs in to Azure with a token GitHub mints for this one
repository's main branch, so no password or key is stored anywhere. The
version in the page footer is stamped by that build. The full record is
ADR: The deploy pipeline, below this entry in the menu.

![The Deploy workflow's runs on GitHub Actions, one green run per version on the live site](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/github-deploy-runs.jpg)

One run, step by step: compute the version, sign in to Azure with the
minted token, build and push the image, roll the container group, verify
the origin and the domain. Every version on the site has a page like this.

![One Deploy run's steps: compute the version, sign in to Azure, build and push, roll the container group, verify](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/github-deploy-steps.jpg)

**The edge deploys itself.** The HTTPS front door of this site (see the
Hosting menu) is three files in this repository. Netlify watches the repo and
redeploys the edge only when those files change, with no human in the loop.

## The design, before the build

Deployment automation landed 2026-09-02 as a second GitHub Actions
workflow, and this is the design it was built from, written the day before
on purpose: the plan is a deliverable too.

- A separate Deploy workflow fires only when CI completes green on
  main, and checks out the exact commit CI tested.
- It builds the image with the two footer build arguments, so the page
  keeps reporting exactly what is running (see ADR: Version in the
  footer under Best Practices).
- It pushes to the registry and rolls the container group from a
  template checked into infra/, the same file every deploy uses.
- Authentication is OIDC federated credentials: Azure trusts a token
  GitHub mints for this one repo's main branch. No password, key, or
  secret exists anywhere in the pipeline, so there is nothing to rotate
  and nothing to leak.
- Roles are scoped to least privilege: push to this one registry,
  manage container groups in this one resource group, assign this one
  identity.
- Displayed versions continue as 1.0.0.(11 + deploy run number), 11
  being the last manual image. Numbers may skip when a red CI run
  consumes one; a gap is a change that never shipped.
- The manual scripted pipeline retires to fallback duty and stays
  documented: it is also the rollback.

The identity, as Azure lists it. The federated credential trusts one issuer
for one subject (the owner and repository ids inside it mean a renamed or
re-created repo cannot inherit the trust), and the three role assignments
are the whole of what the pipeline can do:

```text
name:      github-main
issuer:    https://token.actions.githubusercontent.com
subject:   repo:SteveStout@317307255/TheYard@1352398185:ref:refs/heads/main
audiences: api://AzureADTokenExchange

Role                                        Scope
------------------------------------------  ------------------------------------------------
AcrPush                                     .../registries/crtheyardsszmnetj67bn5h2
Azure Container Instances Contributor Role  .../resourceGroups/RG-THEYARD-SS
Managed Identity Operator                   .../userAssignedIdentities/id-theyard-ss
```

## What comes next

Deployment slots for a zero-downtime roll, which the production design under
Hosting already covers with App Service, and the registrar transfer around
the end of October that retires the Netlify edge. Neither changes the
pipeline: the image and the workflow stay the same, only the target moves.

## Why it is built in this order

Tests first, automation second. A pipeline that ships untested code is a
faster way to break production. The test wall existed before this site had a
URL, and every deploy so far has been gated on it.

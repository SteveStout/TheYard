# CI/CD

How code gets from a laptop to https://theyard.stevenstout.biz, written for a
reviewer or anyone learning how small projects ship safely.

## In plain words

This page shows how a code change gets from a laptop to the live site. Every test runs before a version can ship (the ship's gate), and a push to the main branch then builds the API's container image beside the rendering service's and rolls each onto its web app with no human step.

What that is worth: a developer can merge knowing a failing test stops the change before it reaches anyone, and the organization ships often with no Azure password or key stored anywhere to leak.

## What runs today

**Continuous integration.** Every test runs once per version, in the ship's
gate: the ship pushes the version to a `gate/<version>` branch, `gate.yml` runs
the API suite on both stores, the frontend suite, and a browser suite that
clicks through the running app the way a person would, on a GitHub runner, and
`main` moves only when all of it is green (ADR-098). A change that fails any of
them never reaches `main`. Every result, test by test, ships with the version and is on
the Admin tab. The same suites then run again on GitHub's runners for every
push to main, where anybody can read the result without a login, and for a
pull request from anybody else, which has had no gate. Every one of those jobs
installs .NET through one step, `.github/actions/dotnet`, which tries the
download twice and then builds with the runner's own .NET 10 SDK, so a failed
download cannot turn a good version red (ADR-068, the addendum of 1.0.4.72).

**A ship is a push of `gate/<version>`.** From 1.0.4.101 no machine has to stay
on for a version to ship (ADR-098, the addendum of 1.0.4.101). When the gate
and The Shed's suites are both green, the same run promotes itself: its
promote job runs `scripts/promote.sh`, which checks the version and the locked
resume, writes the results file and the README's figures from the run's
reports, folds them into the commit and fast-forwards `main`, never by force.
The run then calls Deploy and Deploy Render on that commit, starts the edge's
move, reads both names after the roll, and starts CI on `main` and reads it to
its end. A version that changes a workflow is the one exception: GitHub never
lets the run's own token write a workflow, so a person runs the same script
and pushes `main`.

Before the push, whoever ships does what the laptop's ship script did before
it pushed, because the gate checks these and does not write them: draws the
pictures (`node docs/images/render.mjs`, `node docs/images/dataflow.mjs --png`,
`node docs/images/rings.mjs --png`, `node docs/images/og.mjs`), strips their
stamps (`python3 scripts/strip_c2pa.py docs/images`), runs Prettier's write,
commits with the changelog's new line on top, and pushes the commit to
`gate/<version>`. From 1.0.4.105 the gate draws the version card itself, so
the commit and the push are all a ship needs.

**Nothing else waits on a machine either** (ADR-098, the addendum of
1.0.4.105). Each of these is a workflow of its own and goes red on its own run:
`.github/workflows/deploy-function.yml` deploys the job hunt's Function when a
version touches it, started by the gate after the roll and never setting an
app setting; `.github/workflows/blog-post.yml` saves a blog post's body from
`data/blog/<slug>.md`, started by hand; and `.github/workflows/azure-read.yml`
reads Azure Monitor by hand and leaves the raw answers as its artifact.

![The CI workflow runs on GitHub Actions, every one green, forty-one of them by the end of the second day](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/github-ci-runs.jpg)

**Continuous deployment.** When the gate promotes a version to main, it calls a
workflow named Deploy, which builds the container image, pushes it to GitHub's own registry, ghcr.io,
and to Azure Container Registry beside it (from 1.0.4.88 the roll pulls from
GitHub's, public and with no credential, and the Azure registry keeps the same
tag for the undo), and rolls it onto the one web app that serves both sites and The Shed, with no
human in the loop; it reads the Cosmos DB site's name and The Shed's name at the
origin before any domain, and the edge moves after it, only when the edge's
rules changed (ADR: One web app for every .NET process). Until 19 October a
second workflow, Deploy Cosmos, rolled the same image onto the second site's
web app, and a third, Deploy The Shed, rolled The Shed. Deploy Render
fires on the same push: it builds the rendering service's own image from
`render/Dockerfile` as the same version, pushes it as `theyard-render`, rolls
the service's web app, and is done when the service answers its readiness as
that version. The service draws a page only for an API running its own
build, so the two rolls need not land together: until the API and the
service agree, the pages are drawn in the browser, as they were
before the service existed (ADR: A rendering service beside the API). Both
web apps share one App Service plan (ADR: One plan, two sites). Each workflow signs in to Azure with a token GitHub mints for this one
repository's `gate` environment, so no Azure password or key is stored anywhere. The
version in the page footer is stamped by that build. The full record is
ADR: The deploy pipeline, below this entry in the menu.

Both web apps ship the same way: a Docker image built from this repository's own Dockerfile, pushed to the registry, set on the web app by its tag, and verified before anything else moves. Nothing is zip-deployed and nothing is built on the server:

[![Every app TheYard runs ships the same way: build the Docker image, push it to the registry, point the web app at the tag, verify](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/deploy-shape.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/deploy-shape)

*A preview. [Open the deployment diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/deploy-shape) to zoom in.*

![The Deploy workflow's runs on GitHub Actions, one green run per version on the live site](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/github-deploy-runs.jpg)

One run, step by step: compute the version, sign in to Azure with the
minted token, build and push the image, roll the site, verify the origin
and the domain. Every version on the site has a page like this. The picture
is from the weeks the step was called "Roll the container group"; since
1.0.0.156 it rolls a web app and is called "Roll the site".

![One Deploy run's steps: compute the version, sign in to Azure, build and push, roll the container group, verify](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/github-deploy-steps.jpg)

Deploy Render's roll and its check, read here from the workflow in this
build. The roll is one call, because every setting the service runs with is
in `infra/render.bicep` and only the image changes; the check waits until the
service answers its readiness as the version it was built as, and says so
when a site's API has not caught up yet:

```live path=.github/workflows/deploy-render.yml region=render-roll
```

```live path=.github/workflows/deploy-render.yml region=render-verify
```

**The edge deploys itself.** The HTTPS front door of this site (see the
Hosting menu) is three files in this repository. Netlify builds the `edge`
branch, which `edge-branch.yml` moves to main only after Deploy has verified
the origin the rules name and only when those files changed, with no human in
the loop.
The rule that keeps an app push from redeploying three unchanged files is in
the edge's own configuration:

```live path=netlify.toml region=ignore-rule
```

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
  GitHub mints for this one repo's main branch. No password or key gets
  the pipeline into Azure. One secret exists, the session signing key the
  roll hands to both containers so a roll does not sign everybody out; it
  is masked in every log, rotating it is one roll, and a roll without it
  invents a key at startup and says so (ADR: Three readers with no memory
  of the project).
- Roles are scoped to least privilege: push to this one registry,
  manage container groups in this one resource group, assign this one
  identity.
- Displayed versions are read from the changelog's top line, so the
  footer and the file that documents the footer cannot disagree. One
  number is missing, 1.0.0.39: it was skipped under the old run-counter
  formula, and a gap is a change that never shipped.
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

The same identity also holds Website Contributor on each web app it rolls, and on nothing wider: setting a
site's image and its settings is the whole of a roll. Today that is the one web app, granted on 19 October 2026 when
the three .NET web apps became it, and the rendering service, since 8 October (ADR: One web app for every .NET
process; ADR: A rendering service beside the API). The Azure Container Instances role above is left from the container
groups retired that day; nothing uses it now, and taking it away is a change to the identity, so it waits for the owner.

## What comes next

Deployment slots for a zero-downtime roll, which the production design under
Hosting already covers with App Service, and the registrar transfer around
the end of October that retires the Netlify edge. Neither changes the
pipeline: the image and the workflow stay the same, only the target moves.

## Why it is built in this order

Tests first, automation second. A pipeline that ships untested code is a
faster way to break production. The test wall existed before this site had a
URL, and every deploy so far has been gated on it.

## Files

- [`.github/workflows/gate.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/gate.yml): the gate, on a GitHub runner, for every `gate/<version>` branch the ship pushes; `main` moves only on its green.
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): a style job, then the suites as three jobs behind it, on every push to main and on a pull request.
- [`.github/workflows/edge-branch.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/edge-branch.yml): moves the edge's branch to main after Deploy, only when what the edge serves (edge/ or netlify.toml) changed.
- [`.github/workflows/nightly.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/nightly.yml): the nightly job the Function dispatches (ADR: The Nightly Function on the Admin tab).
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the Deploy workflow, step by step.
- [`.github/workflows/deploy-render.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy-render.yml): the rendering service's build and roll, on the same push.
- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep): the plan and the one web app Deploy rolls (the roll sets the image and four settings).
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the image Deploy builds.
- [`render/Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/render/Dockerfile): the image Deploy Render builds: the service's build on Node, nothing else.
- [`docs/CHANGELOG.md`](https://github.com/SteveStout/TheYard/blob/main/docs/CHANGELOG.md): the line every version writes for itself.

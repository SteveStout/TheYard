# ADR: The deploy pipeline

Status: accepted, 2026-09-02, the morning it shipped. The first automated
deploy rolled 1.0.0.12 onto the live site with no hands on the runner.

## Context

Until this morning every ship was a scripted manual pipeline: run the three
suites, build the image on the laptop, push it to the registry, export the
running container group, strip the lines Azure refuses, patch the tag,
recreate the group, poll, verify. Logged end to end and gated on green
tests, but a person still started it and a laptop still had to be on. The
version number (1.0.0.N) was typed by hand for each ship.

The goal, in one sentence: a merge to main reaches the live site with no
human step and no credential stored anywhere.

## Decision

A second GitHub Actions workflow, Deploy, separate from CI on purpose.

- **It fires when CI finishes green on main**, through GitHub's
  workflow_run trigger, and checks out the exact commit CI tested rather
  than whatever main has moved to since. A red CI run still fires the
  event; a job-level condition drops it before anything builds.
- **It builds the image with the two footer build arguments**, APP_VERSION
  and APP_COMMIT, so the page keeps reporting exactly what is running
  (ADR: Version in the footer, under Best Practices).
- **It pushes to the registry and rolls the container group from a
  template checked into infra/**, aci-theyard.yaml, the same file every
  deploy uses. The template is the v11 export with the three line
  families Azure rejects removed and the image line replaced by a
  placeholder the workflow fills in. It holds no secrets: the registry
  pull rides the container group's user-assigned identity.
- **Authentication is OIDC federated credentials.** The workflow asks
  GitHub for a short-lived token for this repository and this branch, and
  Azure trusts that token for one app registration whose federated
  credential subject is pinned to
  repo:SteveStout@317307255/TheYard@1352398185:ref:refs/heads/main.
  GitHub holds three identifiers as repository variables (client, tenant,
  subscription), which are not secrets. No password, key, or secret exists
  anywhere in the pipeline, so there is nothing to rotate and nothing to
  leak.
- **Roles are scoped to least privilege**: AcrPush on the one registry,
  Azure Container Instances Contributor Role on the one resource group, and
  Managed Identity Operator on the one identity the container group assigns
  at create time. The scoped set worked on the first deploy; the wider
  Contributor fallback the plan allowed for was never needed.
- **Displayed versions are read from the changelog's top line.** They were
  1.0.0.(11 + deploy run number) until 1.0.0.41, the offset being what the
  footer showed the morning this workflow was written. A red CI run consumes a
  run number without shipping anything, so that formula could name a version
  nothing ever displayed, and once did. ADR: The version comes from the
  changelog holds the replacement and the incident.
- **Deploys are serialized.** A concurrency group makes two quick merges
  wait their turn instead of fighting over one container group.
- **The workflow verifies what it shipped** the way the runner scripts did:
  it waits for the origin to answer with the new version string, then
  checks the domain for the same version and commit and a live inventory
  endpoint. A green Deploy run means what a green runner log meant.

## In the code

The workflow is [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml)
and the template it renders is [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml).
The samples below are read from this build's copy of the workflow each time
the page is served (ADR: Live code samples). The trigger and the gate:

```live path=.github/workflows/deploy.yml region=deploy-trigger
```

The checkout pinned to the commit CI tested, the version arithmetic, and
the changelog check that joined the step later (ADR: The changelog):

```live path=.github/workflows/deploy.yml region=compute-version
```

The roll, which is the whole deploy step:

```live path=.github/workflows/deploy.yml region=roll
```

The footer build arguments the image is stamped with live in the
[`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile) (ADR: Version in the footer).

## The first run failed, and the failure is worth keeping

Deploy run 1 stopped at the Azure sign-in with AADSTS700213: no federated
identity record matched the presented subject. The credential had been
created with the form every guide shows, repo:SteveStout/TheYard:ref:refs/heads/main.
The token GitHub actually presents carries the owner id and the repository
id inside the subject, repo:SteveStout@317307255/TheYard@1352398185:ref:refs/heads/main,
so a renamed or re-created repository can never inherit the trust. Azure
matches the subject character for character. The credential was updated to
the measured form and the run re-ran green. The lesson: read the subject
off the failing assertion, never off a guide.

## What this replaced

The scripted manual pipeline retires to fallback duty and stays documented,
because it is also the rollback: az container create against the kept v11
export restores the last manual build in one command, and the same command
against any later export restores that build. Nothing was deleted.

## Consequences

- The laptop no longer has to be on for a ship, and no version number is
  typed by hand again.
- The roll still has the same short restart window the manual pipeline
  had, roughly a minute while the group recreates. Accepted for this demo;
  deployment slots on App Service are the production answer, already
  covered by the undeployed Bicep design under Hosting.
- GitHub's hosted runner builds the image now, so a ship costs nothing on
  the laptop and the Docker Desktop dependency is gone from the path.
- The manual scripts, the export-and-strip step included, are now
  documentation of how it used to work rather than the way it works.

## Addendum, 2026-09-20: a roll is two calls on a web app

Superseded in part on 2026-09-20, as 1.0.0.156. The roll above rendered `infra/aci-theyard.yaml`
and ran `az container create`. The sites are web apps on an App Service plan now
(ADR: One plan, two sites), and a web app keeps its settings on itself, so the roll is two calls:
the four values the template does not hold (the telemetry connection string, the database
connection string, the session signing key and the operator's key), written to a file by python and
sent with `-o none` so nothing is printed, and then the image. The live sample above is that step
as it is today. App Service starts the new container beside the old one and moves traffic when the
new one answers, so a site serves through its own roll, which a container group never did. The
deploy identity holds Website Contributor on the two sites and nothing else new.

## Files

- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the whole pipeline; its trigger,
  version, changelog check and roll are the live blocks above.
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the gate it waits for.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml): the template it renders and rolls.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the image it builds, with the two provenance
  arguments (ADR: Version in the footer).
- [`docs/CICD.md`](https://github.com/SteveStout/TheYard/blob/main/docs/CICD.md): the design written before the build, and the
  screenshots of the runs.

## Addendum, 2026-09-09: one secret, where there were none

"No password, key, or secret exists anywhere in the pipeline" was true until
1.0.0.111. There is one now: the session signing key, a repository secret
named `YARD_AUTH_SIGNING_KEY` that the roll step reads through its
environment and substitutes into the container spec the way it substitutes
the connection strings, for both container groups, so a roll no longer ends
every session and a token minted by one container reads on the other. It is
the only thing the pipeline holds that is worth keeping from a reader, GitHub
masks it in every log, and the roll writes it into the group's environment as
a secure value and nowhere else. A roll with no secret keeps the placeholder,
which the application reads as no key at all, and behaves as every roll did
before (ADR: Three readers with no memory of the project). Rotating it is
changing the secret and rolling: every session ends once, which is what
every roll did until now.

## More of the code

The Verify step: the origin must serve the new version, then answer
readiness, then the domain must agree ([`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml)):

```live path=.github/workflows/deploy.yml region=verify
```

## Addendum, 2026-09-21: the deploy starts on the push, and the two sites roll together

Deploy fired when CI finished green on main, and Deploy Cosmos fired when
Deploy finished, so a version waited four and a half minutes for CI to prove
again what the gate had proven on both stores a minute earlier, and the
second site waited for the whole of the first site's roll. From 1.0.0.173
both workflows start on the push to main, which only a green gate makes (ADR:
The five-minute gate, the addendum on every check running once). Deploy
builds and pushes the image and rolls the first site; Deploy Cosmos waits for
that image's tag to be in the registry, ten minutes at most, and rolls the
second site beside it. The plan's one machine starts both new containers at
once, which costs each a little and saves the whole of one roll.

```live path=.github/workflows/deploy-cosmos.yml region=wait-for-image
```

The image's slow layers, `npm ci`, the restore and the publish, are kept in
GitHub's own build cache between runs through Buildx, so a version whose
packages did not change skips them. The cache is GitHub's and not the
registry's: the registry holds the images and nothing else, and its size is
Steve's to prune.

```live path=.github/workflows/deploy.yml region=build-cache
```

## Addendum, 2026-09-21: the cache is written after the roll

The build step used to write every layer to the build cache (mode=max) before it returned, and the roll waited on it. On f5b81c4 the second site found the new tag in the registry 59 s before this site began to roll: that minute was the cache upload. The build now reads the cache and pushes the image, the site rolls and answers, and a last step builds the same thing again, all cache hits on the same runner, and writes the cache for the next version. A failed roll still writes it.

```live path=.github/workflows/deploy.yml region=cache-export
```

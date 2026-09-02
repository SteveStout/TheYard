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
- **Displayed versions continue as 1.0.0.(11 + deploy run number).** The
  offset is the number the footer showed the morning the workflow was
  written, so the sequence continues without a jump. Numbers may skip when a
  red CI run consumes a run number; a gap is a change that never shipped,
  which is more honest than a renumbering.
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

## Files

- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the whole pipeline; its trigger,
  version, changelog check and roll are the live blocks above.
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the gate it waits for.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml): the template it renders and rolls.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the image it builds, with the two provenance
  arguments (ADR: Version in the footer).
- [`docs/CICD.md`](https://github.com/SteveStout/TheYard/blob/main/docs/CICD.md): the design written before the build, and the
  screenshots of the runs.

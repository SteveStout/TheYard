# ADR: The deploy pipeline

Status: accepted, 2026-09-02, the morning it shipped. The first automated
deploy rolled 1.0.0.12 onto the live site with no hands on the runner.

## In plain words

A change reaches the live site with no person involved. When the tests pass on the main branch, a second automated job (GitHub Actions) builds and ships the new image, then checks that the live site reports the new version. It signs in to Azure with a short-lived token (OIDC), so no password or key is stored anywhere.

What that is worth: a developer merges and the deploy follows on its own with nothing to rotate or leak, and the organization gets releases that need neither a laptop nor a stored credential.

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

## Addendum, 2026-09-25: the registry keeps the newest ten images

Steve: "make sure and add default pruning we only want the last 10 deployments". The registry held 212 tagged images and 23 untagged manifests, 14.1 GB against the Basic plan's 10 GiB, one image for every version since 31 August. The first choice was ACR's own purge as a scheduled ACR task; the subscription refuses ACR Tasks (`TasksOperationsNotAllowed`), so the pipeline prunes instead, in two places. After the site answers, `deploy.yml` reads the image list newest first, keeps the ten newest tagged images, and deletes every older one and every untagged manifest; it deletes nothing if it reads fewer than ten, or if either site runs an image outside the ten, and a refusal to delete marks the step and never fails the deploy. The deploy's identity holds AcrPush, which pushes and cannot delete, so that step deletes only once the identity is also granted AcrDelete on the registry, a grant that is Steve's to make. Until then, and after it as a second line, the ship script on the machine that makes every push runs the same rule after the roll with the owner's sign-in. The registry was pruned to ten by hand the same morning (tweaklane, queue 1425: 216 of 225 manifests deleted, 14.1 GB to 2.1 GB). `DeployWorkflowTests` holds the step.

## Addendum, 2026-09-25: a prune that is refused is a warning

Until the deploy identity holds AcrDelete, the prune's first delete is refused, and the step went red with its orange mark on every deploy; a mark that is always there teaches a reader to skip it. From 1.0.3.26 a refused delete prints a warning naming the missing grant and ends the step green, and a site running an image outside the ten is a warning too. The step has a five-minute limit of its own, since `continue-on-error` does not reach the job's. Two tests hold what the step assumes: the second site's name in the prune is the one its own workflow deploys to, and every image build sets `provenance: false` with no second platform, so an untagged digest is a whole image and never the child of one of the ten.

## Addendum, 2026-09-29: the latest push wins

Steve: "focused on the fastest release cycle we can get so this unblocks future work, we can stack releases so we don't have to release each one at a time, the pipeline should always grab the latest push." Both workflows held their concurrency group with `cancel-in-progress: false`, so a second push to main waited in the queue behind the roll in flight and the site was rolled twice in a row to reach the same place: once to the version nobody needed any more, then to the newest.

From 1.0.3.40 both groups cancel. A newer push cancels the run in flight, queued or rolling, and that run's successor rolls the newest commit. Nothing that decides whether a version is live moved: Verify waits for the version its own run built, at the origin and at the domain, so a roll cut off between its two calls (the settings, then the image) is finished by the next run inside its own timeout. Deploy Cosmos waits for its own image tag, so its cancelled run never rolls the second site to an image the first site skipped.

```live path=.github/workflows/deploy.yml region=latest-push-wins
```

Two steps must still run on a cancelled run: the build cache, so the next version's build finds its layers, and the prune that keeps the registry to ten images. `always()` is the condition GitHub evaluates on a cancelled run; each step also asks whether the build step finished, because a run cancelled before its image was built has no new layers to keep and no new image to count. The proof is the second push of 29 September, read off the Actions list: one run cancelled, one complete, both domains on the second version.

```live path=.github/workflows/deploy.yml region=cache-export
```

**Measured on the proof, 29 September.** 1.0.3.40 (8c6f416) was pushed at 08:44:28 CDT and 1.0.3.41 (31a5f44) at 08:50:13, inside its roll. Both 1.0.3.40 runs ended cancelled in their Verify step, after the image was built and both sites had been handed it. On the cancelled Deploy run, `Keep the build cache` (15 s) and `Keep the newest ten images` (5 s) both ran and succeeded, so `always()` is what the runner honours on a cancel, with the build step's outcome as the guard. The 1.0.3.41 runs started when the cancelled run finished those two steps, about forty seconds after the push, and both sites served 1.0.3.41 at 31a5f44 by 08:59:34, nine minutes and twenty-one seconds after its push. One roll's wait was saved: before this the second push would have queued behind the whole of the first roll.

## Addendum, 2026-09-29: the sample rolls on its own

Steve: "lets deploy it along side the yard", "lives under samples, but the only thing shared is the bicep and app service". MapLarge's developer test project was built under `samples/maplarge` as The Shed, a solution of its own inside this repository. It deploys to a third web app on the plan the two sites share, from its own workflow, `deploy-shed.yml`, which runs when a push touches that folder: the version from the sample's own changelog, one image into the same registry, the site described by the sample's own Bicep in incremental mode (an existing plan and identity, one new site, nothing else in the group touched), and a verify that waits for the new build to answer at the origin.

The two site deploys ignore `samples/**` and the sample's workflow file, so a push that changes only the sample does not roll TheYard, and a push that changes both rolls both. The repository tests skip the folder (`Repo.NotOurs`): the sample holds its own copies of the house rules, in its own suite, and this suite reads TheYard. The sample's records say what it borrowed and what it left behind.

**Measured on the first run, 29 September (1.0.3.46).** The workflow's first version asked Azure to deploy the sample's Bicep on every run, and the deploy identity may not: it holds Website Contributor on each site and nothing at the group, which is the least it needs to roll (ADR: The code is public and the secrets are not). So the site was created once, from the same Bicep, by the owner from the runner, the identity was given the same role on the new site, and the workflow rolls it the way `deploy.yml` rolls: the settings, then the image. One first run failed at that step and nothing was rolled; the record and the workflow say why.

```live path=.github/workflows/deploy-shed.yml region=*
```


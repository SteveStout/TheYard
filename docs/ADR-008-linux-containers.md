# ADR: Linux containers over Windows

Status: accepted in practice since the first image; written down 2026-09-01.

## Context

The application is built on a Windows machine, so Windows containers were
the default-looking choice. The Dockerfile went Linux instead: a
node:22-alpine build stage and the aspnet:10.0 Linux runtime. A week of
shipping has turned that instinct into reasons.

## Decision

Linux containers, everywhere the image runs.

## The reasons, as learned

- .NET 10 is fully cross-platform. Nothing in this application touches a
  Windows API, so Windows in the runtime would carry cost without buying
  anything.
- Size is a tax paid on every push and every pull. The Linux runtime image
  keeps the whole application under a few hundred megabytes; Windows base
  images are gigabytes before the first line of app code arrives. This
  registry gets fed over a home connection, and the container group pulls
  the image on every roll.
- The identity design requires it. The registry pull rides a user-assigned
  managed identity, and that capability belongs to Linux container groups
  on Azure Container Instances; Microsoft's own Q&A is a trail of Windows
  container groups failing at exactly this.
- The hardening came free. The Linux aspnet image ships a built-in
  non-root user, and the healthcheck is one curl line.
- One image works everywhere it needs to: Docker Desktop runs it on the
  Windows dev machine through WSL2, GitHub's ubuntu runners will build it
  in CI, and ACI runs it in production. Same bytes, three hosts.

## Consequences

- The dev machine needed WSL2, installed on provisioning day, a one-time
  cost.
- Anything Windows-specific can never quietly creep into the runtime; the
  container would refuse it. That constraint is a feature.

## Files

- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): Linux base images in every stage; the runtime stage
  is shown live below (the other two are in ADR: Docker packaging).
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml) and
  [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): every job runs on
  `ubuntu-latest`, so the image is built where it runs.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml): the Linux container group that hosts it.

```live path=Dockerfile region=runtime
```

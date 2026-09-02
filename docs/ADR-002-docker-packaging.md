# ADR-002: One multi-stage Docker image serves the API and the SPA

Date: 2026-08-31
Status: Accepted

## Context

The application is a .NET 10 minimal API plus a Vite React SPA. Development
happens on Windows; the container must run on Linux hosts. The Dockerfile is a
portfolio artifact: every line has to be explainable in an interview.

## Decision

A single image built in three stages:

1. node:22-alpine builds the SPA with `npm ci` (deterministic, lockfile-driven),
   manifests copied before source so dependency restore caches independently.
2. dotnet/sdk:10.0 publishes the API in Release. The TargetFramework is read
   out of the csproj at build time rather than hard-coded, and the build fails
   loudly if it cannot be resolved.
3. dotnet/aspnet:10.0 is the final runtime stage: no SDK, no compilers. The API
   serves the SPA (static files plus MapFallbackToFile("index.html") mapped
   after the API routes, so deep links work and /api is never swallowed).

Runtime facts: port 8080 via ASPNETCORE_URLS and EXPOSE; a HEALTHCHECK curls
/api/facets so health means "answering real traffic", not "process exists";
the container runs as the aspnet image's built-in non-root `app` user, with
file ownership set per-COPY via --chown instead of a duplicate chown layer.
README.md, docs/ and data/ are copied into the image because the app serves
its documentation from the About menu and loads the dataset at runtime.
A .dockerignore keeps node_modules, bin, obj and .git out of the build context.

## Alternatives considered

- Two containers (nginx for the SPA, the API behind it) with compose or an
  ingress. The standard microservice shape, rejected here: one process to
  run, one origin to front, zero CORS surface, and the API already owns
  static serving. Right answer at larger scale, unnecessary overhead at this
  one.
- Shipping the SDK image as the final stage. Rejected: size and attack
  surface; the runtime image carries no toolchain.
- Creating a custom non-root user in the Dockerfile. Rejected after it
  collided with the base image: aspnet ships a built-in `app` user (APP_UID)
  for exactly this purpose, and using it is the current best practice.

## Consequences

- Roughly 380 MB on disk, of which the application layers are about 17 MB on
  top of Microsoft's runtime image.
- Layer caching behaves predictably: editing one C# file rebuilds only the
  publish and final-stage copies; editing package.json rebuilds only the npm
  layers.
- Visitors run the whole thing with `npm run docker` and stop it with
  `npm run docker:stop`.

## Files

- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the three stages, each shown live below.
- [`.dockerignore`](https://github.com/SteveStout/TheYard/blob/main/.dockerignore): what never enters the build context.
- [`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs): the one process the image runs,
  serving the API and the built SPA from wwwroot with the fallback route.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the build that passes the two
  provenance arguments and pushes the image (ADR: The deploy pipeline).

The frontend build stage:

```live path=Dockerfile region=frontend-build
```

The API publish stage:

```live path=Dockerfile region=api-publish
```

The runtime stage, non-root, with the health check and the sources the live
samples read (ADR: Live code samples):

```live path=Dockerfile region=runtime
```

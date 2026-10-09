# ADR-002: One multi-stage Docker image serves the API and the SPA

Date: 2026-08-31
Status: Accepted

## In plain words

The site ships as one container image (Docker) that runs both the server code and the web pages. The image is built in stages, so the final one carries only the runtime and runs as a user without admin rights (non-root), and its health check asks the app a real question.

What that is worth: a developer runs the whole site with one command and can explain every line of the build, and the organization ships an image with no build tools in it, which leaves less to attack.

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

## Where it sits

No ring applies here: the record is a packaging decision about how the Dockerfile builds one image for the API and the SPA, and SOLID has nothing to say about it.

## Files

- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the three stages, each shown live below.
- [`.dockerignore`](https://github.com/SteveStout/TheYard/blob/main/.dockerignore): what never enters the build context.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the one process the image runs.
- [`api/TheYard.Api/Composition/RequestPipeline.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/RequestPipeline.cs) and [`api/TheYard.Api/Composition/SpaRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/SpaRegistration.cs): serving the
  built SPA from wwwroot, and the fallback route.
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

## Addendum, 2026-10-03: the build stage is Node 24

The build stage moved from `node:22-alpine` to `node:24-alpine`, and CI from Node 22 to Node 24, because 22 is in maintenance and 24 is the active long-term release (ADR: Technology versions). Nothing else in the stage changed.

## Addendum, 2026-10-09 (1.0.3.109): the node image comes from a mirror that needs no account

Deploy #292 for 1.0.3.108 failed in two seconds at Build and push, and twice more when re-run: Docker Hub answered the metadata request for `node:24-alpine` from GitHub's runner with 429 Too Many Requests, and once with a 504 from its token service. Nothing in the commit; the anonymous pull allowance is shared by every runner on the address, and the two .NET images come from Microsoft's registry, which has no such limit. The three Node stages, the site's build stage and the rendering service's build and run stages, now pull the same official image from AWS's public mirror of it, `public.ecr.aws/docker/library/node:24-alpine`, which Docker publishes there itself, needs no account and no secret, and is not metered that way. Read from the build machine before the change: the mirror answers the tag with the image index, status 200. A signed-in pull from Docker Hub would have raised the allowance instead, at the price of a Docker account and a secret in the pipeline, which the rules refuse. `TechnologyVersionsTests` holds both Dockerfiles to the mirror's address and the Node version, and `DockerBuildInputsTests` finds the frontend stage by it.

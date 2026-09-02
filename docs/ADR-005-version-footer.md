# ADR: Version in the footer

Status: accepted, 2026-09-01. The first build carrying it is 1.0.0.9.

## Context

Eight images had shipped before the page could say which build a visitor was
looking at. Confirming a deploy meant checking the registry tag or the
container group, which breaks this project's standing rule: everything
exposed without looking at the code.

## Decision

The page footer shows the version and the short commit hash of the running
build, and the hash links to that commit on GitHub.

Three parts make the number hard to fake and impossible to forget:

1. The ship pipeline passes the version and the commit as Docker build
   arguments, using the same counter it already bumps for the image tag.
   Image tag vN displays as 1.0.0.N.
2. The Dockerfile bakes both into the image as environment variables.
3. The API serves them at /api/version, read from the running container's
   environment, and the footer renders whatever that endpoint says.

A local checkout with no build arguments reports "dev build", so a
workstation run can never be mistaken for a deployed one.

## Alternatives considered

A version string hard-coded in the frontend was rejected because it drifts:
nothing forces the hand edit at ship time, and a number that can lie is
worse than no number. Compiling the version into the frontend bundle was
rejected for a narrower reason: it reports what the bundle was built as,
while the endpoint reports what the container is actually running, and the
second claim is the one the footer makes.

## Consequences

- The footer, the image tag, and the registry digest agree by construction.
- The Playwright suite asserts the footer renders and matches the version
  shape, so a ship that breaks version reporting fails before it builds.
- The automated pipeline planned under the CI/CD menu inherits the same two
  build arguments; nothing in this design is specific to the manual scripts.

## Files

- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the two build arguments become environment
  variables in the image, shown live below.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the pipeline computes the version
  from its run number and passes both arguments (region compute-version in
  ADR: The deploy pipeline).
- [`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs): the endpoint that reports them.
- [`src/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/App.tsx): the footer that renders them, linking the commit to
  GitHub.
- [`tests/e2e/practices.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/practices.spec.ts): the check that the footer reports
  the running build.

```live path=Dockerfile region=build-args
```

```live path=api/TheBlock.Api/Program.cs region=version-endpoint
```

```live path=src/App.tsx region=footer-version
```

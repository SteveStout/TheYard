# ADR: The second manifest

Status: accepted, 2026-09-03. A deploy shipped an image missing three files that
were in the repository, in the build output, and in every green check.

## What happened

1.0.0.58 added `public/robots.txt`, `public/sitemap.xml` and `public/og.png`, so
that a crawler could read this site and a link to it would unfurl with a picture.

Locally, everything agreed. `npm run build` put all three at the root of `dist/`.
A test asserted the files existed. Another asserted the built page carried the
Open Graph tags. The browser suite passed, CI passed, the deploy went green.

Then, on the live domain:

```
/robots.txt   404
/sitemap.xml  404
/og.png       404
```

while the same page, from the same request, carried the meta tags that point at
them.

## The diagnosis, in the order it was actually done

The 404 could come from two places, and guessing which is how an hour gets
spent. There is an edge in front of the origin, so the first question is whether
the request reaches the container at all.

`edge/_redirects` answers it:

```
/* http://theyard-ss-...azurecontainer.io:8080/:splat 200!
```

A catch-all proxy. Every path reaches the origin, including these three. So the
edge is not it, and the origin is answering the 404 itself.

The origin serves static files out of `wwwroot`, and the image builds `wwwroot`
by copying Vite's `dist/`. `dist/` had the files. So the question narrows to
whether the image's `dist/` had them, which is a question about the build:

```dockerfile
COPY index.html ./
COPY tsconfig.json ./
COPY tsconfig.app.json ./
COPY tsconfig.node.json ./
COPY vite.config.ts ./
COPY src ./src

RUN npm run build
```

There it is. The frontend stage copies an explicit list of inputs, and `public/`
is not on it, because `public/` did not exist when the list was written. Vite
inside the image had no `public/` to copy, produced a `dist/` without those three
files, and did it without a warning, because a missing publicDir is not an error.

One line fixes it. The line is not the interesting part.

## Why every check was green

This is the part worth keeping.

The Dockerfile is a **second manifest of what the frontend consists of**, written
in a different file, in a different language, maintained by hand, and consulted
by nothing until a deploy. The repository says the frontend is index.html, the
tsconfigs, vite.config.ts, `src/` and `public/`. The Dockerfile says it is the
same list minus `public/`. Both are internally consistent. Nothing compares them.

And every gate is run in the place where the disagreement is invisible. Locally
`public/` exists, so Vite finds it, so `dist/` is right, so the assertions about
`dist/` pass. The only environment where the two manifests differ is inside the
image, and nothing in CI builds the image.

That is the shape to recognise: **a duplicated list, where one copy is only ever
evaluated somewhere the tests do not run.** It is the same species as the stale
DACPAC in ADR: Data first, where `dotnet build` on the solution reported success
while leaving the previous package on disk, and the same species as the CI
failure that could only be read by somebody signed in.

## Decision

Copy `public/`, and hold the two manifests to each other with a test rather than
with attention.

```csharp
var missing = FrontendInputs
    .Where(input => Directory.Exists(Path.Combine(root, input)) || File.Exists(Path.Combine(root, input)))
    .Where(input => !Regex.IsMatch(stage, $@"^COPY\s+(?:[^\s]+\s+)*{Regex.Escape(input)}[\s/]", RegexOptions.Multiline))
    .ToArray();
```

It reads the frontend stage of the Dockerfile, up to `RUN npm run build`, and
asserts that every input which exists in the repository is copied into it. The
"which exists" clause matters: the list is what Vite reads, and an entry that has
not been created yet is not a failure, so the test does not have to be edited in
lockstep with the project.

It fails today, against the Dockerfile as it was, with the message
"these exist in the repository and the frontend build stage never copies them".

## What was rejected

**Building the image in CI and asserting the files inside it.** This is the
answer that actually proves the thing, and it costs several minutes of runner
time on every push to catch a class of defect that has occurred once. Worth
revisiting if it happens again; not worth it for the first one.

**Copying the whole tree instead of a list.** It removes the second manifest
entirely, and it also removes the layer caching that makes a source-only change
cheap, and it puts `node_modules`, `api/`, `docs/` and the test suites into the
frontend build context. The explicit list is the right shape. It just needed
something checking it.

## Consequences

- The image carries `public/`, so `/robots.txt`, `/sitemap.xml` and `/og.png`
  answer on the domain and the Open Graph tags point at something real.
- Adding a new frontend build input and forgetting the Dockerfile now fails in
  the .NET suite, in about a millisecond, instead of on the live site after a
  deploy.
- One more test that reads a file it does not own, which is a small ongoing cost
  and the reason this record explains itself.

## Files

- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the frontend stage and the line that was missing.
- [`api/TheYard.Tests/DockerBuildInputsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/DockerBuildInputsTests.cs): the two manifests, compared.
- [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the catch-all that ruled the edge out in one line.
- [`docs/ADR-040-database-source-control.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-040-database-source-control.md): the same shape, with a DACPAC.

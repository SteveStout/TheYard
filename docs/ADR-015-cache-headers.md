# ADR: Cache headers

Status: accepted, 2026-09-02, shipped as 1.0.0.20.

## Context

Steve's phone kept showing the dark sidebar after the light one had
shipped. Measured on the live site before this change: the page itself,
`/`, was served with an ETag and a Last-Modified date and no Cache-Control
header at all. HTTP lets a browser reuse a response like that without
asking, for a stretch it estimates from the file's age (the heuristic
freshness rule in RFC 9111). A browser that held the old page could keep
using it, and the old page names the old bundle files, which it held under
the same rule. The API and the documents carried no cache headers either.

The old fix, from the days of hand-written script tags, was to append the
file's date to its address, `app.css?v=20260902`, so a changed file had a
changed address and no cache could confuse the two. A React app built with
Vite does the same thing without anyone typing a date: every bundle file
is named after a hash of its own contents, `assets/index-a1b2c3d4.css`,
and index.html is rewritten at build time to point at the new names. A
change in the CSS is a change in the file name, every build, with no hand
on it. What the hash cannot fix is index.html itself, which keeps its
address forever, and that is the file the phone was reusing.

## Decision

Three cache rules, set in one place by one middleware, chosen from the
shape of the address:

- **Hashed bundle files, `/assets/*`:** `public, max-age=31536000,
  immutable`, a year. Their names change when their contents change, so a
  stale copy is impossible, and a browser may keep them as long as it
  likes, which is what makes the second visit fast. Only a 200 gets this
  header; a missing file is never remembered for a year.
- **Everything that can change under the same address:** `no-cache`. The
  page, the API, the documents, the version. A browser may keep a copy but
  must ask before using it. The page answers those asks with 304 Not
  Modified from its ETag, which costs one round trip and no bytes.
- **The photo set, `/api/images/*`:** unchanged, one day, as it already
  was. The photos do not change between builds.

The date in the address is not needed and not used. The hash in the file
name is the same idea, done by the build, for every file, every time.

## In the code

The rules, read from this build
([`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs)):

```live path=api/TheBlock.Api/Program.cs region=cache-headers
```

The proof is
[`api/TheBlock.Tests/CacheHeaderTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/CacheHeaderTests.cs):
the API, the documents and the version say no-cache; a missing bundle file
says no-cache rather than immutable; a photo keeps its day.

## Consequences

- A deploy is visible on the next load, on any device: the browser asks
  for the page, gets the new one, and the new one names new bundle files.
- A device that loaded the site before this shipped still holds the old
  page under the old rule until it reloads once; after that the rules
  above apply.
- The edge in front of the site was measured the same day as passing API
  responses through without caching them (Cache-Status fwd=miss), so the
  rules above reach the browser as written.
- The Admin tab, the version endpoint and the documents are fetched fresh
  every time. They are small, and being current is their whole job.

## Files

- [`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs): the middleware (region
  cache-headers above), the photo set's own rule, and the SPA fallback that
  answers only app routes.
- [`api/TheBlock.Tests/CacheHeaderTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/CacheHeaderTests.cs): the proof, header by
  header.
- [`vite.config.ts`](https://github.com/SteveStout/TheYard/blob/main/vite.config.ts) and [`index.html`](https://github.com/SteveStout/TheYard/blob/main/index.html): the build that names
  every bundle file by its contents and rewrites the page to match.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): where the built bundle lands (`/app/wwwroot`).

## More of the code

The static file middleware, the photo set's own rule, and the fallback that
answers only app routes ([`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs)):

```live path=api/TheBlock.Api/Program.cs region=static-files
```

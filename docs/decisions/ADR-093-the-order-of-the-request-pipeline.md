# ADR: The order of the request pipeline

Status: accepted, 2026-10-07 (1.0.3.92). The middleware on both sites follows the order Microsoft documents for an app behind a proxy, each placement said in a comment beside its line, held by tests, and measured on the live domains before and after.

## In plain words

Every request to this site passes through a short list of steps before anything answers it: one times it, one works out who sent it, one turns a crash into a readable error, one serves files, one finds the code that answers an address, one reads the sign-in. The order of that list decides what each step can see and how much work a request does before it is answered. Until this version the files came last, so every page, script and photo waited behind the sign-in, the session and the catalogue. This record puts the list in the order Microsoft documents, explains every position in plain words beside its line, and says where a popular graphic of the same list disagrees with the documentation and which one the build follows.

What that is worth: a visitor gets the page's files without the server reading a sign-in it does not need, a developer can read why each step sits where it sits and change one without guessing, and the organization gets a server that reads the visitor's address from the proxies it trusts rather than from a header anyone can write.

## Context

Steve, 7 October: an optimal request pipeline on both sites, in the order Microsoft documents, tuned for hosting behind an edge. The source of truth is Microsoft Learn's middleware order, not the LinkedIn graphic that started it. Seven trouble areas were read at 9810df2 and re-read at 1.0.3.89 before any change:

1. The files ran last.
2. Three places read the edge's headers by hand: the session cookie's Secure flag, the activity card's visitor address (the leftmost X-Forwarded-For entry, which is the one a visitor writes), and the reset link's host (a raw X-Forwarded-Host when Site:Url was unset).
3. The HTTP logger sat above everything. Read again, it had never written a line: the logging configuration has held `Microsoft.AspNetCore` at Warning since the first commit, and the middleware logs at Information.
4. Routing was implicit, so ASP.NET Core put it at the very top.
5. The error record sat below the sign-in and the session's renewal, so an exception in either never reached the Admin tab.
6. The other store warmed on its first visitor.
7. The Shed had none of this.

## Decision

The order, top to bottom, in `Composition/RequestPipeline.cs`:

| Position | Middleware | Why here | What breaks if it moves |
| --- | --- | --- | --- |
| 1 | Request timing | Outermost, so it records the status the caller received and the whole time they waited. | Below the exception handler, a failed request is recorded as a 200. |
| 2 | `UseForwardedHeaders` | Before anything that reads the visitor's address or scheme. | Below it, the cookie, the activity card and generated links see App Service's front end instead of the visitor. |
| 3 | `UseExceptionHandler`, `UseStatusCodePages` | Catches everything after it. | Anything above it fails with an empty body. |
| 4 | Error record | Directly inside the handler, so it sees a failure anywhere below, sign-in included. | Below the sign-in, an exception there is never recorded. |
| 5 | Cache rules | Registered before the files and the endpoints, so its rule is on every response either writes. | Below the files, the bundle loses its year-long rule. |
| 6 | Compression, the catalogue's two reads only | Wraps the bodies the endpoints write. | Wider, it compresses responses that carry secrets (BREACH). |
| 7 | `UseDefaultFiles`, `UseStaticFiles` | A file needs nothing below; it is answered and the request ends. | Lower, every file waits behind the sign-in, the session and the store. |
| 8 | `UseRouting` | After the files, before anything that needs the matched endpoint. The health probes are `ShortCircuit` and end here. | Implicit, routing runs at the top and nothing endpoint-aware has a place to go. |
| 9 | `UseAuthentication`, `UseAuthorization` | After routing, so authorization knows the endpoint; before every endpoint. | Above routing, authorization cannot read endpoint policies. |
| 10 | Session renewal | Needs the user, and must set its cookie before the response starts. | Above authentication, it never sees a user. |
| 11 | Store warmth | Only API reads wait for the catalogue. | Above the files, the photos wait for a hundred thousand vehicles after a roll. |

```live path=api/TheYard.Api/Composition/RequestPipeline.cs region=static-files
```

**The forwarded headers trust two hops, by count (Steve's A, 7 October).** A visitor reaches the container through the edge and App Service's front end, and neither has an address the app can list. So the app clears the known proxies and trusts the two rightmost entries of X-Forwarded-For and X-Forwarded-Proto. A value a visitor wrote is to the left of those and never read. X-Forwarded-Host is never read: a link's host is configuration (`Site:Url`), which every web app the template writes carries. A caller that skips the edge can still write the second entry; the address feeds counts, never a permission. The count is a setting, `Edge:ForwardLimit`, and `/api/admin/arrival`, behind the operator's key, shows the raw headers beside what the app resolved.

```live path=api/TheYard.Api/Composition/ApiRegistration.cs region=forwarded-headers
```

**The HTTP logger is removed, not moved.** It never wrote a line (trouble area 3). Turning it on would send a line per request to Application Insights through OpenTelemetry, whose daily cap is already reached every afternoon, and every request is recorded twice already: by the timing ring and by the request telemetry.

**Readiness on every store, for the swap.** `/readyz?stores=all` answers 503 until every store the container runs has its catalogue in. The deploy asks `/readyz`; a blue-green swap asks the other, so the first visitor to toggle stores after a swap does not wait for a load. Blue-green is written and waits on a larger plan (ADR: The ports learn to wait, the addendum of 1.0.3.91).

**The Shed matches where it has the same need, and says why not where it does not.** The before reading found that The Shed has no edge at all: its domain is bound to App Service, and Kestrel answers it directly, with no compression and no HSTS. So The Shed reads the one forwarded value it needs, the scheme from App Service's front end, one hop, and sends HSTS itself in production, which TheYard's edge already does for TheYard. Routing is called out loud after the files, as here. It leaves out the visitor address, compression, cache rules and short-circuited probes, each with its reason in a comment: it reads no address, its page is 4.5 KB and its script 1 KB, its files keep plain names, and there is nothing between routing and its health controller to skip.

## Where the documentation and the graphic disagree

- **Compression.** The graphic puts it near the bottom. The documentation puts it before anything that writes a response it should compress, static files included. The build follows the documentation for the two reads it compresses, and leaves files to the edge, which compresses them with Brotli.
- **Forwarded headers.** The graphic leaves them out. The documentation puts them first behind a proxy. The build follows the documentation.
- **Static assets.** On .NET 9 and later the documentation offers `MapStaticAssets`, which serves fingerprinted, precompressed output from the .NET build's own manifest. This site's files are built by Vite and copied into the image after the .NET publish, so there is no manifest for it to read; Vite already names every bundle file by its hash, which is what the year-long rule rests on (ADR-015). `UseStaticFiles` stays.

## What was considered and left out

- **HSTS and HTTPS redirection in TheYard.** The edge terminates TLS, redirects HTTP and sends `Strict-Transport-Security: max-age=31536000` on every answer, page, script, photo, API read and probe alike, in the before reading. The app sending its own would be a second copy of the same header.
- **Compressing the page in the container.** Since 1.0.3.88 the page is 113 KB, and the edge forwards it from Azure uncompressed on every visit, because the page is never kept at the edge. Through the edge it still arrives in 280 ms at the median from Missouri, so Brotli on the shared core for every visit would buy little.
- **CORS, antiforgery, output caching, rate limiting.** No cross-origin caller, a same-site Lax cookie on a JSON API, an edge that already keeps the catalogue's reads for thirty seconds, and a site-wide registration ceiling. Each has a place below `UseRouting` the day it is needed.
- **Netlify signed proxy requests (Steve's B, not taken for now).** It would close the direct-to-origin hole for about 15 credits and a shared secret, until the edge moves to Cloudflare on about 30 October.

## Measured

Before: 1.0.3.89, both containers 70 minutes old, from Steve's machine in Missouri, ten asks of each address at each domain and each origin (`orderlane-capture-before.log`). Time to the first byte, median:

| Address | Azure SQL domain | origin | Azure Cosmos DB domain | origin |
| --- | --- | --- | --- | --- |
| The page, `/` | 256 ms | 360 ms | 248 ms | 374 ms |
| The script | 291 ms | 361 ms | 191 ms | 430 ms |
| A photo | 168 ms | 333 ms | 198 ms | 341 ms |
| `/api/version` | 234 ms | 323 ms | 259 ms | 338 ms |
| `/api/facets` | 355 ms | 347 ms | 229 ms | 333 ms |
| `/healthz` | 252 ms | 353 ms | 227 ms | 401 ms |

At the origin every address takes about the same third of a second, a file and a probe alike: the trip from Missouri to West US 3 and back, and its TLS, is nearly all of it, and the pipeline's own share is a few milliseconds that this reading cannot separate. The after reading is taken the same way, and the record says plainly if it cannot tell the two apart.

## Where it sits

The API ring's host only: the composition root's pipeline, its registration and three readers that now trust the middleware instead of a header. No port, no use case and no rule changes. Single responsibility: one place resolves where a request came from, and every reader asks the request. It costs nothing; what would change it is an edge with fixed addresses, where trust by address would replace trust by count.

## Files

- [`api/TheYard.Api/Composition/RequestPipeline.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/RequestPipeline.cs): the order, one comment per placement.
- [`api/TheYard.Api/Composition/ApiRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/ApiRegistration.cs): the forwarded headers' options, and why there is no HTTP logger.
- [`api/TheYard.Api/Endpoints/HealthEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Endpoints/HealthEndpoints.cs): the short-circuited probes, and readiness on every store.
- [`api/TheYard.Tests/RequestPipelineTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RequestPipelineTests.cs): files before the session, probes at routing, the visitor from the right, the cookie's Secure flag.
- [`samples/maplarge/Composition/RequestPipeline.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Composition/RequestPipeline.cs): The Shed's pipeline and what it leaves out.

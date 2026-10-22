# ADR: The resume has an address

Status: accepted, 2026-10-15 (1.0.3.133). The resume is served at `/resume`, the locked file byte for byte, inline in the browser under his name and the lock's date. A link may name the door it was handed out through, `?src=linkedin`, and the activity card counts the opens by door without ever recording the query.

## In plain words

Steve sends a link to his resume now instead of attaching the file. A link has to be short enough to read aloud and stable enough to print, and it should tell him which of his channels recruiters actually open it from. `/resume` is that link. `https://theyard.stevenstout.biz/resume?src=linkedin` opens the same PDF the site has always served, and the visit is counted as a LinkedIn open.

What that is worth: one address in every signature, profile and form field, and a count of which door works, from a site that already counts its visitors without knowing who they are.

## Context

Until this record the resume was reachable at `/api/docs/resume`, which reads like an API call, and at `/docs/resume.pdf`. On 14 October Steve ruled that messages under his name carry a link, not an attachment. The activity card already walks every visitor-day through four steps to the resume, and on 14 October it counted 112 visitor-days that reached the resume in 30 days, with no way to say where they came from: the referring host is recorded, but a link opened from an email or a PDF carries none.

## Decision

**The address is an API route, not an edge redirect.** `GET /resume` answers 200 with the PDF, so the address bar keeps `/resume` and one open counts once. A Netlify redirect to `/api/docs/resume` would cost an edge deploy and count every open twice. The handler serves the same file as `/api/docs/resume` with `Content-Disposition: inline` and the filename `StevenStoutResume_2026-09-29.pdf`, his own naming for the file he sends, and the pipeline's no-cache header, so a new lock is what the next open gets.

```live path=api/TheYard.Api/Features/Docs/DocumentationEndpoints.cs region=resume-address
```

**The door is one word from a short list, recorded as a path.** `src` may be linkedin, application, email, signature or post; anything else is other. The handler leaves `/resume/<door>` on the request and the activity hook records that instead of the path, so the row stays a path with nothing about the visitor in it and the query string is never recorded at all (ADR-071). No schema changes on either store, and the funnel's resume step counts everything under `/resume/`.

```live path=api/TheYard.Api/ResumeDoor.cs region=resume-door
```

**The served file is held to the lock.** `wwwroot/docs/resume.sha256` carries the locked resume's hash, and `ResumeAddressTests` hashes what `/resume` serves against it, so a new resume that forgets the copy fails the gate. The same tests hold the inline header and the filename, the same bytes as `/api/docs/resume`, every door word, and a visit with an email address in its query that reaches the request ring as `/resume/linkedin` with no at sign anywhere.

```live path=api/TheYard.Tests/ResumeAddressTests.cs region=resume-address
```

## What was considered

A Door column on the activity row is just as correct and costs a day across both stores, the EF model, the Cosmos document and the DACPAC; the path carries the same word for nothing. A wrapper page with a download button adds a click and a second request to what a phone already does with a PDF. Both stay available if the bare PDF shows a blank frame on a phone.

## Addendum, 2026-10-15 (1.0.3.134): the opens by door, on the activity card

The door is only worth recording if somebody reads it. The activity report now carries `doors`, one entry per door and one for the site itself (the resume opened from the page's own links, with no door), each with the visitor-days and the requests that opened it, people only so a scanner walking the links does not count as interest. The Site activity card draws them as bars beside the path to the resume, which answers the question the address was made for: which of Steve's channels recruiters actually open the resume from.

```live path=api/TheYard.Api/Features/Admin/Operations/ActivityWho.cs region=doors
```

## Where it sits

The API layer only: one route in `Endpoints/`, one small static class beside it, and two one-line hooks in the activity path. Single responsibility: `ResumeDoor` decides what a door is, the endpoint serves the file, and the activity code counts paths as it always has. What would change it is a second document that wants the same treatment, at which point the door list belongs to the link rather than to the resume.

## Files

- [`api/TheYard.Api/ResumeDoor.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/ResumeDoor.cs): the doors, the path a door is recorded as, and the filename.
- [`api/TheYard.Api/Features/Docs/DocumentationEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Docs/DocumentationEndpoints.cs): the route and its handler.
- [`api/TheYard.Api/Shared/ObservabilityRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/ObservabilityRegistration.cs): the activity hook reads the door's path when the handler left one.
- [`api/TheYard.Api/Features/Admin/Operations/ActivityWho.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Operations/ActivityWho.cs): the resume step counts everything under `/resume/`.
- [`api/TheYard.Tests/ResumeAddressTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ResumeAddressTests.cs) and [`tests/e2e/resume.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/resume.spec.ts): the address, the bytes, the doors and the query that never lands.

## Addendum, 2026-10-16 (1.0.3.142): in the API document

The address shipped excluded from the API document, the way `/about` is, because it serves a file rather than JSON. ADR-075's rule is wider than that: every public endpoint is in the document with its name, a sentence and its responses, and `ApiDocumentTests` holds the document to the routing table. `/resume` is now described there: GetResumeAtItsAddress (GetResume is `/api/docs/resume`, the same file under `/api`), under Documents, answering `application/pdf`, with `src` explained as a door that is counted and never stored. `/about` stays out: it is a page for search engines, not an endpoint a tool calls.

# ADR: Responsive photos

Status: accepted, 2026-09-03, shipped as 1.0.0.47. Part of Steve's performance
and accessibility pass: "srcset and blur-up images."

## Context

The vendored stock photos are 1280 pixels wide. The card paints them at about
358 CSS pixels on a desktop and 341 on a phone. Nothing chose that; it is just
what the grid does with a 1280 source and a `width: 100%` image.

Measured on the live site before anything changed:

| | |
| --- | --- |
| photos on a first load | 9 |
| bytes for them | 2,324 KB |
| source size | 1280 x 750 |
| painted at, desktop | 358 x 269 CSS px |
| painted at, phone | 341 x 256 CSS px |
| bytes on a phone | 2,324 KB, the same |

Two things stand out. The linear oversupply is about three and a half times,
which is roughly twelve times the pixels. And the phone pulls exactly what the
desktop does, on the connection least able to afford it, which is the case
`srcset` was invented for.

## Decision

**A 480-wide copy of every photo, committed beside the original.**
`scripts/resize_photos.mjs` writes `coupe-01-480.jpg` next to `coupe-01.jpg`
using sharp, at quality 78, refusing to enlarge anything already smaller.

| | originals | card copies |
| --- | --- | --- |
| files | 50 | 50 |
| total | 14,361 KB | 1,362 KB |
| each | | 91% smaller |

**Committed, not built.** These are vendored assets, like the originals, and
they change when somebody adds a photo. Putting an image pipeline in CI or in
the Dockerfile so that fifty files can be regenerated on every build would be
machinery earning nothing; sharp is a development dependency and the script is
run by hand with `npm run images:resize`.

**The browser chooses, through `srcset` and `sizes`.** The card says
`(min-width: 1024px) 360px, 92vw`, the detail view's main photo says
`(min-width: 1024px) 640px, 94vw`, and a thumbnail says `88px`. A dense screen
still gets the 1280, which is the point of describing both rather than swapping
one for the other.

**The copy's name is derived in the browser, and a test makes that safe.**
`VehicleImage` swaps `.jpg` for `-480.jpg`. That is a naming convention rather
than a rule about the domain, so it does not need to travel on the wire, but it
does need to be true: a `srcset` candidate that 404s does not degrade to the
other candidate, it fails the image. `PhotoSizeTests` holds every entry in the
photo manifest to having a copy on disk, and holds the copies to being at least
70 per cent smaller in total, so a photo added without running the script fails
the build rather than a card.

**The 1280 descriptor is a claim, so the script enforces it.** The width in a
`srcset` descriptor is the file's real width or it is a lie the browser acts on.
Every original is 1280 wide, and the resize script is the place that knows.

## What is not here

**Blur-up placeholders.** The brief asked for them and they are not in this
change, because the measurement stopped arguing for them. At 480 wide the card
photos are 20 to 44 KB each and arrive fast enough that a placeholder would
mostly be seen flashing. The card already reserves its space with a fixed
aspect ratio, so there is no layout shift for a placeholder to hide, and there
is already a neutral illustration for the case where a photo is missing or
fails. A low-quality placeholder for each of fifty photos is fifty more assets
or a base64 blob in the bundle, to cover a gap that is now about a tenth of a
second on a normal connection. If the gap comes back, this is where to start.

**WebP or AVIF.** They would beat JPEG again by a similar margin, and they are
a second copy of every file plus a `<picture>` element. Worth doing next; not
worth doing in the same change as the thing that already removed nine tenths of
the weight.

## In the code

Choosing the copy (`src/components/VehicleImage.tsx`):

```live path=src/components/VehicleImage.tsx region=srcset
```

The test that makes the convention safe
(`api/TheYard.Tests/PhotoSizeTests.cs`):

```live path=api/TheYard.Tests/PhotoSizeTests.cs region=photo-sizes
```

## Consequences

- The repository and the container image grow by about 1.3 MB, and the page
  gets about 2 MB lighter on every first load. That is a good trade in one
  direction only if you never look at the image size, so it is stated here.
- Adding a photo now has a second step, and forgetting it fails the API suite
  with a message naming the missing files and the command to run.
- The detail view's main photo still pulls the 1280, which is correct: it
  paints at about 640 and one eager image is not the problem this record was
  written about.

## Addendum, 2026-09-22 (1.0.3.2): AVIF first, on the Author page

Steve, on the page's weight: "Keep the originals but you can compress the originals". The originals are untouched; the cuts are what changed.

The Author page was 755 KB on a phone and 1,535 KB on a desk, all of it photographs already cut per width and served as WebP before JPEG. Lowering the WebP quality was not worth it: measured on the originals at 960 wide, quality 78 to 58 took the Christmas photograph from 243 KB only to 189, and the loss was visible in the sky. AVIF at quality 50 took the same picture to 122 KB, the willow from 226 to 116 and the grill from 379 to 183, and a crop at full size was if anything cleaner, because the format holds a night sky's gradient where WebP bands it.

**Decision: every Author photograph is cut to AVIF as well, and the picture element offers AVIF, then WebP, then JPEG.** Across all the cuts, the 960 set went from 1,962 KB to 992 and the 480 set from 586 KB to 296, which is about half in both. WebP stays for a browser too old for AVIF (Safari before 16), and the JPEG stays because an `img` needs a `src` that anything can read; a reader on an old browser gets what they got before. `scripts/author_photos.mjs` writes all three, strips metadata from each and reads every file back; `AuthorPageTests` checks the AVIF set the way it checks the other two, with an ISO base media reader for the width and a scan for any Exif, XMP or GPS marker anywhere in the file.

**1.0.3.3: the vehicle photographs too.** Steve asked for them the same hour. `scripts/resize_photos.mjs` writes an AVIF pair beside the WebP and JPEG pairs, each encoded from the 1280 original rather than from another copy, and `VehicleImage` offers AVIF, then WebP, then the JPEG the `img` carries. Measured across the fifty photographs: 8,131 KB to 4,500 at 1280 and 1,281 KB to 711 at 480, both about 45 per cent under the WebP. `PhotoSizeTests` holds the new pair to the manifest like the others and holds the AVIF set to at least a third under the WebP, so an encode that quietly did nothing fails rather than ships.

## Files

- [`scripts/resize_photos.mjs`](https://github.com/SteveStout/TheYard/blob/main/scripts/resize_photos.mjs): the resizer.
- [`src/components/VehicleImage.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/VehicleImage.tsx): the srcset and the derived name.
- [`src/components/VehicleDetail.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/VehicleDetail.tsx): the two other sizes.
- [`api/TheYard.Tests/PhotoSizeTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PhotoSizeTests.cs): the convention, held.
- [`docs/ADR-015-cache-headers.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-015-cache-headers.md): why these files are cached the way they are once they arrive.

## Addendum, 2026-09-17: the WebP copy this record said was next

Steve, 9/17: the fastest site at no extra cost. The measurement that opened
that lane (`mentor\logs\lane0917-963-perflane-measure.log`, 1.0.0.139) put
the card photographs at seventeen of a first visit's twenty-eight requests
and about 450 KB of its 570 on a desktop, and Lighthouse's phone profile,
which asks for the 1280 copies on a dense screen exactly as the `sizes`
attribute tells it to, weighed the page at 1,628 KiB. The bundle and the
API were a fifth of that between them. The photographs were the weight.

The section above named the next step, and this is it. `resize_photos.mjs`
writes a WebP copy at both widths beside the JPEG pair, from the original
in both cases so the small file is not two rounds of lossy encoding, at
quality 75, which is where WebP matches the JPEG at 78 to the eye on these
photographs. `VehicleImage` renders a `picture` element whose first source
is the WebP pair with the same `srcset` and `sizes` the JPEG pair carries;
a browser that reads WebP, which is every current one, takes it, and one
that does not falls through to the `img` and gets exactly what it got
before.

What the encode measured, and it is not the number the format's reputation
promises. The fifty 1280 copies came out at 8,131 KB against 14,361 KB of
JPEG, 43 per cent under, and that is the copy a dense phone screen reads.
The fifty 480 copies came out at 1,281 KB against 1,362 KB, six per cent
under: mozjpeg at quality 78 was already tight at that size, and WebP at
75 has little left to take. So a desktop card moves a few per cent fewer
bytes and a phone moves a lot fewer, which is the right way round, and
the test is written to the measured margins rather than to a hope:
`PhotoSizeTests` holds all three derived names to the manifest, holds the
1280 WebP set to at least a quarter under the JPEG set, and holds the 480
set to being no larger. The cost is stated the way the first change's was:
the repository and the image grow by about 9.6 MB, most of it the 1280
set, for a page that gets about 40 per cent lighter on a phone.

What was considered and not taken: AVIF, which is smaller again and costs
seconds a file to encode and a third copy of every photograph for a margin
the measurement did not ask for yet; and dropping the 1280 candidate for
phones, which is a change to what a dense screen is offered rather than to
how it is encoded, and belongs to a measurement of its own.


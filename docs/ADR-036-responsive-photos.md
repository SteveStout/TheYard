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

## Files

- [`scripts/resize_photos.mjs`](https://github.com/SteveStout/TheYard/blob/main/scripts/resize_photos.mjs): the resizer.
- [`src/components/VehicleImage.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/VehicleImage.tsx): the srcset and the derived name.
- [`src/components/VehicleDetail.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/VehicleDetail.tsx): the two other sizes.
- [`api/TheYard.Tests/PhotoSizeTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PhotoSizeTests.cs): the convention, held.
- [`docs/ADR-015-cache-headers.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-015-cache-headers.md): why these files are cached the way they are once they arrive.

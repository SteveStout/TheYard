/**
 * Card-sized copies of every vendored photo.
 *
 * The originals are 1280 wide and the card paints them at about 358 CSS pixels
 * on a desktop and 341 on a phone, so a first load pulled 2.3 MB to fill nine
 * boxes a third that size, and the phone pulled exactly the same bytes as the
 * desktop. This writes a 480-wide copy beside each original; the browser picks
 * between them with srcset, and a device with a dense screen still gets the
 * 1280 (ADR: Responsive photos).
 *
 * Since 1.0.0.143 it also writes a WebP copy at both widths, `coupe-01.webp`
 * and `coupe-01-480.webp`, which the page offers first through a `picture`
 * element and every current browser takes; the JPEG pair stays as the
 * fallback and as the file a client without WebP gets (ADR: Responsive
 * photos, addendum). Quality 75 is where WebP matches the JPEG at 78 to the
 * eye on these photographs; measured on this set it is 43 per cent under the
 * JPEG at 1280 and six per cent under at 480, where mozjpeg was already tight.
 *
 * Run it with `npm run images:resize`. The outputs are committed, like the
 * originals, because they are vendored assets and not a build product: nothing
 * in CI or the Dockerfile should have to own an image pipeline for a set of
 * fifty files that changes when somebody adds a photo.
 */
import { readdir, stat } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import sharp from 'sharp';

const DIR = path.join(process.cwd(), 'api', 'TheYard.Api', 'wwwroot', 'images');
const WIDTH = 480;
const SUFFIX = '-480.jpg';
const WEBP_SUFFIX = '-480.webp';
const WEBP_QUALITY = 75;

const files = (await readdir(DIR))
  .filter((name) => name.endsWith('.jpg') && !name.endsWith(SUFFIX))
  .sort();

let before = 0;
let after = 0;
let webpBefore = 0;
let webpAfter = 0;

const wrongWidth = [];

for (const name of files) {
  const source = path.join(DIR, name);
  const target = path.join(DIR, name.replace(/\.jpg$/, SUFFIX));

  // The srcset in VehicleImage describes the original as 1280w, and a width
  // descriptor is either the file's real width or a lie the browser acts on.
  // This is the only place that knows, so this is the place that checks.
  const meta = await sharp(source).metadata();
  if (meta.width !== 1280) {
    wrongWidth.push(`${name} is ${meta.width} wide, not 1280`);
  }

  // withoutEnlargement: a photo narrower than 480 is copied at its own width
  // rather than upscaled, which would cost bytes and buy blur.
  await sharp(source)
    .resize({ width: WIDTH, withoutEnlargement: true })
    .jpeg({ quality: 78, mozjpeg: true })
    .toFile(target);

  // The same two widths as WebP. The 1280 is encoded from the original and the
  // 480 from the original too, never from the JPEG copy, so the smaller file
  // does not carry two rounds of lossy encoding.
  const webpLarge = path.join(DIR, name.replace(/\.jpg$/, '.webp'));
  const webpSmall = path.join(DIR, name.replace(/\.jpg$/, WEBP_SUFFIX));
  await sharp(source).webp({ quality: WEBP_QUALITY }).toFile(webpLarge);
  await sharp(source)
    .resize({ width: WIDTH, withoutEnlargement: true })
    .webp({ quality: WEBP_QUALITY })
    .toFile(webpSmall);

  const from = (await stat(source)).size;
  const to = (await stat(target)).size;
  const webpFrom = (await stat(webpLarge)).size;
  const webpTo = (await stat(webpSmall)).size;
  before += from;
  after += to;
  webpBefore += webpFrom;
  webpAfter += webpTo;
  console.log(
    `${name}: ${Math.round(from / 1024)} KB -> ${Math.round(to / 1024)} KB (${Math.round((1 - to / from) * 100)}% smaller); ` +
      `webp ${Math.round(webpFrom / 1024)} KB and ${Math.round(webpTo / 1024)} KB`
  );
}

console.log(
  `\n${files.length} photos: ${Math.round(before / 1024)} KB of originals, ` +
    `${Math.round(after / 1024)} KB of card copies, ` +
    `${Math.round((1 - after / before) * 100)}% smaller each`
);
console.log(
  `WebP: ${Math.round(webpBefore / 1024)} KB at 1280 (${Math.round((1 - webpBefore / before) * 100)}% under the JPEG), ` +
    `${Math.round(webpAfter / 1024)} KB at 480 (${Math.round((1 - webpAfter / after) * 100)}% under the JPEG)`
);

if (wrongWidth.length > 0) {
  console.error(
    `\nsrcset claims every original is 1280 wide and these are not:\n  ${wrongWidth.join('\n  ')}`
  );
  process.exit(1);
}

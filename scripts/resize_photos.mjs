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

const files = (await readdir(DIR))
  .filter((name) => name.endsWith('.jpg') && !name.endsWith(SUFFIX))
  .sort();

let before = 0;
let after = 0;

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

  const from = (await stat(source)).size;
  const to = (await stat(target)).size;
  before += from;
  after += to;
  console.log(
    `${name}: ${Math.round(from / 1024)} KB -> ${Math.round(to / 1024)} KB (${Math.round((1 - to / from) * 100)}% smaller)`
  );
}

console.log(
  `\n${files.length} photos: ${Math.round(before / 1024)} KB of originals, ` +
    `${Math.round(after / 1024)} KB of card copies, ` +
    `${Math.round((1 - after / before) * 100)}% smaller each`
);

if (wrongWidth.length > 0) {
  console.error(
    `\nsrcset claims every original is 1280 wide and these are not:\n  ${wrongWidth.join('\n  ')}`
  );
  process.exit(1);
}

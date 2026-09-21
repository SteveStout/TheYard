/**
 * The Author page's photographs, cut for the web (ADR: The sidebar, the
 * addendum on the author's section).
 *
 * The originals are somebody's own pictures of his own life, and they never
 * enter the repository: this reads them from a folder outside it, named on
 * the command line, and writes only the cuts. Each photograph in
 * src/lib/authorPhotos.json is looked for there as `<name>-hd.jpg`, then
 * `<name>.jpg`, then `<name>.png`, and written at every width the list gives
 * it, as WebP and as JPEG, into api/TheYard.Api/wwwroot/images/author.
 *
 * Two things are checked here because this is the only place that can:
 *
 * - No cut is ever wider than its source. A width descriptor in a srcset is
 *   either the file's real width or a lie the browser acts on, so a source too
 *   narrow for the list stops the run rather than being scaled up.
 * - No cut carries metadata. sharp drops EXIF, GPS, XMP and ICC unless asked to
 *   keep them, and nothing here asks; every file written is read back and the
 *   run fails if one still holds a block. A phone photograph knows where it
 *   was taken, and this page must not.
 *
 * Run it with `npm run images:author -- <folder of originals>`. The outputs
 * are committed, like the vehicle photos' copies, because they are vendored
 * assets and not a build product.
 */
import { mkdir, readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import sharp from 'sharp';

const SOURCE = process.argv[2];
if (!SOURCE) {
  console.error('usage: node scripts/author_photos.mjs <folder of originals>');
  process.exit(2);
}

const OUT = path.join(process.cwd(), 'api', 'TheYard.Api', 'wwwroot', 'images', 'author');
const LIST = path.join(process.cwd(), 'src', 'lib', 'authorPhotos.json');
const JPEG_QUALITY = 80;
const WEBP_QUALITY = 78;

const exists = (file) =>
  stat(file).then(
    () => true,
    () => false
  );

async function sourceOf(name) {
  for (const candidate of [`${name}-hd.jpg`, `${name}.jpg`, `${name}.png`]) {
    const file = path.join(SOURCE, candidate);
    if (await exists(file)) return file;
  }
  return null;
}

const photos = JSON.parse(await readFile(LIST, 'utf8'));
const cuts = photos.flatMap((photo) => [
  { name: photo.name, widths: photo.widths },
  ...(photo.phone ? [{ name: photo.phone.name, widths: photo.phone.widths }] : []),
]);

await mkdir(OUT, { recursive: true });
const wrong = [];
let bytes = 0;

for (const { name, widths } of cuts) {
  const source = await sourceOf(name);
  if (source === null) {
    wrong.push(`${name}: no original in ${SOURCE}`);
    continue;
  }
  // rotate() with no angle applies the camera's orientation before the tag that carries it is dropped.
  const upright = await sharp(source).rotate().toBuffer({ resolveWithObject: true });
  const largest = Math.max(...widths);
  if (upright.info.width < largest) {
    wrong.push(
      `${name}: the original is ${upright.info.width} wide and the list asks for ${largest}`
    );
    continue;
  }

  for (const width of widths) {
    for (const format of ['webp', 'jpg']) {
      const target = path.join(OUT, `${name}-${width}.${format}`);
      const resized = sharp(upright.data).resize({ width, withoutEnlargement: true });
      await (
        format === 'webp'
          ? resized.webp({ quality: WEBP_QUALITY })
          : resized.jpeg({ quality: JPEG_QUALITY, mozjpeg: true })
      ).toFile(target);

      const written = await sharp(target).metadata();
      const held = ['exif', 'xmp', 'iptc', 'icc'].filter((block) => written[block] !== undefined);
      if (held.length > 0) wrong.push(`${path.basename(target)} still holds ${held.join(', ')}`);
      if (written.width !== width) {
        wrong.push(`${path.basename(target)} is ${written.width} wide, not ${width}`);
      }
      const size = (await stat(target)).size;
      bytes += size;
      console.log(
        `${path.basename(target)}: ${written.width} x ${written.height}, ${Math.round(size / 1024)} KB, metadata ${held.length === 0 ? 'none' : held.join(' ')}`
      );
    }
  }
}

console.log(`\n${cuts.length} photographs, ${Math.round(bytes / 1024)} KB of cuts in ${OUT}`);

if (wrong.length > 0) {
  console.error(`\n${wrong.join('\n')}`);
  process.exit(1);
}

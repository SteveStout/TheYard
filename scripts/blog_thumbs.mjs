// blog_thumbs.mjs
//
// Does:      Makes the 320 px thumbnail of every slide the blog serves (ADR: Every LinkedIn post is also a blog post, the
//            addendum on the slide viewer): reads each post's pictures from a site, downloads each picture once, and
//            writes it beside its thumbnail into a folder, <16 hex>.<kind> and <16 hex>-320.webp, with a manifest of
//            every post's pictures in order (manifest.json). The host never resizes: this runs on a shipping machine,
//            the way the post lane resizes before it uploads.
// Does not:  Upload anything. The folder is put into the blog's container afterwards (az storage blob upload-batch with
//            the pattern *-320.webp), or into a local store through the operator's upload route with the thumb field.
// Used by:   whoever ships a version that adds slides, and the blog's ADR addendum on the slide viewer.
//
// node scripts/blog_thumbs.mjs https://theyard.stevenstout.biz <out-folder>
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import sharp from 'sharp';

const [site, out] = process.argv.slice(2);
if (!site || !out) {
  console.error('node scripts/blog_thumbs.mjs <site> <out-folder>');
  process.exit(2);
}

/** The thumbnail's width, BlogImageNames.ThumbWidth, and the WebP quality that keeps a slide's words legible at it. */
const WIDTH = 320;
const QUALITY = 72;

async function json(path) {
  const answer = await fetch(`${site}${path}`, { headers: { 'Cache-Control': 'no-cache' } });
  if (!answer.ok) throw new Error(`${path} answered ${answer.status}`);
  return answer.json();
}

/** Every post's slug: the list a page at a time, or the older list's single answer of every post. */
async function slugs() {
  const found = [];
  let after = null;
  do {
    const list = await json(`/api/blog/posts${after ? `?after=${encodeURIComponent(after)}` : ''}`);
    found.push(...(list.cards ?? list.posts ?? []).map((post) => post.slug));
    after = list.next ?? null;
  } while (after);
  return found;
}

await mkdir(out, { recursive: true });
const manifest = [];
let before = 0;
let after = 0;
for (const slug of await slugs()) {
  const post = await json(`/api/blog/posts/${slug}?nocache=${Date.now()}`);
  for (const image of post.images ?? []) {
    const name = image.url.split('/').pop();
    const picture = Buffer.from(await (await fetch(`${site}${image.url}`)).arrayBuffer());
    const thumb = await sharp(picture)
      .resize({ width: WIDTH, withoutEnlargement: true })
      .webp({ quality: QUALITY })
      .toBuffer();
    const thumbName = `${name.slice(0, 16)}-${WIDTH}.webp`;
    await writeFile(join(out, name), picture);
    await writeFile(join(out, thumbName), thumb);
    manifest.push({
      slug,
      position: image.position,
      alt_text: image.alt_text,
      picture: name,
      thumb: thumbName,
      bytes: picture.length,
      thumbBytes: thumb.length,
    });
    before += picture.length;
    after += thumb.length;
  }
}
await writeFile(join(out, 'manifest.json'), JSON.stringify(manifest, null, 1));
console.log(`${manifest.length} pictures, ${before} bytes; their thumbnails ${after} bytes`);

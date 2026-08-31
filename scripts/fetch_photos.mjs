/**
 * Fetches 50 vehicle stock photos (10 per body style) from Wikimedia Commons
 * into public/vehicles/, plus a CREDITS.md with author and license for each.
 *
 * Photos are searched per body style using well-photographed models from the
 * dataset's makes, filtered to landscape JPEGs of reasonable size, skipping
 * obvious non-exterior shots. Add unwanted file titles (one per line) to
 * scripts/photo-exclusions.txt and re-run to replace them.
 *
 * Usage: node scripts/fetch_photos.mjs
 */
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const OUT_DIR = path.resolve(import.meta.dirname, '../api/TheBlock.Api/wwwroot/images');
const EXCLUSIONS_FILE = path.resolve(import.meta.dirname, 'photo-exclusions.txt');
const THUMB_WIDTH = 1280;
const PER_STYLE = 10;
const PER_QUERY = 2;
const USER_AGENT =
  'TheBlockChallengePrototype/1.0 (OPENLANE coding challenge; one-time asset fetch)';

// Generation codes (NX4, G20, D41...) appear in Commons filenames and keep
// results on current models — the dataset spans 2016–2026.
const QUERIES = {
  suv: [
    'Ford Bronco 2021',
    'Jeep Wrangler JL',
    'Toyota RAV4 XA50',
    'Honda CR-V 2023',
    'Hyundai Tucson NX4',
    'Subaru Outback BT',
  ],
  truck: [
    'Ford F-150 fourteenth generation',
    'Ram 1500 DT crew cab',
    'Chevrolet Silverado fourth generation',
    'GMC Sierra AT4',
    'Toyota Tundra XK70',
    'Nissan Frontier D41',
  ],
  sedan: [
    'Toyota Camry XV70',
    'Honda Accord 2023 sedan',
    'Hyundai Elantra CN7',
    'BMW 3 Series G20',
    'Tesla Model 3',
    'Mazda3 BP sedan',
  ],
  coupe: [
    'Ford Mustang S550 coupe',
    'Chevrolet Camaro sixth generation',
    'BMW 4 Series G22',
    'Toyota GR86 coupe',
    'Nissan Z RZ34',
    'Subaru BRZ ZD8',
  ],
  hatchback: [
    'Volkswagen Golf VIII',
    'Honda Civic FL hatchback',
    'Mazda3 BP hatchback',
    'Toyota Corolla E210 hatchback',
    'Kia Ceed hatchback',
    'Hyundai i30 PD hatchback',
  ],
};

/** Titles that suggest anything other than a normal exterior photo. */
const EXCLUDE_TITLE =
  /(interior|engine|dashboard|cockpit|seat|trunk|boot|badge|logo|emblem|grille|wheel|rim|diecast|scale model|toy|lego|crash|wreck|damaged|cutaway|chassis|brochure|advert|police|taxi|ambulance|racing|rally|drift|roadster|convertible|station wagon|estate|variant|touring|swace|close-up)/i;

/** Model years before the dataset's 2016 floor (matches leading 4-digit years). */
const OLD_MODEL_YEAR = /\b(19[0-9]{2}|200[0-9]|201[0-5])\b/;

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const stripHtml = (html) => (html ?? '').replace(/<[^>]*>/g, '').replace(/\s+/g, ' ').trim();

async function commonsSearch(term) {
  const url = new URL('https://commons.wikimedia.org/w/api.php');
  url.search = new URLSearchParams({
    action: 'query',
    format: 'json',
    generator: 'search',
    gsrsearch: `${term} filetype:bitmap`,
    gsrnamespace: '6',
    gsrlimit: '25',
    prop: 'imageinfo',
    iiprop: 'url|size|mime|extmetadata',
    iiurlwidth: String(THUMB_WIDTH),
  }).toString();
  for (let attempt = 1; attempt <= 6; attempt++) {
    const res = await fetch(url, { headers: { 'User-Agent': USER_AGENT } });
    if (res.ok) {
      const data = await res.json();
      return Object.values(data.query?.pages ?? {}).sort((a, b) => a.index - b.index);
    }
    if (res.status !== 429 && res.status !== 503) {
      throw new Error(`Commons search failed (${res.status}) for "${term}"`);
    }
    const retryAfter = Number(res.headers.get('retry-after'));
    const waitMs = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : 2000 * 2 ** (attempt - 1);
    console.log(`  search rate-limited (${res.status}), waiting ${Math.round(waitMs / 1000)}s...`);
    await sleep(waitMs);
  }
  throw new Error(`Commons search failed after retries for "${term}"`);
}

function isUsable(page, excluded) {
  const info = page.imageinfo?.[0];
  if (!info) return false;
  if (excluded.has(page.title)) return false;
  if (info.mime !== 'image/jpeg') return false;
  if (info.width < 1100 || info.height < 650) return false;
  if (info.width <= info.height) return false; // exterior shots are landscape
  if (EXCLUDE_TITLE.test(page.title)) return false;
  if (OLD_MODEL_YEAR.test(page.title)) return false;
  return true;
}

async function download(url) {
  // upload.wikimedia.org rate-limits anonymous bursts — back off and retry.
  const cleanUrl = url.split('?')[0];
  for (let attempt = 1; attempt <= 6; attempt++) {
    const res = await fetch(cleanUrl, { headers: { 'User-Agent': USER_AGENT } });
    if (res.ok) return Buffer.from(await res.arrayBuffer());
    if (res.status !== 429 && res.status !== 503) {
      throw new Error(`Download failed (${res.status}): ${cleanUrl}`);
    }
    const retryAfter = Number(res.headers.get('retry-after'));
    const waitMs = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : 2000 * 2 ** (attempt - 1);
    console.log(`  rate-limited (${res.status}), waiting ${Math.round(waitMs / 1000)}s...`);
    await sleep(waitMs);
  }
  throw new Error(`Download failed after retries: ${cleanUrl}`);
}

const excluded = new Set(
  await readFile(EXCLUSIONS_FILE, 'utf8')
    .then((text) => text.split(/\r?\n/).map((line) => line.trim()).filter(Boolean))
    .catch(() => [])
);

await mkdir(OUT_DIR, { recursive: true });
const credits = [];
const usedTitles = new Set();

for (const [style, terms] of Object.entries(QUERIES)) {
  const picks = [];
  const spares = [];

  for (const term of terms) {
    if (picks.length >= PER_STYLE) break;
    const pages = await commonsSearch(term);
    await sleep(150);
    let taken = 0;
    for (const page of pages) {
      if (!isUsable(page, excluded) || usedTitles.has(page.title)) continue;
      if (taken < PER_QUERY && picks.length < PER_STYLE) {
        picks.push(page);
        usedTitles.add(page.title);
        taken++;
      } else {
        spares.push(page);
      }
    }
  }

  // Backfill from spares if some queries came up short.
  for (const page of spares) {
    if (picks.length >= PER_STYLE) break;
    if (usedTitles.has(page.title)) continue;
    picks.push(page);
    usedTitles.add(page.title);
  }

  if (picks.length < PER_STYLE) {
    console.warn(`WARNING: only ${picks.length}/${PER_STYLE} photos found for ${style}`);
  }

  for (const [index, page] of picks.entries()) {
    const info = page.imageinfo[0];
    const filename = `${style}-${String(index + 1).padStart(2, '0')}.jpg`;
    const buffer = await download(info.thumburl);
    await writeFile(path.join(OUT_DIR, filename), buffer);
    await sleep(600);
    const meta = info.extmetadata ?? {};
    credits.push({
      file: filename,
      commonsTitle: page.title,
      source: info.descriptionurl,
      artist: stripHtml(meta.Artist?.value) || 'Unknown',
      license: stripHtml(meta.LicenseShortName?.value) || 'See source',
    });
    console.log(`${filename}  <-  ${page.title} (${credits.at(-1).license})`);
  }
}

const md = [
  '# Photo credits',
  '',
  'Vehicle photos sourced from Wikimedia Commons via `scripts/fetch_photos.mjs`.',
  'Each file below links to its source page with full license details.',
  '',
  ...credits.map(
    (c) => `- **${c.file}** — ${c.artist} — ${c.license} — [source](${c.source})`
  ),
  '',
].join('\n');

await writeFile(path.join(OUT_DIR, 'CREDITS.md'), md);
await writeFile(path.join(OUT_DIR, 'credits.json'), JSON.stringify(credits, null, 2));

// Manifest consumed by the API (api/Program.cs) so photo picks can prefer
// the vehicle's own make (titles reveal what each photo shows).
const manifest = credits.map((c) => ({
  file: c.file,
  style: c.file.replace(/-\d+\.jpg$/, ''),
  title: c.commonsTitle,
}));
await writeFile(
  path.resolve(import.meta.dirname, '../api/TheBlock.Api/photo-manifest.json'),
  JSON.stringify(manifest, null, 2)
);
console.log(`\nDone: ${credits.length} photos in ${OUT_DIR}`);

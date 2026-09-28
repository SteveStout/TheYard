// Warm the edge after a deploy (ADR: Cache headers, addendum of 28 September).
//
//   node scripts/warm-edge.mjs https://theyard.stevenstout.biz [deploy]
//
// Every deploy renames every hashed file, so until this runs the first visitor
// after a deploy pays the edge's miss on every chunk they open (measured on
// 28 September: about 700 ms for the activity card's four chunks against 33 ms
// once the edge held them). This reads the build's file list off the site
// itself, through the public address rather than the origin: the page names its
// entry files, and each script names the chunks it can load, so following those
// names from the page reaches every file the build serves. It then asks for
// each one once, and for the Admin tab's public reads, with the site's own mark
// on the agent so the Site activity card counts these as the site reading
// itself and never as people.
//
// A failure is a warning: a deploy that answers is a good deploy whether or not
// its files were warmed, so this always exits 0.

const site = (process.argv[2] ?? '').replace(/\/$/, '');
const agent = `TheYard-SelfRead/1 (${process.argv[3] ?? 'deploy'})`;
if (!site.startsWith('https://')) {
  console.log('::warning::warm-edge needs the public address, https://...');
  process.exit(0);
}

const READS = [
  '/api/admin/activity?window=24h',
  '/api/admin/activity?window=7d',
  '/api/admin/activity?window=30d',
  '/api/health',
  '/api/admin/machines',
  '/api/admin/pages',
];

async function get(path) {
  const started = performance.now();
  try {
    const response = await fetch(site + path, {
      headers: { 'user-agent': agent },
      signal: AbortSignal.timeout(30_000),
    });
    const body = await response.text();
    return {
      path,
      status: response.status,
      ms: Math.round(performance.now() - started),
      cache: response.headers.get('cache-status') ?? '',
      body,
    };
  } catch (error) {
    return {
      path,
      status: 0,
      ms: Math.round(performance.now() - started),
      cache: '',
      body: '',
      error,
    };
  }
}

// Every hashed file a text names: /assets/name-hash.ext, with or without the leading slash.
const ASSET = /\/?assets\/[\w.-]+\.(?:js|css|woff2?|avif|webp|png|jpg|svg)/g;
const names = (text) => [
  ...new Set((text.match(ASSET) ?? []).map((n) => '/' + n.replace(/^\//, ''))),
];

const page = await get('/');
if (page.status !== 200) {
  console.log(`::warning::warm-edge could not read ${site}/ (status ${page.status})`);
  process.exit(0);
}
const seen = new Set();
let queue = names(page.body);
const results = [];
while (queue.length > 0) {
  const batch = queue.filter((path) => !seen.has(path));
  batch.forEach((path) => seen.add(path));
  const read = await Promise.all(batch.map(get));
  results.push(...read);
  queue = read.filter((r) => /\.(js|css)$/.test(r.path)).flatMap((r) => names(r.body));
}

const reads = [];
for (const path of READS) reads.push(await get(path));

const failed = [...results, ...reads].filter((r) => r.status !== 200);
for (const r of results)
  console.log(`${r.status} ${String(r.ms).padStart(5)} ms  ${r.cache || '-'}  ${r.path}`);
for (const r of reads) console.log(`${r.status} ${String(r.ms).padStart(5)} ms  ${r.path}`);
console.log(
  `warmed ${results.length} files and ${reads.length} reads on ${site}; ${failed.length} did not answer 200`
);
for (const r of failed)
  console.log(`::warning::warm-edge: ${r.path} answered ${r.status || r.error}`);
process.exit(0);

// Draws server-page.svg (ADR: A rendering service beside the API) in the style
// of dataflow.svg: a page drawn on the server, from the address a visitor
// typed to the HTML the browser paints, and then the takeover, where React in
// the browser attaches to that HTML instead of drawing it again. Every box is a
// file; the path under each title is where that step lives. Run from the repo
// root:
//   node docs/images/server-page.mjs          writes docs/images/server-page.svg
//   node docs/images/server-page.mjs --png    also renders server-page.png at 2x in Chrome
// Redraw it when a step moves; the picture is a claim about the code.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1400;

const STYLE = `
      .box { fill: #ffffff; stroke: #7b7f8a; stroke-width: 1.5; rx: 10; }
      .lane { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; rx: 14; }
      .title { fill: #3f3a37; font-size: 15px; font-weight: 600; }
      .lane-title { fill: #3f3a37; font-size: 17px; font-weight: 700; letter-spacing: 0.02em; }
      .lane-sub { fill: #62666f; font-size: 13px; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11.5px; }
      .flow { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .flow-label { fill: #4a6c96; font-size: 12px; font-weight: 500; }
      .loop { stroke: #ab978c; stroke-width: 2; fill: none; stroke-dasharray: 6 4; marker-end: url(#arrow-taupe); }
      .loop-label { fill: #8a766b; font-size: 12px; font-weight: 500; }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
`;

/** The first load, top to bottom: every box a file, in the order a request meets them. */
const FIRST = [
  ['The address', 'the visitor\'s browser', [
    'theyard.stevenstout.biz/?view=inventory, ?vehicle= or ?doc=. A page is the bare path with its query; the bundle, the photographs and /api are other paths, which never come this way.']],
  ['The edge', 'edge/_redirects', [
    'Netlify matches the bare path on either name and rewrites it, query and all, to the rendering service as that site: /site/sql or /site/cosmos. Everything else goes to that site\'s API as before.']],
  ['The page and the build', 'render/page.ts', [
    'The service reads index.html and /api/version from the API it draws for, kept fifteen seconds. The page names the bundle that API serves; the service draws only when the API runs the build the service was made from, and during a roll it sends the API\'s page untouched for the browser to draw.']],
  ['One loader per view, under one deadline', 'render/loaders.ts', [
    'Who is signed in (the cookie forwarded, nothing else) and the build for the footer on every page; on an inventory or vehicle address the first page of the list with its facets; on ?vehicle= the vehicle; on ?doc= the document, rendered with the same Markdown code the browser uses. All at once under 2.5 seconds; a read that fails or runs late is left to the browser.']],
  ['The draw, and the stream', 'render/render.ts, src/app/entry-server.tsx', [
    'Everything above #root leaves at once, before any read has answered, so the browser starts on the stylesheet, the font and the bundle. Then #root with the site drawn by React\'s server renderer from what the loaders read; then the first load, that same data as JSON beside #root with every < escaped; then the rest of the page.']],
  ['The browser paints', 'the visitor\'s browser', [
    'The HTML arrives with the view in it, and the browser shows the inventory, the vehicle or the document before any script has run. Measured on 8 October: the inventory\'s speed index on a phone 3.5 s where it read 5.8 drawn in the browser.']],
];
const FIRST_LABELS = {
  0: 'GET /?view=inventory',
  1: '200 rewrite to /site/sql?view=inventory',
  2: 'the API runs the same build: draw',
  3: 'the reads, all at once',
  4: 'the top first, then the drawing, then the first load',
};

/** The takeover, top to bottom: React in the browser attaches to what the server drew. */
const TAKEOVER = [
  ['The bundle arrives', 'index.html, the API\'s own page', [
    'The script the page named, already downloading while the service was drawing. Nothing in it is special to a drawn page.']],
  ['The first load is read', 'src/app/mount.tsx, src/app/firstLoad.ts', [
    '#root says data-drawn="server" and the JSON beside it says which address it was drawn for. If it matches the address bar, hydrateRoot; otherwise the page is drawn from nothing, as it always was.']],
  ['Every hook starts from it', 'src/hooks/useFirstLoad.ts', [
    'Each hook that used to start empty (the address, the list, the vehicle, the account, the build, the clock) starts from the first load instead, so the browser\'s first draw is the server\'s draw, byte for byte.']],
  ['The four differences, answered', 'src/hooks/useMediaQuery.ts, src/library/DocDialog.tsx', [
    'The clock: the browser counts down from the time the server drew at, then ticks. The time zone: UTC until the page is live, then the viewer\'s. The window\'s width: "does not match" until the page is live, then the real answer. The document window: drawn open where the modal stands, then opened again as a modal.']],
  ['Live', 'src/app/TheYard.tsx', [
    'React has attached its handlers to the markup it found. A click, a filter, a bid: the site from here is the site it always was, reading /api as before. tests/e2e/drawn.spec.ts opens every drawn view in Chrome and fails on a hydration error or a window that moved.']],
];
const TAKEOVER_LABELS = {
  0: 'script runs',
  1: 'the address matches: hydrateRoot',
  2: 'same markup on both sides',
  3: 'after the takeover, each changes once',
};

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** Greedy word wrap at a character budget; the budget was set for Poppins' width, which IBM Plex Sans sits inside. */
function wrap(paragraph, width) {
  const lines = [];
  let line = '';
  for (const word of paragraph.split(' ')) {
    if (line && line.length + 1 + word.length > width) {
      lines.push(line);
      line = word;
    } else {
      line = line ? `${line} ${word}` : word;
    }
  }
  if (line) lines.push(line);
  return lines;
}

/** Stacks a lane's boxes from y0; returns each box's (y, h) and the bottom. */
function lane(x, w, boxes, labels, width, y0, out) {
  const bx = x + 20;
  const bw = w - 40;
  let y = y0;
  const geoms = [];
  boxes.forEach(([title, mono, paragraphs], i) => {
    const lines = paragraphs.flatMap((p) => wrap(p, width));
    const h = 70 + 17 * (lines.length - 1) + 14;
    out.push(`  <rect x="${bx}" y="${y}" width="${bw}" height="${h}" class="box"/>`);
    out.push(`  <text x="${bx + 15}" y="${y + 26}" class="title">${esc(title)}</text>`);
    out.push(`  <text x="${bx + 15}" y="${y + 48}" class="mono">${esc(mono)}</text>`);
    lines.forEach((line, j) => {
      out.push(`  <text x="${bx + 15}" y="${y + 70 + 17 * j}" class="body">${esc(line)}</text>`);
    });
    geoms.push([y, h]);
    if (i < boxes.length - 1) {
      const cx = bx + 60;
      out.push(`  <path d="M${cx} ${y + h} L${cx} ${y + h + 34}" class="flow"/>`);
      if (labels[i]) out.push(`  <text x="${cx + 12}" y="${y + h + 22}" class="flow-label">${esc(labels[i])}</text>`);
      y = y + h + 36;
    } else {
      y = y + h;
    }
  });
  return [geoms, y];
}

const body = [];
const [firstGeoms, firstBottom] = lane(40, 700, FIRST, FIRST_LABELS, 76, 170, body);
const [takeGeoms, takeBottom] = lane(770, 590, TAKEOVER, TAKEOVER_LABELS, 62, 170, body);
const H = Math.max(firstBottom, takeBottom) + 112;

// From the painted page into the takeover: the bundle was already on its way.
const [paintY, paintH] = firstGeoms[firstGeoms.length - 1];
const [bundleY] = takeGeoms[0];
const fromX = 60 + 660;
const midX = 755;
body.push(`  <path d="M${fromX} ${paintY + Math.floor(paintH / 2)} L${midX} ${paintY + Math.floor(paintH / 2)} L${midX} ${bundleY + 40} L790 ${bundleY + 40}" class="loop"/>`);
body.push(`  <text x="75" y="${paintY + paintH + 16}" class="loop-label">the page is on screen while the script is still arriving; the dashed line is the takeover beginning</text>`);

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>A page drawn on the server: from the address through the edge, the rendering service and the API to the HTML the browser paints, and then the takeover</title>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#4a6c96"/>
    </marker>
    <marker id="arrow-taupe" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#ab978c"/>
    </marker>
    <style>${STYLE}    </style>
  </defs>

  <rect width="${W}" height="${H}" fill="#e9e6e7"/>
  <text x="40" y="44" class="heading">A page drawn on the server: the first load, then the takeover</text>
  <text x="40" y="68" class="caption">Every box is a file; the path under each title is where that step lives. The service in render/ is a client of the API, like a browser; the API draws nothing and does not know it exists.</text>

  <!-- ===================== Lane 1: the first load ===================== -->
  <rect x="40" y="92" width="700" height="${firstBottom + 20 - 92}" class="lane"/>
  <text x="60" y="120" class="lane-title">The first load: drawn once, on the server, for this request</text>
  <text x="60" y="140" class="lane-sub">From the address to the painted page. The API is only read; a read that misses the deadline is left to the browser.</text>

  <!-- ===================== Lane 2: the takeover ===================== -->
  <rect x="770" y="92" width="590" height="${takeBottom + 20 - 92}" class="lane"/>
  <text x="790" y="120" class="lane-title">The takeover: React attaches, it does not redraw</text>
  <text x="790" y="140" class="lane-sub">Hydration, in React's word. One mismatch and the markup is thrown away.</text>

${body.join('\n')}

  <text x="40" y="${H - 66}" class="caption">Measured on the service alone in Docker: the top of the page in 7 to 9 ms, the drawn inventory in 105 ms at the median.</text>
  <text x="40" y="${H - 46}" class="caption">Through the domain the top and the drawing arrive together, because the edge in use today holds a proxied answer until it has all of it.</text>
  <text x="40" y="${H - 26}" class="caption">Source: docs/images/server-page.svg in the repository, drawn by docs/images/server-page.mjs and redrawn when a step moves.</text>
</svg>
`;

const svgPath = join(here, 'server-page.svg');
writeFileSync(svgPath, svg, 'utf8');
console.log(`wrote ${svgPath} (${W} by ${H})`);

if (process.argv.includes('--png')) {
  const { chromium } = await import('@playwright/test');
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 2 });
  await page.setContent(
    `<!doctype html><html><head>
      <link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&display=swap" rel="stylesheet">
      <style>html,body{margin:0;background:#e9e6e7}</style>
    </head><body>${svg}</body></html>`,
    { waitUntil: 'networkidle' }
  );
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(500);
  const pngPath = join(here, 'server-page.png');
  await page.locator('svg').screenshot({ path: pngPath, type: 'png' });
  await browser.close();
  console.log(`wrote ${pngPath}`);
}

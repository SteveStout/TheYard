// Draws dataflow.svg (ADR-020) in the style of infrastructure.svg: two lanes,
// every box a file, the read path top to bottom and a bid's path beside it,
// with the refetch loop that ties the two together. Run from the repo root:
//   node docs/images/dataflow.mjs          writes docs/images/dataflow.svg
//   node docs/images/dataflow.mjs --png    also renders dataflow.png at 2x in Chrome
// Redraw it when a box moves; the picture is a claim about the code.
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
      .flow { stroke: #536786; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .flow-label { fill: #536786; font-size: 12px; font-weight: 500; }
      .loop { stroke: #ab978c; stroke-width: 2; fill: none; stroke-dasharray: 6 4; marker-end: url(#arrow-taupe); }
      .loop-label { fill: #8a766b; font-size: 12px; font-weight: 500; }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
`;

/** Every box: a title, the file it lives in, and one or more paragraphs. */
const READ = [
  ['The seed file', 'data/vehicles.json', [
    'The 200 records from the challenge, never modified. The Vehicle record they load into lives with the other plain data shapes in api/TheBlock.Data.']],
  ['JsonFileVehicleSource', 'api/TheBlock.Infrastructure/JsonFileSources.cs', [
    'Reads the seed once, at startup.']],
  ['SyntheticVehicleSource', 'api/TheBlock.Infrastructure/SyntheticVehicleSource.cs', [
    "Expands 200 to 100,000 deterministic variants. Each new id is hashed (FNV-1a, api/TheBlock.Domain/Fnv1a.cs) to vary the VIN, year, odometer, prices and bid state while keeping the seed's make, model and trim mix."]],
  ['InventoryService', 'api/TheBlock.Application/InventoryService.cs', [
    "Applies each vehicle's photo gallery (api/TheBlock.Domain/PhotoGallery.cs picks from the pools in api/TheBlock.Api/photo-manifest.json) and materializes the list plus an id index, once, in a Lazy that Program.cs forces at startup."]],
  ['GET /api/vehicles', 'api/TheBlock.Api/Program.cs', [
    "api/TheBlock.Api/VehicleQueryParams.cs binds and validates every parameter into a filter, a sort and a clock. api/TheBlock.Api/Clocks.cs turns the browser's anchor_ms (its local midnight) into that clock, so the schedule math agrees with the buyer in any time zone."]],
  ['Search, in InventoryService.Search', 'api/TheBlock.Application/InventoryService.cs', [
    "1. The buyer's bids are overlaid first (api/TheBlock.Application/BidService.cs), so price bounds see the figures the page shows.",
    '2. Where: api/TheBlock.Domain/VehicleFilter.cs',
    '3. OrderBy: api/TheBlock.Domain/VehicleOrdering.cs',
    '4. Skip and Take for the page. Auction windows come from api/TheBlock.Domain/AuctionSchedule.cs, all in memory.']],
  ['VehicleWire', 'api/TheBlock.Api/VehicleWire.cs', [
    'Stamps the server-derived facts on each vehicle: auction_starts_at, auction_ends_at, auction_status, min_next_bid. The endpoint answers { total, vehicles } in snake_case.']],
  ['The fetch seam', 'src/lib/data.ts', [
    'Debounces filter changes (500 ms), caches responses per query string for five minutes (a hit skips the debounce) and aborts superseded requests. The query string comes from src/lib/inventory.ts, the same serializer that feeds the address bar.']],
  ['App state and the address bar', 'src/App.tsx', [
    'Holds the page. Filters, sort, ?vehicle and ?view are mirrored into the URL, and Back and Forward re-read it.']],
  ['Cards, detail, bid panel', 'src/components/*', [
    "Format currency (src/lib/format.ts) and tick the countdowns from the server's window (src/lib/auction.ts). No business math runs in the browser."]],
];
const READ_LABELS = { 0: 'once, at startup', 3: 'then, for every request', 6: '{ total, vehicles } as JSON' };

const WRITE = [
  ['BidPanel', 'src/components/BidPanel.tsx', [
    'Posts { amount, anchor_ms } to POST /api/vehicles/{id}/bids through src/lib/data.ts; buy now posts to /buy-now.']],
  ['HandleBid', 'api/TheBlock.Api/Program.cs', [
    'Three questions in order: is the clock anchor valid (400 if not), does the vehicle exist (404 if not), does the domain accept the action (400 with the reason if not).']],
  ['BidRules', 'api/TheBlock.Domain/BidRules.cs', [
    'The sole authority: the live window, the tiered minimum increment, and the buy-now override (a bid at or above buy_now_price wins outright at that price).']],
  ['BidService', 'api/TheBlock.Application/BidService.cs', [
    'Accepted and won bids land in an in-memory map, one anonymous buyer. The response carries the updated vehicle with a fresh min_next_bid.']],
  ['useBids', 'src/hooks/useBids.ts', [
    'Clears the query cache and refetches, so every list, filter and total reflects the bid through the same read path.']],
];
const WRITE_LABELS = { 0: 'POST', 1: 'the domain decides', 2: 'accepted, won, or rejected', 3: 'the updated vehicle' };

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** Greedy word wrap at a character budget; the budget leaves room for Poppins' width. */
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
const [readGeoms, readBottom] = lane(40, 800, READ, READ_LABELS, 88, 170, body);
const [writeGeoms, writeBottom] = lane(870, 490, WRITE, WRITE_LABELS, 56, 170, body);
const H = readBottom + 92;

// The refetch loop: from under useBids, through the corridor, into the fetch seam.
const [ubY, ubH] = writeGeoms[writeGeoms.length - 1];
const [fsY, fsH] = readGeoms[7];
const startX = 890 + 30;
const startY = ubY + ubH;
const midY = fsY + Math.floor(fsH / 2);
body.push(`  <path d="M${startX} ${startY} L${startX} ${startY + 20} L855 ${startY + 20} L855 ${midY} L822 ${midY}" class="loop"/>`);
body.push(`  <text x="${startX + 10}" y="${startY + 16}" class="loop-label">refetch, through the same read path</text>`);

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="Poppins, 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>TheYard data flow: a vehicle from a JSON file on disk to a card in the browser, and a bid back</title>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#536786"/>
    </marker>
    <marker id="arrow-taupe" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#ab978c"/>
    </marker>
    <style>${STYLE}    </style>
  </defs>

  <rect width="${W}" height="${H}" fill="#e9e6e7"/>
  <text x="40" y="44" class="heading">TheYard data flow: a vehicle from a JSON file to a card in the browser, and a bid back</text>
  <text x="40" y="68" class="caption">Every box is a file; the path under each title is where that step lives. Domain, Application, Infrastructure and Api are the projects under api/.</text>

  <!-- ===================== Lane 1: the read path ===================== -->
  <rect x="40" y="92" width="800" height="${readBottom + 20 - 92}" class="lane"/>
  <text x="60" y="120" class="lane-title">The read path, top to bottom</text>
  <text x="60" y="140" class="lane-sub">The first four boxes run once at startup; the rest run for every request.</text>

  <!-- ===================== Lane 2: the write path ===================== -->
  <rect x="870" y="92" width="490" height="${writeBottom + 46 - 92}" class="lane"/>
  <text x="890" y="120" class="lane-title">The write path: a bid</text>
  <text x="890" y="140" class="lane-sub">One anonymous buyer, state in API memory; the domain decides.</text>

${body.join('\n')}

  <text x="40" y="${H - 46}" class="caption">One rule shapes everything: derive, do not store. Windows, statuses, galleries and the 100,000 inventory are computed from stable ids, never persisted.</text>
  <text x="40" y="${H - 26}" class="caption">Source: docs/images/dataflow.svg in the repository, drawn by docs/images/dataflow.mjs and redrawn when a box moves.</text>
</svg>
`;

const svgPath = join(here, 'dataflow.svg');
writeFileSync(svgPath, svg, 'utf8');
console.log(`wrote ${svgPath} (${W} by ${H})`);

if (process.argv.includes('--png')) {
  const { chromium } = await import('@playwright/test');
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 2 });
  await page.setContent(`<!doctype html><html><head><style>html,body{margin:0;background:#e9e6e7}</style></head><body>${svg}</body></html>`);
  await page.waitForTimeout(500);
  const pngPath = join(here, 'dataflow.png');
  await page.locator('svg').screenshot({ path: pngPath, type: 'png' });
  await browser.close();
  console.log(`wrote ${pngPath}`);
}

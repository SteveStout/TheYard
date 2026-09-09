// Draws two-sites.svg (ADR: A permanent address for the second site) in the
// style of infrastructure.svg and dataflow.svg: how two names reach two
// container groups through one edge, and how both groups reach both stores.
// Every box carries the file or the resource it stands for. Run from the repo
// root:
//   node docs/images/two-sites.mjs          writes docs/images/two-sites.svg
//   node docs/images/two-sites.mjs --png    also renders two-sites.png at 2x in Chrome
// Redraw it when a name, a rule or a group changes; the picture is a claim.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1520;

const STYLE = `
      .box { fill: #ffffff; stroke: #7b7f8a; stroke-width: 1.5; rx: 10; }
      .store { fill: #f8f6f7; stroke: #7b7f8a; stroke-width: 1.5; rx: 10; }
      .lane { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; rx: 14; }
      .title { fill: #3f3a37; font-size: 15px; font-weight: 600; }
      .lane-title { fill: #3f3a37; font-size: 17px; font-weight: 700; letter-spacing: 0.02em; }
      .lane-sub { fill: #62666f; font-size: 13px; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 10.5px; }
      .flow { stroke: #536786; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .flow-label { fill: #536786; font-size: 12px; font-weight: 500; }
      .loop { stroke: #ab978c; stroke-width: 2; fill: none; stroke-dasharray: 6 4; marker-end: url(#arrow-taupe); }
      .loop-label { fill: #8a766b; font-size: 12px; font-weight: 500; }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
`;

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

const out = [];

/** One box at a fixed place: a title, a monospace line, wrapped paragraphs. Returns its geometry. */
function box(x, y, w, title, mono, paragraphs, width, cls = 'box') {
  const lines = paragraphs.flatMap((p) => wrap(p, width));
  const h = 70 + 17 * (lines.length - 1) + 14;
  out.push(`  <rect x="${x}" y="${y}" width="${w}" height="${h}" class="${cls}"/>`);
  out.push(`  <text x="${x + 15}" y="${y + 26}" class="title">${esc(title)}</text>`);
  out.push(`  <text x="${x + 15}" y="${y + 48}" class="mono">${esc(mono)}</text>`);
  lines.forEach((line, j) => {
    out.push(`  <text x="${x + 15}" y="${y + 70 + 17 * j}" class="body">${esc(line)}</text>`);
  });
  return { x, y, w, h, bottom: y + h, right: x + w, midY: y + Math.floor(h / 2) };
}

function arrow(d, cls = 'flow') {
  out.push(`  <path d="${d}" class="${cls}"/>`);
}

function label(text, x, y, cls = 'flow-label') {
  out.push(`  <text x="${x}" y="${y}" class="${cls}">${esc(text)}</text>`);
}

// ===================== Lane 1: the names =====================
const L1 = { x: 40, w: 400 };
const b1 = L1.x + 20;
const w1 = L1.w - 40;
const nameLive = box(b1, 170, w1, 'theyard.stevenstout.biz', 'CNAME -> theyard-edge.netlify.app, TTL 30 min', [
  'The live site, the name a resume carries. The group behind it serves Azure SQL Database by default.',
], 42);
const nameCosmos = box(b1, nameLive.bottom + 36, w1, 'theyard-cosmos.stevenstout.biz', 'CNAME -> theyard-edge.netlify.app, TTL 30 min', [
  'The second site, since 1.0.0.100. One label, so any DNS form accepts it; the container group\'s own name with the suffix dropped.',
], 42);
const nameBare = box(b1, nameCosmos.bottom + 36, w1, 'stevenstout.biz and www', 'A 75.2.60.5 and CNAME -> theyard-edge.netlify.app', [
  'Both forward to the live site with a 301, so a trimmed or retyped address still lands on the app.',
], 42);
const lane1Bottom = nameBare.bottom + 20;

// ===================== Lane 2: the edge =====================
const L2 = { x: 470, w: 460 };
const b2 = L2.x + 20;
const w2 = L2.w - 40;
const cert = box(b2, 170, w2, 'One certificate', "Let's Encrypt, issued and renewed by Netlify", [
  'Four names on it: the bare domain, www, theyard and theyard-cosmos. Adding the alias reissued it within the minute; nothing was bought.',
], 50);
const rules = box(b2, cert.bottom + 36, w2, 'edge/_redirects: the name picks the origin', 'the first matching rule wins, top to bottom', [
  'https://theyard-cosmos.stevenstout.biz/* to the second group, 200!',
  '/* to the first group, 200! (the catch-all, last)',
  'stevenstout.biz and www: 301 to theyard.stevenstout.biz',
  'A domain-level rule only matches a name assigned to the site, so the alias is what makes the first line eligible.',
], 50);
const ignore = box(b2, rules.bottom + 36, w2, 'netlify.toml: build only when the edge moves', 'ignore = "git diff --quiet ... -- edge/ netlify.toml"', [
  'An application push costs no credits; an edge change is one deploy, 15 of the month\'s 300. This one cost exactly that, read off the meter.',
], 50);
const lane2Bottom = ignore.bottom + 20;

// ===================== Lane 3: Azure =====================
const L3 = { x: 960, w: 520 };
const b3 = L3.x + 20;
const w3 = L3.w - 100; // a gutter on the right for the Store bar's loop
const groupSql = box(b3, 170, w3, 'aci-theyard-ss', 'theyard-ss-zmnetj67bn5h2.westus2.azurecontainer.io:8080', [
  'Store__Default = sql. Peer__Site = https://theyard-cosmos.stevenstout.biz. Rolled by the Deploy workflow from infra/aci-theyard.yaml.',
], 48);
const groupCosmos = box(b3, groupSql.bottom + 60, w3, 'aci-theyard-cosmos-ss', 'theyard-cosmos-ss-zmnetj67bn5h2.westus2.azurecontainer.io:8080', [
  'Store__Default = cosmos. Peer__Site = https://theyard.stevenstout.biz. Rolled by Deploy Cosmos from infra/aci-theyard-cosmos.yaml. Same image, same 1 CPU and 1.5 GB.',
], 48);
const storesY = groupCosmos.bottom + 76;
const halfW = Math.floor((w3 - 20) / 2);
const storeSql = box(b3, storesY, halfW, 'Azure SQL Database', 'Entra-only, no SQL login', [
  'Reached as the managed identity. One region away.',
], 22, 'store');
const storeCosmos = box(b3 + halfW + 20, storesY, halfW, 'Azure Cosmos DB', 'no keys on the account', [
  'Reached as the managed identity. In the containers\' own region.',
], 22, 'store');
const lane3Bottom = Math.max(storeSql.bottom, storeCosmos.bottom) + 20;
const H = Math.max(lane1Bottom, lane2Bottom, lane3Bottom) + 100;

// ===================== Arrows =====================
// Names to the rules box: the Host header is the only thing that differs.
arrow(`M${nameLive.right} ${nameLive.midY} L${rules.x} ${rules.midY - 26}`);
arrow(`M${nameCosmos.right} ${nameCosmos.midY} L${rules.x} ${rules.midY}`);
arrow(`M${nameBare.right} ${nameBare.midY} L${rules.x} ${rules.midY + 26}`);
label('HTTPS, then the Host header picks the rule', L2.x + 20, rules.y - 10);
// Rules to the two groups, labelled above each group.
arrow(`M${rules.right} ${rules.midY - 20} L${groupSql.x} ${groupSql.midY}`);
arrow(`M${rules.right} ${rules.midY + 20} L${groupCosmos.x} ${groupCosmos.midY}`);
label('everything else: plain HTTP to :8080', groupSql.x, groupSql.y - 10);
label('theyard-cosmos: plain HTTP to :8080', groupCosmos.x, groupCosmos.y - 10);
// Both groups open both stores: two lines down from each group into a bus, and the bus into both stores.
const busY = storesY - 34;
const sqlCx = storeSql.x + Math.floor(storeSql.w / 2);
const cosCx = storeCosmos.x + Math.floor(storeCosmos.w / 2);
out.push(`  <path d="M${groupSql.x + 40} ${groupSql.bottom} L${groupSql.x + 40} ${groupCosmos.y}" class="flow" style="marker-end:none"/>`);
out.push(`  <path d="M${groupCosmos.x + 40} ${groupCosmos.bottom} L${groupCosmos.x + 40} ${busY} L${cosCx} ${busY}" class="flow" style="marker-end:none"/>`);
arrow(`M${sqlCx} ${busY} L${sqlCx} ${storesY}`);
arrow(`M${cosCx} ${busY} L${cosCx} ${storesY}`);
label('both groups open both stores, as their managed identity', groupCosmos.x + 52, busY - 8);
// The Store bar: each site links to the other at the same page, drawn in the gutter.
const gx = groupSql.right + 30;
arrow(`M${groupSql.right} ${groupSql.midY + 16} L${gx} ${groupSql.midY + 16} L${gx} ${groupCosmos.midY - 16} L${groupCosmos.right + 2} ${groupCosmos.midY - 16}`, 'loop');
arrow(`M${groupCosmos.right} ${groupCosmos.midY + 16} L${gx + 24} ${groupCosmos.midY + 16} L${gx + 24} ${groupSql.midY + 40} L${groupSql.right + 2} ${groupSql.midY + 40}`, 'loop');
label('the Store bar: each site links to the other, at the same page', groupSql.right - 372, groupSql.bottom + 30, 'loop-label');

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="Poppins, 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>TheYard's two sites: two names at Wix, one Netlify edge, two container groups on Azure, and both stores behind both</title>
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
  <text x="40" y="44" class="heading">TheYard's two sites: two names, one edge, two container groups, both stores behind both</text>
  <text x="40" y="68" class="caption">A request reads left to right. The name a visitor typed is the only thing that differs until the edge, the edge picks the origin from it, and each origin opens both stores.</text>

  <!-- ===================== Lane 1: the names ===================== -->
  <rect x="${L1.x}" y="92" width="${L1.w}" height="${lane1Bottom - 92}" class="lane"/>
  <text x="${L1.x + 20}" y="120" class="lane-title">The names, at Wix DNS</text>
  <text x="${L1.x + 20}" y="140" class="lane-sub">Three CNAMEs and one A record, all at the same edge.</text>

  <!-- ===================== Lane 2: the edge ===================== -->
  <rect x="${L2.x}" y="92" width="${L2.w}" height="${lane2Bottom - 92}" class="lane"/>
  <text x="${L2.x + 20}" y="120" class="lane-title">One edge: the Netlify site theyard-edge</text>
  <text x="${L2.x + 20}" y="140" class="lane-sub">Terminates HTTPS, then proxies by Host. Three files in this repository.</text>

  <!-- ===================== Lane 3: Azure ===================== -->
  <rect x="${L3.x}" y="92" width="${L3.w}" height="${lane3Bottom - 92}" class="lane"/>
  <text x="${L3.x + 20}" y="120" class="lane-title">Azure: RG-THEYARD-SS, westus2</text>
  <text x="${L3.x + 20}" y="140" class="lane-sub">Two groups running the same image, and the two stores both open.</text>

${out.join('\n')}

  <text x="40" y="${H - 66}" class="caption">Each site is one store's site: the group's default store serves it; a measurement can name the other store for one request with the X-Yard-Store header.</text>
  <text x="40" y="${H - 46}" class="caption">The edge retires at the registrar transfer around the end of October 2026; both names then become two records at Cloudflare pointing at the same two origins.</text>
  <text x="40" y="${H - 26}" class="caption">Source: docs/images/two-sites.svg in the repository, drawn by docs/images/two-sites.mjs and redrawn when a name, a rule or a group changes.</text>
</svg>
`;

const svgPath = join(here, 'two-sites.svg');
writeFileSync(svgPath, svg, 'utf8');
console.log(`wrote ${svgPath} (${W} by ${H})`);

if (process.argv.includes('--png')) {
  const { chromium } = await import('@playwright/test');
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 2 });
  await page.setContent(
    `<!doctype html><html><head>
      <link href="https://fonts.googleapis.com/css2?family=Poppins:wght@400;500;600;700&display=swap" rel="stylesheet">
      <style>html,body{margin:0;background:#e9e6e7}</style>
    </head><body>${svg}</body></html>`,
    { waitUntil: 'networkidle' }
  );
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(500);
  const pngPath = join(here, 'two-sites.png');
  await page.locator('svg').screenshot({ path: pngPath, type: 'png' });
  await browser.close();
  console.log(`wrote ${pngPath}`);
}

// Draws two-sites.svg (ADR: A permanent address for the second site) in the
// style of infrastructure.svg and dataflow.svg: how the two Yard names reach
// the one web app through one edge and The Shed's name reaches it directly
// (ADR: One web app for every .NET process), how a page reaches the rendering
// service beside it (ADR: A rendering service beside the API), and how the
// one process reaches both stores.
// Every box carries the file or the resource it stands for. Run from the repo
// root:
//   node docs/images/two-sites.mjs          writes docs/images/two-sites.svg
//   node docs/images/two-sites.mjs --png    also renders two-sites.png at 2x in Chrome
// Redraw it when a name, a rule or a site changes; the picture is a claim.
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
      .flow { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .flow-label { fill: #4a6c96; font-size: 12px; font-weight: 500; }
      .loop { stroke: #ab978c; stroke-width: 2; fill: none; stroke-dasharray: 6 4; marker-end: url(#arrow-taupe); }
      .loop-label { fill: #8a766b; font-size: 12px; font-weight: 500; }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
`;

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
  'The live site, the name a resume carries. Its requests get Azure SQL Database when they name no store.',
], 42);
const nameCosmos = box(b1, nameLive.bottom + 36, w1, 'theyard-cosmos.stevenstout.biz', 'CNAME -> theyard-edge.netlify.app, TTL 30 min', [
  'The second site, since 1.0.0.100. Its requests get Azure Cosmos DB when they name no store.',
], 42);
const nameBare = box(b1, nameCosmos.bottom + 36, w1, 'stevenstout.biz and www', 'A 75.2.60.5 and CNAME -> theyard-edge.netlify.app', [
  'Both forward to the live site with a 301, so a trimmed or retyped address still lands on the app.',
], 42);
const nameShed = box(b1, nameBare.bottom + 36, w1, 'theshed.stevenstout.biz', 'CNAME -> the one web app, TTL 1 hour', [
  'The Shed, bound straight to the web app with App Service\'s managed certificate. No edge in front of it.',
], 42);
const lane1Bottom = nameShed.bottom + 20;

// ===================== Lane 2: the edge =====================
const L2 = { x: 470, w: 460 };
const b2 = L2.x + 20;
const w2 = L2.w - 40;
const cert = box(b2, 170, w2, 'One certificate', "Let's Encrypt, issued and renewed by Netlify", [
  'Four names on it: the bare domain, www, theyard and theyard-cosmos. Adding the alias reissued it within the minute; nothing was bought.',
], 50);
const rules = box(b2, cert.bottom + 36, w2, 'The rules: edge/_redirects first, then netlify.toml', 'the first matching rule wins, top to bottom', [
  'edge/_redirects: https://theyard-cosmos.stevenstout.biz/ to the rendering service as /site/cosmos; / to it as /site/sql; stevenstout.biz and www, 301 to theyard.',
  'netlify.toml: https://theyard-cosmos.stevenstout.biz/* to the one web app with X-Yard-Host: theyard-cosmos.stevenstout.biz; then /* to the same web app with X-Yard-Host: theyard.stevenstout.biz.',
  'Both proxy rules reach the same address, so each says which name the visitor typed. Only netlify.toml can add a header to a proxied request.',
], 50);
const ignore = box(b2, rules.bottom + 36, w2, 'Moved only after the roll it names', 'edge-branch.yml, after Deploy succeeds', [
  'The edge builds from the edge branch, which moves after Deploy has verified the origin and only when edge/ or netlify.toml changed: one deploy, 15 credits.',
], 50);
const lane2Bottom = ignore.bottom + 20;

// ===================== Lane 3: Azure =====================
const L3 = { x: 960, w: 520 };
const b3 = L3.x + 20;
const w3 = L3.w - 40;
const one = box(b3, 170, w3, 'APP-THEYARD-ONE-SS-ZMNETJ67BN5H2', 'https://app-theyard-one-ss-zmnetj67bn5h2.azurewebsites.net', [
  'One process, three names (api/TheYard.Api/SiteHosts.cs). The name is the Host header, or X-Yard-Host from the edge, and only a configured name counts.',
  'theyard: Azure SQL Database. theyard-cosmos: Azure Cosmos DB. An X-Yard-Store header wins over both. theshed: The Shed\'s own application, started inside the process from /app/shed (Composition/ShedMount.cs).',
  'Rolled by Deploy. A web app for containers on PLAN-THEYARD-SS, one Linux B2, 2 vCPU and 3.5 GB shared with the rendering service.',
], 56);
const renderer = box(b3, one.bottom + 70, w3, 'APP-THEYARD-RENDER-SS-ZMNETJ67BN5H2', 'https://app-theyard-render-ss-zmnetj67bn5h2.azurewebsites.net', [
  'The rendering service: /site/sql draws for the first site, /site/cosmos for the second. Its own image (render/Dockerfile), rolled by Deploy Render on the same push. Both sites are read from the one web app, each read naming its store in X-Yard-Store. It opens no store.',
], 56);
const storesY = renderer.bottom + 76;
const halfW = Math.floor((w3 - 20) / 2);
const storeSql = box(b3, storesY, halfW, 'Azure SQL Database', 'Entra-only, no SQL login', [
  'Reached as the managed identity. West US 3, the plan\'s own region.',
], 26, 'store');
const storeCosmos = box(b3 + halfW + 20, storesY, halfW, 'Azure Cosmos DB', 'no keys on the account', [
  'Reached as the managed identity. West US 2, one region from the plan.',
], 26, 'store');
const lane3Bottom = Math.max(storeSql.bottom, storeCosmos.bottom) + 20;
const H = Math.max(lane1Bottom, lane2Bottom, lane3Bottom) + 130;

// ===================== Arrows =====================
// The three edge names to the rules box.
arrow(`M${nameLive.right} ${nameLive.midY} L${rules.x} ${rules.midY - 26}`);
arrow(`M${nameCosmos.right} ${nameCosmos.midY} L${rules.x} ${rules.midY}`);
arrow(`M${nameBare.right} ${nameBare.midY} L${rules.x} ${rules.midY + 26}`);
label('HTTPS, then the Host header picks the rule', L2.x + 20, rules.y - 10);
// The Shed's name straight to the web app: under the edge lane, then up the gap between the lanes.
const underY = Math.max(lane1Bottom, lane2Bottom) + 16;
const shedX = nameShed.x + Math.floor(nameShed.w / 2);
arrow(`M${shedX} ${nameShed.bottom} L${shedX} ${underY} L${L3.x - 14} ${underY} L${L3.x - 14} ${one.bottom - 20} L${one.x} ${one.bottom - 20}`);
label('theshed: HTTPS straight to the web app, with no edge in front', shedX + 12, underY - 6);
// Rules to the web app and the service.
arrow(`M${rules.right} ${rules.midY - 20} L${one.x} ${one.y + 40}`);
arrow(`M${rules.right} ${rules.midY + 40} L${renderer.x} ${renderer.midY}`);
label('everything but a page: HTTPS with X-Yard-Host', one.x, one.y - 10);
label('a page on either Yard name: HTTPS to the rendering service, as that site', renderer.x, renderer.y - 10);
// The service reads the web app, in the gutter on the right.
const gx = one.right + 14;
arrow(`M${renderer.right} ${renderer.midY} L${gx} ${renderer.midY} L${gx} ${one.midY} L${one.right + 2} ${one.midY}`, 'loop');
label('reads with X-Yard-Store: sql or cosmos', renderer.x + 15, renderer.bottom + 16, 'loop-label');
// The web app opens both stores, past the service, down the lane's left margin.
const busY = storesY - 34;
const sqlCx = storeSql.x + Math.floor(storeSql.w / 2);
const cosCx = storeCosmos.x + Math.floor(storeCosmos.w / 2);
const marginX = L3.x + 8;
out.push(`  <path d="M${one.x + 40} ${one.bottom} L${one.x + 40} ${renderer.y - 30} L${marginX} ${renderer.y - 30} L${marginX} ${busY} L${cosCx} ${busY}" class="flow" style="marker-end:none"/>`);
arrow(`M${sqlCx} ${busY} L${sqlCx} ${storesY}`);
arrow(`M${cosCx} ${busY} L${cosCx} ${storesY}`);
label('the web app opens both stores, as its managed identity; the service opens neither', storeSql.x, busY - 8);

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>TheYard's two sites and The Shed: four names at Wix, one Netlify edge, one web app for every .NET process and the rendering service on one App Service plan, both stores behind it</title>
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
  <text x="40" y="44" class="heading">Two sites and The Shed: one edge, one web app for every .NET process, the rendering service beside it</text>
  <text x="40" y="68" class="caption">Read left to right. The edge sends a Yard page to the rendering service and the rest to the web app, saying which name was typed; The Shed's name needs no edge.</text>

  <!-- ===================== Lane 1: the names ===================== -->
  <rect x="${L1.x}" y="92" width="${L1.w}" height="${lane1Bottom - 92}" class="lane"/>
  <text x="${L1.x + 20}" y="120" class="lane-title">The names, at Wix DNS</text>
  <text x="${L1.x + 20}" y="140" class="lane-sub">Four names at the edge, and The Shed's at the web app.</text>

  <!-- ===================== Lane 2: the edge ===================== -->
  <rect x="${L2.x}" y="92" width="${L2.w}" height="${lane2Bottom - 92}" class="lane"/>
  <text x="${L2.x + 20}" y="120" class="lane-title">One edge: the Netlify site theyard-edge</text>
  <text x="${L2.x + 20}" y="140" class="lane-sub">Terminates HTTPS, then proxies by Host. Two files in this repository.</text>

  <!-- ===================== Lane 3: Azure ===================== -->
  <rect x="${L3.x}" y="92" width="${L3.w}" height="${lane3Bottom - 92}" class="lane"/>
  <text x="${L3.x + 20}" y="120" class="lane-title">Azure: RG-THEYARD-SS, one App Service plan in westus3</text>
  <text x="${L3.x + 20}" y="140" class="lane-sub">Two web apps: every .NET process in one, the service in the other.</text>

${out.join('\n')}

  <text x="40" y="${H - 66}" class="caption">Each Yard site is one store's site by its name; a measurement names the other store for one request with X-Yard-Store (ADR: One web app for every .NET process).</text>
  <text x="40" y="${H - 46}" class="caption">The edge retires at the registrar transfer around the end of October 2026; both names then become two records at Cloudflare, and a Worker takes the rendering function with it.</text>
  <text x="40" y="${H - 26}" class="caption">Source: docs/images/two-sites.svg in the repository, drawn by docs/images/two-sites.mjs and redrawn when a name, a rule or a site changes.</text>
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
      <link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&display=swap" rel="stylesheet">
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

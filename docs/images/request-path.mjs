// Draws request-path.svg (ADR: One web app for every .NET process): every way a request reaches code, read
// from the files that route it. The names at Wix, the edge's two files, the one web app's SiteHosts and
// ShedMount, the store choice in Stores.cs and the rendering service's calls back. Run from the repo root:
//   node docs/images/request-path.mjs     writes docs/images/request-path.svg
// Redraw it when a rule, a name or a header changes; the picture is a claim.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1400;
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const out = [];

/** A box: a title, a monospace line and body lines; its height follows the lines. */
function box(x, y, w, title, mono, lines, kind = 'box') {
  const h = 58 + 17 * lines.length;
  out.push(`  <rect x="${x}" y="${y}" width="${w}" height="${h}" class="${kind}"/>`);
  out.push(`  <text x="${x + 14}" y="${y + 25}" class="title">${esc(title)}</text>`);
  out.push(`  <text x="${x + 14}" y="${y + 45}" class="mono">${esc(mono)}</text>`);
  lines.forEach((line, i) => out.push(`  <text x="${x + 14}" y="${y + 66 + 17 * i}" class="body">${esc(line)}</text>`));
  return { x, y, w, h, right: x + w, bottom: y + h, midX: x + w / 2, midY: y + h / 2 };
}

/** An arrow with an optional label at its middle. */
function arrow(d, label, lx, ly) {
  out.push(`  <path d="${d}" class="flow"/>`);
  if (label) out.push(`  <text x="${lx}" y="${ly}" class="label">${esc(label)}</text>`);
}

function lane(x, y, w, h, title, sub) {
  out.push(`  <rect x="${x}" y="${y}" width="${w}" height="${h}" class="lane"/>`);
  out.push(`  <text x="${x + 16}" y="${y + 28}" class="lane-title">${esc(title)}</text>`);
  out.push(`  <text x="${x + 16}" y="${y + 47}" class="lane-sub">${esc(sub)}</text>`);
}

// Column 1: the names.
lane(30, 100, 270, 560, 'The names, at Wix DNS', 'what the visitor typed');
const yard = box(46, 166, 238, 'theyard', 'CNAME theyard-edge.netlify.app', ['The live site.', 'No store named: Azure SQL.']);
const cosmos = box(46, 296, 238, 'theyard-cosmos', 'CNAME theyard-edge.netlify.app', ['The second site.', 'No store named: Cosmos DB.']);
const shed = box(46, 500, 238, 'theshed', 'CNAME app-theyard-one-ss-...', ['Bound to the web app with', 'App Service\'s certificate.', 'No edge in front of it.']);

// Column 2: the edge.
lane(330, 100, 300, 400, 'The edge: Netlify, theyard-edge', 'HTTPS ends here; the first matching rule wins');
const redirects = box(346, 166, 268, '1. edge/_redirects', 'a page, the bare path with its query', ['To the rendering service as', '/site/sql or /site/cosmos.', 'Bare domain and www: 301.']);
const toml = box(346, 326, 268, '2. netlify.toml', 'everything else, proxied', ['To the one web app, with', 'X-Yard-Host: the name typed.', 'The Cosmos DB rule first.']);

// The rendering service, under the edge.
lane(330, 530, 300, 230, 'The rendering service', 'APP-THEYARD-RENDER-SS, Node');
const render = box(346, 596, 268, 'render/server.mjs', 'YARD_SITES: both names, one address', ['Draws the page from the API,', 'naming the store on every read', '(X-Yard-Store, render/loaders.ts).']);

// Column 3: the one web app.
lane(660, 100, 710, 660, 'The one web app: APP-THEYARD-ONE-SS', 'one .NET process on the B2 plan; every .NET request lands here');
const hosts = box(680, 166, 330, 'Which name? SiteHosts.Of', 'api/TheYard.Api/Shared/Instance/SiteHosts.cs', ['The Host header first, when App Service', 'routed by one of the three names;', 'else X-Yard-Host, only if it names one.', 'Anything else: no name.']);
const shedApp = box(1040, 166, 310, 'The Shed\'s name: HostedSiteMount', 'Shared/Sites/HostedSiteMount.cs, ShedSite.cs', ['Handed over before TheYard\'s pipeline:', 'The Shed\'s own app from /app/shed,', 'its pipeline and controllers, no socket.', '503 if it could not start.'], 'box shed');
const yardApp = box(680, 366, 330, 'A Yard name: TheYard\'s pipeline', 'Program.cs, then the endpoints', ['Auth, limits, then the API and the', 'pages; every endpoint asks', 'CurrentBackend once per request.']);
const store = box(1040, 366, 310, 'Which store? Stores.cs', 'Backends, region backends', ['X-Yard-Store, when a request names one;', 'else the name\'s store;', 'no name (probes, first reads): SQL.']);
const sql = box(1040, 576, 145, 'Azure SQL', 'sqldb-theyard-ss-basic', ['The relational', 'store.'], 'box store');
const cos = box(1205, 576, 145, 'Cosmos DB', 'cosmos-theyard-ss', ['The document', 'store.'], 'box store');

// Arrows.
arrow(`M${yard.right} ${yard.midY} L${redirects.x} ${redirects.midY}`);
arrow(`M${cosmos.right} ${cosmos.midY} L${redirects.x} ${redirects.midY + 20}`);
arrow(`M${cosmos.right} ${cosmos.midY + 10} L${toml.x} ${toml.midY}`);
arrow(`M${redirects.midX} ${redirects.bottom} L${redirects.midX} ${toml.y}`, 'no page rule', redirects.midX + 8, (redirects.bottom + toml.y) / 2 + 4);
// A page leaves the edge down its left gutter to the rendering service.
arrow(`M${redirects.x} ${redirects.bottom - 14} H${redirects.x - 10} V${render.y + 30} H${render.x}`, 'a page', redirects.x + 2, 470);
// The rest reaches the web app: the edge's proxy, The Shed's own name, and the renderer calling back.
arrow(`M${toml.right} ${toml.midY} H638 V${hosts.y + 56} H${hosts.x}`);
arrow(`M${shed.right} 515 H645 V${hosts.bottom - 32} H${hosts.x}`, 'Host: theshed, no edge', 400, 522);
arrow(`M${render.right} ${render.midY} H652 V${hosts.bottom - 14} H${hosts.x}`, 'the renderer\'s reads: X-Yard-Host and X-Yard-Store', 668, render.midY + 4);
arrow(`M${hosts.right} ${hosts.y + 40} L${shedApp.x} ${shedApp.y + 40}`);
arrow(`M${hosts.midX} ${hosts.bottom} L${hosts.midX} ${yardApp.y}`, 'theyard, theyard-cosmos or none', hosts.midX + 8, (hosts.bottom + yardApp.y) / 2 + 4);
arrow(`M${yardApp.right} ${yardApp.midY} L${store.x} ${store.midY}`);
arrow(`M${store.midX - 60} ${store.bottom} L${sql.midX} ${sql.y}`, 'sql', sql.midX - 30, store.bottom + 30);
arrow(`M${store.midX + 60} ${store.bottom} L${cos.midX} ${cos.y}`, 'cosmos', cos.midX + 8, store.bottom + 30);

const H = 830;
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>How a request gets answered: three names at Wix, the Netlify edge's two files, the rendering service, and inside the one web app SiteHosts picking the name, ShedMount handing The Shed's name to The Shed, and Stores picking Azure SQL or Cosmos DB</title>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#4a6c96"/>
    </marker>
    <style>
      .box { fill: #ffffff; stroke: #7b7f8a; stroke-width: 1.5; rx: 10; }
      .shed { fill: #f3f7f2; stroke: #5d7f58; }
      .store { fill: #f6f3ec; stroke: #9a8459; }
      .lane { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; rx: 14; }
      .title { fill: #3f3a37; font-size: 15px; font-weight: 600; }
      .lane-title { fill: #3f3a37; font-size: 17px; font-weight: 700; }
      .lane-sub { fill: #62666f; font-size: 13px; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 10.5px; }
      .label { fill: #4a6c96; font-size: 12px; font-weight: 600; }
      .flow { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
    </style>
  </defs>
  <rect width="${W}" height="${H}" fill="#e9e6e7"/>
  <text x="30" y="44" class="heading">How a request gets answered: three names, one web app, two stores</text>
  <text x="30" y="68" class="caption">Read left to right. The name a visitor typed decides everything after it: The Shed or TheYard, and for TheYard, which store answers when the request names none.</text>
${out.join('\n')}
  <text x="30" y="${H - 26}" class="caption">Source: docs/images/request-path.svg, drawn by docs/images/request-path.mjs from edge/_redirects, netlify.toml, SiteHosts.cs, ShedMount.cs, Stores.cs and render/loaders.ts.</text>
</svg>
`;
writeFileSync(join(here, 'request-path.svg'), svg, 'utf8');
console.log(`wrote ${join(here, 'request-path.svg')} (${W} by ${H})`);

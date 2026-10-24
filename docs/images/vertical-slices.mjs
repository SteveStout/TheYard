// Draws vertical-slices.svg (ADR: The onion cut into vertical slices): the rings as bands, outside at
// the top and inside at the bottom, with a slice cut through all of them as one dashed column, the
// Shared column beside the slices, and one request followed through one slice. Run from the repo root:
//   node docs/images/vertical-slices.mjs     writes docs/images/vertical-slices.svg
// Redraw it when a ring, a slice or a rule changes; the picture is a claim the tests hold.
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1400;
const H = 1010;
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const out = [];

// The host's Shared concerns, read from ADR-114's table rather than typed here, so a new concern is drawn
// the day its row is written (ADR: The host root holds one file).
const concerns = [...readFileSync(join(here, '..', 'decisions', 'ADR-114-the-host-root-holds-one-file.md'), 'utf8')
  .matchAll(/^\|\s*`Shared\/(\w+)\/`\s*\|/gm)].map((match) => match[1]);
/** The concern names wrapped at 38 characters, the width of the Shared column at the small size. */
const concernLines = concerns.reduce((lines, name) => {
  const last = lines.at(-1);
  if (last !== undefined && `${last}, ${name}`.length <= 38) lines[lines.length - 1] = `${last}, ${name}`;
  else lines.push(name);
  return lines;
}, []);

/** A ring drawn as a band across the page: its title, its one-line job, and the band's box. */
function band(y, h, cls, title, job) {
  out.push(`  <rect x="30" y="${y}" width="1100" height="${h}" class="${cls}"/>`);
  out.push(`  <text x="46" y="${y + 24}" class="band-title">${esc(title)}</text>`);
  out.push(`  <text x="46" y="${y + 42}" class="band-sub">${esc(job)}</text>`);
  return { y, h, bottom: y + h };
}

/** A slice: one dashed column through every band, titled above it. */
function slice(x, w, title) {
  out.push(`  <rect x="${x}" y="96" width="${w}" height="538" class="slice"/>`);
  out.push(`  <text x="${x + w / 2}" y="88" class="slice-title" text-anchor="middle">${esc(title)}</text>`);
  return { x, w, midX: x + w / 2 };
}

/** Lines of body text at a column, one per file the slice owns in that ring. */
function files(x, y, lines, cls = 'body') {
  lines.forEach((line, i) => out.push(`  <text x="${x}" y="${y + 16 * i}" class="${cls}">${esc(line)}</text>`));
}

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

// The rings, as bands. Outside at the top, inside at the bottom; references point down and never up.
const api = band(100, 122, 'ring0', 'TheYard.Api, the host', 'Features/<Slice>/Module.cs: Register and Map; Program.cs alone at the root; everything shared in Shared/<Concern>/');
const infra = band(232, 110, 'ring1', 'TheYard.Infrastructure and .Cosmos, the adapters', 'the same ports answered twice, relational and document, in Features/<Slice>/ or Shared/<Concern>/; only the mapped rows stay at the root');
const app = band(352, 110, 'ring2', 'TheYard.Application, the use cases', 'Features/<Slice>/: one handler per request, one HandleAsync; the auction, the stores and the kept history in Shared/<Concern>/');
const domain = band(472, 92, 'ring3', 'TheYard.Domain, the rules', 'pure functions and records, in Shared/Auction or the slice that owns the rule; no clock, no disk, no network');
band(574, 60, 'ring4', 'TheYard.Data, the records', 'zero dependencies, zero behaviour');

// The Shared column: what every slice may use and no slice owns.
out.push(`  <rect x="40" y="96" width="246" height="538" class="shared"/>`);
out.push(`  <text x="163" y="88" class="shared-title" text-anchor="middle">Shared/, the same in every ring</text>`);
// Thirteen pixels a line here, not sixteen, so every concern fits above the adapters' band.
concernLines.forEach((line, i) => out.push(`  <text x="52" y="${152 + 13 * i}" class="small">${esc(line)}</text>`));
files(52, 292, ['ContextFactory.cs, CosmosStore.cs', 'OutboundHttp.cs, one pool', 'AzureMetrics.cs, the host\'s'], 'small');
files(52, 412, ['StandingReader.cs, the reads', 'Ports.cs, IStoreDiagnostics', 'CardFrame.cs, CloudWire.cs'], 'small');
files(52, 532, ['Fnv1a.cs, AuctionSchedule.cs', 'CardTone.cs, the one tone rule'], 'small');

// The slices: the same folder name in every ring, cut through all of them.
// What each slice owns in each ring, by the real file names (vertical-slices-tests hold them to the tree):
// every ring has the folders now; a part two slices share sits in Shared/<Concern>/ and is drawn in the slice
// that leans on it most, its folder named beside it.
const bids = slice(300, 190, 'Features/Bids');
files(bids.x + 12, 160, ['Module.cs', 'BidEndpoints.cs, the wire too']);
files(bids.x + 12, 292, ['EfBidStore.Standing.cs', 'CosmosBidStore.Standing.cs']);
files(bids.x + 12, 412, ['PlaceBidHandler.cs', 'BidReadHandlers.cs', 'IBidStore in Ports.cs']);
files(bids.x + 12, 532, ['BidRules.cs, StandingRules.cs', 'in Shared/Auction']);

const vehicles = slice(506, 190, 'Features/Vehicles');
files(vehicles.x + 12, 160, ['Module.cs', 'VehicleEndpoints.cs', 'VehicleWire.cs, in Shared/Wire']);
files(vehicles.x + 12, 292, ['EfVehicleCatalogue.cs', 'CosmosVehicleCatalogue.cs']);
files(vehicles.x + 12, 412, ['VehicleHandlers.cs', 'IVehicleCatalogue in Ports.cs']);
files(vehicles.x + 12, 532, ['VehicleFilter.cs and', 'VehicleOrdering.cs']);

const accounts = slice(712, 190, 'Features/Accounts');
files(accounts.x + 12, 160, ['Module.cs', 'AccountEndpoints.cs', 'AccountEndpoints.SignIn.cs']);
files(accounts.x + 12, 292, ['Features/Accounts/', 'IdentityAccountStore.cs', 'CosmosUserStore.cs']);
files(accounts.x + 12, 412, ['SessionHandlers.cs', 'ResetHandlers.cs', 'AccountPorts.cs']);
files(accounts.x + 12, 532, ['(no rule of its own;', 'the session is Application\'s)']);

const cloud = slice(918, 196, 'Features/Admin/Cloud');
files(cloud.x + 12, 160, ['Module.cs', 'CloudEndpoints.cs', 'SqlCard.cs and every reader']);
files(cloud.x + 12, 292, ['YardModelFacts.cs', 'the readers\' requests go', 'out through OutboundHttp']);
files(cloud.x + 12, 412, ['ReadCloudCardHandler.cs', 'ICloudCards, beside it']);
files(cloud.x + 12, 532, ['CardTone.cs, in Shared/Cards']);
out.push(`  <text x="${cloud.midX}" y="652" class="label" text-anchor="middle">and Admin/Plan, Admin/Operations, Docs, Health, Visitors, each a column like these</text>`);

// The direction of every reference, drawn once beside the bands.
arrow(`M1148 ${api.y + 60} V${infra.y + 50}`);
arrow(`M1148 ${infra.y + 60} V${app.y + 50}`);
arrow(`M1148 ${app.y + 60} V${domain.y + 40}`);
arrow(`M1148 ${domain.y + 50} V604`);
out.push(`  <text x="1160" y="330" class="label">inward</text>`);
out.push(`  <text x="1160" y="346" class="label">only</text>`);

// The rules that hold the picture, on the right.
out.push(`  <rect x="1200" y="100" width="170" height="534" class="rules"/>`);
out.push(`  <text x="1214" y="124" class="title">Held by the gate</text>`);
files(1214, 148, ['the ten ring rules', '', 'a slice depends on its', 'own folders and Shared/', '', 'every slice has a Module;', 'Program.cs maps nothing', '', 'one handler, one', 'HandleAsync, ports by', 'constructor', '', 'no RequestServices,', 'Environment or', 'Configuration outside', 'Program and *Module', '', 'sites and stores are', 'lists; no host or store', 'literal in the host', '', 'only Infrastructure', 'opens a socket', '', 'no typed resource name,', 'SKU, price or date'], 'small');

// One request through one slice, read left to right.
out.push(`  <rect x="30" y="680" width="1340" height="290" class="lane"/>`);
out.push(`  <text x="46" y="708" class="lane-title">One request, through one slice</text>`);
out.push(`  <text x="46" y="727" class="lane-sub">POST /api/vehicles/{id}/bids. Nothing on this path reads RequestServices, Environment or configuration; the store was chosen once, in Shared, and handed in.</text>`);
const edge = box(46, 748, 196, 'The edge, the host', 'X-Yard-Host, X-Yard-Store', ['The name and the store', 'the request carries.']);
const pipe = box(262, 748, 196, 'Shared pipeline', 'Shared/RequestPipeline.cs', ['Timing, forwarded headers,', 'problems, the warm store.']);
const mod = box(478, 748, 196, 'Bids/Module.Map', 'Features/Bids/Module.cs', ['The one place this route', 'is mapped.'], 'box slicebox');
const ep = box(694, 748, 196, 'BidEndpoints.PlaceBid', 'Features/Bids/BidEndpoints.cs', ['Binds the wire, calls the', 'handler, writes the answer.'], 'box slicebox');
const handler = box(910, 748, 206, 'PlaceBidHandler.HandleAsync', 'Features/Bids/PlaceBidHandler.cs', ['Ports by constructor: the', 'store, the clock. One method.'], 'box app');
const rules = box(1136, 748, 216, 'BidRules, then IBidStore', 'Domain/BidRules.cs, EfBidStore', ['The rule decides, pure; the', 'adapter writes, Ef or Cosmos.'], 'box domain');
arrow(`M${edge.right} ${edge.midY} L${pipe.x} ${pipe.midY}`);
arrow(`M${pipe.right} ${pipe.midY} L${mod.x} ${mod.midY}`);
arrow(`M${mod.right} ${mod.midY} L${ep.x} ${ep.midY}`);
arrow(`M${ep.right} ${ep.midY} L${handler.x} ${handler.midY}`);
arrow(`M${handler.right} ${handler.midY} L${rules.x} ${rules.midY}`);
out.push(`  <text x="46" y="900" class="body">The slice's tests: BidHandlerTests gives the handler a fake IBidStore and a fixed clock and asserts every outcome BidRules can reach; BidEndpointStatusTests is one integration case per status code.</text>`);
out.push(`  <text x="46" y="920" class="body">Adding a slice is one folder in the host and one in Application, and one Module; nothing in Program.cs changes. Removing one is deleting those folders; the gate says what else referred to it.</text>`);
out.push(`  <text x="46" y="940" class="body">Until 1.0.4.0 the host was sliced by kind instead: Endpoints/, Composition/, Cloud/ and ninety files at the root. The rings were held; the slices were not, and a feature was spread across all four folders.</text>`);

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>The onion cut into vertical slices: the five rings as bands with references pointing inward, four slices cut through every ring, each a folder in the host and in Application and its adapters and rules at the inner rings' roots, the Shared column beside them, the rules the gate holds, and one bid followed through one slice</title>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#4a6c96"/>
    </marker>
    <style>
      .ring0 { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; rx: 14; }
      .ring1 { fill: #ece9ea; stroke: #7b7f8a; stroke-width: 1.5; rx: 14; }
      .ring2 { fill: #f7f5f6; stroke: #7b7f8a; stroke-width: 1.5; rx: 14; }
      .ring3 { fill: #efeeea; stroke: #7b7f8a; stroke-width: 1.5; rx: 14; }
      .ring4 { fill: #e6e4df; stroke: #7b7f8a; stroke-width: 1.5; rx: 14; }
      .slice { fill: none; stroke: #9a8459; stroke-width: 2; stroke-dasharray: 8 5; rx: 12; }
      .shared { fill: none; stroke: #7b7f8a; stroke-width: 1.5; stroke-dasharray: 3 4; rx: 12; }
      .shared-title { fill: #62666f; font-size: 13px; font-weight: 700; }
      .rules { fill: #ffffff; stroke: #7b7f8a; stroke-width: 1.5; rx: 12; }
      .lane { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; rx: 14; }
      .box { fill: #ffffff; stroke: #7b7f8a; stroke-width: 1.5; rx: 10; }
      .slicebox { fill: #fbf7ee; stroke: #9a8459; }
      .app { fill: #f7f5f6; stroke: #7b7f8a; }
      .domain { fill: #efeeea; stroke: #7b7f8a; }
      .band-title { fill: #3f3a37; font-size: 16px; font-weight: 700; }
      .band-sub { fill: #62666f; font-size: 12.5px; }
      .slice-title { fill: #7a6535; font-size: 13px; font-weight: 700; }
      .lane-title { fill: #3f3a37; font-size: 17px; font-weight: 700; }
      .lane-sub { fill: #62666f; font-size: 13px; }
      .title { fill: #3f3a37; font-size: 15px; font-weight: 600; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .small { fill: #5e5653; font-size: 11.5px; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 10.5px; }
      .label { fill: #4a6c96; font-size: 12px; font-weight: 600; }
      .flow { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
    </style>
  </defs>
  <rect width="${W}" height="${H}" fill="#e9e6e7"/>
  <text x="30" y="44" class="heading">The onion cut into vertical slices: five rings, one folder name per feature in every ring</text>
  <text x="30" y="68" class="caption">Read top to bottom for the rings, which point inward as they always did; read a dashed column for one feature, which now lives in one place in each ring and nowhere else.</text>
${out.join('\n')}
  <text x="30" y="${H - 22}" class="caption">Source: docs/images/vertical-slices.svg, drawn by docs/images/vertical-slices.mjs from the Features/ folders, OnionTests.cs and SliceTests.cs. The gate holds every claim on it.</text>
</svg>
`;
writeFileSync(join(here, 'vertical-slices.svg'), svg, 'utf8');
console.log(`wrote ${join(here, 'vertical-slices.svg')} (${W} by ${H})`);

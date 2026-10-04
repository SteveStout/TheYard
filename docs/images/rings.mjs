// Draws rings.svg, the onion this codebase is built as, in the style of infrastructure.svg: each
// ring a rounded box inside the one around it, what lives in it, and beside it the tests that fail
// the gate when a dependency points the wrong way. Run from the repo root:
//   node docs/images/rings.mjs          writes docs/images/rings.svg
//   node docs/images/rings.mjs --png    also renders rings.png at 2x in Chrome
// Redraw it when a ring gains or loses a part; the picture is a claim about the code.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1400;
const H = 860;

const STYLE = `
      .ring0 { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; }
      .ring1 { fill: #ece9ea; stroke: #7b7f8a; stroke-width: 1.5; }
      .ring2 { fill: #f7f5f6; stroke: #7b7f8a; stroke-width: 1.5; }
      .ring3 { fill: #ffffff; stroke: #536786; stroke-width: 2; }
      .panel { fill: #ffffff; stroke: #7b7f8a; stroke-width: 1.5; }
      .title { fill: #3f3a37; font-size: 16px; font-weight: 700; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11.5px; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .test { fill: #25663a; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11.5px; }
      .flow { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .flow-label { fill: #4a6c96; font-size: 12px; font-weight: 500; }
      .side { stroke: #4a6c96; stroke-width: 2; fill: none; stroke-dasharray: 6 4; marker-end: url(#arrow); }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
`;

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const out = [];
const text = (x, y, cls, s) => out.push(`  <text x="${x}" y="${y}" class="${cls}">${esc(s)}</text>`);

/** One ring: a box, its name, the project it is, and a line or two on what lives there. */
function ring(cls, x, y, w, h, name, project, lines) {
  out.push(`  <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="18" class="${cls}"/>`);
  text(x + 20, y + 28, 'title', name);
  text(x + 20, y + 48, 'mono', project);
  lines.forEach((line, i) => text(x + 20, y + 68 + 17 * i, 'body', line));
}

ring('ring0', 40, 100, 880, 660, 'Outside: the host and the adapters', 'api/TheYard.Api', [
  'The host reads a request, asks Application and writes the answer (Endpoints/); Composition/ wires it all.',
  'The adapters implement the ports: EF Core over Azure SQL Database or SQLite, and the Cosmos DB SDK.',
]);
// The two adapters share the outer ring with the host. The one sideways arrow: the Cosmos DB adapter
// borrows the relational adapter's shared user and nothing else, which OnionTests holds.
text(190, 148, 'mono', 'api/TheYard.Infrastructure');
text(580, 148, 'mono', 'api/TheYard.Infrastructure.Cosmos');
out.push(`  <path d="M570 144 L384 144" class="side"/>`);
text(433, 135, 'flow-label', 'YardUser only');
ring('ring1', 90, 230, 780, 500, 'Application: the use cases and the ports', 'api/TheYard.Application', [
  "Auction composes a store's catalogue, bids and room in the order the rules need.",
  'It owns the ports: IVehicleSource, IPhotoManifestSource, IBidStore, IActivityStore and the rest.',
]);
ring('ring2', 140, 380, 680, 320, 'Domain: the rules', 'api/TheYard.Domain', [
  'BidRules, AuctionSchedule, AuctionClock, StandingRules, VehicleFilter, VehicleOrdering,',
  'VehicleSearchIndex, PhotoGallery and the Fnv1a hash the schedule is derived from.',
  'Pure functions of their inputs and the clock they are handed. No store, no web, no clock of its own.',
]);
ring('ring3', 190, 530, 580, 140, "Data: the dataset's shapes", 'api/TheYard.Data', [
  'Vehicle and PhotoEntry, exactly as data/vehicles.json has them.',
  'No behaviour and no dependencies: every ring may read it, and it reads nothing.',
]);

// The arrows: every dependency points inward, through the gap between one ring's words and the next ring.
const arrows = [
  [700, 192, 700, 226, 'uses'],
  [700, 342, 700, 376, 'uses'],
  [700, 492, 700, 526, 'uses'],
];
for (const [x1, y1, x2, y2, label] of arrows) {
  out.push(`  <path d="M${x1} ${y1} L${x2} ${y2}" class="flow"/>`);
  text(x1 + 10, y1 + 22, 'flow-label', label);
}

// The panel of tests that hold the rings.
out.push(`  <rect x="960" y="100" width="400" height="660" rx="14" class="panel"/>`);
text(980, 128, 'title', 'Held by the gate');
text(980, 148, 'mono', 'api/TheYard.Tests/OnionTests.cs (NetArchTest)');
const TESTS = [
  'Data_depends_on_nothing_outside_the_BCL',
  'Domain_depends_on_nothing_outside_the_BCL_and_Data',
  'Domain_never_reaches_the_disk_the_network_',
  '    or_the_clock',
  'Application_depends_only_on_Domain_Data_and_the_BCL',
  'Application_never_reaches_the_disk_or_the_network',
  'The_document_store_adapter_borrows_only_',
  '    the_shared_user_from_the_relational_one',
  'Endpoints_never_reach_a_store_or_',
  '    the_service_container',
  'Endpoints_reach_the_auction_only_through_',
  '    the_Application_ring',
  'The_host_never_queries_a_database_itself',
  'Services_are_registered_only_in_the_composition_root',
];
TESTS.forEach((t, i) => text(t.startsWith(' ') ? 1000 : 980, 186 + 24 * i, 'test', t.trim()));
const NOTES = [
  'Each one reads the compiled assemblies, so a',
  'reference in a method body, a generic argument or',
  'a fully written name fails the gate the same way',
  'a using line would. A failure names the type and',
  'the dependency it found.',
  '',
  'Beside the onion, and outside these rules:',
  'TheYard.Database (the SQL schema), the SQLite',
  'migrations, the Experiment console and the tests.',
];
NOTES.forEach((t, i) => text(980, 534 + 19 * i, 'body', t));

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>TheYard's rings: Data at the centre, then Domain, then Application, with the host and the adapters outside, every dependency pointing inward</title>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#4a6c96"/>
    </marker>
    <style>${STYLE}    </style>
  </defs>

  <rect width="${W}" height="${H}" fill="#e9e6e7"/>
  <text x="40" y="48" class="heading">TheYard's rings: every dependency points inward</text>
  <text x="40" y="72" class="caption">Each box is a ring. The inner three are one project each; the outer one holds the host and both adapters. A ring may use the rings inside it and never one outside it.</text>

${out.join('\n')}

  <text x="40" y="${H - 50}" class="caption">The front end keeps the same shape in src/: components use hooks, hooks use lib, and src/lib imports nothing from React.</text>
  <text x="40" y="${H - 28}" class="caption">Source: docs/images/rings.svg in the repository, drawn by docs/images/rings.mjs and redrawn when a ring changes.</text>
</svg>
`;

const svgPath = join(here, 'rings.svg');
writeFileSync(svgPath, svg, 'utf8');
console.log(`wrote ${svgPath} (${W} by ${H})`);

if (process.argv.includes('--png')) {
  const { chromium } = await import('@playwright/test');
  const browser = await chromium.launch({ channel: 'chrome' });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 2 });
  await page.setContent(`<!doctype html><html><head><style>html,body{margin:0;background:#e9e6e7}</style></head><body>${svg}</body></html>`);
  await page.waitForTimeout(500);
  const pngPath = join(here, 'rings.png');
  await page.locator('svg').screenshot({ path: pngPath, type: 'png' });
  await browser.close();
  console.log(`wrote ${pngPath}`);
}

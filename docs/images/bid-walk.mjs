// Draws bid-walk.svg: one bid, POST /api/vehicles/{id}/bids, followed through the rings in the
// order the code runs it. The rings are laid out left to right, outside to inside, in the style of
// rings.svg: the host and the adapters in the outer ring, then Application, then Domain. Each
// numbered arrow is one call the code makes, and each box names the file that makes it, so a
// reader can open the file beside the picture. Run from the repo root:
//   node docs/images/bid-walk.mjs        writes docs/images/bid-walk.svg
// Redraw it when the bid's path changes; the picture is a claim about the code.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1400;
const H = 930;

// The same classes as rings.svg, so the two drawings read as one set.
const STYLE = `
      .ring0 { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; }
      .ring1 { fill: #ece9ea; stroke: #7b7f8a; stroke-width: 1.5; }
      .ring2 { fill: #f7f5f6; stroke: #7b7f8a; stroke-width: 1.5; }
      .part { fill: #ffffff; stroke: #536786; stroke-width: 1.5; }
      .port { fill: #ffffff; stroke: #25663a; stroke-width: 2; stroke-dasharray: 7 4; }
      .title { fill: #3f3a37; font-size: 16px; font-weight: 700; }
      .name { fill: #3f3a37; font-size: 14px; font-weight: 700; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11.5px; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .test { fill: #25663a; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11.5px; }
      .flow { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .back { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .runtime { stroke: #4a6c96; stroke-width: 2; fill: none; stroke-dasharray: 6 4; marker-end: url(#arrow); }
      .implements { stroke: #25663a; stroke-width: 2; fill: none; marker-end: url(#arrow-green); }
      .flow-label { fill: #4a6c96; font-size: 12px; font-weight: 500; }
      .green-label { fill: #25663a; font-size: 12px; font-weight: 500; }
      .step { fill: #4a6c96; }
      .step-text { fill: #ffffff; font-size: 12px; font-weight: 700; }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
`;

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const out = [];
const text = (x, y, cls, s) => out.push(`  <text x="${x}" y="${y}" class="${cls}">${esc(s)}</text>`);
const box = (cls, x, y, w, h, r = 14) =>
  out.push(`  <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" class="${cls}"/>`);

/** A ring as a column: its box, its name and the project it is. */
function ring(cls, x, w, name, project) {
  box(cls, x, 110, w, 660, 18);
  text(x + 20, 138, 'title', name);
  text(x + 20, 158, 'mono', project);
}

/** A part inside a ring: a white box, the type or method, the file, then a line or two on what it does. */
function part(cls, x, y, w, h, name, file, lines) {
  box(cls, x, y, w, h);
  text(x + 16, y + 24, 'name', name);
  text(x + 16, y + 42, 'mono', file);
  lines.forEach((line, i) => text(x + 16, y + 62 + 17 * i, 'body', line));
}

/** A numbered step: a filled circle with the number in it, where the arrow for that call starts. */
function step(n, x, y) {
  out.push(`  <circle cx="${x}" cy="${y}" r="11" class="step"/>`);
  out.push(`  <text x="${x}" y="${y + 4}" class="step-text" text-anchor="middle">${n}</text>`);
}

/** A straight arrow from one point to another. */
function arrow(cls, x1, y1, x2, y2) {
  out.push(`  <path d="M${x1} ${y1} L${x2} ${y2}" class="${cls}"/>`);
}

// The three columns, outside to inside, with room between them for the numbered calls.
ring('ring0', 40, 400, 'Outside: the host and the adapters', 'api/TheYard.Api and the two adapter projects');
ring('ring1', 520, 400, 'Application: the use cases and the port', 'api/TheYard.Application');
ring('ring2', 1000, 360, 'Domain: the rules', 'api/TheYard.Domain');

// The host: where the request arrives and the answer leaves.
part('part', 60, 190, 360, 250, 'BidEndpoints.HandleBid', 'Endpoints/BidEndpoints.cs', [
  'POST /api/vehicles/{id}/bids, signed in.',
  'Is this session on this store? If not, 401.',
  'Does the vehicle exist (Auction.Find)? If not, 404.',
  'Reads the server clock once: Clocks.Now().',
  'Hands the vehicle, the amount and that clock in.',
  '',
  'Writes the answer: 400 with the reason, or 200',
  'with the bid and the vehicle as it now stands.',
]);

// The adapters: where the bid is kept, one per store, both behind the same port.
part('part', 60, 570, 360, 180, 'EfBidStore or CosmosBidStore', 'EfSources.cs and CosmosSources.cs', [
  'Each implements IBidStore against one store.',
  'EF Core: one row per buyer per vehicle, in',
  'Azure SQL Database or SQLite.',
  "Cosmos DB SDK: one document in the buyer's partition.",
  'Composition/StoreRegistration.cs picks which.',
]);

// Application: the use case, then the service that holds everybody's bids, then the port.
part('part', 540, 190, 360, 100, 'Auction.PlaceBidAsync', 'Auction.cs', [
  'The one class endpoints ask about the auction.',
  'Passes the bid to the service that keeps bids.',
]);
part('part', 540, 340, 360, 170, 'BidService.PlaceBidAsync', 'BidService.Bidding.cs', [
  'Takes the gate: one bid at a time.',
  "Folds everybody's bids into the vehicle, so the",
  'rules see the price as it stands.',
  'Asks Domain, then on yes writes the store first',
  'and memory second.',
]);
part('port', 540, 570, 360, 180, 'IBidStore, the port', 'Ports.cs', [
  'Declared here, in the inner ring: the shape of',
  'what Application needs, and nothing about SQL',
  'or Cosmos DB.',
  'LoadAsync, SaveAsync(userId, vehicleId, state),',
  'ClearAsync(userId).',
]);

// Domain: the decision, a pure function of what it is handed.
part('part', 1020, 340, 320, 210, 'BidRules.ResolveBid', 'BidRules.cs', [
  'Sold to anybody already? Refused.',
  'Live and at or above buy-now? Won,',
  'at the buy-now price.',
  'Not started, or ended? Refused.',
  'Below the minimum next bid? Refused.',
  'Otherwise accepted, as a BidOutcome.',
  'No store, no web, no clock of its own.',
]);
text(1020, 600, 'test', 'Held by OnionTests:');
text(1020, 620, 'test', 'Domain_never_reaches_the_disk_');
text(1040, 638, 'test', 'the_network_or_the_clock');
text(1020, 662, 'test', 'Endpoints_reach_the_auction_only_');
text(1040, 680, 'test', 'through_the_Application_ring');

// The calls, numbered in the order they run; the number sits beside the start of its arrow.
arrow('flow', 424, 225, 536, 225);
step(1, 480, 207);
arrow('flow', 720, 294, 720, 336);
step(2, 700, 315);
arrow('flow', 904, 400, 1016, 400);
step(3, 960, 382);
arrow('back', 1016, 470, 904, 470);
step(4, 960, 452);
arrow('flow', 720, 514, 720, 566);
step(5, 700, 540);
arrow('runtime', 536, 630, 424, 630);
step(6, 480, 612);
text(446, 652, 'flow-label', 'runs it');
arrow('implements', 424, 710, 536, 710);
text(442, 732, 'green-label', 'implements');
arrow('back', 536, 270, 424, 270);
step(7, 480, 252);

// The key under the columns, one line per call.
const KEY = [
  [1, 'The endpoint asks Application, with the server clock as a value.'],
  [2, 'Auction hands the bid to BidService, which keeps everybody\'s bids.'],
  [3, 'BidService asks Domain whether the bid stands.'],
  [4, 'Domain answers with a BidOutcome and touches nothing else.'],
  [5, 'On yes, BidService saves through IBidStore, the port it owns.'],
  [6, 'The call reaches whichever adapter Composition wired in at startup.'],
  [7, 'The outcome travels back out and the endpoint writes the answer.'],
];
KEY.forEach(([n, line], i) => {
  const x = i < 4 ? 40 : 720;
  const y = 806 + 22 * (i % 4);
  step(n, x + 11, y - 4);
  text(x + 30, y, 'body', line);
});

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>One bid through TheYard's rings: the endpoint asks Application, Application asks Domain, Domain decides, Application saves through the IBidStore port, an adapter writes the store, and the endpoint answers</title>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#4a6c96"/>
    </marker>
    <marker id="arrow-green" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#25663a"/>
    </marker>
    <style>${STYLE}    </style>
  </defs>

  <rect width="${W}" height="${H}" fill="#e9e6e7"/>
  <text x="40" y="48" class="heading">One bid through the rings</text>
  <text x="40" y="72" class="caption">Left to right is outside to inside. Solid arrows are calls the code makes in order; the dashed arrow is the one call that crosses outward, at runtime, through the port.</text>

${out.join('\n')}

  <text x="40" y="${H - 18}" class="caption">Source: docs/images/bid-walk.svg in the repository, drawn by docs/images/bid-walk.mjs and redrawn when the bid's path changes.</text>
</svg>
`;

const svgPath = join(here, 'bid-walk.svg');
writeFileSync(svgPath, svg, 'utf8');
console.log(`wrote ${svgPath} (${W} by ${H})`);

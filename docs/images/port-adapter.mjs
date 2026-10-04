// Draws port-adapter.svg: dependency inversion as this codebase does it, for the bid store. The
// Application ring declares IBidStore and the code that uses it; the two adapters in the outer ring
// implement it; the composition root, also outside, is the one place that names both sides and
// decides which adapter a store gets. Every arrow that crosses a ring points at the interface, which
// is the whole idea. Same classes as rings.svg and bid-walk.svg. Run from the repo root:
//   node docs/images/port-adapter.mjs     writes docs/images/port-adapter.svg
// Redraw it when the port, an adapter or the registration changes; the picture is a claim about the code.
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const W = 1400;
const H = 860;

const STYLE = `
      .ring0 { fill: #f4f2f3; stroke: #d8d3d4; stroke-width: 1; }
      .ring1 { fill: #ece9ea; stroke: #7b7f8a; stroke-width: 1.5; }
      .part { fill: #ffffff; stroke: #536786; stroke-width: 1.5; }
      .port { fill: #ffffff; stroke: #25663a; stroke-width: 2; stroke-dasharray: 7 4; }
      .title { fill: #3f3a37; font-size: 16px; font-weight: 700; }
      .name { fill: #3f3a37; font-size: 14px; font-weight: 700; }
      .mono { fill: #5e5653; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11.5px; }
      .body { fill: #5e5653; font-size: 12.5px; }
      .test { fill: #25663a; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11.5px; }
      .uses { stroke: #4a6c96; stroke-width: 2; fill: none; marker-end: url(#arrow); }
      .creates { stroke: #4a6c96; stroke-width: 2; fill: none; stroke-dasharray: 6 4; marker-end: url(#arrow); }
      .implements { stroke: #25663a; stroke-width: 2; fill: none; marker-end: url(#arrow-green); }
      .flow-label { fill: #4a6c96; font-size: 12px; font-weight: 500; }
      .green-label { fill: #25663a; font-size: 12px; font-weight: 500; }
      .heading { fill: #3f3a37; font-size: 22px; font-weight: 700; }
      .caption { fill: #62666f; font-size: 13px; }
`;

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const out = [];
const text = (x, y, cls, s) => out.push(`  <text x="${x}" y="${y}" class="${cls}">${esc(s)}</text>`);
const box = (cls, x, y, w, h, r = 14) =>
  out.push(`  <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" class="${cls}"/>`);

/** A ring as a region: its box, its name and the projects it is. */
function ring(cls, x, y, w, h, name, project) {
  box(cls, x, y, w, h, 18);
  text(x + 20, y + 28, 'title', name);
  text(x + 20, y + 48, 'mono', project);
}

/** A part inside a ring: a box, the type, the file, then what it does in a line or two. */
function part(cls, x, y, w, h, name, file, lines, mono = []) {
  box(cls, x, y, w, h);
  text(x + 16, y + 24, 'name', name);
  text(x + 16, y + 42, 'mono', file);
  lines.forEach((line, i) => text(x + 16, y + 62 + 17 * i, 'body', line));
  mono.forEach((line, i) => text(x + 16, y + 66 + 17 * lines.length + 17 * i, 'mono', line));
}

/** A path with an arrow head, and an optional label placed where it reads best. */
function arrow(cls, d, label, lx, ly, labelClass = 'flow-label') {
  out.push(`  <path d="${d}" class="${cls}"/>`);
  if (label) text(lx, ly, labelClass, label);
}

// The inner ring, on the left: the port and the code that uses it.
ring('ring1', 40, 110, 600, 600, 'Application: owns the interface', 'api/TheYard.Application');
part('part', 70, 190, 540, 130, 'BidService', 'BidService.cs, BidService.Bidding.cs', [
  'Holds everybody\'s bids and asks BidRules before any is kept.',
  'Receives its store through the constructor and never learns',
  'which store that is:',
], ['public BidService(IBidStore store)']);
part('port', 70, 380, 540, 170, 'IBidStore, the port', 'Ports.cs', [
  'The shape of what Application needs from somewhere to keep bids,',
  'in Application\'s own words, with no SQL and no Cosmos DB in it.',
], ['Task<IReadOnlyList<StoredBid>> LoadAsync();', 'Task SaveAsync(string userId, string vehicleId, BidState state);', 'Task ClearAsync(string userId);']);
part('part', 70, 590, 540, 90, 'NullBidStore', 'Ports.cs', [
  'The port wired to nothing: bids live in memory. The unit tests use it,',
  'and so does a store that has not come up yet.',
]);

// The outer ring, on the right: the two adapters and the composition root.
ring('ring0', 760, 110, 600, 600, 'Outside: implements the interface', 'api/TheYard.Infrastructure, .Infrastructure.Cosmos, .Api');
part('part', 790, 190, 540, 110, 'EfBidStore', 'Infrastructure/EfSources.cs', [
  'EF Core over Azure SQL Database or SQLite: one row per buyer per',
  'vehicle, replaced in place, three tries on a concurrency conflict.',
], ['public sealed class EfBidStore(...) : IBidStore']);
part('part', 790, 330, 540, 110, 'CosmosBidStore', 'Infrastructure.Cosmos/CosmosSources.cs', [
  "The Cosmos DB SDK: one document per buyer per vehicle, in the buyer's",
  'partition, so every write stays inside one partition.',
], ['public sealed class CosmosBidStore(CosmosStore store) : IBidStore']);
part('part', 790, 500, 540, 180, 'The composition root', 'Api/Composition/StoreRegistration.cs', [
  'The one place that names an adapter. It builds one backend per',
  'store at startup and hands each BidService its adapter:',
], [
  'new BidService(new EfBidStore(contexts))',
  'new BidService(new CosmosBidStore(store))',
  'new BidService(NullBidStore.Instance)',
  '// until the store attaches',
]);

// Implements: both adapters point at the port, inward, across the ring.
arrow('implements', 'M786 245 L700 245 L700 440 L614 440', 'implements', 650, 300, 'green-label');
arrow('implements', 'M786 385 L720 385 L720 470 L614 470', '', 0, 0);
// NullBidStore implements the same port from inside the ring.
arrow('implements', 'M340 586 L340 554', 'implements', 350, 576, 'green-label');
// Uses: BidService calls the port, inside the same ring.
arrow('uses', 'M340 324 L340 376', 'calls', 350, 356);
// Creates: the composition root builds the adapter and hands it to the constructor.
arrow('creates', 'M786 560 L740 560 L740 160 L614 160 L614 186', 'hands in the adapter', 628, 152);

// What holds it, under the rings.
text(40, 748, 'test', 'Held by OnionTests: Application_depends_only_on_Domain_Data_and_the_BCL,');
text(40, 768, 'test', 'Application_never_reaches_the_disk_or_the_network, Services_are_registered_only_in_the_composition_root,');
text(40, 788, 'test', 'Endpoints_never_reach_a_store_or_the_service_container.');

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif" font-size="14">
  <title>The inner ring owns the interface: Application declares IBidStore and uses it, EfBidStore and CosmosBidStore implement it from the outer ring, and the composition root decides which one each store gets</title>
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
  <text x="40" y="48" class="heading">The inner ring owns the interface</text>
  <text x="40" y="72" class="caption">Every green arrow crosses a ring and points inward, at IBidStore. Application compiles with neither adapter in it; only the composition root names both sides.</text>

${out.join('\n')}

  <text x="40" y="${H - 28}" class="caption">Source: docs/images/port-adapter.svg in the repository, drawn by docs/images/port-adapter.mjs and redrawn when the port or an adapter changes.</text>
</svg>
`;

const svgPath = join(here, 'port-adapter.svg');
writeFileSync(svgPath, svg, 'utf8');
console.log(`wrote ${svgPath} (${W} by ${H})`);

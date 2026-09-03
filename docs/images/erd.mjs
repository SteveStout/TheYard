// The entity relationship diagram, drawn rather than described.
//
// Same shape as dataflow.mjs and the same reason: the picture is generated from
// a table of facts, so a column that changes is a one-line edit and not a
// drawing exercise, and the file that is committed is the picture (ADR-020,
// every diagram opens on its own page).
//
// The authority for what is in here is api/TheBlock.Database, and a conformance
// test holds the Entity Framework model to those same .sql files
// (ADR: Data first, and the database in source control). This drawing is the
// third copy and the only one a person reads at a glance, so when it is wrong
// it is wrong in the most expensive place. Regenerate it with:
//
//   node docs/images/erd.mjs
//
// Palette: Urban slate (ADR-016).
import { writeFileSync } from 'node:fs';

const PALETTE = {
  ground: '#e9e6e7',
  panel: '#ffffff',
  laneFill: '#f4f2f3',
  line: '#7b7f8a',
  faint: '#d8d3d4',
  heading: '#3f3a37',
  body: '#5e5653',
  muted: '#62666f',
  accent: '#536786',
  brand: '#ab978c',
};

/** A table: its name, one line about what it is for, and its columns. */
const tables = [
  {
    key: 'vehicles',
    name: 'Vehicles',
    note: 'the 200-row seed catalogue, read whole in Seq order once at startup',
    x: 40,
    y: 150,
    width: 400,
    columns: [
      ['Id', 'nvarchar(64)', 'PK'],
      ['Seq', 'int', 'UK, clustered'],
      ['Vin', 'varchar(17)', 'ISO 3779'],
      ['Year', 'int', ''],
      ['Make', 'nvarchar(64)', ''],
      ['Model', 'nvarchar(64)', ''],
      ['Trim', 'nvarchar(64)', ''],
      ['BodyStyle', 'nvarchar(32)', ''],
      ['ExteriorColor', 'nvarchar(32)', ''],
      ['InteriorColor', 'nvarchar(32)', ''],
      ['Engine', 'nvarchar(128)', ''],
      ['Transmission', 'nvarchar(64)', ''],
      ['Drivetrain', 'nvarchar(16)', ''],
      ['OdometerKm', 'int', ''],
      ['FuelType', 'nvarchar(32)', ''],
      ['ConditionGrade', 'decimal(3,1)', 'exact, not float'],
      ['ConditionReport', 'nvarchar(1024)', ''],
      ['DamageNotes', 'nvarchar(max)', 'JSON array'],
      ['TitleStatus', 'nvarchar(32)', ''],
      ['Province', 'nvarchar(64)', ''],
      ['City', 'nvarchar(64)', ''],
      ['AuctionStart', 'datetime2(0)', 'local, to the second'],
      ['StartingBid', 'int', ''],
      ['ReservePrice', 'int', 'null: no reserve'],
      ['BuyNowPrice', 'int', 'null: no buy now'],
      ['Images', 'nvarchar(max)', 'JSON array'],
      ['SellingDealership', 'nvarchar(128)', ''],
      ['Lot', 'nvarchar(32)', ''],
      ['CurrentBid', 'int', 'null until bid on'],
      ['BidCount', 'int', ''],
    ],
  },
  {
    key: 'photos',
    name: 'Photos',
    note: 'the vendored stock-photo manifest, read whole in Seq order once',
    x: 490,
    y: 150,
    width: 360,
    columns: [
      ['File', 'nvarchar(128)', 'PK'],
      ['Seq', 'int', 'UK, clustered'],
      ['Style', 'nvarchar(32)', 'body-style pool'],
      ['Title', 'nvarchar(256)', 'source title'],
    ],
  },
  {
    key: 'bids',
    name: 'Bids',
    note: 'the only table that changes after startup',
    x: 490,
    y: 360,
    width: 360,
    columns: [
      ['UserId', 'nvarchar(128)', 'PK, FK'],
      ['VehicleId', 'nvarchar(64)', 'PK'],
      ['Amount', 'int', ''],
      ['BidCount', 'int', ''],
      ['WonBuyNow', 'bit', ''],
      ['AtMs', 'bigint', ''],
      ['RowVersion', 'rowversion', 'concurrency token'],
    ],
  },
  {
    key: 'users',
    name: 'AspNetUsers',
    note: 'ASP.NET Core Identity, plus the one column this application adds',
    x: 900,
    y: 150,
    width: 460,
    columns: [
      ['Id', 'nvarchar(128)', 'PK'],
      ['CreatedAtMs', 'bigint', "this application's addition"],
      ['UserName', 'nvarchar(256)', ''],
      ['NormalizedUserName', 'nvarchar(256)', 'UK, filtered'],
      ['Email', 'nvarchar(256)', ''],
      ['NormalizedEmail', 'nvarchar(256)', 'indexed'],
      ['EmailConfirmed', 'bit', ''],
      ['PasswordHash', 'nvarchar(max)', "Identity's, left alone"],
      ['SecurityStamp', 'nvarchar(max)', ''],
      ['ConcurrencyStamp', 'nvarchar(max)', ''],
      ['PhoneNumber', 'nvarchar(max)', ''],
      ['PhoneNumberConfirmed', 'bit', ''],
      ['TwoFactorEnabled', 'bit', ''],
      ['LockoutEnd', 'datetimeoffset', ''],
      ['LockoutEnabled', 'bit', ''],
      ['AccessFailedCount', 'int', ''],
    ],
  },
];

/** Identity's other six, which this application never writes to. */
const satellites = {
  x: 900,
  y: 620,
  width: 460,
  rows: [
    ['AspNetRoles', 'Id PK nvarchar(128), Name, NormalizedName UK'],
    ['AspNetUserRoles', 'UserId + RoleId PK, both FK, cascade'],
    ['AspNetUserClaims', 'Id PK identity, UserId FK, cascade'],
    ['AspNetRoleClaims', 'Id PK identity, RoleId FK, cascade'],
    ['AspNetUserLogins', 'LoginProvider + ProviderKey PK, UserId FK'],
    ['AspNetUserTokens', 'UserId + LoginProvider + Name PK, UserId FK'],
  ],
};

const ROW = 17;
const HEAD = 52;
const PAD = 10;

const height = (t) => HEAD + t.columns.length * ROW + PAD;

function tableSvg(t) {
  const h = height(t);
  const rows = t.columns
    .map(([name, type, note], i) => {
      const y = t.y + HEAD + i * ROW + 12;
      const key = note.startsWith('PK') || note.includes('PK');
      return [
        `  <text x="${t.x + 14}" y="${y}" class="${key ? 'col-key' : 'col'}">${name}</text>`,
        `  <text x="${t.x + t.width * 0.46}" y="${y}" class="type">${type}</text>`,
        note ? `  <text x="${t.x + t.width * 0.74}" y="${y}" class="note">${note}</text>` : '',
      ]
        .filter(Boolean)
        .join('\n');
    })
    .join('\n');
  return [
    `  <rect x="${t.x}" y="${t.y}" width="${t.width}" height="${h}" class="table"/>`,
    `  <rect x="${t.x}" y="${t.y}" width="${t.width}" height="30" class="table-head"/>`,
    `  <text x="${t.x + 14}" y="${t.y + 21}" class="table-name">${t.name}</text>`,
    `  <text x="${t.x + 14}" y="${t.y + 44}" class="table-note">${t.note}</text>`,
    rows,
  ].join('\n');
}

function satellitesSvg(s) {
  const h = HEAD + s.rows.length * (ROW + 3) + PAD;
  const rows = s.rows
    .map(([name, shape], i) => {
      const y = s.y + HEAD + i * (ROW + 3) + 12;
      return [
        `  <text x="${s.x + 14}" y="${y}" class="col-key">${name}</text>`,
        `  <text x="${s.x + 190}" y="${y}" class="note">${shape}</text>`,
      ].join('\n');
    })
    .join('\n');
  return [
    `  <rect x="${s.x}" y="${s.y}" width="${s.width}" height="${h}" class="table"/>`,
    `  <rect x="${s.x}" y="${s.y}" width="${s.width}" height="30" class="table-head"/>`,
    `  <text x="${s.x + 14}" y="${s.y + 21}" class="table-name">Identity's other six</text>`,
    `  <text x="${s.x + 14}" y="${s.y + 44}" class="table-note">Declared because IdentityDbContext expects them; this application writes to none of them.</text>`,
    rows,
  ].join('\n');
}

const byKey = Object.fromEntries(tables.map((t) => [t.key, t]));
const bids = byKey.bids;
const users = byKey.users;
const vehicles = byKey.vehicles;
const photos = byKey.photos;

const relationships = [
  // Bids.UserId to AspNetUsers.Id: the one real constraint.
  {
    d: `M${bids.x + bids.width} ${bids.y + 70} L${users.x} ${users.y + height(users) - 40}`,
    cls: 'rel',
    label: 'FK, ON DELETE CASCADE',
    lx: bids.x + bids.width + 12,
    ly: bids.y + 58,
  },
  // Vehicles to Bids: no constraint, and why.
  {
    d: `M${vehicles.x + vehicles.width} ${bids.y + 40} L${bids.x} ${bids.y + 40}`,
    cls: 'rel-absent',
    label: 'no foreign key: a bid names a synthetic id',
    lx: vehicles.x + vehicles.width + 6,
    ly: bids.y + 30,
  },
  // Vehicles to Photos: computed, never stored.
  {
    d: `M${vehicles.x + vehicles.width} ${photos.y + 40} L${photos.x} ${photos.y + 40}`,
    cls: 'rel-absent',
    label: 'no join table: the gallery is a hash of the vehicle id',
    lx: vehicles.x + vehicles.width + 6,
    ly: photos.y + 30,
  },
];

// Tall enough for the longest table plus the legend, and no taller: the picture
// opens on its own page and empty space at the bottom just means more scrolling
// before a phone reaches anything (ADR-020).
const canvasHeight = 880;
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1400" height="${canvasHeight}" viewBox="0 0 1400 ${canvasHeight}" font-family="Poppins, 'Segoe UI', system-ui, Arial, sans-serif">
  <title>TheYard's database: four tables this application owns, seven ASP.NET Core Identity brings, and the two relationships deliberately left unenforced</title>
  <defs>
    <marker id="rel-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="${PALETTE.accent}"/>
    </marker>
    <style>
      .table { fill: ${PALETTE.panel}; stroke: ${PALETTE.line}; stroke-width: 1.5; rx: 10; }
      .table-head { fill: ${PALETTE.laneFill}; stroke: none; rx: 10; }
      .table-name { fill: ${PALETTE.heading}; font-size: 15px; font-weight: 700; }
      .table-note { fill: ${PALETTE.muted}; font-size: 11.5px; }
      .col { fill: ${PALETTE.body}; font-size: 12px; }
      .col-key { fill: ${PALETTE.heading}; font-size: 12px; font-weight: 600; }
      .type { fill: ${PALETTE.accent}; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11px; }
      .note { fill: ${PALETTE.muted}; font-size: 10.5px; }
      .rel { stroke: ${PALETTE.accent}; stroke-width: 2; fill: none; marker-end: url(#rel-arrow); }
      .rel-absent { stroke: ${PALETTE.brand}; stroke-width: 2; fill: none; stroke-dasharray: 6 5; }
      .rel-label { fill: ${PALETTE.accent}; font-size: 11.5px; font-weight: 500; }
      .heading { fill: ${PALETTE.heading}; font-size: 22px; font-weight: 700; }
      .caption { fill: ${PALETTE.muted}; font-size: 13px; }
      .legend { fill: ${PALETTE.body}; font-size: 12px; }
    </style>
  </defs>

  <rect width="1400" height="${canvasHeight}" fill="${PALETTE.ground}"/>
  <text x="40" y="46" class="heading">TheYard's database, as api/TheBlock.Database declares it</text>
  <text x="40" y="70" class="caption">Hand-written DDL is the authority; Entity Framework maps to it and a conformance test fails the build if they disagree (ADR-039, ADR-040).</text>
  <text x="40" y="92" class="caption">Solid lines are constraints the database enforces. Dashed lines are relationships that exist in the application and deliberately not in the schema.</text>

${tables.map(tableSvg).join('\n\n')}

${satellitesSvg(satellites)}

${relationships
  .map(
    (r) =>
      `  <path d="${r.d}" class="${r.cls}"/>\n  <text x="${r.lx}" y="${r.ly}" class="rel-label">${r.label}</text>`
  )
  .join('\n')}

  <text x="40" y="${canvasHeight - 46}" class="legend">Every length was chosen from what the seed dataset actually holds, with headroom, and the reason sits beside the column in the .sql file.</text>
  <text x="40" y="${canvasHeight - 26}" class="legend">Identity's keys are nvarchar(128) and not its default 450, because 450 is 900 bytes and puts every composite key it takes part in over SQL Server's clustered index limit.</text>
</svg>
`;

writeFileSync(new URL('./erd.svg', import.meta.url), svg);
console.log('wrote erd.svg');

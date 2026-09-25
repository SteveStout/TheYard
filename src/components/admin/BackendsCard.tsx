/**
 * Backends, side by side (ADR: Backends, side by side): the two stores on the same
 * rows, or this container against its peer.
 */
import styles from '../AdminPanel.module.css';
import type {
  RouteTiming,
  StoreSummary,
  RouteCharge,
  Startup,
  SqlSummary,
  BackendMetrics,
  Metrics,
  Peer,
} from './types';
import { useRead, failed, About, ms, msAndRu } from './common';
import { type Column, DataTable } from './DataTable';

// #region comparison
/** The rows the comparison card puts side by side: the paths a visitor actually takes (ADR: Backends, side by side). */
const COMPARED_ROUTES: { route: string; label: string }[] = [
  { route: 'GET /api/vehicles', label: 'Listing page' },
  { route: 'GET /api/vehicles/{id}', label: 'Vehicle page' },
  { route: 'GET /api/facets', label: 'Filter values' },
  { route: 'POST /api/vehicles/{id}/bids', label: 'Bid write' },
  { route: 'POST /api/auth/login', label: 'Sign in' },
  { route: 'POST /api/auth/register', label: 'Register' },
  { route: 'POST /api/market/tick', label: 'Room tick' },
];

/** One column of the comparison: a store, wherever it runs, on the rows the card draws. */
type StoreColumn = {
  title: string;
  store: string;
  startup: Startup;
  requests: { window: number; p50_ms: number; p95_ms: number; by_route: RouteTiming[] };
  charges: RouteCharge[];
  storeOps: string;
};

function storeOpsLine(store: StoreSummary | null, sql: SqlSummary | null): string {
  if (store !== null) {
    return `p50 ${store.p50_ms} ms, p95 ${store.p95_ms} ms, ${store.ru_total} RU over ${store.window}, ${store.cross_partition} cross-partition`;
  }
  if (sql !== null) {
    return `p50 ${sql.p50_ms} ms, p95 ${sql.p95_ms} ms over ${sql.window} statements`;
  }
  return '';
}

/** A whole container's metrics as one column, which is what a peer answers with. */
function columnOf(title: string, m: Metrics): StoreColumn {
  const cosmos = m.store.store === 'Azure Cosmos DB';
  return {
    title,
    store: m.store.store,
    startup: m.startup,
    requests: m.requests,
    charges: cosmos ? m.store_by_route : [],
    storeOps: storeOpsLine(cosmos ? m.store : null, cosmos ? null : m.sql),
  };
}

/** One store of a container that runs more than one (ADR: One container, both stores). */
function columnOfBackend(backend: BackendMetrics, current: boolean): StoreColumn {
  return {
    title: current ? `${backend.store}, serving this visit` : backend.store,
    store: backend.store,
    startup: backend.startup,
    requests: backend.requests,
    charges: backend.store_by_route,
    storeOps: storeOpsLine(backend.store_metrics, backend.sql),
  };
}

/**
 * The columns the card compares. A container running both stores compares
 * them with each other, in one process, the current one first; a container
 * running one compares itself with its peer, whatever /api/admin/peer relayed,
 * and when it relayed nothing the column says why and the rest of the card
 * stands (ADR: Backends, side by side).
 */
function columns(
  mine: Metrics,
  peer: Peer | null
): { columns: StoreColumn[]; note: string | null } {
  const backends = mine.backends ?? [];
  if (backends.length > 1) {
    const current = backends.find((b) => b.store === mine.store.store) ?? backends[0];
    const ordered = [current, ...backends.filter((b) => b !== current)];
    return {
      columns: ordered.map((b) => columnOfBackend(b, b === current)),
      note: 'Both stores run in this container, so the two columns share a process, a region and a request ring; only the store differs.',
    };
  }
  const theirs = peer !== null && peer.reachable ? peer.metrics : null;
  const peerTitle =
    peer === null
      ? 'The peer could not be read'
      : !peer.configured
        ? 'No peer is configured on this container'
        : peer.reachable
          ? `The other site (${peer.metrics?.store.store ?? 'store unknown'})`
          : `The other site is not answering`;
  const peerNote = peer !== null && peer.configured && !peer.reachable ? peer.reason : null;
  const empty: StoreColumn = {
    title: peerTitle,
    store: '',
    startup: {
      ...mine.startup,
      ready_ms: null,
      schema_ms: 0,
      seed_ms: 0,
      seed_ru: null,
      catalogue_ms: null,
      bids_ms: null,
    },
    requests: { window: 0, p50_ms: 0, p95_ms: 0, by_route: [] },
    charges: [],
    storeOps: '',
  };
  return {
    columns: [
      columnOf(`This site (${mine.store.store})`, mine),
      theirs === null ? empty : columnOf(peerTitle, theirs),
    ],
    note: peerNote,
  };
}

/** A line of the comparison: what is measured, and its reading in each store's column. */
type ComparedRow = { label: string; cells: (column: StoreColumn) => string };

function Comparison({ mine, peer }: { mine: Metrics; peer: Peer | null }) {
  const { columns: cols, note } = columns(mine, peer);
  const blank = (column: StoreColumn) => column.store === '';

  const routeCell = (column: StoreColumn, route: string): string => {
    if (blank(column)) return '';
    const timing = column.requests.by_route.find((r) => r.route === route);
    if (timing === undefined) return 'not seen yet';
    const charge = column.charges.find((r) => r.route === route);
    const time = `p50 ${timing.p50_ms} ms, p95 ${timing.p95_ms} ms (${timing.count})`;
    return charge === undefined ? time : `${time} · ${charge.ru_p50} RU`;
  };

  const rows: ComparedRow[] = [
    { label: 'Store', cells: (c) => c.store },
    {
      label: 'Cold start, process start to ready',
      cells: (c) => (blank(c) ? '' : ms(c.startup.ready_ms)),
    },
    {
      label: 'Store check (schema or containers)',
      cells: (c) => (blank(c) ? '' : ms(c.startup.schema_ms)),
    },
    {
      label: 'Seed, first boot only',
      cells: (c) => (blank(c) ? '' : msAndRu(c.startup.seed_ms, c.startup.seed_ru)),
    },
    { label: 'Catalogue load', cells: (c) => (blank(c) ? '' : ms(c.startup.catalogue_ms)) },
    { label: 'Bids load', cells: (c) => (blank(c) ? '' : ms(c.startup.bids_ms)) },
    {
      label: 'Requests, all paths',
      cells: (c) =>
        blank(c)
          ? ''
          : `p50 ${c.requests.p50_ms} ms, p95 ${c.requests.p95_ms} ms (${c.requests.window})`,
    },
    ...COMPARED_ROUTES.map((entry) => ({
      label: entry.label,
      cells: (c: StoreColumn) => routeCell(c, entry.route),
    })),
    { label: 'Store operations, this window', cells: (c) => c.storeOps },
  ];

  return (
    <DataTable
      label="Backends side by side"
      rows={rows}
      rowKey={(row) => row.label}
      columns={[
        { name: 'Measured', cell: (row) => row.label },
        ...cols.map((column): Column<ComparedRow> => ({
          name: column.title,
          mono: true,
          cell: (row) => row.cells(column),
        })),
      ]}
      note={note === null ? undefined : { content: note, testId: 'peer-note' }}
    />
  );
}
// #endregion comparison

export default function BackendsCard({ tick }: { tick: number }) {
  const metrics = useRead<Metrics>('/api/admin/metrics', tick);
  const peer = useRead<Peer>('/api/admin/peer', tick);
  return (
    <>
      {/* #region backends-card */}
      <article className={`${styles.wide} op-glass`} data-testid="backends-card">
        <h2 className={styles.cardTitle}>Backends, side by side</h2>
        <About>
          The two stores on the same rows: how long each took to come up, what the seed cost, and
          how long the things a visitor does take on each, with the request charge beside every
          number the document store can put one on. A container that runs both stores compares them
          with each other, in one process, the one serving this visit first; a container that runs
          one compares itself with its peer, read through its own API with two and a half seconds of
          patience, so a peer that is down is a sentence here and not a hang. Cold start and seed
          are measured on each store at its own start; the rest is the last few hundred requests
          each has served.
        </About>
        {metrics === null || peer === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : metrics === 'failed' ? (
          failed('the comparison')
        ) : (
          <Comparison mine={metrics} peer={peer === 'failed' ? null : peer} />
        )}
      </article>
      {/* #endregion backends-card */}
    </>
  );
}

/**
 * The Admin tab's Timing card, the part of it that is arithmetic and words
 * rather than markup (ADR: Backends, side by side, the addendum on parity).
 * Every number the card shows for the relational store it shows for the
 * document store, and where a container has no document store the card says
 * so in words rather than leaving the line out.
 */

/** The document store's window as /api/admin/metrics answers it, per backend and at the top level. */
export type StoreWindow = {
  store: string;
  window: number;
  p50_ms: number;
  p95_ms: number;
  max_ms: number;
  ru_total: number;
  cross_partition: number;
};

/** The relational store's window: the SQL ring's percentiles. */
export type SqlWindow = { window: number; p50_ms: number; p95_ms: number; max_ms: number };

/** As much of the metrics answer as the Timing card reads. */
export type TimingMetrics = {
  requests: { window: number };
  sql: SqlWindow;
  store: StoreWindow;
  /** Absent from a peer on an older build, so read as optional. */
  backends?: { store_metrics: StoreWindow | null }[];
};

// #region document-store-window
/**
 * The document store's window on this container, or null when it runs none.
 * The per-backend list is the authority: the top-level `store` block is
 * labelled with the store serving the visit, which on a container running
 * both is the relational one half the time, while its numbers are always
 * the document store ring's. A peer on an older build has no list, and for
 * it the label is all there is.
 */
export function documentStore(metrics: TimingMetrics): StoreWindow | null {
  if (metrics.backends !== undefined) {
    const backend = metrics.backends.find((candidate) => candidate.store_metrics !== null);
    return backend?.store_metrics ?? null;
  }
  return metrics.store.store === 'Azure Cosmos DB' ? metrics.store : null;
}

/** The sentence that says what the card's numbers are measured over: every ring the container keeps. */
export function timingWindow(metrics: TimingMetrics): string {
  const store = documentStore(metrics);
  const rings = [
    `${metrics.requests.window} requests`,
    `${metrics.sql.window} SQL statements`,
    store === null
      ? 'no document store on this container'
      : `${store.window} document store operations`,
  ];
  return `Measured in this process, over the last ${rings[0]}, ${rings[1]} and ${rings[2]}, with the endpoints this page reads left out so it does not fill with the act of being read.`;
}

/** The relational store's line: the same three numbers it has always shown. */
export function sqlLine(metrics: TimingMetrics): string {
  return `SQL: p50 ${metrics.sql.p50_ms} ms, p95 ${metrics.sql.p95_ms} ms, slowest ${metrics.sql.max_ms} ms.`;
}

/**
 * The document store's twin of the SQL line, plus the two numbers only it
 * can put a figure on: what the window cost in request units, and how many
 * of its operations fanned out across partitions instead of reading one.
 */
export function documentStoreLine(metrics: TimingMetrics): string {
  const store = documentStore(metrics);
  if (store === null) {
    return 'Document store: none on this container, so there is no operation to time.';
  }
  const fanned =
    store.window === 0
      ? 'no operations yet'
      : `${store.cross_partition} of ${store.window} operations fanned out across partitions`;
  return `Document store: p50 ${store.p50_ms} ms, p95 ${store.p95_ms} ms, slowest ${store.max_ms} ms, ${store.ru_total} RU over the window, ${fanned}.`;
}
// #endregion document-store-window

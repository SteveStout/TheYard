import { describe, expect, it } from 'vitest';
import {
  documentStore,
  documentStoreLine,
  percentileWords,
  sqlLine,
  timingWindow,
  type StoreWindow,
  type TimingMetrics,
} from './metrics';

/**
 * The Timing card's words (ADR: Backends, side by side, the addendum on
 * parity). What is worth holding: the document store's line exists wherever
 * the relational store's does, it is read from the backend that has a
 * document store and not from the top-level block's label, and a container
 * with no document store gets a sentence rather than a missing line.
 */

const ops: StoreWindow = {
  store: 'Azure Cosmos DB',
  window: 40,
  p50_ms: 4,
  p95_ms: 11,
  max_ms: 38,
  ru_total: 96.4,
  cross_partition: 3,
};

/** A container running both stores, visited on the relational one: the top-level block wears the wrong label. */
const both: TimingMetrics = {
  requests: { window: 120 },
  sql: { window: 55, p50_ms: 2, p95_ms: 9, max_ms: 31 },
  store: { ...ops, store: 'Azure SQL Database' },
  backends: [{ store_metrics: null }, { store_metrics: ops }],
};

/** A developer's machine: SQLite alone. */
const alone: TimingMetrics = {
  requests: { window: 12 },
  sql: { window: 4, p50_ms: 1, p95_ms: 3, max_ms: 3 },
  store: { ...ops, store: 'SQLite', window: 0, ru_total: 0, cross_partition: 0 },
  backends: [{ store_metrics: null }],
};

describe('documentStore', () => {
  it('reads the backend that has a document store, whatever the top-level block is labelled', () => {
    expect(documentStore(both)).toBe(ops);
    expect(documentStore(alone)).toBeNull();
  });

  it('falls back on the label for a peer on an older build with no backends list', () => {
    const older: TimingMetrics = { requests: both.requests, sql: both.sql, store: both.store };
    expect(documentStore(older)).toBeNull();
    expect(documentStore({ ...older, store: ops })).toBe(ops);
  });
});

describe('the lines', () => {
  it('gives the document store every number the SQL line has, and the two only it can put a figure on', () => {
    expect(sqlLine(both)).toBe('SQL: p50 2 ms, p95 9 ms, slowest 31 ms.');
    expect(documentStoreLine(both)).toBe(
      'Document store: p50 4 ms, p95 11 ms, slowest 38 ms, 96.4 RU over the window, 3 of 40 operations fanned out across partitions.'
    );
  });

  it('says in words when there is no document store, or nothing in its window yet', () => {
    expect(documentStoreLine(alone)).toBe(
      'Document store: none on this container, so there is no operation to time.'
    );
    const quiet: TimingMetrics = {
      ...both,
      backends: [
        { store_metrics: null },
        { store_metrics: { ...ops, window: 0, cross_partition: 0 } },
      ],
    };
    expect(documentStoreLine(quiet)).toContain('no operations yet');
  });

  it('says an empty window has nothing to time, and an answer under a millisecond as one, never 0 ms', () => {
    const empty: TimingMetrics = {
      ...both,
      sql: { window: 0, p50_ms: null, p95_ms: null, max_ms: null },
      backends: [
        { store_metrics: null },
        {
          store_metrics: {
            ...ops,
            window: 0,
            p50_ms: null,
            p95_ms: null,
            max_ms: null,
            ru_total: 0,
            cross_partition: 0,
          },
        },
      ],
    };
    expect(sqlLine(empty)).toBe('SQL: no statements in the ring yet, so there is nothing to time.');
    expect(documentStoreLine(empty)).toBe(
      'Document store: nothing to time, 0 RU over the window, no operations yet.'
    );
    expect(sqlLine({ ...both, sql: { window: 9, p50_ms: 0, p95_ms: 2, max_ms: 0 } })).toBe(
      'SQL: p50 under 1 ms, p95 2 ms, slowest under 1 ms.'
    );
    expect(percentileWords(null, null)).toBe('no requests');
    expect(percentileWords(0, 14)).toBe('p50 under 1 ms, p95 14 ms');
  });

  it('names every ring the window sentence is measured over', () => {
    expect(timingWindow(both)).toBe(
      'Measured in this process, over the last 120 requests, 55 SQL statements and 40 document store operations, with the endpoints this page reads left out so it does not fill with the act of being read.'
    );
    expect(timingWindow(alone)).toContain('no document store on this container');
  });
});

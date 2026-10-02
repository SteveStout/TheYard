/**
 * The per-visitor rows under the activity chart, grouped by day and sortable by column.
 * The card shows it only with the operator's key on a site that serves the rows; every
 * visitor is still only a keyed hash. It is its own component because it holds its own
 * sort state and has nothing to do with the public chart above it.
 */
import { useState } from 'react';
import {
  groupByDay,
  labelFor,
  sortVisitors,
  type ActivityVisitor,
  type ActivityVisitors,
  type VisitorSortKey,
} from '../../../lib/activity';
import { formatDateTime } from '../../../lib/format';
import cardStyles from '../shared/card.module.css';
import type { Fetched } from '../shared/types';
import { type Column, DataTable } from '../DataTable';

/**
 * A visitor's day: the hash, the network, the store, when first and last seen,
 * how much it asked for and where. Every column but the hash and the paths sorts.
 */
const visitorColumns = (
  current: VisitorSortKey,
  sortBy: (column: VisitorSortKey) => void
): Column<ActivityVisitor>[] => {
  const sort = (column: VisitorSortKey) => ({
    active: current === column,
    onSort: () => sortBy(column),
  });
  return [
    { name: 'Visitor', mono: true, cell: (row) => row.visitor.slice(0, 12) },
    { name: 'Network', mono: true, sort: sort('network'), cell: (row) => row.network },
    { name: 'Store', sort: sort('store'), cell: (row) => row.store },
    {
      name: 'First seen',
      mono: true,
      sort: sort('first_seen'),
      cell: (row) => formatDateTime(row.first_seen),
    },
    {
      name: 'Last seen',
      mono: true,
      sort: sort('last_seen'),
      cell: (row) => formatDateTime(row.last_seen),
    },
    {
      name: 'Requests',
      mono: true,
      sort: sort('requests'),
      cell: (row) => `${row.requests}${row.bots > 0 ? ` (${row.bots} bot)` : ''}`,
    },
    {
      name: 'Top paths',
      mono: true,
      cell: (row) => row.top_paths.map((entry) => `${entry.path} (${entry.requests})`).join(', '),
    },
  ];
};

/**
 * The "Visitors" heading and the rows: loading, refused, or a table of one group per day.
 * Newest last seen first by default; a second click on a column turns its order round,
 * and network and store start from A.
 */
export function ActivityVisitorTable({ visitors }: { visitors: Fetched<ActivityVisitors> }) {
  const [sortKey, setSortKey] = useState<VisitorSortKey>('last_seen');
  const [descending, setDescending] = useState(true);

  const sortBy = (next: VisitorSortKey) => {
    if (next === sortKey) {
      setDescending((d) => !d);
    } else {
      setSortKey(next);
      setDescending(next !== 'network' && next !== 'store');
    }
  };

  return (
    <>
      <h3 className={cardStyles.cardTitle}>Visitors</h3>
      {visitors === null ? (
        <p className={cardStyles.muted}>Loading…</p>
      ) : visitors === 'failed' ? (
        <p className={cardStyles.muted} data-testid="visitors-refused">
          The visitor rows did not answer to this key.
        </p>
      ) : (
        <DataTable
          label="Visitors in the window"
          testId="activity-visitors"
          groups={groupByDay(visitors.visitors).map((group) => ({
            key: group.day,
            testId: 'activity-day',
            title: `${labelFor(group.day, '30d')} (${group.day}): ${group.visitors} visitor${group.visitors === 1 ? '' : 's'}, ${group.requests} request${group.requests === 1 ? '' : 's'}`,
            rows: sortVisitors(group.rows, sortKey, descending),
          }))}
          rowKey={(row) => `${row.store}:${row.day}:${row.visitor}`}
          empty="Nobody in this window."
          columns={visitorColumns(sortKey, sortBy)}
        />
      )}
    </>
  );
}

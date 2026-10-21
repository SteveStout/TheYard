/**
 * Who opened what: each tagged link's opens by its code (ADR: Tagged links, who opened what). A
 * code is six opaque characters; who it was sent to is the operator's own lookup, off the site,
 * so this card is as public as the rest of the tab and names nobody.
 */
import { useState } from 'react';
import {
  LINK_WINDOWS,
  openStamp,
  pathsLine,
  type LinkOpensReport,
  type LinkOpensRow,
} from '../../../lib/links';
import styles from '../shared/card.module.css';
import { About, failed, useRead } from '../shared/common';
import { CardState } from '../shared/CardState';
import { type Column, DataTable } from '../DataTable';

type LinkWindow = (typeof LINK_WINDOWS)[number];

/** A code's row: the code, its first and last open, how many, on how many days, and the paths. */
const columns: Column<LinkOpensRow>[] = [
  { name: 'Code', mono: true, short: true, cell: (row) => row.code },
  { name: 'First open', mono: true, short: true, cell: (row) => openStamp(row.first_ms) },
  { name: 'Last open', mono: true, short: true, cell: (row) => openStamp(row.last_ms) },
  { name: 'Opens', num: true, cell: (row) => row.opens },
  { name: 'Days', num: true, cell: (row) => row.days },
  { name: 'Paths', mono: true, cell: (row) => pathsLine(row.paths) },
];

export default function LinksCard({ tick }: { tick: number }) {
  const [window_, setWindow] = useState<LinkWindow>('30d');
  const report = useRead<LinkOpensReport>(`/api/admin/links?window=${window_}`, tick);
  return (
    <article className={`${styles.wide} op-glass`} data-testid="links-card">
      <h2 className={styles.cardTitle}>Who opened what</h2>
      <About>
        Every link the operator sends out carries a six-character code. The site counts the code
        when a person opens the page, About Steven, the resume, or a pass-through to GitHub or
        LinkedIn, and leaves preview fetchers and scanners out. The code is all the site keeps; who
        it went to is the operator's own list, off the site.
      </About>
      <p
        className={`${styles.statusRow} op-seg op-seg-wrap`}
        role="group"
        aria-label="Window for the links card"
      >
        {LINK_WINDOWS.map((option) => (
          <button
            key={option}
            type="button"
            className={styles.back}
            aria-pressed={option === window_}
            onClick={() => setWindow(option)}
            data-testid={`links-window-${option}`}
          >
            {option === '7d' ? 'Last 7 days' : option === '30d' ? 'Last 30 days' : 'Last 90 days'}
          </button>
        ))}
      </p>
      {report === null ? (
        <CardState state="loading" />
      ) : report === 'failed' ? (
        failed('the link opens')
      ) : !report.available ? (
        <p className={styles.muted} data-testid="links-absent">
          {report.note}
        </p>
      ) : report.codes.length === 0 ? (
        <CardState state="empty" what="tagged links">
          No tagged link was opened in the window.
        </CardState>
      ) : (
        <DataTable
          label="Opens by code"
          testId="links-table"
          rows={report.codes}
          rowKey={(row) => row.code}
          columns={columns}
        />
      )}
      {report !== null && report !== 'failed' && (
        <p className={styles.muted} data-testid="links-keys">
          Pass-through links this site offers:{' '}
          {report.keys.map((key) => `/api/go/${key}`).join(', ')}.
        </p>
      )}
    </article>
  );
}

/**
 * The Overview (ADR: The Admin tab, as a product, the addendum on the
 * Overview): the tab's four questions answered in five seconds, each a row
 * with its number, the line under it, its normal range and a tone on the
 * row's edge. The rows come from src/lib/overview.ts; this file draws them.
 * It reads the page sweep, the bill and the process's start, which no tile
 * holds, and takes the rest from what the strip already read.
 */
import { overviewRows } from '../../../lib/overview';
import type { CostReport } from '../../../lib/spend';
import type { StatTile } from '../../../lib/statTiles';
import styles from '../shared/card.module.css';
import { useRead } from '../shared/common';
import type { Fetched, Health, Metrics, PageStatus } from '../shared/types';
import overviewStyles from './OverviewCard.module.css';

/** The class that colours a row's edge by its tone. */
const TONE_CLASS = {
  good: overviewStyles.good,
  warn: overviewStyles.warn,
  bad: overviewStyles.bad,
  plain: overviewStyles.plain,
  waiting: overviewStyles.waiting,
} as const;

/** The word a screen reader hears for a tone, the same words the tiles use. */
const TONE_WORD = {
  good: 'fine',
  warn: 'worth a look',
  bad: 'needs attention',
  plain: 'for information',
  waiting: 'not read yet',
} as const;

export default function OverviewCard({
  health,
  tiles,
  tick,
}: {
  health: Fetched<Health>;
  tiles: StatTile[];
  tick: number;
}) {
  const pages = useRead<PageStatus>('/api/admin/pages', tick);
  const cost = useRead<CostReport>('/api/admin/costs?window=30d', tick);
  const metrics = useRead<Metrics>('/api/admin/metrics', tick);
  const rows = overviewRows({
    health: health === null || health === 'failed' ? null : health,
    pages: pages === null || pages === 'failed' ? null : pages.report,
    cost: cost === null || cost === 'failed' ? null : cost,
    tiles,
    startedAt: metrics === null || metrics === 'failed' ? null : metrics.startup.started_at,
  });
  return (
    <article className={`${styles.card} op-glass`} data-testid="overview-card">
      <h2 className={styles.cardTitle}>Overview</h2>
      <ul className={overviewStyles.rows}>
        {rows.map((row) => (
          <li
            key={row.key}
            className={`${overviewStyles.row} ${TONE_CLASS[row.tone]}`}
            data-testid={`overview-${row.key}`}
            data-tone={row.tone}
          >
            <span className={overviewStyles.question}>{row.question}</span>
            <span className={overviewStyles.value}>
              {row.value}
              <span className={overviewStyles.toneWord}>{TONE_WORD[row.tone]}</span>
            </span>
            <span className={styles.muted}>{row.detail}</span>
            <span className={overviewStyles.range} data-testid={`overview-${row.key}-range`}>
              Normal: {row.range}
            </span>
          </li>
        ))}
      </ul>
    </article>
  );
}

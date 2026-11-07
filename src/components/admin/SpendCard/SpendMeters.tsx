/**
 * "What watches the meters that can run away?" on the cost card (ADR: What Azure charges, the addendum on
 * the spike guard): every meter the site can run up, the rule Azure holds for it by name and its line. A
 * rule that exists and is on reads fine; one that waits for its deploy is unjudged, never red, because a
 * missing alert needs nobody at three in the morning.
 */
import { type CostReport, type MeterRow, meterTone, metersTone } from '../../../lib/spend';
import cardStyles from '../shared/card.module.css';
import { TONE_WORDS, tonePill } from '../shared/common';
import { DataTable } from '../DataTable';
import styles from './SpendCard.module.css';

export function SpendMeters({ report }: { report: CostReport }) {
  const rows = report.meters;
  const tone = metersTone(rows);
  return (
    <section className={`${styles.part} ${styles.whole}`} data-testid="spend-meters">
      <h3 className={cardStyles.cardTitle}>What watches the meters that can run away?</h3>
      <p className={cardStyles.muted} data-testid="spend-meters-answer">
        {rows === undefined || rows.length === 0
          ? 'not read yet'
          : tone === 'good'
            ? `every one of the ${rows.length} meters has its rule, and each is on`
            : `${rows.filter((row) => meterTone(row) === 'good').length} of ${rows.length} meters have a rule that is on; the rest wait`}
      </p>
      {rows !== undefined && rows.length > 0 && (
        <DataTable<MeterRow>
          label="The meters and the rules that watch them"
          testId="spend-meters-table"
          rows={rows}
          rowKey={(row) => row.rule}
          columns={[
            { name: 'Meter', rowHeader: true, cell: (row) => row.meter },
            { name: 'On', cell: (row) => row.watches },
            { name: 'Rule', mono: true, cell: (row) => row.rule },
            {
              name: 'State',
              short: true,
              cell: (row) =>
                meterTone(row) === 'good' ? (
                  <span className={tonePill('good')}>{TONE_WORDS.good}</span>
                ) : (
                  <span className={cardStyles.muted}>{row.state}</span>
                ),
            },
            { name: 'Its line', cell: (row) => row.line },
          ]}
        />
      )}
    </section>
  );
}

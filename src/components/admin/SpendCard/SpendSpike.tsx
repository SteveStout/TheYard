/**
 * "Did a day spike?" on the cost card (ADR: What Azure charges, the addendum on the spike guard): the judged
 * day and the seven before it as bars, each against its own spike line, and the table under them. The days,
 * their medians and their biggest lines come from the server; the line and the tone from src/lib/ranges.ts.
 */
import {
  type CostReport,
  dayShort,
  money,
  multipleWords,
  type SpikeDay,
  spikeBars,
  spikeRuleWords,
} from '../../../lib/spend';
import { spikeTone } from '../../../lib/ranges';
import cardStyles from '../shared/card.module.css';
import { TONE_WORDS, tonePill } from '../shared/common';
import { DataTable } from '../DataTable';
import styles from './SpendCard.module.css';

const BOX = { width: 320, height: 120, gap: 6 };

export function SpendSpike({ report }: { report: CostReport }) {
  const days = report.spike_days;
  if (days === undefined || days.length === 0) {
    return (
      <section className={styles.part} data-testid="spend-spike">
        <h3 className={cardStyles.cardTitle}>Did a day spike?</h3>
        <p className={cardStyles.muted}>not read yet</p>
      </section>
    );
  }
  const bars = spikeBars(days);
  const width = (BOX.width - BOX.gap * (bars.length - 1)) / bars.length;
  return (
    <section className={`${styles.part} ${styles.whole}`} data-testid="spend-spike">
      <h3 className={cardStyles.cardTitle}>Did a day spike?</h3>
      <p className={cardStyles.muted}>Each day against its own line: {spikeRuleWords()}.</p>
      <svg
        className={styles.spike}
        viewBox={`0 0 ${BOX.width} ${BOX.height}`}
        role="img"
        aria-label={`The last ${bars.length} days against their spike lines: ${bars.map((bar) => `${bar.label} ${money(bar.cost)}, line ${money(bar.line)}, ${TONE_WORDS[bar.tone]}`).join('; ')}`}
      >
        {bars.map((bar, index) => {
          const x = index * (width + BOX.gap);
          const height = Math.max(1, bar.height * BOX.height);
          const lineY = BOX.height - bar.lineAt * BOX.height;
          return (
            <g key={bar.day} data-testid="spend-spike-bar">
              <rect
                className={`${styles.spikeBar} ${bar.tone === 'bad' ? styles.spikeBad : ''} ${bar.partial ? styles.spikePartial : ''}`}
                x={x}
                y={BOX.height - height}
                width={width}
                height={height}
              />
              <line className={styles.spikeLine} x1={x} x2={x + width} y1={lineY} y2={lineY} />
            </g>
          );
        })}
      </svg>
      <DataTable<SpikeDay>
        label="The last days against the week before each"
        testId="spend-spike-table"
        rows={days}
        rowKey={(day) => day.day}
        columns={[
          {
            name: 'Day',
            rowHeader: true,
            short: true,
            cell: (day) => `${dayShort(day.day)}${day.partial ? ' (so far)' : ''}`,
          },
          { name: 'Cost', num: true, cell: (day) => money(day.cost) },
          { name: 'Of the median', cell: (day) => multipleWords(day) },
          {
            name: 'Biggest line',
            mono: true,
            cell: (day) =>
              day.biggest === null ? '' : `${day.biggest} ${money(day.biggest_cost)}`,
          },
          {
            name: 'Reads',
            short: true,
            cell: (day) => {
              const tone = spikeTone(day);
              return <span className={tonePill(tone)}>{TONE_WORDS[tone]}</span>;
            },
          },
        ]}
      />
    </section>
  );
}

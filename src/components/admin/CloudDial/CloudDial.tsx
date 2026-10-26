/**
 * Does:      Draws one dial as the approved canvas does (ADR: One frame for every resource, the addendum on how a
 *            card draws time): the open ring of three quarters, the reading's arc in its tone, each threshold's tick
 *            across the ring with its words outside it on a leader line (amber, red and the thirty-day 95th), a red
 *            dot at the thirty-day peak, and the big figure with its absolute in the middle.
 * Does not:  Decide the tone, the levels or where a label goes (the server and src/lib/cloudGauge.ts do), or print
 *            the dial's name, hold line and sparkline (CardShell.tsx does).
 * Used by:   src/components/admin/CardShell/CardShell.tsx, through index.ts.
 */
import {
  gaugeArc,
  gaugeLabels,
  gaugePoint,
  gaugeAngle,
  type GaugeLabel,
} from '../../../lib/cloudGauge';
import type { CloudDial as Dial } from '../../../lib/cloud';
import styles from './CloudDial.module.css';

const W = 340;
const H = 190;
const CX = W / 2;
const CY = 100;
const R = 64;

function Label({ label }: { label: GaugeLabel }) {
  return (
    <g className={styles[label.key]} data-testid="dial-threshold" data-key={label.key}>
      <line
        className={styles.tick}
        x1={label.tick.x1}
        y1={label.tick.y1}
        x2={label.tick.x2}
        y2={label.tick.y2}
      />
      <path className={styles.leader} d={label.leader} />
      <text className={styles.label} x={label.textX} y={label.textY} textAnchor={label.anchor}>
        {label.text}
      </text>
    </g>
  );
}

export function CloudDial({ dial, testId }: { dial: Dial; testId: string }) {
  const labels = gaugeLabels(dial, CX, CY, R, H);
  const peak = dial.peak === null ? null : gaugePoint(CX, CY, R, gaugeAngle(dial.peak));
  return (
    <figure className={styles.dial} data-testid={testId} data-tone={dial.tone}>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label={`${dial.name}: ${dial.big}, ${dial.absolute}; ${labels.map((l) => l.text).join(', ')}`}
      >
        <path className={styles.track} d={gaugeArc(CX, CY, R, 0, 100)} />
        {dial.percent !== null && dial.percent > 0 && (
          <path
            className={`${styles.arc} ${styles[dial.tone]}`}
            d={gaugeArc(CX, CY, R, 0, dial.percent)}
          />
        )}
        {labels.map((label) => (
          <Label key={label.key} label={label} />
        ))}
        {peak !== null && dial.peak !== null && (
          <circle className={styles.peakDot} cx={peak.x} cy={peak.y} r={3.5}>
            <title>{`Peak over 30 days: ${Math.round(dial.peak)} %`}</title>
          </circle>
        )}
        <text
          className={dial.percent === null ? styles.bigWord : styles.big}
          x={CX}
          y={CY + 8}
          textAnchor="middle"
          data-testid="dial-big"
        >
          {dial.big}
        </text>
        <text className={styles.absolute} x={CX} y={CY + 30} textAnchor="middle">
          {dial.absolute}
        </text>
      </svg>
    </figure>
  );
}

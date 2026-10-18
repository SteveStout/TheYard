/**
 * A Cloud infrastructure card's readout, beside the card (ADR: The App Service plan, read every minute): the
 * resource's name in capitals, a ring for how much of the window it was up, then one line a
 * reading with its tone on the left edge, and where the readings come from.
 */
import { arcPath, pointAt, angleOf, ticks, type CloudReport, valueLines } from '../../../lib/cloud';
import styles from './CloudReadout.module.css';

export function CloudReadout({ readout }: { readout: CloudReport['readout'] }) {
  const share = readout.uptime_percent;
  return (
    <aside
      className={`${styles.readout} op-glass`}
      data-testid="cloud-readout"
      aria-label={readout.title}
    >
      <h3 className={styles.heading}>{readout.title}</h3>
      <svg
        className={styles.ring}
        viewBox="0 0 220 220"
        role="img"
        aria-label={`${share === null ? 'not read yet' : `${share} %`} ${readout.uptime_label}`}
      >
        {ticks().map((tick) => {
          const a = pointAt(110, 110, 96, angleOf(tick.percent));
          const b = pointAt(110, 110, tick.long ? 104 : 100, angleOf(tick.percent));
          return (
            <line key={tick.percent} className={styles.tick} x1={a.x} y1={a.y} x2={b.x} y2={b.y} />
          );
        })}
        <circle className={styles.track} cx="110" cy="110" r="82" />
        {share !== null && share > 0 && (
          <path className={styles.fill} d={arcPath(110, 110, 82, 0, share)} />
        )}
        <text x="110" y="112" textAnchor="middle" className={styles.big}>
          {share === null ? '...' : `${share} %`}
        </text>
        <text x="110" y="136" textAnchor="middle" className={styles.label}>
          {readout.uptime_label}
        </text>
      </svg>
      <dl className={styles.lines}>
        {readout.lines.map((line) => (
          <div
            key={line.label}
            className={`${styles.line} ${styles[line.tone]}`}
            data-testid="cloud-readout-line"
          >
            <dt>{line.label}</dt>
            <dd>
              {valueLines(line.value).map((part, index) => (
                <span key={index} className={styles.part}>
                  {part}
                </span>
              ))}
            </dd>
          </div>
        ))}
      </dl>
      <p className={styles.footnote}>{readout.footnote}</p>
    </aside>
  );
}

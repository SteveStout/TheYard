import { FLARES, RIBBONS, SHINE, SPARKS } from '../lib/ribbons';
import styles from './Ribbons.module.css';

/**
 * The teal and gold ribbons behind the page (ADR: The glass look, the addendum
 * on the ribbon ground): one layer of inline SVG, fixed behind everything, from
 * the rail's edge. It is code and not a picture, so it costs no request.
 * Nothing in it moves (Steve, 2026-09-21: "No ribbon movement at all it should
 * center only with CSS, we want a minimal website"): it is painted once and
 * centred in the content area by the stylesheet alone. Every colour is a token,
 * set on a gradient's stops by the stylesheet. It holds no words.
 */
const GRADIENT = {
  gold: 'url(#ribbon-gold)',
  teal: 'url(#ribbon-teal)',
  mixed: 'url(#ribbon-mixed)',
};

export function Ribbons({ contained = false }: { contained?: boolean }) {
  // contained: the copy inside a dialog (the Author page), filling that dialog rather than the screen.
  return (
    <div
      className={contained ? `${styles.layer} ${styles.contained}` : styles.layer}
      aria-hidden="true"
      data-testid={contained ? 'ribbons-dialog' : 'ribbons'}
    >
      <svg
        className={styles.drawing}
        viewBox="-370 0 1440 900"
        preserveAspectRatio="xMidYMid slice"
        focusable="false"
      >
        <defs>
          <linearGradient id="ribbon-gold" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" className={styles.goldSoft} />
            <stop offset=".5" className={styles.gold} />
            <stop offset="1" className={styles.goldSoft} />
          </linearGradient>
          <linearGradient id="ribbon-teal" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" className={styles.teal} />
            <stop offset=".6" className={styles.tealLight} />
            <stop offset="1" className={styles.gold} />
          </linearGradient>
          <linearGradient id="ribbon-mixed" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" className={styles.gold} />
            <stop offset=".5" className={styles.teal} />
            <stop offset="1" className={styles.green} />
          </linearGradient>
          <linearGradient id="ribbon-highlight" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" className={styles.shinePale} stopOpacity=".2" />
            <stop offset=".45" className={styles.shine} />
            <stop offset="1" className={styles.shineWhite} stopOpacity=".3" />
          </linearGradient>
          <radialGradient id="ribbon-flare">
            <stop offset="0" className={styles.flare} />
            <stop offset=".25" className={styles.shine} stopOpacity=".8" />
            <stop offset="1" className={styles.shine} stopOpacity="0" />
          </radialGradient>
          <radialGradient id="ribbon-spark">
            <stop offset="0" className={styles.flare} />
            <stop offset=".4" className={styles.spark} />
            <stop offset="1" className={styles.spark} stopOpacity="0" />
          </radialGradient>
          <filter id="ribbon-shine-blur" x="-30%" y="-30%" width="160%" height="160%">
            <feGaussianBlur stdDeviation="5" result="b" />
            <feMerge>
              <feMergeNode in="b" />
              <feMergeNode in="b" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
          <filter id="ribbon-glow" x="-20%" y="-20%" width="140%" height="140%">
            <feGaussianBlur stdDeviation="2.5" result="b" />
            <feMerge>
              <feMergeNode in="b" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
        </defs>
        <g className={styles.ribbons}>
          {RIBBONS.map(([d, gradient, width, opacity]) => (
            <path key={d} d={d} stroke={GRADIENT[gradient]} strokeWidth={width} opacity={opacity} />
          ))}
        </g>
        <g className={styles.shineStrands}>
          {SHINE.map(([d, width]) => (
            <path key={d} d={d} stroke="url(#ribbon-highlight)" strokeWidth={width} />
          ))}
        </g>
        <g>
          {FLARES.map(([x, y, r, across, down, width]) => (
            <g
              key={`${x},${y}`}
              transform={`translate(${x},${y})`}
              filter="url(#ribbon-shine-blur)"
            >
              <circle r={r} fill="url(#ribbon-flare)" />
              <path
                className={styles.star}
                d={`M${-across},0 L${across},0 M0,${-down} L0,${down}`}
                strokeWidth={width}
              />
            </g>
          ))}
        </g>
        <g className={styles.sparks}>
          {SPARKS.map(([cx, cy, r]) => (
            <circle key={`${cx},${cy}`} cx={cx} cy={cy} r={r} fill="url(#ribbon-spark)" />
          ))}
        </g>
      </svg>
    </div>
  );
}

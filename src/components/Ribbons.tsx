import type { CSSProperties } from 'react';
import { FLARES, RIBBONS, SHINE, SPARKS } from '../lib/ribbons';
import styles from './Ribbons.module.css';

/**
 * The teal and gold ribbons behind the page (ADR: The glass look, the addendum
 * on the ribbon ground): ONE inline SVG, fixed behind everything, starting at
 * the rail's edge. It is code and not a picture, so it costs no request. The
 * whole drawing drifts on a transform, the sparks twinkle and the two flares
 * pulse on their opacity; the two soft glows sit on the ribbons and the
 * highlight strands, which never move on their own. Every colour is a token,
 * set on a gradient's stops by the stylesheet. It holds no words.
 */
const GRADIENT = {
  gold: 'url(#ribbon-gold)',
  teal: 'url(#ribbon-teal)',
  mixed: 'url(#ribbon-mixed)',
};

export function Ribbons() {
  return (
    <div className={styles.layer} aria-hidden="true" data-testid="ribbons">
      <svg
        className={styles.drawing}
        viewBox="0 0 1440 900"
        preserveAspectRatio="xMinYMid slice"
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
          {FLARES.map(([x, y, r, across, down, width, delay]) => (
            <g
              key={`${x},${y}`}
              className={styles.flareGroup}
              transform={`translate(${x},${y})`}
              style={{ '--d': `${delay}s` } as CSSProperties}
            >
              {/* The pulse is on this group and the glow on the one inside it, so nothing that moves carries a blur. */}
              <g filter="url(#ribbon-shine-blur)">
                <circle r={r} fill="url(#ribbon-flare)" />
                <path
                  className={styles.star}
                  d={`M${-across},0 L${across},0 M0,${-down} L0,${down}`}
                  strokeWidth={width}
                />
              </g>
            </g>
          ))}
        </g>
        <g className={styles.sparks}>
          {SPARKS.map(([cx, cy, r, delay]) => (
            <circle
              key={`${cx},${cy}`}
              cx={cx}
              cy={cy}
              r={r}
              fill="url(#ribbon-spark)"
              style={{ '--d': `${delay}s` } as CSSProperties}
            />
          ))}
        </g>
      </svg>
    </div>
  );
}

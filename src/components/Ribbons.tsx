import { useId } from 'react';
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
 *
 * Each copy names its gradients and filters with an id of its own. The page's
 * copy and the document dialog's copy both said "ribbon-gold", and a reference
 * finds the first element with the name, which was the dialog's, inside a
 * closed dialog: Chrome paints nothing from a gradient it does not render, so
 * the page's ribbons were blank in Chrome while WebKit still drew them.
 */
export function Ribbons({ contained = false }: { contained?: boolean }) {
  // contained: the copy inside a dialog (the Author page), filling that dialog rather than the screen.
  const own = `ribbon-${useId().replace(/[^A-Za-z0-9_-]/g, '')}`;
  const ref = (name: string) => `url(#${own}-${name})`;
  const gradient = { gold: ref('gold'), teal: ref('teal'), mixed: ref('mixed') };
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
          <linearGradient id={`${own}-gold`} x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" className={styles.goldSoft} />
            <stop offset=".5" className={styles.gold} />
            <stop offset="1" className={styles.goldSoft} />
          </linearGradient>
          <linearGradient id={`${own}-teal`} x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" className={styles.teal} />
            <stop offset=".6" className={styles.tealLight} />
            <stop offset="1" className={styles.gold} />
          </linearGradient>
          <linearGradient id={`${own}-mixed`} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" className={styles.gold} />
            <stop offset=".5" className={styles.teal} />
            <stop offset="1" className={styles.green} />
          </linearGradient>
          <linearGradient id={`${own}-highlight`} x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" className={styles.shinePale} stopOpacity=".2" />
            <stop offset=".45" className={styles.shine} />
            <stop offset="1" className={styles.shineWhite} stopOpacity=".3" />
          </linearGradient>
          <radialGradient id={`${own}-flare`}>
            <stop offset="0" className={styles.flare} />
            <stop offset=".25" className={styles.shine} stopOpacity=".8" />
            <stop offset="1" className={styles.shine} stopOpacity="0" />
          </radialGradient>
          <radialGradient id={`${own}-spark`}>
            <stop offset="0" className={styles.flare} />
            <stop offset=".4" className={styles.spark} />
            <stop offset="1" className={styles.spark} stopOpacity="0" />
          </radialGradient>
          <filter id={`${own}-shine-blur`} x="-30%" y="-30%" width="160%" height="160%">
            <feGaussianBlur stdDeviation="5" result="b" />
            <feMerge>
              <feMergeNode in="b" />
              <feMergeNode in="b" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
          <filter id={`${own}-glow`} x="-20%" y="-20%" width="140%" height="140%">
            <feGaussianBlur stdDeviation="2.5" result="b" />
            <feMerge>
              <feMergeNode in="b" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
        </defs>
        <g className={styles.ribbons} filter={ref('glow')}>
          {RIBBONS.map(([d, tone, width, opacity]) => (
            <path key={d} d={d} stroke={gradient[tone]} strokeWidth={width} opacity={opacity} />
          ))}
        </g>
        <g className={styles.shineStrands} filter={ref('shine-blur')}>
          {SHINE.map(([d, width]) => (
            <path key={d} d={d} stroke={ref('highlight')} strokeWidth={width} />
          ))}
        </g>
        <g>
          {FLARES.map(([x, y, r, across, down, width]) => (
            <g key={`${x},${y}`} transform={`translate(${x},${y})`} filter={ref('shine-blur')}>
              <circle r={r} fill={ref('flare')} />
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
            <circle key={`${cx},${cy}`} cx={cx} cy={cy} r={r} fill={ref('spark')} />
          ))}
        </g>
      </svg>
    </div>
  );
}

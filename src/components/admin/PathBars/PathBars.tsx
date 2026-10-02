/**
 * A short list of bars, each a label, a bar and a count, drawn as one SVG at the width
 * the card gives it. The activity card draws the recruiter's path and where visitors
 * came from with it; it is its own component because both tiles share it.
 */
import { useFittedBox } from '../charts';
import styles from '../ActivityCard/ActivityCard.module.css';

/**
 * The drawing's sizes: the widest it grows, each row's height, and the room kept for the
 * label on the left and the count on the right. Drawn at its real width, its words stay
 * the chart text size on a phone and on a wide desk alike.
 */
const PATH_CHART = { width: 560, row: 26, bar: 120, count: 44 } as const;

/** One row: its key, the words beside it, its count, its share of the longest bar, its test id. */
export type PathBar = { key: string; name: string; count: number; share: number; testId: string };

/** Draws the rows as bars in the given colour class, with the label read out to screen readers. */
export function PathBars({ rows, label, tone }: { rows: PathBar[]; label: string; tone: string }) {
  const [fit, box] = useFittedBox({
    width: PATH_CHART.width,
    height: PATH_CHART.row * rows.length,
    top: 0,
    right: 0,
    bottom: 0,
    left: 0,
  });
  const room = box.width - PATH_CHART.bar - PATH_CHART.count;
  return (
    <svg
      ref={fit}
      className={styles.pathChart}
      viewBox={`0 0 ${box.width} ${box.height}`}
      role="img"
      aria-label={label}
    >
      {rows.map((row, index) => {
        const y = index * PATH_CHART.row;
        return (
          <g key={row.key} data-testid={row.testId}>
            <text className={styles.pathLabel} x={0} y={y + 17}>
              {row.name}
            </text>
            <rect
              className={styles.pathTrack}
              x={PATH_CHART.bar}
              y={y + 5}
              width={room}
              height={16}
              rx={4}
            />
            <rect
              className={`${styles.pathBar} ${tone}`}
              x={PATH_CHART.bar}
              y={y + 5}
              width={row.share * room}
              height={16}
              rx={4}
            />
            <text className={styles.pathCount} x={box.width} y={y + 17} textAnchor="end">
              {row.count.toLocaleString()}
            </text>
          </g>
        );
      })}
    </svg>
  );
}

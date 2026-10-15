/**
 * What colleagues say, on the landing page: one sentence from each of the ten
 * recommendations on Steve's LinkedIn profile, word for word, with who wrote
 * it, in a row that scrolls sideways, and one link to the ten in full on About
 * Steven. The words live in src/library/recommendations.ts; this file only
 * draws them, under the heading the landing page gives the row, as it gives
 * every group its heading.
 */
import { RECOMMENDATIONS } from '../../../library/recommendations';
import styles from './Recommendations.module.css';

/**
 * The ten sentences in a row and the link; `onOpenAuthor` opens About Steven,
 * where every recommendation is quoted in full. The row is a region with a
 * name and a tab stop: a box that scrolls sideways under a mouse has to scroll
 * under the arrow keys too (WCAG 2.1.1, the rule the admin tables follow).
 */
export function Recommendations({ onOpenAuthor }: { onOpenAuthor: () => void }) {
  return (
    <>
      <div
        className={styles.row}
        role="region"
        aria-label="What colleagues say: ten recommendations, one sentence each, scrolling sideways"
        tabIndex={0}
      >
        <ul className={styles.quotes}>
          {RECOMMENDATIONS.map((entry) => (
            <li key={entry.name} className={`${styles.quote} op-glass op-tile`}>
              <blockquote
                className={styles.words}
                cite="https://www.linkedin.com/in/stevenwstout/details/recommendations/"
              >
                {entry.pull}
              </blockquote>
              <p className={styles.who}>
                <span className={styles.name}>{entry.name}</span>
                <span>{entry.title}</span>
              </p>
            </li>
          ))}
        </ul>
      </div>
      <p className={styles.all}>
        <button
          type="button"
          className={styles.allLink}
          onClick={onOpenAuthor}
          data-testid="landing-recommendations-all"
        >
          All ten, in full, on About Steven
        </button>
      </p>
    </>
  );
}

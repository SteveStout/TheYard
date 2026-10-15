/**
 * What colleagues say, on the landing page: three sentences lifted word for
 * word from the recommendations on Steve's LinkedIn profile, each with who
 * wrote it, and one link to the ten in full on About Steven. The words live in
 * src/library/recommendations.ts; this file only draws them, under the heading
 * the landing page gives the strip, as it gives every group its heading.
 */
import { landingPicks } from '../../../library/recommendations';
import styles from './Recommendations.module.css';

/** The three quotes and the link; `onOpenAuthor` opens About Steven, where every recommendation is quoted in full. */
export function Recommendations({ onOpenAuthor }: { onOpenAuthor: () => void }) {
  return (
    <>
      <ul className={styles.quotes}>
        {landingPicks().map((entry) => (
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

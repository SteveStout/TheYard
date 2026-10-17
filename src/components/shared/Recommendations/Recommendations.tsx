/**
 * What colleagues say, as a column: the recommendations on Steve's LinkedIn
 * profile, word for word, each with who wrote it, read from /api/recommendations
 * when the component opens. The words live in one file on the server,
 * data/recommendations.json, and this file only draws them: the landing page
 * draws the one sentence each, About Steven draws them in full, both read
 * from the one endpoint. Every one of them stands in the page, one under the
 * next, with no scroll of its own: a reader on the way past the page reads
 * them all, and the page keeps the only scroll bar on the screen.
 * Ten blank places stand in the column
 * from the first paint, so nothing moves when the answer arrives, and an answer
 * that does not come, or is not the shape, is said out loud rather than drawn
 * as an empty column.
 */
import { useEffect, useState } from 'react';
import {
  RECOMMENDATION_PLACES,
  RECOMMENDATIONS_URL,
  recommendationsFrom,
  type Recommendation,
} from '../../../lib/recommendations';
import styles from './Recommendations.module.css';

/** What the column shows of each recommendation: the one pull sentence, or every paragraph. */
export type RecommendationWords = 'pull' | 'full';

/** The blank places, named so each has a key of its own. */
const PLACES = Array.from({ length: RECOMMENDATION_PLACES }, (_, index) => `place ${index + 1}`);

type Reading =
  { state: 'reading' } | { state: 'ready'; entries: Recommendation[] } | { state: 'failed' };

/**
 * The column and, when it has somewhere to send the reader, the link under it; `onOpenAuthor`
 * opens About Steven, where every recommendation stands in full. The column is a named region, so
 * a reader moving by landmark can find it and skip it; it holds no scroll of its own, so it needs
 * no tab stop of its own either (the rule in WCAG 2.1.1 is for a box that scrolls).
 */
export function Recommendations({
  words,
  onOpenAuthor,
}: {
  words: RecommendationWords;
  onOpenAuthor?: () => void;
}) {
  const [reading, setReading] = useState<Reading>({ state: 'reading' });
  useEffect(() => {
    let live = true;
    fetch(RECOMMENDATIONS_URL)
      .then((response) => (response.ok ? (response.json() as Promise<unknown>) : null))
      .catch(() => null)
      .then((answer) => {
        if (!live) return;
        const entries = recommendationsFrom(answer);
        setReading(entries === null ? { state: 'failed' } : { state: 'ready', entries });
      });
    return () => {
      live = false;
    };
  }, []);

  // The count is the answer's, not a number written here, so an eleventh entry in the file names itself.
  const count =
    reading.state === 'ready' ? `${reading.entries.length} recommendations` : 'recommendations';
  const label =
    words === 'pull'
      ? `What colleagues say: ${count}, one sentence each`
      : `What colleagues say: ${count} in full`;
  return (
    <>
      <div
        className={styles.column}
        role="region"
        aria-label={label}
        aria-busy={reading.state === 'reading'}
        data-state={reading.state}
      >
        {reading.state === 'failed' ? (
          <p className={styles.failed}>The recommendations could not be read just now.</p>
        ) : (
          <ul className={styles.quotes}>
            {reading.state === 'ready'
              ? reading.entries.map((entry) => (
                  <li key={entry.name} className={`${styles.quote} op-glass op-tile`}>
                    <blockquote
                      className={styles.words}
                      cite="https://www.linkedin.com/in/stevenwstout/details/recommendations/"
                    >
                      {words === 'pull' ? (
                        <p>{entry.pull}</p>
                      ) : (
                        entry.paragraphs.map((paragraph) => <p key={paragraph}>{paragraph}</p>)
                      )}
                    </blockquote>
                    <p className={styles.who}>
                      <span className={styles.name}>{entry.name}</span>
                      <span>{entry.title}</span>
                      {words === 'full' && (
                        <span>
                          {entry.relationship}, {entry.when}.
                        </span>
                      )}
                    </p>
                  </li>
                ))
              : PLACES.map((place) => (
                  // A blank place the height of a short quotation, so the column is its full height from the start.
                  <li
                    key={place}
                    className={`${styles.quote} ${styles.place} op-glass op-tile`}
                    aria-hidden="true"
                  />
                ))}
          </ul>
        )}
      </div>
      {onOpenAuthor && (
        <p className={styles.all}>
          <button
            type="button"
            className={styles.allLink}
            onClick={onOpenAuthor}
            data-testid="landing-recommendations-all"
          >
            All of them, in full, on About Steven
          </button>
        </p>
      )}
    </>
  );
}

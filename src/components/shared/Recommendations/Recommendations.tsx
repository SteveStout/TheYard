/**
 * What colleagues say, as a column: the recommendations on Steve's LinkedIn
 * profile, word for word and whole, each under the name of the person who
 * wrote it, which opens their own page, read from /api/recommendations when
 * the component opens. The words
 * live in one file on the server, data/recommendations.json, and this file only
 * draws them, the same way on every page that shows them. Every one of them
 * stands in the page, one under the next, with no scroll of its own and nothing
 * to click through to: a reader on the way past the page reads them all, and
 * the page keeps the only scroll bar on the screen. Ten blank places stand in
 * the column from the first paint, so nothing moves when the answer arrives,
 * and an answer that does not come, or is not the shape, is said out loud
 * rather than drawn as an empty column.
 */
import { useEffect, useState } from 'react';
import {
  RECOMMENDATION_PLACES,
  RECOMMENDATIONS_URL,
  recommendationsFrom,
  type Recommendation,
} from '../../../lib/recommendations';
import styles from './Recommendations.module.css';

/** The blank places, named so each has a key of its own. */
const PLACES = Array.from({ length: RECOMMENDATION_PLACES }, (_, index) => `place ${index + 1}`);

type Reading =
  { state: 'reading' } | { state: 'ready'; entries: Recommendation[] } | { state: 'failed' };

/**
 * The column. It is a named region, so a reader moving by landmark can find it and skip it; it
 * holds no scroll of its own, so it needs no tab stop of its own either (the rule in WCAG 2.1.1
 * is for a box that scrolls).
 */
export function Recommendations() {
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
  const label =
    reading.state === 'ready'
      ? `What colleagues say: ${reading.entries.length} recommendations, in full`
      : 'What colleagues say';
  return (
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
                    {entry.paragraphs.map((paragraph) => (
                      <p key={paragraph}>{paragraph}</p>
                    ))}
                  </blockquote>
                  <p className={styles.who}>
                    {/* The name opens the page of the person who wrote it, in a tab of its own. */}
                    <a
                      className={styles.name}
                      href={entry.profile}
                      target="_blank"
                      rel="noreferrer"
                    >
                      {entry.name}
                    </a>
                    <span>{entry.title}</span>
                    <span>
                      {entry.relationship}, {entry.when}.
                    </span>
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
  );
}

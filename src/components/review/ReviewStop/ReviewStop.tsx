/**
 * Does:      Draws one stop on the route in one of three states: done (a green disc with a tick), current (the only one
 *            showing "Do this", Open this stop and Mark done) or next (its number in a ring and a small Open).
 * Does not:  Decide which state a stop is in or keep anything (ReviewPage.tsx and useReviewProgress.ts do).
 * Used by:   ReviewPage.tsx.
 */
import { ICON } from '../../../lib/icons';
import type { ReviewStop as Stop } from '../../../lib/reviewStops';
import pills from '../ReviewPage/ReviewPage.module.css';
import styles from './ReviewStop.module.css';

/** What a stop is to the reviewer right now. */
export type StopState = 'done' | 'current' | 'next';

export function ReviewStop({
  stop,
  index,
  state,
  onChoose,
  onOpened,
  onMarkDone,
}: {
  stop: Stop;
  /** Its place on the route, from 0. */
  index: number;
  state: StopState;
  /** The head was pressed: this stop becomes current. */
  onChoose: () => void;
  /** The stop's address was opened. */
  onOpened: () => void;
  onMarkDone: () => void;
}) {
  const current = state === 'current';
  const headId = `review-stop-${stop.key}`;
  return (
    <li
      className={`${styles.stop} op-glass`}
      data-testid={headId}
      data-state={state}
      aria-current={current ? 'step' : undefined}
    >
      <h2 className={styles.heading}>
        <button
          type="button"
          id={headId}
          className={styles.head}
          onClick={onChoose}
          aria-expanded={current}
        >
          <span className={styles.mark} data-state={state} aria-hidden="true">
            {state === 'done' ? (
              <svg viewBox="0 0 20 20" width={ICON.md} height={ICON.md} focusable="false">
                <path
                  d="M5 10.5l3.2 3.2L15 7"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth={ICON.stroke}
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              </svg>
            ) : (
              index + 1
            )}
          </span>
          <span className={styles.title}>{stop.title}</span>
          <span className={styles.minutes}>
            {stop.minutes} min
            <span className={styles.srOnly}>
              {state === 'done' ? ', done' : current ? ', the stop you are on' : ''}
            </span>
          </span>
        </button>
      </h2>
      <p className={styles.label}>What this shows</p>
      <p className={styles.shows}>{stop.shows}</p>
      {current && (
        <p className={styles.doThis} data-testid="review-do-this">
          <strong>Do this:</strong> {stop.doThis}
        </p>
      )}
      <div className={styles.actions}>
        <a
          className={current ? pills.primary : pills.secondary}
          href={stop.address}
          onClick={onOpened}
          aria-describedby={headId}
        >
          {current ? 'Open this stop' : 'Open'}
        </a>
        {current && (
          <button type="button" className={pills.secondary} onClick={onMarkDone}>
            Mark done
          </button>
        )}
      </div>
      <p className={styles.address}>
        Address: <code className={styles.code}>{stop.address}</code>
      </p>
    </li>
  );
}

/**
 * The three states a card is in before it has something to draw, said the
 * same way on every card: still loading, the last read failed (and when the
 * next try is), or read and empty. One component, one role="status", so a
 * screen reader hears a card arrive and a test finds the state by its id
 * whichever card it is on.
 */
import type { ReactNode } from 'react';
import styles from './card.module.css';

export type CardStateKind = 'loading' | 'failed' | 'empty';

export function CardState({
  state,
  what = 'this card',
  children,
}: {
  state: CardStateKind;
  /** What the card reads, as the failed sentence names it ("the log", "Azure's view"). */
  what?: string;
  /** The empty sentence, when the card has its own words for nothing. */
  children?: ReactNode;
}) {
  return (
    <p className={styles.muted} role="status" data-testid={`card-${state}`}>
      {state === 'loading'
        ? 'Loading…'
        : state === 'failed'
          ? `Could not read ${what} on the last try; the next try is in 30 seconds.`
          : (children ?? `Nothing recorded for ${what} yet.`)}
    </p>
  );
}

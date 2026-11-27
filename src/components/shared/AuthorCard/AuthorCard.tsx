/**
 * Does:      Draws the author card the blog's side opens with, on the list and on every post (ADR-142, the addendum on
 *            the landing views): his photo in the gold ring, his name and role, the sentence About opens with, and two
 *            doors, About Steven and Check my work, in one glass card.
 * Does not:  Hold its words (authorCard.ts does) or count a press (each door is a counted address of its own).
 * Used by:   BlogPage.tsx.
 */
import { AUTHOR_CARD, AUTHOR_PHOTO } from '../../../lib/authorCard';
import styles from './AuthorCard.module.css';

export function AuthorCard() {
  return (
    <section
      className={`${styles.card} op-glass`}
      aria-label="Who writes this"
      data-mock="author-card"
      data-testid="author-card"
    >
      <img
        className={styles.photo}
        src={AUTHOR_PHOTO}
        width={64}
        height={64}
        alt={AUTHOR_CARD.name}
      />
      <div>
        <p className={styles.name}>{AUTHOR_CARD.name}</p>
        <p className={styles.role}>{AUTHOR_CARD.role}</p>
      </div>
      <p className={styles.line}>{AUTHOR_CARD.line}</p>
      <p className={styles.doors}>
        {AUTHOR_CARD.doors.map((door) => (
          <a key={door.href} className="op-pill-secondary" href={door.href} data-mock={door.mock}>
            {door.label}
          </a>
        ))}
      </p>
    </section>
  );
}

// The rail of the Admin workbench: a box to find a card, a note when the address named a card
// that does not exist, and every card grouped under the question it answers. The workbench
// shows it beside the open card on a wide screen and in the Cards drawer on a narrower one.
// It is its own file because both places draw the same list, and the list keeps its own search.
import { useState } from 'react';
import { BENCH_QUESTIONS, benchCard, findCards } from '../../../lib/bench';
import type { CardSlug } from '../../../lib/workbench';
import styles from '../AdminPanel/AdminPanel.module.css';
import { followCardLink } from '../AdminPanel/followCardLink';

/**
 * What goes in the rail (the side list of cards): a search box, a note when the
 * address named an unknown card, and the cards grouped under five questions.
 * On a wide screen the rail sits beside the card; on narrower ones it is in the
 * Cards drawer.
 */
export function AdminRailContent({
  open,
  asked,
  onOpen,
}: {
  /** The card open now, marked as the current page in the list. */
  open: CardSlug;
  /** A card name from the address that matched no card, or null. */
  asked: string | null;
  onOpen: (slug: CardSlug) => void;
}) {
  const [query, setQuery] = useState('');
  const found = findCards(query);
  const openCard = benchCard(open);
  return (
    <>
      <label className={styles.railFind}>
        <span className={styles.railFindLabel}>Find a card</span>
        <input
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="timing, errors, sql"
          data-testid="bench-find"
        />
      </label>
      {asked !== null && (
        <p className={styles.railNote} role="status" data-testid="bench-unknown">
          No card is called &lsquo;{asked}&rsquo;, so this is {openCard.name}.
        </p>
      )}
      {BENCH_QUESTIONS.map((entry, index) => {
        const cards = found.filter((candidate) => candidate.question === entry.key);
        if (cards.length === 0) return null;
        return (
          <section
            key={entry.key}
            className={styles.railGroup}
            aria-labelledby={`question-${entry.key}`}
            data-testid={`question-${entry.key}`}
          >
            <h2 className={styles.railTitle} id={`question-${entry.key}`}>
              <span>
                {String(index + 1).padStart(2, '0')} {entry.title}
              </span>
              <span className={styles.railCount}>{cards.length}</span>
            </h2>
            <ul className={styles.railList}>
              {cards.map((candidate) => (
                <li key={candidate.slug}>
                  <a
                    href={`?view=admin&card=${candidate.slug}`}
                    className={styles.railLink}
                    aria-current={candidate.slug === open ? 'page' : undefined}
                    data-testid={`bench-link-${candidate.slug}`}
                    onClick={(event) => followCardLink(event, () => onOpen(candidate.slug))}
                  >
                    {candidate.name}
                  </a>
                </li>
              ))}
            </ul>
          </section>
        );
      })}
      {found.length === 0 && (
        <p className={styles.railNote} data-testid="bench-none">
          No card matches &lsquo;{query}&rsquo;.
        </p>
      )}
    </>
  );
}

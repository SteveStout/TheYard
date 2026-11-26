/**
 * Does:      Draws Away from the keyboard on the About page as the qa1126 picture draws it (ADR: The sidebar, the addendum
 *            on the person beside the work): even cards three across in the page's order, each with its photographs
 *            first, the rabbits' card and the strip of the couple's photographs the full width, the photographer's credit under
 *            the strip. A photograph whose largest file cannot fill a wider box stops at the width it can.
 * Does not:  Choose the order, which photograph goes where or a photograph's cap (aboutWeave.ts does), hold a word of
 *            its own, or draw a card's glass or a photograph's frame (panels.css and AuthorPage.module.css do).
 * Used by:   AboutPage.tsx.
 */
import type { Away, Photo } from '../../../lib/aboutWeave';
import styles from './AboutAway.module.css';

/** A card's photographs, two or three across; one whose file cannot fill a wider box stops at its cap. */
function Photos({ photos }: { photos: Photo[] }) {
  const across = photos.length >= 3 ? styles.three : photos.length === 2 ? styles.two : '';
  return (
    <div className={`${styles.photos} ${across}`} data-mock="photos">
      {photos.map((photo) => (
        <div
          key={photo.figure}
          className={styles.photo}
          style={photo.cap === null ? undefined : { maxWidth: photo.cap }}
          dangerouslySetInnerHTML={{ __html: photo.figure }}
        />
      ))}
    </div>
  );
}

export function AboutAway({ away }: { away: Away }) {
  return (
    <section className={`author-panel op-glass ${styles.away}`} data-mock="away">
      <div className={styles.contents} dangerouslySetInnerHTML={{ __html: away.heading }} />
      <div className={`author-blocks ${styles.grid}`} data-mock="away-grid">
        {away.cards.map((card) => (
          <section
            key={card.title}
            className={`author-block op-glass ${styles.card} ${card.wide ? styles.wide : ''}`}
            data-mock="away-card"
          >
            {card.photos.length > 0 && <Photos photos={card.photos} />}
            <div
              className={styles.contents}
              dangerouslySetInnerHTML={{ __html: card.heading + card.body }}
            />
          </section>
        ))}
        {away.strip && (
          <section
            className={`author-block op-glass ${styles.card} ${styles.wide} ${styles.strip}`}
            data-mock="away-strip"
          >
            <Photos photos={away.strip.photos} />
            {away.strip.credit && (
              <footer
                className="author-credit"
                dangerouslySetInnerHTML={{ __html: away.strip.credit }}
              />
            )}
          </section>
        )}
      </div>
    </section>
  );
}

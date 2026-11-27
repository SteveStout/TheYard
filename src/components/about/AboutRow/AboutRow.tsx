/**
 * Does:      Draws one personal row of the About page as the about design draws it (ADR-142, the addendum on the landing
 *            views; the ruling for bigger photos mixed around): one glass card, its photographs in one column and its
 *            words in the other, vertically centred. A photo-left row gives the photographs the 7/12 seat, a photo-right
 *            row the 5/12 seat, so big and medium alternate down the page; a wide row (the rabbits) sets three across
 *            over its words; the couple's strip sets its three across over the photographer's credit. Every row's words
 *            open with the away panel's heading as a small label.
 * Does not:  Choose a row's side or place (aboutOrder.ts does), cut the document (aboutWeave.ts does), hold a word of its
 *            own, or draw a card's glass or a photograph's frame (panels.css and AuthorPage.module.css do).
 * Used by:   AboutPage.tsx.
 */
import type { AwayCard, Photo } from '../../../lib/aboutWeave';
import type { RowSide } from '../../../lib/aboutOrder';
import prose from '../../../library/DocProse.module.css';
import author from '../../../library/AuthorPage.module.css';
import styles from './AboutRow.module.css';

/** The document's sheets a row's own words need: its links, its pictures' frame and captions. */
const PROSE = `${prose.prose} ${author.prose}`;

/** A row's photographs, one, two side by side, or three across. */
function Photos({ photos }: { photos: readonly Photo[] }) {
  return (
    <div className={styles.photos} data-count={Math.min(photos.length, 3)} data-mock="photos">
      {photos.map((figure) => (
        <div key={figure} className={styles.photo} dangerouslySetInnerHTML={{ __html: figure }} />
      ))}
    </div>
  );
}

export function AboutRow(
  props:
    | { card: AwayCard; side: RowSide; label: string }
    | { strip: { photos: readonly Photo[]; credit: string | null } }
) {
  if ('strip' in props)
    return (
      <section className={`${PROSE} op-glass ${styles.row} ${styles.strip}`} data-mock="away-strip">
        <Photos photos={props.strip.photos} />
        {props.strip.credit && (
          <footer
            className="author-credit"
            dangerouslySetInnerHTML={{ __html: props.strip.credit }}
          />
        )}
      </section>
    );
  const { card, side, label } = props;
  return (
    <section
      className={`${PROSE} op-glass ${styles.row}`}
      data-mock="away-card"
      data-photo={side}
      data-row="away"
    >
      {card.photos.length > 0 && <Photos photos={card.photos} />}
      <div className={styles.words}>
        <p className={styles.label} data-mock="away-label">
          {label}
        </p>
        <div
          className={styles.contents}
          dangerouslySetInnerHTML={{ __html: card.heading + card.body }}
        />
      </div>
    </section>
  );
}

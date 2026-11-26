/**
 * Does:      Draws How I lead on the About page as the qa1126 picture draws it (ADR: The sidebar, the addendum on the
 *            person beside the work): the document's heading and lede, then the four cards three across, each of the
 *            first three with its personal story under the away panel's own heading as a small label and its picture
 *            on the card's floor, and the coaching card the full width with the team drawing on its right.
 * Does not:  Choose which story sits with which card (aboutWeave.ts does), hold a word of its own, or draw a card's
 *            glass (panels.css does).
 * Used by:   AboutPage.tsx.
 */
import type { Lead } from '../../../lib/aboutWeave';
import styles from './AboutLead.module.css';

export function AboutLead({ lead, label }: { lead: Lead; label: string }) {
  return (
    <section className={`author-panel op-glass ${styles.lead}`} data-mock="lead">
      <div
        className={styles.contents}
        dangerouslySetInnerHTML={{ __html: lead.heading + lead.lede + (lead.picture ?? '') }}
      />
      <div className={`author-blocks ${styles.grid}`} data-mock="lead-grid">
        {lead.cards.map((card) => (
          <section
            key={card.title}
            className={`author-block op-glass ${styles.card} ${card.picture ? styles.wide : ''}`}
            data-mock="lead-card"
          >
            <div className={styles.words}>
              <div
                className={styles.contents}
                dangerouslySetInnerHTML={{ __html: card.heading + card.body }}
              />
              {card.aside && (
                <div className={styles.aside} data-mock="aside">
                  <p className={styles.label} data-mock="aside-label">
                    {label}
                  </p>
                  <h4 data-mock="aside-title">{card.aside.title}</h4>
                  <div
                    className={styles.contents}
                    dangerouslySetInnerHTML={{
                      __html: card.aside.body + card.aside.figures.join(''),
                    }}
                  />
                </div>
              )}
            </div>
            {card.picture && (
              <div
                className={styles.picture}
                data-mock="lead-picture"
                dangerouslySetInnerHTML={{ __html: card.picture }}
              />
            )}
          </section>
        ))}
      </div>
    </section>
  );
}

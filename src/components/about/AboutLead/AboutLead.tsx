/**
 * Does:      Draws How I lead on the About page as the about design draws it (ADR: The sidebar, the addendum on the
 *            person beside the work; ADR-142, the addendum on the landing views): under the section's own numbered head
 *            and its lede, the three practice cards three across on the ground, no panel around them, each with its
 *            personal story under the away panel's heading as a small label and its picture on the card's floor; and,
 *            asked for apart, the coaching card the full width with the team drawing on its right, further down the page.
 * Does not:  Choose which story sits with which card (aboutWeave.ts does) or where the coaching card stands
 *            (aboutOrder.ts does), hold a word of its own, or draw a card's glass (panels.css does).
 * Used by:   AboutPage.tsx.
 */
import { COACH, words, type Lead, type LeadCard } from '../../../lib/aboutWeave';
import { SectionHead } from '../../shared/SectionHead';
import prose from '../../../library/DocProse.module.css';
import author from '../../../library/AuthorPage.module.css';
import styles from './AboutLead.module.css';

/** The document's sheets a card's own words need: its links, its pictures' frame and captions. */
const PROSE = `${prose.prose} ${author.prose}`;

/** One card: its heading and words, the story beside it under the label, and the team drawing when it has one. */
function Card({ card, label }: { card: LeadCard; label: string }) {
  return (
    <section
      className={`author-block op-glass ${styles.card} ${card.picture ? styles.wide : ''}`}
      data-mock={card.picture ? 'coaching' : 'lead-card'}
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
              dangerouslySetInnerHTML={{ __html: card.aside.body + card.aside.figures.join('') }}
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
  );
}

export function AboutLead({
  lead,
  label,
  number,
  part,
}: {
  lead: Lead;
  /** The away panel's heading, the label over each story. */
  label: string;
  /** The section's place on the page, its badge. */
  number: string;
  /** The section with its three cards, or the coaching card alone. */
  part: 'section' | 'coaching';
}) {
  const coach = lead.cards.find((card) => card.title === COACH && card.picture !== null);
  if (part === 'coaching')
    return coach ? (
      <div className={`${PROSE} ${styles.lead}`} data-row="coaching">
        <Card card={coach} label={label} />
      </div>
    ) : null;
  return (
    <section aria-labelledby="about-lead-title" data-mock="section">
      <SectionHead
        id="about-lead-title"
        number={number}
        title={words(lead.heading)}
        hint={words(lead.lede)}
      />
      {lead.picture && (
        <div
          className={`${PROSE} ${styles.drawing}`}
          dangerouslySetInnerHTML={{ __html: lead.picture }}
        />
      )}
      <div className={`${PROSE} ${styles.lead}`}>
        <div className={`author-blocks ${styles.grid}`} data-mock="lead-grid">
          {lead.cards
            .filter((card) => card !== coach)
            .map((card) => (
              <Card key={card.title} card={card} label={label} />
            ))}
        </div>
      </div>
    </section>
  );
}

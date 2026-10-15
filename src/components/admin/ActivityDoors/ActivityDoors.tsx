/**
 * Resume opens, by door (ADR: The resume has an address): which link the
 * people who opened the resume came through, linkedin, an application, an
 * email, the signature, a post, another word, or the site itself, as bars
 * over the window's visitor-days. The doors come from the activity report.
 */
import { pathShares, type ActivityReport } from '../../../lib/activity';
import styles from '../ActivityCard/ActivityCard.module.css';
import cardStyles from '../shared/card.module.css';
import { PathBars } from '../PathBars';

/** A door as a reader says it. */
const DOOR_NAMES: Record<string, string> = {
  application: 'An application',
  email: 'An email',
  linkedin: 'LinkedIn',
  other: 'Another link',
  post: 'A post',
  signature: 'The signature',
  site: 'The site itself',
};

export function ActivityDoors({ report, tone }: { report: ActivityReport; tone: string }) {
  const doors = report.doors ?? [];
  const opened = doors.filter((door) => door.visitor_days > 0);
  return (
    <section className={styles.pathTile} data-testid="activity-doors">
      <h3 className={cardStyles.cardTitle}>Resume opens, by door</h3>
      {opened.length === 0 ? (
        <p className={cardStyles.muted}>No one opened the resume in the window.</p>
      ) : (
        <PathBars
          rows={opened.map((door, index) => ({
            key: door.door,
            name: DOOR_NAMES[door.door] ?? door.door,
            count: door.visitor_days,
            share: pathShares(opened)[index],
            testId: `activity-door-${door.door}`,
          }))}
          label={`Resume opens by door over the ${report.window} window: ${opened
            .map((door) => `${DOOR_NAMES[door.door] ?? door.door} ${door.visitor_days}`)
            .join(', ')}`}
          tone={tone}
        />
      )}
      <p className={cardStyles.muted}>
        People only, in visitor-days. A link to /resume names its door with ?src=, and the site
        keeps the word, never the address it was sent to.
      </p>
    </section>
  );
}

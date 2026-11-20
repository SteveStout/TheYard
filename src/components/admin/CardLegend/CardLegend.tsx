/**
 * Does:      Prints what each colour on a Cloud infrastructure card means, one list under the card's title (ADR-102,
 *            the addendum on what a colour means): each entry a dot in the site's own colour for its tone, the
 *            colour's name in bold, then its words. The words come from the API, so every card says the same.
 * Does not:  Type a word of its own, or decide a card's tone; a body with no legend draws nothing.
 * Used by:   src/components/admin/CardShell/CardShell.tsx, src/components/admin/AzureCard/AzureCard.tsx,
 *            src/components/admin/MachinesCard/MachinesCard.tsx, src/components/admin/FollowsCard/FollowsCard.tsx,
 *            through index.ts.
 */
import type { CloudLegendEntry } from '../../../lib/cloud';
import styles from './CardLegend.module.css';

export function CardLegend({ entries }: { entries: CloudLegendEntry[] | undefined }) {
  if (entries === undefined || entries.length === 0) {
    return null;
  }
  return (
    <ul className={styles.legend} data-mock="legend" aria-label="What a colour means">
      {entries.map((entry) => (
        <li key={entry.tone}>
          <i className={styles.dot} data-tone={entry.tone} aria-hidden="true" />
          <span>
            <b>{entry.name}:</b> {entry.words}
          </span>
        </li>
      ))}
    </ul>
  );
}

/**
 * How the activity card shows a series: the colour class for a store's line or a kind's
 * band, and the name a store goes by. The chart, its legend, its readout and the totals
 * under it all draw the same series, so the answer lives once, here, beside the card.
 */
import type { ActivityKind, ActivityReport } from '../../../lib/activity';
import styles from './ActivityCard.module.css';
import chartStyles from '../charts/charts.module.css';

/** The colour class of a store's line: Cosmos DB, Azure SQL, or everybody together. */
export function storeTone(store: string): string {
  return store === 'cosmos'
    ? chartStyles.cosmosLine
    : store === 'sql'
      ? chartStyles.sqlLine
      : chartStyles.allLine;
}

/** The colour class of a kind's band: people, scanners and crawlers, or the site's own reads. */
export function kindTone(kind: ActivityKind): string {
  return kind === 'people'
    ? styles.whoPeople
    : kind === 'scanners'
      ? styles.whoScanners
      : styles.whoSelf;
}

/** The name the report gives a store, or the store's key when the report names it nowhere. */
export function storeName(report: ActivityReport, store: string): string {
  return report.series.find((line) => line.store === store)?.name ?? store;
}

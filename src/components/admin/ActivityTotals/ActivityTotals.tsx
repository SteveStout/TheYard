/**
 * The words under the activity chart: the totals, each store's share, how long rows are
 * kept, what people asked for, what was left out, the collector's own counts, and the
 * strip of what scanners probed. It is its own component because it is all sentences
 * read off the report, with none of the chart's geometry.
 */
import {
  collectorSummary,
  costSentence,
  namedPaths,
  totalsSentence,
  type ActivityReport,
  type ActivityWho,
} from '../../../lib/activity';
import styles from '../ActivityCard/ActivityCard.module.css';
import chartStyles from '../charts/charts.module.css';
import cardStyles from '../shared/card.module.css';
import { storeName, storeTone } from '../ActivityCard/activitySeriesLook';

/**
 * The summary list and the scanner strip for the report, for the people alone or all
 * traffic. Under By store the totals line carries the everybody swatch, since the chart
 * above it draws everybody as a line of its own.
 */
export function ActivityTotals({
  report,
  who,
  view,
}: {
  report: ActivityReport;
  who: ActivityWho;
  view: 'kind' | 'store';
}) {
  const shown = who === 'people' ? report.who.people : report.who.all;
  const { people, scanners, self } = report.who;
  return (
    <>
      <ul className={chartStyles.summaryList} data-testid="activity-totals">
        <li>
          {view === 'store' && (
            <span className={`${chartStyles.swatch} ${chartStyles.allLine}`} aria-hidden="true" />
          )}
          {totalsSentence(report, who)}
        </li>
        {shown.by_store.map((store) => (
          <li key={store.store}>
            <span
              className={`${chartStyles.swatch} ${storeTone(store.store)}`}
              aria-hidden="true"
            />
            {storeName(report, store.store)}: {store.visitor_days.toLocaleString()} visitor-days,{' '}
            {store.requests.toLocaleString()} requests over those days.
          </li>
        ))}
        {report.retention !== null && (
          <li className={cardStyles.muted} data-testid="activity-retention">
            Rows {report.retention}.
          </li>
        )}
        {report.stores
          .filter((store) => !store.available)
          .map((store) => (
            <li key={store.store} data-testid="activity-unavailable">
              {store.name} keeps no activity here: {store.reason}.
            </li>
          ))}
        <li data-testid="activity-asked-for">
          What people asked for, named by page:{' '}
          {people.top_paths.length === 0
            ? 'nothing yet'
            : namedPaths(people.top_paths)
                .slice(0, 8)
                .map((entry) => `${entry.name} ${entry.requests.toLocaleString()}`)
                .join(' · ')}
          .
        </li>
        {who === 'all' && self.top_paths.length > 0 && (
          <li className={cardStyles.muted} data-testid="activity-own-asked-for">
            The site's own reads asked for:{' '}
            {namedPaths(self.top_paths)
              .slice(0, 5)
              .map((entry) => `${entry.name} ${entry.requests.toLocaleString()}`)
              .join(' · ')}
            .
          </li>
        )}
        {who === 'people' && (
          <li className={cardStyles.muted} data-testid="activity-left-out">
            Left out: {scanners.visitor_days.toLocaleString()} visitor-days of scanners and crawlers
            ({scanners.requests.toLocaleString()} requests) and {self.visitor_days.toLocaleString()}{' '}
            of the site's own reads ({self.requests.toLocaleString()} requests: App Service keeping
            the container warm, the page sweep and the ship's readers).
          </li>
        )}
        <li className={cardStyles.muted} data-testid="activity-collector">
          <details className={cardStyles.about}>
            <summary className={cardStyles.aboutSummary}>
              {collectorSummary(report.collector)}
            </summary>
            <p className={cardStyles.muted} data-testid="activity-collector-details">
              {report.collector.offered.toLocaleString()} hits offered since the process started,{' '}
              {report.collector.written.toLocaleString()} written,{' '}
              {report.collector.dropped.toLocaleString()} dropped by a full queue,{' '}
              {report.collector.failed_batches} batches failed; everything queued goes as one batch
              to the keeper, {storeName(report, report.kept_by)}, every{' '}
              {report.collector.interval_seconds} seconds.
              {report.cost !== null && ` ${costSentence(report.cost)}`}
            </p>
          </details>
        </li>
      </ul>
      {scanners.top_paths.length > 0 && (
        <p className={styles.scannerStrip} data-testid="activity-scanners">
          <strong>What scanners probed, kept out of the lists above:</strong>{' '}
          {scanners.top_paths
            .slice(0, 6)
            .map((entry) => `${entry.path} ${entry.requests.toLocaleString()}`)
            .join(' · ')}
          ; {scanners.requests.toLocaleString()} requests from{' '}
          {scanners.visitor_days.toLocaleString()} visitor-days that looked like scanners and
          crawlers.
        </p>
      )}
    </>
  );
}

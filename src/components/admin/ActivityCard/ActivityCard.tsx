/**
 * Site activity (ADR: Site activity, and the line an address does not cross).
 * The card is the frame and the two choices; the parts live beside it:
 *   useActivityReports.ts        the report and the visitor rows, read per window
 *   activitySeriesLook.ts        the colour and the name of a store or a kind
 *   ActivityGraph/               the split toggle, legend, chart, totals, tiles and day table
 *     ActivityPlot/              the chart drawing, its crosshair and ActivityReadout/
 *     ActivityTotals/            the sentences under the chart
 *     ActivityPathTiles/         the path to the resume and where they came from, as PathBars/
 *     ActivityDaysTable/         the day by day table
 *   ActivityVisitorTable/        the per-visitor rows, behind the operator's key
 */
import { useState } from 'react';
import {
  ACTIVITY_WHO,
  ACTIVITY_WINDOWS,
  type ActivityReport,
  type ActivityWho,
} from '../../../lib/activity';
import cardStyles from '../shared/card.module.css';
import { About } from '../shared/common';
import { useActivityReports } from './useActivityReports';
import { ActivityGraph } from '../ActivityGraph';
import { ActivityVisitorTable } from '../ActivityVisitorTable';

/**
 * Site activity (ADR: Site activity, and the line an address does not cross).
 * The graph at the top of the tab: requests over time, the two stores as two
 * lines on one axis, drawn as an inline SVG in the palette the drawings under
 * docs/images use, with the totals, the split and the top paths beside it.
 * It names nobody, so it is as public as the rest of the tab.
 *
 * The table under it is not public. It fetches only when the address bar
 * carries a key, sends the key with the request, and the endpoint answers
 * 404 to everybody else, so without the key the table does not exist here
 * any more than it exists on the wire.
 */
export default function ActivityCard({
  adminKey,
  rowsServed,
  onReport,
}: {
  adminKey: string | null;
  rowsServed: boolean | null;
  onReport: (report: ActivityReport) => void;
}) {
  const {
    window: window_,
    chooseWindow,
    report,
    visitors,
  } = useActivityReports(adminKey, rowsServed, onReport);
  // Visitors only is the default: the card is read for who came, and the
  // site's own reads and the scanners are a click away. The report carries
  // both, so the toggle redraws and never fetches.
  const [who, setWho] = useState<ActivityWho>('people');

  return (
    <article className={`${cardStyles.wide} op-glass`} data-testid="activity-card">
      <h2 className={cardStyles.cardTitle}>Site activity</h2>
      <About>
        Visitor-days per day, stacked by who they were: people at the bottom, then scanners and
        crawlers (every request looked like a bot, by its agent or by what it asked for), then the
        site's own reads (App Service asking after the container from its own loopback address, and
        the site's tools, which carry a mark on their agent). Visitors only shows the people; All
        traffic shows the three together; By store splits the same days by the store that served
        them. Under the chart: what people asked for, named by page, the path to the resume from the
        site to the resume, and where visitors came from by the host that linked here. Every row is
        kept in Azure Cosmos DB, one batch every few seconds written off the request path, each row
        naming the store that served it, so a paused relational database cannot take this card down
        with it; the page's own files, the photos and this tab's reads are not counted. A visitor is
        a keyed hash of the address that changes daily, so the counts group and nothing joins across
        days or back to a person; a full address is never stored and no account is ever named. The
        per-visitor rows, still only hashes, are served to the operator's key alone, and only on a
        site that turns them on.
      </About>
      <p className={`${cardStyles.statusRow} op-seg op-seg-wrap`} role="group" aria-label="Window">
        {ACTIVITY_WINDOWS.map((option) => (
          <button
            key={option}
            type="button"
            className={cardStyles.back}
            aria-pressed={option === window_}
            onClick={() => chooseWindow(option)}
            data-testid={`activity-window-${option}`}
          >
            {option === '24h' ? 'Last 24 hours' : option === '7d' ? 'Last 7 days' : 'Last 30 days'}
          </button>
        ))}
      </p>
      <p className={`${cardStyles.statusRow} op-seg`} role="group" aria-label="Whose traffic">
        {ACTIVITY_WHO.map((option) => (
          <button
            key={option}
            type="button"
            className={cardStyles.back}
            aria-pressed={option === who}
            onClick={() => setWho(option)}
            data-testid={`activity-who-${option}`}
          >
            {option === 'people' ? 'Visitors only' : 'All traffic'}
          </button>
        ))}
      </p>
      {report === null ? (
        <p className={cardStyles.muted}>Loading…</p>
      ) : report === 'failed' ? (
        <p className={cardStyles.muted} data-testid="card-failed">
          Could not read the activity on the last try; the next try is on the next window change.
        </p>
      ) : (
        <ActivityGraph report={report} who={who} />
      )}
      {adminKey !== null && rowsServed === true && <ActivityVisitorTable visitors={visitors} />}
    </article>
  );
}

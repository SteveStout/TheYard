/**
 * Everything the activity card shows once its report has arrived: the By kind or By store
 * toggle, the legend, the chart, the totals, the path and source tiles and the day by day
 * table, in that order. It is its own component because it holds the one choice they
 * all follow (how the chart is split) and works out the bands and lines they share.
 */
import { useState } from 'react';
import {
  KIND_NAMES,
  dayLines,
  stackBands,
  type ActivityReport,
  type ActivityWho,
} from '../../../lib/activity';
import styles from '../ActivityCard/ActivityCard.module.css';
import chartStyles from '../charts/charts.module.css';
import cardStyles from '../shared/card.module.css';
import { kindTone } from '../ActivityCard/activitySeriesLook';
import { ActivityPlot } from '../ActivityPlot';
import { ActivityTotals } from '../ActivityTotals';
import { ActivityPathTiles } from '../ActivityPathTiles';
import { ActivityDaysTable } from '../ActivityDaysTable';

/**
 * The chart, the totals and the top paths; the arithmetic is in src/lib/activity.ts.
 * By kind, the default: visitor-days per day stacked by who they were, people at the
 * bottom, then scanners and crawlers, then the site's own reads, in the three colours
 * validated for colour vision together, with a legend and the band's name on the band
 * where it is thick enough to carry it. By store: everybody as one line and each store.
 */
export function ActivityGraph({ report, who }: { report: ActivityReport; who: ActivityWho }) {
  const [view, setView] = useState<'kind' | 'store'>('kind');
  // Unique visitors per UTC day, by store: everybody as one line, and one
  // line per store underneath it; people only under Visitors only.
  const lines = dayLines(
    report.days,
    report.series.map((line) => line.store),
    who
  );
  const bands = stackBands(report.days, who);
  return (
    <>
      <p
        className={`${cardStyles.statusRow} op-seg`}
        role="group"
        aria-label="How the chart is split"
      >
        {(['kind', 'store'] as const).map((option) => (
          <button
            key={option}
            type="button"
            className={cardStyles.back}
            aria-pressed={option === view}
            onClick={() => setView(option)}
            data-testid={`activity-view-${option}`}
          >
            {option === 'kind' ? 'By kind' : 'By store'}
          </button>
        ))}
      </p>
      {view === 'kind' && bands.length > 1 && (
        <ul className={chartStyles.legend} data-testid="activity-legend">
          {bands.map((band) => (
            <li key={band.kind}>
              <span className={`${chartStyles.swatch} ${kindTone(band.kind)}`} aria-hidden="true" />
              {KIND_NAMES[band.kind]}
            </li>
          ))}
        </ul>
      )}
      <p className={styles.chartCaption} data-testid="activity-axis-name">
        Visitor-days per day
        {view === 'store'
          ? ', by store'
          : who === 'people'
            ? ', people only'
            : ', stacked by who they were'}
      </p>
      <ActivityPlot report={report} who={who} view={view} bands={bands} lines={lines} />
      <ActivityTotals report={report} who={who} view={view} />
      <ActivityPathTiles report={report} who={who} />
      <ActivityDaysTable days={report.days} />
    </>
  );
}

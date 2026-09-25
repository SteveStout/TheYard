/**
 * Site activity (ADR: Site activity, and the line an address does not cross).
 */
import { type PointerEvent, useEffect, useState } from 'react';
import {
  ACTIVITY_KINDS,
  ACTIVITY_WHO,
  ACTIVITY_WINDOWS,
  CHART,
  KIND_NAMES,
  SOURCE_NAMES,
  STEP_NAMES,
  areaPath,
  bandPath,
  ceilingOf,
  collectorSummary,
  costSentence,
  countFor,
  dayAt,
  dayLines,
  edgePath,
  groupSources,
  groupByDay,
  labelFor,
  labelSpot,
  labelledIndexes,
  linePath,
  namedPaths,
  partialDay,
  pathShares,
  sortVisitors,
  stackBands,
  stackCeiling,
  todayNote,
  xAt,
  yAt,
  type ActivityDay,
  type ActivityKind,
  type ActivityReport,
  type ActivityVisitor,
  type ActivityVisitors,
  type ActivityWho,
  type ActivityWindow,
  type VisitorSortKey,
} from '../../lib/activity';
import { plotFrame } from '../../lib/plotFrame';
import styles from '../AdminPanel.module.css';
import { PlotFrame, useFittedBox } from './charts';
import type { Fetched } from './types';
import { About } from './common';
import { type Column, DataTable } from './DataTable';

/**
 * A visitor's day: the hash, the network, the store, when first and last seen,
 * how much it asked for and where. Every column but the hash and the paths sorts.
 */
const visitorColumns = (
  current: VisitorSortKey,
  sortBy: (column: VisitorSortKey) => void
): Column<ActivityVisitor>[] => {
  const sort = (column: VisitorSortKey) => ({
    active: current === column,
    onSort: () => sortBy(column),
  });
  return [
    { name: 'Visitor', mono: true, cell: (row) => row.visitor.slice(0, 12) },
    { name: 'Network', mono: true, sort: sort('network'), cell: (row) => row.network },
    { name: 'Store', sort: sort('store'), cell: (row) => row.store },
    {
      name: 'First seen',
      mono: true,
      sort: sort('first_seen'),
      cell: (row) => new Date(row.first_seen).toLocaleString(),
    },
    {
      name: 'Last seen',
      mono: true,
      sort: sort('last_seen'),
      cell: (row) => new Date(row.last_seen).toLocaleString(),
    },
    {
      name: 'Requests',
      mono: true,
      sort: sort('requests'),
      cell: (row) => `${row.requests}${row.bots > 0 ? ` (${row.bots} bot)` : ''}`,
    },
    {
      name: 'Top paths',
      mono: true,
      cell: (row) => row.top_paths.map((entry) => `${entry.path} (${entry.requests})`).join(', '),
    },
  ];
};

/** A site that linked here, and how many visitor-days it sent. */
const SOURCE_COLUMNS: Column<{ host: string; visitor_days: number }>[] = [
  { name: 'Site', mono: true, cell: (entry) => entry.host },
  {
    name: 'Visitor-days',
    mono: true,
    num: true,
    cell: (entry) => entry.visitor_days.toLocaleString(),
  },
];

/** A day: its visitor-days of each kind, and all of them. */
const DAY_COLUMNS: Column<ActivityDay>[] = [
  { name: 'Day', rowHeader: true, cell: (day) => labelFor(day.day, '30d') },
  ...ACTIVITY_KINDS.map((kind): Column<ActivityDay> => ({
    name: KIND_NAMES[kind],
    mono: true,
    num: true,
    cell: (day) => day[kind].toLocaleString(),
  })),
  {
    name: 'All',
    mono: true,
    num: true,
    cell: (day) => countFor(day, 'all').toLocaleString(),
  },
];

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
  const [window_, setWindow] = useState<ActivityWindow>('7d');
  // Visitors only is the default (1.0.3.11): the card is read for who came,
  // and the site's own reads and the scanners are a click away. The report
  // carries both, so the toggle redraws and never fetches.
  const [who, setWho] = useState<ActivityWho>('people');
  const [report, setReport] = useState<Fetched<ActivityReport>>(null);
  const [visitors, setVisitors] = useState<Fetched<ActivityVisitors>>(null);
  const [sortKey, setSortKey] = useState<VisitorSortKey>('last_seen');
  const [descending, setDescending] = useState(true);
  const key = adminKey;

  useEffect(() => {
    let live = true;
    void fetch(`/api/admin/activity?window=${window_}`)
      .then((r) =>
        r.ok ? (r.json() as Promise<ActivityReport>) : Promise.reject(new Error(String(r.status)))
      )
      .then((v) => {
        if (live) {
          setReport(v);
          onReport(v);
        }
      })
      .catch(() => {
        if (live) setReport('failed');
      });
    if (key !== null && rowsServed === true) {
      void fetch(`/api/admin/activity/visitors?window=${window_}`, {
        headers: { 'X-Admin-Key': key },
      })
        .then((r) =>
          r.ok
            ? (r.json() as Promise<ActivityVisitors>)
            : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (live) setVisitors(v);
        })
        .catch(() => {
          if (live) setVisitors('failed');
        });
    }
    return () => {
      live = false;
    };
  }, [window_, key, rowsServed, onReport]);

  const sortBy = (next: VisitorSortKey) => {
    if (next === sortKey) {
      setDescending((d) => !d);
    } else {
      setSortKey(next);
      setDescending(next !== 'network' && next !== 'store');
    }
  };

  return (
    <article className={`${styles.wide} op-glass`} data-testid="activity-card">
      <h2 className={styles.cardTitle}>Site activity</h2>
      <About>
        Visitor-days per day, stacked by who they were: people at the bottom, then scanners and
        crawlers (every request looked like a bot, by its agent or by what it asked for), then the
        site's own reads (App Service asking after the container from its own loopback address, and
        the site's tools, which carry a mark on their agent). Visitors only shows the people; All
        traffic shows the three together; By store splits the same days by the store that served
        them. Under the chart: what people asked for, named by page, the recruiter's path from the
        site to the resume, and where visitors came from by the host that linked here. Every row is
        kept in Azure Cosmos DB, one batch every few seconds written off the request path, each row
        naming the store that served it, so a paused relational database cannot take this card down
        with it; the page's own files, the photos and this tab's reads are not counted. A visitor is
        a keyed hash of the address that changes daily, so the counts group and nothing joins across
        days or back to a person; a full address is never stored and no account is ever named. The
        per-visitor rows, still only hashes, are served to the operator's key alone, and only on a
        site that turns them on.
      </About>
      <p className={`${styles.statusRow} op-seg op-seg-wrap`} role="group" aria-label="Window">
        {ACTIVITY_WINDOWS.map((option) => (
          <button
            key={option}
            type="button"
            className={styles.back}
            aria-pressed={option === window_}
            onClick={() => {
              // The change of window is the event; the cards go back to
              // loading here rather than inside the effect that fetches. The
              // window already showing is not a change: the effect would not
              // run again and the card would stay on "Loading" for good
              // (the 1.0.0.116 gate, take one).
              if (option === window_) return;
              setWindow(option);
              setReport(null);
              setVisitors(null);
            }}
            data-testid={`activity-window-${option}`}
          >
            {option === '24h' ? 'Last 24 hours' : option === '7d' ? 'Last 7 days' : 'Last 30 days'}
          </button>
        ))}
      </p>
      <p className={`${styles.statusRow} op-seg`} role="group" aria-label="Whose traffic">
        {ACTIVITY_WHO.map((option) => (
          <button
            key={option}
            type="button"
            className={styles.back}
            aria-pressed={option === who}
            onClick={() => setWho(option)}
            data-testid={`activity-who-${option}`}
          >
            {option === 'people' ? 'Visitors only' : 'All traffic'}
          </button>
        ))}
      </p>
      {report === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : report === 'failed' ? (
        <p className={styles.muted} data-testid="card-failed">
          Could not read the activity on the last try; the next try is on the next window change.
        </p>
      ) : (
        <ActivityGraph report={report} who={who} />
      )}
      {key !== null && rowsServed === true && (
        <>
          <h3 className={styles.cardTitle}>Visitors</h3>
          {visitors === null ? (
            <p className={styles.muted}>Loading…</p>
          ) : visitors === 'failed' ? (
            <p className={styles.muted} data-testid="visitors-refused">
              The visitor rows did not answer to this key.
            </p>
          ) : (
            <DataTable
              label="Visitors in the window"
              testId="activity-visitors"
              groups={groupByDay(visitors.visitors).map((group) => ({
                key: group.day,
                testId: 'activity-day',
                title: `${labelFor(group.day, '30d')} (${group.day}): ${group.visitors} visitor${group.visitors === 1 ? '' : 's'}, ${group.requests} request${group.requests === 1 ? '' : 's'}`,
                rows: sortVisitors(group.rows, sortKey, descending),
              }))}
              rowKey={(row) => `${row.store}:${row.day}:${row.visitor}`}
              empty="Nobody in this window."
              columns={visitorColumns(sortKey, sortBy)}
            />
          )}
        </>
      )}
    </article>
  );
}

/** The recruiter's path drawn as four bars: a label column, the bar, the count, in SVG units the card scales. */
const PATH_CHART = { width: 360, row: 26, bar: 120, count: 44 } as const;

/**
 * The chart, the totals and the top paths; the arithmetic is in src/lib/activity.ts.
 * By kind (the default from 1.0.3.12): visitor-days per day stacked by who
 * they were, people at the bottom, then scanners and crawlers, then the site's
 * own reads, in the three colours validated for colour vision together, with
 * a legend and the band's name on the band where it is thick enough to carry
 * it. By store: the lines the card drew before, everybody and each store.
 */
function ActivityGraph({ report, who }: { report: ActivityReport; who: ActivityWho }) {
  const [view, setView] = useState<'kind' | 'store'>('kind');
  // The day under the pointer or the finger (1.0.3.14); null when there is none.
  const [hover, setHover] = useState<number | null>(null);
  const [fit, chart] = useFittedBox(CHART);
  // Unique visitors per UTC day, by store: everybody as one line, and one
  // line per store underneath it; people only under Visitors only.
  const lines = dayLines(
    report.days,
    report.series.map((line) => line.store),
    who
  );
  const bands = stackBands(report.days, who);
  const ceiling = view === 'kind' ? stackCeiling(bands) : ceilingOf(lines);
  const count = report.days.length;
  const labels = labelledIndexes(count);
  const innerWidth = chart.width - chart.left - chart.right;
  const step = count <= 1 ? 0 : innerWidth / (count - 1);
  const colour = (store: string) =>
    store === 'cosmos' ? styles.cosmosLine : store === 'sql' ? styles.sqlLine : styles.allLine;
  const kindClass = (kind: ActivityKind) =>
    kind === 'people'
      ? styles.whoPeople
      : kind === 'scanners'
        ? styles.whoScanners
        : styles.whoSelf;
  const shown = who === 'people' ? report.who.people : report.who.all;
  const { people, scanners, self } = report.who;
  const nameOf = (store: string) =>
    report.series.find((line) => line.store === store)?.name ?? store;
  const partial = partialDay(report.days, new Date());
  // The day under the pointer, read off the drawing's own width, so a finger
  // and a mouse land on the same day in every engine.
  const point = (event: PointerEvent<SVGSVGElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    if (box.width === 0) return;
    setHover(dayAt(((event.clientX - box.left) / box.width) * chart.width, count, chart));
  };
  const hovered = hover !== null && hover < count ? hover : null;
  // What the crosshair reads out: each band's own share under By kind, each
  // line under By store, in the order the legend names them.
  const readings =
    hovered === null
      ? []
      : view === 'kind'
        ? bands.map((band) => ({
            key: band.kind,
            className: kindClass(band.kind),
            name: KIND_NAMES[band.kind],
            value: band.upper[hovered] - band.lower[hovered],
            top: band.upper[hovered],
          }))
        : lines.map((line) => ({
            key: line.store,
            className: colour(line.store),
            name: line.store === 'all' ? line.name : nameOf(line.store),
            value: line.points[hovered]?.requests ?? 0,
            top: line.points[hovered]?.requests ?? 0,
          }));
  const readingTotal =
    view === 'kind'
      ? readings.reduce((sum, reading) => sum + reading.value, 0)
      : (readings[0]?.value ?? 0);
  return (
    <>
      <p className={`${styles.statusRow} op-seg`} role="group" aria-label="How the chart is split">
        {(['kind', 'store'] as const).map((option) => (
          <button
            key={option}
            type="button"
            className={styles.back}
            aria-pressed={option === view}
            onClick={() => setView(option)}
            data-testid={`activity-view-${option}`}
          >
            {option === 'kind' ? 'By kind' : 'By store'}
          </button>
        ))}
      </p>
      {view === 'kind' && bands.length > 1 && (
        <ul className={styles.legend} data-testid="activity-legend">
          {bands.map((band) => (
            <li key={band.kind}>
              <span className={`${styles.swatch} ${kindClass(band.kind)}`} aria-hidden="true" />
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
      <div className={styles.chartWrap}>
        <svg
          ref={fit}
          className={styles.chart}
          viewBox={`0 0 ${chart.width} ${chart.height}`}
          onPointerMove={point}
          onPointerDown={point}
          onPointerLeave={(event) => {
            if (event.pointerType === 'mouse') setHover(null);
          }}
          role="img"
          aria-label={
            view === 'kind'
              ? `Visitor-days per day over the ${report.window} window, ${
                  who === 'people'
                    ? 'people only'
                    : "all traffic, stacked: people, scanners and crawlers, the site's own reads"
                }`
              : `Unique visitors per day over the ${report.window} window, ${
                  who === 'people' ? 'people only' : 'all traffic'
                }, everybody as one line and one line per store`
          }
          data-testid="activity-graph"
        >
          <line
            className={styles.axis}
            x1={chart.left}
            y1={chart.height - chart.bottom}
            x2={chart.width - chart.right}
            y2={chart.height - chart.bottom}
          />
          <line
            className={styles.axis}
            x1={chart.left}
            y1={chart.top}
            x2={chart.left}
            y2={chart.height - chart.bottom}
          />
          {/* The Mark VII grammar (the tweaks pass, B2): graduations up the side at the
              quarters, a tick under each day, and two gold bracket ticks at the corners,
              the frame every framed chart shares (src/lib/plotFrame.ts). */}
          <PlotFrame
            frame={plotFrame(
              chart,
              report.days.map((day, index) => ({
                key: `d${day.day}`,
                x: xAt(index, count, chart),
                major: false,
              }))
            )}
          />
          <text className={styles.axisLabel} x={chart.left - 8} y={chart.top + 4} textAnchor="end">
            {ceiling}
          </text>
          <text
            className={styles.axisLabel}
            x={chart.left - 8}
            y={chart.height - chart.bottom}
            textAnchor="end"
          >
            0
          </text>
          {labels.map((index) => (
            <text
              key={index}
              className={styles.axisLabel}
              x={chart.left + index * step}
              y={chart.height - 8}
              textAnchor={index === 0 ? 'start' : index === count - 1 ? 'end' : 'middle'}
              data-testid="activity-x-label"
            >
              {report.days[index] ? labelFor(report.days[index].day, report.window) : ''}
            </text>
          ))}
          {partial !== null && (
            <text
              className={styles.axisLabel}
              x={chart.width - chart.right}
              y={chart.top - 2}
              textAnchor="end"
              data-testid="activity-today-note"
            >
              {todayNote(partial.hours)}
            </text>
          )}
          {partial !== null && count > 1 && (
            <rect
              className={styles.todayBand}
              x={xAt(partial.index, count, chart) - step / 2}
              y={chart.top}
              width={step / 2}
              height={chart.height - chart.top - chart.bottom}
              data-testid="activity-today"
            />
          )}
          {view === 'kind'
            ? bands.map((band) => (
                <g
                  key={band.kind}
                  className={kindClass(band.kind)}
                  data-testid={`activity-band-${band.kind}`}
                >
                  <path className={styles.band} d={bandPath(band, ceiling, chart)} />
                  <path className={styles.bandEdge} d={edgePath(band, ceiling, chart)} />
                </g>
              ))
            : lines.map((line) => (
                <g
                  key={line.store}
                  className={colour(line.store)}
                  data-testid={`activity-line-${line.store}`}
                >
                  <path className={styles.area} d={areaPath(line.points, ceiling, chart)} />
                  <path className={styles.line} d={linePath(line.points, ceiling, chart)} />
                </g>
              ))}
          {view === 'kind' &&
            bands.map((band) => {
              const spot = labelSpot(band, ceiling, chart);
              return spot === null ? null : (
                <text
                  key={`label:${band.kind}`}
                  className={styles.bandLabel}
                  x={spot.x}
                  y={spot.y}
                  textAnchor={spot.anchor}
                  data-testid={`activity-band-label-${band.kind}`}
                >
                  {KIND_NAMES[band.kind]}
                </text>
              );
            })}
          {hovered !== null && (
            <g className={styles.crosshair} data-testid="activity-crosshair">
              <line
                x1={xAt(hovered, count, chart)}
                x2={xAt(hovered, count, chart)}
                y1={chart.top}
                y2={chart.height - chart.bottom}
              />
              {readings.map((reading) => (
                <circle
                  key={reading.key}
                  className={`${styles.crossDot} ${reading.className}`}
                  cx={xAt(hovered, count, chart)}
                  cy={yAt(reading.top, ceiling)}
                  r={4}
                />
              ))}
            </g>
          )}
        </svg>
        {hovered !== null && (
          <div
            className={`${styles.chartTip} ${
              xAt(hovered, count, chart) > chart.width / 2
                ? styles.chartTipLeft
                : styles.chartTipRight
            }`}
            role="status"
            data-testid="activity-tooltip"
          >
            <strong>
              {labelFor(report.days[hovered].day, '30d')}
              {partial !== null && partial.index === hovered ? `, ${todayNote(partial.hours)}` : ''}
              : {readingTotal.toLocaleString()}
            </strong>
            {readings.map((reading) => (
              <span key={reading.key}>
                <span className={`${styles.swatch} ${reading.className}`} aria-hidden="true" />
                {reading.name} {reading.value.toLocaleString()}
              </span>
            ))}
          </div>
        )}
      </div>
      <ul className={styles.summaryList} data-testid="activity-totals">
        <li>
          {view === 'store' && (
            <span className={`${styles.swatch} ${styles.allLine}`} aria-hidden="true" />
          )}
          {who === 'people'
            ? `${people.visitor_days.toLocaleString()} visitor-days that looked like people across the days in the window, ${people.requests.toLocaleString()} requests in the window.`
            : `${shown.visitor_days.toLocaleString()} visitor-days across the days in the window: ${people.visitor_days.toLocaleString()} people, ${scanners.visitor_days.toLocaleString()} scanners and crawlers, ${self.visitor_days.toLocaleString()} the site's own reads; ${shown.requests.toLocaleString()} requests in the window.`}
        </li>
        {shown.by_store.map((store) => (
          <li key={store.store}>
            <span className={`${styles.swatch} ${colour(store.store)}`} aria-hidden="true" />
            {nameOf(store.store)}: {store.visitor_days.toLocaleString()} visitor-days,{' '}
            {store.requests.toLocaleString()} requests.
          </li>
        ))}
        {report.retention !== null && (
          <li className={styles.muted} data-testid="activity-retention">
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
          <li className={styles.muted} data-testid="activity-own-asked-for">
            The site's own reads asked for:{' '}
            {namedPaths(self.top_paths)
              .slice(0, 5)
              .map((entry) => `${entry.name} ${entry.requests.toLocaleString()}`)
              .join(' · ')}
            .
          </li>
        )}
        {who === 'people' && (
          <li className={styles.muted} data-testid="activity-left-out">
            Left out: {scanners.visitor_days.toLocaleString()} visitor-days of scanners and crawlers
            ({scanners.requests.toLocaleString()} requests) and {self.visitor_days.toLocaleString()}{' '}
            of the site's own reads ({self.requests.toLocaleString()} requests: App Service keeping
            the container warm, the page sweep and the ship's readers).
          </li>
        )}
        <li className={styles.muted} data-testid="activity-collector">
          <details className={styles.about}>
            <summary className={styles.aboutSummary}>{collectorSummary(report.collector)}</summary>
            <p className={styles.muted} data-testid="activity-collector-details">
              {report.collector.offered.toLocaleString()} hits offered since the process started,{' '}
              {report.collector.written.toLocaleString()} written,{' '}
              {report.collector.dropped.toLocaleString()} dropped by a full queue,{' '}
              {report.collector.failed_batches} batches failed; everything queued goes as one batch
              to the keeper, {nameOf(report.kept_by)}, every {report.collector.interval_seconds}{' '}
              seconds.
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
      <section className={styles.pathTile} data-testid="activity-path">
        <h3 className={styles.cardTitle}>The recruiter's path</h3>
        <svg
          className={styles.pathChart}
          viewBox={`0 0 ${PATH_CHART.width} ${PATH_CHART.row * shown.path.length}`}
          role="img"
          aria-label={`The recruiter's path over the ${report.window} window: ${shown.path
            .map((step) => `${STEP_NAMES[step.step]} ${step.visitor_days}`)
            .join(', ')}`}
        >
          {shown.path.map((step, index) => {
            const y = index * PATH_CHART.row;
            const share = pathShares(shown.path)[index];
            return (
              <g key={step.step} data-testid={`activity-path-${step.step}`}>
                <text className={styles.pathLabel} x={0} y={y + 17}>
                  {STEP_NAMES[step.step]}
                </text>
                <rect
                  className={`${styles.pathTrack}`}
                  x={PATH_CHART.bar}
                  y={y + 5}
                  width={PATH_CHART.width - PATH_CHART.bar - PATH_CHART.count}
                  height={16}
                  rx={4}
                />
                <rect
                  className={`${styles.pathBar} ${who === 'people' ? styles.whoPeople : styles.allLine}`}
                  x={PATH_CHART.bar}
                  y={y + 5}
                  width={share * (PATH_CHART.width - PATH_CHART.bar - PATH_CHART.count)}
                  height={16}
                  rx={4}
                />
                <text className={styles.pathCount} x={PATH_CHART.width} y={y + 17} textAnchor="end">
                  {step.visitor_days.toLocaleString()}
                </text>
              </g>
            );
          })}
        </svg>
        <p className={styles.muted}>
          Visitor-days that asked for each step in the window: the page itself, the inventory's
          listing, About Steven, and the resume, which is the number this site exists for.
        </p>
      </section>
      <section className={styles.pathTile} data-testid="activity-sources">
        <h3 className={styles.cardTitle}>Where they came from</h3>
        {shown.sources.length === 0 ? (
          <p className={styles.muted}>
            Counted from 1.0.3.17, 24 September: no page load in the window has arrived since with
            its referring site kept.
          </p>
        ) : (
          <>
            <svg
              className={styles.pathChart}
              viewBox={`0 0 ${PATH_CHART.width} ${PATH_CHART.row * 5}`}
              role="img"
              aria-label={`Where they came from over the ${report.window} window: ${groupSources(
                shown.sources
              )
                .map((entry) => `${SOURCE_NAMES[entry.group]} ${entry.visitor_days}`)
                .join(', ')}`}
            >
              {groupSources(shown.sources).map((entry, index, all) => {
                const y = index * PATH_CHART.row;
                const share = pathShares(all)[index];
                return (
                  <g key={entry.group} data-testid={`activity-source-${entry.group}`}>
                    <text className={styles.pathLabel} x={0} y={y + 17}>
                      {SOURCE_NAMES[entry.group]}
                    </text>
                    <rect
                      className={styles.pathTrack}
                      x={PATH_CHART.bar}
                      y={y + 5}
                      width={PATH_CHART.width - PATH_CHART.bar - PATH_CHART.count}
                      height={16}
                      rx={4}
                    />
                    <rect
                      className={`${styles.pathBar} ${who === 'people' ? styles.whoPeople : styles.allLine}`}
                      x={PATH_CHART.bar}
                      y={y + 5}
                      width={share * (PATH_CHART.width - PATH_CHART.bar - PATH_CHART.count)}
                      height={16}
                      rx={4}
                    />
                    <text
                      className={styles.pathCount}
                      x={PATH_CHART.width}
                      y={y + 17}
                      textAnchor="end"
                    >
                      {entry.visitor_days.toLocaleString()}
                    </text>
                  </g>
                );
              })}
            </svg>
            <DataTable
              label="The sites that linked here"
              testId="activity-source-hosts"
              rows={shown.sources.slice(0, 10)}
              rowKey={(entry) => entry.host}
              columns={SOURCE_COLUMNS}
            />
          </>
        )}
        <p className={styles.muted}>
          The host of the page that linked here, kept on a page load and never its path. A link
          opened from a PDF, the resume among them, sends no referrer and reads as typed or unknown.
        </p>
      </section>
      <details className={styles.about}>
        <summary className={styles.aboutSummary}>Day by day</summary>
        <DataTable
          label="Visitor-days per day, by kind"
          testId="activity-days-table"
          rows={report.days}
          rowKey={(day) => day.day}
          columns={DAY_COLUMNS}
        />
      </details>
    </>
  );
}

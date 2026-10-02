/**
 * The two tiles under the activity totals: the recruiter's path from the site to the
 * resume, and where visitors came from, grouped by the kind of site and then listed by
 * host. Both are bars over the same visitor-days, so they sit together in their own file.
 */
import {
  SOURCE_NAMES,
  STEP_NAMES,
  groupSources,
  pathShares,
  type ActivityReport,
  type ActivityWho,
} from '../../../lib/activity';
import styles from '../ActivityCard/ActivityCard.module.css';
import chartStyles from '../charts/charts.module.css';
import cardStyles from '../shared/card.module.css';
import { type Column, DataTable } from '../DataTable';
import { PathBars } from '../PathBars';

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

/**
 * The recruiter's path and where they came from, for the people alone or for all traffic,
 * as the card's toggle says. The bars take the people's colour under Visitors only and
 * the everybody colour otherwise.
 */
export function ActivityPathTiles({ report, who }: { report: ActivityReport; who: ActivityWho }) {
  const shown = who === 'people' ? report.who.people : report.who.all;
  const tone = who === 'people' ? styles.whoPeople : chartStyles.allLine;
  return (
    <>
      <section className={styles.pathTile} data-testid="activity-path">
        <h3 className={cardStyles.cardTitle}>The recruiter's path</h3>
        <PathBars
          rows={shown.path.map((step, index) => ({
            key: step.step,
            name: STEP_NAMES[step.step],
            count: step.visitor_days,
            share: pathShares(shown.path)[index],
            testId: `activity-path-${step.step}`,
          }))}
          label={`The recruiter's path over the ${report.window} window: ${shown.path
            .map((step) => `${STEP_NAMES[step.step]} ${step.visitor_days}`)
            .join(', ')}`}
          tone={tone}
        />
        <p className={cardStyles.muted}>
          Visitor-days that asked for each step in the window: the page itself, the inventory's
          listing, About Steven, and the resume, which is the number this site exists for.
        </p>
      </section>
      <section className={styles.pathTile} data-testid="activity-sources">
        <h3 className={cardStyles.cardTitle}>Where they came from</h3>
        {shown.sources.length === 0 ? (
          <p className={cardStyles.muted}>
            Counted from 1.0.3.17, 24 September: no page load in the window has arrived since with
            its referring site kept.
          </p>
        ) : (
          <>
            <PathBars
              rows={groupSources(shown.sources).map((entry, index, all) => ({
                key: entry.group,
                name: SOURCE_NAMES[entry.group],
                count: entry.visitor_days,
                share: pathShares(all)[index],
                testId: `activity-source-${entry.group}`,
              }))}
              label={`Where they came from over the ${report.window} window: ${groupSources(
                shown.sources
              )
                .map((entry) => `${SOURCE_NAMES[entry.group]} ${entry.visitor_days}`)
                .join(', ')}`}
              tone={tone}
            />
            <DataTable
              label="The sites that linked here"
              testId="activity-source-hosts"
              rows={shown.sources.slice(0, 10)}
              rowKey={(entry) => entry.host}
              columns={SOURCE_COLUMNS}
            />
          </>
        )}
        <p className={cardStyles.muted}>
          The host of the page that linked here, kept on a page load and never its path. A link
          opened from a PDF, the resume among them, sends no referrer and reads as typed or unknown.
        </p>
      </section>
    </>
  );
}

// The activity card's sentences: the totals line under the chart, the
// collector's one line, and what keeping activity has cost. Its own file
// because each sentence has to say exactly what its numbers measure, and that
// care is easier to check with the words gathered in one place.

import type { ActivityReport, ActivityWho } from './activityTypes';

// #region totals-words
/**
 * The totals line under the chart. The report carries two request counts that
 * measure different things. `totals.requests` is summed from the hour rows,
 * which start at `since`, so it is the window's own count, of everyone. A
 * kind's `requests` is summed from its visitor-day rows, and a visitor-day row
 * is a whole UTC day: on the first day of the window it also carries the
 * requests that came before `since`. So only the hour rows' count is called
 * the window's, and a kind's count is said to be over its visitor-days.
 */
export function totalsSentence(
  report: Pick<ActivityReport, 'totals' | 'who'>,
  who: ActivityWho
): string {
  const { people, scanners, self, all } = report.who;
  const inWindow = `${report.totals.requests.toLocaleString()} requests in the window`;
  return who === 'people'
    ? `${people.visitor_days.toLocaleString()} visitor-days that looked like people across the days in the window, with ${people.requests.toLocaleString()} requests over those whole days; ${inWindow} from everyone.`
    : `${all.visitor_days.toLocaleString()} visitor-days across the days in the window: ${people.visitor_days.toLocaleString()} people, ${scanners.visitor_days.toLocaleString()} scanners and crawlers, ${self.visitor_days.toLocaleString()} the site's own reads; ${inWindow}.`;
}
// #endregion totals-words

// #region collector-words
/** The collector's one line: fine, or what went wrong first (failed batches outrank drops). */
export function collectorSummary(collector: ActivityReport['collector']): string {
  const plural = (count: number, one: string, many: string) =>
    `${count.toLocaleString()} ${count === 1 ? one : many}`;
  if (collector.failed_batches > 0)
    return `Collector: ${plural(collector.failed_batches, 'batch', 'batches')} failed`;
  if (collector.dropped > 0)
    return `Collector: ${plural(collector.dropped, 'hit', 'hits')} dropped`;
  return 'Collector fine';
}

/**
 * What keeping activity has cost, in a sentence. The count is this container's
 * own since it started: the other container writes to the same keeper and
 * counts its own, so the sentence says whose it is.
 */
export function costSentence(cost: NonNullable<ActivityReport['cost']>): string {
  const failed = cost.failures > 0 ? `, ${cost.failures.toLocaleString()} of them failed` : '';
  return `Keeping it has cost this container ${cost.request_units.toLocaleString()} request units over ${cost.operations.toLocaleString()} operations since it started${failed}.`;
}
// #endregion collector-words

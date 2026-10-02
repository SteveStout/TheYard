// The activity card's days: a day's count under the toggle, the graph's lines
// of unique visitors per UTC day, and the visitor rows gathered under their day.
// Its own file because it is the one place a day is counted, and both the
// chart and the visitor table lean on it.

import type {
  ActivityDay,
  ActivityKinds,
  ActivitySeries,
  ActivityVisitor,
  ActivityWho,
} from './activityTypes';

// #region days
/** A day's visitor-days under the toggle: the people only, or the three kinds together. */
export function countFor(kinds: ActivityKinds, who: ActivityWho): number {
  return who === 'people' ? kinds.people : kinds.people + kinds.scanners + kinds.self;
}

/**
 * The graph's series: unique visitors per UTC day, one line for everybody and
 * one per store, on the point shape the line geometry already draws. Under
 * Visitors only every line counts people; under All traffic, all three kinds.
 */
export function dayLines(
  days: ActivityDay[],
  stores: string[],
  who: ActivityWho = 'all'
): ActivitySeries[] {
  const all: ActivitySeries = {
    store: 'all',
    name: who === 'people' ? 'People' : 'All traffic',
    points: days.map((day) => ({ at: day.day, requests: countFor(day, who), bots: day.scanners })),
  };
  const perStore = stores.map((store) => ({
    store,
    name: store,
    points: days.map((day) => {
      const entry = day.by_store.find((candidate) => candidate.store === store);
      return { at: day.day, requests: entry ? countFor(entry, who) : 0, bots: 0 };
    }),
  }));
  return [all, ...perStore];
}

/** The visitor rows grouped by UTC day, newest day first, with the day's own totals for its heading. */
export function groupByDay(
  rows: ActivityVisitor[]
): { day: string; visitors: number; requests: number; rows: ActivityVisitor[] }[] {
  const byDay = new Map<string, ActivityVisitor[]>();
  for (const row of rows) {
    const list = byDay.get(row.day) ?? [];
    list.push(row);
    byDay.set(row.day, list);
  }
  return [...byDay.entries()]
    .sort(([a], [b]) => b.localeCompare(a))
    .map(([day, dayRows]) => ({
      day,
      visitors: new Set(dayRows.map((row) => row.visitor)).size,
      requests: dayRows.reduce((sum, row) => sum + row.requests, 0),
      rows: dayRows,
    }));
}
// #endregion days

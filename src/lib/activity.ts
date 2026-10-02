// The activity card's arithmetic, kept out of the component so it can be
// tested without a browser (ADR: Site activity, and the line an address does
// not cross). This file is the list of its parts: one line per part, with the
// file that holds it, so every importer keeps reading from here.

export * from './activityTypes'; // the wire shapes and the names the card shows for them
export * from './activityDays'; // a day's count, the lines per UTC day, the rows by day
export * from './activityPages'; // page names, the recruiter's path bars, where visitors came from
export * from './activityChart'; // the chart's drawing area, lines, stacked bands and labels
export * from './activityVisitorOrder'; // the visitor table's sort
export * from './activityWords'; // the totals line, the collector's line, the cost sentence

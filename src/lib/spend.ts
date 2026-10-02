/**
 * What Azure charges, for the cost card on the Admin tab: the answer's shape,
 * the window a chart window maps to, money in words, and the geometry of the
 * three pictures (the spend line with its forecast, the donut by resource, the
 * bars by type). The server owns every figure; these files only lay out what
 * the server said (ADR: What Azure charges).
 *
 * No React in here: each is a plain function, so each is a rule a test can read.
 * This file is the list of parts; each line names the file beside it that holds one.
 */

// spend/costReport.ts: the cost answer's shape, as the server writes it
export type { CostKind, CostPoint, CostReport, CostSlice, CostWindow } from './spend/costReport';
// spend/costWords.ts: the cost window, money, days and months in words, and the headline
export { costWindowFor, dayShort, dayWords, headline, money, monthWords } from './spend/costWords';
// spend/spendLine.ts: the running total and the forecast as paths, with the axis
export type { LineBox, SpendLine } from './spend/spendLine';
export { niceCeiling, SPEND_BOX, spendLine } from './spend/spendLine';
// spend/spendDonut.ts: the donut by resource as SVG arcs
export type { DonutArc } from './spend/spendDonut';
export { donutArcs } from './spend/spendDonut';
// spend/spendBars.ts: the bars by type, and the sentences under the resource and type pictures
export { rateWords, resourcesWords, typeBars } from './spend/spendBars';

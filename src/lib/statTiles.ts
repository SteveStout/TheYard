/**
 * The strip of stat tiles across the top of the Admin tab. The tiles answer four
 * questions, in the order someone on call asks them: is it up, is it fast, is it
 * costing anything, what broke. Each tile is one number, a short line saying what
 * the number counts, and a tone (good, warn, bad, plain or waiting)
 * (ADR: The Admin tab, as a product).
 *
 * There is no React here. Plain functions turn what the tab has already read into
 * tiles, so the rules that make a tile amber or red are easy to read and test.
 * A reading that has not arrived yet gives a tile that says so, never a zero.
 * This file is the list of parts; each line names the file beside it that holds one.
 */

// statTiles/tileTypes.ts: what a tile holds, its ring, and the readings tiles are built from
export type {
  StatTile,
  TileQuestion,
  TileReadings,
  TileRing,
  TileTone,
} from './statTiles/tileTypes';
// statTiles/tileRing.ts: a share of a known whole as a ring, and the ring as an SVG stroke
export { ringOf, ringStroke } from './statTiles/tileRing';
// statTiles/tileWords.ts: uptime, milliseconds and the hour's stretch in words, and a tile's sentence
export {
  millisecondsWords,
  RING_HOLDS,
  ringStretch,
  ringStretchInFull,
  tileSentence,
  uptimeWords,
} from './statTiles/tileWords';
// statTiles/tileRules.ts: every tile in display order, and the thresholds that colour them
export {
  MEMORY_BAD_SHARE,
  MEMORY_WARN_SHARE,
  QUIET_BELOW_REQUESTS,
  SLOW_P95_MS,
  tilesFrom,
  VERY_SLOW_P95_MS,
} from './statTiles/tileRules';
// statTiles/visitorsToday.ts: today's visitor count from the activity report's days
export { visitorsOn } from './statTiles/visitorsToday';
// statTiles/coldStart.ts: the minutes after a start that the speed tile leaves out
export { afterColdStart, COLD_START_MINUTES } from './statTiles/coldStart';
// statTiles/hourTiming.ts: the hour's median and 95th percentile, pooled, nearest rank
export { hourTiming } from './statTiles/hourTiming';
// statTiles/sparkline.ts: the line under a tile as polyline points, and its caption
export { sparkCaption, sparkRuns } from './statTiles/sparkline';

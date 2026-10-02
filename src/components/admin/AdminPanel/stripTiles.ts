// Builds what the Admin tab's strip shows from data already read: the tiles, the "This hour"
// summary and the caption over the sparklines. It applies the rules in src/lib/statTiles.ts and
// src/lib/bench.ts to the shared reads and draws nothing, so the page component stays a layout.
// It is its own file because turning readings into tiles is one job, with no React in it.
import {
  clockLabel,
  fromFirstReading,
  keptSparks,
  type MachineWindow,
  timeline,
  trafficTotals,
  windowName,
} from '../../../lib/machineChart';
import {
  afterColdStart,
  hourTiming,
  sparkCaption,
  type StatTile,
  tilesFrom,
} from '../../../lib/statTiles';
import { hourGlance, type HourGlance } from '../../../lib/bench';
import type { ErrorEntry, Fetched, Health, Machines } from '../shared/types';
import { hourSlots } from '../shared/common';

/** The readings the strip is built from, all supplied by reads the tab already makes. */
export type StripReadings = {
  /** The last good machine stats, or null before the first answer. */
  seen: Machines | null;
  /** The chart window the user picked. */
  window: MachineWindow;
  health: Fetched<Health>;
  errors: Fetched<ErrorEntry[]>;
  pagesSeen: { checked: number; up: number } | null;
  visitorsToday: number | null;
};

/** What the strip draws: the tiles, the hour's summary and the sparklines' caption. */
export type StripView = {
  tiles: StatTile[];
  glance: HourGlance;
  caption: string;
};

/**
 * Turns the shared readings into the strip's tiles, the "This hour" summary and
 * the caption that says what the lines under the tiles are of. Because the
 * strip and the cards share the same data, a tile can never disagree with the
 * card it links to.
 */
export function stripTiles({
  seen,
  window: machineWindow,
  health,
  errors,
  pagesSeen,
  visitorsToday,
}: StripReadings): StripView {
  // #region tiles
  // Build the tiles from data already read above, using the rules in
  // statTiles.ts. Because the strip and the cards share the same data, a tile
  // can never disagree with the card it links to.
  const hour = seen === null ? null : hourSlots(seen);
  // The small line charts (sparklines) under the tiles use the chart window
  // the user picked. For any window longer than 1h, use the stored history,
  // but only once it has arrived for that window and the store keeps it.
  const keptHistory =
    seen !== null &&
    machineWindow !== '1h' &&
    seen.history !== undefined &&
    seen.history.window === machineWindow &&
    seen.history.available
      ? seen.history
      : null;
  const keptLines =
    keptHistory === null || machineWindow === '1h'
      ? null
      : keptSparks(
          fromFirstReading(
            timeline(
              keptHistory.buckets,
              machineWindow,
              keptHistory.bucket_minutes,
              new Date(keptHistory.as_of)
            )
          )
        );
  const lastSample =
    seen !== null && seen.container.samples.length > 0
      ? seen.container.samples[seen.container.samples.length - 1]
      : null;
  // A cold start (the slow first minutes after the process starts) should not
  // count against the hour's speed (see statTiles.ts). Start time is the newest
  // sample's time minus uptime. Both come from the server's answer, so this
  // code never reads the browser's clock.
  const startedAt =
    seen === null || lastSample === null
      ? null
      : new Date(new Date(lastSample.at).getTime() - seen.container.uptime_seconds * 1000);
  const warmed = hour === null ? null : afterColdStart(hour, startedAt);
  // Request and error counts still cover the whole hour. Only the timings
  // (p50 is the median, p95 the 95th percentile) skip the cold-start minutes.
  const warmTiming = warmed === null ? null : hourTiming(warmed.warm);
  const hourTotals =
    hour === null || warmTiming === null
      ? null
      : {
          ...trafficTotals(hour, 1),
          warm_requests: warmTiming.requests,
          p50_ms: warmTiming.p50_ms,
          p95_ms: warmTiming.p95_ms,
          slowest_at: warmTiming.slowest_at,
        };
  const coldStart =
    warmed !== null && warmed.left_out.some((slot) => (slot.requests ?? 0) > 0)
      ? clockLabel(warmed.left_out[0].at)
      : null;
  const tiles = tilesFrom({
    health: health !== null && health !== 'failed' ? health : null,
    pages: pagesSeen,
    traffic:
      hourTotals === null
        ? null
        : {
            ...hourTotals,
            slowest_label:
              hourTotals.slowest_at === null ? null : clockLabel(hourTotals.slowest_at),
            cold_start_label: coldStart,
          },
    memory:
      seen === null || lastSample === null
        ? null
        : { working_set_mb: lastSample.working_set_mb, limit_mb: seen.container.memory_limit_mb },
    charged:
      seen === null
        ? null
        : seen.document.available
          ? {
              request_units: seen.document.request_units,
              free_per_second: seen.document.free_request_units_per_second,
            }
          : 'none',
    errors: errors !== null && errors !== 'failed' ? errors.length : null,
    visitorsToday,
    sparks:
      seen === null
        ? undefined
        : keptLines !== null
          ? { ...keptLines, charged: seen.document.available ? keptLines.charged : undefined }
          : {
              speed: hour?.map((slot) => slot.p50_ms),
              memory: seen.container.samples.map((sample) => sample.working_set_mb),
              charged: seen.document.available
                ? seen.document.minutes.map((minute) => minute.request_units)
                : undefined,
              errors: hour?.map((slot) => slot.server_errors),
            },
  });
  // #endregion tiles
  const glance = hourGlance(
    hourTotals === null
      ? null
      : {
          requests: hourTotals.requests,
          server_errors: hourTotals.server_errors,
          client_errors: hourTotals.client_errors,
          p50_ms: hourTotals.p50_ms,
          p95_ms: hourTotals.p95_ms,
        }
  );
  // What the lines under the tiles are of: the hour, the kept history, a
  // window the store does not keep, or a window still being read.
  const caption = sparkCaption(
    windowName(machineWindow).toLowerCase(),
    machineWindow === '1h'
      ? 'hour'
      : keptLines !== null
        ? 'kept'
        : seen?.history?.window === machineWindow
          ? 'not-kept'
          : 'reading'
  );
  return { tiles, glance, caption };
}

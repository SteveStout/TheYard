/**
 * Traffic as slots, whatever the window: the hour folded from the request
 * ring a minute at a time, a kept window from its buckets, the lines under the
 * tiles, the totals over the charts, and the proof's paired bars. One shape
 * for both sources, so the traffic charts cannot disagree about a gap or a
 * zero. Its own file because traffic is read and added up apart from the
 * machines; the docs quote the whole traffic region from here.
 */

import type { KeptBucket } from './machineWindows';

// #region traffic
/**
 * Traffic, in one shape whatever the window (ADR: The Admin tab, as a
 * product). The hour comes from the request ring a minute at a time; a kept
 * window comes from buckets of kept minutes. Both become the same slots, so
 * the four traffic charts are drawn by one piece of code and cannot disagree
 * about what a gap or a zero means: a slot nobody measured is null, and a
 * measured slot in which nobody asked for anything is zero requests with no
 * median, because there is no median of nothing.
 */
export type TrafficMinute = {
  at: string;
  requests: number;
  p50_ms: number;
  p95_ms: number;
  server_errors: number;
  client_errors: number;
  /** Every request's time in the minute, sorted, for the hour's own percentiles (statTiles.ts, hourTiming). */
  durations_ms?: number[];
  /**
   * True on the oldest minute of a full request ring: the ring pushed out what
   * came before it, so its counts are short and nothing older is held. The ring
   * is a number of requests, not an hour, and this is where it stops reaching.
   */
  clipped?: boolean;
};

/** One slot of a traffic chart, from a minute of the ring or a kept bucket; null wherever nothing was measured. */
export type TrafficSlot = {
  at: string;
  /** Requests a minute, or null where nothing was measured. */
  requests: number | null;
  p50_ms: number | null;
  p95_ms: number | null;
  server_errors: number | null;
  client_errors: number | null;
  /** The minute's request times, sorted; absent on a kept bucket, which holds only its percentiles. */
  durations_ms?: number[];
  /** The minute the request ring stops reaching back at, when it is full (TrafficMinute.clipped). */
  clipped?: boolean;
};

/**
 * The hour the ring holds: every minute from the oldest request it still has,
 * or an hour ago if that is later, to now. Minutes inside that span with no
 * request are true zeros, because the process was up and nobody asked.
 */
export function hourOfTraffic(minutes: TrafficMinute[], asOf: Date): TrafficSlot[] {
  const step = 60_000;
  const end = Math.floor(asOf.getTime() / step) * step;
  const held = new Map<number, TrafficMinute>();
  let oldest = end;
  for (const minute of minutes) {
    const at = Math.floor(new Date(minute.at).getTime() / step) * step;
    if (Number.isNaN(at)) continue;
    held.set(at, minute);
    if (at < oldest) oldest = at;
  }
  const start = Math.max(oldest, end - 59 * step);
  const slots: TrafficSlot[] = [];
  for (let at = start; at <= end; at += step) {
    const minute = held.get(at);
    slots.push({
      at: new Date(at).toISOString(),
      requests: minute?.requests ?? 0,
      p50_ms: minute?.p50_ms ?? null,
      p95_ms: minute?.p95_ms ?? null,
      server_errors: minute?.server_errors ?? 0,
      client_errors: minute?.client_errors ?? 0,
      durations_ms: minute?.durations_ms ?? [],
      ...(minute?.clipped === true ? { clipped: true } : {}),
    });
  }
  return slots;
}

/** A kept window's slots as traffic: counts become a rate a minute over the minutes the bucket holds. */
export function keptTraffic(slots: { at: string; bucket: KeptBucket | null }[]): TrafficSlot[] {
  const rate = (count: number, minutes: number) =>
    minutes <= 0 ? null : Math.round((count / minutes) * 100) / 100;
  return slots.map(({ at, bucket }) =>
    bucket === null
      ? { at, requests: null, p50_ms: null, p95_ms: null, server_errors: null, client_errors: null }
      : {
          at,
          requests: rate(bucket.requests, bucket.minutes),
          p50_ms: bucket.p50_ms,
          p95_ms: bucket.p95_ms,
          server_errors: rate(bucket.server_errors, bucket.minutes),
          client_errors: rate(bucket.client_errors, bucket.minutes),
        }
  );
}

/**
 * The lines under the tiles over a kept window (ADR: The Admin tab, as a
 * product, the addendum on one window for every chart): the same four
 * readings the hour's lines are drawn from, one value a bucket, and null
 * where the store holds no bucket, so a day the site was down is a gap under
 * the tile exactly as it is on the chart.
 */
export function keptSparks(slots: { at: string; bucket: KeptBucket | null }[]): {
  speed: (number | null)[];
  memory: (number | null)[];
  charged: (number | null)[];
  errors: (number | null)[];
} {
  return {
    // The line under the speed tile is the typical answer, the same reading as the number over it.
    speed: slots.map(({ bucket }) => bucket?.p50_ms ?? null),
    memory: slots.map(({ bucket }) => bucket?.working_set_mb ?? null),
    charged: slots.map(({ bucket }) => bucket?.request_units ?? null),
    errors: slots.map(({ bucket }) => bucket?.server_errors ?? null),
  };
}

/**
 * What the hour's minutes add up to, from the slots themselves. A slot is one
 * minute of the ring, so `minutesPerSlot` is 1. A kept window's totals do not
 * come from here: its slots are rates over the minutes each bucket holds, and a
 * rate times the bucket's width overstates the newest bucket, which holds only
 * the minutes kept so far, so the server adds the counts up (keptWindowTotals).
 *
 * `ring_minutes` is how many minutes the figures reach back when the request
 * ring stops short of the hour, counted from the minute the server marked, and
 * null when the slots are the whole hour or everything since the process began.
 */
export function trafficTotals(slots: TrafficSlot[], minutesPerSlot: number) {
  let requests = 0;
  let serverErrors = 0;
  let clientErrors = 0;
  let slowest: number | null = null;
  let slowestAt: string | null = null;
  for (const slot of slots) {
    requests += (slot.requests ?? 0) * minutesPerSlot;
    serverErrors += (slot.server_errors ?? 0) * minutesPerSlot;
    clientErrors += (slot.client_errors ?? 0) * minutesPerSlot;
    if (slot.p95_ms !== null && (slowest === null || slot.p95_ms > slowest)) {
      slowest = slot.p95_ms;
      slowestAt = slot.at;
    }
  }
  const clippedAt = slots.findIndex((slot) => slot.clipped === true);
  return {
    requests: Math.round(requests),
    server_errors: Math.round(serverErrors),
    client_errors: Math.round(clientErrors),
    slowest_p95_ms: slowest,
    // Where to look: a tile that says "slow" and not "when" sends somebody through an hour of rows.
    slowest_at: slowestAt,
    ring_minutes: clippedAt < 0 ? null : (slots.length - clippedAt) * minutesPerSlot,
  };
}

/** A kept window's totals as the endpoint sends them, counted on the server from the buckets' own counts. */
export type KeptTotals = {
  requests: number;
  server_errors: number;
  client_errors: number;
  /** The worst single minute's 95th percentile in the window, or null when no minute had a request. */
  worst_minute_p95_ms: number | null;
};

/**
 * The totals over a kept window's charts, from what the server counted, in the
 * shape the hour's come in. Null when the answer carries none, which a window
 * the store could not read never does.
 */
export function keptWindowTotals(history: { window: string; totals?: KeptTotals | null }) {
  const totals = history.totals;
  if (totals === undefined || totals === null) return null;
  return {
    requests: totals.requests,
    server_errors: totals.server_errors,
    client_errors: totals.client_errors,
    slowest_p95_ms: totals.worst_minute_p95_ms,
    ring_minutes: null,
  };
}

/**
 * The proof's paired medians as bars (ADR: Same performance, proven): one
 * pair a path, each bar a share of the longest median on the card, so the
 * eye reads what the table says, that most pairs are the same length and the
 * ones that are not differ by a round trip.
 */
export type PairedBar = { label: string; bars: { store: string; ms: number; share: number }[] };

/** The proof's rows as paired bars: rows with no samples are left off, and each bar is a share of the longest median. */
export function pairedBars(
  rows: { label: string; cells: { store: string; samples: number; p50_ms: number }[] }[]
): PairedBar[] {
  const measured = rows
    .map((row) => ({ label: row.label, cells: row.cells.filter((cell) => cell.samples > 0) }))
    .filter((row) => row.cells.length > 0);
  const longest = measured.reduce(
    (most, row) => Math.max(most, ...row.cells.map((cell) => cell.p50_ms)),
    0
  );
  return measured.map((row) => ({
    label: row.label,
    bars: row.cells.map((cell) => ({
      store: cell.store,
      ms: cell.p50_ms,
      // A bar for a zero is still drawn, one per cent wide, so a path both
      // stores answer in no time reads as two equal bars and not as nothing.
      share: longest <= 0 ? 1 : Math.max(1, Math.round((cell.p50_ms / longest) * 100)),
    })),
  }));
}
// #endregion traffic

/**
 * The strip of stat tiles across the top of the Admin tab (ADR-080). The tiles
 * answer four questions, in the order someone on call asks them: is it up, is it
 * fast, is it costing anything, what broke. Each tile is one number, a short line
 * saying what the number counts, and a tone (good, warn, bad, plain or waiting).
 *
 * There is no React here. Plain functions turn what the tab has already read into
 * tiles, so the rules that make a tile amber or red are easy to read and test.
 * A reading that has not arrived yet gives a tile that says so, never a zero.
 */

import { formatNumber } from './format';

export type TileTone = 'good' | 'warn' | 'bad' | 'plain' | 'waiting';

/** The four questions. Each is also the section of the tab a tile links down to. */
export type TileQuestion = 'up' | 'fast' | 'cost' | 'broke';

export type StatTile = {
  key: string;
  question: TileQuestion;
  label: string;
  value: string;
  /**
   * The line under the number. It must fit in two lines on the narrowest tile,
   * because a line cut off with an ellipsis looks like a fault.
   * tests/e2e/mobile.spec.ts checks the longest cases.
   */
  detail: string;
  /**
   * The rest of the sentence, when a reading has more to say than fits. It starts
   * with its own joining word or punctuation, so `detail + more` reads as one
   * sentence. Screen readers and the tile's tooltip get it; the card further down
   * the page shows it in full.
   */
  more?: string;
  tone: TileTone;
  /** The last hour behind the number, oldest first. null marks an unmeasured minute. */
  spark?: (number | null)[];
  /**
   * A ring (a small circular gauge) beside the number. Only for a number that is
   * a share of a known whole: checks passing, pages up, memory used of its limit.
   * A number with no whole, like milliseconds, gets no ring, because a ring drawn
   * for looks is a gauge that measures nothing (ADR-081).
   */
  ring?: TileRing;
  /**
   * Whether the tile reserves room for a ring, from the first paint, even before
   * its reading arrives. Tiles that never draw a ring reserve no room. tilesFrom
   * sets this, so the decision lives in one place.
   */
  ringed: boolean;
};

export type TileRing = {
  /** From 0 to 1, clamped. */
  share: number;
  /** The text in the middle of the ring. Keep it short enough to fit. */
  label: string;
};

/** A part of a whole as a ring. Returns undefined when there is no whole to measure against. */
export function ringOf(part: number, whole: number, label: string): TileRing | undefined {
  if (!(whole > 0) || Number.isNaN(part)) return undefined;
  return { share: Math.min(1, Math.max(0, part / whole)), label };
}

/**
 * The ring as an SVG stroke dash: the circle's full length, and how much of it
 * to leave undrawn. Both are rounded to one decimal place.
 */
export function ringStroke(share: number, radius: number): { length: number; gap: number } {
  const length = 2 * Math.PI * radius;
  const held = Math.min(1, Math.max(0, share));
  return { length: Math.round(length * 10) / 10, gap: Math.round(length * (1 - held) * 10) / 10 };
}

/**
 * Everything the tiles are built from. Each field is null until it has been read.
 * "The ring" below means the ring buffer of recent readings that the server
 * process keeps in memory, not the ring gauge drawn on a tile.
 */
export type TileReadings = {
  health: {
    status: string;
    uptime_seconds: number;
    version: string;
    commit: string;
    checks: { status: string }[];
  } | null;
  pages: { checked: number; up: number } | null;
  /**
   * The last hour's traffic: total requests and errors, plus the timing a visitor
   * felt, from hourTiming() over the warm minutes (the minutes after the cold
   * start).
   */
  traffic: {
    requests: number;
    server_errors: number;
    client_errors: number;
    /** Requests in the warm minutes. The two percentiles are over these. */
    warm_requests: number;
    p50_ms: number | null;
    p95_ms: number | null;
    /** The minute of the slowest request, as a clock time. Absent on a quiet hour. */
    slowest_label?: string | null;
    /** The minute of a cold start that was left out, as a clock time. Absent if none. */
    cold_start_label?: string | null;
  } | null;
  memory: { working_set_mb: number; limit_mb: number } | null;
  /**
   * What the document store (Cosmos DB) charged in request units over the ring,
   * and how many units a second are free. 'none' when no document store is in use.
   */
  charged: { request_units: number; free_per_second: number } | 'none' | null;
  /** Errors the server and the browser reported, over the ring. */
  errors: number | null;
  visitorsToday: number | null;
  /** The last hour behind four of the tiles, one minute or one sample per entry. */
  sparks?: {
    speed?: (number | null)[];
    memory?: (number | null)[];
    charged?: (number | null)[];
    errors?: (number | null)[];
  };
};

/** Uptime as the two largest non-zero units: "2d 5h", "3h 12m" or "7m". */
export function uptimeWords(totalSeconds: number): string {
  const days = Math.floor(totalSeconds / 86_400);
  const hours = Math.floor((totalSeconds % 86_400) / 3_600);
  const minutes = Math.floor((totalSeconds % 3_600) / 60);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m`;
}

/**
 * A time in words. The readings are stored as whole milliseconds, so 0 really
 * means "under a millisecond". Every place that shows a time uses this, so they
 * all say it the same way.
 */
export function millisecondsWords(ms: number): string {
  return ms === 0 ? 'under 1 ms' : `${ms.toLocaleString('en-US')} ms`;
}

/** A tile's whole sentence: the line under its number plus the part left out. */
export function tileSentence(tile: Pick<StatTile, 'detail' | 'more'>): string {
  return tile.detail + (tile.more ?? '');
}

/**
 * The speed tile's line and the rest of its sentence. The line says what the
 * number is measured over. The rest adds the hour, the slowest minute, and any
 * cold start that was left out. `judged` is true when the hour was busy enough
 * to colour; `both` is the median and 95th percentile in words, or ''.
 */
function speedLine(
  t: NonNullable<TileReadings['traffic']>,
  judged: boolean,
  both: string
): { detail: string; more?: string } {
  const tail =
    (t.slowest_label ? `, slowest at ${t.slowest_label}` : '') +
    (t.cold_start_label ? `; the start at ${t.cold_start_label} is left out` : '');
  if (judged && t.p95_ms !== null) {
    return {
      detail: `95th ${formatNumber(t.p95_ms)} ms over ${formatNumber(t.warm_requests)} requests`,
      more: ` in the last hour${tail}`,
    };
  }
  if (t.warm_requests > 0) {
    return {
      detail: `${t.warm_requests} requests in the last hour`,
      more: `, too few to judge${both === '' ? '' : `; ${both}`}${tail}`,
    };
  }
  return {
    detail: `${t.requests} requests in the last hour`,
    ...(tail === '' ? {} : { more: tail }),
  };
}

/** A tile whose reading has not arrived yet. */
const waiting = (key: string, question: TileQuestion, label: string): Omit<StatTile, 'ringed'> => ({
  key,
  question,
  label,
  value: '…',
  detail: 'not read yet',
  tone: 'waiting',
});

// #region tile-rules
/**
 * The colour rules, all in one place. Amber means "worth a look". Red means
 * "someone should be looking". A failing check or a page that is down is red.
 * A slow 95th percentile (p95: 95 of every 100 requests were faster), memory
 * past 80% of its limit, or a refused request is amber. The thresholds are
 * this site's own, set from what it measures on a quiet day, and open to change.
 */
export const SLOW_P95_MS = 1_000;
export const VERY_SLOW_P95_MS = 3_000;
/**
 * The speed tile only takes a colour when the hour has at least this many warm
 * requests. With fewer, the 95th percentile is really just the one slowest
 * request, so the tile stays plain and shows the request count instead.
 */
export const QUIET_BELOW_REQUESTS = 20;

/**
 * The tiles whose number is a share of a known whole. Only these reserve room
 * for a ring, so the other tiles keep their full width for text on a phone.
 */
const RINGED = new Set(['health', 'pages', 'memory']);
export const MEMORY_WARN_SHARE = 0.8;
export const MEMORY_BAD_SHARE = 0.95;

/** Builds every tile, in display order, from the readings the tab holds. */
export function tilesFrom(readings: TileReadings): StatTile[] {
  const { health, pages, traffic, memory, charged, errors, visitorsToday, sparks } = readings;
  const tiles: Omit<StatTile, 'ringed'>[] = [];

  if (health === null) {
    tiles.push(waiting('version', 'up', 'Version'), waiting('health', 'up', 'Health'));
  } else {
    const passing = health.checks.filter((check) => check.status === 'pass').length;
    tiles.push({
      key: 'version',
      question: 'up',
      label: 'Version',
      value: health.version,
      detail: `${health.commit}, up ${uptimeWords(health.uptime_seconds)}`,
      tone: 'plain',
    });
    tiles.push({
      key: 'health',
      question: 'up',
      label: 'Health',
      value: health.status === 'healthy' ? 'Healthy' : health.status,
      detail: `${passing} of ${health.checks.length} checks pass`,
      tone:
        passing === health.checks.length ? 'good' : health.status === 'healthy' ? 'warn' : 'bad',
      ring: ringOf(passing, health.checks.length, `${passing}/${health.checks.length}`),
    });
  }

  tiles.push(
    pages === null
      ? waiting('pages', 'up', 'Pages')
      : {
          key: 'pages',
          question: 'up',
          label: 'Pages',
          value: `${pages.up} of ${pages.checked}`,
          detail:
            pages.up === pages.checked
              ? 'every address it serves is up'
              : `${pages.checked - pages.up} down`,
          tone: pages.checked === 0 ? 'warn' : pages.up === pages.checked ? 'good' : 'bad',
          ring: ringOf(
            pages.up,
            pages.checked,
            `${Math.floor((pages.up / Math.max(1, pages.checked)) * 100)}%`
          ),
        }
  );

  // Speed, as a visitor felt it. The median (p50) is the big number and the 95th
  // percentile goes in the line under it, both over the warm requests. Below
  // QUIET_BELOW_REQUESTS the hour is too quiet to colour, and the tile says so.
  const busy = traffic !== null && traffic.warm_requests >= QUIET_BELOW_REQUESTS;
  const percentiles = (t: NonNullable<TileReadings['traffic']>) =>
    t.p50_ms === null || t.p95_ms === null
      ? ''
      : `typical ${formatNumber(t.p50_ms)} ms, 95th ${formatNumber(t.p95_ms)} ms`;
  tiles.push(
    traffic === null
      ? waiting('speed', 'fast', 'Typical answer')
      : {
          key: 'speed',
          question: 'fast',
          label: 'Typical answer',
          // With no median to show, the hour is "quiet", unless the only requests
          // came during the cold start, in which case the process is "warming".
          value:
            busy && traffic.p50_ms !== null
              ? millisecondsWords(traffic.p50_ms)
              : traffic.warm_requests === 0 && traffic.requests > 0 && traffic.cold_start_label
                ? 'warming'
                : 'quiet',
          ...speedLine(traffic, busy, percentiles(traffic)),
          tone:
            !busy || traffic.p95_ms === null
              ? 'plain'
              : traffic.p95_ms >= VERY_SLOW_P95_MS
                ? 'bad'
                : traffic.p95_ms >= SLOW_P95_MS
                  ? 'warn'
                  : 'good',
          spark: sparks?.speed,
        }
  );

  if (memory === null || memory.limit_mb <= 0) {
    tiles.push(waiting('memory', 'cost', 'Memory'));
  } else {
    const share = memory.working_set_mb / memory.limit_mb;
    tiles.push({
      key: 'memory',
      question: 'cost',
      label: 'Memory',
      value: `${Math.round(share * 100)}%`,
      detail: `${formatNumber(Math.round(memory.working_set_mb))} of ${formatNumber(Math.round(memory.limit_mb))} MB`,
      tone: share >= MEMORY_BAD_SHARE ? 'bad' : share >= MEMORY_WARN_SHARE ? 'warn' : 'good',
      spark: sparks?.memory,
      ring: ringOf(memory.working_set_mb, memory.limit_mb, `${Math.round(share * 100)}%`),
    });
  }

  tiles.push(
    charged === null
      ? waiting('charged', 'cost', 'Request units')
      : charged === 'none'
        ? {
            key: 'charged',
            question: 'cost',
            label: 'Request units',
            value: 'none',
            detail: 'no document store is answering here',
            tone: 'plain',
          }
        : {
            key: 'charged',
            question: 'cost',
            label: 'Request units',
            value: `${Math.round(charged.request_units * 10) / 10}`,
            // This is a total, not a rate, so it is never coloured against the free
            // allowance per second. The machines card compares the busiest minute.
            detail: `in the ring; ${formatNumber(charged.free_per_second)} a second is free`,
            tone: 'plain',
            spark: sparks?.charged,
          }
  );

  // Errors: the big number is the larger of two counts, the server's 5xx answers
  // this hour and the errors reported over the ring. The line shows both. Any
  // error at all is red.
  if (errors === null && traffic === null) {
    tiles.push(waiting('errors', 'broke', 'Errors'));
  } else {
    const failing = traffic?.server_errors ?? 0;
    const reported = errors ?? 0;
    tiles.push({
      key: 'errors',
      question: 'broke',
      label: 'Errors',
      value: `${Math.max(failing, reported)}`,
      detail:
        failing === 0 && reported === 0
          ? 'no 5xx answered and none reported'
          : `${failing} answered 5xx, ${reported} reported`,
      ...(failing === 0 && reported === 0 ? {} : { more: "; the 5xx are the last hour's" }),
      tone: failing > 0 || reported > 0 ? 'bad' : 'good',
      spark: sparks?.errors,
    });
  }

  tiles.push(
    visitorsToday === null
      ? waiting('visitors', 'fast', 'Visitors today')
      : {
          key: 'visitors',
          question: 'fast',
          label: 'Visitors today',
          value: `${visitorsToday}`,
          detail: 'counted by a daily hash, bots left out',
          tone: 'plain',
        }
  );

  return tiles.map((tile) => ({ ...tile, ringed: RINGED.has(tile.key) }));
}
// #endregion tile-rules

/**
 * Today's visitor count from an activity report's list of days. No row for
 * today means zero. Prefer `people`: the older `humans` count also included
 * App Service's own requests from the loopback address, so it is only a
 * fallback for a report that has no `people` field.
 */
export function visitorsOn(
  days: { day: string; humans: number; people?: number }[],
  now: Date
): number {
  const today = now.toISOString().slice(0, 10);
  const entry = days.find((candidate) => candidate.day === today);
  return entry ? (entry.people ?? entry.humans) : 0;
}

// #region cold-start
/**
 * The first few minutes after a process starts are not held against it. A cold
 * process takes about a second to answer its first request, and on a quiet
 * minute that one request is the whole 95th percentile. Counting it would turn
 * the speed tile amber after every deploy, and an alarm that fires on every
 * deploy teaches people to ignore it. So the tile leaves these minutes out and
 * says so on its face. The traffic card still draws them.
 */
export const COLD_START_MINUTES = 3;

/**
 * Splits per-minute slots into warm ones and the ones inside the cold-start
 * window. The window starts at the minute the process started (rounded down).
 * With no start time, every slot counts as warm.
 */
export function afterColdStart<T extends { at: string }>(
  slots: T[],
  startedAt: Date | null
): { warm: T[]; left_out: T[] } {
  if (startedAt === null || Number.isNaN(startedAt.getTime())) return { warm: slots, left_out: [] };
  const from = Math.floor(startedAt.getTime() / 60_000) * 60_000;
  const until = from + COLD_START_MINUTES * 60_000;
  const cold = (slot: T) => {
    const at = new Date(slot.at).getTime();
    return at >= from && at < until;
  };
  return { warm: slots.filter((slot) => !cold(slot)), left_out: slots.filter(cold) };
}
// #endregion cold-start

// #region hour-timing
/**
 * The hour's median and 95th percentile, over every request in the minutes
 * passed in (usually the warm ones from afterColdStart). You cannot get an
 * hour's percentile from its minutes' percentiles, so this pools every request
 * duration first. It uses the nearest-rank method, the same arithmetic as the
 * API's Percentiles.Of, so the two agree on an hour that has one minute.
 */
export function hourTiming(
  slots: { at: string; requests?: number | null; durations_ms?: number[] }[]
): {
  requests: number;
  p50_ms: number | null;
  p95_ms: number | null;
  /** The minute the slowest request was in, or null on an hour with none. */
  slowest_at: string | null;
} {
  const all: number[] = [];
  let slowest: number | null = null;
  let slowestAt: string | null = null;
  for (const slot of slots) {
    const durations = slot.durations_ms ?? [];
    for (const ms of durations) {
      all.push(ms);
      if (slowest === null || ms > slowest) {
        slowest = ms;
        slowestAt = slot.at;
      }
    }
  }
  all.sort((a, b) => a - b);
  // Nearest rank: the value at position ceil(p% of n), counting from 1.
  const rank = (percentile: number) =>
    all.length === 0 ? null : all[Math.max(0, Math.ceil((percentile / 100) * all.length) - 1)];
  return { requests: all.length, p50_ms: rank(50), p95_ms: rank(95), slowest_at: slowestAt };
}
// #endregion hour-timing

/**
 * The caption that says what time span the small lines under the tiles cover.
 * A sparkline has no axis, so without this it says nothing about its own width.
 * The number over a line is always "now"; only the line follows the chosen window.
 */
export function sparkCaption(
  stretch: string,
  state: 'hour' | 'reading' | 'kept' | 'not-kept'
): string {
  switch (state) {
    case 'hour':
      return 'The line under a tile is the last hour.';
    case 'reading':
      return `Reading the ${stretch}; the lines are still the last hour.`;
    case 'not-kept':
      return `The ${stretch} is not kept here, so the lines are still the last hour.`;
    case 'kept':
      return `The line under a tile is the ${stretch}, from the minutes this site keeps. The number over it is still now.`;
  }
}

// #region spark
/**
 * The sparkline under a tile, as point lists for SVG polylines. There is one
 * list per unbroken run, so an unmeasured minute is a gap in the line, not a
 * drop to zero. The scale starts at zero: a line scaled from its own smallest
 * value makes a few megabytes of drift look like a cliff. A run of a single
 * reading is dropped, because a one-point polyline draws nothing.
 */
export function sparkRuns(values: (number | null)[], width: number, height: number): string[] {
  const measured = values.filter((value): value is number => value !== null);
  if (measured.length < 2) return [];
  const top = Math.max(...measured, 0);
  const step = values.length > 1 ? width / (values.length - 1) : 0;
  const runs: string[] = [];
  let run: string[] = [];
  // Ends the current run, keeping it only if it has at least two points.
  const close = () => {
    if (run.length > 1) runs.push(run.join(' '));
    run = [];
  };
  values.forEach((value, index) => {
    if (value === null) {
      close();
      return;
    }
    const x = Math.round(index * step * 10) / 10;
    // One unit of room top and bottom, so a flat line at zero or at the top is not clipped.
    const y =
      top <= 0 ? height - 1 : Math.round((height - 1 - (value / top) * (height - 2)) * 10) / 10;
    run.push(`${x},${y}`);
  });
  close();
  return runs;
}
// #endregion spark

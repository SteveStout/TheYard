/**
 * The strip of tiles across the top of the Admin tab (ADR: The Admin tab, as a
 * product, the addendum on the look). Four questions in the order somebody
 * asks them at three in the morning: is it up, is it fast, is it costing
 * anything, what broke. Each tile is one number, a line under it that says
 * what the number is a number of, and a tone.
 *
 * No React in here. The tiles are decided by plain functions over what the
 * tab has already read, so what makes a tile amber or red is a rule that can
 * be read and tested, and a reading that has not arrived is a tile that says
 * so rather than a zero.
 */

export type TileTone = 'good' | 'warn' | 'bad' | 'plain' | 'waiting';

/** The four questions, which are also the four sections a tile links down to. */
export type TileQuestion = 'up' | 'fast' | 'cost' | 'broke';

export type StatTile = {
  key: string;
  question: TileQuestion;
  label: string;
  value: string;
  detail: string;
  tone: TileTone;
  /** The last hour behind the number, oldest first, null where nothing was measured; absent where a line would say nothing. */
  spark?: (number | null)[];
  /**
   * A ring beside the number, only where the number is a share of a known
   * whole: checks passing, pages up, memory against its limit. A number with
   * no whole, milliseconds or request units, gets none: a ring drawn for looks
   * is a gauge that measures nothing (ADR: The glass look).
   */
  ring?: TileRing;
  /**
   * Whether the tile holds a ring's room beside its number, from the first
   * paint and whether or not the reading has drawn one yet (1.0.3.24): a tile
   * that never draws one keeps no empty 44 px box. Set by tilesFrom, the one
   * place that decides which tiles have a whole to be a share of.
   */
  ringed: boolean;
};

export type TileRing = {
  /** From 0 to 1, clamped. */
  share: number;
  /** What the ring's middle says, short enough to fit inside it. */
  label: string;
};

/** A share of a whole as a ring's reading; nothing to draw when there is no whole. */
export function ringOf(part: number, whole: number, label: string): TileRing | undefined {
  if (!(whole > 0) || Number.isNaN(part)) return undefined;
  return { share: Math.min(1, Math.max(0, part / whole)), label };
}

/** The ring as an SVG stroke: the circle's length and how much of it is left undrawn. */
export function ringStroke(share: number, radius: number): { length: number; gap: number } {
  const length = 2 * Math.PI * radius;
  const held = Math.min(1, Math.max(0, share));
  return { length: Math.round(length * 10) / 10, gap: Math.round(length * (1 - held) * 10) / 10 };
}

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
   * The last hour's traffic: every request and every error, added up, and the
   * hour as a visitor felt it, from hourTiming() over the warm minutes: how
   * many requests they held, their median and their ninety-fifth.
   */
  traffic: {
    requests: number;
    server_errors: number;
    client_errors: number;
    /** Requests in the warm minutes, the ones the two percentiles are over. */
    warm_requests: number;
    p50_ms: number | null;
    p95_ms: number | null;
    /** The minute the slowest request was answered in, already written as a clock time; absent on a quiet hour. */
    slowest_label?: string | null;
    /** The minute a cold start was left out of the reading above, as a clock time; absent when none was. */
    cold_start_label?: string | null;
  } | null;
  memory: { working_set_mb: number; limit_mb: number } | null;
  /** What the document store charged in the ring this process holds, and the allowance a second it is charged against. */
  charged: { request_units: number; free_per_second: number } | 'none' | null;
  /** Errors the server and the browser reported, in the ring this process holds. */
  errors: number | null;
  visitorsToday: number | null;
  /** The hour behind four of the tiles, a minute or a sample at a time. */
  sparks?: {
    speed?: (number | null)[];
    memory?: (number | null)[];
    charged?: (number | null)[];
    errors?: (number | null)[];
  };
};

/** Days, hours and minutes, the two largest that are not zero. */
export function uptimeWords(totalSeconds: number): string {
  const days = Math.floor(totalSeconds / 86_400);
  const hours = Math.floor((totalSeconds % 86_400) / 3_600);
  const minutes = Math.floor((totalSeconds % 3_600) / 60);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m`;
}

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
 * The rules, in one place. Amber is "worth a look" and red is "somebody should
 * be looking": a failing check or a page that is down is red; a slow
 * ninety-fifth, memory past four fifths of its limit or a refused request is
 * amber. The thresholds are this site's own, read off what it measures on a
 * quiet day, and they are here to be argued with.
 */
export const SLOW_P95_MS = 1_000;
export const VERY_SLOW_P95_MS = 3_000;
/**
 * A colour needs an hour with this many requests in it (Steve, 2026-09-23:
 * "is it fast keeps showing it's slow"). Over fewer, a ninety-fifth is one
 * request's time, and the tile reads quiet and shows the count instead.
 * Measured over the plan's first three days, about 30,000 requests: the old
 * tile, which headlined the worst minute's ninety-fifth, read amber 45 per
 * cent of the time on the SQL site while 1.5 per cent of its minutes were
 * slow; the hour's own ninety-fifth over its warm requests, with this floor,
 * reads amber under 1 per cent of the time on either site.
 */
export const QUIET_BELOW_REQUESTS = 20;

/**
 * The tiles whose number is a share of a known whole, the only ones that hold a
 * ring's room (1.0.3.24): a tile that never draws one kept an empty 44 px box
 * and broke "under 1 ms" over two lines on a phone.
 */
const RINGED = new Set(['health', 'pages', 'memory']);
export const MEMORY_WARN_SHARE = 0.8;
export const MEMORY_BAD_SHARE = 0.95;

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

  // The hour as a visitor felt it: the median is the number, the ninety-fifth
  // is the line under it, and both are over the warm minutes' requests. Under
  // QUIET_BELOW_REQUESTS the hour is too quiet to colour and says so.
  const busy = traffic !== null && traffic.warm_requests >= QUIET_BELOW_REQUESTS;
  const percentiles = (t: NonNullable<TileReadings['traffic']>) =>
    t.p50_ms === null || t.p95_ms === null ? '' : `typical ${t.p50_ms} ms, 95th ${t.p95_ms} ms`;
  tiles.push(
    traffic === null
      ? waiting('speed', 'fast', 'Typical answer')
      : {
          key: 'speed',
          question: 'fast',
          label: 'Typical answer',
          // No median to read is a quiet hour, unless the only requests there
          // were are the cold start's, and then it is a process warming.
          // The ring keeps whole milliseconds, so a median of 0 is a median
          // under a millisecond, which is what the tile says (1.0.3.9).
          value:
            busy && traffic.p50_ms !== null
              ? traffic.p50_ms === 0
                ? 'under 1 ms'
                : `${traffic.p50_ms} ms`
              : traffic.warm_requests === 0 && traffic.requests > 0 && traffic.cold_start_label
                ? 'warming'
                : 'quiet',
          detail:
            (busy && traffic.p95_ms !== null
              ? `95th ${traffic.p95_ms} ms over ${traffic.warm_requests} requests in the last hour`
              : traffic.warm_requests > 0
                ? `${traffic.warm_requests} requests in the last hour, too few to judge; ${percentiles(traffic)}`
                : `${traffic.requests} requests in the last hour`) +
            (traffic.slowest_label ? `, slowest at ${traffic.slowest_label}` : '') +
            (traffic.cold_start_label
              ? `; the start at ${traffic.cold_start_label} is left out`
              : ''),
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
      detail: `${Math.round(memory.working_set_mb)} of ${Math.round(memory.limit_mb)} MB`,
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
            // A total, so it is not set against the free tier's rate (the self-review of
            // 25 September); the rate is on the machines card, busiest minute against it.
            detail: `charged in the ring; the free tier allows ${charged.free_per_second} a second`,
            tone: 'plain',
            spark: sparks?.charged,
          }
  );

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
          ? 'nothing answered 5xx and nothing was reported'
          : `${failing} answered 5xx in the last hour, ${reported} reported`,
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
          detail: 'people, by a hash that changes daily; bots are left out',
          tone: 'plain',
        }
  );

  return tiles.map((tile) => ({ ...tile, ringed: RINGED.has(tile.key) }));
}
// #endregion tile-rules

/**
 * Today's people, from the days an activity report carries; a report with no
 * row for today is a zero, which is what it means. People, and not humans,
 * from 1.0.3.11: humans counted App Service's own requests from the loopback
 * address, most of every day since 20 September.
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
 * The minutes a process has just started in are not held against it. The
 * first tile to go amber went amber over something real, and then the same
 * tile went amber after every roll, because the first request a cold process
 * serves takes a second and a ninety-fifth over a quiet minute is that
 * minute's slowest request. An alarm that fires on every deploy teaches
 * people to look past it. So the speed tile reads the hour without the first
 * three minutes after the start, says on its face that it left them out, and
 * the traffic card goes on drawing them.
 */
export const COLD_START_MINUTES = 3;

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
 * The hour's own median and ninety-fifth, over every request in the minutes
 * handed in (the warm ones, after afterColdStart), from the durations each
 * minute carries. A percentile of an hour cannot be had from its minutes'
 * percentiles, and the worst minute's ninety-fifth, which the tile headlined
 * until 1.0.3.4, is one request's time on a quiet site and stayed on the tile
 * for the sixty minutes that minute was in the hour. Nearest rank, the same
 * arithmetic the API uses for a minute (Percentiles.Of), so the two agree on
 * an hour of one minute.
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
  const rank = (percentile: number) =>
    all.length === 0 ? null : all[Math.max(0, Math.ceil((percentile / 100) * all.length) - 1)];
  return { requests: all.length, p50_ms: rank(50), p95_ms: rank(95), slowest_at: slowestAt };
}
// #endregion hour-timing

/**
 * What the lines under the tiles are lines of, in words, because a line with
 * no axis says nothing about its own width. The number over a line is always
 * now; only the line follows the window.
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
 * The line under a tile, as the point lists of an SVG polyline: one list per
 * unbroken run, so a minute nobody measured is a gap in the line and not a
 * dive to the floor. The scale starts at zero, because a line scaled to its
 * own smallest value makes four megabytes of drift look like a cliff. A run
 * of one reading is left out: a polyline of one point draws nothing.
 */
export function sparkRuns(values: (number | null)[], width: number, height: number): string[] {
  const measured = values.filter((value): value is number => value !== null);
  if (measured.length < 2) return [];
  const top = Math.max(...measured, 0);
  const step = values.length > 1 ? width / (values.length - 1) : 0;
  const runs: string[] = [];
  let run: string[] = [];
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

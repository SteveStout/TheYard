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
};

export type TileReadings = {
  health: {
    status: string;
    uptime_seconds: number;
    version: string;
    commit: string;
    checks: { status: string }[];
  } | null;
  pages: { checked: number; up: number } | null;
  /** The last hour's traffic, added up: requests, the slowest ninety-fifth, and errors. */
  traffic: {
    requests: number;
    slowest_p95_ms: number | null;
    server_errors: number;
    client_errors: number;
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

export const QUESTIONS: Record<TileQuestion, string> = {
  up: 'Is it up?',
  fast: 'Is it fast?',
  cost: 'Is it costing anything?',
  broke: 'What broke?',
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

const waiting = (key: string, question: TileQuestion, label: string): StatTile => ({
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
export const MEMORY_WARN_SHARE = 0.8;
export const MEMORY_BAD_SHARE = 0.95;

export function tilesFrom(readings: TileReadings): StatTile[] {
  const { health, pages, traffic, memory, charged, errors, visitorsToday, sparks } = readings;
  const tiles: StatTile[] = [];

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
        }
  );

  tiles.push(
    traffic === null
      ? waiting('speed', 'fast', 'Slowest 95th')
      : {
          key: 'speed',
          question: 'fast',
          label: 'Slowest 95th',
          value: traffic.slowest_p95_ms === null ? 'quiet' : `${traffic.slowest_p95_ms} ms`,
          detail: `${traffic.requests} requests in the last hour`,
          tone:
            traffic.slowest_p95_ms === null
              ? 'plain'
              : traffic.slowest_p95_ms >= VERY_SLOW_P95_MS
                ? 'bad'
                : traffic.slowest_p95_ms >= SLOW_P95_MS
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
            detail: `in the ring, against ${charged.free_per_second} a second free`,
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

  return tiles;
}
// #endregion tile-rules

/** Today's people, from the days an activity report carries; a report with no row for today is a zero, which is what it means. */
export function visitorsOn(days: { day: string; humans: number }[], now: Date): number {
  const today = now.toISOString().slice(0, 10);
  return days.find((entry) => entry.day === today)?.humans ?? 0;
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

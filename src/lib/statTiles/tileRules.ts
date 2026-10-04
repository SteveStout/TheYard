/**
 * The rules that turn the tab's readings into tiles, and what makes each tile
 * amber or red. Every threshold and every tile's wording is decided here, in
 * one function a person can read top to bottom, so it is its own file, apart
 * from the shapes, the shared words and the drawing helpers it builds on.
 */
import { formatNumber } from '../format';
import { ringOf } from './tileRing';
import type { StatTile, TileQuestion, TileReadings } from './tileTypes';
import { millisecondsWords, RING_HOLDS, ringStretch, uptimeWords } from './tileWords';

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
  const stretch = ringStretch(t.ring_minutes);
  const short = t.ring_minutes === null || t.ring_minutes === undefined ? '' : RING_HOLDS;
  const tail =
    (t.slowest_label ? `, slowest at ${t.slowest_label}` : '') +
    (t.cold_start_label ? `; the start at ${t.cold_start_label} is left out` : '');
  if (judged && t.p95_ms !== null) {
    return {
      detail: `95th ${millisecondsWords(t.p95_ms)} over ${formatNumber(t.warm_requests)} requests`,
      more: ` in the ${stretch}${short}${tail}`,
    };
  }
  if (t.warm_requests > 0) {
    return {
      detail: `${t.warm_requests} requests in the ${stretch}`,
      more: `${short}, too few to judge${both === '' ? '' : `; ${both}`}${tail}`,
    };
  }
  const rest = short + tail;
  return {
    detail: `${t.requests} requests in the ${stretch}`,
    ...(rest === '' ? {} : { more: rest }),
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
/** A 95th percentile at or above this many milliseconds turns the speed tile red. */
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
/** The share of its limit at which memory turns the tile amber. */
export const MEMORY_WARN_SHARE = 0.8;
/** The share of its limit at which memory turns the tile red. */
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
      : `typical ${millisecondsWords(t.p50_ms)}, 95th ${millisecondsWords(t.p95_ms)}`;
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
            // A total says the stretch it covers, which the server measures.
            ...(charged.span_minutes === null || charged.span_minutes === undefined
              ? {
                  detail: `none held yet; ${formatNumber(charged.free_per_second)} a second is free`,
                }
              : {
                  detail: `RU over the last ${formatNumber(charged.span_minutes)} min`,
                  more: `; ${formatNumber(charged.free_per_second)} a second is free`,
                }),
            tone: 'plain',
            spark: sparks?.charged,
          }
  );

  // Errors: two counts over two different windows, so each is named with its
  // own. The big number is the error list the tile links to, the server's and
  // the browser's most recent errors; the 5xx answers are the traffic's
  // stretch. Before the list is read, the big number is the 5xx count, and the
  // line says so. Any error at all is red.
  if (errors === null && traffic === null) {
    tiles.push(waiting('errors', 'broke', 'Errors'));
  } else {
    const failing = traffic?.server_errors ?? 0;
    const reported = errors ?? 0;
    const stretch = ringStretch(traffic?.ring_minutes);
    const clean = failing === 0 && reported === 0;
    tiles.push({
      key: 'errors',
      question: 'broke',
      label: 'Errors',
      value: `${errors === null ? failing : reported}`,
      detail: clean
        ? 'no 5xx answered and none listed'
        : errors === null
          ? `${failing} answered 5xx`
          : `${reported} listed, ${failing} answered 5xx`,
      more:
        (errors === null ? '' : '; the list is the most recent from the server and the browser') +
        (traffic === null ? '' : `${errors === null ? ';' : ','} the 5xx are from the ${stretch}`),
      tone: clean ? 'good' : 'bad',
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
          detail: 'since midnight UTC, bots left out',
          more: '; counted by a daily hash, the day the server keeps',
          tone: 'plain',
        }
  );

  return tiles.map((tile) => ({ ...tile, ringed: RINGED.has(tile.key) }));
}
// #endregion tile-rules

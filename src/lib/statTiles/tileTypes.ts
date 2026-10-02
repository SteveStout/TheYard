/**
 * The shapes behind the stat tiles: what one tile holds, the ring a tile may
 * draw, and the readings every tile is built from. Only types live here, so the
 * rules, the words and the drawing code can all name the same shapes without
 * importing each other.
 */

/** How a tile is coloured: good, worth a look (warn), bad, plain, or still waiting for its reading. */
export type TileTone = 'good' | 'warn' | 'bad' | 'plain' | 'waiting';

/** The four questions. Each is also the section of the tab a tile links down to. */
export type TileQuestion = 'up' | 'fast' | 'cost' | 'broke';

/** One tile: a number, a short line saying what it counts, and a tone. */
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
   * for looks is a gauge that measures nothing (ADR: The glass look).
   */
  ring?: TileRing;
  /**
   * Whether the tile reserves room for a ring, from the first paint, even before
   * its reading arrives. Tiles that never draw a ring reserve no room. tilesFrom
   * sets this, so the decision lives in one place.
   */
  ringed: boolean;
};

/** The small circular gauge beside a tile's number: how full it is, and the text inside it. */
export type TileRing = {
  /** From 0 to 1, clamped. */
  share: number;
  /** The text in the middle of the ring. Keep it short enough to fit. */
  label: string;
};

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
    /**
     * How many minutes the figures reach back when the request ring stops short
     * of the hour (trafficTotals in machineChart.ts). Null or absent is the hour.
     */
    ring_minutes?: number | null;
  } | null;
  memory: { working_set_mb: number; limit_mb: number } | null;
  /**
   * What the document store (Cosmos DB) charged in request units over the ring,
   * how many minutes back the ring reaches (the server's span_minutes), and how
   * many units a second are free. 'none' when no document store is in use.
   */
  charged:
    | { request_units: number; free_per_second: number; span_minutes?: number | null }
    | 'none'
    | null;
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

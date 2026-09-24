/**
 * The workbench's rail and what stands beside the open card (ADR: The Admin tab,
 * as a product, the addendum on the workbench): the five questions and every
 * card under the one it answers, where the previous and next keys lead, what
 * the rail's search finds, which card a tile opens and the hour at a glance.
 * Only the Admin tab's chunk loads it; the addresses are in workbench.ts,
 * which the page's first chunk carries.
 *
 * No React in here: each is a plain function, so each is a rule a test can read.
 */
import { CARD_SLUGS, type CardSlug } from './workbench';

/** The five questions, which are the rail's five groups. */
export type BenchQuestion = 'up' | 'fast' | 'cost' | 'broke' | 'desk';

export type BenchCard = {
  slug: CardSlug;
  /** The card's own title, as its heading says it. */
  name: string;
  question: BenchQuestion;
  /** The card's test id, which every spec already finds it by. */
  testId: string;
};

export const BENCH_QUESTIONS: { key: BenchQuestion; title: string }[] = [
  { key: 'up', title: 'Is it up?' },
  { key: 'fast', title: 'Is it fast?' },
  { key: 'cost', title: 'Is it costing anything?' },
  { key: 'broke', title: 'What broke?' },
  { key: 'desk', title: 'Who came, and the desk' },
];

const card = (
  slug: CardSlug,
  name: string,
  question: BenchQuestion,
  testId?: string
): BenchCard => ({
  slug,
  name,
  question,
  testId: testId ?? `${slug}-card`,
});

/** Every card, in the rail's order; the order is the questions' order, and within one the page's. */
export const BENCH_CARDS: BenchCard[] = [
  card('health', 'Application health', 'up'),
  card('azure', "Azure's view of the container", 'up'),
  card('pages', 'Every page, checked', 'up'),
  card('tests', 'Every test, for this build', 'up'),
  card('traffic', 'Traffic', 'fast'),
  card('telemetry', 'Traffic, last hour', 'fast'),
  card('timing', 'Timing', 'fast'),
  card('backends', 'Backends, side by side', 'fast'),
  card('proof', 'Same performance, proven', 'fast'),
  card('machines', 'What the machines are doing', 'cost'),
  card('experiment', 'The partition key, live', 'cost'),
  card('sql', 'The SQL this application ran', 'cost'),
  card('store', 'What the document store ran', 'cost'),
  card('errors', 'Recent errors', 'broke'),
  card('log', 'The log, as the console got it', 'broke'),
  card('kept', 'Kept log', 'broke', 'kept-logs-card'),
  card('activity', 'Site activity', 'desk'),
  card('operator', 'Operator', 'desk'),
  card('reset', 'Reset a password', 'desk', 'reset-link-card'),
];

export function benchCard(slug: CardSlug): BenchCard {
  return BENCH_CARDS.find((entry) => entry.slug === slug) ?? BENCH_CARDS[0];
}

/** The card before and after this one in the rail's order, wrapping at the ends, as j and k walk it. */
export function neighbours(slug: CardSlug): { previous: CardSlug; next: CardSlug } {
  const at = CARD_SLUGS.indexOf(slug);
  const count = CARD_SLUGS.length;
  return {
    previous: CARD_SLUGS[(at - 1 + count) % count],
    next: CARD_SLUGS[(at + 1) % count],
  };
}

/** The rail's search: every word typed has to be in the card's name, its slug or its question. */
export function findCards(query: string): BenchCard[] {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) return BENCH_CARDS;
  return BENCH_CARDS.filter((entry) => {
    const title = BENCH_QUESTIONS.find((question) => question.key === entry.question)?.title ?? '';
    const haystack = `${entry.name} ${entry.slug} ${title}`.toLowerCase();
    return words.every((word) => haystack.includes(word));
  });
}

/** The tiles across the top, each a link to the card that answers it (the strip keeps its own reads). */
export const TILE_CARD: Record<string, CardSlug> = {
  version: 'tests',
  health: 'health',
  pages: 'pages',
  speed: 'timing',
  visitors: 'activity',
  memory: 'machines',
  charged: 'machines',
  errors: 'errors',
};

/** Where a tile goes; a tile this list does not know goes to health rather than nowhere. */
export function cardForTile(key: string): CardSlug {
  return TILE_CARD[key] ?? 'health';
}

// #region hour-glance
/** The scale the hour's ring reads its answer times against: ten milliseconds all the way round. */
export const HOUR_RING_MS = 10;

export type HourReading = {
  requests: number;
  server_errors: number;
  client_errors: number;
  p50_ms: number | null;
  p95_ms: number | null;
};

export type HourGlance = {
  /** The ninety-fifth (outside) and the median (inside) against the ring's scale; null before the hour is read. */
  ring: { p95: number; p50: number; max: number } | null;
  /** The few characters in the middle of the ring. */
  inside: string;
  /** What the ring says to a screen reader. */
  label: string;
  rows: [string, string][];
};

/** A time as a reading: under a millisecond is said as that, and no time is said as none. */
export function msWords(ms: number | null): string {
  if (ms === null) return 'none';
  if (ms < 1) return 'under 1 ms';
  return `${Math.round(ms)} ms`;
}

/**
 * The hour at a glance, beside the open card (the operator's look): the typical
 * answer and the ninety-fifth as two rings against ten milliseconds, and the
 * hour's counts as a readout. Made of what the strip already read, so it and
 * the tiles cannot disagree.
 */
export function hourGlance(hour: HourReading | null): HourGlance {
  if (hour === null) {
    return {
      ring: null,
      inside: '...',
      label: 'The hour, not read yet',
      rows: [
        ['Typical answer', 'not read yet'],
        ['Ninety-fifth', 'not read yet'],
        ['Requests', 'not read yet'],
      ],
    };
  }
  const p50 = hour.p50_ms ?? 0;
  const p95 = hour.p95_ms ?? 0;
  const count = (n: number) => n.toLocaleString('en-US');
  return {
    ring: { p95, p50, max: HOUR_RING_MS },
    inside:
      hour.p50_ms === null ? 'none' : hour.p50_ms < 1 ? '< 1 ms' : `${Math.round(hour.p50_ms)} ms`,
    label: `The hour's typical answer ${msWords(hour.p50_ms)} and its ninety-fifth ${msWords(hour.p95_ms)}, against ${HOUR_RING_MS} ms`,
    rows: [
      ['Typical answer', msWords(hour.p50_ms)],
      ['Ninety-fifth', msWords(hour.p95_ms)],
      ['Requests', count(hour.requests)],
      ['Server errors', count(hour.server_errors)],
      ['Turned away', count(hour.client_errors)],
    ],
  };
}
// #endregion hour-glance

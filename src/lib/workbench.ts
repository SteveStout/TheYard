/**
 * The Admin tab as a workbench (ADR: The Admin tab, as a product, the addendum
 * on the workbench): one card at a time, large, with the address naming it.
 * `/?view=admin&card=timing` opens the timing card, `&pin=errors` keeps the
 * errors card beside it, and the rail down the left is the five questions in
 * the order somebody asks them, each card under the question it answers.
 *
 * No React in here. Which card an address opens, what a wrong name falls back
 * to, where the previous and next keys lead and what the rail's search finds
 * are plain functions, so each is a rule a test can read.
 */

/** The cards by their slug, which is the card's test id without "-card", in the rail's order. */
export const CARD_SLUGS = [
  'health',
  'azure',
  'pages',
  'tests',
  'traffic',
  'telemetry',
  'timing',
  'backends',
  'proof',
  'machines',
  'experiment',
  'sql',
  'store',
  'errors',
  'log',
  'kept',
  'activity',
  'operator',
  'reset',
] as const;

export type CardSlug = (typeof CARD_SLUGS)[number];

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

/** The card an address names, and whether the name was one: a wrong or missing name opens health. */
export function cardFromAddress(raw: string | null): {
  slug: CardSlug;
  asked: string | null;
  known: boolean;
} {
  const asked = raw === null || raw.trim() === '' ? null : raw.trim();
  const known = asked !== null && (CARD_SLUGS as readonly string[]).includes(asked);
  return { slug: known ? (asked as CardSlug) : 'health', asked, known: asked === null || known };
}

/**
 * A pinned card from the address: only a real name pins. A card may be pinned
 * while it is the open one; it stays when the rail opens the next, which is
 * the comparison the pin is for.
 */
export function pinFromAddress(raw: string | null): CardSlug | null {
  const asked = raw === null ? '' : raw.trim();
  return (CARD_SLUGS as readonly string[]).includes(asked) ? (asked as CardSlug) : null;
}

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

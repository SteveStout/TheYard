/**
 * The Admin tab as a workbench (ADR: The Admin tab, as a product, the addendum
 * on the workbench): one card at a time, large, with the address naming it.
 * `/?view=admin&card=timing` opens the timing card, `&pin=errors` keeps the
 * errors card beside it, and the rail down the left is the five questions in
 * the order somebody asks them, each card under the question it answers.
 *
 * No React in here. Which card an address opens and what a wrong name falls
 * back to are plain functions, so each is a rule a test can read. This file is
 * all the page's first chunk carries of the workbench, because the app reads
 * the address on every view; the rail, its search, the tiles' cards and the
 * hour at a glance are in bench.ts, which only the Admin tab's chunk loads.
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

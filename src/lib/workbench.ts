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
  'overview',
  'health',
  'pages',
  'tests',
  'traffic',
  'telemetry',
  'timing',
  'backends',
  'proof',
  'spend',
  'experiment',
  'sql',
  'store',
  'plan',
  'sqldb',
  'cosmosdb',
  'registry',
  'insights',
  'mail',
  'edge',
  'azure',
  'machines',
  'follows',
  'nightly',
  'errors',
  'log',
  'kept',
  'activity',
  'links',
  'operator',
  'reset',
] as const;

export type CardSlug = (typeof CARD_SLUGS)[number];

/**
 * The resource cards under Cloud infrastructure after the plan, each answering on
 * /api/admin/cloud/{slug}: the one list the rail's glances and anything else that walks the
 * cloud cards read, so a new card is named once.
 */
export const CLOUD_RESOURCES = [
  'sqldb',
  'cosmosdb',
  'registry',
  'insights',
  'mail',
  'edge',
  'nightly',
] as const satisfies readonly CardSlug[];

/** The card ?view=admin opens, and the one a wrong name falls back to: the Overview. */
export const DEFAULT_CARD: CardSlug = 'overview';

/** The card an address names, and whether the name was one: a wrong or missing name opens the Overview. */
export function cardFromAddress(raw: string | null): {
  slug: CardSlug;
  asked: string | null;
  known: boolean;
} {
  const asked = raw === null || raw.trim() === '' ? null : raw.trim();
  const known = asked !== null && (CARD_SLUGS as readonly string[]).includes(asked);
  return {
    slug: known ? (asked as CardSlug) : DEFAULT_CARD,
    asked,
    known: asked === null || known,
  };
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

/**
 * Does:      Holds the review route's second first screen, at /?view=review&for=ai (ADR: The review route gives a
 *            reason before it asks, the addendum on the mirror): which screen an address names, its words, word for
 *            word from the design, the gate's fixed parts, and the pickers for its live lines and its strip of names.
 * Does not:  Read anything or draw anything; no React in here. Every number is read from an answer or the build.
 * Used by:   App.tsx, ReviewPage.tsx, ReviewMirror.tsx, GatePanel.tsx, NameStrip.tsx, reviewMirror.test.ts.
 */
import { figures, proofFigures, type TestSummary } from './landingProof';
import type { Recommendation } from './recommendations';

/** Which first screen the route draws: the verdict, or the mirror for a link that names for=ai. */
export type ReviewScreen = 'verdict' | 'mirror';

/** The screen an address's own parts name: only for=ai draws the mirror, and any other value the verdict. */
export function screenOf(pageQuery: string): ReviewScreen {
  return new URLSearchParams(pageQuery).get('for') === 'ai' ? 'mirror' : 'verdict';
}

/** The mirror's words, word for word from the design, in the order the screen draws them. */
export const MIRROR = {
  eyebrow: 'For the manager hiring a remote Lead or Staff engineer',
  title: 'Shipping with AI without losing quality.',
  problemLabel: 'The problem',
  problem:
    'AI writes code faster than a team can review it. Someone has to decide what ships and show that quality is holding.',
  answerLabel: 'How I run it',
  answer:
    'On this site Claude writes most of the code and I decide what ships. My hours moved from typing code to QA, architecture and design. Every push goes through one gate, CodeRabbit reviews every pull request, and each quality number is only allowed to improve.',
  primary: 'See what this version had to pass',
  byName: 'Steven Stout',
  byline: ', Lead / Staff .NET engineer. 12+ years shipping C#, .NET, SQL Server, React and Azure.',
  panelLabel: 'The proof, running now',
  panelTitle: 'The gate that shipped this version',
  panelLine: 'Every push has to pass all of this before anything ships.',
  slipLead: 'It slipped once.',
  slip: 'Two weeks in October, then the ratchet that turned it.',
  slipLink: 'Read the slip and the turn',
  stripLink: 'Read what they wrote',
} as const;

/** Where the primary button goes: the Admin tab's tests card, which an address already brings into view. */
export const GATE_ADDRESS = '/?view=admin&card=tests';

/** The two tiles from the record, as the mirror words them. */
export const MIRROR_TILES = [
  {
    key: 'releases',
    title: '200+ production releases, zero downtime',
    source: 'Conway Investment Research, 2019 to 2024. Sole engineer for the last three years.',
  },
  {
    key: 'cycle',
    title: 'Release cycle cut 50%',
    source:
      'Storee Construction, 2024 to 2026. First lead-level engineering hire; hired and mentored a four-person team.',
  },
] as const;

/** The gate's fixed parts, as the design words them; the last carries CodeRabbit's word for this version. */
export const GATE_PARTS = [
  'xUnit',
  'Vitest',
  'Playwright with axe',
  'Architecture rules',
  'Both stores: Azure SQL and Azure Cosmos DB',
  'CodeRabbit on the pull request',
] as const;

/** CodeRabbit's word for a version, as a page carries it in data-review and data-state. */
export type ReviewWord = 'full' | 'rate limited' | 'not reviewed' | 'pending';

/**
 * CodeRabbit's word for a version from its row (ADR-129): full when it read the version whole, rate limited when its row
 * says its rate limit held it off, not reviewed for any other reason, and pending while the gate has not written the row.
 */
export function reviewWord(review: { reviewed: string } | null): ReviewWord {
  if (review === null) return 'pending';
  if (review.reviewed === 'full') return 'full';
  return review.reviewed.startsWith('rate limited') ? 'rate limited' : 'not reviewed';
}

/** CodeRabbit's word for this version in a sentence: reviewed, rate limited, not reviewed or pending. */
export function reviewedWord(review: { reviewed: string } | null): string {
  const word = reviewWord(review);
  return word === 'full' ? 'reviewed' : word;
}

/** The panel's live totals: the tests passed (the landing page's figure), the failures among them, and the version. */
export function gateTotals(
  summary: TestSummary | null,
  buildVersion: string
): { passed: string; failed: string; version: string } {
  const tests = proofFigures(summary, 0)?.find((figure) => figure.key === 'tests');
  const failed = tests?.ring ? tests.ring.max - tests.ring.value : null;
  return {
    passed: tests?.figure ?? '',
    failed: failed === null ? '' : figures(failed),
    version: summary?.version ?? buildVersion,
  };
}

/** The names on the strip, in this order, each printed with the API's own title. */
export const STRIP_NAMES = [
  'Drew Dell',
  'Sean Whitesell',
  'Anthony Fortner',
  'Khushbu Shah',
  'Cliff True',
  'Henry Crans',
  'Meagan Colley',
] as const;

/** The strip: the named entries the answer has, in the strip's order, a name it lacks skipped, and how many are left. */
export function stripOf(entries: readonly Recommendation[] | null): {
  shown: Recommendation[];
  more: number;
  count: number;
} {
  const list = entries ?? [];
  const shown = STRIP_NAMES.flatMap((name) => list.filter((entry) => entry.name === name));
  return { shown, more: Math.max(0, list.length - shown.length), count: list.length };
}

/** The strip's label, with the answer's own count. */
export function stripLabel(count: number): string {
  return `${count} people who worked with him put their name on it`;
}

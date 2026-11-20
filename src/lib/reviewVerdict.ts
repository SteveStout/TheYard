/**
 * Does:      Holds the review route's first screen (ADR: The review route gives a reason before it asks): its words,
 *            word for word from the design, the two tiles from the record, and the pickers that turn the live
 *            answers into the third tile, the one quote and the counts. Every number is read, never typed.
 * Does not:  Read anything or draw anything; no React in here.
 * Used by:   ReviewVerdict.tsx, ReviewLeadQuote.tsx, ReviewMirror.tsx, GatePanel.tsx, NameStrip.tsx, ReviewPage.tsx,
 *            StoreBar.tsx, Landing.tsx, LandingVoices.tsx, QualityFoot.tsx, landingMoves.ts, AskClose.tsx,
 *            reviewVerdict.test.ts.
 */
import { proofFigures, type TestSummary } from './landingProof';
import type { Recommendation } from './recommendations';
import type { ReviewAction } from './reviewProgress';

/** The first screen's words, word for word from the spec, in the order the screen draws them. */
export const VERDICT = {
  eyebrow: ['Steven Stout', 'Lead / Staff .NET engineer', 'Remote'],
  /** The H1 in its two sentences; at a desk each sentence is a line of its own. */
  title: ['Production .NET that stays up.', 'Built with AI, measured on every version.'],
  lede: '12+ years shipping C#, .NET, SQL Server, React and Azure. This site is the running system, so you can check that claim in two minutes.',
  /** The first move: down the page to the work (sales1120); the proof is TheYard card's first door. */
  primary: 'See the work',
  book: 'Book 20 minutes',
  resume: 'Resume (PDF)',
  quoteLabel: 'Who worked with him says',
  roles: 'Open to remote Lead, Staff and Principal roles.',
} as const;

/** The full tour's words, word for word from the whole-page picture: its heading, its line, its place and cell 8. */
export const TOUR = {
  title: 'The full tour, if you want it',
  line: 'Seven stops, ten minutes, all optional. Every stop is the running system, and your place is kept on this browser.',
  startOver: 'Start over',
  restTitle: 'The rest is in the sidebar',
  /** The phone's control: the eight cells are closed behind it until it is pressed. */
  show: 'Show the seven stops',
  hide: 'Hide the seven stops',
} as const;

/** "0 of 7 seen.", from the tour's stops reached on this browser. */
export function tourSeen(reached: number, of: number): string {
  return `${reached} of ${of} seen.`;
}

/** Cell 8's line: what the sidebar still holds, both counts read from the sidebar's own lists. */
export function restLine(records: number, diagrams: number): string {
  return `${records} decision records, ${diagrams} diagrams, the incidents and the changelog.`;
}

/**
 * The close's words: its readout and its two outlined buttons; the filled one is the first screen's "Book 20 minutes".
 * The question, its line and the three moves are the one closing row's (AskClose.tsx), and /about draws the same words
 * from AskCloseHtml.cs, which AskCloseHtmlTests holds equal to these.
 */
export const CLOSE = {
  /** The heading a screen reader lands on in the home page's close, over the question drawn as words. */
  label: 'Next step',
  linkedin: 'Message me on LinkedIn',
  /** "06 Talk": the review route's closing question, the three the booking email asks (sales1120). */
  number: '06',
  question: 'What has to work, where is it today, and what does success look like?',
  line: 'Book 20 minutes opens an email to me with those three questions in it, and I answer with a time.',
} as const;

/** "Message me on LinkedIn": Steven's own page, the address the finish panel used. */
export const LINKEDIN_ADDRESS = 'https://www.linkedin.com/in/stevenwstout/';

/**
 * The document "See the proof in 2 minutes" and the path's first stop open, and the section the mirror's slip line
 * lands on: Quality, at "The slip, and the turn".
 */
export const PROOF = { doc: 'quality', anchor: 'the-slip-and-the-turn' } as const;

/** The proof's address: the top of Quality, where a phone's first screen shows the slip, the ratchet and the chart. */
export const PROOF_ADDRESS = `/?doc=${PROOF.doc}`;

/** The mirror's slip line's address: Quality at the slip and the turn. */
export const SLIP_ADDRESS = `/?doc=${PROOF.doc}#${PROOF.anchor}`;

/** "Book 20 minutes": the counted redirect to an email asking for a time; no page carries the address. */
export const BOOK_ADDRESS = '/api/go/book';

/** The resume, the address the rail held. */
export const RESUME_ADDRESS = '/resume?pdf=1';

/**
 * The close's three moves in their order, each with the look of its button and the action its press reports on the
 * review route; the Quality sheet's foot draws the same three and reports the same names (saleshome1120).
 */
export const CLOSE_MOVES = [
  { label: VERDICT.book, href: BOOK_ADDRESS, look: 'op-pill-primary', action: 'book' },
  { label: VERDICT.resume, href: RESUME_ADDRESS, look: 'op-pill-resume', action: 'resume' },
  { label: CLOSE.linkedin, href: LINKEDIN_ADDRESS, look: 'op-pill-secondary', action: 'linkedin' },
] as const satisfies readonly { label: string; href: string; look: string; action: ReviewAction }[];

/** All the recommendations, the address the rail held. */
export const RECOMMENDATIONS_ADDRESS = '/?view=recommendations';

/** The tour's own anchor on the page. */
export const TOUR_ID = 'review-tour';

/** The three pills under "Open to remote Lead, Staff and Principal roles.", each at the address the rail held. */
export const ROLE_LINKS = [
  { label: 'LinkedIn', href: 'https://www.linkedin.com/in/stevenwstout/', action: 'linkedin' },
  { label: 'GitHub', href: 'https://github.com/SteveStout/TheYard', action: 'github' },
  { label: 'About Steven', href: '/about', action: 'about' },
] as const;

/**
 * One proof tile: the figure, what it counts, where it comes from, whether it is read live from this build, and the
 * case card its "See how" door goes to.
 */
export type VerdictTile = {
  key: string;
  figure: string;
  label: string;
  source: string;
  live: boolean;
  /** The anchor of the case card under "01 The work" that tells this number's story. */
  door: 'case-conway' | 'case-storee' | 'case-theyard';
};

/** The words of a tile's door. */
export const TILE_DOOR = 'See how';

/** The two tiles from the record: facts from the resume, in the words the design gives them. */
export const RECORD_TILES: readonly VerdictTile[] = [
  {
    key: 'releases',
    figure: '200+',
    label: 'production releases, zero downtime',
    source:
      'Conway Investment Research, 2019 to 2024. Sole engineer for the last three years on a live financial advisory platform.',
    live: false,
    door: 'case-conway',
  },
  {
    key: 'cycle',
    figure: '50%',
    label: 'shorter release cycle',
    source:
      'Storee Construction, 2024 to 2026. Four ASP.NET applications onto one .NET 10 platform on Azure, with a four-person team I hired and mentored.',
    live: false,
    door: 'case-storee',
  },
];

/** The quality numbers this build held, as the gate badge reads them, or null when the build has no reading. */
export type HeldQuality = { held: number; of: number } | null;

/**
 * The third tile, live: the tests passed on this version, the same figure the landing page prints (proofFigures over
 * /api/tests/summary, each suite once), and the quality numbers held when the build has a reading. Before the summary
 * arrives the figure is empty and the tile keeps its place.
 */
export function testsTile(summary: TestSummary | null, quality: HeldQuality): VerdictTile {
  const tests = proofFigures(summary, 0)?.find((figure) => figure.key === 'tests');
  return {
    key: 'tests',
    figure: tests?.figure ?? '',
    label: 'tests passed on the version you are reading',
    source:
      quality === null
        ? 'TheYard, read from the build.'
        : `TheYard, read from the build. ${quality.held} of ${quality.of} quality numbers held.`,
    live: true,
    door: 'case-theyard',
  };
}

/** Whose words the first screen quotes. */
export const LEAD_NAME = 'Drew Dell';

/** The quote the first screen shows: Drew Dell's when the answer has him, else the answer's first; null for none. */
export function leadQuote(entries: readonly Recommendation[] | null): Recommendation | null {
  if (entries === null || entries.length === 0) return null;
  return entries.find((entry) => entry.name === LEAD_NAME) ?? entries[0];
}

/** The link under the quote, with the answer's own count. */
export function recommendationsLink(count: number): string {
  return `Read all ${count} named recommendations`;
}

/**
 * The one sentence the store bar shows on the review route in place of the toggle, the ring and the gate chips:
 * "passed every check" only when the build carries the gate's own reading of this version.
 */
export function liveSentence(gated: boolean): { lead: string; rest: string | null } {
  return {
    lead: 'Live system.',
    rest: gated ? 'This version passed every check before it shipped.' : null,
  };
}

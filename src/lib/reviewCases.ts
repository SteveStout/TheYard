/**
 * Does:      Holds the review route's three case cards under "01 The work" (ADR: The sales pages set the site's look):
 *            each card's anchor, eyebrow, heading, three bullets, its dark panel's stack or the word that it is read live,
 *            the doors on its last line and the slug of its post, word for word from the sales1120 picture. Every number
 *            on a card is on the locked resume and LinkedIn, or read live; TheYard's heading takes the same figure the
 *            first screen's tests tile prints, never a second count. Also the post link both ways: the card's "Read the
 *            post" door when its slug is set, and the post's "From the work" line back to the card.
 * Does not:  Read anything or draw anything; no React in here.
 * Used by:   ReviewWork.tsx, CaseCard.tsx, BlogPostCard.tsx, reviewCases.test.ts.
 */
import { postLink } from './blog';
import { cardAddress } from './recommendationsPage';
import type { ReviewAction } from './reviewProgress';
import { PROOF_ADDRESS } from './reviewVerdict';

/** The work section's own anchor, which "See the work" and the path's first step go to. */
export const WORK_ID = 'the-work';

/** The short path's anchor, which TheYard card's last door goes to. */
export const QUESTIONS_ID = 'questions';

/** The work section's head: its title and its line. The gold Resume (PDF) button sits above it, so no door is here. */
export const WORK = {
  title: 'The work',
  hint: 'Each number is on my resume and my LinkedIn. The last one is read from this site as you look at it.',
} as const;

/** One door on a card's last line: its words, its address, the action it reports and the arrow it ends with. */
export type CaseDoor = {
  label: string;
  href: string;
  action: ReviewAction;
  /** The proof opens Quality in place; a post opens the blog in place; the rest follow their address. */
  kind: 'voice' | 'proof' | 'questions' | 'post';
  arrow: '→' | '↓';
  /** The post a post door opens. */
  slug?: string;
};

/** What a card's dark panel holds: its title and the stack as four facts, or the week read live. */
export type CasePanel =
  | { title: string; live: false; facts: readonly (readonly [string, string])[] }
  | { title: string; live: true };

/** One case card, as the picture draws it. */
export type ReviewCase = {
  /** The card's anchor on the page, which its first-screen tile's door goes to. */
  id: 'case-conway' | 'case-storee' | 'case-theyard';
  eyebrow: string;
  /** The heading; TheYard's is null, made from the live tests figure by caseHeading. */
  heading: string | null;
  bullets: readonly string[];
  panel: CasePanel;
  doors: readonly CaseDoor[];
  /** The slug of the post that tells this card's story, or null while there is none (no post line is drawn). */
  post: string | null;
};

/** The words after TheYard's live figure. */
export const THEYARD_HEADING = 'tests passed on the version you are reading';

/** The two named voices the first two cards lead to, each at their own company (a link, never a quote). */
export const CASE_VOICES = [
  { name: 'Meagan Colley', company: 'Conway' },
  { name: 'Henry Crans', company: 'Storee' },
] as const;

const voiceDoor = (voice: (typeof CASE_VOICES)[number]): CaseDoor => ({
  label: `Worked with him at ${voice.company}: ${voice.name}, in full`,
  href: cardAddress(voice.name),
  action: 'recommendations',
  kind: 'voice',
  arrow: '→',
});

/** The three cards, in the order the first screen's tiles name them. */
export const REVIEW_CASES: readonly ReviewCase[] = [
  {
    id: 'case-conway',
    eyebrow: 'Conway Investment Research · Financial services',
    heading: '200+ production releases, zero downtime',
    bullets: [
      'Sole engineer for the last three of five years on a live, client-facing financial advisory platform serving 200+ advisors, clients and staff.',
      'Designed and operated the CI/CD pipelines behind every one of those releases.',
      'Cut $30,000+ from Azure and third-party spend.',
    ],
    panel: {
      title: 'The stack',
      live: false,
      facts: [
        ['Runtime', '.NET 6 on Azure'],
        ['Edge', 'Azure Front Door'],
        ['Identity', 'Auth0 SSO with MFA'],
        ['Delivery', 'CI/CD I designed'],
      ],
    },
    doors: [voiceDoor(CASE_VOICES[0])],
    post: null,
  },
  {
    id: 'case-storee',
    eyebrow: 'Storee Construction · Construction software',
    heading: '50% shorter release cycle',
    bullets: [
      'Consolidated four ASP.NET applications onto one .NET 10 platform on Azure with a phased Strangler Fig migration.',
      'Hired and mentored a four-person engineering team and set its architecture and code-quality bar.',
      'Defined the Azure infrastructure in Bicep, so every environment builds from source control.',
    ],
    panel: {
      title: 'The stack',
      live: false,
      facts: [
        ['Runtime', '.NET 10 on Azure'],
        ['Data', 'Entity Framework'],
        ['Front end', 'React and TypeScript'],
        ['Infrastructure', 'Azure Bicep'],
      ],
    },
    doors: [voiceDoor(CASE_VOICES[1])],
    post: null,
  },
  {
    id: 'case-theyard',
    eyebrow: 'TheYard · This site, built and run solo',
    heading: null,
    bullets: [
      'A .NET 10 API and a React front end over Azure SQL and Cosmos DB, side by side.',
      'Every version passes one gate: unit, browser and architecture tests. CodeRabbit is asked to read every ship.',
      'Fifteen code-quality numbers that may only improve, measured on every version.',
    ],
    panel: { title: 'TheYard, this week', live: true },
    doors: [
      {
        label: 'See the proof in 2 minutes',
        href: PROOF_ADDRESS,
        action: 'proof',
        kind: 'proof',
        arrow: '→',
      },
      {
        label: 'Or ask it three questions',
        href: `#${QUESTIONS_ID}`,
        action: 'work',
        kind: 'questions',
        arrow: '↓',
      },
    ],
    post: null,
  },
];

/**
 * A card's heading: its own, or for TheYard the first screen's live tests figure before the words. Before that figure
 * is read the heading says the words alone, capitalised, never a dash or an empty number.
 */
export function caseHeading(entry: ReviewCase, testsFigure: string): string {
  if (entry.heading !== null) return entry.heading;
  return testsFigure === ''
    ? THEYARD_HEADING.charAt(0).toUpperCase() + THEYARD_HEADING.slice(1)
    : `${testsFigure} ${THEYARD_HEADING}`;
}

/** A card's doors, with "Read the post" last when its post is set; no post, no post line. */
export function caseDoors(entry: ReviewCase): CaseDoor[] {
  if (entry.post === null) return [...entry.doors];
  return [
    ...entry.doors,
    {
      label: 'Read the post',
      href: postLink(entry.post),
      action: 'blog',
      kind: 'post',
      arrow: '→',
      slug: entry.post,
    },
  ];
}

/** The way back from a post to the card that names it: "From the work: <heading>" and the card's address, or null. */
export function fromTheWork(
  slug: string,
  testsFigure: string,
  cases: readonly ReviewCase[] = REVIEW_CASES
): { line: string; href: string } | null {
  const entry = cases.find((each) => each.post === slug);
  if (!entry) return null;
  return {
    line: `From the work: ${caseHeading(entry, testsFigure)}`,
    href: `/?view=review#${entry.id}`,
  };
}

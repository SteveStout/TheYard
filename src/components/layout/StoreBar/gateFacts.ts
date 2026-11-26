/**
 * Does:      Says what the running version had to pass, from what the build carries: the quality history's newest
 *            reading against the one before it (the two the release facts carry, never the whole history), and the
 *            CodeRabbit row the gate wrote for it (ADR-130). The badge's
 *            chips and its popover read this one answer.
 * Does not:  Fetch anything or type a count: a version the history or the findings file has no row for says so.
 * Used by:   StoreBar.tsx (the badge), GatePopover.tsx (the popover's words), gateFacts.test.ts.
 */
import RELEASES from 'virtual:release-facts';
import { heldAt, type HistoryRow } from '../../../lib/qualityHeld';
import { reviewedWord } from '../../../lib/reviewMirror';
import type { ReleaseFacts } from '../../../lib/releaseFacts';

/** The badge's answer: the version, whether the gate's own reading of it is on file, the quality held, the review. */
export type GateFacts = {
  version: string;
  commit: string;
  /** The gate measured this version: its reading is the history's newest. */
  gated: boolean;
  quality: { held: number; of: number; better: number } | null;
  review: ReleaseFacts['review'];
  /** The public gate run that shipped this version, its proof in one click; null until the gate has written it. */
  run: number | null;
};

/** The answer for the rows and facts given; the default is what this build carries. */
export function gateFacts(
  rows: readonly HistoryRow[] = RELEASES.latest,
  facts: ReleaseFacts = RELEASES
): GateFacts {
  const newest = rows[rows.length - 1];
  const gated = newest !== undefined && newest.version === facts.version;
  return {
    version: facts.version,
    commit: gated ? (newest.commit ?? '').slice(0, 7) : '',
    gated,
    quality: gated ? heldAt(newest, rows[rows.length - 2]) : null,
    review: facts.review,
    run: gated ? facts.run : null,
  };
}

/** Where a gate run is read in public: its page on GitHub Actions. */
export function gateRunUrl(run: number): string {
  return `https://github.com/SteveStout/TheYard/actions/runs/${run}`;
}

/**
 * The three facts, in words: the gate, the quality numbers held, and CodeRabbit's read. A version the gate has not
 * finished (its own build, on a gate branch) says "pending" for what is not written yet, short so the band keeps its
 * room on either site; a version CodeRabbit could not read in full says why, rate limited or not reviewed.
 */
export function gateChips(facts: GateFacts): [string, string, string] {
  return [
    facts.gated ? 'Gate green' : 'Gate pending',
    facts.quality ? `Quality ${facts.quality.held} of ${facts.quality.of} held` : 'Quality pending',
    `CodeRabbit ${reviewedWord(facts.review)}`,
  ];
}

/** The popover's quality line: "none worse" only when every number held, and how many moved the good way. */
export function qualityLine({
  held,
  of,
  better,
}: {
  held: number;
  of: number;
  better: number;
}): string {
  const worse = of - held;
  const moved = better > 0 ? `, ${better} better` : '';
  return `${held} of ${of} quality numbers held, ${worse === 0 ? 'none' : worse} worse than in the version before${moved}`;
}

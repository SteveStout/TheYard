/**
 * Does:      Reads the two release facts the first paint needs from the repository's own records, at build time:
 *            how many versions the changelog dates on each of its newest days, and the newest CodeRabbit row
 *            (ADR-132). Counts the releases of the last seven Central days from them.
 * Does not:  Read a file (vite.config.ts hands the text in, as the virtual module virtual:release-facts), or type a
 *            count: every number is the changelog's or the findings file's.
 * Used by:   vite.config.ts (carryTheReleaseFacts), Landing.tsx, GateBadge.tsx, releaseFacts.test.ts.
 */

// #region release-facts
/** What the build carries: versions a day, the top version, the day it was built and that version's review. */
export type ReleaseFacts = {
  /** Versions the changelog dates on each day, for its newest fourteen days, keyed YYYY-MM-DD. */
  days: Record<string, number>;
  /** The changelog's top version, the one this build is. */
  version: string;
  /** The Central day the build ran, so a page drawn ahead of a visit counts from a day it knows. */
  builtOn: string;
  /** The findings file's row for the top version; null until the gate has written it. */
  review: { version: string; reviewed: string; critical: number } | null;
};

/** How many of the changelog's newest days the build keeps: twice the window, so a late visit still counts true. */
const DAYS_KEPT = 14;

/** A dated changelog line: `- **1.0.5.75** (2026-11-17): ...`. */
const DATED = /^- \*\*(\d+\.\d+\.\d+\.\d+)\*\* \((\d{4}-\d\d-\d\d)\)/gm;

/** The day in Central time, YYYY-MM-DD, as the changelog dates its lines. */
export function centralDay(at: Date): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Chicago' }).format(at);
}

type FindingsFile = {
  rows: { version: string; reviewed?: string; findings?: { critical?: number } }[];
};

/** The facts from the changelog's text and the findings file, as the build reads them. */
export function releaseFacts(
  changelog: string,
  findings: FindingsFile,
  builtAt: Date
): ReleaseFacts {
  const dated = [...changelog.matchAll(DATED)].map((match) => ({
    version: match[1]!,
    day: match[2]!,
  }));
  const days: Record<string, number> = {};
  for (const { day } of dated) {
    if (!(day in days) && Object.keys(days).length === DAYS_KEPT) break;
    days[day] = (days[day] ?? 0) + 1;
  }
  const version = dated[0]?.version ?? '';
  const row = findings.rows.find((entry) => entry.version === version);
  return {
    days,
    version,
    builtOn: centralDay(builtAt),
    review: row
      ? { version, reviewed: row.reviewed ?? '', critical: row.findings?.critical ?? 0 }
      : null,
  };
}

/** The versions dated in the seven Central days ending on <today>, today included. */
export function releasesInLastSevenDays(days: Record<string, number>, today: string): number {
  const end = Date.parse(`${today}T00:00:00Z`);
  const start = end - 6 * 86_400_000;
  return Object.entries(days)
    .filter(([day]) => {
      const at = Date.parse(`${day}T00:00:00Z`);
      return at >= start && at <= end;
    })
    .reduce((sum, [, count]) => sum + count, 0);
}
// #endregion release-facts

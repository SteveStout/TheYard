/**
 * Does:      Reads the hook on each page a reader is sent to (ADR-132, A hook on every page) from data/hooks.json, the one
 *            file the API reads for /about and the resume door too; builds the landing hook's chart from the quality
 *            history the build carries, so the first screen draws with no read; fills a count a label names; and
 *            says whether an address opens Admin, which no hook may; and reads each page's "Where next" links (ladder v7).
 * Does not:  Draw anything (components/shared/Hook does), fetch anything, or type a count: every number comes from data.
 *            Builds the landing's lines too: its headline, the held-since line under it and the week's releases.
 * Used by:   Landing.tsx, WhereNext.tsx, pageHooks.test.ts.
 */
import file from '../../data/hooks.json';
import history from '../../data/quality-history.json';
import type { QualityTrendData } from './qualityTrend';
import { heldSince, measured, type HistoryRow } from './qualityHeld';

/** The pages that carry a hook, one entry each in the file. */
export const HOOK_PAGES = ['landing', 'about', 'post', 'resume', 'quality', 'review'] as const;

/** A page's name in the file. */
export type HookPage = (typeof HOOK_PAGES)[number];

/** One page's entry: its words, each copied word for word from its source, its chart and its one link. */
export type PageHook = {
  page: string;
  path: string;
  lines: string[];
  source: string;
  chart: { number: string; label: string; direction: string } | null;
  link: { label: string; href: string } | null;
};

const HOOKS: readonly PageHook[] = file.hooks as PageHook[];

/** The entry for a page; the file has one for every page in HOOK_PAGES, which pageHooks.test.ts holds. */
export function hookFor(page: HookPage): PageHook {
  return HOOKS.find((hook) => hook.page === page)!;
}

/**
 * The hook's chart as the Quality page draws its first one: every measured reading of the number, in the history's
 * order, with the ratchet's first version marked. Null when the hook has no chart.
 */
export function hookTrend(hook: PageHook): QualityTrendData | null {
  if (!hook.chart) return null;
  const key = hook.chart.number;
  const points = history.rows.flatMap((row) => {
    const value = (row.numbers as Record<string, unknown>)[key];
    return typeof value === 'number'
      ? [{ date: row.date, version: row.version, value, preScan: row.source === 'pre-scan' }]
      : [];
  });
  return {
    label: hook.chart.label,
    direction: hook.chart.direction,
    marker: history.first_ratchet_version,
    points,
  };
}

/** A day of the history, 2026-11-10, as the page says it: 10 November. */
function dayWords(day: string): string {
  const at = new Date(`${day}T12:00:00Z`);
  return Number.isNaN(at.getTime())
    ? day
    : at.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', timeZone: 'UTC' });
}

/** <count> <noun>, the noun made plural past one. */
function counted(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}

/**
 * The landing's words (ADR-132): the headline, then the run the quality history holds
 * now, computed from it (heldSince), then the hook's own lines with their counts read from the history, then the
 * releases of the last seven days the caller counted from the changelog. The held line is left out when the newest
 * version made a number worse, so it never claims a run that is not there.
 */
export function landingLines(hook: PageHook, releasesThisWeek: number): string[] {
  const rows = measured(history.rows as unknown as HistoryRow[]);
  const newest = rows[rows.length - 1];
  const counts = {
    measures: newest ? Object.keys(newest.numbers).length : 0,
    versions: rows.length,
  };
  const [headline = '', ...rest] = hook.lines.map((line) => withCounts(line, counts));
  const held = heldSince(rows);
  const heldLine = held
    ? [
        `No quality number has gotten worse at any release since ${held.version} on ${dayWords(held.date)}: ${counted(held.releases, 'release')} in a row.`,
      ]
    : [];
  return [
    headline,
    ...heldLine,
    ...rest,
    `${counted(releasesThisWeek, 'release')} in the last 7 days, each through the same gate.`,
  ];
}

/** The label with each {name} replaced by its count; a name with no count is left as it is. */
export function withCounts(label: string, counts: Record<string, number>): string {
  return label.replace(/\{([a-z_]+)\}/g, (named, name: string) =>
    name in counts ? String(counts[name]) : named
  );
}

/** The pages that carry a "Where next" footer, one entry each in the file's next list. */
export const NEXT_PAGES = [
  'landing',
  'review',
  'post',
  'about',
  'quality',
  'record',
  'architecture',
  'resume',
] as const;

/** A page's name in the next list. */
export type NextPage = (typeof NEXT_PAGES)[number];

/** One "Where next" link: its words, one line of reason, the minutes it takes, and where it goes. */
export type NextLink = { label: string; reason: string; minutes: number; href: string };

const NEXT: readonly { page: string; links: NextLink[] }[] = file.next;

/** A page's "Where next" links, at most two, which pageHooks.test.ts holds. */
export function nextFor(page: NextPage): NextLink[] {
  return NEXT.find((entry) => entry.page === page)?.links ?? [];
}

/** True when the address opens Admin: its view is admin, or it is one of Admin's own reads. */
export function opensAdmin(href: string): boolean {
  const address = new URL(href, 'https://theyard.stevenstout.biz');
  return (
    address.searchParams.get('view') === 'admin' ||
    address.pathname.startsWith('/admin') ||
    address.pathname.startsWith('/api/admin')
  );
}

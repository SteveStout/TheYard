/**
 * Does:      Reads the hook on each page a reader is sent to (ADR-132, A hook on every page) from data/hooks.json, the one
 *            file the API reads for /about and the resume door too; builds the landing hook's chart from the quality
 *            history the build carries, so the first screen draws with no read; fills a count a label names; and
 *            says whether an address opens Admin, which no hook may.
 * Does not:  Draw anything (components/shared/Hook does), fetch anything, or type a count: every number comes from data.
 * Used by:   Landing.tsx, pageHooks.test.ts.
 */
import file from '../../data/hooks.json';
import history from '../../data/quality-history.json';
import type { QualityTrendData } from './qualityTrend';

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

/** The label with each {name} replaced by its count; a name with no count is left as it is. */
export function withCounts(label: string, counts: Record<string, number>): string {
  return label.replace(/\{([a-z_]+)\}/g, (named, name: string) =>
    name in counts ? String(counts[name]) : named
  );
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

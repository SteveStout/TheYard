/**
 * Does:      Reads the hook on each page a reader is sent to (ADR-132, A hook on every page) from data/hooks.json, the one
 *            file the API reads for /about and the resume door too; builds the landing hook's chart from the quality
 *            history's digest the build carries (releaseFacts.ts), so the first screen draws with no read and no page
 *            carries the history itself; fills a count a label names; and
 *            says whether an address opens Admin, which no hook may; and reads each page's "Where next" links (ladder v7).
 * Does not:  Draw anything (components/shared/Hook does), fetch anything, or type a count: every number comes from data.
 *            Builds the landing's lines too: its headline, the held-since line under it and the week's releases.
 * Used by:   Landing.tsx, WhereNext.tsx, DocDialog.tsx, pageHooks.test.ts, WhereNext.test.ts.
 */
import file from '../../data/hooks.json';
import RELEASES from 'virtual:release-facts';
import type { QualityTrendData } from './qualityTrend';
import type { GateStop } from './releaseFacts';

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
  return {
    label: hook.chart.label,
    direction: hook.chart.direction,
    marker: RELEASES.quality.marker,
    points: RELEASES.quality.trends[hook.chart.number] ?? [],
  };
}

/** A day of the history, as the page says it: the day of the month, then the month's name. */
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
  const { measures, versions, held } = RELEASES.quality;
  const [headline = '', ...rest] = hook.lines.map((line) =>
    withCounts(line, { measures, versions })
  );
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

/**
 * The last version the gate turned back, from data/gate-reds.json (G5): the landing ends on it, under the strip of
 * what the gate measured. Never one of the hook's lines, which have to fit a phone's first screen (hooks.spec.ts).
 */
export function stoppedLine(stopped: GateStop): string {
  const where =
    stopped.failed.length === 0
      ? ''
      : `, at its ${stopped.failed.join(' and ')} job${stopped.failed.length === 1 ? '' : 's'}`;
  return `The last release the gate stopped was ${stopped.version} on ${dayWords(stopped.day)}${where}.`;
}

/** The label with each {name} replaced by its count; a name with no count is left as it is. */
export function withCounts(label: string, counts: Record<string, number>): string {
  return label.replace(/\{([a-z_]+)\}/g, (named, name: string) =>
    name in counts ? String(counts[name]) : named
  );
}

/** The pages that carry a "Where next" footer, one entry each in the file's next list. */
export const NEXT_PAGES = ['landing', 'post', 'about', 'record', 'architecture', 'resume'] as const;

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

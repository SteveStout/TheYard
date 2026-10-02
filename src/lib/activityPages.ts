// What people looked at and where they came from, in words a reader knows:
// an address turned into the page it is, the recruiter's path as bar widths,
// and a referring host put in one of five groups. Its own file because it is
// naming, not counting or drawing, and its pattern lists grow on their own.

import type { ActivityPath } from './activityTypes';

/**
 * Each step's bar as a share of the widest, which is the first step when the
 * steps are walked in order; a step anybody reached is never drawn thinner
 * than a sliver, so one resume opened among a thousand visits still shows.
 */
export function pathShares(path: { visitor_days: number }[]): number[] {
  const most = Math.max(0, ...path.map((step) => step.visitor_days));
  return path.map((step) =>
    most === 0 || step.visitor_days === 0 ? 0 : Math.max(0.02, step.visitor_days / most)
  );
}

// #region page-names
/**
 * A path as the page a person asked for: the listing, the facets, About
 * Steven, and not the route, so the list reads as what people looked at.
 * The most particular pattern first; a path nothing names is itself.
 */
const PAGE_NAMES: readonly (readonly [RegExp, string])[] = [
  [/^\/(index\.html)?$/, 'Opened the site'],
  [/^\/api\/vehicles$/, 'Inventory listing'],
  [/^\/api\/vehicles\/[^/]+\/bids$/, "One vehicle's bids"],
  [/^\/api\/vehicles\/[^/]+$/, 'One vehicle'],
  [/^\/api\/facets$/, 'Facets and filters'],
  [/^\/api\/bids$/, 'Bids'],
  [/^\/api\/auth\/me$/, 'Who am I (sign-in state)'],
  [/^\/api\/stores$/, 'The store bar'],
  [/^\/api\/version$/, 'The version line'],
  [/^\/api\/tests\/summary$/, 'The test counts'],
  [/^\/api\/docs\/author$/, 'About Steven'],
  [/^(\/api\/docs\/resume|\/docs\/resume\.pdf|\/resume\.pdf)$/, 'The resume'],
  [/^\/api\/docs\/(.+)$/, 'Document: $1'],
  [/^\/api\/admin\/(.+)$/, 'Admin: $1'],
];

/** The page name for one path: the first pattern in PAGE_NAMES that matches it, or the path itself. */
export function pageName(path: string): string {
  for (const [pattern, name] of PAGE_NAMES) {
    const match = pattern.exec(path);
    if (match) return name.replace('$1', match[1] ?? '');
  }
  return path;
}

/** Paths by page name, two paths that are one page added together, most asked for first. */
export function namedPaths(paths: ActivityPath[]): { name: string; requests: number }[] {
  const merged = new Map<string, number>();
  for (const entry of paths) {
    const name = pageName(entry.path);
    merged.set(name, (merged.get(name) ?? 0) + entry.requests);
  }
  return [...merged.entries()]
    .map(([name, requests]) => ({ name, requests }))
    .sort((a, b) => b.requests - a.requests || a.name.localeCompare(b.name));
}

/**
 * Where they came from: the host of the page that linked here, put in one of
 * five groups, in a fixed order. A host nothing names is another site;
 * "(none)" is a page load with no referring page, typed, bookmarked, or opened
 * from something that says nothing, a PDF among them.
 */
export type SourceGroup = 'linkedin' | 'github' | 'search' | 'other' | 'none';

/** The five groups in the fixed order the tile lists them. */
export const SOURCE_GROUPS: readonly SourceGroup[] = [
  'linkedin',
  'github',
  'search',
  'other',
  'none',
];

/** Each group as the tile names it. */
export const SOURCE_NAMES: Readonly<Record<SourceGroup, string>> = {
  linkedin: 'LinkedIn',
  github: 'GitHub',
  search: 'Search',
  other: 'Another site',
  none: 'Typed or unknown',
};

/** The group one referring host falls in, read off its name: LinkedIn, GitHub, a search engine, or another site. */
export function sourceGroup(host: string): SourceGroup {
  if (host === '(none)') return 'none';
  if (/(^|\.)linkedin\.com$|^lnkd\.in$/.test(host)) return 'linkedin';
  if (/(^|\.)github\.(com|io)$/.test(host)) return 'github';
  if (
    /(^|\.)(google\.[a-z.]+|bing\.com|duckduckgo\.com|yahoo\.com|ecosia\.org|baidu\.com|yandex\.[a-z]+|startpage\.com)$/.test(
      host
    ) ||
    host === 'search.brave.com'
  )
    return 'search';
  return 'other';
}

/** The hosts added up by group, every group present and in the fixed order, so the tile's rows never move. */
export function groupSources(
  sources: { host: string; visitor_days: number }[]
): { group: SourceGroup; visitor_days: number }[] {
  const totals = new Map<SourceGroup, number>(SOURCE_GROUPS.map((group) => [group, 0]));
  for (const entry of sources) {
    const group = sourceGroup(entry.host);
    totals.set(group, (totals.get(group) ?? 0) + entry.visitor_days);
  }
  return SOURCE_GROUPS.map((group) => ({ group, visitor_days: totals.get(group) ?? 0 }));
}
// #endregion page-names

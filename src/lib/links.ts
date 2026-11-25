/**
 * Tagged links (ADR: Tagged links, who opened what): a link the operator sends carries ?r= and
 * six opaque characters. The page is served by the rendering service, so the code on its address
 * reaches the API only when the page reports it; this is the one line that decides whether to,
 * and what the Admin tab's card makes of what the API kept. No React in here.
 */

/** The code a tagged address carries, or null when it carries none or not a code. */
export function linkCode(search: string): string | null {
  const code = new URLSearchParams(search).get('r');
  return code !== null && /^[a-z0-9]{6}$/.test(code) ? code : null;
}

/**
 * The report the page sends once for its own open, or null when the address carries no code.
 * @param path the page view the open is kept under: "/" for the home page, "/review" for the review route
 */
export function pageOpenReport(search: string, path: string): string | null {
  const code = linkCode(search);
  return code === null ? null : `/api/links/open?r=${code}&path=${path}`;
}

/** Where a visit's code is held, so a footer link can carry it (ladder v7b). */
const HELD = 'yard-link-code';

/**
 * The code this visit arrived with: the address's, kept in the session as it is read, or the one kept earlier in
 * the visit. Null when there is none, or the store is shut.
 */
export function holdCode(
  search: string,
  store: Pick<Storage, 'getItem' | 'setItem'> | null
): string | null {
  const code = linkCode(search);
  try {
    if (code !== null) store?.setItem(HELD, code);
    return code ?? linkCode(`?r=${store?.getItem(HELD) ?? ''}`);
  } catch {
    return code;
  }
}

/** The session store, or null where there is none or it is shut. */
export function sessionOrNull(): Storage | null {
  try {
    return typeof window === 'undefined' ? null : window.sessionStorage;
  } catch {
    return null;
  }
}

/** A link of this site with the visit's code on it, or the link as it was: another site's link never carries one. */
export function withCode(href: string, code: string | null): string {
  if (code === null || !href.startsWith('/')) return href;
  // The code goes in the query, before a section's #name, or the browser reads it as part of the section's name.
  const [path, section] = href.split('#', 2);
  const coded = path + (path.includes('?') ? '&' : '?') + `r=${code}`;
  return section === undefined ? coded : `${coded}#${section}`;
}

/**
 * The view a page reports its open under: the blog, the Quality page and About Steven by name, else what the caller
 * says. A sent About link is /?doc=author, and /about with a code redirects there, so it counts under /about.
 */
export function reportPath(search: string, fallback: string): string {
  const params = new URLSearchParams(search);
  if (params.get('view') === 'blog') return '/blog';
  if (params.get('doc') === 'quality') return '/quality';
  if (params.get('doc') === 'author' || params.get('view') === 'about') return '/about';
  return fallback;
}

/** One code's row on the card, as the API answers it. */
export type LinkOpensRow = {
  code: string;
  first_ms: number;
  last_ms: number;
  opens: number;
  days: number;
  paths: Record<string, number>;
  /** A day of the code's has a link scanner's shape: four or more different paths inside 30 seconds (ladder v7c). */
  scanned?: boolean;
};

/** The card's report. */
export type LinkOpensReport = {
  available: boolean;
  note: string | null;
  window: string;
  codes: LinkOpensRow[];
  keys: string[];
  /** How many of the codes are flagged as scanned (ladder v7c). */
  scanned_codes?: number;
};

/** The windows the card offers, in the order its buttons show them. */
export const LINK_WINDOWS = ['7d', '30d', '90d'] as const;

/** A row's paths as one line, busiest first: "/ 2 · /resume/email 1". */
export function pathsLine(paths: Record<string, number>): string {
  return Object.entries(paths)
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .map(([path, count]) => `${path} ${count}`)
    .join(' · ');
}

/** When an open happened, as the card prints it: the UTC day and minute. */
export function openStamp(ms: number): string {
  return new Date(ms).toISOString().slice(0, 16).replace('T', ' ') + ' UTC';
}

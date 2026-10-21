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

/** The report the page sends once for its own open, or null when the address carries no code. */
export function pageOpenReport(search: string): string | null {
  const code = linkCode(search);
  return code === null ? null : `/api/links/open?r=${code}&path=/`;
}

/** One code's row on the card, as the API answers it. */
export type LinkOpensRow = {
  code: string;
  first_ms: number;
  last_ms: number;
  opens: number;
  days: number;
  paths: Record<string, number>;
};

/** The card's report. */
export type LinkOpensReport = {
  available: boolean;
  note: string | null;
  window: string;
  codes: LinkOpensRow[];
  keys: string[];
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

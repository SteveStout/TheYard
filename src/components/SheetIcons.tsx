import type { DocKind } from './DocsMenu';
import type { NavIcon } from '../lib/siteMap';

/** Every row kind the sidebar draws: the four doc kinds plus three actions. */
export type RowKind = DocKind | 'external' | 'admin' | 'reset' | 'account';

/**
 * One stroked path per kind on a 20x20 grid. Eight icons cover every row in
 * the sidebar (ADR-011 addendum, ADR-013): a doc is an overview, a decision
 * record, the infrastructure file, or the changelog; the rest are links out,
 * the Admin tab, Reset bids, and the account. Rows reuse these; nothing gets a
 * bespoke icon.
 */
// #region icons
// One stroked path per row kind on a shared 20x20 grid, rather than an icon
// library: eight paths cover every row in the rail and the drawer, they inherit
// currentColor so a theme change needs no icon work, and the bundle carries no
// font or sprite (ADR-011).
const PATHS: Record<RowKind, string> = {
  overview:
    'M6 2.5h6.5L17 6.5v10a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1v-13a1 1 0 0 1 1-1zM12.5 2.5v4h4M8 10.5h5M8 13.5h5',
  adr: 'M6 2.5h6.5L17 6.5v10a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1v-13a1 1 0 0 1 1-1zM12.5 2.5v4h4M7.5 12.5l2 2 3.5-3.5',
  infra: 'M10 3.5 3.5 7 10 10.5 16.5 7 10 3.5zM3.5 10.5 10 14l6.5-3.5M3.5 13.5 10 17l6.5-3.5',
  changelog: 'M3.5 10.5v-6a1 1 0 0 1 1-1h6l6.5 6.5-7 7-6.5-6.5zM7.5 7.5h.01',
  external:
    'M11 4H5.5A1.5 1.5 0 0 0 4 5.5v9A1.5 1.5 0 0 0 5.5 16h9a1.5 1.5 0 0 0 1.5-1.5V9M12.5 3.5h4v4M16.5 3.5l-7 7',
  admin: 'M3 10.5h3.2l2-5.5 3.6 10 2-4.5H17',
  reset: 'M4.5 10a5.5 5.5 0 1 1 1.6 3.9M4.5 15v-4.5H9',
  account: 'M10 4a2.6 2.6 0 1 1 0 5.2A2.6 2.6 0 0 1 10 4zM4.9 16.4a5.1 5.1 0 0 1 10.2 0',
};
// #endregion icons

/**
 * A sidebar row's leading icon. Decorative, so hidden from assistive tech;
 * the row's text carries the meaning. Drawn in currentColor so the row's
 * state (rest, hover, pressed) colors it through CSS alone.
 */
export function RowIcon({ kind, className }: { kind: RowKind; className?: string }) {
  return (
    <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true" className={className}>
      <path
        d={PATHS[kind]}
        fill="none"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

// #region nav-icons
/**
 * The site map's icons (src/lib/siteMap.ts): one per section and action, on the
 * same 20 by 20 grid, the same stroke and the same currentColor as the row
 * icons above, so a tile on the landing page and a row in the sidebar are
 * drawn in one hand. Five reuse a row icon's path; the rest are new.
 */
const NAV_PATHS: Record<NavIcon, string> = {
  inventory:
    'M3.5 12.5v-2l1.8-4a1.5 1.5 0 0 1 1.4-1h6.6a1.5 1.5 0 0 1 1.4 1l1.8 4v2a1 1 0 0 1-1 1h-11a1 1 0 0 1-1-1zM3.5 10.5h13M6 15.5v-2M14 15.5v-2',
  architecture: 'M3.5 3.5h5v5h-5zM11.5 3.5h5v5h-5zM3.5 11.5h5v5h-5zM11.5 11.5h5v5h-5z',
  api: 'M7 6.5 3.5 10 7 13.5M13 6.5l3.5 3.5-3.5 3.5M11.2 4.5l-2.4 11',
  stores:
    'M4 5c0-1.1 2.7-2 6-2s6 .9 6 2-2.7 2-6 2-6-.9-6-2zM4 5v10c0 1.1 2.7 2 6 2s6-.9 6-2V5M4 10c0 1.1 2.7 2 6 2s6-.9 6-2',
  performance: 'M3.5 14a6.5 6.5 0 1 1 13 0M10 14l3-4.5',
  diagrams: 'M3.5 3.5h5v5h-5zM11.5 11.5h5v5h-5zM6 8.5v4a1.5 1.5 0 0 0 1.5 1.5h4',
  style:
    'M10 3.5c3.6 0 6.5 2.6 6.5 5.8 0 1.9-1.5 3.2-3.3 3.2h-1.5a1.3 1.3 0 0 0-.9 2.2 1.3 1.3 0 0 1-.9 2.2c-3.6 0-6.4-2.9-6.4-6.7s2.9-6.7 6.5-6.7zM6.8 9.5h.01M9 6.7h.01M12.6 7h.01',
  hosting: 'M6 15.5a3.5 3.5 0 0 1-.4-7 4.5 4.5 0 0 1 8.7-.9A3.8 3.8 0 0 1 14 15.5z',
  ai: 'M10 3.5l1.6 4.9 4.9 1.6-4.9 1.6-1.6 4.9-1.6-4.9-4.9-1.6 4.9-1.6z',
  cicd: 'M4.5 10a5.5 5.5 0 0 1 9.6-3.6M15.5 10a5.5 5.5 0 0 1-9.6 3.6M14.5 3.5v3h-3M5.5 16.5v-3h3',
  practices: 'M10 3l6 2.2v4.3c0 3.7-2.6 6.3-6 7.5-3.4-1.2-6-3.8-6-7.5V5.2zM7.5 10l1.8 1.8 3.2-3.3',
  records: PATHS.adr,
  changelog: PATHS.changelog,
  about: 'M10 3.5a6.5 6.5 0 1 1 0 13 6.5 6.5 0 0 1 0-13zM10 9.5v4M10 6.8h.01',
  author: PATHS.account,
  admin: PATHS.admin,
  signin: 'M11 3.5h4.5a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H11M3.5 10h8M8.5 7l3 3-3 3',
  resume: PATHS.overview,
  repo: 'M6 4v12M14 4.5a1.8 1.8 0 1 1 0 3.6 1.8 1.8 0 0 1 0-3.6zM14 8.1c0 3.4-8 2.6-8 6',
};

/** A site-map icon at any size. Decorative: the tile or row it sits in carries the name. */
export function NavGlyph({
  icon,
  size = 20,
  className,
}: {
  icon: NavIcon;
  size?: number;
  className?: string;
}) {
  return (
    <svg
      viewBox="0 0 20 20"
      width={size}
      height={size}
      aria-hidden="true"
      className={className}
      data-icon={icon}
    >
      <path
        d={NAV_PATHS[icon]}
        fill="none"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}
// #endregion nav-icons

import type { DocKind } from './DocsMenu';

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

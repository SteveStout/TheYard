/**
 * Reading the site map: the sections of one group, and the landing page's
 * tiles in the order the page draws them. Its own file because this is the
 * one place the map is turned into a layout; the entries themselves stay a
 * plain list in siteMapEntries.ts.
 */

import { SITE_GROUPS, SITE_MAP } from './siteMapEntries';
import type { SiteAction, SiteGroupKey, SiteSection } from './siteMapTypes';

/** A landing tile: a section or an action, in the order the page draws them. */
export type LandingTile =
  { kind: 'section'; section: SiteSection } | { kind: 'action'; action: SiteAction };

/** A large tile's place in the top row: Inventory, Author, then the resume. */
function rankOf(tile: LandingTile): number {
  const rank = tile.kind === 'section' ? tile.section.featuredRank : tile.action.featuredRank;
  return rank ?? Number.MAX_SAFE_INTEGER;
}

/** The sections of one group, in map order. */
export function sectionsIn(group: SiteGroupKey, map = SITE_MAP): SiteSection[] {
  return map.sections.filter((section) => section.group === group);
}

/**
 * The landing page, read off the map: the large tiles (actions first, then
 * sections, each in map order); then every other section under its group's
 * heading, in group order; then the other actions. `grid` is the same tiles
 * as one list, in the order the page draws them.
 */
export function landingTiles(map = SITE_MAP): {
  featured: LandingTile[];
  groups: { key: SiteGroupKey; label: string; tiles: LandingTile[] }[];
  rest: LandingTile[];
  grid: LandingTile[];
} {
  const actions = map.actions.filter((action) => action.onLanding);
  const asSection = (section: SiteSection): LandingTile => ({ kind: 'section', section });
  const asAction = (action: SiteAction): LandingTile => ({ kind: 'action', action });
  const groups = SITE_GROUPS.map((group) => ({
    ...group,
    tiles: sectionsIn(group.key, map)
      .filter((s) => !s.featured)
      .map(asSection),
  })).filter((group) => group.tiles.length > 0);
  const rest = actions.filter((a) => !a.featured).map(asAction);
  return {
    featured: [
      ...actions.filter((a) => a.featured).map(asAction),
      ...map.sections.filter((s) => s.featured).map(asSection),
    ].sort((a, b) => rankOf(a) - rankOf(b)),
    groups,
    rest,
    grid: [...groups.flatMap((group) => group.tiles), ...rest],
  };
}

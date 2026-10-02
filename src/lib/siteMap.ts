/**
 * The site map: one data structure that the sidebar and the landing page are
 * both drawn from. The order of `sections` is the sidebar's section order and
 * the landing grid's order; the sections are listed group by group
 * (SITE_GROUPS), and both places show each group's heading above its sections;
 * the order of `actions` is the order of the sidebar's pinned rows and of the
 * action tiles. A section or an action added, removed, reordered or renamed
 * changes both places, because neither keeps a list of its own.
 *
 * What is in a section (its documents and its links) stays in MENUS in
 * src/library/sections.ts, beside the documents themselves. The map holds only
 * the shape of the site: what comes first, its icon, its one line, and
 * whether the landing page shows it large.
 *
 * No React here, so the structure can be tested on its own. This file is the
 * list of its parts, one line per part with the file beside it, plus the
 * section order every reader shares.
 */

import { SITE_MAP } from './siteMapEntries';
import type { MenuVariant } from './siteMapTypes';

export * from './siteMapTypes'; // the section names, the icons, a section's and an action's shape
export * from './siteMapEntries'; // the groups, then every section and action, in order
export * from './siteMapLanding'; // the sections of a group, and the landing page's tiles

// #region MENU_ORDER
/** Section order, top to bottom: the sidebar and the landing grid both follow it. */
export const MENU_ORDER: readonly MenuVariant[] = SITE_MAP.sections.map((section) => section.menu);
// #endregion MENU_ORDER

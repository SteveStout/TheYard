/**
 * The site map: one data structure that the sidebar and the landing page are
 * both drawn from (Steve, 2026-09-22: "one data structure in react that feeds
 * the navigation and the dashboard so this is more configurable"). The order
 * of `sections` is the sidebar's section order and the landing grid's order;
 * the sections are listed group by group (SITE_GROUPS), and both places show
 * each group's heading above its sections (1.0.1.4);
 * the order of `actions` is the order of the sidebar's pinned rows and of the
 * action tiles. A section or an action added, removed, reordered or renamed
 * here changes both places, because neither keeps a list of its own.
 *
 * What is in a section (its documents and its links) stays in MENUS in
 * DocsMenu.tsx, which holds the documents themselves. This file holds only
 * the shape of the site: what comes first, its icon, its one line, and
 * whether the landing page shows it large.
 *
 * No React here, so the structure can be tested on its own.
 */

/** Every sidebar section. The union lives here so the map can be checked for completeness. */
export type MenuVariant =
  | 'about'
  | 'architecture'
  | 'apiReference'
  | 'stores'
  | 'performance'
  | 'diagrams'
  | 'look'
  | 'hosting'
  | 'builtWithAi'
  | 'cicd'
  | 'practices'
  | 'records'
  | 'changelog'
  | 'author';

/** The tile and row icons, drawn in SheetIcons.tsx on the same 20 by 20 grid as the row icons. */
export type NavIcon =
  | 'home'
  | 'inventory'
  | 'architecture'
  | 'api'
  | 'stores'
  | 'performance'
  | 'diagrams'
  | 'style'
  | 'hosting'
  | 'ai'
  | 'cicd'
  | 'practices'
  | 'records'
  | 'changelog'
  | 'about'
  | 'author'
  | 'admin'
  | 'signin'
  | 'resume'
  | 'repo';

/**
 * The three groups the sections are gathered under, in order, with the
 * heading each shows in the sidebar and on the landing page (Steve,
 * 2026-09-22, 1.0.1.4: group and reorder both).
 */
export type SiteGroupKey = 'built' | 'run' | 'who';

export const SITE_GROUPS: readonly { key: SiteGroupKey; label: string }[] = [
  { key: 'built', label: 'How it is built' },
  { key: 'run', label: 'How it is run' },
  { key: 'who', label: 'Who and why' },
];

/**
 * A round photograph in a large tile's badge, in place of its icon: a square
 * crop, drawn at 88 px. `credit` is the photograph's line from
 * api/TheYard.Api/wwwroot/images/credits.json when it has one.
 */
export type BadgePhoto = { src: string; srcSet: string; credit?: string };

export type SiteSection = {
  menu: MenuVariant;
  icon: NavIcon;
  /** The group it sits under; the map lists a group's sections together, in group order. */
  group: SiteGroupKey;
  /** The tile's one line: a sentence, ending in a full stop. */
  blurb: string;
  /** Drawn as one of the large tiles at the top of the landing page. */
  featured?: boolean;
  badgePhoto?: BadgePhoto;
};

/**
 * The things that are not documents: a view of the app, or a link out. A view
 * is opened by App (it owns the address bar); a link is one of LINKS.
 */
export type SiteAction = {
  key: 'home' | 'inventory' | 'account' | 'admin' | 'resume' | 'repo';
  /** The label when nobody is signed in; the account row shows the address instead. */
  label: string;
  icon: NavIcon;
  blurb: string;
  featured?: boolean;
  /** A pinned row at the foot of the sidebar. */
  inRail: boolean;
  /** A tile on the landing page. The resume is the header's icon instead. */
  onLanding: boolean;
  badgePhoto?: BadgePhoto;
};

export const SITE_MAP: { sections: readonly SiteSection[]; actions: readonly SiteAction[] } = {
  sections: [
    // How it is built.
    {
      menu: 'architecture',
      icon: 'architecture',
      group: 'built',
      blurb: 'How the .NET API and the React app fit together.',
    },
    {
      menu: 'apiReference',
      icon: 'api',
      group: 'built',
      blurb: 'Every endpoint, with the OpenAPI document.',
    },
    {
      menu: 'stores',
      icon: 'stores',
      group: 'built',
      blurb: 'One site on two stores, side by side.',
    },
    {
      menu: 'diagrams',
      icon: 'diagrams',
      group: 'built',
      blurb: 'Infrastructure, data flow, the database, the two sites.',
    },
    {
      menu: 'look',
      icon: 'style',
      group: 'built',
      blurb: 'Colour, type and the rules the build enforces.',
    },
    {
      menu: 'builtWithAi',
      icon: 'ai',
      group: 'built',
      blurb: 'How the site was built with AI, and what that means.',
    },
    // How it is run.
    {
      menu: 'performance',
      icon: 'performance',
      group: 'run',
      blurb: 'What the site measures and how fast it answers.',
    },
    {
      menu: 'hosting',
      icon: 'hosting',
      group: 'run',
      blurb: 'Azure, Front Door and the container behind them.',
    },
    {
      menu: 'cicd',
      icon: 'cicd',
      group: 'run',
      blurb: 'The pipeline from commit to live, and its gates.',
    },
    {
      menu: 'practices',
      icon: 'practices',
      group: 'run',
      blurb: 'Security, observability, sealed by default.',
    },
    // Who and why. Author is the large tile at the top of the landing page,
    // and the last section of this group in the sidebar.
    {
      menu: 'records',
      icon: 'records',
      group: 'who',
      blurb: 'Every decision, and why it was made.',
    },
    {
      menu: 'changelog',
      icon: 'changelog',
      group: 'who',
      blurb: 'Every version and what changed in it.',
    },
    { menu: 'about', icon: 'about', group: 'who', blurb: 'What TheYard is and why it exists.' },
    {
      menu: 'author',
      icon: 'author',
      group: 'who',
      blurb: 'Steven Stout, who built it.',
      featured: true,
      // The vineyard selfie of Steve and Katie (Steve, 2026-09-22: option A).
      badgePhoto: {
        src: '/api/images/badges/author-176.jpg',
        srcSet: '/api/images/badges/author-176.webp 176w, /api/images/badges/author-264.webp 264w',
      },
    },
  ],
  actions: [
    {
      key: 'home',
      label: 'Home',
      icon: 'home',
      blurb: 'The front page, with every part of the site on it.',
      inRail: true,
      onLanding: false,
    },
    {
      key: 'inventory',
      label: 'Inventory',
      icon: 'inventory',
      blurb: 'Browse and bid on 100,000 vehicles in live auctions.',
      featured: true,
      // One of the inventory's own photographs, the yellow coupe (Steve, 2026-09-22: B).
      badgePhoto: {
        src: '/api/images/badges/inventory-176.jpg',
        srcSet:
          '/api/images/badges/inventory-176.webp 176w, /api/images/badges/inventory-264.webp 264w',
        credit:
          'Nissan Fairlady Z photograph by Kazyakuruma, CC BY-SA 4.0, from Wikimedia Commons.',
      },
      inRail: true,
      onLanding: true,
    },
    {
      key: 'account',
      label: 'Sign in',
      icon: 'signin',
      blurb: 'Sign in to bid under your own account.',
      inRail: true,
      onLanding: true,
    },
    {
      key: 'admin',
      label: 'Admin',
      icon: 'admin',
      blurb: 'The running system reporting on itself: up, fast, cost, errors.',
      inRail: true,
      onLanding: true,
    },
    {
      key: 'resume',
      label: "Steven's resume (PDF)",
      icon: 'resume',
      blurb: "Steven's resume, as a PDF.",
      inRail: true,
      onLanding: false,
    },
    {
      key: 'repo',
      label: 'GitHub repository',
      icon: 'repo',
      blurb: 'The source code and every CI run.',
      inRail: true,
      onLanding: true,
    },
  ],
};

// #region MENU_ORDER
/** Section order, top to bottom: the sidebar and the landing grid both follow it. */
export const MENU_ORDER: readonly MenuVariant[] = SITE_MAP.sections.map((section) => section.menu);
// #endregion MENU_ORDER

/** A landing tile: a section or an action, in the order the page draws them. */
export type LandingTile =
  { kind: 'section'; section: SiteSection } | { kind: 'action'; action: SiteAction };

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
    ],
    groups,
    rest,
    grid: [...groups.flatMap((group) => group.tiles), ...rest],
  };
}

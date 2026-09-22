/**
 * The site map: one data structure that the sidebar and the landing page are
 * both drawn from (Steve, 2026-09-22: "one data structure in react that feeds
 * the navigation and the dashboard so this is more configurable"). The order
 * of `sections` is the sidebar's section order and the landing grid's order;
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

export type SiteSection = {
  menu: MenuVariant;
  icon: NavIcon;
  /** The tile's one line: a sentence, ending in a full stop. */
  blurb: string;
  /** Drawn as one of the large tiles at the top of the landing page. */
  featured?: boolean;
};

/**
 * The things that are not documents: a view of the app, or a link out. A view
 * is opened by App (it owns the address bar); a link is one of LINKS.
 */
export type SiteAction = {
  key: 'inventory' | 'account' | 'admin' | 'resume' | 'repo';
  /** The label when nobody is signed in; the account row shows the address instead. */
  label: string;
  icon: NavIcon;
  blurb: string;
  featured?: boolean;
  /** A pinned row at the foot of the sidebar. */
  inRail: boolean;
  /** A tile on the landing page. The resume is the header's icon instead. */
  onLanding: boolean;
};

export const SITE_MAP: { sections: readonly SiteSection[]; actions: readonly SiteAction[] } = {
  sections: [
    {
      menu: 'architecture',
      icon: 'architecture',
      blurb: 'How the .NET API and the React app fit together.',
    },
    { menu: 'apiReference', icon: 'api', blurb: 'Every endpoint, with the OpenAPI document.' },
    { menu: 'stores', icon: 'stores', blurb: 'One site on two stores, side by side.' },
    {
      menu: 'performance',
      icon: 'performance',
      blurb: 'What the site measures and how fast it answers.',
    },
    {
      menu: 'diagrams',
      icon: 'diagrams',
      blurb: 'Infrastructure, data flow, the database, the two sites.',
    },
    { menu: 'look', icon: 'style', blurb: 'Colour, type and the rules the build enforces.' },
    { menu: 'hosting', icon: 'hosting', blurb: 'Azure, Front Door and the container behind them.' },
    {
      menu: 'builtWithAi',
      icon: 'ai',
      blurb: 'How the site was built with AI, and what that means.',
    },
    { menu: 'cicd', icon: 'cicd', blurb: 'The pipeline from commit to live, and its gates.' },
    { menu: 'practices', icon: 'practices', blurb: 'Security, observability, sealed by default.' },
    { menu: 'records', icon: 'records', blurb: 'Every decision, and why it was made.' },
    { menu: 'changelog', icon: 'changelog', blurb: 'Every version and what changed in it.' },
    { menu: 'about', icon: 'about', blurb: 'What TheYard is and why it exists.' },
    { menu: 'author', icon: 'author', blurb: 'Steven Stout, who built it.', featured: true },
  ],
  actions: [
    {
      key: 'inventory',
      label: 'Inventory',
      icon: 'inventory',
      blurb: 'Browse and bid on 100,000 vehicles in live auctions.',
      featured: true,
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

/**
 * The landing page's two groups, read off the map: the large tiles (actions
 * first, then sections, each in map order), then every other section in the
 * sidebar's order, then the other actions.
 */
export function landingTiles(map = SITE_MAP): { featured: LandingTile[]; grid: LandingTile[] } {
  const actions = map.actions.filter((action) => action.onLanding);
  const asSection = (section: SiteSection): LandingTile => ({ kind: 'section', section });
  const asAction = (action: SiteAction): LandingTile => ({ kind: 'action', action });
  return {
    featured: [
      ...actions.filter((a) => a.featured).map(asAction),
      ...map.sections.filter((s) => s.featured).map(asSection),
    ],
    grid: [
      ...map.sections.filter((s) => !s.featured).map(asSection),
      ...actions.filter((a) => !a.featured).map(asAction),
    ],
  };
}

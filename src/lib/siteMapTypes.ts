/**
 * The site map's vocabulary: the names of the sections, the icons, the groups,
 * and the shape of a section and of an action. Its own file so the entries in
 * siteMapEntries.ts and the reading in siteMapLanding.ts share one set of
 * types, and the unions can be checked for completeness on their own.
 */

/** Every sidebar section. The union lives here so the map can be checked for completeness. */
export type MenuVariant =
  | 'about'
  | 'architecture'
  | 'apiReference'
  | 'stores'
  | 'performance'
  | 'traffic'
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
  | 'traffic'
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

/** The keys of the three groups the sections are gathered under: how it is built, how it is run, who and why. */
export type SiteGroupKey = 'built' | 'run' | 'who';

/**
 * A round photograph in a large tile's badge, in place of its icon: a square
 * crop, drawn at 88 px. `credit` is the photograph's line from
 * api/TheYard.Api/wwwroot/images/credits.json when it has one.
 */
export type BadgePhoto = { src: string; srcSet: string; credit?: string };

/** One sidebar section as the map holds it: its icon, its group, its one line, and whether it is a large tile. */
export type SiteSection = {
  menu: MenuVariant;
  icon: NavIcon;
  /** The group it sits under; the map lists a group's sections together, in group order. */
  group: SiteGroupKey;
  /** The tile's one line: a sentence, ending in a full stop. */
  blurb: string;
  /** Drawn as one of the large tiles at the top of the landing page. */
  featured?: boolean;
  /** Where it sits among the large tiles, low first; the map's own order breaks a tie. */
  featuredRank?: number;
  badgePhoto?: BadgePhoto;
};

/**
 * The things that are not documents: a view of the app, or a link out. A view
 * is opened by useNavigation (useAddressBar owns the address bar); a link is one of LINKS.
 */
export type SiteAction = {
  key: 'home' | 'inventory' | 'account' | 'admin' | 'resume' | 'repo';
  /** The label when nobody is signed in; the account row shows the address instead. */
  label: string;
  icon: NavIcon;
  blurb: string;
  featured?: boolean;
  /** Where it sits among the large tiles, low first; the map's own order breaks a tie. */
  featuredRank?: number;
  /** A pinned row at the foot of the sidebar. */
  inRail: boolean;
  /** A tile on the landing page. */
  onLanding: boolean;
  badgePhoto?: BadgePhoto;
};

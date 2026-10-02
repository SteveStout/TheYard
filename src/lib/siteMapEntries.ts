/**
 * The site map's entries: the three groups, then every section and every
 * action in the order the sidebar and the landing page show them. Its own file
 * because this is the list a person edits to add, move or rename a part of the
 * site, and it reads best with nothing else around it.
 */

import type { SiteAction, SiteGroupKey, SiteSection } from './siteMapTypes';

/** The three groups the sections are gathered under, in order, with the heading each shows in the sidebar and on the landing page. */
export const SITE_GROUPS: readonly { key: SiteGroupKey; label: string }[] = [
  { key: 'built', label: 'How it is built' },
  { key: 'run', label: 'How it is run' },
  { key: 'who', label: 'Who and why' },
];

/**
 * The one structure: `sections` in sidebar and landing order, listed group by
 * group, and `actions` in the order of the sidebar's pinned rows and the
 * action tiles.
 */
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
    // #region look-row
    {
      menu: 'look',
      icon: 'style',
      group: 'built',
      blurb: 'Colour, type and the rules the build enforces.',
    },
    // #endregion look-row
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
      menu: 'traffic',
      icon: 'traffic',
      group: 'run',
      blurb: 'Who comes, what is kept about them, and how the site is found.',
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
      // The resume's own words, so the tile says what he is rather than that he exists;
      // short enough to keep to two lines at 1024 beside Inventory's two.
      blurb: 'Staff-level .NET engineer, twelve years full stack, seven fully remote.',
      featured: true,
      featuredRank: 2,
      // The author's vineyard photograph.
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
      featuredRank: 1,
      // One of the inventory's own photographs, the yellow coupe.
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
      // A large tile: the first thing a recruiter looks for, and as a sidebar
      // row alone it would sit behind the menu button on a phone.
      label: "Steven's resume (PDF)",
      icon: 'resume',
      blurb: 'Twelve years of full stack .NET on one page, opens as a PDF.',
      featured: true,
      featuredRank: 3,
      inRail: true,
      onLanding: true,
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

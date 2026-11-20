import { describe, expect, it } from 'vitest';
import { landingTiles, MENU_ORDER, SITE_GROUPS, SITE_MAP, type MenuVariant } from './siteMap';

// Every section once. A Record over the union makes the compiler refuse a
// section that is missing from this list, so the list and the type cannot drift.
const EVERY_SECTION: Record<MenuVariant, true> = {
  about: true,
  architecture: true,
  apiReference: true,
  stores: true,
  performance: true,
  traffic: true,
  diagrams: true,
  look: true,
  hosting: true,
  builtWithAi: true,
  quality: true,
  shed: true,
  cicd: true,
  practices: true,
  records: true,
  incidents: true,
  changelog: true,
  author: true,
  recommendations: true,
  stack: true,
};

describe('the site map', () => {
  it('names every section exactly once, and the sidebar order is the map order', () => {
    const menus = SITE_MAP.sections.map((section) => section.menu);
    expect(new Set(menus).size).toBe(menus.length);
    expect([...menus].sort()).toEqual(Object.keys(EVERY_SECTION).sort());
    expect(MENU_ORDER).toEqual(menus);
  });

  it('gives every section and action an icon and one sentence, in the house voice', () => {
    for (const item of [...SITE_MAP.sections, ...SITE_MAP.actions]) {
      expect(item.icon).toBeTruthy();
      expect(item.blurb).toMatch(/^[A-Z].*\.$/);
      expect(item.blurb.split('. ').length).toBe(1);
      // The house rule, written without the character itself, which the voice test forbids.
      const dash = String.fromCharCode(0x2014);
      expect(item.blurb.includes(dash)).toBe(false);
    }
    // The Author tile says what he is, in the resume's own title line.
    const author = SITE_MAP.sections.find((section) => section.menu === 'author')!;
    expect(author.blurb).toContain('Lead / Staff .NET engineer');
    const keys = SITE_MAP.actions.map((action) => action.key);
    expect(new Set(keys).size).toBe(keys.length);
  });

  it('groups the sections in the order Steve set, and lists each group together', () => {
    expect(SITE_GROUPS.map((group) => group.label)).toEqual([
      'How it is built',
      'How it is run',
      'Who and why',
    ]);
    expect(MENU_ORDER).toEqual([
      'architecture',
      'apiReference',
      'stores',
      'diagrams',
      'look',
      'builtWithAi',
      'stack',
      'quality',
      'shed',
      'performance',
      'traffic',
      'hosting',
      'cicd',
      'practices',
      'records',
      'incidents',
      'changelog',
      'about',
      'author',
      'recommendations',
    ]);
    // A group's sections sit together, so a heading above the first one covers them all.
    const order = SITE_MAP.sections.map((section) =>
      SITE_GROUPS.findIndex((g) => g.key === section.group)
    );
    expect(order).toEqual([...order].sort((a, b) => a - b));
    expect(order.every((index) => index >= 0)).toBe(true);
  });

  it('draws the home page from the same map: four doors with their chips, then the small tiles by group', () => {
    const { doors, explore, featured, grid } = landingTiles();
    const name = (tile: (typeof grid)[number]) =>
      tile.kind === 'section' ? tile.section.menu : tile.action.key;
    const cost = (tile: (typeof grid)[number]) =>
      tile.kind === 'section' ? tile.section.cost : tile.action.cost;
    // Start here: one row of four, the review route first, each with what it costs (ADR-082, the addendum on the
    // home page's order).
    expect(doors.map((door) => [name(door), cost(door)])).toEqual([
      ['review', '2 min'],
      ['author', '3 min'],
      ['inventory', 'Live'],
      ['resume', 'PDF'],
    ]);
    // The small tiles: the same groups, the stack page after Built with AI, GitHub in the first group, and no Sign
    // in or Admin, which the sidebar keeps.
    expect(explore.map((group) => [group.label, group.tiles.map(name)])).toEqual([
      [
        'How it is built',
        [
          'architecture',
          'apiReference',
          'stores',
          'diagrams',
          'look',
          'builtWithAi',
          'stack',
          'quality',
          'shed',
          'repo',
        ],
      ],
      ['How it is run', ['performance', 'traffic', 'hosting', 'cicd', 'practices']],
      ['Who and why', ['records', 'incidents', 'changelog', 'about', 'recommendations']],
    ]);
    const drawn = explore.flatMap((group) => group.tiles.map(name));
    expect(drawn).not.toContain('account');
    expect(drawn).not.toContain('admin');
    // The resume is large on the landing page, not a sidebar row only, and drawn as a banner.
    const resume = SITE_MAP.actions.find((action) => action.key === 'resume')!;
    expect([resume.featured, resume.banner, resume.onLanding, resume.inRail]).toEqual([
      true,
      true,
      true,
      true,
    ]);
    // Exactly two banners: the resume and the review route.
    expect(SITE_MAP.actions.filter((action) => action.banner).map((action) => action.key)).toEqual([
      'review',
      'resume',
    ]);
    expect(grid.map(name)).toEqual([
      ...MENU_ORDER.filter((menu) => menu !== 'author'),
      'account',
      'admin',
      'repo',
    ]);
    expect(grid).toHaveLength(22);
    // The two large tiles carry a photograph in the badge, and nothing else does.
    expect(
      featured.map(
        (tile) => (tile.kind === 'section' ? tile.section.badgePhoto : tile.action.badgePhoto)?.src
      )
    ).toEqual(['/api/images/badges/steve-176.jpg', '/api/images/badges/inventory-176.jpg']);
  });

  it('puts the review route directly under Home in the rail, so the Blog row is third', () => {
    const rail = SITE_MAP.actions.filter((action) => action.inRail).map((action) => action.key);
    expect(rail.slice(0, 3)).toEqual(['home', 'review', 'blog']);
    const review = SITE_MAP.actions.find((action) => action.key === 'review')!;
    expect(review).toMatchObject({
      label: 'Check my work',
      icon: 'review',
      featured: true,
      banner: true,
      onLanding: true,
    });
  });

  it('follows the map when the map changes, which is the point of it', () => {
    const moved = {
      sections: [...SITE_MAP.sections].reverse(),
      actions: SITE_MAP.actions.map((action) =>
        action.key === 'repo' ? { ...action, featured: true, featuredRank: 9 } : action
      ),
    };
    const { grid } = landingTiles(moved);
    expect(grid[0]).toEqual({
      kind: 'section',
      section: moved.sections.find((section) => !section.featured && section.group === 'built'),
    });
    // A tile the map makes large leaves the grid and joins the top row, last by its rank.
    const { featured } = landingTiles(moved);
    expect(grid.some((tile) => tile.kind === 'action' && tile.action.key === 'repo')).toBe(false);
    expect(featured[featured.length - 1]).toMatchObject({ kind: 'action' });
  });
});

import { describe, expect, it } from 'vitest';
import { landingTiles, MENU_ORDER, SITE_MAP, type MenuVariant } from './siteMap';

// Every section once. A Record over the union makes the compiler refuse a
// section that is missing from this list, so the list and the type cannot drift.
const EVERY_SECTION: Record<MenuVariant, true> = {
  about: true,
  architecture: true,
  apiReference: true,
  stores: true,
  performance: true,
  diagrams: true,
  look: true,
  hosting: true,
  builtWithAi: true,
  cicd: true,
  practices: true,
  records: true,
  changelog: true,
  author: true,
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
    const keys = SITE_MAP.actions.map((action) => action.key);
    expect(new Set(keys).size).toBe(keys.length);
  });

  it('draws the landing page from the same map: Inventory and Author large, then the rest in order', () => {
    const { featured, grid } = landingTiles();
    const name = (tile: (typeof grid)[number]) =>
      tile.kind === 'section' ? tile.section.menu : tile.action.key;
    expect(featured.map(name)).toEqual(['inventory', 'author']);
    expect(grid.map(name)).toEqual([
      ...MENU_ORDER.filter((menu) => menu !== 'author'),
      'account',
      'admin',
      'repo',
    ]);
    // Sixteen: four even rows of four on a desk and eight of two on a phone.
    expect(grid).toHaveLength(16);
  });

  it('follows the map when the map changes, which is the point of it', () => {
    const moved = {
      sections: [...SITE_MAP.sections].reverse(),
      actions: SITE_MAP.actions.map((action) =>
        action.key === 'resume' ? { ...action, onLanding: true } : action
      ),
    };
    const { grid } = landingTiles(moved);
    expect(grid[0]).toEqual({
      kind: 'section',
      section: moved.sections.find((section) => !section.featured),
    });
    expect(grid.some((tile) => tile.kind === 'action' && tile.action.key === 'resume')).toBe(true);
  });
});

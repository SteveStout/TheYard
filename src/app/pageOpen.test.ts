import { describe, expect, it } from 'vitest';
import { landingOf, LANDING_PAGES, pageOf } from './pageOpen';

// The six landing views a recruiter link opens share the review route's shell: the rail starts closed, the band is
// the live sentence, the box is the review route's (ADR-142, the addendum on the landing views).
describe('the landing views', () => {
  const view = (over: Partial<Parameters<typeof landingOf>[0]> = {}) =>
    landingOf({ page: null, homeShowing: false, adminOpen: false, accountOpen: false, ...over });

  it('are home, the review route, About, the blog (the list and every post) and the recommendations', () => {
    expect([...LANDING_PAGES].sort()).toEqual(['about', 'blog', 'recommendations', 'review']);
    expect(view({ homeShowing: true })).toBe(true);
    expect(view({ page: pageOf(false, true, false) })).toBe(true);
    expect(view({ page: pageOf(false, false, false, false, true) })).toBe(true);
    expect(view({ page: pageOf(true, false, false) })).toBe(true);
    expect(view({ page: pageOf(false, false, true) })).toBe(true);
  });

  it('are not the stack, the inventory, a vehicle or a document over home, nor Admin or Account drawn in their place', () => {
    expect(view({ page: pageOf(false, false, false, true) })).toBe(false);
    expect(view()).toBe(false);
    expect(view({ page: 'blog', adminOpen: true })).toBe(false);
    expect(view({ page: 'about', accountOpen: true })).toBe(false);
  });
});

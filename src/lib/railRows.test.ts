import { describe, expect, it } from 'vitest';
import { railActions } from './railRows';
import { SITE_MAP } from './siteMap';

const keys = (view: { reviewOpen: boolean; signedIn: boolean }) =>
  railActions(SITE_MAP.actions, view).map((action) => action.key);

describe('the rail on the review route', () => {
  it('draws neither Sign in nor Admin on the review route while nobody is signed in', () => {
    const drawn = keys({ reviewOpen: true, signedIn: false });
    expect(drawn).not.toContain('account');
    expect(drawn).not.toContain('admin');
    for (const kept of ['home', 'review', 'blog', 'inventory', 'resume', 'repo'])
      expect(drawn).toContain(kept);
  });

  it('keeps a signed-in visitor’s own account row on the review route, and never Admin', () => {
    const drawn = keys({ reviewOpen: true, signedIn: true });
    expect(drawn).toContain('account');
    expect(drawn).not.toContain('admin');
  });

  it('draws every rail action on every other view, in the site map’s order', () => {
    expect(keys({ reviewOpen: false, signedIn: false })).toEqual(
      SITE_MAP.actions.filter((action) => action.inRail).map((action) => action.key)
    );
  });
});

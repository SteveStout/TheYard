import { describe, expect, it } from 'vitest';
import { RECOMMENDATIONS, landingPicks } from './recommendations';

describe('the recommendations', () => {
  it('are the ten on his LinkedIn profile, each from a different person', () => {
    expect(RECOMMENDATIONS).toHaveLength(10);
    expect(new Set(RECOMMENDATIONS.map((entry) => entry.name)).size).toBe(10);
  });

  it('carry nothing a page should not: no address, no em dash, and a title and a date on every one', () => {
    for (const entry of RECOMMENDATIONS) {
      expect(entry.text).not.toContain('@');
      expect(`${entry.name} ${entry.title} ${entry.relationship} ${entry.text}`).not.toContain(
        '\u2014'
      );
      expect(entry.title.length).toBeGreaterThan(0);
      expect(entry.when).toMatch(/^[A-Z][a-z]+ 20\d\d$/);
    }
  });

  it('lend the landing page three sentences, each written by the person it is credited to', () => {
    const picks = landingPicks();
    expect(picks).toHaveLength(3);
    for (const pick of picks) {
      expect(pick.text).toContain(pick.pull);
    }
  });
});

import { describe, expect, it } from 'vitest';
import { drawLanding } from './drawLanding';

// #region draws-without-a-browser
describe('the landing page drawn on a server', () => {
  it('draws with no window, so a component that reads the browser on its first draw fails here and not on the live site', () => {
    // Vitest runs these in Node: touching window, document or localStorage while drawing
    // throws, which is the rule server rendering puts on every component (ADR: The landing
    // page rendered at build time, server rendering as the goal).
    expect(typeof (globalThis as { window?: unknown }).window).toBe('undefined');
    const html = drawLanding();
    expect(html).toContain('Welcome to The Yard');
    // Drawn once, whole, and nothing fetched: no loading state where the figures will go.
    expect(html.length).toBeGreaterThan(10_000);
  });
});
// #endregion draws-without-a-browser

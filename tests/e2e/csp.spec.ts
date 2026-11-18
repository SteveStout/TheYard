import { expect, test } from '@playwright/test';

// #region csp
/**
 * Nothing on the site breaks under its Content-Security-Policy (ADR: Every LinkedIn post is also a blog post, the
 * addendum on the headers): each kind of page, opened through the rendering service the way the edge sends a visitor,
 * runs, draws and reads with no violation of the policy, and every answer carries the headers. The service runs in the
 * browser suite only where its build was made and asked for (YARD_RENDER, the gate); elsewhere these end at their
 * first line, as drawn.spec.ts does.
 */
const RENDERED = 'http://localhost:5220';

for (const path of [
  '/',
  '/?view=blog',
  '/?view=inventory',
  '/?doc=readme',
  '/?view=admin',
  '/?view=review',
]) {
  test(`${path} runs under the policy with no violation`, async ({ page }) => {
    if (!process.env.YARD_RENDER) return;
    test.setTimeout(60_000);
    const violations: string[] = [];
    page.on('console', (message) => {
      if (/Content.Security.Policy/i.test(message.text())) violations.push(message.text());
    });
    await page.addInitScript(() => {
      document.addEventListener('securitypolicyviolation', (event) => {
        (window as unknown as { violated?: string[] }).violated ??= [];
        (window as unknown as { violated: string[] }).violated.push(
          `${event.violatedDirective} ${event.blockedURI}`
        );
      });
    });
    const answer = await page.goto(RENDERED + path);
    const policy = answer!.headers()['content-security-policy'] ?? '';
    expect(policy).toContain("frame-ancestors 'none'");
    expect(policy).not.toMatch(/script-src[^;]*'unsafe-inline'/);
    expect(answer!.headers()['strict-transport-security']).toBe(
      'max-age=31536000; includeSubDomains'
    );
    await page.waitForLoadState('networkidle');
    const violated = await page.evaluate(
      () => (window as unknown as { violated?: string[] }).violated ?? []
    );
    expect([...violations, ...violated]).toEqual([]);
  });
}
// #endregion csp

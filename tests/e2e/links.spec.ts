import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// #region tagged-links
/**
 * Tagged links (ADR: Tagged links, who opened what): a page opened with ?r= and a code reports
 * the open once, a pass-through to GitHub counts and sends the browser on, and the Admin tab's
 * "Who opened what" card shows the code with its paths and nothing that names a person.
 */
// A headless browser is a scanner to the filter (LinkPreviews), and Playwright's Chromium says
// HeadlessChrome, so the page here carries a person's agent, the way a sent link is opened.
test.use({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0 Safari/537.36' });

test('a tagged open is counted under its code and the card shows the code, never a name', async ({
  page,
  request,
}) => {
  const code = 'e2e' + Math.random().toString(36).slice(2, 5).padEnd(3, 'x');
  const reported = page.waitForResponse(
    (response) => response.url().includes('/api/links/open?r=' + code) && response.status() === 204
  );
  await openTheYard(page, `/?view=inventory&r=${code}`);
  await reported;

  // The pass-through counts and answers a redirect the browser follows; the API's own address is enough here.
  const through = await request.get(`http://localhost:5210/api/go/github?r=${code}`, {
    maxRedirects: 0,
    headers: {
      'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0 Safari/537.36',
    },
  });
  expect(through.status()).toBe(302);
  expect(through.headers()['location']).toContain('github.com');
  // A preview fetcher's open is not counted.
  const unfurled = await request.get(`http://localhost:5210/api/links/open?r=${code}&path=/`, {
    headers: { 'User-Agent': 'LinkedInBot/1.0 (compatible; Mozilla/5.0; Apache-HttpClient)' },
  });
  expect(unfurled.status()).toBe(204);

  await openTheYard(page, '/?view=admin&card=links');
  const card = page.getByTestId('links-card');
  await expect(card).toBeVisible();
  // The collector writes every five seconds; the card reads every thirty, so poll the route.
  await expect
    .poll(
      async () => {
        const answer = await request.get('http://localhost:5210/api/admin/links?window=7d');
        const report = (await answer.json()) as {
          codes: { code: string; opens: number; paths: Record<string, number> }[];
        };
        return report.codes.find((row) => row.code === code)?.opens ?? 0;
      },
      { timeout: 30_000 }
    )
    .toBe(2);
  await page.getByTestId('links-window-7d').click();
  const row = card.getByTestId('links-table').locator('tr', { hasText: code });
  await expect(row).toBeVisible({ timeout: 30_000 });
  await expect(row).toContainText('/api/go/github 1');
  await expect(card).not.toContainText('Chrome');
  await expect(card).not.toContainText('LinkedInBot');
});
// #endregion tagged-links

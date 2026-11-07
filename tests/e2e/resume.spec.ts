import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// The resume at its own address (ADR: The resume has an address): what a link
// Steve hands out opens, through the page's own origin as a visitor reaches it.
test('/resume serves the locked resume inline, the same bytes as the document the rail links (ADR: The resume has an address)', async ({
  request,
}) => {
  const address = await request.get('/resume?src=linkedin');
  expect(address.status()).toBe(200);
  expect(address.headers()['content-type']).toContain('application/pdf');
  expect(address.headers()['content-disposition']).toContain('inline');
  expect(address.headers()['content-disposition']).toContain('StevenStoutResume_2026-11-05.pdf');
  const document = await request.get('/api/docs/resume');
  expect(Buffer.compare(await address.body(), await document.body())).toBe(0);
});

test('a resume opened through a door is counted on the activity card by its door (ADR: The resume has an address)', async ({
  page,
  request,
}) => {
  const activity = (await (
    await request.get('http://localhost:5210/api/admin/activity?window=7d')
  ).json()) as { doors?: { door: string }[] };
  expect(activity.doors?.map((door) => door.door)).toEqual([
    'application',
    'email',
    'linkedin',
    'post',
    'signature',
    'other',
    'site',
  ]);
  await openTheYard(page, '/?view=admin&card=activity');
  await expect(page.getByTestId('activity-card').getByTestId('activity-doors')).toContainText(
    'Resume opens, by door'
  );
});

test('a browser that opens /resume gets the door: the band leads to the review route, and at 390 the PDF is a pill and nothing scrolls sideways (ADR: The resume has an address)', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto('/resume?src=email');
  const band = page.getByTestId('resume-band');
  await expect(band).toContainText('you can check it in ten minutes');
  await expect(page.locator('iframe')).toHaveAttribute('src', '/resume?pdf=1&src=email');
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('iframe')).toBeHidden();
  await expect(band.getByRole('link', { name: 'Open the PDF on its own' })).toBeVisible();
  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(sideways).toBeLessThanOrEqual(0);
  await page.setViewportSize({ width: 1280, height: 900 });
  await band.getByRole('link', { name: 'Check my work in ten minutes' }).click();
  await expect(page).toHaveURL(/\?view=review/);
  await expect(page.getByTestId('review-page')).toBeVisible();
});

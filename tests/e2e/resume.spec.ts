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
  expect(address.headers()['content-disposition']).toContain('StevenStoutResume_2026-09-29.pdf');
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

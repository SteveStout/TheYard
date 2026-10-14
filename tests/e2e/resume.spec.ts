import { expect, test } from '@playwright/test';

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

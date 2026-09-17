import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

/**
 * The type is the site's own (ADR: The palette, addendum of 17 September).
 *
 * Until 1.0.0.140 the page asked fonts.googleapis.com for a stylesheet and
 * fonts.gstatic.com for four files on every cold visit: two extra origins and
 * the one render-blocking resource on the page. Now the four faces are bundle
 * files. Two things are worth holding: that nothing on the page goes to a
 * font host any more, and that Poppins is still what the page paints with,
 * because a self-hosted font that fails to load falls back silently and the
 * only sign is a page that looks slightly wrong.
 */
test('the page loads its type from its own origin, and Poppins is the face it paints with', async ({
  page,
}) => {
  const thirdParty: string[] = [];
  page.on('request', (request) => {
    const host = new URL(request.url()).host;
    if (host.endsWith('googleapis.com') || host.endsWith('gstatic.com')) {
      thirdParty.push(request.url());
    }
  });

  await openTheYard(page);

  // document.fonts.ready resolves once every face the page asked for has
  // either loaded or failed; check() then says whether Poppins at the body
  // weight is usable, which is false when the file 404s or the face is not
  // declared at all.
  const poppins = await page.evaluate(async () => {
    await document.fonts.ready;
    const loaded = [...document.fonts]
      .filter((face) => face.family.replace(/"/g, '') === 'Poppins' && face.status === 'loaded')
      .map((face) => face.weight)
      .sort();
    return { usable: document.fonts.check('16px Poppins'), loaded };
  });

  expect(thirdParty, 'requests to a font host').toEqual([]);
  expect(poppins.usable).toBe(true);
  expect(poppins.loaded).toContain('400');
  // The files came from this origin. The suite runs on the development
  // server, which serves them from src/assets as they are; the production
  // build hashes them under /assets, and the after-ship read of the live site
  // is what holds that half (ADR: Cache headers).
  const fontRequests = await page.evaluate(() =>
    performance
      .getEntriesByType('resource')
      .map((entry) => entry.name)
      .filter((name) => name.includes('.woff2'))
  );
  expect(fontRequests.length).toBeGreaterThan(0);
  for (const url of fontRequests) {
    expect(new URL(url).origin).toBe(new URL(page.url()).origin);
    expect(new URL(url).pathname).toMatch(/poppins-latin-\d{3}/);
  }
});

import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

/**
 * The type is the site's own (ADR: The palette, addendum of 17 September).
 *
 * Until 1.0.0.140 the page asked fonts.googleapis.com for a stylesheet and
 * fonts.gstatic.com for four files on every cold visit: two extra origins and
 * the one render-blocking resource on the page. Now the four faces are bundle
 * files. Two things are worth holding: that nothing on the page goes to a
 * font host any more, and that the site's one face, IBM Plex Sans since the
 * 24 September addendum (Poppins before it), is what the page paints with,
 * because a self-hosted font that fails to load falls back silently and the
 * only sign is a page that looks slightly wrong.
 */
test('the page loads its type from its own origin, and IBM Plex Sans is the face it paints with', async ({
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

  // The head starts the type with the stylesheet rather than after it (1.0.3.1):
  // it paints on the first screen, and a preload of a font is a CORS fetch
  // whatever the origin, so the link carries crossorigin. Since IBM Plex Sans it
  // is one variable file for the four weights (Poppins was four files).
  const preloads = await page.evaluate(() =>
    [...document.querySelectorAll('link[rel="preload"][as="font"]')].map((link) => ({
      href: new URL(link.getAttribute('href')!, location.href).pathname,
      type: link.getAttribute('type'),
      crossorigin: link.getAttribute('crossorigin'),
    }))
  );
  expect(preloads).toHaveLength(1);
  for (const preload of preloads) {
    expect(preload.href).toMatch(/ibm-plex-sans-latin[^/]*\.woff2$/);
    expect(preload.href).not.toContain('poppins');
    expect(preload.type).toBe('font/woff2');
    expect(preload.crossorigin).toBe('');
  }

  // document.fonts.ready resolves once every face the page asked for has
  // either loaded or failed; check() then says whether the face at the body
  // weight is usable, which is false when the file 404s or the face is not
  // declared at all. And no face but the one is declared: Poppins is gone.
  const face = await page.evaluate(async () => {
    await document.fonts.ready;
    const families = [...new Set([...document.fonts].map((font) => font.family.replace(/"/g, '')))];
    const loaded = [...document.fonts]
      .filter(
        (font) => font.family.replace(/"/g, '') === 'IBM Plex Sans' && font.status === 'loaded'
      )
      .map((font) => font.weight)
      .sort();
    return {
      usable: [400, 500, 600, 700].every((weight) =>
        document.fonts.check(`${weight} 16px "IBM Plex Sans"`)
      ),
      loaded,
      families,
      body: getComputedStyle(document.body).fontFamily,
    };
  });

  expect(thirdParty, 'requests to a font host').toEqual([]);
  expect(face.usable).toBe(true);
  // One face carries the four weights the tokens name.
  expect(face.loaded).toEqual(['400 700']);
  expect(face.families).toEqual(['IBM Plex Sans']);
  expect(face.body).toMatch(/^"?IBM Plex Sans"?,/);
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
    expect(new URL(url).pathname).toMatch(/ibm-plex-sans-latin/);
  }
});

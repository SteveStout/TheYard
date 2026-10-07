import { expect, test, type Page } from '@playwright/test';
import { openTheYard } from './app';

/**
 * The landing page drawn at build time (ADR: The landing page rendered at build
 * time, server rendering as the goal). The build writes the landing page into
 * #root, the browser paints it before any script runs, and src/app/mount.tsx
 * takes it over with hydrateRoot. These hold the three things that can go wrong:
 * the page arrives empty, React finds markup it did not expect and throws it
 * away (a hydration error), or another address flashes the landing page before
 * its own view.
 */

/** What React says when the markup it finds is not the markup it draws, in development or in production. */
const HYDRATION =
  /hydrat|did not match|server rendered|Minified React error #(418|419|421|422|423|425)/i;

/** Every error the page reports, from the console and from uncaught throws. */
function watchErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

/**
 * Before any script runs (the document has been parsed, and the site's module
 * runs after this), keep the node the build drew and whether it is showing.
 */
async function keepTheDrawnNode(page: Page): Promise<void> {
  await page.addInitScript(() => {
    document.addEventListener('readystatechange', () => {
      if (document.readyState !== 'interactive') return;
      const root = document.getElementById('root');
      const keep = window as unknown as { __drawn?: Element | null; __drawnShown?: string };
      keep.__drawn = root?.firstElementChild ?? null;
      keep.__drawnShown = root ? getComputedStyle(root).display : '';
    });
  });
}

/** The dev server draws everything in the browser; the built page, which the gate opens, carries the drawing. */
const builtPage = (page: Page) =>
  page.evaluate(() => document.querySelector('script[src="/src/main.tsx"]') === null);

test('the bare address arrives with the landing page already drawn in its HTML', async ({
  page,
}) => {
  const html = await (await page.request.get('/')).text();
  const root = html.slice(html.indexOf('<div id="root"'), html.indexOf('<noscript>'));
  if (html.includes('/src/main.tsx')) {
    // The development server has no build step, so it draws everything in the browser.
    expect(root).toContain('<div id="root"></div>');
    return;
  }
  expect(root).toContain('data-drawn="landing"');
  expect(root).toContain('Welcome to The Yard');
  expect(root).toContain('data-testid="landing-tile-inventory"');
  // Drawn with nothing fetched: the API is asked nothing while the site is built.
  expect(root).toContain('data-state="reading"');
});

for (const viewport of [
  { width: 390, height: 844 },
  { width: 1280, height: 900 },
]) {
  test(`at ${viewport.width}, React takes the drawn landing page over with no hydration error`, async ({
    page,
  }) => {
    await page.setViewportSize(viewport);
    const errors = watchErrors(page);
    await keepTheDrawnNode(page);
    await openTheYard(page, '/');
    await expect(page.getByTestId('landing-admin-health')).toHaveText(/^(Healthy|Degraded)$/);

    expect(errors.filter((error) => HYDRATION.test(error))).toEqual([]);
    if (await builtPage(page)) {
      // Taken over, not replaced: the node the build drew is the node on the page now.
      const kept = await page.evaluate(() => {
        const keep = window as unknown as { __drawn?: Element | null };
        return (
          keep.__drawn != null &&
          document.getElementById('root')?.firstElementChild === keep.__drawn
        );
      });
      expect(kept, 'the drawn landing page is the one on the page').toBe(true);
    }

    // The frame the window wants: the docked rail on a desk, the header and its menu on a phone.
    if (viewport.width >= 1024) {
      await expect(page.getByTestId('side-rail')).toBeVisible();
      await expect(page.locator('[data-frame="header"]')).toHaveCount(0);
    } else {
      await expect(page.locator('[data-frame="header"]')).toBeVisible();
      await expect(page.getByTestId('side-rail')).toHaveCount(0);
    }

    // And it works: a tile opens the inventory.
    await page.getByTestId('landing-tile-inventory').click();
    await expect(page.getByTestId('view-announcement')).not.toHaveText('The Yard, home');
    expect(errors.filter((error) => HYDRATION.test(error))).toEqual([]);
  });
}

test('an address that opens another view never shows the drawn landing page', async ({ page }) => {
  const errors = watchErrors(page);
  await keepTheDrawnNode(page);
  await openTheYard(page, '/?view=inventory');

  if (await builtPage(page)) {
    const shown = await page.evaluate(
      () => (window as unknown as { __drawnShown?: string }).__drawnShown
    );
    expect(shown, 'the drawn landing page is kept off the screen').toBe('none');
  }
  await expect(page.getByTestId('landing')).toHaveCount(0);
  expect(errors.filter((error) => HYDRATION.test(error))).toEqual([]);
});

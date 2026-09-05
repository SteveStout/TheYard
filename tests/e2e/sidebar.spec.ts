import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

/**
 * The sidebar's docked shape (ADR-013): at 1024px and up the panel is a
 * persistent left rail, the header is gone, and every doc and action is one
 * click away. The drawer shape is proven by mobile.spec at 375px.
 */
test.describe('the docked rail', () => {
  test.use({ viewport: { width: 1280, height: 800 } });

  /**
   * Nothing in the rail is cut off by its own width.
   *
   * A screenshot of the records list showed a third of it arriving as "008
   * ADR: Linux over Wind..." and "010 ADR: Observability (A...", which no test
   * could see because every one of them asks for a row by its full accessible
   * name and gets it: the name is complete, the pixels are not. So this asks
   * the browser instead, for every span in the rail, whether the text is wider
   * than the box it was given.
   */
  test('no row in the rail is cut off by its own width', async ({ page }) => {
    await openTheYard(page);
    const rail = page.getByTestId('side-rail');
    await rail.getByText('Decision Records', { exact: true }).click();
    await expect(rail.getByRole('button', { name: 'ADR: Front Door origin' })).toBeVisible();

    const clipped = await rail.evaluate((root) =>
      Array.from(root.querySelectorAll<HTMLElement>('span'))
        .filter(
          (element) =>
            element.scrollWidth > element.clientWidth + 1 &&
            getComputedStyle(element).overflow !== 'visible'
        )
        .map((element) => element.textContent?.trim() ?? '')
    );

    expect(clipped).toEqual([]);
  });

  test('a laptop gets the rail, no hamburger, and no dropdowns', async ({ page }) => {
    await openTheYard(page);
    const rail = page.getByTestId('side-rail');
    await expect(rail).toBeVisible();
    await expect(page.getByRole('button', { name: 'Menu' })).toBeHidden();
    await expect(page.getByRole('button', { name: 'Hosting', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'About', exact: true })).toHaveCount(0);
    for (const section of [
      'App Architecture',
      'Hosting',
      'CI/CD',
      'Best Practices',
      'Changelog',
      'About',
    ]) {
      await expect(rail.getByRole('heading', { name: section, exact: true })).toBeVisible();
    }
    const box = await rail.boundingBox();
    expect(box?.x).toBe(0);
    expect(box?.width).toBeGreaterThanOrEqual(240);
    await expect(rail.getByRole('link', { name: 'GitHub repository' })).toHaveAttribute(
      'href',
      'https://github.com/SteveStout/TheYard'
    );
    await expect(page.getByTestId('build-version')).toBeVisible();
  });

  test('the rail collapses to icons, keeps its names, and remembers the choice', async ({
    page,
  }) => {
    await openTheYard(page);
    const rail = page.getByTestId('side-rail');
    await rail.getByRole('button', { name: 'Collapse the sidebar' }).click();
    await expect(rail).toHaveAttribute('data-collapsed', 'true');
    await expect.poll(async () => (await rail.boundingBox())?.width ?? 0).toBeLessThanOrEqual(80);
    // Labels leave the screen but not the accessibility tree.
    const hosting = rail.getByRole('button', { name: 'Hosting overview' });
    await expect(hosting).toBeVisible();
    await expect(hosting).toHaveAttribute('title', 'Hosting overview');

    await page.reload();
    await expect(page.getByTestId('side-rail')).toHaveAttribute('data-collapsed', 'true');
    await page.getByTestId('side-rail').getByRole('button', { name: 'Expand the sidebar' }).click();
    await expect(page.getByTestId('side-rail')).toHaveAttribute('data-collapsed', 'false');
    await expect
      .poll(async () => (await page.getByTestId('side-rail').boundingBox())?.width ?? 0)
      .toBeGreaterThanOrEqual(240);
  });

  test('a doc opens from the rail and its row reads as current while it is open', async ({
    page,
  }) => {
    await openTheYard(page);
    const rail = page.getByTestId('side-rail');
    await rail.getByRole('button', { name: 'Hosting overview' }).click();
    const doc = page.getByRole('dialog', { name: 'Hosting' });
    await expect(doc.getByRole('heading', { level: 1, name: 'Hosting' })).toBeVisible();
    // "page" rather than "true" since ADR-026: aria-current takes a token
    // saying what kind of current thing this is, and a document row is a page.
    await expect(page.locator('[data-testid="side-rail"] [aria-current="page"]')).toHaveText(
      'Hosting overview'
    );
    await page.keyboard.press('Escape');
    await expect(doc).toBeHidden();
    await expect(page.locator('[data-testid="side-rail"] [aria-current]')).toHaveCount(0);
  });

  test('Admin opens from the rail, reads as the current page, and the brand goes home', async ({
    page,
  }) => {
    await openTheYard(page);
    const rail = page.getByTestId('side-rail');
    await rail.getByRole('button', { name: 'Admin', exact: true }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
    await expect(page).toHaveURL(/view=admin/);
    await expect(page.locator('[data-testid="side-rail"] [aria-current="page"]')).toHaveText(
      'Admin'
    );
    await rail.getByRole('button', { name: 'The Yard' }).click();
    await expect(page.getByRole('heading', { name: 'Inventory' })).toBeVisible();
    await expect(page).not.toHaveURL(/view=admin/);
  });
});

test.describe('just under the docking line', () => {
  test.use({ viewport: { width: 1023, height: 800 } });

  test('1023px gets the header and the hamburger, not the rail', async ({ page }) => {
    await openTheYard(page);
    await expect(page.getByTestId('side-rail')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Menu' })).toBeVisible();
    await page.getByRole('button', { name: 'Menu' }).click();
    await expect(page.getByRole('dialog', { name: 'Menu' })).toBeVisible();
  });
});

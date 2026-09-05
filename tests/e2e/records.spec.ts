import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

/**
 * A record has an address (ADR: A record with no address).
 *
 * The decision records are the centre of this project and until now none of
 * them could be linked to: the only way to reach one was to open the site,
 * expand a group and scroll. A document is a view, and every other view here is
 * a GET parameter, so this checks that one is too, in both directions.
 */
test.describe('a record has an address', () => {
  test.use({ viewport: { width: 1280, height: 800 } });

  test('a link opens the record it names', async ({ page }) => {
    await openTheYard(page, '/?doc=adr-changelog');

    await expect(
      page.getByRole('dialog').getByRole('heading', { level: 1, name: 'ADR: The changelog' })
    ).toBeVisible();
  });

  test('opening one from the rail puts it in the address bar, and Back closes it', async ({
    page,
  }) => {
    await openTheYard(page);
    const rail = page.getByTestId('side-rail');
    await rail.getByText('Decision Records', { exact: true }).click();
    await rail.getByRole('button', { name: 'ADR: The changelog' }).click();

    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(page).toHaveURL(/[?&]doc=adr-changelog/);

    // Pushed, not replaced, so the browser's Back button closes the record the
    // way it closes a vehicle.
    await page.goBack();
    await expect(dialog).toBeHidden();
    await expect(page).not.toHaveURL(/[?&]doc=/);
  });

  test('closing it with the keyboard takes the address with it', async ({ page }) => {
    await openTheYard(page, '/?doc=adr-changelog');
    await expect(page.getByRole('dialog')).toBeVisible();

    await page.keyboard.press('Escape');

    await expect(page.getByRole('dialog')).toBeHidden();
    await expect(page).not.toHaveURL(/[?&]doc=/);
  });

  test('the record offers its own link, because nothing else says it has one', async ({
    page,
    context,
  }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await openTheYard(page, '/?doc=adr-changelog');
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();

    await dialog.getByRole('button', { name: 'Copy link' }).click();

    // The label is the whole state. A feature nobody can find is a feature
    // nobody has, and a record's address is only useful if the page admits it
    // exists.
    await expect(dialog.getByRole('button', { name: 'Link copied' })).toBeVisible();
    const copied = await page.evaluate(() => navigator.clipboard.readText());
    expect(copied).toContain('doc=adr-changelog');
  });

  test('an address that names no record opens nothing and breaks nothing', async ({ page }) => {
    await openTheYard(page, '/?doc=not-a-record');

    await expect(page.getByRole('dialog')).toBeHidden();
    await expect(page.getByRole('heading', { level: 1, name: 'Inventory' })).toBeVisible();
  });
});

import { expect, test } from '@playwright/test';
import { openTheYard, openSection } from './app';

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
    await openSection(rail, 'Decision Records');
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
    // No inventory parameter, so the landing page, as a bare address.
    await expect(
      page.getByRole('heading', {
        level: 1,
        name: 'Built with AI. Gated so quality never slides.',
      })
    ).toBeVisible();
  });
});

/**
 * The index marks which records a test holds (ADR-029, the addendum on the
 * marks): a filled dot where a rule cites the record, a ring where review
 * holds it. They arrive when the index is opened, each row keeps its name, and
 * no mark pushes a row past the rail, at the docked width or in the drawer.
 */
test.describe('the index marks which records a test holds', () => {
  for (const width of [1024, 1280, 1920]) {
    test(`on the docked rail at ${width} px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await openTheYard(page);
      const rail = page.getByTestId('side-rail');
      const asked = page.waitForResponse((response) =>
        response.url().endsWith('/api/docs/enforced')
      );
      await openSection(rail, 'Decision Records');
      expect((await asked).ok()).toBe(true);
      await expectTheMarks(rail);
    });
  }

  for (const width of [360, 390, 768]) {
    test(`in the drawer at ${width} px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await openTheYard(page);
      await page.getByRole('button', { name: 'Menu' }).click();
      const drawer = page.getByRole('dialog', { name: 'Menu' });
      await openSection(drawer, 'Decision Records');
      await expectTheMarks(drawer);
    });
  }
});

/** Every record row carries one mark inside its own box, the key counts them all, and a row is named by its label. */
async function expectTheMarks(scope: import('@playwright/test').Locator): Promise<void> {
  const key = scope.getByTestId('records-held-key');
  await expect(key).toBeVisible();
  // A has locator is read inside each details, so it is rooted at the page, not at the rail.
  const section = scope
    .locator('details')
    .filter({ has: scope.page().getByTestId('records-held-key') });
  const rows = section.getByRole('button');
  const marks = section.locator('button [data-held]');
  await expect(marks.first()).toBeVisible();
  expect(await marks.count()).toBe(await rows.count());

  const counts = (await key.innerText()).match(/(\d+) held by a test\s+(\d+) by review/);
  expect(counts).not.toBeNull();
  expect(Number(counts![1]) + Number(counts![2])).toBe(await rows.count());

  // The mark's words describe the row; its name is still the label a reader hears.
  const changelog = section.getByRole('button', { name: 'ADR: The changelog' });
  await expect(changelog).toHaveAccessibleDescription(/^Held by (review|a test|tests)/);
  expect(await changelog.getAttribute('aria-describedby')).toBeTruthy();

  const outside = await section.evaluate(
    (node) =>
      [...node.querySelectorAll('button')].filter((row) => {
        const mark = row.querySelector('[data-held]');
        if (!mark) return false;
        const box = row.getBoundingClientRect();
        const dot = mark.getBoundingClientRect();
        return (
          dot.right > box.right + 0.5 ||
          dot.left < box.left ||
          row.scrollWidth > row.clientWidth + 1
        );
      }).length
  );
  expect(outside).toBe(0);
  const scroller = await section.evaluate((node) => {
    const scroll = node.closest('nav')?.querySelector('div') as HTMLElement;
    return scroll.scrollWidth - scroll.clientWidth;
  });
  expect(scroller).toBeLessThanOrEqual(0);
}

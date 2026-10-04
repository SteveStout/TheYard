import { expect, test } from '@playwright/test';
import { openTheYard } from './app';

// The onion practices page is meant to be linked a section at a time, from a post or a comment,
// so a shared address such as ?doc=onion#the-build-holds-the-rings has to open the page and land on
// that section. The ids come from the heading words (markdown.ts), and the section is scrolled to
// once the document is drawn (useLiveBlocks.tsx); this checks both on a cold load, the way a link
// from outside the site arrives.
test('a link to a section of the onion practices page opens the page on that section', async ({
  page,
}) => {
  await openTheYard(page, '/?doc=onion#the-build-holds-the-rings');
  const onion = page.getByRole('dialog', { name: 'Onion architecture, the practices' });
  await expect(
    onion.getByRole('heading', { level: 1, name: 'Onion architecture, the practices' })
  ).toBeVisible();

  // Every section a link can name has its id, so each address in a post has somewhere to land.
  for (const id of [
    'in-plain-words',
    'the-rule-in-one-sentence',
    'a-request-walks-the-rings',
    'the-inner-ring-owns-the-interface',
    'the-rules-take-the-clock-as-a-value',
    'the-build-holds-the-rings',
    'one-project-rings-as-folders',
    'when-not-to-build-it-this-way',
    'references',
  ]) {
    await expect(onion.locator(`h2[id="${id}"]`)).toHaveCount(1);
  }
  // The one the address named is on screen, and the page's first section is not.
  await expect(onion.locator('#the-build-holds-the-rings')).toBeInViewport();
  await expect(onion.locator('#in-plain-words')).not.toBeInViewport();

  // Every whole file on the page is read from this build, the sample's tests included, and each
  // drawing links to a page of its own.
  await expect(onion.locator('em').filter({ hasText: 'Sample unavailable' })).toHaveCount(0);
  await expect(
    onion
      .locator('pre code')
      .filter({ hasText: 'Every_type_sits_in_one_of_the_folders_the_rings_name' })
  ).toHaveCount(1);
  for (const drawing of ['rings', 'bid-walk', 'port-adapter']) {
    await expect(onion.locator(`a[href$="/api/docs/diagrams/${drawing}"]`)).not.toHaveCount(0);
  }
});

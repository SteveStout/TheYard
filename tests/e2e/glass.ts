import type { Page } from '@playwright/test';

/**
 * What the glass look promises, as two questions asked of the rendered page
 * (ADR: The glass look). glass.spec.ts holds both; they live here so that a
 * look at the page can ask them too.
 */

/** Every element drawn at less than full strength that holds a word or an image. */
export async function fadedContent(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const found: string[] = [];
    for (const element of Array.from(document.querySelectorAll<HTMLElement>('body *'))) {
      const style = getComputedStyle(element);
      if (Number(style.opacity) >= 1 || style.display === 'none') continue;
      const words = (element.textContent ?? '').trim().length > 0;
      const pictures = element.matches('img, picture') || element.querySelector('img') !== null;
      if (words || pictures) {
        found.push(
          `${element.tagName.toLowerCase()}.${element.className} at opacity ${style.opacity}`
        );
      }
    }
    return found;
  });
}

/**
 * Quiet words on the bare page. The muted and faint colours clear AA on the
 * page ground with little to spare, and the watermark's darkest stroke takes
 * that away, so they are only ever used inside a panel, where the panel's
 * white is between the word and the drawing. Returned: each word in a quiet
 * colour with no ground of its own anywhere above it.
 */
export async function quietWordsOnTheBareGround(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const probe = document.createElement('span');
    document.body.append(probe);
    const quiet = new Set(
      ['--color-text-muted', '--color-text-faint'].map((token) => {
        probe.style.color = `var(${token})`;
        return getComputedStyle(probe).color;
      })
    );
    probe.remove();
    const found: string[] = [];
    for (const element of Array.from(document.querySelectorAll<HTMLElement>('main *, footer *'))) {
      const own = Array.from(element.childNodes).some(
        (node) => node.nodeType === Node.TEXT_NODE && (node.textContent ?? '').trim().length > 0
      );
      if (!own || element.offsetWidth <= 1 || element.closest('dialog') !== null) continue;
      if (!quiet.has(getComputedStyle(element).color)) continue;
      let above: HTMLElement | null = element;
      let grounded = false;
      while (above !== null && above !== document.body) {
        const ground = getComputedStyle(above).backgroundColor;
        if (ground !== 'rgba(0, 0, 0, 0)' && ground !== 'transparent') {
          grounded = true;
          break;
        }
        above = above.parentElement;
      }
      if (!grounded) {
        found.push(
          `${element.tagName.toLowerCase()}.${element.className}: ${(element.textContent ?? '').trim().slice(0, 60)}`
        );
      }
    }
    return found;
  });
}

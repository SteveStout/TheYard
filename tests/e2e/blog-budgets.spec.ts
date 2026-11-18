import { readFileSync } from 'node:fs';
import { brotliCompressSync, constants } from 'node:zlib';
import { expect, test, type Page } from '@playwright/test';
import { openTheYard } from './app';
import { answerBlog, made } from './blogFake';

// #region blog-budgets
/**
 * The blog's budgets on a cold load at a phone's size (ADR: Every LinkedIn post is also a blog post, the addendum on
 * the read model): the blog route's JavaScript at most 150 KB in brotli and none of the Markdown or Admin chunks,
 * the first screen at most 300 KB, the largest paint the post's title or its cover slide asked for first, the layout
 * moving less than 0.01, no long task over 50 ms while a search is typed, and the viewer's slide on screen within
 * 200 ms of the click. Bytes are counted as brotli would send them, read off the files themselves, so the count is the
 * same on any server; the list's own answer is held to 6 KB by BlogApiTests. The first screen's bytes and the tasks
 * while typing are the built site's (YARD_WEB_PREVIEW, the gate): the dev server sends every module unminified and
 * React's development build, so there those two end at their first line, as csp.spec.ts does without the renderer.
 */
const POSTS = [
  made(
    'the-newest-post-with-slides',
    'The newest post, with its slides and a title long enough to wrap',
    1,
    { slides: 11, keywords: ['copilot'] }
  ),
  ...Array.from({ length: 11 }, (_, n) =>
    made(`an-older-post-${n + 1}`, `An older post ${n + 1}`, 3 + n)
  ),
];

const KB = 1024;

/** The bytes brotli sends for a body, at the quality the edge uses for files that do not change. */
const brotli = (body: Buffer) =>
  brotliCompressSync(body, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }).length;

/** A full-size slide's bytes as the live ones weigh (about 140 KB), so a first screen that asked for one would show it. */
const FULL_SLIDE = Buffer.concat([
  readFileSync('api/TheYard.Api/wwwroot/images/badges/steve-176.jpg'),
  Buffer.alloc(140 * KB),
]);

async function coldPhone(page: Page) {
  await page.setViewportSize({ width: 390, height: 844 });
  await answerBlog(page, POSTS);
  await page.route(/steve-176\.jpg\?full=/, (route) =>
    route.fulfill({ status: 200, contentType: 'image/jpeg', body: FULL_SLIDE })
  );
  await page.addInitScript(() => {
    const w = window as unknown as {
      shift: number;
      lcp: Element | null;
      longTasks: number[];
      moved: string[];
    };
    w.shift = 0;
    w.moved = [];
    w.lcp = null;
    w.longTasks = [];
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries() as unknown as {
        value: number;
        hadRecentInput: boolean;
        sources?: { node?: Node | null }[];
      }[]) {
        if (entry.hadRecentInput) continue;
        w.shift += entry.value;
        // What moved, kept so a failure names it rather than a number alone.
        for (const source of entry.sources ?? []) {
          const node = source.node instanceof Element ? source.node : source.node?.parentElement;
          w.moved.push(
            `${entry.value.toFixed(4)} ${node?.tagName ?? '?'}.${node?.className ?? ''}`
          );
        }
      }
    }).observe({ type: 'layout-shift', buffered: true });
    new PerformanceObserver((list) => {
      const entries = list.getEntries() as unknown as { element: Element | null }[];
      w.lcp = entries.at(-1)?.element ?? w.lcp;
    }).observe({ type: 'largest-contentful-paint', buffered: true });
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) w.longTasks.push(entry.duration);
    }).observe({ type: 'longtask', buffered: true });
  });
}

test('the blog route asks for at most 150 KB of JavaScript in brotli, and no Markdown or Admin chunk', async ({
  page,
  request,
}) => {
  await coldPhone(page);
  await openTheYard(page, '/?view=blog');
  await expect(page.getByTestId('blog-post')).toBeVisible();
  // Idle, as long as the site waits before it fetches ahead: the blog is promised none of it.
  await page.waitForTimeout(3_000);
  const scripts = await page.evaluate(() =>
    performance
      .getEntriesByType('resource')
      .filter((entry) => /\.js(\?|$)/.test(entry.name))
      .map((entry) => entry.name)
  );
  expect(scripts.filter((url) => /\/assets\/(markdown|AdminPanel)-/.test(url))).toEqual([]);
  let total = 0;
  for (const url of scripts.filter((each) => each.includes('/assets/')))
    total += brotli(await (await request.get(url)).body());
  expect(
    total,
    `the blog route's scripts are ${Math.round(total / KB)} KB in brotli`
  ).toBeLessThanOrEqual(150 * KB);
});

test('the first screen costs at most 300 KB, its largest paint is the title or the cover, and nothing moves', async ({
  page,
  request,
}) => {
  if (!process.env.YARD_WEB_PREVIEW) return;
  await coldPhone(page);
  await openTheYard(page, '/?view=blog');
  await expect(page.getByTestId('blog-post')).toBeVisible();
  await page.waitForTimeout(1_500);
  const { resources, onScreen } = await page.evaluate(() => ({
    resources: performance.getEntriesByType('resource').map((entry) => {
      const timing = entry as PerformanceResourceTiming;
      return { url: timing.name, kind: timing.initiatorType, size: timing.transferSize };
    }),
    // The pictures the first screen is made of: every eager one, and every lazy one inside the window. A lazy picture
    // outside it (further down the list, or along the strip's sideways scroll) is not the first screen's, though
    // the browser may fetch it ahead.
    onScreen: [...document.querySelectorAll('img')]
      .filter((img) => {
        const box = img.getBoundingClientRect();
        const inside =
          box.bottom > 0 && box.right > 0 && box.top < innerHeight && box.left < innerWidth;
        return img.loading !== 'lazy' || inside;
      })
      .map((img) => img.currentSrc),
  }));
  let total = brotli(Buffer.from(await page.content()));
  for (const resource of resources) {
    if (resource.kind === 'img') total += onScreen.includes(resource.url) ? resource.size : 0;
    else if (resource.url.includes('/api/')) total += resource.size;
    else if (/\.(js|css|woff2)(\?|$)/.test(resource.url))
      total += /woff2/.test(resource.url)
        ? (await (await request.get(resource.url)).body()).length
        : brotli(await (await request.get(resource.url)).body());
  }
  expect(total, `the first screen is ${Math.round(total / KB)} KB`).toBeLessThanOrEqual(300 * KB);
  const largest = await page.evaluate(() => {
    const element = (window as unknown as { lcp: Element | null }).lcp;
    return element
      ? {
          tag: element.tagName,
          title: element.closest('[data-testid="blog-post"] h2') !== null,
          priority: element.getAttribute('fetchpriority'),
        }
      : null;
  });
  expect(
    largest !== null && (largest.title || (largest.tag === 'IMG' && largest.priority === 'high')),
    JSON.stringify(largest)
  ).toBe(true);
  const { shift, moved } = await page.evaluate(() => {
    const w = window as unknown as { shift: number; moved: string[] };
    return { shift: w.shift, moved: w.moved };
  });
  expect(shift, `moved: ${moved.join('; ')}`).toBeLessThan(0.01);
});

test('typing a search runs no task over 50 ms', async ({ page }) => {
  if (!process.env.YARD_WEB_PREVIEW) return;
  await coldPhone(page);
  await openTheYard(page, '/?view=blog');
  await expect(page.getByTestId('blog-post')).toBeVisible();
  await page.waitForTimeout(1_500);
  await page.evaluate(() => ((window as unknown as { longTasks: number[] }).longTasks = []));
  await page.getByTestId('blog-search').pressSequentially('copilot', { delay: 60 });
  await expect(page.getByTestId('blog-count')).toHaveText(/^Showing 1 of/);
  await page.waitForTimeout(500);
  const tasks = await page.evaluate(() => (window as unknown as { longTasks: number[] }).longTasks);
  expect(
    tasks.filter((duration) => duration > 50),
    `long tasks while typing: ${tasks.join(', ')}`
  ).toEqual([]);
});

test('the viewer has its slide on screen within 200 ms of the click', async ({ page }) => {
  await coldPhone(page);
  await openTheYard(page, '/?view=blog&post=the-newest-post-with-slides');
  const thumb = page
    .getByTestId('blog-slides-strip')
    .getByRole('button', { name: /^Open slide 3 of 11/ });
  await thumb.scrollIntoViewIfNeeded();
  await page.evaluate(() => {
    const w = window as unknown as { clicked: number; painted: number };
    document.addEventListener('click', () => (w.clicked = performance.now()), {
      capture: true,
      once: true,
    });
    let seen = false;
    const check = () => {
      // The selected slide's own full picture, decoded: not the thumbnail drawn under it, not a broken image.
      const shown = document.querySelector(
        '[data-testid="slide-viewer"][open] [data-testid="slide-image"]'
      );
      if (
        !seen &&
        shown instanceof HTMLImageElement &&
        shown.src.includes('full=the-newest-post-with-slides-3') &&
        shown.complete &&
        shown.naturalWidth > 0
      ) {
        seen = true;
        requestAnimationFrame(() => (w.painted = performance.now()));
        watch.disconnect();
      }
    };
    const watch = new MutationObserver(check);
    // A picture's load changes nothing in the tree, so its load is watched too (load does not bubble; capture sees it).
    document.addEventListener('load', check, true);
    watch.observe(document.body, { subtree: true, childList: true, attributes: true });
  });
  await thumb.click();
  await expect(page.getByTestId('slide-viewer')).toBeVisible();
  const elapsed = await page.waitForFunction(() => {
    const w = window as unknown as { clicked?: number; painted?: number };
    return w.painted !== undefined && w.clicked !== undefined ? w.painted - w.clicked : null;
  });
  expect(await elapsed.jsonValue()).toBeLessThanOrEqual(200);
});
// #endregion blog-budgets

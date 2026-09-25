import type { APIRequestContext } from '@playwright/test';
import { CARD_SLUGS } from '../../src/lib/workbench';

/**
 * Every page the site lists for itself, and what each one is read for (ADR: The
 * glass look, the addendum on every page the same). Steve, 2026-09-24: "I want
 * every page to be styled the same no missed pages". The list is the site's own,
 * never a hand-written one, so a new document or card is covered by the next run
 * without anybody adding it: every view the app routes, the Admin tab's cards by
 * their slugs, the first three vehicles of the listing, every document the page
 * sweep serves, and every HTML address the sweep holds.
 */
export type SitePage = { key: string; address: string };

export async function siteList(request: APIRequestContext): Promise<SitePage[]> {
  const pages: SitePage[] = [
    { key: 'landing', address: '/' },
    { key: 'inventory', address: '/?view=inventory' },
    { key: 'account', address: '/?view=account' },
    ...CARD_SLUGS.map((slug) => ({ key: `admin-${slug}`, address: `/?view=admin&card=${slug}` })),
  ];
  const listing = (await (
    await request.get('http://localhost:5210/api/vehicles?limit=3')
  ).json()) as {
    vehicles: { id: number | string }[];
  };
  for (const vehicle of listing.vehicles.slice(0, 3)) {
    pages.push({ key: `vehicle-${vehicle.id}`, address: `/?vehicle=${vehicle.id}` });
  }
  // The sweep runs when the container starts; on a cold process it may still be running.
  type Sweep = {
    report: { entries: { address: string; kind: string; content_type: string | null }[] } | null;
  };
  let sweep: Sweep = { report: null };
  for (let attempt = 0; attempt < 60 && !sweep.report; attempt += 1) {
    sweep = (await (await request.get('http://localhost:5210/api/admin/pages')).json()) as Sweep;
    if (!sweep.report) await new Promise((resolve) => setTimeout(resolve, 2000));
  }
  for (const entry of sweep.report?.entries ?? []) {
    const doc = /^\/api\/docs\/([a-z0-9-]+)$/.exec(entry.address);
    if (doc && entry.kind === 'document') {
      pages.push({ key: `doc-${doc[1]}`, address: `/?doc=${doc[1]}` });
    } else if ((entry.content_type ?? '').startsWith('text/html') && entry.address !== '/') {
      pages.push({
        key: `html${entry.address.replace(/[^a-z0-9]+/gi, '-')}`,
        address: entry.address,
      });
    }
  }
  return pages;
}

export type PageFacts = {
  font: string;
  panels: number;
  unstyled: string[];
  square: string[];
  wrongFace: string[];
  /** Ids used more than once on the page. */
  twice: string[];
  /** References inside a drawing that find nothing, another drawing's element, or a hidden one. */
  lost: string[];
  /** Table cells that break a word anywhere (the tweaks pass, A1: "Resou rce" on a phone). */
  anywhere: string[];
};

/**
 * What one page is read for, in the browser. Kept in step with the lane's own
 * reader (staging\operatorlane\coverage.cjs): the same facts, read the same way.
 *
 * A panel is a box the page draws: an element whose own class is a panel's name
 * (a module's class is written _name_hash, so the name is matched whole: cardTitle
 * is not a card), an article, a section, or an open dialog, which draws a ground,
 * a border or a shadow of its own and is at least 120 by 40. It wears the look
 * when it carries the brackets (::before) and the 3 px rule on its top edge.
 * Not panels, each for its reason: the frame (the header, the site's rail, the
 * store band, the ribbon ground, the watermark and the footer are the page, not
 * boxes on it: Steve, "leave the header and page background the same"), code
 * (a sample is a listing, not a panel), a form control, a table, a picture, and
 * anything round as a pill (a pill is a control).
 */
export function readPage(): PageFacts {
  const visible = (e: Element) => {
    const r = e.getBoundingClientRect();
    const s = getComputedStyle(e);
    return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none';
  };
  const frame = (e: Element) =>
    e.closest(
      '[data-frame], [data-testid="side-rail"], [data-testid="store-bar"], [data-testid^="ribbons"], [data-testid="watermark"]'
    );
  // Code is a listing, not a panel and not a reading: pre, code, and a block made to hold one.
  const inCode = (e: Element) => e.closest('pre, code, [class*="code-block"]');
  // Not panels, by name: a colour swatch on the style page is a sample of a token drawn in the token itself.
  const sample = (e: Element) => e.closest('.swatches');
  // A module's class is written _name_hash and a global one name-name, so the name is matched whole after either.
  const PANEL =
    /(^|_|-)(panel|card|wide|tile|big|hero|proofItem|strip|bar|block|rail|dialog|op-glass)(_[A-Za-z0-9]+)?$/;
  const alpha = (color: string) => {
    if (color === 'transparent') return 0;
    const parts = (color.match(/rgba?\(([^)]+)\)/)?.[1] ?? '0,0,0,1').split(',');
    return parts.length > 3 ? Number(parts[3]) : 1;
  };
  // A box is a ground, a shadow, or a border on two sides or more: a hairline over a section is a divider, not a box.
  const draws = (e: Element) => {
    const s = getComputedStyle(e);
    const sides = (['Top', 'Right', 'Bottom', 'Left'] as const).filter(
      (side) =>
        parseFloat(s.getPropertyValue(`border-${side.toLowerCase()}-width`)) > 0 &&
        s.getPropertyValue(`border-${side.toLowerCase()}-style`) !== 'none'
    ).length;
    return (
      alpha(s.backgroundColor) > 0.02 ||
      s.backgroundImage !== 'none' ||
      s.boxShadow !== 'none' ||
      sides >= 2
    );
  };
  const pill = (e: Element) => {
    const radius = getComputedStyle(e).borderRadius;
    return radius === '50%' || (radius.endsWith('px') && parseFloat(radius) >= 999);
  };
  const name = (e: Element) => {
    const cls = e.getAttribute('class') ?? '';
    return `${e.tagName.toLowerCase()}.${cls.split(' ')[0]}`;
  };
  const panels = Array.from(document.querySelectorAll('[class], article, section, dialog[open]'))
    .filter(
      (e) =>
        Array.from(e.classList).some((c) => PANEL.test(c)) ||
        e.matches('article, section, dialog[open]')
    )
    .filter(visible)
    .filter((e) => !frame(e) && !inCode(e) && !sample(e) && draws(e) && !pill(e))
    .filter((e) => !e.matches('input, select, textarea, table, td, th, img, svg, picture'))
    .filter((e) => {
      const r = e.getBoundingClientRect();
      return r.width >= 120 && r.height >= 40;
    });
  const unstyled = panels.filter(
    (e) =>
      getComputedStyle(e, '::before').content === 'none' ||
      !getComputedStyle(e).borderTopWidth.startsWith('3px')
  );
  // A tile that is a button is a panel (it wears the glass), and a button that draws no box is a link in words.
  const square = Array.from(
    document.querySelectorAll('button, a[class*="button"], a[class*="pill"], [role="button"]')
  )
    .filter(visible)
    .filter((e) => !frame(e) && !e.classList.contains('op-glass') && draws(e) && !pill(e));
  const wrong = Array.from(document.querySelectorAll('h1, h2, h3, p, li, dt, dd, button, a, span'))
    .filter(visible)
    .filter((e) => !inCode(e) && !getComputedStyle(e).fontFamily.includes('IBM Plex Sans'));
  // The fault that left the ribbons blank in Chrome (1.0.3.20): two copies of a drawing gave their
  // gradients one set of names, a reference found the first, and that was inside a closed dialog.
  // So an id is used once, and a reference inside a drawing finds an element of its own drawing
  // that is drawn whenever the reference is.
  // The API reference's client is Scalar's markup, not the site's, and it names its own parts
  // twice (read on the live page, 1400); every other id on every page is the site's.
  const count = new Map<string, number>();
  for (const e of Array.from(document.querySelectorAll('[id]'))) {
    if (e.closest('.scalar-app')) continue;
    count.set(e.id, (count.get(e.id) ?? 0) + 1);
  }
  const twice = Array.from(count)
    .filter(([, n]) => n > 1)
    .map(([id]) => id);
  const hidden = (e: Element) => {
    for (let n: Element | null = e; n; n = n.parentElement) {
      if (getComputedStyle(n).display === 'none') return true;
      if (n instanceof HTMLDialogElement && !n.open) return true;
    }
    return false;
  };
  const lost: string[] = [];
  const REFERENCES = ['fill', 'stroke', 'filter', 'clip-path', 'mask'];
  for (const e of Array.from(document.querySelectorAll('svg, svg *'))) {
    for (const attribute of REFERENCES) {
      const id = /url\(\s*['"]?#([^)'"]+)/.exec(e.getAttribute(attribute) ?? '')?.[1];
      if (id === undefined) continue;
      const target = document.getElementById(id);
      if (
        target === null ||
        target.closest('svg') !== e.closest('svg') ||
        (hidden(target) && !hidden(e))
      ) {
        lost.push(`${name(e)} ${attribute} #${id}`);
      }
    }
  }
  // A table cell wraps between words or not at all (the tweaks pass, A1): a cell that
  // computes overflow-wrap: anywhere broke "Resource" into "Resou rce" on a phone.
  const anywhere = Array.from(document.querySelectorAll('td, th'))
    .filter((e) => !e.closest('.scalar-app'))
    .filter((e) => {
      const wrap = getComputedStyle(e).overflowWrap;
      return wrap === 'anywhere' || getComputedStyle(e).wordBreak === 'break-all';
    });
  const unique = (list: Element[], label: (e: Element) => string) =>
    Array.from(new Set(list.map(label))).slice(0, 12);
  return {
    font: getComputedStyle(document.body).fontFamily,
    panels: panels.length,
    unstyled: unique(unstyled, name),
    square: unique(
      square,
      (e) =>
        `${name(e)} "${(e.getAttribute('aria-label') ?? e.textContent ?? '').trim().slice(0, 24)}"`
    ),
    wrongFace: unique(wrong, (e) => `${name(e)} ${getComputedStyle(e).fontFamily.split(',')[0]}`),
    twice: twice.slice(0, 12),
    lost: Array.from(new Set(lost)).slice(0, 12),
    anywhere: unique(anywhere, (e) => `${name(e)} "${(e.textContent ?? '').trim().slice(0, 24)}"`),
  };
}

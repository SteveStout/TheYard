/**
 * Does:      Brings a rendered document's live blocks to life: draws the ribbons into a ribbon strip and a site-map
 *            icon into each tile's badge, opens an in-library link ([data-doc]) in the same window, and scrolls to
 *            the section a link or the first address named.
 * Does not:  Render the markdown (markdown.ts and styleBlocks.ts do), fetch a document, or decide which one is open.
 * Used by:   DocDialog.tsx.
 */
import { useEffect, useRef, type RefObject } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Ribbons } from '../components/layout/Background/Ribbons';
import { NavGlyph } from '../components/shared/SheetIcons';
import type { NavIcon } from '../lib/siteMap';
import { DOCS, type DocKey } from './documents';

// #region live-blocks
/**
 * The section to land on once the next document is drawn. The first address's
 * hash is read before the address bar is rewritten without it, so a shared
 * link such as `?doc=color-style#the-glass` still lands on its section.
 */
let pendingAnchor =
  typeof window === 'undefined' ? '' : decodeURIComponent(window.location.hash.slice(1));

/** The document a slug names, by the address the API serves it at. */
function keyOf(slug: string): DocKey | undefined {
  return (Object.keys(DOCS) as DocKey[]).find((key) => DOCS[key].url === `/api/docs/${slug}`);
}

/** Every live block inside `container`, mounted while `html` is on screen and unmounted when it goes. */
export function useLiveBlocks(
  container: RefObject<HTMLElement | null>,
  html: string | undefined,
  onOpenDoc?: (key: DocKey) => void
) {
  // The newest callback, read at click time, so a parent that passes a new function each render does not remount the blocks.
  const opener = useRef(onOpenDoc);
  useEffect(() => {
    opener.current = onOpenDoc;
  });
  useEffect(() => {
    const root = container.current;
    if (!root || html === undefined) return;
    const roots: Root[] = [];
    // The ribbons, drawn by the same component as the page's ground, from the same data, with ids of their own.
    for (const box of root.querySelectorAll<HTMLElement>('[data-ribbon-strip]')) {
      const strip = createRoot(box);
      strip.render(<Ribbons />);
      roots.push(strip);
    }
    for (const badge of root.querySelectorAll<HTMLElement>('[data-glyph]')) {
      const glyph = createRoot(badge);
      glyph.render(<NavGlyph icon={badge.dataset.glyph as NavIcon} size={28} />);
      roots.push(glyph);
    }
    if (pendingAnchor !== '') {
      const target = root.querySelector<HTMLElement>(`[id="${CSS.escape(pendingAnchor)}"]`);
      if (target) {
        target.scrollIntoView({ block: 'start' });
        pendingAnchor = '';
      }
    }
    const open = (event: MouseEvent) => {
      const link = (event.target as Element | null)?.closest<HTMLAnchorElement>('a[data-doc]');
      const key = link ? keyOf(link.dataset.doc ?? '') : undefined;
      if (
        !link ||
        key === undefined ||
        !opener.current ||
        event.button !== 0 ||
        event.metaKey ||
        event.ctrlKey
      )
        return;
      event.preventDefault();
      const anchor = link.dataset.anchor ?? '';
      // A section of the document already open: land on it without opening the document again.
      const here =
        anchor === '' ? null : root.querySelector<HTMLElement>(`[id="${CSS.escape(anchor)}"]`);
      if (here) {
        here.scrollIntoView({ block: 'start' });
        return;
      }
      pendingAnchor = anchor;
      opener.current(key);
    };
    root.addEventListener('click', open);
    return () => {
      root.removeEventListener('click', open);
      // After this render, never inside it: React refuses to unmount a root while it is rendering.
      queueMicrotask(() => roots.forEach((each) => each.unmount()));
    };
  }, [container, html]);
}
// #endregion live-blocks

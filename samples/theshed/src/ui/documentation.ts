/**
 * The Docs tab: a list of the documents the app serves about itself, grouped by
 * section, beside the open document drawn from its markdown. The markdown is
 * parsed by lib/markdown.ts and turned into page elements by ui/elements.ts.
 *
 * The open document is part of the page address, as ?view=docs&doc=<slug>, so a
 * link to a document reopens that document. The list of documents is fetched
 * once and kept for as long as the page is open.
 * (More in docs/ADR-012-documents-served-by-the-app.md.)
 */

import * as api from '../lib/api.js';
import { parse } from '../lib/markdown.js';
import type { DocumentEntry } from '../lib/types.js';
import type { Navigate, State } from '../lib/urlState.js';
import type { View } from './browser.js';
import { buildElement, buildFromMarkdown, replaceContents } from './elements.js';
import { messageOf } from './notices.js';

/**
 * Builds the Docs tab inside a container element and returns an object whose
 * render() draws it for a given State.
 * @param root the element the Docs tab fills; it replaces everything inside it
 * @param navigate the function to call to change the State and the page address
 */
export function createDocumentation(root: HTMLElement, navigate: Navigate): View {
  let catalogue: DocumentEntry[] | null = null;
  let current = '';
  const nav = buildElement('nav', { 'aria-label': 'Documents' });
  const article = buildElement('article', { 'aria-live': 'polite' });
  replaceContents(root, nav, article);

  /** Fetches the list of documents once, then draws the list and the document the State names. */
  async function render(state: State): Promise<void> {
    if (!catalogue) {
      try {
        catalogue = await api.listDocuments();
      } catch (error) {
        replaceContents(article, buildElement('p', { class: 'notice error' }, messageOf(error)));
        return;
      }
    }
    const slug = state.doc || catalogue[0]?.slug || '';
    current = slug;
    renderNav(catalogue, slug);
    await renderDocument(slug);
  }

  /** Draws the list of documents under their group headings, marking the open one. */
  function renderNav(entries: DocumentEntry[], slug: string): void {
    const groups = new Map<string, DocumentEntry[]>();
    for (const entry of entries) {
      const group = groups.get(entry.group);
      if (group) {
        group.push(entry);
      } else {
        groups.set(entry.group, [entry]);
      }
    }
    const nodes: Node[] = [];
    for (const [group, members] of groups) {
      nodes.push(buildElement('h3', {}, group));
      for (const entry of members) {
        nodes.push(buildElement('button', {
          type: 'button',
          'aria-current': entry.slug === slug ? 'page' : false,
          onclick: () => navigate({ view: 'docs', doc: entry.slug }),
        }, entry.title));
      }
    }
    replaceContents(nav, ...nodes);
  }

  /** Fetches one document's markdown and draws it, unless another document was chosen meanwhile. */
  async function renderDocument(slug: string): Promise<void> {
    replaceContents(article, buildElement('p', {}, 'Loading'));
    try {
      const markdown = await api.fetchDocument(slug);
      if (slug !== current) {
        return; // Another document is now selected; its own render will draw it.
      }
      replaceContents(article, buildFromMarkdown(parse(markdown)));
      article.scrollTop = 0;
      // Move focus to the first heading, so a screen reader starts at the document title
      // rather than the top of the panel. tabindex -1 makes a heading focusable from code.
      const heading = article.querySelector<HTMLElement>('h1, h2');
      if (heading) {
        heading.setAttribute('tabindex', '-1');
        heading.focus();
      }
    } catch (error) {
      replaceContents(article, buildElement('p', { class: 'notice error' }, messageOf(error)));
    }
  }

  return { render };
}

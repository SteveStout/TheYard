// The Docs tab: the records the app serves about itself, listed by group and
// rendered from markdown with the reader in lib/markdown.js (ADR-012). Which
// document is open is in the URL as ?view=docs&doc=slug (ADR-005).

import * as api from '../lib/api.js';
import { parse } from '../lib/markdown.js';
import { h, replace, toDom } from './dom.js';

/**
 * @param {HTMLElement} root the element the docs view owns
 * @param {(partial: object) => void} navigate
 */
export function createDocs(root, navigate) {
  let catalogue = null;
  let state = null;
  const nav = h('nav', { 'aria-label': 'Documents' });
  const article = h('article', { 'aria-live': 'polite' });
  replace(root, nav, article);

  async function render(next) {
    state = next;
    if (!catalogue) {
      try {
        catalogue = await api.docs();
      } catch (error) {
        replace(article, h('p', { class: 'notice error' }, error.message));
        return;
      }
    }
    const slug = state.doc || catalogue[0].slug;
    renderNav(slug);
    await renderDoc(slug);
  }

  function renderNav(current) {
    const groups = new Map();
    for (const entry of catalogue) {
      if (!groups.has(entry.group)) {
        groups.set(entry.group, []);
      }
      groups.get(entry.group).push(entry);
    }
    const nodes = [];
    for (const [group, entries] of groups) {
      nodes.push(h('h3', {}, group));
      for (const entry of entries) {
        nodes.push(h('button', {
          type: 'button',
          'aria-current': entry.slug === current ? 'page' : false,
          onclick: () => navigate({ view: 'docs', doc: entry.slug }),
        }, entry.title));
      }
    }
    replace(nav, ...nodes);
  }

  async function renderDoc(slug) {
    replace(article, h('p', {}, 'Loading'));
    try {
      const markdown = await api.doc(slug);
      if (slug !== (state.doc || catalogue[0].slug)) {
        return; // Another document was chosen while this one loaded.
      }
      replace(article, toDom(parse(markdown)));
      article.scrollTop = 0;
      article.querySelector('h1, h2')?.focus?.();
    } catch (error) {
      replace(article, h('p', { class: 'notice error' }, error.message));
    }
  }

  return { render };
}

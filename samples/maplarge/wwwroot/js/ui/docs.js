// The Docs tab: the records the app serves about itself, listed by group and
// rendered from markdown with the reader in lib/markdown.ts (ADR-012). Which
// document is open is in the URL as ?view=docs&doc=slug (ADR-005).
import * as api from '../lib/api.js';
import { parse } from '../lib/markdown.js';
import { h, replace, toDom } from './dom.js';
function messageOf(error) {
    return error instanceof Error ? error.message : String(error);
}
/**
 * @param root the element the docs view owns
 * @param navigate pushes a state change
 */
export function createDocs(root, navigate) {
    let catalogue = null;
    let current = '';
    const nav = h('nav', { 'aria-label': 'Documents' });
    const article = h('article', { 'aria-live': 'polite' });
    replace(root, nav, article);
    async function render(state) {
        if (!catalogue) {
            try {
                catalogue = await api.docs();
            }
            catch (error) {
                replace(article, h('p', { class: 'notice error' }, messageOf(error)));
                return;
            }
        }
        const slug = state.doc || catalogue[0]?.slug || '';
        current = slug;
        renderNav(catalogue, slug);
        await renderDoc(slug);
    }
    function renderNav(entries, slug) {
        const groups = new Map();
        for (const entry of entries) {
            const group = groups.get(entry.group);
            if (group) {
                group.push(entry);
            }
            else {
                groups.set(entry.group, [entry]);
            }
        }
        const nodes = [];
        for (const [group, members] of groups) {
            nodes.push(h('h3', {}, group));
            for (const entry of members) {
                nodes.push(h('button', {
                    type: 'button',
                    'aria-current': entry.slug === slug ? 'page' : false,
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
            if (slug !== current) {
                return; // Another document was chosen while this one loaded.
            }
            replace(article, toDom(parse(markdown)));
            article.scrollTop = 0;
            // The heading takes focus so a screen reader lands on the title, not the top of the panel.
            const heading = article.querySelector('h1, h2');
            if (heading) {
                heading.setAttribute('tabindex', '-1');
                heading.focus();
            }
        }
        catch (error) {
            replace(article, h('p', { class: 'notice error' }, messageOf(error)));
        }
    }
    return { render };
}

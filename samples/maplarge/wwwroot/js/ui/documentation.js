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
import { buildElement, buildFromMarkdown, replaceContents } from './elements.js';
function messageOf(error) {
    return error instanceof Error ? error.message : String(error);
}
/**
 * Builds the Docs tab inside a container element and returns an object whose
 * render() draws it for a given State.
 * @param root the element the Docs tab fills; it replaces everything inside it
 * @param navigate the function to call to change the State and the page address
 */
export function createDocumentation(root, navigate) {
    let catalogue = null;
    let current = '';
    const nav = buildElement('nav', { 'aria-label': 'Documents' });
    const article = buildElement('article', { 'aria-live': 'polite' });
    replaceContents(root, nav, article);
    async function render(state) {
        if (!catalogue) {
            try {
                catalogue = await api.listDocuments();
            }
            catch (error) {
                replaceContents(article, buildElement('p', { class: 'notice error' }, messageOf(error)));
                return;
            }
        }
        const slug = state.doc || catalogue[0]?.slug || '';
        current = slug;
        renderNav(catalogue, slug);
        await renderDocument(slug);
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
    async function renderDocument(slug) {
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
            const heading = article.querySelector('h1, h2');
            if (heading) {
                heading.setAttribute('tabindex', '-1');
                heading.focus();
            }
        }
        catch (error) {
            replaceContents(article, buildElement('p', { class: 'notice error' }, messageOf(error)));
        }
    }
    return { render };
}

/**
 * Helpers that build DOM elements. h() builds the elements the page code
 * describes, and toDom() builds elements from the tree the markdown parser in
 * lib/markdown.ts produces.
 *
 * Both create elements with createElement and put text in text nodes. Neither
 * ever assigns an HTML string through innerHTML, so text from a file name or a
 * document is always shown as text and can never become markup or script.
 * (More in docs/ADR-006-typescript-organised.md.)
 */
import { isElement } from '../lib/markdown.js';
/**
 * Creates an element, sets its attributes and appends its children in one call.
 * @param tag the element name, such as "div" or "button"
 * @param attrs attributes to set: a function under an "on..." key becomes an event listener,
 *   "class" sets the class name, true adds the attribute with no value, and false, null
 *   and undefined leave the attribute out
 * @param children child nodes; strings become text nodes and empty values are skipped
 */
export function h(tag, attrs = {}, ...children) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(attrs)) {
        if (value === false || value === null || value === undefined) {
            continue;
        }
        if (typeof value === 'function') {
            if (key.startsWith('on')) {
                node.addEventListener(key.slice(2), value);
            }
        }
        else if (key === 'class') {
            node.className = String(value);
        }
        else if (value === true) {
            node.setAttribute(key, '');
        }
        else {
            node.setAttribute(key, value);
        }
    }
    append(node, children);
    return node;
}
function append(node, children) {
    for (const child of children) {
        if (child === null || child === undefined || child === false) {
            continue;
        }
        if (Array.isArray(child)) {
            append(node, child);
        }
        else {
            node.append(typeof child === 'string' ? document.createTextNode(child) : child);
        }
    }
}
/**
 * Converts the tree from the markdown parser into DOM nodes. Elements are made
 * with createElement and text with text nodes, so a document's text is always
 * shown as text. Attributes with an empty value are left out.
 */
export function toDom(nodes) {
    const fragment = document.createDocumentFragment();
    for (const node of nodes) {
        if (!isElement(node)) {
            fragment.append(document.createTextNode(node.text));
            continue;
        }
        const element = document.createElement(node.tag);
        for (const [key, value] of Object.entries(node.attrs)) {
            if (value) {
                element.setAttribute(key, value);
            }
        }
        element.append(toDom(node.children));
        fragment.append(element);
    }
    return fragment;
}
/**
 * Removes all of a container's children and appends new ones. Doing it in one
 * step means the browser lays out the result once, so a person never sees a
 * half-drawn table.
 */
export function replace(container, ...children) {
    container.replaceChildren();
    append(container, children);
}

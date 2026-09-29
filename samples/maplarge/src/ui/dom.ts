/**
 * The two ways this page makes HTML: h() for what the code writes and toDom()
 * for what the markdown reader read (ADR-006). Both set text with textContent,
 * never innerHTML, so nothing a file name or a document carries can become
 * markup.
 */

import { isElement, type MdNode } from '../lib/markdown.js';

/** What h() accepts as a child: a node, text, nothing, or a list of those. */
export type Child = Node | string | null | undefined | false | Child[];

/** An attribute value: text, a flag, or, for "on..." keys, a listener. */
export type Attr = string | boolean | null | undefined | EventListener;

/**
 * An element with attributes and children in one call.
 * @param attrs "onclick" style keys become listeners; true is a bare attribute; false, null and undefined are left out
 * @param children strings become text nodes
 */
export function h<K extends keyof HTMLElementTagNameMap>(tag: K, attrs: Record<string, Attr> = {}, ...children: Child[]): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (value === false || value === null || value === undefined) {
      continue;
    }
    if (typeof value === 'function') {
      if (key.startsWith('on')) {
        node.addEventListener(key.slice(2), value);
      }
    } else if (key === 'class') {
      node.className = String(value);
    } else if (value === true) {
      node.setAttribute(key, '');
    } else {
      node.setAttribute(key, value);
    }
  }
  append(node, children);
  return node;
}

function append(node: ParentNode, children: Child[]): void {
  for (const child of children) {
    if (child === null || child === undefined || child === false) {
      continue;
    }
    if (Array.isArray(child)) {
      append(node, child);
    } else {
      node.append(typeof child === 'string' ? document.createTextNode(child) : child);
    }
  }
}

/** The markdown reader's tree as DOM nodes, by createElement and text nodes, so a document's text stays text. */
export function toDom(nodes: MdNode[]): DocumentFragment {
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

/** Replaces a container's children in one step, so a redraw is one reflow and never a half-drawn table. */
export function replace(container: ParentNode, ...children: Child[]): void {
  container.replaceChildren();
  append(container, children);
}

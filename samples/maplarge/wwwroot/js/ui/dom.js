// The two ways this page makes HTML: h() for what the code writes and toDom()
// for what the markdown reader read (ADR-006). Both set text with textContent,
// never innerHTML, so nothing a file name or a document carries can become
// markup.

/**
 * An element with attributes and children in one call.
 * @param {string} tag
 * @param {Record<string, string|boolean|((event: Event) => void)>} [attrs] "onclick" style keys become listeners
 * @param {...(Node|string|null|undefined|Array)} children strings become text nodes
 */
export function h(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (value === false || value === null || value === undefined) {
      continue;
    }
    if (key.startsWith('on') && typeof value === 'function') {
      node.addEventListener(key.slice(2), value);
    } else if (key === 'class') {
      node.className = String(value);
    } else if (value === true) {
      node.setAttribute(key, '');
    } else {
      node.setAttribute(key, String(value));
    }
  }
  append(node, children);
  return node;
}

function append(node, children) {
  for (const child of children.flat()) {
    if (child === null || child === undefined || child === false) {
      continue;
    }
    node.append(typeof child === 'string' ? document.createTextNode(child) : child);
  }
}

/**
 * The markdown reader's tree as DOM nodes.
 * @param {import('../lib/markdown.js').Node[]} nodes
 * @returns {DocumentFragment}
 */
export function toDom(nodes) {
  const fragment = document.createDocumentFragment();
  for (const node of nodes) {
    if ('text' in node) {
      fragment.append(document.createTextNode(node.text));
      continue;
    }
    const element = document.createElement(node.tag);
    for (const [key, value] of Object.entries(node.attrs ?? {})) {
      if (value) {
        element.setAttribute(key, value);
      }
    }
    element.append(toDom(node.children));
    fragment.append(element);
  }
  return fragment;
}

/** Replaces a container's children with new ones in one step. */
export function replace(container, ...children) {
  container.replaceChildren();
  append(container, children);
}

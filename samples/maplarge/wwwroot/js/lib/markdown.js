/**
 * A small markdown parser for the documents the app serves in its Docs tab.
 * It turns markdown text into a tree of plain objects: {tag, attrs, children}
 * for an element and {text} for text. The code in ui/elements.ts then builds real
 * DOM nodes from that tree with createElement and text nodes.
 *
 * The parser never produces an HTML string and nothing is assigned to innerHTML,
 * so a document cannot inject script or markup into the page. Because the output
 * is plain objects, the parser can also be tested in node without a DOM.
 *
 * It supports only what the documents use: "#" headings, paragraphs, bullet and
 * numbered lists, fenced code blocks (with a language and an optional caption on
 * the opening fence line), block quotes, pipe tables, horizontal rules, and inline
 * code, bold, italic, links and images. Anything else is treated as a paragraph.
 * (More in docs/ADR-012-documents-served-by-the-app.md.)
 */
export function isElement(node) {
    return 'tag' in node;
}
function element(tag, children, attrs = {}) {
    return { tag, attrs, children };
}
// #region blocks
/**
 * Parses a whole document into its top-level blocks. It walks the lines once,
 * and at each line tries the block types in a fixed order (fence, heading, rule,
 * quote, table, list); a line that starts none of them begins a paragraph.
 */
export function parse(markdown) {
    const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
    const blocks = [];
    let i = 0;
    while (i < lines.length) {
        const line = lines[i] ?? '';
        if (line.trim() === '') {
            i += 1;
            continue;
        }
        // Code fences are checked first. Inside a fence, a line starting with "#" or "-" is code,
        // so the fence consumes those lines before the heading or list checks below can see them.
        const fence = /^```(\S*)\s*(.*)$/.exec(line);
        if (fence) {
            const body = [];
            i += 1;
            while (i < lines.length && !(lines[i] ?? '').startsWith('```')) {
                body.push(lines[i] ?? '');
                i += 1;
            }
            i += 1;
            blocks.push(codeBlock(fence[1] ?? '', (fence[2] ?? '').trim(), body.join('\n')));
            continue;
        }
        const heading = /^(#{1,6})\s+(.*)$/.exec(line);
        if (heading) {
            const text = (heading[2] ?? '').trim();
            blocks.push(element(`h${(heading[1] ?? '#').length}`, inline(text), { id: slug(text) }));
            i += 1;
            continue;
        }
        if (/^(-{3,}|\*{3,})\s*$/.test(line)) {
            blocks.push(element('hr', []));
            i += 1;
            continue;
        }
        if (line.startsWith('>')) {
            const quoted = [];
            while (i < lines.length && (lines[i] ?? '').startsWith('>')) {
                quoted.push((lines[i] ?? '').replace(/^>\s?/, ''));
                i += 1;
            }
            // The text inside a quote can hold any block (lists, code, headings), so the quoted lines
            // are parsed again with this same function instead of handling each case here.
            blocks.push(element('blockquote', parse(quoted.join('\n'))));
            continue;
        }
        if (isTableStart(lines, i)) {
            const rows = [];
            while (i < lines.length && (lines[i] ?? '').trim().startsWith('|')) {
                rows.push(lines[i] ?? '');
                i += 1;
            }
            blocks.push(table(rows));
            continue;
        }
        const item = listItem(line);
        if (item) {
            const items = [];
            const ordered = item.ordered;
            while (i < lines.length) {
                const current = lines[i] ?? '';
                const next = listItem(current);
                if (next && next.ordered === ordered) {
                    items.push(next.text);
                    i += 1;
                }
                else if (current.startsWith('  ') && items.length > 0 && current.trim() !== '') {
                    // An indented line continues the previous item, so join it onto that item's text.
                    items[items.length - 1] += ` ${current.trim()}`;
                    i += 1;
                }
                else {
                    break;
                }
            }
            blocks.push(element(ordered ? 'ol' : 'ul', items.map((text) => element('li', inline(text)))));
            continue;
        }
        const paragraph = [];
        while (i < lines.length) {
            const current = lines[i] ?? '';
            if (current.trim() === '' || /^(#{1,6}\s|```|>|\||-{3,}\s*$)/.test(current) || listItem(current)) {
                break;
            }
            paragraph.push(current.trim());
            i += 1;
        }
        const children = inline(paragraph.join(' '));
        // A paragraph that holds only one image gets the "figure" class, so the stylesheet can
        // frame it as a standalone picture instead of an inline one.
        const first = children[0];
        const onlyImage = children.length === 1 && first !== undefined && isElement(first) && first.tag === 'img';
        blocks.push(element('p', children, onlyImage ? { class: 'figure' } : {}));
    }
    return blocks;
}
function listItem(line) {
    const bullet = /^\s{0,3}[-*+]\s+(.*)$/.exec(line);
    if (bullet) {
        return { ordered: false, text: bullet[1] ?? '' };
    }
    const number = /^\s{0,3}\d+[.)]\s+(.*)$/.exec(line);
    if (number) {
        return { ordered: true, text: number[1] ?? '' };
    }
    return null;
}
function isTableStart(lines, i) {
    const next = lines[i + 1];
    return (lines[i] ?? '').trim().startsWith('|') && next !== undefined && /^\s*\|?\s*:?-{3,}/.test(next);
}
function table(rows) {
    const cells = (row) => row.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map((cell) => cell.trim());
    const head = cells(rows[0] ?? '');
    const body = rows.slice(2).map(cells);
    return element('table', [
        element('thead', [element('tr', head.map((cell) => element('th', inline(cell))))]),
        element('tbody', body.map((row) => element('tr', row.map((cell) => element('td', inline(cell)))))),
    ]);
}
function codeBlock(language, caption, code) {
    const children = [];
    if (caption) {
        children.push(element('span', [{ text: caption }], { class: 'caption' }));
    }
    children.push(element('code', highlight(code, language), { class: language ? `language-${language}` : '' }));
    return element('pre', children);
}
// #endregion blocks
// #region inline
/**
 * Parses the inline marks inside one block of text: code spans, images, links,
 * bold and italic. One regular expression finds the next mark; text between
 * marks becomes plain text nodes. Code spans come first in the pattern so that
 * nothing inside backticks is read as another mark.
 */
export function inline(text) {
    const out = [];
    const pattern = /(`+)([^`]|[^`][\s\S]*?[^`])\1(?!`)|!\[([^\]]*)\]\(([^)\s]+)(?:\s+"([^"]*)")?\)|\[([^\]]+)\]\(([^)\s]+)\)|\*\*([^*]+)\*\*|\*([^*]+)\*/g;
    let last = 0;
    let match;
    while ((match = pattern.exec(text)) !== null) {
        if (match.index > last) {
            out.push({ text: text.slice(last, match.index) });
        }
        if (match[2] !== undefined) {
            out.push(element('code', [{ text: match[2].trim() }]));
        }
        else if (match[4] !== undefined) {
            // An image. The alt text stays plain text, and the image address goes through the same
            // safety check as a link address.
            out.push(element('img', [], { src: safeHref(match[4]), alt: match[3] ?? '', title: match[5] ?? '', loading: 'lazy' }));
        }
        else if (match[6] !== undefined) {
            out.push(element('a', inline(match[6]), { href: safeHref(match[7] ?? ''), rel: 'noopener', target: '_blank' }));
        }
        else if (match[8] !== undefined) {
            out.push(element('strong', inline(match[8])));
        }
        else {
            out.push(element('em', inline(match[9] ?? '')));
        }
        last = match.index + match[0].length;
    }
    if (last < text.length) {
        out.push({ text: text.slice(last) });
    }
    return out;
}
/**
 * Allows only http, https, site-relative and in-page addresses. Anything else,
 * such as a "javascript:" address, is replaced with "#" so a document cannot run
 * script when a link is clicked.
 */
export function safeHref(href) {
    return /^(https?:\/\/|\/|\?|#)/i.test(href) ? href : '#';
}
// #endregion inline
// #region highlight
const KEYWORDS = {
    csharp: 'using namespace public private internal static sealed class record interface enum readonly async await return new var if else foreach for while switch case default throw try catch finally out ref in is not null true false void string int long bool object this base override virtual abstract partial get set init where'.split(' '),
    javascript: 'import export from const let var function return if else for of in while switch case default new this class extends async await try catch finally throw null undefined true false typeof instanceof'.split(' '),
    typescript: 'import export from const let var function return if else for of in while switch case default new this class extends async await try catch finally throw null undefined true false typeof instanceof type interface readonly as'.split(' '),
};
/**
 * Adds simple syntax colouring to a code block in C#, JavaScript or TypeScript.
 * Comments, strings and keywords are wrapped in spans with a class the
 * stylesheet colours; every other character passes through as plain text.
 * It uses one regular expression rather than a real parser, because it only
 * needs to help reading. Anything it does not recognise is left uncoloured.
 * Code in any other language is returned as a single text node.
 */
export function highlight(code, language) {
    const words = KEYWORDS[language];
    if (!words) {
        return [{ text: code }];
    }
    const out = [];
    const pattern = /(\/\/[^\n]*|\/\*[\s\S]*?\*\/)|("(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)*'|@"(?:[^"]|"")*")|\b([A-Za-z_]\w*)\b/g;
    let last = 0;
    let match;
    while ((match = pattern.exec(code)) !== null) {
        if (match.index > last) {
            out.push({ text: code.slice(last, match.index) });
        }
        if (match[1] !== undefined) {
            out.push(element('span', [{ text: match[1] }], { class: 'tok-comment' }));
        }
        else if (match[2] !== undefined) {
            out.push(element('span', [{ text: match[2] }], { class: 'tok-string' }));
        }
        else if (match[3] !== undefined && words.includes(match[3])) {
            out.push(element('span', [{ text: match[3] }], { class: 'tok-keyword' }));
        }
        else {
            out.push({ text: match[3] ?? '' });
        }
        last = match.index + match[0].length;
    }
    if (last < code.length) {
        out.push({ text: code.slice(last) });
    }
    return out;
}
// #endregion highlight
/**
 * Builds the id for a heading: lower case, with runs of other characters turned
 * into single hyphens. A link such as "?doc=x#the-decision" can then jump
 * straight to the heading "The decision".
 */
export function slug(text) {
    return text
        .toLowerCase()
        .replace(/[`*_]/g, '')
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-|-$/g, '');
}

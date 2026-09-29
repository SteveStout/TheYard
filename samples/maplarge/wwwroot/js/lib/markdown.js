// A small markdown reader for the records the app serves (ADR-012). It turns
// text into a tree of plain objects, {tag, attrs, children} and {text}, which
// ui/docs.js builds into DOM nodes with createElement and textContent. Nothing
// here is ever handed to innerHTML, so a document cannot carry script into the
// page, and the reader can be tested in node without a DOM.
//
// What it reads is what the records use: ATX headings, paragraphs, bullet and
// numbered lists, fenced code (with the language and an optional caption on
// the fence line), block quotes, pipe tables, horizontal rules, and inline
// code, bold, italic and links. Anything else is a paragraph.

/** @typedef {{ tag: string, attrs?: Record<string, string>, children: Node[] } | { text: string }} Node */

// #region blocks
/**
 * @param {string} markdown
 * @returns {Node[]} the top-level blocks
 */
export function parse(markdown) {
  const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
  const blocks = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (line.trim() === '') {
      i += 1;
      continue;
    }
    const fence = /^```(\S*)\s*(.*)$/.exec(line);
    if (fence) {
      const body = [];
      i += 1;
      while (i < lines.length && !lines[i].startsWith('```')) {
        body.push(lines[i]);
        i += 1;
      }
      i += 1;
      blocks.push(codeBlock(fence[1], fence[2].trim(), body.join('\n')));
      continue;
    }
    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    if (heading) {
      blocks.push(element(`h${heading[1].length}`, inline(heading[2].trim()), { id: slug(heading[2]) }));
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
      while (i < lines.length && lines[i].startsWith('>')) {
        quoted.push(lines[i].replace(/^>\s?/, ''));
        i += 1;
      }
      blocks.push(element('blockquote', parse(quoted.join('\n'))));
      continue;
    }
    if (isTableStart(lines, i)) {
      const rows = [];
      while (i < lines.length && lines[i].trim().startsWith('|')) {
        rows.push(lines[i]);
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
        const next = listItem(lines[i]);
        if (next && next.ordered === ordered) {
          items.push(next.text);
          i += 1;
        } else if (lines[i].startsWith('  ') && items.length > 0 && lines[i].trim() !== '') {
          // A continuation line, indented under the item it belongs to.
          items[items.length - 1] += ` ${lines[i].trim()}`;
          i += 1;
        } else {
          break;
        }
      }
      blocks.push(element(ordered ? 'ol' : 'ul', items.map((text) => element('li', inline(text)))));
      continue;
    }
    const paragraph = [];
    while (i < lines.length && lines[i].trim() !== '' && !/^(#{1,6}\s|```|>|\||-{3,}\s*$)/.test(lines[i]) && !listItem(lines[i])) {
      paragraph.push(lines[i].trim());
      i += 1;
    }
    blocks.push(element('p', inline(paragraph.join(' '))));
  }
  return blocks;
}

function listItem(line) {
  const bullet = /^\s{0,3}[-*+]\s+(.*)$/.exec(line);
  if (bullet) {
    return { ordered: false, text: bullet[1] };
  }
  const number = /^\s{0,3}\d+[.)]\s+(.*)$/.exec(line);
  if (number) {
    return { ordered: true, text: number[1] };
  }
  return null;
}

function isTableStart(lines, i) {
  return lines[i].trim().startsWith('|') && i + 1 < lines.length && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1]);
}

function table(rows) {
  const cells = (row) => row.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map((cell) => cell.trim());
  const head = cells(rows[0]);
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
 * Inline marks: code spans first (their contents are literal), then links,
 * bold and italic. Nothing is interpreted inside a code span.
 * @param {string} text
 * @returns {Node[]}
 */
export function inline(text) {
  const out = [];
  const pattern = /(`+)([^`]|[^`][\s\S]*?[^`])\1(?!`)|\[([^\]]+)\]\(([^)\s]+)\)|\*\*([^*]+)\*\*|\*([^*]+)\*/g;
  let last = 0;
  let match;
  while ((match = pattern.exec(text)) !== null) {
    if (match.index > last) {
      out.push({ text: text.slice(last, match.index) });
    }
    if (match[2] !== undefined) {
      out.push(element('code', [{ text: match[2].trim() }]));
    } else if (match[3] !== undefined) {
      out.push(element('a', inline(match[3]), { href: safeHref(match[4]), rel: 'noopener', target: '_blank' }));
    } else if (match[5] !== undefined) {
      out.push(element('strong', inline(match[5])));
    } else {
      out.push(element('em', inline(match[6])));
    }
    last = match.index + match[0].length;
  }
  if (last < text.length) {
    out.push({ text: text.slice(last) });
  }
  return out;
}

/** Only web and in-page addresses; anything else becomes a harmless "#". */
export function safeHref(href) {
  return /^(https?:\/\/|\/|\?|#)/i.test(href) ? href : '#';
}
// #endregion inline

// #region highlight
const KEYWORDS = {
  csharp: 'using namespace public private internal static sealed class record interface enum readonly async await return new var if else foreach for while switch case default throw try catch finally out ref in is not null true false void string int long bool object this base override virtual abstract partial get set init where'.split(' '),
  javascript: 'import export from const let var function return if else for of in while switch case default new this class extends async await try catch finally throw null undefined true false typeof instanceof'.split(' '),
};

/**
 * Comments, strings and keywords in C# and JavaScript, as spans; every other
 * character passes through as text. It is a reader's aid, not a parser, and a
 * construct it does not know is simply not coloured.
 * @param {string} code
 * @param {string} language
 * @returns {Node[]}
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
    } else if (match[2] !== undefined) {
      out.push(element('span', [{ text: match[2] }], { class: 'tok-string' }));
    } else if (words.includes(match[3])) {
      out.push(element('span', [{ text: match[3] }], { class: 'tok-keyword' }));
    } else {
      out.push({ text: match[3] });
    }
    last = match.index + match[0].length;
  }
  if (last < code.length) {
    out.push({ text: code.slice(last) });
  }
  return out;
}
// #endregion highlight

/** A heading's id: lower case, words joined by hyphens, so "?doc=x#the-decision" lands. */
export function slug(text) {
  return text
    .toLowerCase()
    .replace(/[`*_]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
}

function element(tag, children, attrs = {}) {
  return { tag, attrs, children };
}

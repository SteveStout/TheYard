// node --test tests/js: the markdown reader builds a tree, never markup (ADR-012).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { highlight, inline, parse, safeHref, slug } from '../../wwwroot/js/lib/markdown.js';

const text = (nodes) => nodes.map((n) => ('text' in n ? n.text : text(n.children))).join('');

test('headings, paragraphs and a rule', () => {
  const blocks = parse('# ADR: The title\n\nA paragraph\nover two lines.\n\n---\n\n## Context');
  assert.deepEqual(blocks.map((b) => b.tag), ['h1', 'p', 'hr', 'h2']);
  assert.equal(text(blocks[1].children), 'A paragraph over two lines.');
  assert.equal(blocks[0].attrs.id, 'adr-the-title');
});

test('bullet and numbered lists, with continuation lines', () => {
  const blocks = parse('- one\n- two\n  continued\n\n1. first\n2. second');
  assert.equal(blocks[0].tag, 'ul');
  assert.equal(blocks[0].children.length, 2);
  assert.equal(text(blocks[0].children[1].children), 'two continued');
  assert.equal(blocks[1].tag, 'ol');
  assert.equal(blocks[1].children.length, 2);
});

test('a fenced block keeps its text, its language and a caption from the fence line', () => {
  const [pre] = parse('```csharp Domain/HomePath.cs\nvar x = 1; // why\n```');
  assert.equal(pre.tag, 'pre');
  assert.equal(pre.children[0].attrs.class, 'caption');
  assert.equal(text(pre.children[0].children), 'Domain/HomePath.cs');
  assert.equal(pre.children[1].attrs.class, 'language-csharp');
  assert.equal(text(pre.children[1].children), 'var x = 1; // why');
});

test('a pipe table becomes thead and tbody', () => {
  const [table] = parse('| The rule | Held by |\n| --- | --- |\n| No em dash | HouseVoiceTests |\n| Sealed | SealedByDefaultTests |');
  assert.equal(table.tag, 'table');
  assert.equal(table.children[0].children[0].children.length, 2);
  assert.equal(table.children[1].children.length, 2);
  assert.equal(text(table.children[1].children[1].children[1].children), 'SealedByDefaultTests');
});

test('a block quote holds blocks of its own', () => {
  const [quote] = parse('> Sample unavailable: `x` is not in this build.');
  assert.equal(quote.tag, 'blockquote');
  assert.equal(quote.children[0].tag, 'p');
});

test('inline code, bold, italic and links', () => {
  const nodes = inline('Use `HomePath.Resolve` for **every** path, *never* `..`, see [ADR-003](?view=docs&doc=adr-003).');
  const tags = nodes.filter((n) => !('text' in n)).map((n) => n.tag);
  assert.deepEqual(tags, ['code', 'strong', 'em', 'code', 'a']);
  assert.equal(nodes.at(-2).attrs.href, '?view=docs&doc=adr-003');
  assert.equal(nodes.at(-2).attrs.target, '_blank');
});

test('a script tag in the source is text, and a javascript link is not a link', () => {
  const blocks = parse('<script>alert(1)</script> and [x](javascript:alert(1))');
  assert.equal(blocks[0].tag, 'p');
  assert.equal(blocks[0].children[0].text, '<script>alert(1)</script> and ');
  assert.equal(blocks[0].children[1].attrs.href, '#');
  assert.equal(safeHref('https://example.com/a'), 'https://example.com/a');
  assert.equal(safeHref('data:text/html,hi'), '#');
});

test('highlight marks comments, strings and keywords and passes the rest through', () => {
  const nodes = highlight('public sealed class A { string s = "x"; } // done', 'csharp');
  const classes = nodes.filter((n) => !('text' in n)).map((n) => n.attrs.class);
  assert.deepEqual(classes, ['tok-keyword', 'tok-keyword', 'tok-keyword', 'tok-keyword', 'tok-string', 'tok-comment']);
  assert.equal(text(nodes), 'public sealed class A { string s = "x"; } // done');
  assert.deepEqual(highlight('anything', 'text'), [{ text: 'anything' }]);
});

test('slug is lower case words joined by hyphens', () => {
  assert.equal(slug('The line a path cannot cross'), 'the-line-a-path-cannot-cross');
  assert.equal(slug('`HomePath`, and why'), 'homepath-and-why');
});

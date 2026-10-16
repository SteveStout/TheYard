// Tests how the page state is read from and written to the address bar query string (run with:
// npm test). No browser is needed, because the parser works on plain strings. The
// page keeps its state in the address so a link or a reload reopens the same view, which means
// unknown keys and bad values must fall back to defaults and a parse then write must round-trip.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DEFAULTS, cleanPath, crumbs, isOpen, parentOf, parse, serialize } from '../../wwwroot/js/lib/urlState.js';

test('a bare address is closed and at the defaults', () => {
  const state = parse('');
  assert.equal(state.open, false);
  assert.deepEqual({ ...state, open: undefined }, { ...DEFAULTS, open: undefined });
  assert.equal(isOpen(state), false);
  assert.equal(serialize(state), '');
});

test('parse reads every key and drops what it does not know', () => {
  const state = parse('?path=docs%2Fnotes&q=*.md&sort=size&dir=desc&view=browse&doc=&colour=red');
  assert.deepEqual(state, { open: true, view: 'browse', path: 'docs/notes', q: '*.md', sort: 'size', dir: 'desc', doc: '' });
});

test('a value outside its list falls back to the default', () => {
  const state = parse('?sort=colour&dir=sideways&view=admin');
  assert.equal(state.sort, 'name');
  assert.equal(state.dir, 'asc');
  assert.equal(state.view, 'browse');
  assert.equal(state.open, true);
});

test('serialize writes only what differs and keeps view when nothing else would be written', () => {
  assert.equal(serialize({ open: true }), '?view=browse');
  assert.equal(serialize({ open: true, view: 'docs' }), '?view=docs');
  assert.equal(serialize({ path: 'a/b', q: 'x' }), '?path=a%2Fb&q=x');
  assert.equal(serialize({ open: false, path: 'a/b' }), '');
});

test('parse and serialize round-trip', () => {
  for (const search of ['?view=browse', '?path=reports%2F2026', '?q=*.csv&sort=size&dir=desc', '?view=docs&doc=adr-001-the-starter-kept-as-given']) {
    assert.equal(serialize(parse(search)), search);
  }
});

test('a path is normalised to one spelling', () => {
  assert.equal(cleanPath('/docs//notes/'), 'docs/notes');
  assert.equal(cleanPath('docs\\notes'), 'docs/notes');
  assert.equal(cleanPath(null), '');
  assert.equal(parse('?path=%2Fdocs%2F').path, 'docs');
});

test('crumbs walk from home to the path', () => {
  assert.deepEqual(crumbs(''), [{ name: 'Home', path: '' }]);
  assert.deepEqual(crumbs('a/b'), [{ name: 'Home', path: '' }, { name: 'a', path: 'a' }, { name: 'b', path: 'a/b' }]);
});

test('parentOf is null at home, empty at the top, and the folder above otherwise', () => {
  assert.equal(parentOf(''), null);
  assert.equal(parentOf('a'), '');
  assert.equal(parentOf('a/b/c'), 'a/b');
});

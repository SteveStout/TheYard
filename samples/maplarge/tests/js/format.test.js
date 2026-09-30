// Tests the display formatters (run with: node --test tests/js). They check that byte counts
// step by 1024 with one decimal, that times read as a short local stamp or "today", and that
// nouns take the right plural, because these strings are what a person reads in the file list.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { bytes, plural, when } from '../../wwwroot/js/lib/format.js';

test('bytes step by 1024 with one decimal above bytes', () => {
  assert.equal(bytes(0), '0 B');
  assert.equal(bytes(1023), '1,023 B');
  assert.equal(bytes(1024), '1.0 KB');
  assert.equal(bytes(1536), '1.5 KB');
  assert.equal(bytes(5 * 1024 * 1024), '5.0 MB');
  assert.equal(bytes(3.2 * 1024 * 1024 * 1024), '3.2 GB');
  assert.equal(bytes(-1), '');
  assert.equal(bytes(NaN), '');
});

test('when is a short local stamp, and today says so', () => {
  const now = new Date(2026, 8, 29, 13, 5);
  const today = new Date(2026, 8, 29, 9, 7).getTime();
  const earlier = new Date(2026, 8, 1, 23, 59).getTime();
  assert.equal(when(today, now), 'today 09:07');
  assert.equal(when(earlier, now), '2026-09-01 23:59');
  assert.equal(when(0, now), '');
});

test('plural picks the noun', () => {
  assert.equal(plural(1, 'folder'), '1 folder');
  assert.equal(plural(12, 'file'), '12 files');
  assert.equal(plural(0, 'match', 'matches'), '0 matches');
});

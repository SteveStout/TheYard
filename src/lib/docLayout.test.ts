import { describe, expect, it } from 'vitest';
import { layoutDocument, statusReading } from './docLayout';

describe('every document on the Author page panels', () => {
  it('opens with the title and gives each second-level heading a panel of its own', () => {
    const html = layoutDocument(
      '<h1>ADR: A record</h1><p>Status: accepted.</p><h2>Context</h2><p>Why.</p><h2>Decision</h2><p>What.</p>'
    );
    expect(html.match(/class="doc-panel/g)).toHaveLength(3);
    expect(html).toContain('<section class="doc-panel op-glass doc-lead"><h1>ADR: A record</h1>');
    expect(html.indexOf('Context')).toBeLessThan(html.indexOf('Decision'));
    // Nothing is dropped on the way through: every word of the document is still in it.
    for (const word of ['A record', 'Status: accepted.', 'Why.', 'What.']) {
      expect(html).toContain(word);
    }
  });

  it('is one panel for a document with no headings, and for one that opens on a heading', () => {
    expect(layoutDocument('<p>One paragraph.</p>').match(/class="doc-panel/g)).toHaveLength(1);
    const headingFirst = layoutDocument('<h2>Straight in</h2><p>Words.</p>');
    expect(headingFirst.match(/class="doc-panel/g)).toHaveLength(1);
    expect(headingFirst).not.toContain('doc-lead');
  });

  it('leaves a heading inside a code sample alone, because it is escaped markup', () => {
    const html = layoutDocument(
      '<h1>Code</h1><pre><code>&lt;h2&gt;not a heading&lt;/h2&gt;</code></pre>'
    );
    expect(html.match(/class="doc-panel/g)).toHaveLength(1);
    expect(html).toContain('&lt;h2&gt;not a heading&lt;/h2&gt;');
  });
});

describe("a record's status line, as a reading (the operator's look)", () => {
  it('draws status, date and the version it shipped as, and keeps the rest of the paragraph', () => {
    const lead =
      '<h1>ADR: The palette</h1><p>Status: accepted, 2026-09-02, shipped as 1.0.0.21. Steve chose it.</p>';
    const read = statusReading(lead);
    expect(read).toContain(
      '<dl class="doc-status"><div><dt>Status</dt><dd>accepted</dd></div><div><dt>Date</dt><dd>2026-09-02</dd></div><div><dt>Shipped as</dt><dd>1.0.0.21</dd></div></dl>'
    );
    expect(read).toContain('<p>Steve chose it.</p>');
    expect(read).not.toContain('Status: accepted');
  });

  it('leaves a status line in any other shape exactly as it was written', () => {
    for (const lead of [
      '<h1>A</h1><p>Status: accepted.</p>',
      '<h1>A</h1><p>Status: written for a developer who has used React.</p>',
      '<h1>A</h1><p>Date: 2026-09-01</p>',
    ]) {
      expect(statusReading(lead)).toBe(lead);
    }
    expect(statusReading('<h1>A</h1><p>Status: proposed, 2026-09-08.</p>')).toBe(
      '<h1>A</h1><dl class="doc-status"><div><dt>Status</dt><dd>proposed</dd></div><div><dt>Date</dt><dd>2026-09-08</dd></div></dl>'
    );
  });

  it('puts every panel on the shared glass', () => {
    const html = layoutDocument('<h1>T</h1><h2>A</h2><p>a</p><h2>B</h2><p>b</p>');
    expect(html.match(/class="doc-panel op-glass/g)).toHaveLength(3);
  });
});

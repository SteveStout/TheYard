import { describe, expect, it } from 'vitest';
import { layoutDocument } from './docLayout';

describe('every document on the Author page panels', () => {
  it('opens with the title and gives each second-level heading a panel of its own', () => {
    const html = layoutDocument(
      '<h1>ADR: A record</h1><p>Status: accepted.</p><h2>Context</h2><p>Why.</p><h2>Decision</h2><p>What.</p>'
    );
    expect(html.match(/class="doc-panel/g)).toHaveLength(3);
    expect(html).toContain('<section class="doc-panel doc-lead"><h1>ADR: A record</h1>');
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

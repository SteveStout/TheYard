import { describe, expect, it } from 'vitest';
import { layoutDocument, sayTitleOnce, statusReading } from './docLayout';

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

describe('the wide sheet numbers its cards (saleshome1120)', () => {
  it('hooks each titled card and puts its number badge, 01 on, inside its heading; the lede card has none', () => {
    const html = layoutDocument(
      '<h1>Quality you can watch</h1><p>Lede.</p><h2 id="the-slip">The slip</h2><p>A.</p><h2 id="files">Files</h2><p>B.</p>',
      'Quality you can watch',
      true
    );
    expect(html.match(/data-mock="q-card"/g)).toHaveLength(2);
    expect(html).toContain(
      '<h2 id="the-slip"><span class="op-num" data-mock="section-num">01</span>The slip</h2>'
    );
    expect(html).toContain(
      '<h2 id="files"><span class="op-num" data-mock="section-num">02</span>Files</h2>'
    );
    expect(html).toMatch(/<section class="doc-panel op-glass doc-lead">[^]*Lede\.<\/p><\/section>/);
  });

  it('leaves every other document’s cards as they were', () => {
    const html = layoutDocument('<h1>A record</h1><h2>Context</h2><p>Why.</p>', 'A record');
    expect(html).not.toContain('q-card');
    expect(html).not.toContain('op-num');
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

describe('one title (the quality1120 picture)', () => {
  it('keeps a first heading that repeats the bar for a screen reader, and draws no panel for it alone', () => {
    const html = layoutDocument(
      '<h1>Quality you can watch</h1>\n<h2 id="a">A</h2><p>a</p>',
      'Quality you can watch'
    );
    expect(html).toContain('<h1 class="doc-title-in-bar">Quality you can watch</h1><h2 id="a">');
    expect(html.match(/<section/g)).toHaveLength(1);
    const lede = layoutDocument(
      '<h1>Quality you can watch</h1>\n<p>Lede.</p><h2>A</h2>',
      'Quality you can watch'
    );
    expect(lede).toContain(
      'doc-lead"><h1 class="doc-title-in-bar">Quality you can watch</h1><p>Lede.</p>'
    );
  });

  it('draws a first heading with words of its own as it was', () => {
    const html = layoutDocument('<h1>TheYard</h1>\n<p>Lede.</p>', 'README');
    expect(html).toContain('<h1>TheYard</h1>');
    expect(html).not.toContain('doc-title-in-bar');
    expect(sayTitleOnce('<p>No title</p>', 'README')).toEqual({
      hidden: '',
      rest: '<p>No title</p>',
    });
  });
});

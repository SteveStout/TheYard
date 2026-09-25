import { describe, expect, it } from 'vitest';
import { renderDocument } from './markdown';

describe('renderDocument', () => {
  it('colours a fenced block through the highlighter and names its grammar', async () => {
    const html = await renderDocument('```csharp\npublic sealed class Yard { }\n```\n');

    expect(html).toContain('<code class="hljs language-csharp">');
    expect(html).toContain('hljs-keyword');
  });

  it('escapes a fence in a language nobody here reads, with no grammar class', async () => {
    const html = await renderDocument('```bicep\n<script>alert(1)</script>\n```\n');

    expect(html).toContain('<code class="hljs">');
    expect(html).toContain('&lt;script&gt;');
    expect(html).not.toContain('<script>');
  });

  it("marks a document's pictures lazy, and keeps their words", async () => {
    const html = await renderDocument(
      '![The app on a laptop](/api/docs/images/app-home.jpg "The Yard")\n'
    );

    expect(html).toContain(
      '<img src="/api/docs/images/app-home.jpg" alt="The app on a laptop" title="The Yard" loading="lazy" decoding="async">'
    );
  });

  it("holds a picture's room from the size its address carries, and asks for the picture without it", async () => {
    const html = await renderDocument('![The app](/api/docs/images/app-home.jpg#1280x800)\n');

    expect(html).toContain(
      '<img src="/api/docs/images/app-home.jpg" alt="The app" width="1280" height="800" loading="lazy" decoding="async">'
    );
  });

  it('makes the live domain relative and opens every other link in a new tab', async () => {
    const html = await renderDocument(
      '[here](https://theyard.stevenstout.biz/diagrams/x) and [there](https://github.com/x) and [top](#top)'
    );

    expect(html).toContain('<a target="_blank" rel="noopener" href="/diagrams/x">');
    expect(html).toContain('<a target="_blank" rel="noopener" href="https://github.com/x">');
    expect(html).toContain('<a href="#top">');
  });

  it('puts every table in a scroller of its own, reachable from the keyboard (the tweaks pass, A1)', async () => {
    const html = await renderDocument(
      '| Resource | Cost |\n| --- | ---: |\n| Azure Cosmos DB | $0.00 |\n'
    );
    expect(html).toContain(
      '<div class="table-scroll" role="group" aria-label="Table, scrolls sideways" tabindex="0"><table>'
    );
    expect(html).toContain('</table></div>');
    expect(html).toContain('<td align="right">$0.00</td>');
  });
});

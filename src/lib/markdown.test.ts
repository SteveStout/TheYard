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

  it('makes the live domain relative and opens every other link in a new tab', async () => {
    const html = await renderDocument(
      '[here](https://theyard.stevenstout.biz/diagrams/x) and [there](https://github.com/x) and [top](#top)'
    );

    expect(html).toContain('<a target="_blank" rel="noopener" href="/diagrams/x">');
    expect(html).toContain('<a target="_blank" rel="noopener" href="https://github.com/x">');
    expect(html).toContain('<a href="#top">');
  });
});

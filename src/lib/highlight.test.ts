import { describe, expect, it } from 'vitest';
import { escapeHtml, grammarFor, highlight } from './highlight';

describe('grammarFor', () => {
  it('reads the names the documents write and the expander emits', () => {
    expect(grammarFor('csharp')).toBe('csharp');
    expect(grammarFor('cs')).toBe('csharp');
    expect(grammarFor('ts')).toBe('typescript');
    expect(grammarFor('tsx')).toBe('typescript');
    expect(grammarFor('yml')).toBe('yaml');
    expect(grammarFor('toml')).toBe('ini');
  });

  it('answers null for a language nobody here reads, and for no language at all', () => {
    // Bicep and Mermaid are both on the site and neither has a grammar in this
    // bundle; the block renders as text rather than as a broken one.
    expect(grammarFor('bicep')).toBeNull();
    expect(grammarFor('mermaid')).toBeNull();
    expect(grammarFor(undefined)).toBeNull();
    expect(grammarFor('')).toBeNull();
  });
});

describe('highlight', () => {
  it('colors a C# class the way a reader expects: keyword, type, string', () => {
    const html = highlight('public sealed class Yard { const string Name = "yard"; }', 'csharp');

    expect(html).toContain('hljs-keyword');
    expect(html).toContain('hljs-string');
    expect(html).toContain('sealed');
  });

  it('reads a region that starts mid-file rather than giving up on it', () => {
    // Every live sample is a region, so most of what this renders is a
    // fragment: a method with no class around it, or a closing brace.
    const html = highlight('    public int For(int standing) => 250;\n}', 'csharp');

    expect(html).toContain('hljs-keyword');
    expect(html).toContain('For');
  });

  it('escapes a block in a language it does not read, and adds no markup', () => {
    const html = highlight('<script>alert(1)</script>', 'bicep');

    expect(html).toBe('&lt;script&gt;alert(1)&lt;/script&gt;');
    expect(html).not.toContain('<span');
  });

  it('never lets a tag in a sample reach the page as a tag', () => {
    expect(highlight('var x = "<img src=x onerror=alert(1)>";', 'csharp')).not.toContain('<img');
    expect(escapeHtml('a & b < c > d "e" \'f\'')).toBe(
      'a &amp; b &lt; c &gt; d &quot;e&quot; &#39;f&#39;'
    );
  });
});

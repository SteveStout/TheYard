import { describe, expect, it } from 'vitest';
import { bodyBlocks, inlineParts } from './blogBody';

describe('a post read as a blog', () => {
  it('keeps plain text whole, as paragraphs with their line breaks, and colours the hashtags', () => {
    const blocks = bodyBlocks(
      'SOLID is five habits.\nIn The Shed: three classes.\n\n#dotnet #csharp and C# stays a word'
    );
    expect(blocks.map((b) => b.kind)).toEqual(['paragraph', 'paragraph']);
    const [first, second] = blocks;
    if (first.kind !== 'paragraph' || second.kind !== 'paragraph') throw new Error('paragraphs');
    expect(first.lines.map((line) => line.map((p) => p.text).join(''))).toEqual([
      'SOLID is five habits.',
      'In The Shed: three classes.',
    ]);
    expect(second.lines[0].map((p) => p.text).join('')).toBe('#dotnet #csharp and C# stays a word');
    expect(second.lines[0].filter((p) => p.kind === 'tag').map((p) => p.text)).toEqual([
      '#dotnet',
      '#csharp',
    ]);
  });

  it('reads a heading, a list and a paragraph apart, and a blank line ends each', () => {
    const blocks = bodyBlocks(
      '## 1. Write the spec first\n\nOne scenario.\n- Say what, not how\n- Make it fail\n\nThen.'
    );
    expect(blocks.map((b) => b.kind)).toEqual(['heading', 'paragraph', 'list', 'paragraph']);
    const list = blocks[2];
    if (list.kind !== 'list') throw new Error('list');
    expect(list.items.map((item) => item.map((p) => p.text).join(''))).toEqual([
      'Say what, not how',
      'Make it fail',
    ]);
  });

  it('reads bold, code and a link, and lets only an http or https link through', () => {
    const parts = inlineParts(
      'the test `Assert.Equal` in [ParityTests.cs](https://github.com/x/y) is **the contract**'
    );
    expect(parts.map((p) => p.kind)).toEqual(['text', 'code', 'text', 'link', 'text', 'bold']);
    const link = parts[3];
    if (link.kind !== 'link') throw new Error('link');
    expect(link).toEqual({ kind: 'link', text: 'ParityTests.cs', href: 'https://github.com/x/y' });
    expect(parts.map((p) => p.text).join('')).toBe(
      'the test Assert.Equal in ParityTests.cs is the contract'
    );
    expect(inlineParts('[nope](javascript:alert(1))').map((p) => p.kind)).toEqual(['text']);
  });
});

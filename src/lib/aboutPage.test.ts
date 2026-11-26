import { describe, expect, it } from 'vitest';
import { heroOf, titleOf } from './aboutPage';

const intro =
  '<h2 id="hi">Hi</h2><p>Who he is.</p>' +
  '<figure class="author-photo" data-photo="vineyard"><img src="/a.jpg" alt="A"></figure>' +
  '<p class="author-buttons"><a class="author-button" target="_blank" rel="noopener" href="/api/docs/resume">Read my resume</a>' +
  '<a class="author-button" target="_blank" rel="noopener" href="https://www.linkedin.com/in/stevenwstout">LinkedIn</a>' +
  '<a class="author-button" target="_blank" rel="noopener" href="https://github.com/SteveStout/TheYard">GitHub</a></p>';

describe("the About page's hero, cut from the author document", () => {
  const hero = heroOf(intro);

  it('keeps the words without the row of buttons or the photograph', () => {
    expect(hero.words).toBe('<h2 id="hi">Hi</h2><p>Who he is.</p>');
  });

  it("keeps the document's profile links in order and drops its resume link, which the gold button carries", () => {
    expect(hero.links).toEqual([
      { href: 'https://www.linkedin.com/in/stevenwstout', label: 'LinkedIn' },
      { href: 'https://github.com/SteveStout/TheYard', label: 'GitHub' },
    ]);
  });

  it('keeps the first photograph whole', () => {
    expect(hero.photo).toBe(
      '<figure class="author-photo" data-photo="vineyard"><img src="/a.jpg" alt="A"></figure>'
    );
    expect(heroOf('<h2>Hi</h2>').photo).toBeNull();
  });

  it('hooks the last of three paragraphs as the person beside the work, word for word, and no paragraph of fewer', () => {
    const personal =
      'Outside work, I build in my garage woodshop and read a lot of Roman history. Katie and I have two rabbits and too many house plants.';
    const words = heroOf(
      `<h2>Hi</h2><p><strong>Lead.</strong></p><p>The work.</p><p>${personal}</p>` +
        '<p class="author-buttons"><a class="author-button" href="/x">LinkedIn</a></p>'
    ).words;
    expect(words).toBe(
      `<h2>Hi</h2><p><strong>Lead.</strong></p><p>The work.</p><p data-mock="intro-personal">${personal}</p>`
    );
    expect(hero.words).not.toContain('intro-personal');
  });

  it("names the page by the document's own title", () => {
    expect(titleOf('<h1>About Steven</h1>\n')).toBe('About Steven');
    expect(titleOf('')).toBe('About Steven');
  });
});

import { describe, expect, it } from 'vitest';
import { layoutAuthor } from './author';
import { AUTHOR_PHOTOS, photoFigure, photoNamed } from './authorPhotos';

const photo = AUTHOR_PHOTOS[0];
const address = `https://raw.githubusercontent.com/SteveStout/TheYard/main/api/TheYard.Api/wwwroot/images/author/${photo.name}-960.jpg`;

const page = [
  '<h1>About Steven</h1>',
  '<h2>Hi</h2><p>Who he is.</p>',
  '<ul>\n<li><a target="_blank" rel="noopener" href="/api/docs/resume">Read my resume</a></li>\n<li><a target="_blank" rel="noopener" href="https://www.linkedin.com/in/stevenwstout">LinkedIn</a></li>\n</ul>',
  '<h2>Away from the keyboard</h2>',
  '<h3>The lake</h3><p>No boat.</p>',
  '<h3>History</h3><p>Rome.</p>',
  `<h3>Rabbits</h3><p><img src="${address}" alt="Two white rabbits asleep on a grey rug" title="They are fine."></p>`,
  '<hr>',
  '<p>A closing line.</p>',
].join('\n');

describe('the Author page in its shape', () => {
  const html = layoutAuthor(page);

  it('opens a panel at every second-level heading, and the first is who he is', () => {
    expect(html.match(/class="author-panel/g)).toHaveLength(3);
    expect(html).toContain('<section class="author-panel author-intro"><h2>Hi</h2>');
    expect(html).toContain('<section class="author-panel author-close">');
  });

  it('makes a block of every third-level heading, with no colour of its own to get wrong', () => {
    expect(html.match(/<section class="author-block[ "]/g)).toHaveLength(3);
    expect(html).not.toMatch(/author-block-(gold|teal)/);
  });

  it('gives a block with photographs the whole row, and the odd plain one out too', () => {
    expect(html.match(/author-block-wide/g)).toHaveLength(1);
    expect(html).toMatch(/<section class="author-block author-block-wide"><h3>Rabbits<\/h3>/);
    const odd = layoutAuthor('<h2>Hi</h2><h2>Away</h2><h3>One</h3><h3>Two</h3><h3>Three</h3>');
    expect(odd.match(/author-block-wide/g)).toHaveLength(1);
    expect(odd).toContain('<section class="author-block author-block-wide"><h3>Three</h3>');
  });

  it('sets two photographs in a row side by side, and the first panel’s beside its words', () => {
    const image = `<p><img src="${address}" alt="Rabbits"></p>`;
    const paired = layoutAuthor(`<h2>Hi</h2><h2>Away</h2><h3>Rabbits</h3>${image}\n${image}`);
    expect(paired.match(/<div class="author-pair">/g)).toHaveLength(1);
    const hero = layoutAuthor(`<h2>Hi</h2><p>Words.</p>${image}<h2>Away</h2>`);
    expect(hero).toMatch(
      /<div class="author-hero"><div class="author-hero-words"><h2>Hi<\/h2><p>Words\.<\/p><\/div><figure/
    );
  });

  it('draws a list of links as buttons, and leaves the links what they were', () => {
    expect(html).toContain('<p class="author-buttons">');
    expect(html).toContain(
      '<a class="author-button" target="_blank" rel="noopener" href="/api/docs/resume">'
    );
    expect(html).not.toContain('<ul>');
  });

  it('serves a photograph the document names as its responsive figure, in the frame, with its caption', () => {
    expect(html).toContain(`data-photo="${photo.name}"`);
    expect(html).toContain('class="author-frame"');
    expect(html).toContain('alt="Two white rabbits asleep on a grey rug"');
    expect(html).toContain('<figcaption>They are fine.</figcaption>');
    expect(html).not.toContain('raw.githubusercontent.com');
  });

  it('sets what follows a second rule under the panels, as the small print', () => {
    const credited = layoutAuthor(`${page}\n<hr>\n<p>Photos by somebody.</p>`);
    expect(credited.match(/class="author-panel/g)).toHaveLength(3);
    expect(credited).toContain(
      '</section><footer class="author-credit">\n<p>Photos by somebody.</p></footer></div>'
    );
    expect(html).not.toContain('author-credit');
  });

  it('leaves out a picture that is not one of the page’s photographs', () => {
    const smuggled = layoutAuthor(
      '<h2>Hi</h2><p><img src="https://example.com/somebody.jpg" alt="Somebody"></p>'
    );
    expect(smuggled).not.toContain('<img');
    expect(smuggled).toContain('left out');
  });
});

describe('a photograph as markup', () => {
  it('offers WebP before JPEG at every width, and states each file’s real width', () => {
    const figure = photoFigure(photo, 'Alt', null);
    const webp = figure.indexOf('type="image/webp"');
    expect(webp).toBeGreaterThan(-1);
    expect(webp).toBeLessThan(figure.indexOf('<img'));
    for (const width of photo.widths) {
      expect(figure).toContain(`/images/author/${photo.name}-${width}.webp ${width}w`);
      expect(figure).toContain(`/images/author/${photo.name}-${width}.jpg ${width}w`);
    }
  });

  it('offers a phone its own tighter cut first, when the photograph has one', () => {
    const wide = { ...photo, phone: { name: 'tight-cut', widths: [480, 960] } };
    const figure = photoFigure(wide, 'Alt', null);
    const tight = figure.indexOf('media="(max-width: 720px)" type="image/webp"');
    expect(tight).toBeGreaterThan(-1);
    expect(tight).toBeLessThan(figure.indexOf(`${photo.name}-480.webp`));
    expect(figure).toContain('/api/images/author/tight-cut-960.jpg 960w');
    const plain = photoFigure({ ...photo, phone: undefined }, 'Alt', null);
    expect(plain).toContain(
      `media="(max-width: 720px)" type="image/webp" srcset="/api/images/author/${photo.name}-480.webp 480w`
    );
  });

  it('never offers a phone a file wider than 960, and reserves the phone cut its own box', () => {
    for (const each of AUTHOR_PHOTOS) {
      const figure = photoFigure(each, 'Alt', null);
      const phoneSources = figure.match(/<source media="[^"]*"[^>]*>/g) ?? [];
      expect(phoneSources).toHaveLength(2);
      for (const source of phoneSources) {
        expect(source).not.toMatch(/-(1[0-9]{3}|[2-9][0-9]{3})\.(webp|jpg) /);
        expect(source).toMatch(/ width="960" height="\d+"/);
      }
    }
  });

  it('reserves the box, waits below the fold, and has no caption it was not given', () => {
    const figure = photoFigure(photo, 'Alt', null);
    expect(figure).toMatch(/width="\d+" height="\d+"/);
    expect(figure).toContain('loading="lazy"');
    expect(figure).not.toContain('<figcaption>');
    expect(photoFigure(photo, 'Alt', null, true)).toContain('loading="eager"');
  });

  it('never lets an alt text or a caption write markup', () => {
    const figure = photoFigure(photo, '"><script>', '<b>bold</b>');
    expect(figure).not.toContain('<script>');
    expect(figure).toContain('&lt;b&gt;bold&lt;/b&gt;');
  });

  it('knows its own photographs by the address a document gives, and nobody else’s', () => {
    expect(photoNamed(address)?.name).toBe(photo.name);
    expect(photoNamed('https://example.com/images/author/stranger-960.jpg')).toBeNull();
    expect(photoNamed('/images/coupe-01.jpg')).toBeNull();
  });
});
